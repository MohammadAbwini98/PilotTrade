namespace PilotTrade.Application.Security;

using System.Security.Cryptography;
using PilotTrade.Domain;

public interface ICsrfTokenService
{
    string IssueToken();
    bool Validate(string token);
}

public sealed class InMemoryCsrfTokenService : ICsrfTokenService
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> _tokens = new();
    private readonly IClock _clock;
    public InMemoryCsrfTokenService(IClock clock) { _clock = clock; }

    public string IssueToken()
    {
        var now = _clock.UtcNow;
        foreach (var expired in _tokens.Where(t => t.Value <= now)) _tokens.TryRemove(expired.Key, out _);
        var t = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _tokens[t] = now.AddMinutes(10);
        return t;
    }

    // Single use: removal decides the winner if the same token is presented twice at once.
    public bool Validate(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        return _tokens.TryRemove(token, out var exp) && exp > _clock.UtcNow;
    }
}
