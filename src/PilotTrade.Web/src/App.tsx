import { useCallback, useRef, useState } from 'react'
import type { CSSProperties, FormEvent } from 'react'

type Runtime = { mode: string; environment: string; accountId: string; executionEnabled: boolean; liveLocked: boolean }
type AccountSummary = { accountId: string; name: string | null; status: string | null; currency: string | null; preferred: boolean }
type InstrumentSummary = { epic: string; name: string; symbol: string | null; assetClass: number; status: number; bid: number | null; offer: number | null }
type InstrumentMetadata = {
  epic: string; name: string; status: number; quoteCurrency: string; pricePrecision: number
  minDealSize: number; maxDealSize: number; dealSizeStep: number
  minStopDistance: number | null; maxStopDistance: number | null; minStopDistanceUnit: number; maxStopDistanceUnit: number
}
type Selection = { epic: string; selectionVersion: number }
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
type ProbeEvidence = { allPassed: boolean; orderEndpointsCalled: boolean; steps: { name: string; passed: boolean; detail: string | null }[] }

const MARKET_STATUS = ['Unknown', 'Open', 'Closed', 'Pre-market', 'Post-market', 'Halted', 'Maintenance']
const SIDE = ['BUY', 'SELL']
const ORDER_TYPE = ['MARKET', 'LIMIT', 'STOP', 'STOP-LIMIT']
const DISTANCE_UNIT = ['points', '%']

// A missing broker value stays visibly missing; it is never shown as zero.
const show = (value: number | null | undefined, digits = 2) => (value === null || value === undefined ? 'not provided' : value.toFixed(digits))

class ApiError extends Error {}

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
  const [err, setErr] = useState('')
  // Bumped on every account or instrument change, so a slow reply for the old choice cannot overwrite the new one.
  const generation = useRef(0)

  const request = useCallback(async <T,>(path: string, init?: RequestInit): Promise<T> => {
    const headers = new Headers(init?.headers)
    headers.set('X-PilotTrade-Owner-Token', ownerToken.current)
    const r = await fetch(path, { ...init, headers })
    if (r.status === 403) throw new ApiError('Refused: check the owner token.')
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
    try { await work() } catch (e) { setErr(e instanceof ApiError ? e.message : 'The request could not be completed.') }
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

  const switchAccount = (id: string) => guard(async () => {
    clearInstrument()
    setAccountId(id)
    setSelection(null)
    setAccount(null)
    setPositions([])
    setOrders([])
    await loadAccount(id)
  })

  const connect = (e: FormEvent) => {
    e.preventDefault()
    ownerToken.current = tokenInput.trim()
    setTokenInput('')
    void guard(async () => {
      const rt = await request<Runtime>('/api/runtime')
      const list = await request<AccountSummary[]>('/api/accounts')
      setRuntime(rt)
      setAccounts(list)
      const initial = list.find(a => a.accountId === rt.accountId) ?? list.find(a => a.preferred) ?? list[0]
      if (initial) await switchAccount(initial.accountId)
    })
  }

  const search = (e: FormEvent) => {
    e.preventDefault()
    void guard(async () => setInstruments(await request<InstrumentSummary[]>(`/api/instruments/search?q=${encodeURIComponent(q)}&limit=50`)))
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

  const testConnection = () => guard(async () => setEvidence(await post<ProbeEvidence>('/api/demo/test-connection', {})))

  if (!runtime) {
    return (
      <div style={page}>
        <h1>PilotTrade</h1>
        <form onSubmit={connect}>
          <label htmlFor="owner-token">Owner token (from <code>PilotTrade.Api --show-owner-token</code>)</label>
          <div style={{ marginTop: 8 }}>
            <input id="owner-token" type="password" autoComplete="off" value={tokenInput} onChange={e => setTokenInput(e.target.value)} />
            <button type="submit" disabled={!tokenInput.trim()}>Connect</button>
          </div>
        </form>
        {err && <p role="alert" style={error}>{err}</p>}
      </div>
    )
  }

  return (
    <div style={page}>
      <h1>PilotTrade</h1>
      <p>
        {runtime.mode} ({runtime.environment}) | Execution {runtime.executionEnabled ? 'ENABLED' : 'disabled'} | Live {runtime.liveLocked ? 'locked' : 'NOT LOCKED'}
      </p>
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
                <button onClick={() => void select(i.epic)} aria-pressed={selection?.epic === i.epic} style={{ width: '100%', textAlign: 'left', background: selection?.epic === i.epic ? '#1f2a44' : 'transparent', color: 'inherit' }}>
                  {i.name} ({i.epic}) | {MARKET_STATUS[i.status] ?? 'Unknown'} | {show(i.bid, 5)} / {show(i.offer, 5)}
                </button>
              </li>
            ))}
          </ul>
        </section>
        <section>
          <h3>Selected: {selection ? `${selection.epic} (version ${selection.selectionVersion})` : 'none'}</h3>
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

const page: CSSProperties = { fontFamily: 'system-ui', padding: 16, background: '#0b1020', color: '#e6e6e6', minHeight: '100vh', boxSizing: 'border-box' }
const error: CSSProperties = { color: '#ff8080' }
