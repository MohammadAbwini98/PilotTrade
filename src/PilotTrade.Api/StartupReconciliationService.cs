namespace PilotTrade.Api;

using Microsoft.Extensions.Hosting;
using PilotTrade.Application.Controls;
using PilotTrade.Domain;
using PilotTrade.Domain.Broker;
using PilotTrade.Domain.Execution;

public sealed class StartupReconciliationService : IHostedService
{
    private readonly ITradingControl _control;
    private readonly IOrderJournal _journal;
    private readonly IBrokerAdapter _broker;
    public StartupReconciliationService(ITradingControl control, IOrderJournal journal, IBrokerAdapter broker)
    { _control = control; _journal = journal; _broker = broker; }

    public async Task StartAsync(CancellationToken ct)
    {
        _control.State.ReconciliationComplete = false;
        try
        {
            var uncertain = await _journal.ListUncertainAsync(_broker.Environment, "SIM-ACC", ct);
            if (uncertain.Count > 0)
            {
                _control.State.SuspensionReason = $"Startup: {uncertain.Count} uncertain orders require reconciliation";
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