namespace PilotTrade.Infrastructure.AI;

using System.Text;
using System.Text.Json;
using PilotTrade.Domain;
using PilotTrade.Domain.AI;
using PilotTrade.Domain.Decision;
using PilotTrade.Domain.Security;

public sealed class JevEvaluatorClient : IJevEvaluator
{
    private readonly HttpClient _http;
    private readonly ISecretStore _secrets;
    private readonly TradingEnvironment _env;
    private readonly string _modelId;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;

    public JevEvaluatorClient(HttpClient http, ISecretStore secrets, TradingEnvironment env, string modelId, IClock clock, IIdGenerator ids)
    { _http = http; _secrets = secrets; _env = env; _modelId = modelId; _clock = clock; _ids = ids; }

    public async Task<JevResult> EvaluateAsync(JevRequest request, CancellationToken ct)
    {
        var start = _clock.UtcNow;
        var apiKey = await _secrets.GetAsync($"typesafe:{_env}:api_key", ct) ?? throw new AiCredentialMissingException("TypeSafe");
        var body = new
        {
            model = request.ModelId,
            state = request.StateJson,
            questions = request.Questions.Select(q => new { name = q.Name, type = q.Type, choices = q.Choices }).ToArray()
        };
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.typesafe.ai/v1/systemone");
        req.Headers.Add("Authorization", $"Bearer {apiKey}");
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var answers = new List<JevAnswer>();
        if (root.TryGetProperty("answers", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in arr.EnumerateArray())
            {
                answers.Add(new JevAnswer(
                    a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    a.TryGetProperty("choice", out var c) ? c.GetString() : null,
                    a.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble() : null,
                    a.TryGetProperty("text", out var t) ? t.GetString() : null));
            }
        }
        var modelVersion = root.TryGetProperty("modelVersion", out var mv) ? mv.GetString() ?? request.ModelId : request.ModelId;
        return new JevResult(_ids.NewId("jev"), request.SnapshotId, request.ModelId, modelVersion, answers, _clock.UtcNow, _clock.UtcNow - start,
            request.AccountId, request.Epic, request.Environment, request.SelectionVersion);
    }
}