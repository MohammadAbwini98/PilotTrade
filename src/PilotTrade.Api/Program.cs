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

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PilotTradeDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=pilottrade.db"), ServiceLifetime.Singleton);

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IIdGenerator, GuidIdGenerator>();
builder.Services.AddSingleton<ISecretStore>(_ => OperatingSystem.IsWindows()
    ? new DpapiSecretStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PilotTrade", "secrets"))
    : new InMemorySecretStore());
builder.Services.AddSingleton<ITradingControl, DefaultTradingControl>();
builder.Services.AddSingleton<IBrokerSessionFactory, BrokerAdapterFactory>();
builder.Services.AddSingleton<IBrokerAdapter>(sp =>
{
    var factory = sp.GetRequiredService<IBrokerSessionFactory>();
    var envStr = builder.Configuration["TRADING_ENVIRONMENT"] ?? "demo";
    var env = envStr.Equals("live", StringComparison.OrdinalIgnoreCase) ? TradingEnvironment.Live : TradingEnvironment.Demo;
    return factory.Create(env);
});
builder.Services.AddSingleton<IInstrumentCatalog>(sp => new InstrumentCatalogService(sp.GetRequiredService<IBrokerAdapter>()));
builder.Services.AddSingleton<IMarketDataService>(sp => new MarketDataService(sp.GetRequiredService<IBrokerAdapter>(), sp.GetRequiredService<IClock>()));
builder.Services.AddSingleton<IRiskEngine, RiskEngine>();
builder.Services.AddSingleton<IDecisionCoordinator, DecisionCoordinator>();
builder.Services.AddSingleton<IPositionGuardian>(sp => new PositionGuardian(sp.GetRequiredService<IBrokerAdapter>(), sp.GetRequiredService<IClock>()));
builder.Services.AddSingleton<IAuditLogger, EfAuditLogger>();
builder.Services.AddHttpClient("capital");

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173").AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PilotTradeDbContext>();
    db.Database.EnsureCreated();
}

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTimeOffset.UtcNow }));

// Instruments
app.MapGet("/api/instruments/search", async (string q, int? limit, IInstrumentCatalog catalog, CancellationToken ct) =>
    Results.Ok(await catalog.SearchAsync(q ?? "", limit ?? 50, ct)));

app.MapGet("/api/instruments/{epic}", async (string epic, string accountId, TradingEnvironment? env, IBrokerAdapter broker, CancellationToken ct) =>
{
    var e = env ?? TradingEnvironment.Demo;
    var m = await broker.GetInstrumentAsync(epic, accountId, ct);
    return m is null ? Results.NotFound() : Results.Ok(m);
});

// Account
app.MapGet("/api/account", async (string accountId, IBrokerAdapter broker, CancellationToken ct) =>
    Results.Ok(await broker.GetAccountAsync(accountId, ct)));

// Positions
app.MapGet("/api/positions", async (string accountId, IBrokerAdapter broker, CancellationToken ct) =>
    Results.Ok(await broker.GetPositionsAsync(accountId, ct)));

app.MapGet("/api/orders", async (string accountId, IBrokerAdapter broker, CancellationToken ct) =>
    Results.Ok(await broker.GetWorkingOrdersAsync(accountId, ct)));

// Market snapshot
app.MapPost("/api/market/snapshot", async (MarketSnapshotRequest req, IMarketDataService mds, CancellationToken ct) =>
{
    try
    {
        var bundle = await mds.BuildSnapshotAsync(req.AccountId, req.Environment, req.Epic, req.SelectionVersion, req.Timeframes ?? new[] { "5m", "1h" }, ct);
        return Results.Ok(bundle);
    }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// Risk preview endpoint (placeholder)
app.MapGet("/api/health/feed", (IMarketDataService mds, string epic) => Results.Ok(new { epic, health = mds.GetFeedHealth(epic).ToString() }));

app.Run();

public record MarketSnapshotRequest(string AccountId, TradingEnvironment Environment, string Epic, int SelectionVersion, string[]? Timeframes);

public class DefaultTradingControl : ITradingControl
{
    public TradingControlState State { get; } = new();
    public void EngageKillSwitch() => State.KillSwitchEngaged = true;
    public void DisarmLive() { State.LiveArmed = false; State.LiveArmedUntilUtc = null; }
}