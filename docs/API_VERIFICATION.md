# API Verification

Reviewed 2026-10-31 from documentation and mocks. **No authenticated result has been recorded:** the owner's first evidence run on 2026-10-31 crashed on its output path after the probe, losing the results. This file records no real broker validation until sanitized evidence exists.

## Capital.com
- Demo base: https://demo-api-capital.backend-capital.com
- Live base: https://api-capital.backend-capital.com (never contacted; Live is locked)
- Contract: see `BROKER_CONTRACT_MATRIX.md`.
- Evidence: not yet recorded. See `DEMO_RELEASE_GO_NO_GO.md`.

## Anthropic Claude
- Messages API: POST https://api.anthropic.com/v1/messages with x-api-key and anthropic-version.
- The model ID is pinned through ANTHROPIC_MODEL_ID.

## TypeSafe JEV
- GET https://api.typesafe.ai/v1/models
- POST https://api.typesafe.ai/v1/systemone with model, state and questions.
- Strict schema parsing; malformed responses are NO_TRADE.

## Dependencies
- GHSA-v5pm-xwqc-g5wc (Microsoft.OpenApi): resolved. `Microsoft.AspNetCore.OpenApi` 10.0.14 with `Microsoft.OpenApi` 2.13.0, above the patched 2.7.5. No warning is suppressed, and a test confirms `/openapi/v1.json` is still served in Development.
- `dotnet list package --vulnerable --include-transitive` and `npm audit --omit=dev` report nothing. CI runs both on Windows.
