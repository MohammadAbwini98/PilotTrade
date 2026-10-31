using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PilotTrade.Application.Controls;
using PilotTrade.Application.Services;
using PilotTrade.Domain;
using PilotTrade.Domain.Audit;
using PilotTrade.Domain.Broker;
using PilotTrade.Domain.Market;
using PilotTrade.Domain.Security;
using PilotTrade.Infrastructure.Audit;
using PilotTrade.Infrastructure.Broker;
using PilotTrade.Infrastructure.Persistence;
using PilotTrade.Infrastructure.Security;

// Explicit owner request from their own terminal; the token is never written to logs.
if (args.Contains("--show-owner-token"))
{
    var token = await new OwnerAuthorizationService(PlatformSecretStores.Create(allowProtectedMutations: false)).EnsureOwnerTokenAsync(CancellationToken.None);
    Console.WriteLine(token);
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PilotTradeDbContext>((sp, o) =>
    o.UseSqlite(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default") ?? "Data Source=pilottrade.db"), ServiceLifetime.Singleton);

builder.Services.AddOpenApi();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IIdGenerator, GuidIdGenerator>();
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return BrokerRuntimeOptions.Parse(config["BROKER_MODE"], config["TRADING_ENVIRONMENT"], config["TRADING_ACCOUNT_ID"]);
});
// Reads and owner-authorized writes use the same OS-protected store; only writers may change protected targets.
builder.Services.AddSingleton<ISecretStore>(_ => PlatformSecretStores.Create(allowProtectedMutations: false));
builder.Services.AddKeyedSingleton<ISecretStore>("privileged", (_, _) => PlatformSecretStores.Create(allowProtectedMutations: true));
builder.Services.AddSingleton<IOwnerAuthorizationService>(sp => new OwnerAuthorizationService(sp.GetRequiredService<ISecretStore>()));
builder.Services.AddSingleton<IPrivilegedCredentialManager>(sp =>
    new PrivilegedCredentialManager(sp.GetRequiredKeyedService<ISecretStore>("privileged")));
builder.Services.AddSingleton<ICsrfTokenService, InMemoryCsrfTokenService>();
builder.Services.AddSingleton<IPersistedExecutionState, EfPersistedExecutionState>();
builder.Services.AddSingleton<IOrderJournal, EfOrderJournal>();
builder.Services.AddSingleton<ITradingControl>(sp =>
{
    var options = sp.GetRequiredService<BrokerRuntimeOptions>();
    return new PersistedTradingControl(sp.GetRequiredService<IPersistedExecutionState>(), options.Environment, options.AccountId, sp.GetRequiredService<IClock>());
});
builder.Services.AddSingleton<BrokerAdapterFactory>();
builder.Services.AddSingleton<IBrokerSessionFactory>(sp => sp.GetRequiredService<BrokerAdapterFactory>());
builder.Services.AddSingleton<IBrokerAdapter>(sp =>
    sp.GetRequiredService<IBrokerSessionFactory>().Create(sp.GetRequiredService<BrokerRuntimeOptions>().Environment));
builder.Services.AddSingleton<IInstrumentCatalog>(sp => new InstrumentCatalogService(sp.GetRequiredService<IBrokerAdapter>()));
builder.Services.AddSingleton<IInstrumentSelectionService, InstrumentSelectionService>();
builder.Services.AddSingleton<IMarketDataService>(sp => new MarketDataService(sp.GetRequiredService<IBrokerAdapter>(), sp.GetRequiredService<IClock>()));
builder.Services.AddSingleton<IRiskEngine, RiskEngine>();
builder.Services.AddSingleton<IDecisionCoordinator, DecisionCoordinator>();
builder.Services.AddSingleton<IPositionGuardian>(sp => new PositionGuardian(sp.GetRequiredService<IBrokerAdapter>(), sp.GetRequiredService<IClock>()));
builder.Services.AddSingleton<IAuditLogger, EfAuditLogger>();
// The default HttpClient logging records request URLs, which have no place in this app's logs.
builder.Services.AddHttpClient("capital", c => c.Timeout = TimeSpan.FromSeconds(20)).RemoveAllLoggers();
builder.Services.AddSingleton<OrderReconciliationService>();
builder.Services.AddHostedService<StartupReconciliationService>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173").AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Invalid broker configuration (including any request for Live) stops startup here.
var runtime = app.Services.GetRequiredService<BrokerRuntimeOptions>();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PilotTradeDbContext>();
    db.Database.Migrate();
}

// Explicitly requested, sanitized read-only evidence run: POST/PUT /session plus GETs, no order endpoints.
if (args.Contains(DemoEvidenceRunner.Flag))
{
    var probeAccount = string.IsNullOrWhiteSpace(app.Configuration["TRADING_ACCOUNT_ID"]) ? null : runtime.AccountId;
    Environment.ExitCode = await DemoEvidenceRunner.RunAsync(args, probeAccount,
        () => app.Services.GetRequiredService<BrokerAdapterFactory>().CreateCapitalDemo(probeAccount),
        app.Services.GetRequiredService<IClock>(), Console.Out, Console.Error, CancellationToken.None);
    return;
}

try { await app.Services.GetRequiredService<IOwnerAuthorizationService>().EnsureOwnerTokenAsync(CancellationToken.None); }
catch (PlatformNotSupportedException) { app.Logger.LogWarning("No OS-protected credential store on this platform; all /api routes will refuse access."); }

app.UseExceptionHandler(a => a.Run(ctx =>
{
    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    return ctx.Response.WriteAsJsonAsync(new { error = "internal_error" });
}));
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTimeOffset.UtcNow }));

var api = app.MapGroup("/api").AddEndpointFilter(ApiSecurity.RequireOwner).AddEndpointFilter(ApiSecurity.SanitizeBrokerErrors);
var guarded = api.MapGroup("").AddEndpointFilter(ApiSecurity.RequireBrowserGuards);

api.MapGet("/runtime", (BrokerRuntimeOptions options, IBrokerAdapter broker) => Results.Ok(new
{
    mode = options.Mode.ToString(),
    environment = options.Environment.ToString(),
    accountId = options.AccountId,
    executionEnabled = broker.ExecutionEnabled,
    liveLocked = true
}));

api.MapGet("/security/csrf-token", (ICsrfTokenService csrf) => Results.Ok(new { csrfToken = csrf.IssueToken() }));

// Instruments
api.MapGet("/instruments/search", async (string? q, int? limit, IInstrumentCatalog catalog, CancellationToken ct) =>
    Results.Ok(await catalog.SearchAsync(q ?? "", Math.Clamp(limit ?? 50, 1, 200), ct)));

api.MapGet("/instruments/selection", (string accountId, BrokerRuntimeOptions options, IInstrumentSelectionService selections) =>
    selections.Current(accountId, options.Environment) is { } s ? Results.Ok(s) : Results.NotFound());

api.MapGet("/instruments/{epic}", async (string epic, string accountId, IBrokerAdapter broker, CancellationToken ct) =>
{
    var m = await broker.GetInstrumentAsync(epic, accountId, ct);
    return m is null ? Results.NotFound() : Results.Ok(m);
});

guarded.MapPost("/instruments/select", async (InstrumentSelectRequest req, IBrokerAdapter broker, BrokerRuntimeOptions options, IInstrumentSelectionService selections, CancellationToken ct) =>
{
    var meta = await broker.GetInstrumentAsync(req.Epic, req.AccountId, ct);
    if (meta is null) return Results.NotFound(new { error = "NotFound" });
    return Results.Ok(new { selection = selections.Select(req.AccountId, options.Environment, req.Epic, "owner"), instrument = meta });
});

// Account
api.MapGet("/accounts", async (IBrokerAdapter broker, CancellationToken ct) => Results.Ok(await broker.GetAccountsAsync(ct)));

api.MapGet("/account", async (string accountId, IBrokerAdapter broker, CancellationToken ct) =>
    Results.Ok(await broker.GetAccountAsync(accountId, ct)));

// Positions
api.MapGet("/positions", async (string accountId, IBrokerAdapter broker, CancellationToken ct) =>
    Results.Ok(await broker.GetPositionsAsync(accountId, ct)));

api.MapGet("/orders", async (string accountId, IBrokerAdapter broker, CancellationToken ct) =>
    Results.Ok(await broker.GetWorkingOrdersAsync(accountId, ct)));

// Market snapshot: any missing, invalid or outdated input is a NO_TRADE result, not a partial snapshot.
guarded.MapPost("/market/snapshot", async (MarketSnapshotRequest req, IMarketDataService mds, IInstrumentSelectionService selections, BrokerRuntimeOptions options, CancellationToken ct) =>
{
    var current = selections.Current(req.AccountId, options.Environment);
    if (current is null || current.Epic != req.Epic || current.SelectionVersion != req.SelectionVersion)
        return Results.Ok(new { outcome = "NO_TRADE", reason = "Selection changed: request outdated", snapshot = (MarketSnapshotBundle?)null });
    try
    {
        var bundle = await mds.BuildSnapshotAsync(req.AccountId, options.Environment, req.Epic, req.SelectionVersion, req.Timeframes ?? new[] { "5m", "1h" }, ct);
        return Results.Ok(new { outcome = "SNAPSHOT_READY", reason = (string?)null, snapshot = (MarketSnapshotBundle?)bundle });
    }
    catch (MarketDataRejectedException ex)
    {
        return Results.Ok(new { outcome = "NO_TRADE", reason = (string?)ex.Message, snapshot = (MarketSnapshotBundle?)null });
    }
});

// Read-only Demo connection check. Returns sanitized evidence only.
guarded.MapPost("/demo/test-connection", async (string? epic, BrokerAdapterFactory factory, BrokerRuntimeOptions options, IAuditLogger audit, IClock clock, IIdGenerator ids, CancellationToken ct) =>
{
    var target = options.Mode == BrokerMode.CapitalDemo ? options.AccountId : null;
    var evidence = await new DemoReadOnlyProbe(factory.CreateCapitalDemo(target), clock).RunAsync(target, string.IsNullOrWhiteSpace(epic) ? "EURUSD" : epic, ct);
    await audit.LogAsync(new AuditEvent(ids.NewId("audit"), clock.UtcNow, "Demo", "TestConnection", "owner", "demo", null, null, null, JsonSerializer.Serialize(new { evidence.AllPassed }), true), ct);
    return Results.Ok(evidence);
});

api.MapGet("/demo/credential-status", async (ISecretStore secrets, CancellationToken ct) =>
{
    var key = await secrets.GetAsync("capital:Demo:api_key", ct);
    var id = await secrets.GetAsync("capital:Demo:identifier", ct);
    var pw = await secrets.GetAsync("capital:Demo:password", ct);
    return Results.Ok(new
    {
        apiKeyPresent = !string.IsNullOrWhiteSpace(key),
        identifierPresent = !string.IsNullOrWhiteSpace(id),
        passwordPresent = !string.IsNullOrWhiteSpace(pw)
    });
});

guarded.MapPost("/demo/configure-credentials", async (CredentialSetupRequest req, IPrivilegedCredentialManager mgr, IAuditLogger audit, IClock clock, IIdGenerator ids, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.ApiKey) || string.IsNullOrWhiteSpace(req.ApiPassword) || string.IsNullOrWhiteSpace(req.Identifier))
        return Results.BadRequest(new { error = "All fields required" });

    try
    {
        await mgr.ConfigureDemoCredentialsAsync(req.ApiKey, req.ApiPassword, req.Identifier, req.Replace, ct);
    }
    catch (InvalidOperationException)
    {
        return Results.Conflict(new { error = "Credentials could not be saved. Replacing existing credentials needs an explicit replace." });
    }
    await audit.LogAsync(new AuditEvent(ids.NewId("audit"), clock.UtcNow, "Demo", "ConfigureCredentials", "owner", "demo", null, null, null, "{\"status\":\"configured\"}", true), ct);
    return Results.Ok(new { status = "configured" });
});

api.MapGet("/health/feed", (IMarketDataService mds, string epic) => Results.Ok(new { epic, health = mds.GetFeedHealth(epic).ToString(), lastQuoteUtc = mds.LastQuoteUtc(epic) }));

app.Run();

public record MarketSnapshotRequest(string AccountId, string Epic, int SelectionVersion, string[]? Timeframes);
public record InstrumentSelectRequest(string AccountId, string Epic);
public record CredentialSetupRequest(string ApiKey, string ApiPassword, string Identifier, bool Replace = false);

public partial class Program { }
