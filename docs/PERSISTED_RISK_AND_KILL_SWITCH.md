# Persisted Risk and Kill Switch

Execution state persisted in SQLite (ExecutionStateEntity). On startup Live is always disarmed. Kill switch durable. Suspension reason recorded. Kill switch blocks new entries only; does not cancel valid protective orders.