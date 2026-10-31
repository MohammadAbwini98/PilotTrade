namespace PilotTrade.Infrastructure.Security;

using System.Security.Cryptography;
using System.Text;
using PilotTrade.Domain.Security;

public sealed class OwnerAuthorizationService : IOwnerAuthorizationService
{
    private const string TokenKey = "pilottrade:owner:token";
    private readonly ISecretStore _secrets;
    public OwnerAuthorizationService(ISecretStore secrets) { _secrets = secrets; }

    // The token stays in the OS store. Show it on demand with: PilotTrade.Api --show-owner-token
    public async Task<string> EnsureOwnerTokenAsync(CancellationToken ct)
    {
        var existing = await _secrets.GetAsync(TokenKey, ct);
        if (!string.IsNullOrWhiteSpace(existing)) return existing;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await _secrets.SetAsync(TokenKey, token, ct);
        return token;
    }

    public async Task<bool> AuthorizeAsync(string ownerToken, string csrfToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerToken)) return false;
        string? stored;
        try { stored = await _secrets.GetAsync(TokenKey, ct); }
        catch (PlatformNotSupportedException) { return false; }
        if (string.IsNullOrWhiteSpace(stored)) return false;
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(stored)), SHA256.HashData(Encoding.UTF8.GetBytes(ownerToken)));
    }
}
