import type { ConnectedSession } from '../../hooks/useOwnerSession'
import { ORDER_TYPE, SIDE, dataSource, show } from '../../lib/format'

// Read-only. There is no control here that could place, amend, cancel or close an order.
export function OrdersPage({ s }: { s: ConnectedSession }) {
  const { noOrders } = s
  return (
    <div className="grid">
      <section className="panel" aria-labelledby="op-positions">
        <h2 id="op-positions">Positions</h2>
        <p className="muted">Source: {dataSource(s.runtime)}</p>
        {s.positions.length === 0 ? <p>None</p> : <ul>{s.positions.map(p => <li key={p.positionId}>{p.epic} {SIDE[p.direction]} {p.size} @ {show(p.openLevel, 5)} | P&amp;L {show(p.unrealizedPnL)}</li>)}</ul>}
        <h2>Working orders</h2>
        {s.orders.length === 0 ? <p>None</p> : <ul>{s.orders.map(o => <li key={o.orderId}>{o.epic} {SIDE[o.direction]} {ORDER_TYPE[o.type]} {o.size} @ {show(o.level, 5)}</li>)}</ul>}
      </section>

      <section className="panel">
        <h2>No-order evidence</h2>
        <div className="toolbar">
          <button onClick={() => void s.checkNoOrders()} disabled={!s.accountId}>Check that no order was sent</button>
        </div>
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
      </section>
    </div>
  )
}
