namespace PilotTrade.Application.Services;

using PilotTrade.Domain;
using PilotTrade.Domain.Broker;
using PilotTrade.Domain.Market;

public sealed class InstrumentCatalogService : IInstrumentCatalog
{
    private readonly IBrokerAdapter _broker;
    public InstrumentCatalogService(IBrokerAdapter broker) { _broker = broker; }

    public Task<IReadOnlyList<InstrumentSummary>> SearchAsync(string query, int limit, CancellationToken ct)
        => _broker.SearchInstrumentsAsync(query, limit <= 0 ? 50 : limit, ct);

    public Task<InstrumentMetadata?> ResolveAsync(string epic, string accountId, TradingEnvironment env, CancellationToken ct)
        => _broker.GetInstrumentAsync(epic, accountId, ct);
}