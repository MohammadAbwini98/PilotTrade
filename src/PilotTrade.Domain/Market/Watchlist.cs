namespace PilotTrade.Domain.Market;

using System.Text.RegularExpressions;
using PilotTrade.Domain;

// The owner's list of instruments to monitor, per account and environment. Monitoring never selects, signals or trades.
public sealed record Watchlist(string AccountId, TradingEnvironment Environment, IReadOnlyList<string> Epics, int Version, DateTimeOffset? UpdatedUtc, string? UpdatedBy)
{
    public static Watchlist Empty(string accountId, TradingEnvironment env) => new(accountId, env, Array.Empty<string>(), 0, null, null);
}

public static partial class WatchlistRules
{
    // Each refresh asks the broker for every item's metadata, so the list is kept short.
    public const int MaxItems = 10;

    [GeneratedRegex("^[A-Za-z0-9._-]{1,40}$")]
    private static partial Regex EpicPattern();

    // Returns the problem with a requested list, or null when it can be stored.
    public static string? Problem(IReadOnlyList<string>? epics)
    {
        if (epics is null) return "The list is missing.";
        if (epics.Count > MaxItems) return $"At most {MaxItems} instruments.";
        if (epics.Any(e => e is null || !EpicPattern().IsMatch(e))) return "An epic is not valid.";
        if (epics.Distinct(StringComparer.Ordinal).Count() != epics.Count) return "An epic is listed twice.";
        return null;
    }
}

public sealed class WatchlistConflictException : Exception
{
    public Watchlist Current { get; }
    public WatchlistConflictException(Watchlist current) : base("The watchlist was changed elsewhere.") { Current = current; }
}

public interface IWatchlistStore
{
    Watchlist Load(string accountId, TradingEnvironment env);

    // Stores the list and its audit event together, or neither. Throws WatchlistConflictException when expectedVersion is not the stored one.
    Watchlist Save(string accountId, TradingEnvironment env, IReadOnlyList<string> epics, int expectedVersion, string updatedBy, DateTimeOffset nowUtc, string auditId);
}
