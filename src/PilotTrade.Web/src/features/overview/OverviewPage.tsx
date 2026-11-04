import type { ConnectedSession } from '../../hooks/useOwnerSession'
import { MARKET_STATUS, dataSource, executionLabel, liveLabel, maskAccount, modeLabel, selectionText, show } from '../../lib/format'

export function OverviewPage({ s }: { s: ConnectedSession }) {
  const { account } = s
  return (
    <div className="grid">
      <section className="panel" aria-labelledby="ov-account">
        <h2 id="ov-account">Account</h2>
        <div className="toolbar">
          <select aria-label="Account" value={s.accountId} onChange={e => void s.switchAccount(e.target.value)}>
            {s.accounts.map(a => <option key={a.accountId} value={a.accountId}>{a.name ?? 'Account'} ({maskAccount(a.accountId)}) {a.currency ?? ''}</option>)}
          </select>
          <button onClick={() => void s.refreshAccount()} disabled={!s.accountId}>Refresh</button>
        </div>
        {account ? (
          <dl>
            <dt>Balance</dt><dd>{show(account.balance)} {account.currency}</dd>
            <dt>Equity{account.equityIsDerived ? ' (derived)' : ''}</dt><dd>{show(account.equity)}</dd>
            <dt>Free margin</dt><dd>{show(account.freeMargin)}</dd>
            <dt>Margin used</dt><dd>{show(account.marginUsed)}</dd>
            <dt>Unrealized P&amp;L</dt><dd>{show(account.unrealizedPnL)}</dd>
            <dt>Realized P&amp;L today</dt><dd>{show(account.realizedPnLToday)}</dd>
          </dl>
        ) : <p className="muted">Account figures not loaded.</p>}
        <p className="muted">Source: {dataSource(s.runtime)}</p>
      </section>

      <section className="panel" aria-labelledby="ov-safety">
        <h2 id="ov-safety">Safety state</h2>
        <dl>
          <dt>Mode</dt><dd>{modeLabel(s.runtime).text}</dd>
          <dt>Demo orders</dt><dd>{executionLabel(s.runtime).text}</dd>
          <dt>Live trading</dt><dd>{liveLabel(s.runtime).text}</dd>
          <dt>Kill switch</dt><dd>Not available yet (task 02-05)</dd>
        </dl>
      </section>

      <section className="panel" aria-labelledby="ov-activity">
        <h2 id="ov-activity">Positions and orders</h2>
        <dl>
          <dt>Open positions</dt><dd>{account ? s.positions.length : 'not loaded'}</dd>
          <dt>Working orders</dt><dd>{account ? s.orders.length : 'not loaded'}</dd>
        </dl>
        <p className="muted">Details are on the Orders &amp; Positions page.</p>
      </section>

      <section className="panel">
        <h2>Selected: {selectionText(s.selection)}</h2>
        {s.instrument
          ? <dl><dt>Instrument</dt><dd>{s.instrument.name}</dd><dt>Market</dt><dd>{MARKET_STATUS[s.instrument.status] ?? 'Unknown'}</dd></dl>
          : <p className="muted">No instrument chosen. Choose one on the Markets page.</p>}
      </section>
    </div>
  )
}
