namespace PilotTrade.Infrastructure.Security;

using PilotTrade.Domain.Security;

public sealed class OwnerAuthorizationService : IOwnerAuthorizationService
{
    private readonly ISecretStore _secrets;
    public OwnerAuthorizationService(ISecretStore secrets) { _secrets = secrets; }

    public async Task<string> EnsureOwnerTokenAsync(CancellationToken ct)
    {
        var existing = await _secrets.GetAsync("pilottrade:owner:token", ct);
        if (!string.IsNullOrWhiteSpace(existing)) return existing;
        var token = Guid.NewGuid().ToString("N");
        await _secrets.SetAsync("pilottrade:owner:token", token, ct);
        Console.WriteLine($"PILOTTRADE OWNER TOKEN (store securely): {token}");
        return token;
    }

    public async Task<bool> AuthorizeAsync(string ownerToken, string csrfToken, CancellationToken ct)
    {
        var stored = await _secrets.GetAsync("pilottrade:owner:token", ct);
        return !string.IsNullOrWhiteSpace(stored) && string.Equals(stored, ownerToken, StringComparison.Ordinal);
    }
}