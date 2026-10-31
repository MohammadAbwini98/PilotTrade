namespace PilotTrade.Infrastructure.Security;

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