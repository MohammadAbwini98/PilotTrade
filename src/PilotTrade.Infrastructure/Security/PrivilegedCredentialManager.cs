namespace PilotTrade.Infrastructure.Security;

using PilotTrade.Domain.Security;

public sealed class PrivilegedCredentialManager : IPrivilegedCredentialManager
{
    private readonly ISecretStore _privilegedStore;
    private readonly IOwnerAuthorizationService _auth;
    public PrivilegedCredentialManager(ISecretStore privilegedStore, IOwnerAuthorizationService auth)
    { _privilegedStore = privilegedStore; _auth = auth; }

    private async Task EnsureAuthorizedAsync(string ownerToken, string csrfToken, CancellationToken ct)
    {
        if (!await _auth.AuthorizeAsync(ownerToken, csrfToken, ct))
            throw new UnauthorizedAccessException("Owner authorization failed.");
    }

    public async Task ConfigureDemoCredentialsAsync(string apiKey, string apiPassword, string identifier, bool replace, CancellationToken ct)
    {
        var existing = await _privilegedStore.GetAsync("capital:Demo:api_key", ct);
        if (!string.IsNullOrWhiteSpace(existing) && !replace)
            throw new InvalidOperationException("Credentials already configured. Explicit replace required.");
        await _privilegedStore.SetAsync("capital:Demo:api_key", apiKey, ct);
        await _privilegedStore.SetAsync("capital:Demo:password", apiPassword, ct);
        await _privilegedStore.SetAsync("capital:Demo:identifier", identifier, ct);
    }

    public async Task DeleteDemoCredentialsAsync(CancellationToken ct)
    {
        await _privilegedStore.DeleteAsync("capital:Demo:api_key", ct);
        await _privilegedStore.DeleteAsync("capital:Demo:password", ct);
        await _privilegedStore.DeleteAsync("capital:Demo:identifier", ct);
    }
}