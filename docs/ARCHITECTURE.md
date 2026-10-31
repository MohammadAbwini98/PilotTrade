# Architecture

Modules: Domain, Application, Infrastructure, Api, Web. SQLite persistence. Loopback-only service.

## Broker

- `BrokerRuntimeOptions` parses explicit configuration. Simulation is the default, Capital.com Demo needs `BROKER_MODE=capital_demo` and an account, and Live stops startup.
- `BrokerAdapterFactory` creates `SimulatedBrokerAdapter` or `CapitalComBrokerAdapter`.
- `CapitalComBrokerAdapter` proves the active account before account-scoped reads, sends session tokens per request, applies rate limits, and retries only safe reads. Typed `BrokerErrors` separate a failure from genuinely empty data.
- `DemoReadOnlyProbe` produces sanitized evidence and uses no order endpoint.

## Decisions and execution

- `InstrumentSelectionService` versions the selected instrument per account. A changed version outdates snapshots and Claude and JEV results. Gemini is not used.
- `MarketDataService` validates quotes and candles. Anything missing, stale or invalid is NO_TRADE.
- `RiskEngine` fails closed on null financial values. Claude and JEV are analysis only.
- `ExecutionGateway` is the sole execution path, with a durable journal and kill switch. An uncertain outcome is never resubmitted.
- `OrderReconciliationService` runs at startup and engages the kill switch if anything stays uncertain.

## API and UI

- `ApiSecurity` enforces owner authorization, strict Origin parsing, CSRF and sanitized broker errors.
- The React UI builds into `src/PilotTrade.Api/wwwroot`, which the API serves and publishes.
