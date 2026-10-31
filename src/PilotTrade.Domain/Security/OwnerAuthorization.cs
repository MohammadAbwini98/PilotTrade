namespace PilotTrade.Domain.Security;

public interface IOwnerAuthorizationService
{
    Task<string> EnsureOwnerTokenAsync(CancellationToken ct);
    Task<bool> AuthorizeAsync(string ownerToken, string csrfToken, CancellationToken ct);
}