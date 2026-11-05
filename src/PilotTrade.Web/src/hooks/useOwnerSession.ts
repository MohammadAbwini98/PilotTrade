import { useEffect, useMemo, useRef, useState } from 'react'
import { ApiError, NotFound, createApiClient } from '../api/client'
import type { LinkState } from '../api/client'
import type {
  Account, AccountSummary, CredentialStatus, InstrumentMetadata, InstrumentSummary, NoOrderEvidence, OperationsStatus, Position,
  ProbeEvidence, Runtime, Selection, SnapshotResult, WorkingOrder,
} from '../types/api'

const q = encodeURIComponent

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

  const checkNoOrders = () => guard(async () => {
    const current = generation.current
    const r = await api.get<NoOrderEvidence>(`/api/evidence/no-orders?accountId=${q(accountId)}`)
    if (current === generation.current) setNoOrders(r)
  })

  const testConnection = () => guard(async () => setEvidence(await api.post<ProbeEvidence>('/api/demo/test-connection', {})))

  const loadCredentialStatus = () => guard(async () => setCredentials(await api.get<CredentialStatus>('/api/demo/credential-status')))

  return {
    runtime, accounts, accountId, account, positions, orders, lastRefresh, link, status, statusFailed, updateFailure, instruments, searched, selection, instrument,
    snapshot, evidence, noOrders, credentials, busy: busy > 0, err,
    connect, disconnect, switchAccount, refreshAccount, search, select, buildSnapshot, checkNoOrders, testConnection, loadCredentialStatus,
  }
}

export type OwnerSession = ReturnType<typeof useOwnerSession>
export type ConnectedSession = OwnerSession & { runtime: Runtime }
