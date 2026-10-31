namespace PilotTrade.Domain.Security;

public interface ISecretStore
{
    Task SetAsync(string key, string value, CancellationToken ct);
    Task<string?> GetAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}