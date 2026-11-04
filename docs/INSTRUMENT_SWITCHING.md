# Instrument Switching

Any instrument the broker offers can be selected in the UI; none is hardcoded.

1. `POST /api/instruments/select` checks the instrument with the broker, then raises the account's selection version.
2. The UI clears the old metadata and snapshot, and ignores late replies for the previous choice.
3. A snapshot request with an outdated epic or version returns NO_TRADE and no data.
4. Snapshots and Claude and JEV results carry the selection version; the decision coordinator refuses any that no longer match.
5. Position monitoring continues during a switch.

## After a restart or restore

The selection is stored per environment and account in the database. On the first use after a start, each stored selection is re-issued with a new id, a version above every stored selection, decision, AI analysis and order intent, and `selectedBy` set to `restored`. So the instrument comes back, but nothing made for the earlier version does: an old snapshot, Claude or JEV result, decision or execution authorization no longer matches. A selection that cannot be stored is not applied. The UI loads the restored selection, checks the instrument with the broker again before showing it, and labels it "restored after restart". `RestartPersistenceTests` and `HardeningTests` cover this.
