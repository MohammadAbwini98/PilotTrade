import { useEffect, useState } from 'react'

type Instrument = { epic: string; name: string; assetClass: number; status: number; quoteCurrency: string; minDealSize: number; maxDealSize: number; dealSizeStep: number }
type Account = { accountId: string; currency: string; balance: number; equity: number; freeMargin: number }
type Position = { positionId: string; epic: string; direction: number; size: number; unrealizedPnL: number | null }

const API = ''

export default function App() {
  const [q, setQ] = useState('EUR')
  const [instruments, setInstruments] = useState<Instrument[]>([])
  const [selected, setSelected] = useState<string>('EURUSD')
  const [account, setAccount] = useState<Account | null>(null)
  const [positions, setPositions] = useState<Position[]>([])
  const [snapshot, setSnapshot] = useState<any>(null)
  const [conn, setConn] = useState<any>(null)
  const [err, setErr] = useState<string>('')

  const search = async () => {
    try {
      const r = await fetch(`${API}/api/instruments/search?q=${encodeURIComponent(q)}&limit=20`)
      const d = await r.json()
      setInstruments(d)
    } catch (e: any) { setErr(e.message) }
  }

  const loadAccount = async () => {
    try {
      const r = await fetch(`${API}/api/account?accountId=SIM-ACC`)
      setAccount(await r.json())
      const pr = await fetch(`${API}/api/positions?accountId=SIM-ACC`)
      setPositions(await pr.json())
    } catch (e: any) { setErr(e.message) }
  }

  const testConnection = async () => {
    try {
      const r = await fetch(`${API}/api/demo/test-connection`, { method: 'POST' })
      setConn(await r.json())
    } catch (e: any) { setErr(e.message) }
  }

  const buildSnapshot = async () => {
    try {
      const r = await fetch(`${API}/api/market/snapshot`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ accountId: 'SIM-ACC', environment: 0, epic: selected, selectionVersion: 1, timeframes: ['5m', '1h'] }) })
      setSnapshot(await r.json())
    } catch (e: any) { setErr(e.message) }
  }

  useEffect(() => { search(); loadAccount() }, [])

  return (
    <div style={{ fontFamily: 'system-ui', padding: 16, background: '#0b1020', color: '#e6e6e6', minHeight: '100vh' }}>
      <h1>PilotTrade — Capital.com Multi-Instrument (DEMO)</h1>
      <div style={{ display: 'grid', gridTemplateColumns: '320px 1fr', gap: 16 }}>
        <div>
          <h3>Instrument Search</h3>
          <input value={q} onChange={e => setQ(e.target.value)} style={{ width: '70%' }} />
          <button onClick={search}>Search</button>
          <ul style={{ listStyle: 'none', padding: 0, maxHeight: 300, overflow: 'auto' }}>
            {instruments.map(i => (
              <li key={i.epic} onClick={() => setSelected(i.epic)} style={{ padding: 6, cursor: 'pointer', background: selected === i.epic ? '#1f2a44' : 'transparent' }}>
                {i.name} ({i.epic}) [{i.quoteCurrency}]
              </li>
            ))}
          </ul>
          <h3>Account</h3>
          {account && <div>Balance {account.balance.toFixed(2)} {account.currency} | Equity {account.equity.toFixed(2)} | Free {account.freeMargin.toFixed(2)}</div>}
          <button onClick={loadAccount}>Refresh</button>
          <button onClick={testConnection}>Test Demo Connection</button>
          {conn && <pre style={{ background: '#111', padding: 8, fontSize: 12 }}>{JSON.stringify(conn, null, 2)}</pre>}
          <h3>Positions</h3>
          <ul>{positions.map(p => <li key={p.positionId}>{p.epic} {p.direction === 0 ? 'BUY' : 'SELL'} {p.size} PnL {p.unrealizedPnL?.toFixed(2)}</li>)}</ul>
        </div>
        <div>
          <h3>Selected: {selected}</h3>
          <button onClick={buildSnapshot}>Build Market Snapshot</button>
          {snapshot && <pre style={{ background: '#111', padding: 12, overflow: 'auto', maxHeight: 500 }}>{JSON.stringify(snapshot, null, 2)}</pre>}
          {err && <div style={{ color: 'red' }}>{err}</div>}
        </div>
      </div>
    </div>
  )
}