namespace PilotTrade.Infrastructure.Broker;

using System.Net;
using System.Text;
using System.Text.Json;
using PilotTrade.Domain;
using PilotTrade.Domain.Broker;
using PilotTrade.Domain.Security;

// Read-only Capital.com Demo adapter. Contract: https://open-api.capital.com/ (see docs/BROKER_CONTRACT_MATRIX.md).
// Failures raise BrokerException, so a broker failure can never look like an empty account.
// Order submission, cancel and close are disabled in this phase and make no network call.
public sealed class CapitalComBrokerAdapter : IBrokerAdapter
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan GeneralInterval = TimeSpan.FromMilliseconds(100); // 10 requests/s
    private static readonly TimeSpan SessionInterval = TimeSpan.FromSeconds(1); // POST /session: 1 request/s
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(10); // expires after 10 minutes idle
    private static readonly IReadOnlyDictionary<string, (string Resolution, TimeSpan Span)> Timeframes = new Dictionary<string, (string, TimeSpan)>
    {
        ["1m"] = ("MINUTE", TimeSpan.FromMinutes(1)),
        ["5m"] = ("MINUTE_5", TimeSpan.FromMinutes(5)),
        ["15m"] = ("MINUTE_15", TimeSpan.FromMinutes(15)),
        ["30m"] = ("MINUTE_30", TimeSpan.FromMinutes(30)),
        ["1h"] = ("HOUR", TimeSpan.FromHours(1)),
        ["4h"] = ("HOUR_4", TimeSpan.FromHours(4)),
        ["1d"] = ("DAY", TimeSpan.FromDays(1)),
        ["1w"] = ("WEEK", TimeSpan.FromDays(7)),
    };

    private readonly BrokerEnvironmentConfig _config;
    private readonly ISecretStore _secrets;
    private readonly IHttpClientFactory? _httpFactory;
    private readonly HttpClient? _httpClient;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private readonly SemaphoreSlim _accountLock = new(1, 1); // the active account is per session, so switch + read is one unit
    private readonly RateGate _generalGate;
    private readonly RateGate _sessionGate;
    private BrokerSession? _session;
    private long _sessionGeneration;
    private int _nonSessionWrites;

    public CapitalComBrokerAdapter(BrokerEnvironmentConfig config, ISecretStore secrets, IHttpClientFactory httpFactory, IClock clock, IIdGenerator ids, Func<TimeSpan, CancellationToken, Task>? delay = null)
        : this(config, secrets, httpFactory, null, clock, ids, delay) { }

    public CapitalComBrokerAdapter(BrokerEnvironmentConfig config, ISecretStore secrets, HttpClient httpClient, IClock clock, IIdGenerator ids, Func<TimeSpan, CancellationToken, Task>? delay = null)
        : this(config, secrets, null, httpClient, clock, ids, delay) { }

    private CapitalComBrokerAdapter(BrokerEnvironmentConfig config, ISecretStore secrets, IHttpClientFactory? httpFactory, HttpClient? httpClient, IClock clock, IIdGenerator ids, Func<TimeSpan, CancellationToken, Task>? delay)
    {
        if (config.Environment != TradingEnvironment.Demo || config.LiveExecutionEnabled)
            throw new InvalidOperationException("Capital.com Live is locked.");
        if (!string.Equals(config.ApiBaseUri.TrimEnd('/'), BrokerRuntimeOptions.CapitalDemoBaseUri, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Capital.com adapter only accepts the Demo API host.");
        _config = config; _secrets = secrets; _httpFactory = httpFactory; _httpClient = httpClient; _clock = clock; _ids = ids;
        _delay = delay ?? Task.Delay;
        _generalGate = new RateGate(GeneralInterval, clock, _delay);
        _sessionGate = new RateGate(SessionInterval, clock, _delay);
    }

    public TradingEnvironment Environment => _config.Environment;
    public bool ExecutionEnabled => false;
    public string ApiHost => new Uri(_config.ApiBaseUri).Host;
    public int NonSessionWriteRequests => Volatile.Read(ref _nonSessionWrites);

    private HttpClient Http() => _httpClient ?? _httpFactory!.CreateClient("capital");
    private Uri Url(string pathAndQuery) => new(new Uri(_config.ApiBaseUri), pathAndQuery);

    private sealed class RateGate
    {
        private readonly TimeSpan _interval;
        private readonly IClock _clock;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly object _lock = new();
        private DateTimeOffset _next = DateTimeOffset.MinValue;

        public RateGate(TimeSpan interval, IClock clock, Func<TimeSpan, CancellationToken, Task> delay) { _interval = interval; _clock = clock; _delay = delay; }

        public Task WaitAsync(CancellationToken ct)
        {
            TimeSpan wait;
            lock (_lock)
            {
                var now = _clock.UtcNow;
                var slot = _next > now ? _next : now;
                _next = slot + _interval;
                wait = slot - now;
            }
            return wait > TimeSpan.Zero ? _delay(wait, ct) : Task.CompletedTask;
        }
    }

    private static TimeSpan Backoff(int attempt, HttpResponseMessage? resp = null)
    {
        if (resp?.Headers.RetryAfter?.Delta is { } d && d > TimeSpan.Zero) return d;
        return TimeSpan.FromSeconds(1 << attempt);
    }

    private async Task<BrokerSession> EnsureSessionAsync(CancellationToken ct)
    {
        var current = _session;
        if (current is not null && current.ExpiresUtc > _clock.UtcNow.AddMinutes(1)) return current;
        await _sessionLock.WaitAsync(ct);
        try
        {
            current = _session;
            if (current is not null && current.ExpiresUtc > _clock.UtcNow.AddMinutes(1)) return current;

            var apiKey = await RequireSecretAsync("api_key", ct);
            var identifier = await RequireSecretAsync("identifier", ct);
            var password = await RequireSecretAsync("password", ct);
            var body = JsonSerializer.Serialize(new { identifier, password, encryptedPassword = false });

            for (var attempt = 0; ; attempt++)
            {
                await _sessionGate.WaitAsync(ct);
                using var req = new HttpRequestMessage(HttpMethod.Post, Url("/api/v1/session"));
                req.Headers.TryAddWithoutValidation("X-CAP-API-KEY", apiKey);
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");

                HttpResponseMessage resp;
                try { resp = await Http().SendAsync(req, ct); }
                catch (Exception ex) when (IsTransient(ex, ct))
                {
                    if (attempt + 1 >= MaxAttempts) throw new BrokerException(BrokerErrorKind.Unavailable, "Broker session request failed");
                    await _delay(Backoff(attempt), ct);
                    continue;
                }
                using (resp)
                {
                    if (resp.StatusCode == HttpStatusCode.TooManyRequests || (int)resp.StatusCode >= 500)
                    {
                        if (attempt + 1 >= MaxAttempts) throw TerminalError(resp.StatusCode);
                        await _delay(Backoff(attempt, resp), ct);
                        continue;
                    }
                    if (!resp.IsSuccessStatusCode)
                        throw new BrokerException(BrokerErrorKind.Authentication, $"Broker refused the session (HTTP {(int)resp.StatusCode})");

                    var cst = Header(resp, "CST");
                    var security = Header(resp, "X-SECURITY-TOKEN");
                    using var doc = CapitalComJson.Parse(await resp.Content.ReadAsStringAsync(ct));
                    var activeAccount = CapitalComJson.Str(doc.RootElement, "currentAccountId");
                    var session = new BrokerSession(_ids.NewId("sess"), cst, security, _clock.UtcNow + SessionLifetime, _config.Environment, activeAccount);
                    Interlocked.Increment(ref _sessionGeneration);
                    _session = session;
                    return session;
                }
            }
        }
        finally { _sessionLock.Release(); }
    }

    private async Task<string> RequireSecretAsync(string name, CancellationToken ct)
    {
        var value = await _secrets.GetAsync($"capital:{_config.Environment}:{name}", ct);
        return string.IsNullOrWhiteSpace(value)
            ? throw new BrokerException(BrokerErrorKind.CredentialsMissing, "Capital.com Demo credentials are not configured")
            : value;
    }

    private static string Header(HttpResponseMessage resp, string name) =>
        resp.Headers.TryGetValues(name, out var values) && values.FirstOrDefault() is { Length: > 0 } v ? v : throw CapitalComJson.Malformed($"{name} header");

    private static bool IsTransient(Exception ex, CancellationToken ct) =>
        !ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException;

    private static BrokerException TerminalError(HttpStatusCode status) => status switch
    {
        HttpStatusCode.TooManyRequests => new BrokerException(BrokerErrorKind.RateLimited, "Broker rate limit reached"),
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new BrokerException(BrokerErrorKind.Authentication, "Broker rejected the session"),
        HttpStatusCode.NotFound => new BrokerException(BrokerErrorKind.NotFound, "Broker resource not found"),
        >= HttpStatusCode.InternalServerError => new BrokerException(BrokerErrorKind.Unavailable, $"Broker unavailable (HTTP {(int)status})"),
        _ => new BrokerException(BrokerErrorKind.InvalidRequest, $"Broker refused the request (HTTP {(int)status})")
    };

    // Only GET and PUT /session come through here, and both are safe to repeat. Order submission never does.
    private async Task<string> SendAsync(HttpMethod method, string pathAndQuery, string? jsonBody, CancellationToken ct)
    {
        if (method != HttpMethod.Get && !pathAndQuery.StartsWith("/api/v1/session", StringComparison.Ordinal)) Interlocked.Increment(ref _nonSessionWrites);
        var reauthenticated = false;
        for (var attempt = 0; ; attempt++)
        {
            var session = await EnsureSessionAsync(ct);
            await _generalGate.WaitAsync(ct);
            using var req = new HttpRequestMessage(method, Url(pathAndQuery));
            req.Headers.TryAddWithoutValidation("CST", session.CstToken);
            req.Headers.TryAddWithoutValidation("X-SECURITY-TOKEN", session.SecurityToken);
            if (jsonBody is not null) req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            HttpResponseMessage resp;
            try { resp = await Http().SendAsync(req, ct); }
            catch (Exception ex) when (IsTransient(ex, ct))
            {
                if (attempt + 1 >= MaxAttempts) throw new BrokerException(BrokerErrorKind.Unavailable, "Broker request failed");
                await _delay(Backoff(attempt), ct);
                continue;
            }
            using (resp)
            {
                if (resp.IsSuccessStatusCode)
                {
                    _session = session with { ExpiresUtc = _clock.UtcNow + SessionLifetime };
                    return await resp.Content.ReadAsStringAsync(ct);
                }
                if (resp.StatusCode == HttpStatusCode.Unauthorized && !reauthenticated)
                {
                    reauthenticated = true;
                    _session = null;
                    attempt--;
                    continue;
                }
                if ((resp.StatusCode == HttpStatusCode.TooManyRequests || (int)resp.StatusCode >= 500) && attempt + 1 < MaxAttempts)
                {
                    await _delay(Backoff(attempt, resp), ct);
                    continue;
                }
                throw TerminalError(resp.StatusCode);
            }
        }
    }

    private async Task<string> GetAsync(string pathAndQuery, CancellationToken ct) => await SendAsync(HttpMethod.Get, pathAndQuery, null, ct);

    // Runs a read against the requested account after proving, through GET /session, that it is the active one.
    // A re-login during the read lands back on the default account, so that read is discarded and repeated.
    private async Task<T> WithAccountAsync<T>(string accountId, Func<Task<T>> read, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accountId)) throw new BrokerException(BrokerErrorKind.AccountNotFound, "Account is required");
        await _accountLock.WaitAsync(ct);
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                await ActivateAccountAsync(accountId, ct);
                var generation = Interlocked.Read(ref _sessionGeneration);
                var result = await read();
                if (Interlocked.Read(ref _sessionGeneration) == generation) return result;
            }
            throw new BrokerException(BrokerErrorKind.AccountMismatch, "Broker session changed during the request");
        }
        finally { _accountLock.Release(); }
    }

    private async Task ActivateAccountAsync(string accountId, CancellationToken ct)
    {
        if (await ReadActiveAccountAsync(ct) == accountId) return;
        try
        {
            await SendAsync(HttpMethod.Put, "/api/v1/session", JsonSerializer.Serialize(new { accountId }), ct);
        }
        catch (BrokerException ex) when (ex.Kind is BrokerErrorKind.InvalidRequest or BrokerErrorKind.NotFound)
        {
            throw new BrokerException(BrokerErrorKind.AccountNotFound, "Broker refused the requested account");
        }
        if (await ReadActiveAccountAsync(ct) != accountId)
            throw new BrokerException(BrokerErrorKind.AccountMismatch, "Broker session is not on the requested account");
    }

    private async Task<string> ReadActiveAccountAsync(CancellationToken ct)
    {
        using var doc = CapitalComJson.Parse(await GetAsync("/api/v1/session", ct));
        return CapitalComJson.Str(doc.RootElement, "accountId");
    }

    public async Task<BrokerSession> CreateSessionAsync(CancellationToken ct) => await EnsureSessionAsync(ct);

    public async Task<IReadOnlyList<BrokerAccountSummary>> GetAccountsAsync(CancellationToken ct)
    {
        using var doc = CapitalComJson.Parse(await GetAsync("/api/v1/accounts", ct));
        return CapitalComJson.Arr(doc.RootElement, "accounts").EnumerateArray()
            .Select(a => new BrokerAccountSummary(
                CapitalComJson.Str(a, "accountId"), CapitalComJson.OptStr(a, "accountName"), CapitalComJson.OptStr(a, "status"),
                CapitalComJson.OptStr(a, "currency"), CapitalComJson.Bool(a, "preferred")))
            .ToList();
    }

    public async Task<BrokerAccount> GetAccountAsync(string accountId, CancellationToken ct) => await WithAccountAsync(accountId, async () =>
    {
        using var doc = CapitalComJson.Parse(await GetAsync("/api/v1/accounts", ct));
        var matches = CapitalComJson.Arr(doc.RootElement, "accounts").EnumerateArray()
            .Where(a => CapitalComJson.Str(a, "accountId") == accountId).ToList();
        if (matches.Count == 0) throw new BrokerException(BrokerErrorKind.AccountNotFound, "Requested account not found");
        if (matches.Count > 1) throw CapitalComJson.Malformed("duplicate accountId");

        var account = matches[0];
        var money = CapitalComJson.Obj(account, "balance");
        var balance = CapitalComJson.Dec(money, "balance");
        var deposit = CapitalComJson.Dec(money, "deposit");
        var profitLoss = CapitalComJson.Dec(money, "profitLoss");
        var available = CapitalComJson.Dec(money, "available");

        // The API has no equity field. Balance only counts as equity when the response itself shows it already
        // includes open P/L (balance = deposit + profitLoss); otherwise equity stays null and risk fails closed.
        // Margin used and realized P/L are not documented at all and are never estimated.
        decimal? equity = Math.Abs(balance - (deposit + profitLoss)) <= 0.01m ? balance : null;

        return new BrokerAccount(accountId, _config.Environment, CapitalComJson.Str(account, "currency"), balance, equity, available,
            null, profitLoss, null, _clock.UtcNow, equity is not null);
    }, ct);

    public async Task<IReadOnlyList<InstrumentSummary>> SearchInstrumentsAsync(string query, int limit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<InstrumentSummary>();
        using var doc = CapitalComJson.Parse(await GetAsync($"/api/v1/markets?searchTerm={Uri.EscapeDataString(query)}", ct));
        var results = CapitalComJson.Arr(doc.RootElement, "markets").EnumerateArray()
            .Select(m => new InstrumentSummary(
                CapitalComJson.Str(m, "epic"), CapitalComJson.Str(m, "instrumentName"), CapitalComJson.OptStr(m, "symbol"),
                CapitalComJson.AssetClass(CapitalComJson.OptStr(m, "instrumentType")), CapitalComJson.Status(m),
                CapitalComJson.OptDec(m, "bid"), CapitalComJson.OptDec(m, "offer")));
        return (limit > 0 ? results.Take(limit) : results).ToList();
    }

    public async Task<InstrumentMetadata?> GetInstrumentAsync(string epic, string accountId, CancellationToken ct) => await WithAccountAsync(accountId, async () =>
    {
        string json;
        try { json = await GetAsync($"/api/v1/markets/{Uri.EscapeDataString(epic)}", ct); }
        catch (BrokerException ex) when (ex.Kind == BrokerErrorKind.NotFound) { return null; }

        using var doc = CapitalComJson.Parse(json);
        var root = doc.RootElement;
        var instrument = CapitalComJson.Obj(root, "instrument");
        var rules = CapitalComJson.Obj(root, "dealingRules");
        var snapshot = CapitalComJson.Obj(root, "snapshot");

        if (CapitalComJson.Str(instrument, "epic") != epic) throw CapitalComJson.Malformed("instrument.epic");
        var minSize = CapitalComJson.Rule(rules, "minDealSize").Value;
        var maxSize = CapitalComJson.Rule(rules, "maxDealSize").Value;
        if (maxSize < minSize) throw CapitalComJson.Malformed("maxDealSize");
        var step = CapitalComJson.Rule(rules, "minSizeIncrement").Value;
        var minStop = CapitalComJson.Rule(rules, "minStopOrProfitDistance");
        var maxStop = CapitalComJson.Rule(rules, "maxStopOrProfitDistance");

        var precisionValue = CapitalComJson.Dec(snapshot, "decimalPlacesFactor");
        if (precisionValue < 0 || precisionValue > 12 || precisionValue != decimal.Truncate(precisionValue)) throw CapitalComJson.Malformed("decimalPlacesFactor");
        var precision = (int)precisionValue;
        var tick = 1m;
        for (var i = 0; i < precision; i++) tick /= 10m;

        var marginFactor = CapitalComJson.OptDec(instrument, "marginFactor");
        decimal? leverage = marginFactor is > 0 && CapitalComJson.OptStr(instrument, "marginFactorUnit") == "PERCENTAGE" ? 100m / marginFactor : null;
        var regular = snapshot.TryGetProperty("marketModes", out var modes) && modes.ValueKind == JsonValueKind.Array
            && modes.EnumerateArray().Any(m => m.GetString() == "REGULAR");
        var marketPreference = CapitalComJson.OptStr(rules, "marketOrderPreference");
        var currency = CapitalComJson.Str(instrument, "currency");

        return new InstrumentMetadata(
            epic, CapitalComJson.Str(instrument, "name"), CapitalComJson.OptStr(instrument, "symbol") ?? epic,
            CapitalComJson.AssetClass(CapitalComJson.OptStr(instrument, "type")), CapitalComJson.Status(snapshot),
            currency, currency, precision, tick, minSize, maxSize, step, marginFactor, leverage, minStop.Value, maxStop.Value,
            regular && marketPreference is not null && marketPreference.StartsWith("AVAILABLE", StringComparison.Ordinal),
            regular, regular, ReadTradingHours(instrument), _clock.UtcNow, accountId, _config.Environment,
            minStop.Unit, maxStop.Unit, CapitalComJson.Bool(instrument, "guaranteedStopAllowed"));
    }, ct);

    private static IReadOnlyList<string> ReadTradingHours(JsonElement instrument)
    {
        if (!instrument.TryGetProperty("openingHours", out var hours) || hours.ValueKind != JsonValueKind.Object) return Array.Empty<string>();
        var zone = CapitalComJson.OptStr(hours, "zone");
        var result = new List<string>();
        foreach (var day in hours.EnumerateObject().Where(d => d.Value.ValueKind == JsonValueKind.Array))
            foreach (var range in day.Value.EnumerateArray())
                result.Add($"{day.Name} {range.GetString()} {zone}".TrimEnd());
        return result;
    }

    // An epics query answers with marketDetails (only searchTerm answers with markets).
    public async Task<IReadOnlyList<MarketQuote>> GetQuotesAsync(IEnumerable<string> epics, CancellationToken ct)
    {
        var quotes = new List<MarketQuote>();
        decimal? timezoneOffset = null;
        foreach (var batch in epics.Distinct().Chunk(50)) // GET /markets accepts at most 50 epics
        {
            var query = Uri.EscapeDataString(string.Join(",", batch));
            using var doc = CapitalComJson.Parse(await GetAsync($"/api/v1/markets?epics={query}", ct));
            foreach (var m in CapitalComJson.Arr(doc.RootElement, "marketDetails").EnumerateArray())
            {
                var epic = CapitalComJson.Str(CapitalComJson.Obj(m, "instrument"), "epic");
                if (!batch.Contains(epic)) throw CapitalComJson.Malformed("instrument.epic");
                var snapshot = CapitalComJson.Obj(m, "snapshot");
                var bid = CapitalComJson.Positive(snapshot, "bid");
                var offer = CapitalComJson.Positive(snapshot, "offer");
                if (offer < bid) throw CapitalComJson.Malformed("crossed quote");
                var delayed = CapitalComJson.OptDec(snapshot, "delayTime") is > 0;

                // The documented snapshot has no UTC field. updateTime is session-local, as the documented pairs of
                // updateTime and updateTimeUTC elsewhere show, so it is converted with the session's own offset.
                DateTimeOffset updated;
                if (snapshot.TryGetProperty("updateTimeUTC", out _)) updated = CapitalComJson.Utc(snapshot, "updateTimeUTC");
                else
                {
                    timezoneOffset ??= await ReadTimezoneOffsetAsync(ct);
                    updated = CapitalComJson.Utc(snapshot, "updateTime") - TimeSpan.FromHours((double)timezoneOffset.Value);
                }
                quotes.Add(new MarketQuote(epic, bid, offer, (bid + offer) / 2m, offer - bid,
                    updated, _clock.UtcNow, delayed ? FeedHealth.Stale : FeedHealth.Healthy));
            }
        }
        return quotes;
    }

    private async Task<decimal> ReadTimezoneOffsetAsync(CancellationToken ct)
    {
        using var doc = CapitalComJson.Parse(await GetAsync("/api/v1/session", ct));
        var hours = CapitalComJson.Dec(doc.RootElement, "timezoneOffset");
        return Math.Abs(hours) <= 14m ? hours : throw CapitalComJson.Malformed("timezoneOffset");
    }

    public async Task<IReadOnlyList<Candle>> GetHistoryAsync(string epic, string timeframe, int count, CancellationToken ct)
    {
        if (!Timeframes.TryGetValue(timeframe, out var tf))
            throw new BrokerException(BrokerErrorKind.InvalidRequest, "Unsupported timeframe");
        var max = Math.Clamp(count, 1, 1000);
        using var doc = CapitalComJson.Parse(await GetAsync($"/api/v1/prices/{Uri.EscapeDataString(epic)}?resolution={tf.Resolution}&max={max}", ct));
        var now = _clock.UtcNow;
        return CapitalComJson.Arr(doc.RootElement, "prices").EnumerateArray().Select(p =>
        {
            var open = CapitalComJson.Utc(p, "snapshotTimeUTC");
            var close = open + tf.Span;
            return new Candle(epic, timeframe, open, close, close <= now,
                Mid(p, "openPrice"), Mid(p, "highPrice"), Mid(p, "lowPrice"), Mid(p, "closePrice"), CapitalComJson.OptDec(p, "lastTradedVolume"));
        }).ToList();
    }

    private static decimal Mid(JsonElement price, string name)
    {
        var side = CapitalComJson.Obj(price, name);
        return (CapitalComJson.Positive(side, "bid") + CapitalComJson.Positive(side, "ask")) / 2m;
    }

    public async Task<IReadOnlyList<Position>> GetPositionsAsync(string accountId, CancellationToken ct) => await WithAccountAsync<IReadOnlyList<Position>>(accountId, async () =>
    {
        using var doc = CapitalComJson.Parse(await GetAsync("/api/v1/positions", ct));
        var now = _clock.UtcNow;
        return CapitalComJson.Arr(doc.RootElement, "positions").EnumerateArray().Select(item =>
        {
            var p = CapitalComJson.Obj(item, "position");
            var m = CapitalComJson.Obj(item, "market");
            var side = CapitalComJson.Side(p, "direction");
            var bid = CapitalComJson.OptDec(m, "bid");
            var offer = CapitalComJson.OptDec(m, "offer");
            return new Position(CapitalComJson.Str(p, "dealId"), accountId, _config.Environment, CapitalComJson.Str(m, "epic"), side,
                CapitalComJson.Positive(p, "size"), CapitalComJson.Positive(p, "level"), side == OrderSide.Buy ? bid : offer,
                CapitalComJson.Dec(p, "upl"), CapitalComJson.OptDec(p, "stopLevel"), CapitalComJson.OptDec(p, "profitLevel"),
                CapitalComJson.Utc(p, "createdDateUTC"), now);
        }).ToList();
    }, ct);

    public async Task<IReadOnlyList<WorkingOrder>> GetWorkingOrdersAsync(string accountId, CancellationToken ct) => await WithAccountAsync<IReadOnlyList<WorkingOrder>>(accountId, async () =>
    {
        using var doc = CapitalComJson.Parse(await GetAsync("/api/v1/workingorders", ct));
        var now = _clock.UtcNow;
        return CapitalComJson.Arr(doc.RootElement, "workingOrders").EnumerateArray().Select(item =>
        {
            var o = CapitalComJson.Obj(item, "workingOrderData");
            var type = CapitalComJson.Str(o, "orderType") switch
            {
                "LIMIT" => OrderType.Limit,
                "STOP" => OrderType.Stop,
                _ => throw CapitalComJson.Malformed("orderType")
            };
            return new WorkingOrder(CapitalComJson.Str(o, "dealId"), accountId, _config.Environment, CapitalComJson.Str(o, "epic"),
                CapitalComJson.Side(o, "direction"), type, CapitalComJson.Positive(o, "orderSize"), CapitalComJson.Positive(o, "orderLevel"),
                null, null, CapitalComJson.Utc(o, "createdDateUTC"), now, CapitalComJson.OptDec(o, "stopDistance"), CapitalComJson.OptDec(o, "profitDistance"));
        }).ToList();
    }, ct);

    // Disabled in this phase: never opens a session and never sends a request.
    public Task<BrokerSubmissionResult> SubmitOrderAsync(OrderIntent intent, CancellationToken ct) =>
        Task.FromResult(new BrokerSubmissionResult(intent.IntentId, null, null, OrderState.Rejected, "Capital.com order execution is disabled", _clock.UtcNow, null));

    public async Task<BrokerSubmissionResult?> GetConfirmationAsync(string brokerDealReference, CancellationToken ct)
    {
        string json;
        try { json = await GetAsync($"/api/v1/confirms/{Uri.EscapeDataString(brokerDealReference)}", ct); }
        catch (BrokerException ex) when (ex.Kind == BrokerErrorKind.NotFound) { return null; }
        return CapitalComOrderContract.ParseConfirmation(json, brokerDealReference, _clock.UtcNow);
    }

    public Task<bool> CancelOrderAsync(string orderId, string accountId, CancellationToken ct) => Task.FromResult(false);
    public Task<bool> ClosePositionAsync(string positionId, decimal? size, CancellationToken ct) => Task.FromResult(false);
}
