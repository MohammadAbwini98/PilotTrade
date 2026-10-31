# Order Confirmation and Recovery

State machine: PROPOSED -> VALIDATED -> RISK_APPROVED -> PREVIEWED -> SUBMITTING -> BROKER_PENDING -> CONFIRMED/REJECTED/UNKNOWN -> RECONCILED.
On timeout/ambiguous: reconcile confirms/positions/orders before any retry; never blind resubmit. Multi-deal affectedDeals must be handled. Restart clears Live arm.