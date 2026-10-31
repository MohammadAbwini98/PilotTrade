namespace PilotTrade.IntegrationTests;

using System.Text;
using System.Text.Json;

public class FakeClock : IClock
{
    public FakeClock(DateTimeOffset now) { UtcNow = now; }
    public DateTimeOffset UtcNow { get; set; }
}

public sealed record RecordedRequest(string Method, string PathAndQuery, string Path, string? Body, string? Cst, string? SecurityToken, string? ApiKey);

// In-process stand-in for the documented Capital.com Demo API. The active account lives in the session and a new
// session returns to the default account, as on the real API. No test using it can reach the network.
public sealed class CapitalFake : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<Func<HttpResponseMessage?>>> _scripts = new();
    private readonly IClock _clock;
    private int _sessions;

    public CapitalFake(IClock clock) { _clock = clock; }

    public List<RecordedRequest> Requests { get; } = new();
    public string DefaultAccount { get; set; } = "ACC-A";
    public string ActiveAccount { get; private set; } = "ACC-A";
    public bool IgnoreAccountSwitch { get; set; }
    public string AccountsJson { get; set; } = TwoAccounts;
    public string SessionBody { get; set; } = "";
    public Dictionary<string, string> MarketDetails { get; } = new() { ["EURUSD"] = MarketDetail("EURUSD"), ["GOLD"] = MarketDetail("GOLD", currency: "EUR") };
    public string MarketsJson { get; set; } = "";
    public string PricesJson { get; set; } = "";
    public Dictionary<string, string> Positions { get; } = new() { ["ACC-A"] = PositionsFor("EURUSD"), ["ACC-B"] = PositionsFor("GOLD") };
    public Dictionary<string, string> WorkingOrders { get; } = new() { ["ACC-A"] = "{\"workingOrders\":[]}", ["ACC-B"] = WorkingOrdersFor("GOLD") };
    public int SessionCount => _sessions;
    public IEnumerable<RecordedRequest> OrderRequests => Requests.Where(r => r.Method != "GET" && (r.Path.EndsWith("/positions") || r.Path.EndsWith("/workingorders") || r.Path.Contains("/positions/") || r.Path.Contains("/workingorders/")));

    public const string TwoAccounts = """
        {"accounts":[
          {"accountId":"ACC-A","accountName":"Main","status":"ENABLED","accountType":"CFD","preferred":true,"balance":{"balance":1100.0,"deposit":1000.0,"profitLoss":100.0,"available":900.0},"currency":"USD","symbol":"$"},
          {"accountId":"ACC-B","accountName":"Second","status":"ENABLED","accountType":"CFD","preferred":false,"balance":{"balance":500.0,"deposit":400.0,"profitLoss":20.0,"available":300.0},"currency":"EUR","symbol":"E"}
        ]}
        """;

    public static string MarketDetail(string epic, string currency = "USD", string status = "TRADEABLE") => $$"""
        {"instrument":{"epic":"{{epic}}","symbol":"{{epic}}","name":"{{epic}} name","type":"CURRENCIES","currency":"{{currency}}","marginFactor":5,"marginFactorUnit":"PERCENTAGE","guaranteedStopAllowed":true,
           "openingHours":{"mon":["00:00 - 24:00"],"zone":"UTC"} },
         "dealingRules":{"minDealSize":{"unit":"POINTS","value":0.5},"maxDealSize":{"unit":"POINTS","value":200},"minSizeIncrement":{"unit":"POINTS","value":0.1},
           "minStopOrProfitDistance":{"unit":"PERCENTAGE","value":0.2},"maxStopOrProfitDistance":{"unit":"PERCENTAGE","value":30},"marketOrderPreference":"AVAILABLE_DEFAULT_OFF"},
         "snapshot":{"marketStatus":"{{status}}","marketModes":["REGULAR"],"bid":1.1,"offer":1.1002,"decimalPlacesFactor":4} }
        """;

    public static string PositionsFor(string epic) => $$"""
        {"positions":[{"position":{"dealId":"deal-{{epic}}","dealReference":"o_1","size":2,"leverage":20,"upl":-3.5,"direction":"SELL","level":1.2,"currency":"USD","createdDateUTC":"2026-10-30T10:00:00.000","guaranteedStop":false,"stopLevel":1.3},
          "market":{"epic":"{{epic}}","bid":1.1,"offer":1.11} }]}
        """;

    public static string WorkingOrdersFor(string epic) => $$"""
        {"workingOrders":[{"workingOrderData":{"dealId":"wo-{{epic}}","direction":"BUY","epic":"{{epic}}","orderSize":3,"orderLevel":1.05,"orderType":"LIMIT","createdDateUTC":"2026-10-30T10:00:00.000","stopDistance":20,"profitDistance":40},"marketData":{} }]}
        """;

    public void Script(string methodAndPath, params Func<HttpResponseMessage?>[] responses)
    {
        if (!_scripts.TryGetValue(methodAndPath, out var queue)) _scripts[methodAndPath] = queue = new();
        foreach (var r in responses) queue.Enqueue(r);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string? Header(HttpRequestMessage req, string name) =>
        req.Headers.TryGetValues(name, out var v) ? v.First() : null;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        var uri = request.RequestUri!;
        Requests.Add(new RecordedRequest(request.Method.Method, uri.PathAndQuery, uri.AbsolutePath, body, Header(request, "CST"), Header(request, "X-SECURITY-TOKEN"), Header(request, "X-CAP-API-KEY")));

        var key = $"{request.Method.Method} {uri.AbsolutePath}";
        if (_scripts.TryGetValue(key, out var queue) && queue.Count > 0 && queue.Dequeue()() is { } scripted) return scripted;

        if (key == "POST /api/v1/session")
        {
            _sessions++;
            ActiveAccount = DefaultAccount;
            var login = Json(HttpStatusCode.OK, SessionBody.Length > 0 ? SessionBody : JsonSerializer.Serialize(new { currentAccountId = DefaultAccount }));
            login.Headers.Add("CST", $"cst-{_sessions}");
            login.Headers.Add("X-SECURITY-TOKEN", $"sec-{_sessions}");
            return login;
        }

        if (Header(request, "CST") != $"cst-{_sessions}" || Header(request, "X-SECURITY-TOKEN") != $"sec-{_sessions}")
            return Json(HttpStatusCode.Unauthorized, "{\"errorCode\":\"error.invalid.session.token\"}");

        switch (key)
        {
            case "GET /api/v1/session":
                return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { clientId = "c", accountId = ActiveAccount, timezoneOffset = 0, locale = "en", currency = "USD" }));
            case "PUT /api/v1/session":
                var target = JsonDocument.Parse(body!).RootElement.GetProperty("accountId").GetString()!;
                if (!AccountsJson.Contains($"\"{target}\"") || target == ActiveAccount) return Json(HttpStatusCode.BadRequest, "{\"errorCode\":\"error.not-different.accountId\"}");
                if (!IgnoreAccountSwitch) ActiveAccount = target;
                return Json(HttpStatusCode.OK, "{}");
            case "GET /api/v1/accounts":
                return Json(HttpStatusCode.OK, AccountsJson);
            case "GET /api/v1/markets":
                return Json(HttpStatusCode.OK, MarketsJson.Length > 0 ? MarketsJson : DefaultMarkets());
            case "GET /api/v1/positions":
                return Json(HttpStatusCode.OK, Positions[ActiveAccount]);
            case "GET /api/v1/workingorders":
                return Json(HttpStatusCode.OK, WorkingOrders[ActiveAccount]);
        }
        if (uri.AbsolutePath.StartsWith("/api/v1/markets/"))
            return MarketDetails.TryGetValue(uri.AbsolutePath["/api/v1/markets/".Length..], out var detail)
                ? Json(HttpStatusCode.OK, detail) : Json(HttpStatusCode.NotFound, "{\"errorCode\":\"error.not-found.epic\"}");
        if (uri.AbsolutePath.StartsWith("/api/v1/prices/"))
            return Json(HttpStatusCode.OK, PricesJson.Length > 0 ? PricesJson : DefaultPrices(uri.Query.Contains("HOUR") ? TimeSpan.FromHours(1) : TimeSpan.FromMinutes(5)));
        return Json(HttpStatusCode.NotFound, "{\"errorCode\":\"error.not-found\"}");
    }

    private string DefaultMarkets() =>
        $$"""{"markets":[{"epic":"EURUSD","symbol":"EURUSD","instrumentName":"EUR/USD","instrumentType":"CURRENCIES","marketStatus":"TRADEABLE","marketModes":["REGULAR"],"bid":1.1,"offer":1.1002,"updateTimeUTC":"{{_clock.UtcNow.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fff}}","delayTime":0}]}""";

    private string DefaultPrices(TimeSpan bar)
    {
        var now = _clock.UtcNow.UtcDateTime;
        var last = new DateTime(now.Ticks - now.Ticks % bar.Ticks, DateTimeKind.Utc);
        var items = Enumerable.Range(0, 40).Select(i => last - bar * (40 - i)).Select(t =>
            $$"""{"snapshotTimeUTC":"{{t:yyyy-MM-ddTHH:mm:ss}}","openPrice":{"bid":1.1,"ask":1.1002},"closePrice":{"bid":1.1001,"ask":1.1003},"highPrice":{"bid":1.2,"ask":1.2002},"lowPrice":{"bid":1.0,"ask":1.0002},"lastTradedVolume":10}""");
        return $$"""{"prices":[{{string.Join(",", items)}}],"instrumentType":"CURRENCIES"}""";
    }
}
