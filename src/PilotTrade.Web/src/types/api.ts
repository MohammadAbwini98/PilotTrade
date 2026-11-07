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
  checkedUtc: string; killSwitchEngaged: boolean; tradingMode: string; reconciliationComplete: boolean; suspensionReason: string | null; killSwitchReason?: string | null
  feed: { epic: string; health: FeedHealth; lastQuoteUtc: string | null } | null
}

// Task 02-05. Known: reported by the broker. Derived: computed on the server from known values only. Unknown: a needed value is missing.
export type RiskProvenance = 'Known' | 'Derived' | 'Unknown'
export type RiskField = { name: string; value: number | null; provenance: RiskProvenance; source: string; asOfUtc: string | null; note: string | null }
export type RiskMetric = {
  key: string; label: string; current: number | null; limit: number | null; unit: string; provenance: RiskProvenance
  utilizationPercent: number | null; breached: boolean | null; note: string
}
export type RiskFlag = { key: string; label: string; raised: boolean | null; detail: string }
export type RiskRule = { rule: string; result: 'Pass' | 'Veto' | 'Unknown'; reason: string; evaluatedUtc: string; inputAsOfUtc: string | null }
export type RiskView = {
  checkedUtc: string; policySource: string; policyEditable: boolean; accountCurrency: string | null; brokerSource: string; selectedEpic: string | null
  accountFields: RiskField[]; metrics: RiskMetric[]; flags: RiskFlag[]; rules: RiskRule[]; readErrors: string[]
}
export type ClearCheck = { name: string; passed: boolean; detail: string }
export type KillSwitchState = {
  checkedUtc: string; engaged: boolean; reason: string | null; changedUtc: string | null; changedBy: string | null; reconciliation: string
  clearChecks: ClearCheck[]; clearPhrase: string; maxReasonLength: number
}

// Task 02-06: the read-only order ledger. Every ID is masked on the server; Evidence and Reconciliation are server-side labels.
export type LedgerAlert = { severity: 'danger' | 'warn' | 'info'; kind: string; message: string }
export type LedgerTransition = { fromState: string | null; toState: string; occurredUtc: string; reason: string | null; source: string }
export type LedgerAudit = { id: string; occurredUtc: string; category: string; action: string; actor: string; sensitiveRedacted: boolean; refers: string }
export type LedgerEntry = {
  commandRef: string; intentRef: string; accountRef: string; epic: string; side: string | null; size: number | null
  state: string; statusGroup: string; evidence: string; reconciliation: string; reconciliationDetail: string; retryState: string | null
  dealReference: string | null; dealIds: string | null
  createdUtc: string; submittedUtc: string | null; confirmedUtc: string | null; updatedUtc: string; reason: string | null
  transitions: LedgerTransition[]; audit: LedgerAudit[]
}
export type LedgerPosition = {
  positionRef: string; epic: string; direction: string; size: number; openLevel: number | null; currentLevel: number | null
  unrealizedPnL: number | null; currency: string | null; openedUtc: string; asOfUtc: string; provenance: string; inJournal: boolean
}
export type LedgerWorkingOrder = {
  orderRef: string; epic: string; direction: string; type: string; size: number; level: number | null
  createdUtc: string; asOfUtc: string; provenance: string; inJournal: boolean
}
export type LedgerConfirm = { commandRef: string; epic: string; dealReference: string; dealIds: string | null; state: string; evidence: string; confirmedUtc: string | null; updatedUtc: string }
export type LedgerView = {
  // runtimeMode, execution and live are server wording: simulated fills are never Capital.com order permission.
  checkedUtc: string; source: string; runtimeMode: string; simulatedExecution: boolean; brokerExecutionEnabled: boolean
  execution: string; live: string; accountRef: string
  startup: { outcome: string; complete: boolean; suspensionReason: string | null }
  statusCounts: Record<string, number>; journalTotal: number; alerts: LedgerAlert[]
  journal: { total: number; offset: number; limit: number; items: LedgerEntry[] }
  positions: LedgerPosition[] | null; workingOrders: LedgerWorkingOrder[] | null; confirms: LedgerConfirm[]; readErrors: string[]
}
export type LedgerFilter = { q: string; epic: string; status: string; offset: number }

// Task 02-07. Audit records never carry their stored details; references are masked to the last four characters.
export type AuditFilter = { from: string; to: string; category: string; epic: string; correlation: string; q: string; offset: number }
export type AuditItem = {
  reference: string | null; occurredUtc: string; category: string; action: string; actor: string; environment: string
  scope: string; epic: string | null; correlationRef: string | null; detailsRedacted: boolean; sensitiveRedacted: boolean
}
export type AuditPageView = {
  checkedUtc: string; total: number; offset: number; limit: number; categories: string[]; classification: string; retention: string; items: AuditItem[]
}
export type AlertItem = { key: string; severity: string; category: string; title: string; detail: string; observedUtc: string | null; source: string }
export type AlertsView = { checkedUtc: string; alerts: AlertItem[]; note: string }
export type DiagnosticComponent = { key: string; label: string; status: string; detail: string; asOfUtc: string | null }
export type OperationalFailure = { occurredUtc: string | null; source: string; detail: string }
export type DiagnosticsView = {
  checkedUtc: string; overall: string; version: string; runtimeMode: string; execution: string; live: string; accountRef: string
  components: DiagnosticComponent[]; failures: OperationalFailure[]; note: string
}
