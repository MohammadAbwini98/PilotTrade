// Shapes of the PilotTrade API replies. A nullable field means the broker or backend may not report it.
export type Runtime = { mode: string; environment: string; accountId: string; executionEnabled: boolean; liveLocked: boolean }
export type AccountSummary = { accountId: string; name: string | null; status: string | null; currency: string | null; preferred: boolean }
export type InstrumentSummary = { epic: string; name: string; symbol: string | null; assetClass: number; status: number; bid: number | null; offer: number | null }
export type InstrumentMetadata = {
  epic: string; name: string; status: number; assetClass?: number; quoteCurrency: string; pricePrecision: number
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
  snapshot: {
    epic: string; selectionVersion: number; asOfUtc: string; quote: { bid: number; ask: number; spread: number; sourceTimestampUtc: string }
    candlesByTimeframe?: Record<string, Candle[]>
  } | null
}
export type Candle = { openTimeUtc: string; closeTimeUtc: string; isClosed: boolean }
// Task 02-03. The owner's watchlist, versioned so a second tab cannot silently overwrite it.
export type Watchlist = { accountId: string; epics: string[]; version: number; updatedUtc: string | null; updatedBy: string | null }
export type WatchQuote = { bid: number; ask: number; spreadPercent: number; sourceTimestampUtc: string; ageSeconds: number; health: FeedHealth }
export type WatchDealing = {
  pricePrecision: number; tickSize: number; minDealSize: number; maxDealSize: number; dealSizeStep: number
  minStopDistance: number | null; maxStopDistance: number | null; minStopDistanceUnit: number; maxStopDistanceUnit: number
  supportsMarketOrders: boolean; supportsLimitOrders: boolean; supportsStopOrders: boolean; tradingHours: string[]
}
// Asset class and market status arrive as names here. quoteChecksPassed is about the quote only; it is never a trade signal.
export type WatchRow = {
  epic: string; name: string | null; assetClass: string; marketStatus: string; quote: WatchQuote | null; maxSpreadPercent: number | null
  dealing: WatchDealing | null; quoteChecksPassed: boolean; blockers: string[]
}
export type MarketWatch = { checkedUtc: string; maxQuoteAgeSeconds: number; watchlistVersion: number; rows: WatchRow[] }
export type MarketPolicy = {
  source: string; editable: boolean; maxQuoteAgeSeconds: number; minClosedCandles: number; maxCandleAgeInBars: number
  maxSpreadPercent: Record<string, number>
}
export type NoOrderEvidence = {
  checkedUtc: string; adapter: string; executionEnabled: boolean; liveLocked: boolean; journalEntries: number
  journalEntriesSentToBroker: number; orderIntents: number; brokerWriteRequestsSinceStart: number | null; noOrderSent: boolean
}
export type ProbeEvidence = { allPassed: boolean; orderEndpointsCalled: boolean; steps: { name: string; passed: boolean; detail: string | null }[] }
// Presence only: the backend never returns a stored credential.
export type CredentialStatus = { apiKeyPresent: boolean; identifierPresent: boolean; passwordPresent: boolean }
// GET /api/status: the server's own safety state. Feed health is known only for the selected instrument.
export type FeedHealth = 'Healthy' | 'Stale' | 'Disconnected' | 'Unknown'
// Task 02-04. AI decision history: metadata only. Prompt text and reply text are never stored or sent.
export type AiStepStatus = 'Disabled' | 'Unavailable' | 'TimedOut' | 'Rejected' | 'Succeeded' | 'NotRun'
export type AiStep = {
  provider: string; status: AiStepStatus; model: string | null; latencyMs: number | null; inputTokens: number | null; outputTokens: number | null
  costUsd: number | null; confidence: number | null; output: string | null; schemaValid: boolean | null; problem: string | null
}
export type DecisionRecord = {
  decisionId: string; accountId: string; environment: string; epic: string; selectionVersion: number; snapshotId: string; snapshotAsOfUtc: string
  startedUtc: string; recordedUtc: string; claude: AiStep; jev: AiStep; promptSha256: string | null; promptChars: number | null; promptStored: boolean
  decision: string; noTradeReason: string | null; riskVeto: string | null; strategyId: string; strategyVersion: string; auditEventId: string
}
export type DecisionList = { total: number; items: DecisionRecord[] }
export type DecisionFilter = { epic: string; decision: string; status: string }
export type AiStatus = {
  runtimeWired: boolean; note: string; allowedProviders: string[]; decisionsRecorded: number
  providers: { name: string; registered: boolean; credentialPresent: boolean }[]
}
export type OperationsStatus = {
  checkedUtc: string; killSwitchEngaged: boolean; tradingMode: string; reconciliationComplete: boolean; suspensionReason: string | null
  feed: { epic: string; health: FeedHealth; lastQuoteUtc: string | null } | null
}
