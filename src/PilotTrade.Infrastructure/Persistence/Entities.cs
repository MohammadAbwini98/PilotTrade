namespace PilotTrade.Infrastructure.Persistence;

public class BrokerEnvironmentEntity
{
    public string Id { get; set; } = "";
    public string Environment { get; set; } = "";
    public string ApiBaseUri { get; set; } = "";
    public string? AccountId { get; set; }
    public bool LiveExecutionEnabled { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}

public class InstrumentEntity
{
    public string Epic { get; set; } = "";
    public string Environment { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string AssetClass { get; set; } = "";
    public string QuoteCurrency { get; set; } = "";
    public int PricePrecision { get; set; }
    public decimal TickSize { get; set; }
    public decimal MinDealSize { get; set; }
    public decimal MaxDealSize { get; set; }
    public decimal DealSizeStep { get; set; }
    public DateTimeOffset RetrievedUtc { get; set; }
}

public class InstrumentSelectionEntity
{
    public string Id { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string Environment { get; set; } = "";
    public string Epic { get; set; } = "";
    public int SelectionVersion { get; set; }
    public DateTimeOffset SelectedUtc { get; set; }
    public string SelectedBy { get; set; } = "";
}

public class OrderIntentEntity
{
    public string IntentId { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string Environment { get; set; } = "";
    public string Epic { get; set; } = "";
    public string Side { get; set; } = "";
    public string Type { get; set; } = "";
    public decimal Size { get; set; }
    public decimal? LimitLevel { get; set; }
    public decimal? StopLevel { get; set; }
    public decimal? TakeProfitLevel { get; set; }
    public string? ClientReference { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public string SelectionVersion { get; set; } = "";
    public string SnapshotId { get; set; } = "";
    public string StrategyVersion { get; set; } = "";
    public string State { get; set; } = "";
    public string? BrokerDealReference { get; set; }
    public string? BrokerDealId { get; set; }
    public string? Error { get; set; }
}

public class AuditEventEntity
{
    public string Id { get; set; } = "";
    public DateTimeOffset OccurredUtc { get; set; }
    public string Category { get; set; } = "";
    public string Action { get; set; } = "";
    public string Actor { get; set; } = "";
    public string Environment { get; set; } = "";
    public string? AccountId { get; set; }
    public string? InstrumentEpic { get; set; }
    public string? CorrelationId { get; set; }
    public string DetailsJson { get; set; } = "";
    public bool SensitiveRedacted { get; set; }
}

public class DecisionEntity
{
    public string DecisionId { get; set; } = "";
    public string SnapshotId { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string Environment { get; set; } = "";
    public string Epic { get; set; } = "";
    public int SelectionVersion { get; set; }
    public string StrategyId { get; set; } = "";
    public string StrategyVersion { get; set; } = "";
    public string Candidate { get; set; } = "";
    public string Explanation { get; set; } = "";
    public string ChecksJson { get; set; } = "";
    public DateTimeOffset DecidedUtc { get; set; }
    public string? ClaudeAnalysisId { get; set; }
    public string? JevEvaluationId { get; set; }
    public string? RejectionReason { get; set; }
}

public class ClaudeAnalysisEntity
{
    public string AnalysisId { get; set; } = "";
    public string SnapshotId { get; set; } = "";
    public string Epic { get; set; } = "";
    public string Environment { get; set; } = "";
    public int SelectionVersion { get; set; }
    public string ModelId { get; set; } = "";
    public string ModelVersion { get; set; } = "";
    public string Signal { get; set; } = "";
    public string MarketRegime { get; set; } = "";
    public string EvidenceJson { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public DateTimeOffset ProducedUtc { get; set; }
}

public class JevEvaluationEntity
{
    public string EvaluationId { get; set; } = "";
    public string SnapshotId { get; set; } = "";
    public string ModelId { get; set; } = "";
    public string ModelVersion { get; set; } = "";
    public string AnswersJson { get; set; } = "";
    public DateTimeOffset ProducedUtc { get; set; }
}