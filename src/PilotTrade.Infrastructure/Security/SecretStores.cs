namespace PilotTrade.Infrastructure.Security;

using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using PilotTrade.Domain.Security;

public sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _store = new();
    public Task SetAsync(string key, string value, CancellationToken ct) { _store[key] = value; return Task.CompletedTask; }
    public Task<string?> GetAsync(string key, CancellationToken ct) => Task.FromResult(_store.TryGetValue(key, out var v) ? v : null);
    public Task DeleteAsync(string key, CancellationToken ct) { _store.Remove(key); return Task.CompletedTask; }
}

// Fails closed: credentials are never held in memory or plaintext on an OS without a supported protected store.
public sealed class UnsupportedPlatformSecretStore : ISecretStore
{
    private static PlatformNotSupportedException Error() => new("No OS-protected credential store is available on this platform.");
    public Task SetAsync(string key, string value, CancellationToken ct) => throw Error();
    public Task<string?> GetAsync(string key, CancellationToken ct) => throw Error();
    public Task DeleteAsync(string key, CancellationToken ct) => throw Error();
}

public static class PlatformSecretStores
{
    // Configuration and retrieval both use this, so they always reference the same protected storage.
    public static ISecretStore Create(bool allowProtectedMutations)
    {
        if (OperatingSystem.IsWindows()) return new WindowsCredentialManagerSecretStore(allowProtectedMutations);
        if (OperatingSystem.IsMacOS()) return new MacOsKeychainSecretStore(allowProtectedMutations);
        return new UnsupportedPlatformSecretStore();
    }
}

internal static class CredentialTargets
{
    public static bool IsProtected(string target) =>
        target.StartsWith("CapitalTrading/Demo/", StringComparison.OrdinalIgnoreCase) ||
        target.StartsWith("CapitalTrading/Live/", StringComparison.OrdinalIgnoreCase);

    public static string For(string key) => key switch
    {
        "capital:Demo:api_key" => "CapitalTrading/Demo/ApiKey",
        "capital:Demo:password" => "CapitalTrading/Demo/ApiPassword",
        "capital:Demo:identifier" => "CapitalTrading/Demo/Identifier",
        "capital:Live:api_key" => "CapitalTrading/Live/ApiKey",
        "capital:Live:password" => "CapitalTrading/Live/ApiPassword",
        "capital:Live:identifier" => "CapitalTrading/Live/Identifier",
        _ => key
    };
}

[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _dir;
    public DpapiSecretStore(string dir) { _dir = dir; Directory.CreateDirectory(dir); }
    private string PathFor(string key) => System.IO.Path.Combine(_dir, Convert.ToBase64String(Encoding.UTF8.GetBytes(key)).Replace("/", "_").Replace("+", "-") + ".bin");

    public Task SetAsync(string key, string value, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(PathFor(key), protectedBytes);
        return Task.CompletedTask;
    }
    public Task<string?> GetAsync(string key, CancellationToken ct)
    {
        var p = PathFor(key);
        if (!File.Exists(p)) return Task.FromResult<string?>(null);
        var protectedBytes = File.ReadAllBytes(p);
        var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes));
    }
    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var p = PathFor(key);
        if (File.Exists(p)) File.Delete(p);
        return Task.CompletedTask;
    }
}
