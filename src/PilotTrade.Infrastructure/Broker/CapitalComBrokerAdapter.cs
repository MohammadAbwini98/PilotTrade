namespace PilotTrade.Infrastructure.Broker;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PilotTrade.Domain;
using PilotTrade.Domain.Broker;
using PilotTrade.Domain.Security;

// NOTE: Endpoint paths/fields must be verified against official Capital.com docs before production use.
// See docs/API_VERIFICATION.md. This adapter is intentionally conservative and fail-closed.
public sealed class CapitalComBrokerAdapter : IBrokerAdapter
{
    private readonly BrokerEnvironmentConfig _config;
    private readonly ISecretStore _secrets;
    private readonly IHttpClientFactory? _httpFactory;
    private readonly HttpClient? _httpClient;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private BrokerSession? _session;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private readonly SemaphoreSlim _rateLimiter = new(10, 10);
    private DateTimeOffset _lastRequestUtc = DateTimeOffset.MinValue;

    public CapitalComBrokerAdapter(BrokerEnvironmentConfig config, ISecretStore secrets, IHttpClientFactory httpFactory, IClock clock, IIdGenerator ids)
    {
        _config = config; _secrets = secrets; _httpFactory = httpFactory; _clock = clock; _ids = ids;
    }

    public CapitalComBrokerAdapter(BrokerEnvironmentConfig config, ISecretStore secrets, HttpClient httpClient, IClock clock, IIdGenerator ids)
    {
        _config = config; _secrets = secrets; _httpClient = httpClient; _clock = clock; _ids = ids;
    }

    private HttpClient CreateHttp()
    {
        if (_httpClient is not null)
        {
            if (_httpClient.BaseAddress is null) _httpClient.BaseAddress = new Uri(_config.ApiBaseUri);
            return _httpClient;
        }
        var http = _httpFactory!.CreateClient("capital");
        http.BaseAddress = new Uri(_config.ApiBaseUri);
        return http;
    }

    public TradingEnvironment Environment => _config.Environment;

    private async Task ThrottleAsync(CancellationToken ct)
    {
        await _rateLimiter.WaitAsync(ct);
        try
        {
            var now = _clock.UtcNow;
            var elapsed = now - _lastRequestUtc;
            if (elapsed < TimeSpan.FromMilliseconds(100))
                await Task.Delay(TimeSpan.FromMilliseconds(100) - elapsed, ct);
            _lastRequestUtc = _clock.UtcNow;
        }
        finally { _rateLimiter.Release(); }
    }

    private async Task<BrokerSession> EnsureSessionAsync(CancellationToken ct)
    {
        if (_session is not null && _session.ExpiresUtc > _clock.UtcNow.AddMinutes(1)) return _session;
        await _sessionLock.WaitAsync(ct);
        try
        {
            if (_session is not null && _session.ExpiresUtc > _clock.UtcNow.AddMinutes(1)) return _session;
            var apiKey = await _secrets.GetAsync($"capital:{_config.Environment}:api_key", ct) ?? throw new InvalidOperationException("Missing API key");
            var identifier = await _secrets.GetAsync($"capital:{_config.Environment}:identifier", ct) ?? throw new InvalidOperationException("Missing identifier");
            var password = await _secrets.GetAsync($"capital:{_config.Environment}:password", ct) ?? throw new InvalidOperationException("Missing password");
            Exception? last = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    await ThrottleAsync(ct);
                    var http = CreateHttp();
                    var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/session");
                    req.Headers.Add("X-CAP-API-KEY", apiKey);
                    var body = JsonSerializer.Serialize(new { identifier, password, encryptedPassword = false });
                    req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    var resp = await http.SendAsync(req, ct);
                    if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests || (int)resp.StatusCode >= 500)
                    {
                        last = new InvalidOperationException($"Capital.com session HTTP {(int)resp.StatusCode}");
                        var delay = TimeSpan.FromSeconds(1.1);
                        if (resp.Headers.RetryAfter?.Delta is TimeSpan d) delay = d + TimeSpan.FromMilliseconds(100);
                        await Task.Delay(delay, ct);
                        continue;
                    }
                    if (!resp.IsSuccessStatusCode)
                    {
                        var errBody = await resp.Content.ReadAsStringAsync(ct);
                        throw new InvalidOperationException($"Capital.com session failed: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {errBody}");
                    }
                    var cst = resp.Headers.GetValues("CST").FirstOrDefault() ?? throw new InvalidOperationException("Missing CST");
                    var sec = resp.Headers.GetValues("X-SECURITY-TOKEN").FirstOrDefault() ?? throw new InvalidOperationException("Missing X-SECURITY-TOKEN");
                    _session = new BrokerSession(_ids.NewId("sess"), cst, sec, _clock.UtcNow.AddMinutes(10), _config.Environment, _config.AccountId ?? "");
                    return _session;
                }
                catch (Exception ex) { last = ex; await Task.Delay(TimeSpan.FromMilliseconds(200 * (attempt + 1)), ct); }
            }
            throw last ?? new InvalidOperationException("Session creation failed");
        }
        finally { _sessionLock.Release(); }
    }

    private HttpClient ClientWithSession(BrokerSession s)
    {
        var http = CreateHttp();
        http.DefaultRequestHeaders.Add("CST", s.CstToken);
        http.DefaultRequestHeaders.Add("X-SECURITY-TOKEN", s.SecurityToken);
        return http;
    }

    public async Task<BrokerSession> CreateSessionAsync(CancellationToken ct) => await EnsureSessionAsync(ct);

    public async Task<BrokerAccount> GetAccountAsync(string accountId, CancellationToken ct)
    {
        var s = await EnsureSessionAsync(ct);
        var http = ClientWithSession(s);
        var resp = await http.GetAsync($"/api/v1/accounts", ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        // Best-effort parse; real schema verification required.
        using var doc = JsonDocument.Parse(json);
        decimal balance = 0, equity = 0, free = 0, margin = 0, unreal = 0;
        if (doc.RootElement.TryGetProperty("accounts", out var accs) && accs.GetArrayLength() > 0)
        {
            var a = accs[0];
            if (a.TryGetProperty("balance", out var b)) balance = b.GetDecimal();
            if (a.TryGetProperty("equity", out var e)) equity = e.GetDecimal();
            if (a.TryGetProperty("available", out var av)) free = av.GetDecimal();
        }
        return new BrokerAccount(accountId, _config.Environment, "USD", balance, equity == 0 ? balance : equity, free, margin, unreal, 0m, _clock.UtcNow);
    }

    public async Task<IReadOnlyList<InstrumentMetadata>> SearchInstrumentsAsync(string query, int limit, CancellationToken ct)
    {
        var s = await EnsureSessionAsync(ct);
        var http = ClientWithSession(s);
        var resp = await http.GetAsync($"/api/v1/markets?searchTerm={Uri.EscapeDataString(query ?? "")}", ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        var list = new List<InstrumentMetadata>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("markets", out var markets))
        {
            foreach (var m in markets.EnumerateArray())
            {
                var epic = m.TryGetProperty("epic", out var e) ? e.GetString() ?? "" : "";
                var name = m.TryGetProperty("instrumentName", out var n) ? n.GetString() ?? epic : epic;
                list.Add(new InstrumentMetadata(epic, name, epic, InstrumentAssetClass.Unknown, MarketStatus.Unknown,
                    m.TryGetProperty("bid", out _) ? "USD" : "USD", null, 2, 0.01m, 0.01m, 1000m, 0.01m, null, null, null, null, true, true, true, Array.Empty<string>(), _clock.UtcNow, _config.AccountId ?? "", _config.Environment));
                if (limit > 0 && list.Count >= limit) break;
            }
        }
        return list;
    }

    public async Task<InstrumentMetadata?> GetInstrumentAsync(string epic, string accountId, CancellationToken ct)
    {
        var s = await EnsureSessionAsync(ct);
        var http = ClientWithSession(s);
        var resp = await http.GetAsync($"/api/v1/markets/{Uri.EscapeDataString(epic)}", ct);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var name = root.TryGetProperty("instrumentName", out var n) ? n.GetString() ?? epic : epic;
        return new InstrumentMetadata(epic, name, epic, InstrumentAssetClass.Unknown, MarketStatus.Unknown, "USD", null, 2, 0.01m, 0.01m, 1000m, 0.01m, null, null, null, null, true, true, true, Array.Empty<string>(), _clock.UtcNow, accountId, _config.Environment);
    }

    public async Task<IReadOnlyList<Candle>> GetHistoryAsync(string epic, string timeframe, int count, CancellationToken ct)
    {
        await ThrottleAsync(ct);
        var s = await EnsureSessionAsync(ct);
        var http = ClientWithSession(s);
        var resolution = timeframe switch { "1m" => "MINUTE", "5m" => "MINUTE_5", "15m" => "MINUTE_15", "1h" => "HOUR", "4h" => "HOUR_4", "1d" => "DAY", _ => "MINUTE_5" };
        var resp = await http.GetAsync($"/api/v1/prices/{Uri.EscapeDataString(epic)}?resolution={resolution}&max={count}", ct);
        if (!resp.IsSuccessStatusCode) return Array.Empty<Candle>();
        var json = await resp.Content.ReadAsStringAsync(ct);
        var list = new List<Candle>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("prices", out var prices))
            {
                foreach (var p in prices.EnumerateArray())
                {
                    var ot = p.TryGetProperty("snapshotTimeUTC", out var st) ? DateTimeOffset.Parse(st.GetString()!) : DateTimeOffset.UtcNow;
                    list.Add(new Candle(epic, timeframe, ot, ot, true,
                        p.TryGetProperty("openPrice", out var o) && o.TryGetProperty("bid", out var ob) ? ob.GetDecimal() : 0m,
                        p.TryGetProperty("highPrice", out var h) && h.TryGetProperty("bid", out var hb) ? hb.GetDecimal() : 0m,
                        p.TryGetProperty("lowPrice", out var l) && l.TryGetProperty("bid", out var lb) ? lb.GetDecimal() : 0m,
                        p.TryGetProperty("closePrice", out var c) && c.TryGetProperty("bid", out var cb) ? cb.GetDecimal() : 0m,
                        p.TryGetProperty("lastTradedVolume", out var v) ? v.GetDecimal() : null));
                }
            }
        }
        catch { /* malformed -> empty, fail closed */ }
        return list;
    }

    public async Task<IReadOnlyList<Position>> GetPositionsAsync(string accountId, CancellationToken ct)
    {
        await ThrottleAsync(ct);
        var s = await EnsureSessionAsync(ct);
        var http = ClientWithSession(s);
        var resp = await http.GetAsync("/api/v1/positions", ct);
        if (!resp.IsSuccessStatusCode) return Array.Empty<Position>();
        var json = await resp.Content.ReadAsStringAsync(ct);
        var list = new List<Position>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("positions", out var positions))
            {
                foreach (var p in positions.EnumerateArray())
                {
                    var epic = p.TryGetProperty("market", out var m) && m.TryGetProperty("epic", out var e) ? e.GetString() ?? "" : "";
                    var dealId = p.TryGetProperty("position", out var pos) && pos.TryGetProperty("dealId", out var d) ? d.GetString() ?? "" : "";
                    var direction = p.TryGetProperty("position", out var pos2) && pos2.TryGetProperty("direction", out var dir) && dir.GetString() == "SELL" ? OrderSide.Sell : OrderSide.Buy;
                    var size = p.TryGetProperty("position", out var pos3) && pos3.TryGetProperty("size", out var sz) ? sz.GetDecimal() : 0m;
                    list.Add(new Position(dealId, accountId, _config.Environment, epic, direction, size, null, null, null, null, null, _clock.UtcNow, _clock.UtcNow));
                }
            }
        }
        catch { }
        return list;
    }

    public Task<IReadOnlyList<WorkingOrder>> GetWorkingOrdersAsync(string accountId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<WorkingOrder>>(Array.Empty<WorkingOrder>());

    public async Task<BrokerSubmissionResult> SubmitOrderAsync(OrderIntent intent, CancellationToken ct)
    {
        await ThrottleAsync(ct);
        var s = await EnsureSessionAsync(ct);
        var http = ClientWithSession(s);
        var body = JsonSerializer.Serialize(new
        {
            epic = intent.Epic,
            direction = intent.Side == OrderSide.Buy ? "BUY" : "SELL",
            size = intent.Size,
            guaranteedStop = false,
            stopLevel = intent.StopLevel,
            profitLevel = intent.TakeProfitLevel
        });
        var resp = await http.PostAsync("/api/v1/positions", new StringContent(body, Encoding.UTF8, "application/json"), ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            return new BrokerSubmissionResult(intent.IntentId, null, null, OrderState.Rejected, $"HTTP {(int)resp.StatusCode}: {json}", _clock.UtcNow, null);
        }
        string? dealRef = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("dealReference", out var dr)) dealRef = dr.GetString();
        }
        catch { }
        return new BrokerSubmissionResult(intent.IntentId, dealRef, null, OrderState.BrokerPending, null, _clock.UtcNow, null);
    }

    public async Task<BrokerSubmissionResult?> GetConfirmationAsync(string brokerDealReference, CancellationToken ct)
    {
        await ThrottleAsync(ct);
        var s = await EnsureSessionAsync(ct);
        var http = ClientWithSession(s);
        var resp = await http.GetAsync($"/api/v1/confirms/{Uri.EscapeDataString(brokerDealReference)}", ct);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync(ct);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var dealStatus = doc.RootElement.TryGetProperty("dealStatus", out var ds) ? ds.GetString() : null;
            var state = dealStatus switch
            {
                "ACCEPTED" => OrderState.Confirmed,
                "REJECTED" => OrderState.Rejected,
                _ => OrderState.BrokerPending
            };
            string? dealId = null;
            if (doc.RootElement.TryGetProperty("affectedDeals", out var ad) && ad.ValueKind == JsonValueKind.Array && ad.GetArrayLength() > 0)
            {
                dealId = ad[0].TryGetProperty("dealId", out var di) ? di.GetString() : null;
            }
            var reason = doc.RootElement.TryGetProperty("reason", out var r) ? r.GetString() : null;
            return new BrokerSubmissionResult("", brokerDealReference, dealId, state, reason, _clock.UtcNow, state == OrderState.Confirmed ? _clock.UtcNow : null);
        }
        catch { return null; }
    }

    public Task<bool> CancelOrderAsync(string orderId, string accountId, CancellationToken ct) => Task.FromResult(false);
    public Task<bool> ClosePositionAsync(string positionId, decimal? size, CancellationToken ct) => Task.FromResult(false);
    public Task<IReadOnlyList<MarketQuote>> GetQuotesAsync(IEnumerable<string> epics, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<MarketQuote>>(Array.Empty<MarketQuote>());
}