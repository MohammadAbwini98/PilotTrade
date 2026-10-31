# Instrument Switching

Any instrument the broker offers can be selected in the UI; none is hardcoded.

1. `POST /api/instruments/select` checks the instrument with the broker, then raises the account's selection version.
2. The UI clears the old metadata and snapshot, and ignores late replies for the previous choice.
3. A snapshot request with an outdated epic or version returns NO_TRADE and no data.
4. Snapshots and Claude and JEV results carry the selection version; the decision coordinator refuses any that no longer match.
5. Position monitoring continues during a switch.
