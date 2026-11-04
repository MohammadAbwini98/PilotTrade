import { useCallback, useRef, useState } from 'react'
import type { CSSProperties, FormEvent } from 'react'
import { palette } from './theme'

type Runtime = { mode: string; environment: string; accountId: string; executionEnabled: boolean; liveLocked: boolean }
type AccountSummary = { accountId: string; name: string | null; status: string | null; currency: string | null; preferred: boolean }
type InstrumentSummary = { epic: string; name: string; symbol: string | null; assetClass: number; status: number; bid: number | null; offer: number | null }
type InstrumentMetadata = {
  epic: string; name: string; status: number; quoteCurrency: string; pricePrecision: number
  minDealSize: number; maxDealSize: number; dealSizeStep: number
  minStopDistance: number | null; maxStopDistance: number | null; minStopDistanceUnit: number; maxStopDistanceUnit: number
}
type Selection = { epic: string; selectionVersion: number; selectedBy?: string }
type Account = {
  accountId: string; currency: string; balance: number; equity: number | null; freeMargin: number | null
  marginUsed: number | null; unrealizedPnL: number; realizedPnLToday: number | null; equityIsDerived: boolean
}
type Position = { positionId: string; epic: string; direction: number; size: number; openLevel: number | null; unrealizedPnL: number | null }
type WorkingOrder = { orderId: string; epic: string; direction: number; type: number; size: number; level: number | null }
type SnapshotResult = {
  outcome: 'NO_TRADE' | 'SNAPSHOT_READY'; reason: string | null
  snapshot: { epic: string; selectionVersion: number; asOfUtc: string; quote: { bid: number; ask: number; spread: number; sourceTimestampUtc: string } } | null
}
type NoOrderEvidence = {
  checkedUtc: string; adapter: string; executionEnabled: boolean; liveLocked: boolean; journalEntries: number
  journalEntriesSentToBroker: number; orderIntents: number; brokerWriteRequestsSinceStart: number | null; noOrderSent: boolean
}
type ProbeEvidence = { allPassed: boolean; orderEndpointsCalled: boolean; steps: { name: string; passed: boolean; detail: string | null }[] }

const MARKET_STATUS = ['Unknown', 'Open', 'Closed', 'Pre-market', 'Post-market', 'Halted', 'Maintenance']
const SIDE = ['BUY', 'SELL']
const ORDER_TYPE = ['MARKET', 'LIMIT', 'STOP', 'STOP-LIMIT']
const DISTANCE_UNIT = ['points', '%']

// A missing broker value stays visibly missing; it is never shown as zero.
const show = (value: number | null | undefined, digits = 2) => (value === null || value === undefined ? 'not provided' : value.toFixed(digits))

class ApiError extends Error {}
class NotFound extends ApiError {}

// A read is tried once more after a network failure; a write never is, so a POST cannot reach the server twice.
const RETRY_DELAY_MS = 500

export default function App() {
  // Held in memory only, so closing or reloading the page forgets the token.
  const [tokenInput, setTokenInput] = useState('')
  const ownerToken = useRef('')
  const [runtime, setRuntime] = useState<Runtime | null>(null)
  const [accounts, setAccounts] = useState<AccountSummary[]>([])
  const [accountId, setAccountId] = useState('')
  const [account, setAccount] = useState<Account | null>(null)
  const [positions, setPositions] = useState<Position[]>([])
  const [orders, setOrders] = useState<WorkingOrder[]>([])
  const [q, setQ] = useState('')
  const [instruments, setInstruments] = useState<InstrumentSummary[]>([])
  const [selection, setSelection] = useState<Selection | null>(null)
  const [instrument, setInstrument] = useState<InstrumentMetadata | null>(null)
  const [snapshot, setSnapshot] = useState<SnapshotResult | null>(null)
  const [evidence, setEvidence] = useState<ProbeEvidence | null>(null)
  const [noOrders, setNoOrders] = useState<NoOrderEvidence | null>(null)
  const [searched, setSearched] = useState(false)
  const [busy, setBusy] = useState(0)
  const [err, setErr] = useState('')
  // Bumped on every account or instrument change, so a slow reply for the old choice cannot overwrite the new one.
  const generation = useRef(0)

  const request = useCallback(async <T,>(path: string, init?: RequestInit): Promise<T> => {
    const headers = new Headers(init?.headers)
    headers.set('X-PilotTrade-Owner-Token', ownerToken.current)
    const isRead = (init?.method ?? 'GET') === 'GET'
    let r: Response
    try { r = await fetch(path, { ...init, headers }) }
    catch {
      if (!isRead) throw new ApiError('The server could not be reached. The request was not repeated.')
      await new Promise(resolve => setTimeout(resolve, RETRY_DELAY_MS))
      try { r = await fetch(path, { ...init, headers }) }
      catch { throw new ApiError('The server could not be reached.') }
    }
    if (r.status === 403) throw new ApiError('Refused: check the owner token.')
    if (r.status === 404) throw new NotFound('Not found.')
    if (!r.ok) {
      const body = await r.json().catch(() => null) as { error?: string } | null
      throw new ApiError(`Request failed (${r.status}${body?.error ? `: ${body.error}` : ''}).`)
    }
    return await r.json() as T
  }, [])

  const post = useCallback(async <T,>(path: string, body: unknown): Promise<T> => {
    const { csrfToken } = await request<{ csrfToken: string }>('/api/security/csrf-token')
    return await request<T>(path, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-PilotTrade-CSRF': csrfToken }, body: JSON.stringify(body) })
  }, [request])

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
      request<Account>(`/api/account?accountId=${encodeURIComponent(id)}`),
      request<Position[]>(`/api/positions?accountId=${encodeURIComponent(id)}`),
      request<WorkingOrder[]>(`/api/orders?accountId=${encodeURIComponent(id)}`),
    ])
    if (current !== generation.current) return
    setAccount(a)
    setPositions(p)
    setOrders(o)
  }

  // The backend keeps the owner's choice across restarts under a new, higher version; the instrument is checked afresh before it is shown.
  const restoreSelection = async (id: string) => {
    const current = generation.current
    let saved: Selection
    try { saved = await request<Selection>(`/api/instruments/selection?accountId=${encodeURIComponent(id)}`) }
    catch (e) { if (e instanceof NotFound) return; throw e }
    if (current !== generation.current) return
    const meta = await request<InstrumentMetadata>(`/api/instruments/${encodeURIComponent(saved.epic)}?accountId=${encodeURIComponent(id)}`)
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

  const connect = (e: FormEvent) => {
    e.preventDefault()
    ownerToken.current = tokenInput.trim()
    setTokenInput('')
    void guard(async () => {
      const rt = await request<Runtime>('/api/runtime')
      const list = await request<AccountSummary[]>('/api/accounts')
      setRuntime(rt)
      // Only the account the backend was configured with is ever selected, never a preferred or first one.
      const configured = list.filter(a => a.accountId === rt.accountId)
      setAccounts(configured)
      if (configured.length === 0) throw new ApiError('The configured trading account is not available from the broker.')
      await switchAccount(configured[0].accountId)
    })
  }

  const search = (e: FormEvent) => {
    e.preventDefault()
    void guard(async () => {
      setInstruments(await request<InstrumentSummary[]>(`/api/instruments/search?q=${encodeURIComponent(q)}&limit=50`))
      setSearched(true)
    })
  }

  const select = (epic: string) => guard(async () => {
    clearInstrument()
    setSelection(null)
    const current = generation.current
    const r = await post<{ selection: Selection; instrument: InstrumentMetadata }>('/api/instruments/select', { accountId, epic })
    if (current !== generation.current) return
    setSelection(r.selection)
    setInstrument(r.instrument)
  })

  const buildSnapshot = () => guard(async () => {
    if (!selection) return
    const current = generation.current
    const r = await post<SnapshotResult>('/api/market/snapshot', { accountId, epic: selection.epic, selectionVersion: selection.selectionVersion, timeframes: ['5m', '1h'] })
    if (current === generation.current) setSnapshot(r)
  })

  const checkNoOrders = () => guard(async () => {
    const current = generation.current
    const r = await request<NoOrderEvidence>(`/api/evidence/no-orders?accountId=${encodeURIComponent(accountId)}`)
    if (current === generation.current) setNoOrders(r)
  })

  const testConnection = () => guard(async () => setEvidence(await post<ProbeEvidence>('/api/demo/test-connection', {})))

  if (!runtime) {
    return (
      <div style={page}>
        <h1>PilotTrade</h1>
        <form onSubmit={connect}>
          <label htmlFor="owner-token">Owner token (run <code>Show-Owner-Token.cmd</code> in the PilotTrade folder)</label>
          <div style={{ marginTop: 8 }}>
            <input id="owner-token" type="password" autoComplete="off" value={tokenInput} onChange={e => setTokenInput(e.target.value)} />
            <button type="submit" disabled={!tokenInput.trim() || busy > 0}>Connect</button>
          </div>
        </form>
        {busy > 0 && <p role="status">Connecting…</p>}
        {err && <p role="alert" style={error}>{err}</p>}
      </div>
    )
  }

  return (
    <div style={page} aria-busy={busy > 0}>
      <h1>PilotTrade</h1>
      <p>
        {runtime.mode} ({runtime.environment}) | Execution {runtime.executionEnabled ? 'ENABLED' : 'disabled'} | Live {runtime.liveLocked ? 'locked' : 'NOT LOCKED'}
      </p>
      <p role="status">{busy > 0 ? 'Loading…' : ''}</p>
      {err && <p role="alert" style={error}>{err}</p>}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(300px, 1fr))', gap: 16 }}>
        <section>
          <h3>Account</h3>
          <select aria-label="Account" value={accountId} onChange={e => void switchAccount(e.target.value)}>
            {accounts.map(a => <option key={a.accountId} value={a.accountId}>{a.name ?? a.accountId} ({a.accountId}) {a.currency ?? ''}</option>)}
          </select>
          <button onClick={() => void guard(() => loadAccount(accountId))} disabled={!accountId}>Refresh</button>
          {account && (
            <dl>
              <dt>Balance</dt><dd>{show(account.balance)} {account.currency}</dd>
              <dt>Equity{account.equityIsDerived ? ' (derived)' : ''}</dt><dd>{show(account.equity)}</dd>
              <dt>Free margin</dt><dd>{show(account.freeMargin)}</dd>
              <dt>Margin used</dt><dd>{show(account.marginUsed)}</dd>
              <dt>Unrealized P&amp;L</dt><dd>{show(account.unrealizedPnL)}</dd>
              <dt>Realized P&amp;L today</dt><dd>{show(account.realizedPnLToday)}</dd>
            </dl>
          )}
          <h3>Positions</h3>
          {positions.length === 0 ? <p>None</p> : <ul>{positions.map(p => <li key={p.positionId}>{p.epic} {SIDE[p.direction]} {p.size} @ {show(p.openLevel, 5)} | P&amp;L {show(p.unrealizedPnL)}</li>)}</ul>}
          <h3>Working orders</h3>
          {orders.length === 0 ? <p>None</p> : <ul>{orders.map(o => <li key={o.orderId}>{o.epic} {SIDE[o.direction]} {ORDER_TYPE[o.type]} {o.size} @ {show(o.level, 5)}</li>)}</ul>}
          <h3>No-order evidence</h3>
          <button onClick={() => void checkNoOrders()} disabled={!accountId}>Check that no order was sent</button>
          {noOrders && (
            <dl aria-label="No-order evidence">
              <dt>Result</dt><dd>{noOrders.noOrderSent ? 'No order sent' : 'AN ORDER WAS SENT'}</dd>
              <dt>Adapter</dt><dd>{noOrders.adapter}</dd>
              <dt>Journal entries</dt><dd>{noOrders.journalEntries} ({noOrders.journalEntriesSentToBroker} sent to the broker)</dd>
              <dt>Order intents</dt><dd>{noOrders.orderIntents}</dd>
              <dt>Broker write requests since start</dt><dd>{noOrders.brokerWriteRequestsSinceStart ?? 'not applicable (simulator)'}</dd>
              <dt>Checked (UTC)</dt><dd>{noOrders.checkedUtc}</dd>
            </dl>
          )}
          <h3>Demo connection</h3>
          <button onClick={() => void testConnection()}>Run read-only check</button>
          {evidence && (
            <>
              <p>{evidence.allPassed ? 'All steps passed' : 'One or more steps failed'} | Order endpoints called: {evidence.orderEndpointsCalled ? 'YES' : 'no'}</p>
              <ul>{evidence.steps.map(s => <li key={s.name}>{s.passed ? 'PASS' : 'FAIL'} {s.name}{s.detail ? `: ${s.detail}` : ''}</li>)}</ul>
            </>
          )}
        </section>
        <section>
          <h3>Instrument</h3>
          <form onSubmit={search}>
            <input aria-label="Search instruments" placeholder="Search any market" value={q} onChange={e => setQ(e.target.value)} />
            <button type="submit">Search</button>
          </form>
          <ul style={{ listStyle: 'none', padding: 0, maxHeight: 300, overflow: 'auto' }}>
            {instruments.map(i => (
              <li key={i.epic}>
                <button onClick={() => void select(i.epic)} aria-pressed={selection?.epic === i.epic} style={{ width: '100%', textAlign: 'left', background: selection?.epic === i.epic ? palette.selected : 'transparent', color: 'inherit' }}>
                  {i.name} ({i.epic}) | {MARKET_STATUS[i.status] ?? 'Unknown'} | {show(i.bid, 5)} / {show(i.offer, 5)}
                </button>
              </li>
            ))}
          </ul>
          {searched && instruments.length === 0 && <p>No instruments found.</p>}
        </section>
        <section>
          <h3>Selected: {selection ? `${selection.epic} (version ${selection.selectionVersion}${selection.selectedBy === 'restored' ? ', restored after restart' : ''})` : 'none'}</h3>
          {instrument && (
            <dl>
              <dt>Name</dt><dd>{instrument.name}</dd>
              <dt>Status</dt><dd>{MARKET_STATUS[instrument.status] ?? 'Unknown'}</dd>
              <dt>Currency</dt><dd>{instrument.quoteCurrency}</dd>
              <dt>Deal size</dt><dd>{instrument.minDealSize} to {instrument.maxDealSize}, step {instrument.dealSizeStep}</dd>
              <dt>Stop distance</dt><dd>{show(instrument.minStopDistance, 4)} {DISTANCE_UNIT[instrument.minStopDistanceUnit]} to {show(instrument.maxStopDistance, 4)} {DISTANCE_UNIT[instrument.maxStopDistanceUnit]}</dd>
            </dl>
          )}
          <button onClick={() => void buildSnapshot()} disabled={!selection}>Build market snapshot</button>
          {snapshot?.outcome === 'NO_TRADE' && <p role="status" style={error}>NO_TRADE: {snapshot.reason}</p>}
          {snapshot?.snapshot && (
            <dl>
              <dt>Bid / ask</dt><dd>{snapshot.snapshot.quote.bid} / {snapshot.snapshot.quote.ask}</dd>
              <dt>Spread</dt><dd>{snapshot.snapshot.quote.spread}</dd>
              <dt>Quote time (UTC)</dt><dd>{snapshot.snapshot.quote.sourceTimestampUtc}</dd>
            </dl>
          )}
        </section>
      </div>
    </div>
  )
}

const page: CSSProperties = { fontFamily: 'system-ui', padding: 16, background: palette.background, color: palette.text, minHeight: '100vh', boxSizing: 'border-box' }
const error: CSSProperties = { color: palette.error }
