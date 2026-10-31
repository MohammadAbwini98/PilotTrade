namespace PilotTrade.Domain.Execution;

using PilotTrade.Domain;

public sealed record OrderJournalEntry(
    string CommandId,
    string IntentId,
    TradingEnvironment Environment,
    string AccountId,
    string Epic,
    OrderState State,
    string RequestDigest,
    string? DecisionSnapshotId,
    string? RiskAuthorizationJson,
    string? BrokerDealReference,
    string? BrokerDealId,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? SubmittedUtc,
    DateTimeOffset? ConfirmedUtc,
    string? RejectionReason,
    DateTimeOffset UpdatedUtc,
    string? CorrelationId);

public interface IOrderJournal
{
    Task<OrderJournalEntry> CreateAsync(OrderJournalEntry entry, CancellationToken ct);
    Task UpdateAsync(OrderJournalEntry entry, CancellationToken ct);
    Task<OrderJournalEntry?> GetAsync(string commandId, CancellationToken ct);
    Task<OrderJournalEntry?> FindByIntentAsync(string intentId, CancellationToken ct);
    Task<IReadOnlyList<OrderJournalEntry>> ListUncertainAsync(TradingEnvironment env, string accountId, CancellationToken ct);
}