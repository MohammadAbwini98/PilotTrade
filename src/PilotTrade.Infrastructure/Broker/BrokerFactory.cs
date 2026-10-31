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
    private readonly BrokerRuntimeOptions _options;

    public BrokerAdapterFactory(ISecretStore secrets, IHttpClientFactory http, IClock clock, IIdGenerator ids, BrokerRuntimeOptions options)
    { _secrets = secrets; _http = http; _clock = clock; _ids = ids; _options = options; }

    public IBrokerAdapter Create(TradingEnvironment environment)
    {
        if (environment != TradingEnvironment.Demo) throw new InvalidOperationException("Live trading is locked.");
        return _options.Mode == BrokerMode.CapitalDemo
            ? CreateCapitalDemo(_options.AccountId)
            : new SimulatedBrokerAdapter(environment, _clock, _ids);
    }

    public CapitalComBrokerAdapter CreateCapitalDemo(string? accountId) => new(
        new BrokerEnvironmentConfig(TradingEnvironment.Demo, BrokerRuntimeOptions.CapitalDemoBaseUri, accountId, false),
        _secrets, _http, _clock, _ids);
}
