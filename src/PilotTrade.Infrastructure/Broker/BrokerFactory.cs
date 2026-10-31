namespace PilotTrade.Infrastructure.Broker;

using PilotTrade.Domain;
using PilotTrade.Domain.Broker;
using PilotTrade.Domain.Security;

public sealed class BrokerAdapterFactory : IBrokerSessionFactory
{
    private readonly ISecretStore _secrets;
    private readonly IHttpClientFactory _http;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;

    public BrokerAdapterFactory(ISecretStore secrets, IHttpClientFactory http, IClock clock, IIdGenerator ids)
    { _secrets = secrets; _http = http; _clock = clock; _ids = ids; }

    public IBrokerAdapter Create(TradingEnvironment environment)
    {
        // Default to simulator unless explicitly configured with real base URL secret? 
        // For safety, real adapter requires config; factory returns simulator by default.
        return new SimulatedBrokerAdapter(environment, _clock, _ids);
    }
}