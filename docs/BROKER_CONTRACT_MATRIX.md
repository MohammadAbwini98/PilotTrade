# Broker Contract Matrix

Source: https://open-api.capital.com/, plus deterministic mocks in `tests/PilotTrade.IntegrationTests/CapitalFake.cs`. "Real run" means the read-only probe runs on `09244cf`, with one account and three epics (see `phase3/DEMO_ACCEPTANCE_09244cf.md`). It shows the request succeeded and parsed, not that the values were correct, which waits for the owner's comparison.

| Endpoint | Method | Use | Status | Notes |
|---|---|---|---|---|
| /api/v1/session | POST | Log in | Real run | CST and X-SECURITY-TOKEN come from response headers and are sent per request. Limited to 1 request/s |
| /api/v1/session | GET | Prove the active account, quote time zone | Real run for the account; `timezoneOffset` mock tested | A mismatch is refused (mock tested). The meaning of `timezoneOffset` is inferred from documented samples |
| /api/v1/session | PUT | Switch account | Mock tested | Confirmed with GET afterwards |
| /api/v1/accounts | GET | List accounts, balances | Real run | No margin-used or realized-P&L field, so both stay null |
| /api/v1/positions | GET | Exposure | Real run, empty only | A broker failure is an error, never an empty list. Non-empty shape mock tested |
| /api/v1/workingorders | GET | Exposure | Real run, empty only | A broker failure is an error, never an empty list. Non-empty shape mock tested |
| /api/v1/markets?epics= | GET | Quotes | Real run: open on BTCUSD, closed on EURUSD and GOLD | Answers with `marketDetails`, not `markets`. Earlier mocks had this wrong. Stale or delayed quotes become NO_TRADE |
| /api/v1/markets?searchTerm= | GET | Search | Real run, 3 epics | Answers with `markets` |
| /api/v1/markets/{epic} | GET | Metadata, dealing rules | Real run, 3 epics | The documentation's sample confirms the nesting of `marketOrderPreference` |
| /api/v1/prices/{epic} | GET | Candles | Real run, 5m and 1h | Timestamps, ordering, values and missing bars checked on real data. Open-market freshness proven on BTCUSD only |
| /api/v1/positions, /workingorders | POST | Orders | Disabled | Payload contract tested in isolation; the adapter refuses to submit |
| /api/v1/confirms/{dealReference} | GET | Order outcome | Mock only | Keeps all affected deals; never resubmits. Only a referenced acceptance or an explicit rejection is a known outcome, so an acceptance without a deal ID, a rejection with affected deals, or an unrecognized status stays Unknown and is reconciled |

## Limits applied

10 requests/s overall, 1 request/s for `POST /session`, 10-minute idle session expiry. Documented Demo order limit: 1000/hour.
