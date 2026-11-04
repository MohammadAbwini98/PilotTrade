import { useState } from 'react'
import type { FormEvent } from 'react'
import type { ConnectedSession } from '../../hooks/useOwnerSession'
import { DISTANCE_UNIT, MARKET_STATUS, selectionText, show } from '../../lib/format'

export function MarketsPage({ s }: { s: ConnectedSession }) {
  const [text, setText] = useState('')
  const submit = (e: FormEvent) => { e.preventDefault(); void s.search(text) }
  const { selection, instrument, snapshot } = s

  return (
    <div className="grid">
      <section className="panel" aria-labelledby="mk-search">
        <h2 id="mk-search">Instrument search</h2>
        <form onSubmit={submit} className="toolbar">
          <input aria-label="Search instruments" placeholder="Search any market" value={text} onChange={e => setText(e.target.value)} />
          <button type="submit">Search</button>
        </form>
        <ul className="results">
          {s.instruments.map(i => (
            <li key={i.epic}>
              <button onClick={() => void s.select(i.epic)} aria-pressed={selection?.epic === i.epic}>
                {i.name} ({i.epic}) | {MARKET_STATUS[i.status] ?? 'Unknown'} | {show(i.bid, 5)} / {show(i.offer, 5)}
              </button>
            </li>
          ))}
        </ul>
        {s.searched && s.instruments.length === 0 && <p>No instruments found.</p>}
      </section>

      <section className="panel">
        <h2>Selected: {selectionText(selection)}</h2>
        {instrument && (
          <dl>
            <dt>Name</dt><dd>{instrument.name}</dd>
            <dt>Status</dt><dd>{MARKET_STATUS[instrument.status] ?? 'Unknown'}</dd>
            <dt>Currency</dt><dd>{instrument.quoteCurrency}</dd>
            <dt>Deal size</dt><dd>{instrument.minDealSize} to {instrument.maxDealSize}, step {instrument.dealSizeStep}</dd>
            <dt>Stop distance</dt><dd>{show(instrument.minStopDistance, 4)} {DISTANCE_UNIT[instrument.minStopDistanceUnit]} to {show(instrument.maxStopDistance, 4)} {DISTANCE_UNIT[instrument.maxStopDistanceUnit]}</dd>
          </dl>
        )}
        <div className="toolbar">
          <button onClick={() => void s.buildSnapshot()} disabled={!selection}>Build market snapshot</button>
        </div>
        {snapshot?.outcome === 'NO_TRADE' && <p role="status" className="error">NO_TRADE: {snapshot.reason}</p>}
        {snapshot?.snapshot && (
          <dl>
            <dt>Bid / ask</dt><dd>{snapshot.snapshot.quote.bid} / {snapshot.snapshot.quote.ask}</dd>
            <dt>Spread</dt><dd>{snapshot.snapshot.quote.spread}</dd>
            <dt>Quote time (UTC)</dt><dd>{snapshot.snapshot.quote.sourceTimestampUtc}</dd>
          </dl>
        )}
      </section>
    </div>
  )
}
