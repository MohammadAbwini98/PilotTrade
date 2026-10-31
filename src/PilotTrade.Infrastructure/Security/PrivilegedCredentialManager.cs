namespace PilotTrade.Infrastructure.Security;

using PilotTrade.Domain.Security;

// Callers are owner-authorized at the API boundary (see ApiSecurity.RequireOwner).
public sealed class PrivilegedCredentialManager : IPrivilegedCredentialManager
{
    private static readonly string[] DemoKeys = { "capital:Demo:api_key", "capital:Demo:password", "capital:Demo:identifier" };
    private readonly ISecretStore _privilegedStore;
    public PrivilegedCredentialManager(ISecretStore privilegedStore) { _privilegedStore = privilegedStore; }

    public async Task ConfigureDemoCredentialsAsync(string apiKey, string apiPassword, string identifier, bool replace, CancellationToken ct)
    {
        var previous = new Dictionary<string, string?>();
        foreach (var key in DemoKeys) previous[key] = await _privilegedStore.GetAsync(key, ct);
        if (previous.Values.Any(v => !string.IsNullOrWhiteSpace(v)) && !replace)
            throw new InvalidOperationException("Credentials already configured. Explicit replace required.");

        var values = new[] { apiKey, apiPassword, identifier };
        try
        {
            for (var i = 0; i < DemoKeys.Length; i++) await _privilegedStore.SetAsync(DemoKeys[i], values[i], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Three separate writes: restore the previous set so a key can never pair with another key's password.
            foreach (var (key, value) in previous)
            {
                try
                {
                    if (value is null) await _privilegedStore.DeleteAsync(key, CancellationToken.None);
                    else await _privilegedStore.SetAsync(key, value, CancellationToken.None);
                }
                catch (Exception restore) when (restore is not OperationCanceledException) { }
            }
            throw new InvalidOperationException("Credentials could not be saved.");
        }
    }

    public async Task DeleteDemoCredentialsAsync(CancellationToken ct)
    {
        foreach (var key in DemoKeys) await _privilegedStore.DeleteAsync(key, ct);
    }
}
