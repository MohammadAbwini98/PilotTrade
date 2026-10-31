# Broker Contract Matrix

Source: https://open-api.capital.com/, plus deterministic mocks in `tests/PilotTrade.IntegrationTests/CapitalFake.cs`. No row has been checked against the real API yet.

| Endpoint | Method | Use | Status | Notes |
|---|---|---|---|---|
| /api/v1/session | POST | Log in | Mock tested | CST and X-SECURITY-TOKEN come from response headers and are sent per request. Limited to 1 request/s |
| /api/v1/session | GET | Prove the active account | Mock tested | A mismatch is refused |
| /api/v1/session | PUT | Switch account | Mock tested | Confirmed with GET afterwards |
| /api/v1/accounts | GET | List accounts, balances | Mock tested | No margin-used or realized-P&L field, so both stay null |
| /api/v1/positions | GET | Exposure | Mock tested | A broker failure is an error, never an empty list |
| /api/v1/workingorders | GET | Exposure | Mock tested | A broker failure is an error, never an empty list |
| /api/v1/markets | GET | Search, quotes | Mock tested | Stale or delayed quotes become NO_TRADE |
| /api/v1/markets/{epic} | GET | Metadata, dealing rules | Mock tested | Confirm the nesting of `marketOrderPreference` in the evidence run |
| /api/v1/prices/{epic} | GET | Candles | Mock tested | Timestamps, ordering and values validated |
| /api/v1/positions, /workingorders | POST | Orders | Disabled | Payload contract tested in isolation; the adapter refuses to submit |
| /api/v1/confirms/{dealReference} | GET | Order outcome | Mock only | Handles multiple affected deals; never resubmits |

## Limits applied

10 requests/s overall, 1 request/s for `POST /session`, 10-minute idle session expiry. Documented Demo order limit: 1000/hour.
