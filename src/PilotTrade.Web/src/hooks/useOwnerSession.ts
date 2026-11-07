import { useEffect, useMemo, useRef, useState } from 'react'
import { ApiError, NotFound, createApiClient } from '../api/client'
import type { LinkState } from '../api/client'
import type {
  Account, AccountSummary, AiStatus, AlertsView, AuditFilter, AuditPageView, ClearCheck, DiagnosticsView, CredentialStatus, KillSwitchState, LedgerFilter, LedgerView, RiskView, DecisionFilter, DecisionList, DecisionRecord, InstrumentMetadata, InstrumentSummary, MarketPolicy, MarketWatch, NoOrderEvidence, OperationsStatus,
  Position, ProbeEvidence, Runtime, Selection, SnapshotResult, Watchlist, WorkingOrder,
} from '../types/api'

const q = encodeURIComponent
export const LEDGER_PAGE = 25
export const AUDIT_PAGE = 25

// The overview refreshes itself on this interval. After a failed update the wait doubles, up to the maximum, and returns to normal after a success.
// Tests shorten these; the app never changes them.
export const POLL = { baseMs: 15_000, maxMs: 120_000 }

// A failed background update: the figures on screen are kept, but marked stale from this moment.
export type UpdateFailure = { at: Date; message: string }

// Everything the owner's browser tab knows, and every action it can take. All of it comes from the backend.
export function useOwnerSession() {
  // Held in memory only, so closing or reloading the page forgets the token.
  const ownerToken = useRef('')
  const [link, setLink] = useState<LinkState>('unknown')
  const api = useMemo(() => createApiClient(() => ownerToken.current, setLink), [])

  const [runtime, setRuntime] = useState<Runtime | null>(null)
  const [accounts, setAccounts] = useState<AccountSummary[]>([])
  const [accountId, setAccountId] = useState('')
  const [account, setAccount] = useState<Account | null>(null)
  const [positions, setPositions] = useState<Position[]>([])
  const [orders, setOrders] = useState<WorkingOrder[]>([])
  const [lastRefresh, setLastRefresh] = useState<Date | null>(null)
  const [status, setStatus] = useState<OperationsStatus | null>(null)
  const [statusFailed, setStatusFailed] = useState(false)
  const [updateFailure, setUpdateFailure] = useState<UpdateFailure | null>(null)
  // One update at a time: a timer tick or a Refresh click that arrives during an update joins it instead of starting another.
  const inFlight = useRef<Promise<ApiError | null> | null>(null)
  const accountRef = useRef('')
  const [instruments, setInstruments] = useState<InstrumentSummary[]>([])
  const [searched, setSearched] = useState(false)
  const [selection, setSelection] = useState<Selection | null>(null)
  const [instrument, setInstrument] = useState<InstrumentMetadata | null>(null)
  const [snapshot, setSnapshot] = useState<SnapshotResult | null>(null)
  const [evidence, setEvidence] = useState<ProbeEvidence | null>(null)
  const [noOrders, setNoOrders] = useState<NoOrderEvidence | null>(null)
  const [credentials, setCredentials] = useState<CredentialStatus | null>(null)
  const [busy, setBusy] = useState(0)
  const [err, setErr] = useState('')
  // Bumped on every account or instrument change, so a slow reply for the old choice cannot overwrite the new one.
  const generation = useRef(0)
  const [watchlist, setWatchlist] = useState<Watchlist | null>(null)
  const [watch, setWatch] = useState<MarketWatch | null>(null)
  const [policy, setPolicy] = useState<MarketPolicy | null>(null)
  // Bumped on account change, disconnect and every monitor refresh, so only the newest monitor reply for the current account lands.
  const watchGeneration = useRef(0)
  const [aiStatus, setAiStatus] = useState<AiStatus | null>(null)
  const [decisions, setDecisions] = useState<DecisionList | null>(null)
  const [decision, setDecision] = useState<DecisionRecord | null>(null)
  // Bumped on account change, disconnect and every decision read, so only the newest reply for the current account lands.
  const decisionGeneration = useRef(0)
  // Task 02-05. The kill switch keeps its own error and busy state, so a failure elsewhere on the page never hides or blocks the stop.
  const [risk, setRisk] = useState<RiskView | null>(null)
  const [riskFailure, setRiskFailure] = useState('')
  const [killSwitch, setKillSwitch] = useState<KillSwitchState | null>(null)
  const [killFailure, setKillFailure] = useState('')
  const [killReadFailure, setKillReadFailure] = useState('')
  const [killMessage, setKillMessage] = useState('')
  const [refusedChecks, setRefusedChecks] = useState<ClearCheck[] | null>(null)
  const [killBusy, setKillBusy] = useState(false)
  const riskGeneration = useRef(0)
  // Task 02-06. The ledger keeps its own error, so a failed read shows as such and never as an empty journal.
  const [ledger, setLedger] = useState<LedgerView | null>(null)
  const [ledgerFailure, setLedgerFailure] = useState('')
  const ledgerGeneration = useRef(0)
  // Task 02-07. Audit, alerts and diagnostics each keep their own reply, failure and loading flag; only the newest reply for the
  // current account lands, so a slow older reply never overwrites a newer one.
  const [audit, setAudit] = useState<AuditPageView | null>(null)
  const [auditFailure, setAuditFailure] = useState('')
  const [auditLoading, setAuditLoading] = useState(false)
  const auditGeneration = useRef(0)
  const [alerts, setAlerts] = useState<AlertsView | null>(null)
  const [alertsFailure, setAlertsFailure] = useState('')
  const [alertsLoading, setAlertsLoading] = useState(false)
  const alertsGeneration = useRef(0)
  const [diagnostics, setDiagnostics] = useState<DiagnosticsView | null>(null)
  const [diagnosticsFailure, setDiagnosticsFailure] = useState('')
  const [diagnosticsLoading, setDiagnosticsLoading] = useState(false)
  const diagnosticsGeneration = useRef(0)
  const resetOperations = () => {
    auditGeneration.current++; alertsGeneration.current++; diagnosticsGeneration.current++
    setAudit(null); setAuditFailure(''); setAuditLoading(false)
    setAlerts(null); setAlertsFailure(''); setAlertsLoading(false)
    setDiagnostics(null); setDiagnosticsFailure(''); setDiagnosticsLoading(false)
  }

  const guard = async (work: () => Promise<void>) => {
    setErr('')
    setBusy(b => b + 1)
    try { await work() } catch (e) { setErr(e instanceof ApiError ? e.message : 'The request could not be completed.') }
    finally { setBusy(b => b - 1) }
  }

  const clearInstrument = () => {
    generation.current++
    setInstrument(null)
    setSnapshot(null)
  }

  const loadAccount = async (id: string) => {
    const current = generation.current
    // The status read failing must not hide account figures that did arrive; it is shown as unavailable instead.
    const statusRead = api.get<OperationsStatus>(`/api/status?accountId=${q(id)}`).then(r => r, () => null)
    const [a, p, o, st] = await Promise.all([
      api.get<Account>(`/api/account?accountId=${q(id)}`),
      api.get<Position[]>(`/api/positions?accountId=${q(id)}`),
      api.get<WorkingOrder[]>(`/api/orders?accountId=${q(id)}`),
      statusRead,
    ])
    if (current !== generation.current) return
    setAccount(a)
    setPositions(p)
    setOrders(o)
    setStatus(st)
    setStatusFailed(st === null)
    setUpdateFailure(null)
    setLastRefresh(new Date())
  }

  // Feed health changes when a snapshot is built or the selection moves, so it is read again then.
  const reloadStatus = async (id: string) => {
    const current = generation.current
    const st = await api.get<OperationsStatus>(`/api/status?accountId=${q(id)}`).then(r => r, () => null)
    if (current !== generation.current) return
    setStatus(st)
    setStatusFailed(st === null)
  }

  // Shared by the timer and the Refresh button. A failure keeps the last figures and marks them stale.
  const update = (): Promise<ApiError | null> => {
    if (inFlight.current) return inFlight.current
    const id = accountRef.current
    if (!id) return Promise.resolve(null)
    const run = (async () => {
      try { await loadAccount(id); return null }
      catch (e) {
        const error = e instanceof ApiError ? e : new ApiError('The request could not be completed.')
        setUpdateFailure({ at: new Date(), message: error.message })
        return error
      }
      finally { inFlight.current = null }
    })()
    inFlight.current = run
    return run
  }

  // The backend keeps the owner's choice across restarts under a new, higher version; the instrument is checked afresh before it is shown.
  const restoreSelection = async (id: string) => {
    const current = generation.current
    let saved: Selection
    try { saved = await api.get<Selection>(`/api/instruments/selection?accountId=${q(id)}`) }
    catch (e) { if (e instanceof NotFound) return; throw e }
    if (current !== generation.current) return
    const meta = await api.get<InstrumentMetadata>(`/api/instruments/${q(saved.epic)}?accountId=${q(id)}`)
    if (current !== generation.current) return
    setSelection(saved)
    setInstrument(meta)
  }

  const switchAccount = (id: string) => guard(async () => {
    clearInstrument()
    watchGeneration.current++
    setWatchlist(null)
    setWatch(null)
    decisionGeneration.current++
    setAiStatus(null); setDecisions(null); setDecision(null)
    riskGeneration.current++
    setRisk(null); setRiskFailure(''); setKillSwitch(null); setKillFailure(''); setKillReadFailure(''); setKillMessage(''); setRefusedChecks(null)
    ledgerGeneration.current++
    setLedger(null); setLedgerFailure('')
    resetOperations()
    setAccountId(id)
    accountRef.current = id
    setSelection(null)
    setAccount(null)
    setPositions([])
    setOrders([])
    setNoOrders(null)
    setStatus(null)
    setUpdateFailure(null)
    await loadAccount(id)
    await restoreSelection(id)
    await reloadStatus(id)
  })

  const connect = (token: string) => {
    ownerToken.current = token
    void guard(async () => {
      const rt = await api.get<Runtime>('/api/runtime')
      const list = await api.get<AccountSummary[]>('/api/accounts')
      setRuntime(rt)
      // Only the account the backend was configured with is ever selected, never a preferred or first one.
      const configured = list.filter(a => a.accountId === rt.accountId)
      setAccounts(configured)
      if (configured.length === 0) throw new ApiError('The configured trading account is not available from the broker.')
      await switchAccount(configured[0].accountId)
    })
  }

  // Forgets the token and everything loaded with it; replies still in flight are dropped.
  const disconnect = () => {
    generation.current++
    watchGeneration.current++
    setWatchlist(null); setWatch(null); setPolicy(null)
    decisionGeneration.current++
    setAiStatus(null); setDecisions(null); setDecision(null)
    riskGeneration.current++
    setRisk(null); setRiskFailure(''); setKillSwitch(null); setKillFailure(''); setKillReadFailure(''); setKillMessage(''); setRefusedChecks(null)
    ledgerGeneration.current++
    setLedger(null); setLedgerFailure('')
    resetOperations()
    ownerToken.current = ''
    accountRef.current = ''
    setStatus(null); setStatusFailed(false); setUpdateFailure(null)
    setRuntime(null); setAccounts([]); setAccountId(''); setAccount(null); setPositions([]); setOrders([])
    setInstruments([]); setSearched(false); setSelection(null); setInstrument(null); setSnapshot(null)
    setEvidence(null); setNoOrders(null); setCredentials(null); setLastRefresh(null); setLink('unknown'); setErr('')
  }

  const refreshAccount = () => guard(async () => { const error = await update(); if (error) throw error })

  // Polling runs while an account is open. The timer is cleared on disconnect, on account change and on unmount,
  // and a reply that arrives after that is dropped by the generation check.
  useEffect(() => {
    if (!accountId) return
    let delay = POLL.baseMs
    let stopped = false
    let timer: ReturnType<typeof setTimeout>
    const tick = async () => {
      const error = await update()
      if (stopped) return
      delay = error ? Math.min(delay * 2, POLL.maxMs) : POLL.baseMs
      timer = setTimeout(() => void tick(), delay)
    }
    timer = setTimeout(() => void tick(), delay)
    return () => { stopped = true; clearTimeout(timer) }
    // update reads the account from a ref, so the timer only restarts when the account changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [accountId])

  const search = (text: string) => guard(async () => {
    setInstruments(await api.get<InstrumentSummary[]>(`/api/instruments/search?q=${q(text)}&limit=50`))
    setSearched(true)
  })

  const select = (epic: string) => guard(async () => {
    clearInstrument()
    setSelection(null)
    const current = generation.current
    const r = await api.post<{ selection: Selection; instrument: InstrumentMetadata }>('/api/instruments/select', { accountId, epic })
    if (current !== generation.current) return
    setSelection(r.selection)
    setInstrument(r.instrument)
    await reloadStatus(accountId)
  })

  const buildSnapshot = () => guard(async () => {
    if (!selection) return
    const current = generation.current
    const r = await api.post<SnapshotResult>('/api/market/snapshot', { accountId, epic: selection.epic, selectionVersion: selection.selectionVersion, timeframes: ['5m', '1h'] })
    if (current !== generation.current) return
    setSnapshot(r)
    await reloadStatus(accountId)
  })

  // The monitor only reads: metadata and one batched quote request per refresh. It never selects, signals or orders.
  const loadWatch = async () => {
    const id = accountRef.current
    if (!id) return
    const current = ++watchGeneration.current
    const w = await api.get<MarketWatch>(`/api/market/watch?accountId=${q(id)}`)
    if (current === watchGeneration.current && id === accountRef.current) setWatch(w)
  }

  const openMarkets = () => guard(async () => {
    const id = accountRef.current
    if (!id) return
    const current = watchGeneration.current
    const [p, list] = await Promise.all([policy ? Promise.resolve(policy) : api.get<MarketPolicy>('/api/market/policy'), api.get<Watchlist>(`/api/watchlist?accountId=${q(id)}`)])
    if (current !== watchGeneration.current) return
    setPolicy(p)
    setWatchlist(list)
    await loadWatch()
  })

  const refreshWatch = () => guard(loadWatch)

  // Saved under the version this tab last saw. If another tab saved first, the server refuses and the newer list is shown instead.
  const saveWatchlist = (epics: string[]) => guard(async () => {
    const id = accountRef.current
    if (!id || !watchlist) return
    const current = watchGeneration.current
    try {
      const saved = await api.post<Watchlist>('/api/watchlist', { accountId: id, epics, expectedVersion: watchlist.version })
      if (current !== watchGeneration.current) return
      setWatchlist(saved)
    } catch (e) {
      if (e instanceof ApiError && e.status === 409 && current === watchGeneration.current) {
        setWatchlist((e.body as { current: Watchlist }).current)
        throw new ApiError('The watchlist was changed in another tab. The current list is shown; nothing was saved.')
      }
      throw e
    }
    await loadWatch()
  })

  const addToWatchlist = (epic: string) => saveWatchlist([...(watchlist?.epics ?? []), epic])
  const removeFromWatchlist = (epic: string) => saveWatchlist((watchlist?.epics ?? []).filter(e => e !== epic))

  // Read-only. The history lists what was recorded; this page never asks an AI provider anything.
  const loadDecisions = (f: DecisionFilter) => guard(async () => {
    const id = accountRef.current
    if (!id) return
    const current = ++decisionGeneration.current
    const filter = [f.epic.trim() && `&epic=${q(f.epic.trim())}`, f.decision && `&decision=${q(f.decision)}`, f.status && `&status=${q(f.status)}`].join('')
    const [st, list] = await Promise.all([
      api.get<AiStatus>(`/api/ai/status?accountId=${q(id)}`),
      api.get<DecisionList>(`/api/decisions?accountId=${q(id)}${filter}&limit=50`),
    ])
    if (current !== decisionGeneration.current || id !== accountRef.current) return
    setAiStatus(st)
    setDecisions(list)
  })

  const openDecision = (decisionId: string) => guard(async () => {
    const id = accountRef.current
    if (!id) return
    const current = ++decisionGeneration.current
    const d = await api.get<DecisionRecord>(`/api/decisions/${q(decisionId)}?accountId=${q(id)}`)
    if (current === decisionGeneration.current && id === accountRef.current) setDecision(d)
  })

  const closeDecision = () => { decisionGeneration.current++; setDecision(null) }

  const message = (e: unknown) => {
    if (!(e instanceof ApiError)) return 'The request could not be completed.'
    const detail = (e.body as { detail?: string } | null)?.detail
    if (detail) return detail
    if (e.message.includes('KillSwitchNotPersisted')) return 'The kill switch is engaged in the running server but could not be saved, so it would not survive a restart.'
    return e.message
  }

  // Read-only. The risk view and the kill-switch state are read separately, so one failing never hides the other.
  const loadRisk = async () => {
    const id = accountRef.current
    if (!id) return
    const current = ++riskGeneration.current
    const [r, k] = await Promise.allSettled([
      api.get<RiskView>(`/api/risk?accountId=${q(id)}`),
      api.get<KillSwitchState>(`/api/kill-switch?accountId=${q(id)}`),
    ])
    if (current !== riskGeneration.current || id !== accountRef.current) return
    if (r.status === 'fulfilled') { setRisk(r.value); setRiskFailure('') } else setRiskFailure(message(r.reason))
    // A read failure is kept apart from the outcome of an engage or clear, so a later successful read never hides a refusal.
    if (k.status === 'fulfilled') { setKillSwitch(k.value); setKillReadFailure('') } else setKillReadFailure(message(k.reason))
  }

  // The stop needs only the account id and a reason; it does not wait for, or depend on, any other read on the page.
  const engageKillSwitch = async (reason: string) => {
    const id = accountRef.current
    if (!id || killBusy) return
    setKillBusy(true); setKillFailure(''); setKillMessage(''); setRefusedChecks(null)
    try {
      const r = await api.post<{ changed: boolean; auditRecorded: boolean }>('/api/kill-switch/engage', { accountId: id, reason })
      setKillMessage(`${r.changed ? 'Kill switch engaged.' : 'The kill switch was already engaged; its first reason is kept.'}${r.auditRecorded ? '' : ' The audit log could not record it.'}`)
    } catch (e) { setKillFailure(message(e)) }
    finally { setKillBusy(false) }
    await loadRisk().catch(() => {})
    await reloadStatus(id).catch(() => {})
  }

  // Clearing enables nothing. The server runs every safety check and refuses the clear if any fails.
  const clearKillSwitch = async (confirmation: string) => {
    const id = accountRef.current
    if (!id || killBusy) return
    setKillBusy(true); setKillFailure(''); setKillMessage(''); setRefusedChecks(null)
    try {
      await api.post('/api/kill-switch/clear', { accountId: id, confirmation })
      setKillMessage('Kill switch cleared. Demo order submission stays disabled and Live stays locked.')
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setRefusedChecks((e.body as { checks?: ClearCheck[] } | null)?.checks ?? [])
        setKillFailure('The clear was refused: a safety check failed. The kill switch stays engaged.')
      } else setKillFailure(message(e))
    } finally { setKillBusy(false) }
    await loadRisk().catch(() => {})
    await reloadStatus(id).catch(() => {})
  }

  const checkNoOrders = () => guard(async () => {
    const current = generation.current
    const r = await api.get<NoOrderEvidence>(`/api/evidence/no-orders?accountId=${q(accountId)}`)
    if (current === generation.current) setNoOrders(r)
  })

  // Read-only: one GET. Only the newest reply for the current account lands.
  const loadLedger = async (f: LedgerFilter) => {
    const id = accountRef.current
    if (!id) return
    const current = ++ledgerGeneration.current
    const filter = [f.q.trim() && `&q=${q(f.q.trim())}`, f.epic.trim() && `&epic=${q(f.epic.trim())}`, f.status && `&status=${q(f.status)}`].join('')
    try {
      const view = await api.get<LedgerView>(`/api/ledger?accountId=${q(id)}${filter}&limit=${LEDGER_PAGE}&offset=${Math.max(0, f.offset)}`)
      if (current !== ledgerGeneration.current || id !== accountRef.current) return
      setLedger(view); setLedgerFailure('')
    } catch (e) {
      if (current !== ledgerGeneration.current || id !== accountRef.current) return
      setLedgerFailure(message(e))
    }
  }

  // Read-only: one GET each. A failed read clears the view, so stale alerts or statuses are never shown as current.
  const readLatest = async <T>(gen: { current: number }, path: (id: string) => string, set: (v: T | null) => void, fail: (m: string) => void, loading: (b: boolean) => void) => {
    const id = accountRef.current
    if (!id) return
    const current = ++gen.current
    loading(true)
    try {
      const view = await api.get<T>(path(id))
      if (current !== gen.current || id !== accountRef.current) return
      set(view); fail('')
    } catch (e) {
      if (current !== gen.current || id !== accountRef.current) return
      set(null); fail(message(e))
    } finally {
      if (current === gen.current) loading(false)
    }
  }

  const loadAudit = (f: AuditFilter) => {
    const filter = [
      f.from && `&from=${q(`${f.from}T00:00:00Z`)}`, f.to && `&to=${q(`${f.to}T23:59:59.999Z`)}`, f.category && `&category=${q(f.category)}`,
      f.epic.trim() && `&epic=${q(f.epic.trim())}`, f.correlation.trim() && `&correlation=${q(f.correlation.trim())}`, f.q.trim() && `&q=${q(f.q.trim())}`,
    ].join('')
    return readLatest<AuditPageView>(auditGeneration, id => `/api/audit?accountId=${q(id)}${filter}&limit=${AUDIT_PAGE}&offset=${Math.max(0, f.offset)}`, setAudit, setAuditFailure, setAuditLoading)
  }
  const loadAlerts = () => readLatest<AlertsView>(alertsGeneration, id => `/api/alerts?accountId=${q(id)}`, setAlerts, setAlertsFailure, setAlertsLoading)
  const loadDiagnostics = () => readLatest<DiagnosticsView>(diagnosticsGeneration, id => `/api/diagnostics?accountId=${q(id)}`, setDiagnostics, setDiagnosticsFailure, setDiagnosticsLoading)

  const testConnection = () => guard(async () => setEvidence(await api.post<ProbeEvidence>('/api/demo/test-connection', {})))

  const loadCredentialStatus = () => guard(async () => setCredentials(await api.get<CredentialStatus>('/api/demo/credential-status')))

  return {
    runtime, accounts, accountId, account, positions, orders, lastRefresh, link, status, statusFailed, updateFailure, instruments, searched, selection, instrument,
    snapshot, evidence, noOrders, credentials, watchlist, watch, policy, aiStatus, decisions, decision, busy: busy > 0, err,
    risk, riskFailure, killSwitch, killFailure, killReadFailure, killMessage, refusedChecks, killBusy, ledger, ledgerFailure,
    audit, auditFailure, auditLoading, alerts, alertsFailure, alertsLoading, diagnostics, diagnosticsFailure, diagnosticsLoading,
    connect, disconnect, switchAccount, refreshAccount, search, select, buildSnapshot, checkNoOrders, testConnection, loadCredentialStatus,
    openMarkets, refreshWatch, addToWatchlist, removeFromWatchlist, loadDecisions, openDecision, closeDecision,
    loadRisk, engageKillSwitch, clearKillSwitch, loadLedger, loadAudit, loadAlerts, loadDiagnostics,
  }
}

export type OwnerSession = ReturnType<typeof useOwnerSession>
export type ConnectedSession = OwnerSession & { runtime: Runtime }
