namespace PilotTrade.Domain;

public enum TradingEnvironment { Demo, Live }

public enum TradingMode { MonitorOnly, DemoAutomated, LiveLocked, LiveArmed, Suspended }

public enum InstrumentAssetClass { Unknown, Fx, CryptoCfd, Commodity, Index, Share, Etf, Other }

public enum MarketStatus { Unknown, Open, Closed, PreMarket, PostMarket, Halted, Maintenance }

public enum OrderSide { Buy, Sell }

public enum OrderType { Market, Limit, Stop, StopLimit }

public enum OrderState
{
    Proposed, Validated, RiskApproved, Previewed, Submitting,
    BrokerPending, Confirmed, Rejected, Unknown, Reconciled, Cancelled
}

public enum DecisionCandidate { Buy, Sell, Hold, NoTrade }

public enum SignalOutcome { Approved, Rejected, NoTrade }

public enum RiskDecision { Approved, Rejected }

public enum FeedHealth { Healthy, Stale, Disconnected, Unknown }

public enum AuthorizationState { Locked, Armed, Expired, Revoked }

public enum BrokerMode { Simulation, CapitalDemo }

public enum DistanceUnit { Points, Percentage }
