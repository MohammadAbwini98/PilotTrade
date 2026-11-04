import { useMemo, useRef, useState } from 'react'
import { ApiError, NotFound, createApiClient } from '../api/client'
import type { LinkState } from '../api/client'
import type {
  Account, AccountSummary, CredentialStatus, InstrumentMetadata, InstrumentSummary, NoOrderEvidence, Position,
  ProbeEvidence, Runtime, Selection, SnapshotResult, WorkingOrder,
} from '../types/api'

const q = encodeURIComponent

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
    const [a, p, o] = await Promise.all([
      api.get<Account>(`/api/account?accountId=${q(id)}`),
      api.get<Position[]>(`/api/positions?accountId=${q(id)}`),
      api.get<WorkingOrder[]>(`/api/orders?accountId=${q(id)}`),
    ])
    if (current !== generation.current) return
    setAccount(a)
    setPositions(p)
    setOrders(o)
    setLastRefresh(new Date())
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
    setSelection(null)
    setAccount(null)
    setPositions([])
    setOrders([])
    setNoOrders(null)
    await loadAccount(id)
    await restoreSelection(id)
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
    setRuntime(null); setAccounts([]); setAccountId(''); setAccount(null); setPositions([]); setOrders([])
    setInstruments([]); setSearched(false); setSelection(null); setInstrument(null); setSnapshot(null)
    setEvidence(null); setNoOrders(null); setCredentials(null); setLastRefresh(null); setLink('unknown'); setErr('')
  }

  const refreshAccount = () => guard(() => loadAccount(accountId))

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
  })

  const buildSnapshot = () => guard(async () => {
    if (!selection) return
    const current = generation.current
    const r = await api.post<SnapshotResult>('/api/market/snapshot', { accountId, epic: selection.epic, selectionVersion: selection.selectionVersion, timeframes: ['5m', '1h'] })
    if (current === generation.current) setSnapshot(r)
  })

  const checkNoOrders = () => guard(async () => {
    const current = generation.current
    const r = await api.get<NoOrderEvidence>(`/api/evidence/no-orders?accountId=${q(accountId)}`)
    if (current === generation.current) setNoOrders(r)
  })

  const testConnection = () => guard(async () => setEvidence(await api.post<ProbeEvidence>('/api/demo/test-connection', {})))

  const loadCredentialStatus = () => guard(async () => setCredentials(await api.get<CredentialStatus>('/api/demo/credential-status')))

  return {
    runtime, accounts, accountId, account, positions, orders, lastRefresh, link, instruments, searched, selection, instrument,
    snapshot, evidence, noOrders, credentials, busy: busy > 0, err,
    connect, disconnect, switchAccount, refreshAccount, search, select, buildSnapshot, checkNoOrders, testConnection, loadCredentialStatus,
  }
}

export type OwnerSession = ReturnType<typeof useOwnerSession>
export type ConnectedSession = OwnerSession & { runtime: Runtime }
