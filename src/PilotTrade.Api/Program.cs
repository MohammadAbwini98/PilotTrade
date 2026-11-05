using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using PilotTrade.Api;
using PilotTrade.Application.Controls;
using PilotTrade.Application.Services;
using PilotTrade.Domain;
using PilotTrade.Domain.Audit;
using PilotTrade.Domain.Broker;
using PilotTrade.Domain.Execution;
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

var packaged = PortableLayout.IsPackaged(AppContext.BaseDirectory);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = packaged ? AppContext.BaseDirectory : null });

// Without an explicit address Kestrel would pick its own port. A packaged build stays on one known loopback address.
if (packaged && string.IsNullOrWhiteSpace(builder.Configuration["urls"])) builder.WebHost.UseUrls(PortableLayout.DefaultUrl);

builder.Services.AddDbContext<PilotTradeDbContext>((sp, o) =>
    o.UseSqlite(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")
        ?? (packaged ? PortableLayout.UserDatabaseConnection() : "Data Source=pilottrade.db")), ServiceLifetime.Singleton);

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
builder.Services.AddSingleton<IInstrumentSelectionStore, EfInstrumentSelectionStore>();
builder.Services.AddSingleton<IInstrumentSelectionService>(sp => new InstrumentSelectionService(
    sp.GetRequiredService<IClock>(), sp.GetRequiredService<IIdGenerator>(), sp.GetRequiredService<IInstrumentSelectionStore>()));
// One policy object, so the monitor shows exactly the limits the snapshot applies.
builder.Services.AddSingleton(MarketDataPolicy.Default);
builder.Services.AddSingleton<IMarketDataService>(sp => new MarketDataService(sp.GetRequiredService<IBrokerAdapter>(), sp.GetRequiredService<IClock>(), sp.GetRequiredService<MarketDataPolicy>()));
builder.Services.AddSingleton<IWatchlistStore, EfWatchlistStore>();
builder.Services.AddSingleton(sp => new MarketWatchService(sp.GetRequiredService<IBrokerAdapter>(), sp.GetRequiredService<IClock>(), sp.GetRequiredService<MarketDataPolicy>()));
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

// The journal and kill-switch state in an older database do not carry over by themselves, so this is said out loud.
if (packaged && app.Configuration.GetConnectionString("Default") is null
    && new[] { AppContext.BaseDirectory, Environment.CurrentDirectory }.Any(folder => File.Exists(Path.Combine(folder, "pilottrade.db"))))
    app.Logger.LogWarning("Found an older pilottrade.db that this build does not use. Its journal, audit log and kill-switch state do not apply here. README.txt says how to move it.");

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
    // The Development environment throws on a malformed body where other environments answer 400 directly.
    if (ctx.Features.Get<IExceptionHandlerFeature>()?.Error is BadHttpRequestException)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        return ctx.Response.WriteAsJsonAsync(new { error = "invalid_request" });
    }
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

// One account per run: a route never reads or selects for an account other than the one explicitly configured.
static IResult? RefuseAccount(BrokerRuntimeOptions options, string accountId) =>
    accountId == options.AccountId ? null : Results.Json(new { error = "AccountNotConfigured" }, statusCode: StatusCodes.Status403Forbidden);

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
    RefuseAccount(options, accountId) ?? (selections.Current(accountId, options.Environment) is { } s ? Results.Ok(s) : Results.NotFound()));

api.MapGet("/instruments/{epic}", async (string epic, string accountId, IBrokerAdapter broker, BrokerRuntimeOptions options, CancellationToken ct) =>
{
    if (RefuseAccount(options, accountId) is { } refused) return refused;
    var m = await broker.GetInstrumentAsync(epic, accountId, ct);
    return m is null ? Results.NotFound() : Results.Ok(m);
});

guarded.MapPost("/instruments/select", async (InstrumentSelectRequest req, IBrokerAdapter broker, BrokerRuntimeOptions options, IInstrumentSelectionService selections, CancellationToken ct) =>
{
    if (RefuseAccount(options, req.AccountId) is { } refused) return refused;
    var meta = await broker.GetInstrumentAsync(req.Epic, req.AccountId, ct);
    if (meta is null) return Results.NotFound(new { error = "NotFound" });
    return Results.Ok(new { selection = selections.Select(req.AccountId, options.Environment, req.Epic, "owner"), instrument = meta });
});

// Account
api.MapGet("/accounts", async (IBrokerAdapter broker, CancellationToken ct) => Results.Ok(await broker.GetAccountsAsync(ct)));

api.MapGet("/account", async (string accountId, IBrokerAdapter broker, BrokerRuntimeOptions options, CancellationToken ct) =>
    RefuseAccount(options, accountId) ?? Results.Ok(await broker.GetAccountAsync(accountId, ct)));

// Positions
api.MapGet("/positions", async (string accountId, IBrokerAdapter broker, BrokerRuntimeOptions options, CancellationToken ct) =>
    RefuseAccount(options, accountId) ?? Results.Ok(await broker.GetPositionsAsync(accountId, ct)));

api.MapGet("/orders", async (string accountId, IBrokerAdapter broker, BrokerRuntimeOptions options, CancellationToken ct) =>
    RefuseAccount(options, accountId) ?? Results.Ok(await broker.GetWorkingOrdersAsync(accountId, ct)));

// Market snapshot: any missing, invalid or outdated input is a NO_TRADE result, not a partial snapshot.
guarded.MapPost("/market/snapshot", async (MarketSnapshotRequest req, IMarketDataService mds, IInstrumentSelectionService selections, BrokerRuntimeOptions options, CancellationToken ct) =>
{
    if (RefuseAccount(options, req.AccountId) is { } refused) return refused;
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

// Task 02-03: watchlist and market monitor. Monitoring reads metadata and quotes only; it never selects, signals or orders.
api.MapGet("/watchlist", (string accountId, BrokerRuntimeOptions options, IWatchlistStore store) =>
    RefuseAccount(options, accountId) ?? Results.Ok(store.Load(accountId, options.Environment)));

guarded.MapPost("/watchlist", async (WatchlistSaveRequest req, BrokerRuntimeOptions options, IWatchlistStore store, IBrokerAdapter broker,
    IClock clock, IIdGenerator ids, CancellationToken ct) =>
{
    if (RefuseAccount(options, req.AccountId) is { } refused) return refused;
    if (WatchlistRules.Problem(req.Epics) is { } problem) return Results.BadRequest(new { error = "InvalidWatchlist", reason = problem });
    // Only instruments the broker knows for this account are stored.
    foreach (var epic in req.Epics!)
        if (await broker.GetInstrumentAsync(epic, req.AccountId, ct) is null)
            return Results.BadRequest(new { error = "InvalidWatchlist", reason = $"Unknown instrument: {epic}" });
    try { return Results.Ok(store.Save(req.AccountId, options.Environment, req.Epics!, req.ExpectedVersion, "owner", clock.UtcNow, ids.NewId("audit"))); }
    catch (WatchlistConflictException ex) { return Results.Json(new { error = "WatchlistChanged", current = ex.Current }, statusCode: StatusCodes.Status409Conflict); }
});

api.MapGet("/market/watch", async (string accountId, BrokerRuntimeOptions options, IWatchlistStore store, MarketWatchService watch, CancellationToken ct) =>
{
    if (RefuseAccount(options, accountId) is { } refused) return refused;
    var list = store.Load(accountId, options.Environment);
    var result = await watch.BuildAsync(accountId, list.Epics, ct);
    return Results.Ok(new { result.CheckedUtc, result.MaxQuoteAgeSeconds, watchlistVersion = list.Version, result.Rows });
});

// The limits the snapshot applies. They are built-in defaults; editing them is task 02-09.
api.MapGet("/market/policy", (MarketDataPolicy policy) => Results.Ok(new
{
    source = "Built-in defaults",
    editable = false,
    maxQuoteAgeSeconds = policy.MaxQuoteAge.TotalSeconds,
    minClosedCandles = policy.MinClosedCandles,
    maxCandleAgeInBars = policy.MaxCandleAgeInBars,
    maxSpreadPercent = policy.MaxSpreadPercent.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value)
}));

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

// Read-only evidence that no order left this app: the journal, stored intents and the broker adapter's own count of
// non-session write requests. Owner-only, and it changes nothing.
api.MapGet("/evidence/no-orders", async (string accountId, BrokerRuntimeOptions options, IBrokerAdapter broker, IOrderJournal journal,
    DbContextOptions<PilotTradeDbContext> dbOptions, IClock clock, CancellationToken ct) =>
{
    if (RefuseAccount(options, accountId) is { } refused) return refused;
    var entries = await journal.ListAsync(options.Environment, accountId, ct);
    await using var db = new PilotTradeDbContext(dbOptions);
    var env = options.Environment.ToString();
    var intents = await db.OrderIntents.CountAsync(x => x.AccountId == accountId && x.Environment == env, ct);
    var capital = broker as CapitalComBrokerAdapter;
    var submitted = entries.Count(e => e.SubmittedUtc is not null || e.BrokerDealReference is not null || e.BrokerDealId is not null);
    var writes = capital?.NonSessionWriteRequests;
    return Results.Ok(new
    {
        checkedUtc = clock.UtcNow,
        mode = options.Mode.ToString(),
        environment = env,
        accountId,
        adapter = capital is null ? "simulated" : "capital.com",
        executionEnabled = broker.ExecutionEnabled,
        liveLocked = true,
        journalEntries = entries.Count,
        journalEntriesSentToBroker = submitted,
        journalStates = entries.GroupBy(e => e.State.ToString()).ToDictionary(g => g.Key, g => g.Count()),
        orderIntents = intents,
        // Counted since this process started; the simulator sends nothing anywhere, so it has no such count.
        brokerWriteRequestsSinceStart = writes,
        noOrderSent = submitted == 0 && (writes ?? 0) == 0
    });
});

api.MapGet("/health/feed", (IMarketDataService mds, string epic) => Results.Ok(new { epic, health = mds.GetFeedHealth(epic).ToString(), lastQuoteUtc = mds.LastQuoteUtc(epic) }));

// Task 02-02: read-only operational status for the overview. Every value is the server's current state; nothing here can change it.
api.MapGet("/status", (string accountId, BrokerRuntimeOptions options, ITradingControl control, IInstrumentSelectionService selections,
    IMarketDataService mds, IClock clock) =>
{
    if (RefuseAccount(options, accountId) is { } refused) return refused;
    var state = control.State;
    var selected = selections.Current(accountId, options.Environment);
    return Results.Ok(new
    {
        checkedUtc = clock.UtcNow,
        killSwitchEngaged = state.KillSwitchEngaged,
        tradingMode = state.Mode.ToString(),
        reconciliationComplete = state.ReconciliationComplete,
        suspensionReason = state.SuspensionReason,
        // Feed health is only known for the selected instrument, and only once a quote has been requested for it.
        feed = selected is null ? null : new { epic = selected.Epic, health = mds.GetFeedHealth(selected.Epic).ToString(), lastQuoteUtc = mds.LastQuoteUtc(selected.Epic) }
    });
});

app.Run();

public record MarketSnapshotRequest(string AccountId, string Epic, int SelectionVersion, string[]? Timeframes);
public record InstrumentSelectRequest(string AccountId, string Epic);
public record WatchlistSaveRequest(string AccountId, string[]? Epics, int ExpectedVersion);
public record CredentialSetupRequest(string ApiKey, string ApiPassword, string Identifier, bool Replace = false);

public partial class Program { }
