# Requirements Matrix

| ID | Requirement | Status | Evidence |
|---|---|---|---|
| INSTR-01 | Dynamic EPIC search | PARTIAL | Simulated adapter + API endpoint; real broker BLOCKED (no creds) |
| INSTR-02 | Two asset classes sizing | PARTIAL | Simulator metadata; tests pending |
| INSTR-03 | Switch with open position monitored | PARTIAL | Architecture + guardian; integration test pending |
| INSTR-04 | Drop old AI response | PASS | SafetyTests.DecisionCoordinator_StaleSnapshot_NoTrade |
| DATA-01 | Candle/indicator reproducibility | PARTIAL | MarketDataService deterministic calcs |
| DATA-02 | Stale feed blocks orders | PASS | RiskEngine_RejectsStalePrice |
| AI-01 | Claude schema validation | PARTIAL | Client strict parse; tests offline only |
| AI-02 | JEV schema validation | PARTIAL | Client strict parse |
| RISK-01 | Risk limits enforced | PASS | RiskEngine |
| RISK-04 | Kill switch | PASS | ExecutionGateway test |
| ORDER-02 | No duplicate on retry | PASS | ExecutionGateway idempotency |
| ENV-02 | Live locked on boot | PASS | Default TradingMode.MonitorOnly; LiveArmed false |
| ENV-04 | AI cannot activate Live | PASS | No AI->control path |
| OPS-02 | No secrets in logs | PARTIAL | Redaction not fully implemented |
| PACK-01 | Windows 10/11 x64 packaging | PARTIAL | win-x64 publish runs in CI; portable release is task 01-03. macOS is out of scope |