// Shapes of the PilotTrade API replies. A nullable field means the broker or backend may not report it.
export type Runtime = { mode: string; environment: string; accountId: string; executionEnabled: boolean; liveLocked: boolean }
export type AccountSummary = { accountId: string; name: string | null; status: string | null; currency: string | null; preferred: boolean }
export type InstrumentSummary = { epic: string; name: string; symbol: string | null; assetClass: number; status: number; bid: number | null; offer: number | null }
export type InstrumentMetadata = {
  epic: string; name: string; status: number; quoteCurrency: string; pricePrecision: number
  minDealSize: number; maxDealSize: number; dealSizeStep: number
  minStopDistance: number | null; maxStopDistance: number | null; minStopDistanceUnit: number; maxStopDistanceUnit: number
}
export type Selection = { epic: string; selectionVersion: number; selectedBy?: string }
export type Account = {
  accountId: string; currency: string; balance: number; equity: number | null; freeMargin: number | null
  marginUsed: number | null; unrealizedPnL: number; realizedPnLToday: number | null; equityIsDerived: boolean
}
export type Position = { positionId: string; epic: string; direction: number; size: number; openLevel: number | null; unrealizedPnL: number | null }
export type WorkingOrder = { orderId: string; epic: string; direction: number; type: number; size: number; level: number | null }
export type SnapshotResult = {
  outcome: 'NO_TRADE' | 'SNAPSHOT_READY'; reason: string | null
  snapshot: { epic: string; selectionVersion: number; asOfUtc: string; quote: { bid: number; ask: number; spread: number; sourceTimestampUtc: string } } | null
}
export type NoOrderEvidence = {
  checkedUtc: string; adapter: string; executionEnabled: boolean; liveLocked: boolean; journalEntries: number
  journalEntriesSentToBroker: number; orderIntents: number; brokerWriteRequestsSinceStart: number | null; noOrderSent: boolean
}
export type ProbeEvidence = { allPassed: boolean; orderEndpointsCalled: boolean; steps: { name: string; passed: boolean; detail: string | null }[] }
// Presence only: the backend never returns a stored credential.
export type CredentialStatus = { apiKeyPresent: boolean; identifierPresent: boolean; passwordPresent: boolean }
