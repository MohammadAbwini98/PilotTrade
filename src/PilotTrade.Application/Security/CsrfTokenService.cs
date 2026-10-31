namespace PilotTrade.Application.Security;

public interface ICsrfTokenService
{
    string IssueToken();
    bool Validate(string token);
}

public sealed class InMemoryCsrfTokenService : ICsrfTokenService
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> _tokens = new();
    public string IssueToken()
    {
        var t = Guid.NewGuid().ToString("N");
        _tokens[t] = DateTimeOffset.UtcNow.AddMinutes(10);
        return t;
    }
    public bool Validate(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        if (_tokens.TryGetValue(token, out var exp) && exp > DateTimeOffset.UtcNow)
        {
            _tokens.TryRemove(token, out _);
            return true;
        }
        return false;
    }
}