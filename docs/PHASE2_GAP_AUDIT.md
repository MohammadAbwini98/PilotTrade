# Phase 2 Gap Audit

Date: 2026-10-31

## Status legend: IMPLEMENTED / TESTED / PARTIAL / BLOCKED / NOT IMPLEMENTED

| Area | Status | Evidence |
|---|---|---|
| Domain contracts | IMPLEMENTED | src/PilotTrade.Domain |
| Simulator broker | IMPLEMENTED/TESTED | SimulatedBrokerAdapter; unit tests |
| Capital.com adapter | PARTIAL | Real HTTP session/search; submit not verified; contract tests pass |
| Persisted kill switch | IMPLEMENTED | EfPersistedExecutionState; PersistedTradingControl; startup disarm |
| Risk engine | IMPLEMENTED/TESTED | RiskEngine; SafetyTests |
| Decision coordinator | IMPLEMENTED/TESTED | DecisionCoordinator; stale snapshot test |
| Execution gateway | IMPLEMENTED/TESTED | ExecutionGateway; idempotency/kill switch tests |
| Claude client | PARTIAL | Strict parse; no live creds |
| JEV client | PARTIAL | Strict parse; no live creds |
| Market data | PARTIAL | Simulator only |
| Backtest/replay | NOT IMPLEMENTED | docs only |
| macOS package | BLOCKED | No macOS host; CI workflow added |
| Windows package | IMPLEMENTED/TESTED | release/win-x64 smoke test passed |

## Path from AI to broker
Claude/JEV -> DecisionCoordinator -> RiskEngine -> ExecutionGateway -> IBrokerAdapter. No model has direct broker access.

## Original 5 unit tests
1. RiskEngine_RejectsStalePrice - asserts rejection on stale quote.
2. DecisionCoordinator_MissingClaude_NoTrade - asserts NO_TRADE when Claude missing.
3. ExecutionGateway_LiveNotArmed_Rejects - asserts rejection in monitor-only mode.
4. DecisionCoordinator_StaleSnapshot_NoTrade - asserts NO_TRADE on snapshot mismatch.
(5th test count includes integration contract tests.)