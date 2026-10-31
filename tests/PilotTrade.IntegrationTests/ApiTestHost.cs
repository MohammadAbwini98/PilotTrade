namespace PilotTrade.IntegrationTests;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Runs the real API against in-memory secret stores, a temporary database and the Capital.com fake, so a test can
// never read a real credential, change the real owner token or reach the network.
public sealed class ApiTestHost : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pilottrade-test-{Guid.NewGuid():N}.db");
    private readonly Dictionary<string, string?> _settings;

    public InMemorySecretStore Secrets { get; } = new();
    public CapitalFake Capital { get; } = new(new SystemClock());
    public IPAddress RemoteAddress { get; set; } = IPAddress.Loopback;

    public ApiTestHost(string mode = "simulation", string environment = "demo", string? account = null)
    {
        _settings = new()
        {
            ["ConnectionStrings:Default"] = $"Data Source={_dbPath}",
            ["BROKER_MODE"] = mode,
            ["TRADING_ENVIRONMENT"] = environment,
            ["TRADING_ACCOUNT_ID"] = account
        };
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(c => c.AddInMemoryCollection(_settings));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ISecretStore>(Secrets);
            services.AddKeyedSingleton<ISecretStore>("privileged", Secrets);
            services.AddHttpClient("capital").ConfigurePrimaryHttpMessageHandler(() => Capital);
            services.AddSingleton<IStartupFilter>(new RemoteAddressFilter(this));
        });
    }

    public async Task StoreDemoCredentialsAsync()
    {
        await Secrets.SetAsync("capital:Demo:api_key", "TEST-KEY", CancellationToken.None);
        await Secrets.SetAsync("capital:Demo:identifier", "TEST-USER", CancellationToken.None);
        await Secrets.SetAsync("capital:Demo:password", "TEST-PASS", CancellationToken.None);
    }

    public async Task<HttpClient> OwnerClientAsync()
    {
        var client = CreateClient();
        var token = await Services.GetRequiredService<IOwnerAuthorizationService>().EnsureOwnerTokenAsync(CancellationToken.None);
        client.DefaultRequestHeaders.Add("X-PilotTrade-Owner-Token", token);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    private sealed class RemoteAddressFilter : IStartupFilter
    {
        private readonly ApiTestHost _host;
        public RemoteAddressFilter(ApiTestHost host) { _host = host; }
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nextMiddleware) => { ctx.Connection.RemoteIpAddress = _host.RemoteAddress; return nextMiddleware(); });
            next(app);
        };
    }
}

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Windows only"; }
}

public sealed class MacFactAttribute : FactAttribute
{
    public MacFactAttribute() { if (!OperatingSystem.IsMacOS()) Skip = "macOS only"; }
}
