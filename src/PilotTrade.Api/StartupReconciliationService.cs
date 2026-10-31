namespace PilotTrade.Api;

using Microsoft.Extensions.Hosting;
using PilotTrade.Application.Controls;
using PilotTrade.Domain;
using PilotTrade.Domain.Broker;

public sealed class StartupReconciliationService : IHostedService
{
    private readonly ITradingControl _control;
    private readonly OrderReconciliationService _reconciliation;
    private readonly IBrokerAdapter _broker;
    private readonly BrokerRuntimeOptions _options;
    public StartupReconciliationService(ITradingControl control, OrderReconciliationService reconciliation, IBrokerAdapter broker, BrokerRuntimeOptions options)
    { _control = control; _reconciliation = reconciliation; _broker = broker; _options = options; }

    public async Task StartAsync(CancellationToken ct)
    {
        _control.State.ReconciliationComplete = false;
        try
        {
            var summary = await _reconciliation.ReconcileAsync(_broker.Environment, _options.AccountId, ct);
            if (summary.StillUncertain > 0)
            {
                _control.State.SuspensionReason = $"Startup: {summary.StillUncertain} uncertain orders require reconciliation";
                _control.EngageKillSwitch();
            }
            _control.State.ReconciliationComplete = true;
        }
        catch
        {
            _control.State.SuspensionReason = "Startup reconciliation failed";
            _control.EngageKillSwitch();
        }
    }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
