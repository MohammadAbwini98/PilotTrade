# Security Threat Model

- Secrets in OS store (DPAPI Windows). Never in repo/logs.
- Environment confusion prevented by separate config + adapter.
- Prompt injection: market data treated as untrusted; models have no tools.
- TOCTOU: risk re-evaluated before submit.
- Live locked by default; no AI activation path.
- Kill switch durable in state (in-memory in MVP; persist in future).