namespace PilotTrade.Application.Services;

using PilotTrade.Domain;

// One wording for the runtime mode wherever the server describes it. The simulator "executes" against itself, which is never
// Capital.com order permission: broker execution is reported only for the Capital.com Demo adapter, and Live stays locked.
public static class RuntimeLabels
{
    public static string Mode(BrokerMode mode) => mode == BrokerMode.CapitalDemo ? "Capital.com Demo" : "Simulation";

    public static bool BrokerExecutionEnabled(BrokerMode mode, bool adapterExecutionEnabled) => mode == BrokerMode.CapitalDemo && adapterExecutionEnabled;

    public static bool SimulatedExecution(BrokerMode mode, bool adapterExecutionEnabled) => mode == BrokerMode.Simulation && adapterExecutionEnabled;

    public static string Execution(BrokerMode mode, bool adapterExecutionEnabled) => mode switch
    {
        BrokerMode.CapitalDemo when adapterExecutionEnabled => "Capital.com Demo order submission is enabled on the server",
        BrokerMode.CapitalDemo => "Capital.com Demo order submission disabled (read-only)",
        _ when adapterExecutionEnabled => "Simulated fills only. No Capital.com orders are sent.",
        _ => "Simulated execution disabled. No Capital.com orders are sent.",
    };

    public const string Live = "Live locked";
}
