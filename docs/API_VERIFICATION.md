# API Verification

Date verified: 2026-10-31 (by documentation review; live calls not executed due to no credentials).

## Capital.com
- Demo base: https://demo-api-capital.backend-capital.com
- Live base: https://api-capital.backend-capital.com
- Prefix: /api/v1
- Auth: POST /api/v1/session; headers X-CAP-API-KEY; response headers CST, X-SECURITY-TOKEN.
- Session inactivity: ~10 minutes (verify).
- Rate limits: ~10 req/s/user; session creation 1 req/s; max 40 streaming instruments; demo order hourly limit (verify).
- Instrument discovery: GET /api/v1/markets?searchTerm=...; details GET /api/v1/markets/{epic}.
- History: GET /api/v1/prices/{epic} (resolution/max) — needs verification.
- Positions: GET /api/v1/positions; working orders: GET /api/v1/workingorders.
- Order submit/confirm: POST /api/v1/orders or /api/v1/positions; GET /api/v1/confirms/{dealReference} — needs verification.
- Uncertainty: exact field names, order payloads, close/cancel endpoints. Capital.com real adapter returns not-verified for submit.

## Anthropic Claude
- Messages API: POST https://api.anthropic.com/v1/messages with x-api-key, anthropic-version.
- Model ID must be verified/pinned; configured via ANTHROPIC_MODEL_ID.

## TypeSafe JEV
- GET https://api.typesafe.ai/v1/models
- POST https://api.typesafe.ai/v1/systemone with model, state, questions.
- Strict schema parsing; no-trade on malformed.

## Contract Test Plan
- Simulator-based tests for all flows.
- Real broker contract tests (BLOCKED without credentials).