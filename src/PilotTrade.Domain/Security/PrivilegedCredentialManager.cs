namespace PilotTrade.Domain.Security;

public interface IPrivilegedCredentialManager
{
    Task ConfigureDemoCredentialsAsync(string apiKey, string apiPassword, string identifier, bool replace, CancellationToken ct);
    Task DeleteDemoCredentialsAsync(CancellationToken ct);
}