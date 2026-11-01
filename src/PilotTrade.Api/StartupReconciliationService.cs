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

    public Task StartAsync(CancellationToken ct) => _reconciliation.RunStartupAsync(_control, _broker.Environment, _options.AccountId, ct);
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
