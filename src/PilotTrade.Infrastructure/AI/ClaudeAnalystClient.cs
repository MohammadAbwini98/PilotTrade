namespace PilotTrade.Infrastructure.AI;

using System.Text;
using System.Text.Json;
using PilotTrade.Domain;
using PilotTrade.Domain.AI;
using PilotTrade.Domain.Market;
using PilotTrade.Domain.Security;

public sealed class ClaudeAnalystClient : IClaudeAnalyst
{
    private readonly HttpClient _http;
    private readonly ISecretStore _secrets;
    private readonly TradingEnvironment _env;
    private readonly string _modelId;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;

    public ClaudeAnalystClient(HttpClient http, ISecretStore secrets, TradingEnvironment env, string modelId, IClock clock, IIdGenerator ids)
    { _http = http; _secrets = secrets; _env = env; _modelId = modelId; _clock = clock; _ids = ids; }

    public async Task<ClaudeAnalysisResult> AnalyzeAsync(ClaudeAnalysisRequest request, CancellationToken ct)
    {
        var start = _clock.UtcNow;
        var system = "You are a market analyst. Respond ONLY with valid JSON matching the required schema. No prose.";
        var user = BuildUser(request);
        var body = new
        {
            model = _modelId,
            max_tokens = 1024,
            system,
            messages = new[] { new { role = "user", content = user } }
        };
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        req.Headers.Add("x-api-key", await GetKeyAsync(ct));
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
        var usage = doc.RootElement.TryGetProperty("usage", out var u) ? u : default;
        int inTok = usage.ValueKind != JsonValueKind.Undefined && usage.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0;
        int outTok = usage.ValueKind != JsonValueKind.Undefined && usage.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0;

        // strict parse
        using var parsed = JsonDocument.Parse(text);
        var root = parsed.RootElement;
        var signalStr = root.GetProperty("signal").GetString() ?? "NO_TRADE";
        if (!Enum.TryParse<DecisionCandidate>(signalStr, true, out var signal)) signal = DecisionCandidate.NoTrade;
        // validate snapshot/epic/env/selection match
        var snapId = root.GetProperty("snapshotId").GetString();
        var epic = root.GetProperty("instrumentEpic").GetString();
        var env = root.GetProperty("environment").GetString();
        var sel = root.GetProperty("selectionVersion").GetInt32();
        if (snapId != request.Snapshot.SnapshotId) throw new InvalidOperationException("snapshotId mismatch");
        if (epic != request.Snapshot.Epic) throw new InvalidOperationException("epic mismatch");
        if (env != request.Snapshot.Environment.ToString().ToLowerInvariant()) throw new InvalidOperationException("environment mismatch");
        if (sel != request.Snapshot.SelectionVersion) throw new InvalidOperationException("selectionVersion mismatch");

        var validUntil = root.TryGetProperty("validUntilUtc", out var vu) ? DateTimeOffset.Parse(vu.GetString()!) : _clock.UtcNow.AddMinutes(5);

        return new ClaudeAnalysisResult(
            _ids.NewId("claude"), request.Snapshot.SnapshotId, request.Snapshot.Epic, request.Snapshot.Environment, request.Snapshot.SelectionVersion,
            _modelId, _modelId, signal,
            root.TryGetProperty("marketRegime", out var mr) ? mr.GetString() ?? "unknown" : "unknown",
            ReadStringArray(root, "evidence"),
            ReadStringArray(root, "contradictions"),
            ReadStringArray(root, "entryConditions"),
            ReadStringArray(root, "invalidationConditions"),
            ReadStringArray(root, "uncertaintyNotes"),
            validUntil, _clock.UtcNow, inTok, outTok, 0m, _clock.UtcNow - start);
    }

    private async Task<string> GetKeyAsync(CancellationToken ct) =>
        await _secrets.GetAsync($"anthropic:{_env}:api_key", ct) ?? throw new InvalidOperationException("Missing Anthropic API key");

    private static List<string> ReadStringArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return new();
        return arr.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
    }

    private static string BuildUser(ClaudeAnalysisRequest request)
    {
        return JsonSerializer.Serialize(new
        {
            snapshotId = request.Snapshot.SnapshotId,
            instrumentEpic = request.Snapshot.Epic,
            environment = request.Snapshot.Environment.ToString().ToLowerInvariant(),
            selectionVersion = request.Snapshot.SelectionVersion,
            quote = request.Snapshot.Quote,
            indicators = request.Snapshot.Indicators,
            availableRiskBudget = request.AvailableRiskBudget,
            currentExposure = request.CurrentExposureSameInstrument
        });
    }
}