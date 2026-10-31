namespace PilotTrade.Domain.Broker;

public enum BrokerErrorKind
{
    Authentication,
    RateLimited,
    Unavailable,
    MalformedResponse,
    AccountNotFound,
    AccountMismatch,
    NotFound,
    InvalidRequest,
    CredentialsMissing,
    ExecutionDisabled
}

// Messages are safe to show: they never carry broker bodies, tokens or credentials.
public sealed class BrokerException : Exception
{
    public BrokerErrorKind Kind { get; }
    public BrokerException(BrokerErrorKind kind, string safeMessage) : base(safeMessage) { Kind = kind; }
}

public sealed class MarketDataRejectedException : Exception
{
    public MarketDataRejectedException(string reason) : base(reason) { }
}

public sealed record BrokerRuntimeOptions(BrokerMode Mode, TradingEnvironment Environment, string AccountId)
{
    public const string CapitalDemoBaseUri = "https://demo-api-capital.backend-capital.com";
    public const string SimulationAccountId = "SIM-ACC";

    // Simulation is the default. The real Demo adapter needs an explicit mode and account; Live is refused outright.
    public static BrokerRuntimeOptions Parse(string? mode, string? environment, string? accountId)
    {
        if (!string.IsNullOrWhiteSpace(environment) && !environment.Equals("demo", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("TRADING_ENVIRONMENT must be 'demo'. Live trading is locked.");

        var parsed = (mode ?? "simulation").Trim().ToLowerInvariant() switch
        {
            "simulation" or "" => BrokerMode.Simulation,
            "capital_demo" => BrokerMode.CapitalDemo,
            _ => throw new InvalidOperationException("BROKER_MODE must be 'simulation' or 'capital_demo'.")
        };

        if (parsed == BrokerMode.CapitalDemo && string.IsNullOrWhiteSpace(accountId))
            throw new InvalidOperationException("BROKER_MODE=capital_demo requires TRADING_ACCOUNT_ID.");

        return new BrokerRuntimeOptions(parsed, TradingEnvironment.Demo,
            string.IsNullOrWhiteSpace(accountId) ? SimulationAccountId : accountId.Trim());
    }
}
