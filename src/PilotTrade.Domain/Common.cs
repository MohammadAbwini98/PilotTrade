namespace PilotTrade.Domain;

public interface IClock { DateTimeOffset UtcNow { get; } }
public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

public interface IIdGenerator { string NewId(string prefix); }
public sealed class GuidIdGenerator : IIdGenerator { public string NewId(string prefix) => $"{prefix}_{Guid.NewGuid():N}"; }

public readonly record struct Money(decimal Amount, string Currency)
{
    public Money Add(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Currency mismatch {Currency} vs {other.Currency}");
        return new(Amount + other.Amount, Currency);
    }
}

public readonly record struct Price(decimal Value, string Currency, int Precision);

public readonly record struct Quantity(decimal Units, string UnitType);

public sealed record AuditEvent(
    string Id,
    DateTimeOffset OccurredUtc,
    string Category,
    string Action,
    string Actor,
    string Environment,
    string? AccountId,
    string? InstrumentEpic,
    string? CorrelationId,
    string DetailsJson,
    bool SensitiveRedacted);