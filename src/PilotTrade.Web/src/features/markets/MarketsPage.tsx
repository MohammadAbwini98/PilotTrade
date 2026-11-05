import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import type { ConnectedSession } from '../../hooks/useOwnerSession'
import type { MarketPolicy, WatchRow } from '../../types/api'
import {
  ASSET_CLASS, DISTANCE_UNIT, FEED_TONE, MARKET_STATE_LABEL, MARKET_STATUS, assetLabel, candleStats, limitFor, selectionText, show, utcIso,
} from '../../lib/format'

const MAX_WATCH = 10

// Seconds since an ISO time, measured on this computer's clock.
const ageSeconds = (iso: string, now: number) => Math.max(0, Math.round((now - Date.parse(iso)) / 1000))

function Readiness({ row, stale }: { row: WatchRow; stale: string | null }) {
  const blockers = stale ? [stale, ...row.blockers] : row.blockers
  if (blockers.length === 0) return <span className="tone-ok">Quote checks passed<br /><span className="muted">Candles are checked when a snapshot is built.</span></span>
  return <span className="tone-warn">Blocked<ul>{blockers.map(b => <li key={b}>{b}</li>)}</ul></span>
}

function WatchTable({ s, now }: { s: ConnectedSession; now: number }) {
  const { watch, watchlist } = s
  if (!watchlist) return <p className="muted">Watchlist not loaded.</p>
  if (watchlist.epics.length === 0) return <p className="muted">No instruments on the watchlist. Add up to {MAX_WATCH} from the search results.</p>
  if (!watch) return <p className="muted">Monitor not loaded: every instrument is Unknown.</p>
  const checkedAge = ageSeconds(watch.checkedUtc, now)
  // Rows older than the quote age limit, or read before the link was lost, are never shown as passing.
  const stale = s.link !== 'connected' ? 'Backend link lost: monitor data is stale'
    : checkedAge > watch.maxQuoteAgeSeconds ? `Monitor data stale (checked ${checkedAge} s ago)` : null
  return (
    <>
      {watch.watchlistVersion !== watchlist.version && <p className="tone-warn">These rows are for an earlier version of the list. Refresh the monitor.</p>}
      <div className="table-wrap">
        <table className="watch" aria-label="Watched instruments">
          <thead>
            <tr><th>Instrument</th><th>Class</th><th>Market</th><th>Bid / ask</th><th>Spread (limit)</th><th>Quote time (age)</th><th>Dealing</th><th>Readiness</th><th></th></tr>
          </thead>
          <tbody>
            {watch.rows.map(r => {
              const p = r.dealing?.pricePrecision ?? 5
              const health = stale ? 'Stale' : r.quote?.health ?? 'Unknown'
              return (
                <tr key={r.epic} aria-label={r.epic}>
                  <td>{r.epic}<br /><span className="muted">{r.name ?? 'name not provided'}</span></td>
                  <td>{assetLabel(r.assetClass)}</td>
                  <td className={r.marketStatus === 'Open' ? '' : 'tone-warn'}>{MARKET_STATE_LABEL[r.marketStatus] ?? 'Unknown'}</td>
                  <td>{r.quote ? `${show(r.quote.bid, p)} / ${show(r.quote.ask, p)}` : 'Unknown'}</td>
                  <td>{r.quote ? `${r.quote.spreadPercent.toFixed(4)}%` : 'Unknown'} ({r.maxSpreadPercent === null ? 'no limit' : `max ${r.maxSpreadPercent}%`})</td>
                  <td>{r.quote ? <>{utcIso(r.quote.sourceTimestampUtc)} ({Math.round(r.quote.ageSeconds)} s)<br /><span className={`tone-${FEED_TONE[health] ?? 'neutral'}`}>{health}</span></> : 'Unknown'}</td>
                  <td>{r.dealing
                    ? <>Size {r.dealing.minDealSize} to {r.dealing.maxDealSize}, step {r.dealing.dealSizeStep}<br />Tick {r.dealing.tickSize}, {p} decimals<br />
                        Stops {show(r.dealing.minStopDistance, 4)} {DISTANCE_UNIT[r.dealing.minStopDistanceUnit]} to {show(r.dealing.maxStopDistance, 4)} {DISTANCE_UNIT[r.dealing.maxStopDistanceUnit]}</>
                    : 'Unknown'}</td>
                  <td><Readiness row={r} stale={stale} /></td>
                  <td><button aria-label={`Remove ${r.epic} from watchlist`} onClick={() => void s.removeFromWatchlist(r.epic)} disabled={s.busy}>Remove</button></td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
      <p className="muted">Checked {utcIso(watch.checkedUtc)} ({checkedAge} s ago). The monitor never selects an instrument, signals or places orders.</p>
    </>
  )
}

function Limits({ policy }: { policy: MarketPolicy | null }) {
  if (!policy) return <p className="muted">Limits not loaded.</p>
  return (
    <>
      <dl aria-label="Market data limits">
        {Object.entries(policy.maxSpreadPercent).map(([cls, max]) => <span key={cls} style={{ display: 'contents' }}><dt>Max spread, {assetLabel(cls)}</dt><dd>{max}% of mid</dd></span>)}
        <dt>Max quote age</dt><dd>{policy.maxQuoteAgeSeconds} s</dd>
        <dt>Closed candles needed</dt><dd>{policy.minClosedCandles}</dd>
        <dt>Max candle age</dt><dd>{policy.maxCandleAgeInBars} bars</dd>
      </dl>
      <p className="muted">Source: {policy.source}. {policy.editable ? '' : 'Not editable here; limit settings come in a later task.'} Any other asset class has no limit and is refused.</p>
    </>
  )
}

export function MarketsPage({ s }: { s: ConnectedSession }) {
  const [text, setText] = useState('')
  const [now, setNow] = useState(() => Date.now())
  const submit = (e: FormEvent) => { e.preventDefault(); void s.search(text) }
  const { selection, instrument, snapshot, watchlist, policy } = s

  // Loaded when the page opens and again for another account; refreshed only on request, to spare the broker.
  useEffect(() => { void s.openMarkets() }, [s.accountId]) // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { const t = setInterval(() => setNow(Date.now()), 5_000); return () => clearInterval(t) }, [])

  const listed = new Set(watchlist?.epics ?? [])
  const full = listed.size >= MAX_WATCH
  const assetClass = instrument?.assetClass === undefined ? undefined : ASSET_CLASS[instrument.assetClass]
  const precision = instrument?.pricePrecision ?? 5

  // Decision readiness for the chosen instrument. It is never shown as ready while the market is closed or the data is old.
  const blockers: string[] = []
  if (instrument && instrument.status !== 1) blockers.push(`Market ${MARKET_STATUS[instrument.status] ?? 'Unknown'}`)
  const snap = snapshot?.snapshot
  if (snapshot?.outcome === 'NO_TRADE' && snapshot.reason) blockers.push(snapshot.reason)
  if (snap && policy && ageSeconds(snap.asOfUtc, now) > policy.maxQuoteAgeSeconds) blockers.push('Snapshot stale: build it again')
  if (snap && s.link !== 'connected') blockers.push('Backend link lost')
  const readiness = !selection ? null
    : blockers.length ? { tone: 'warn', text: `Blocked: ${blockers.join('; ')}` }
      : snap ? { tone: 'ok', text: 'Snapshot inputs passed. No AI decision has been made.' }
        : { tone: 'neutral', text: 'Unknown: no snapshot built yet.' }

  return (
    <div className="grid">
      <section className="panel" aria-labelledby="mk-search">
        <h2 id="mk-search">Instrument search</h2>
        <form onSubmit={submit} className="toolbar">
          <input aria-label="Search instruments" placeholder="FX, crypto, commodities, indices, shares" value={text} onChange={e => setText(e.target.value)} />
          <button type="submit">Search</button>
        </form>
        <ul className="results">
          {s.instruments.map(i => (
            <li key={i.epic}>
              <button onClick={() => void s.select(i.epic)} aria-pressed={selection?.epic === i.epic}>
                {i.name} ({i.epic}) | {assetLabel(ASSET_CLASS[i.assetClass])} | {MARKET_STATUS[i.status] ?? 'Unknown'} | {show(i.bid, 5)} / {show(i.offer, 5)}
              </button>
              <button aria-label={`Add ${i.epic} to watchlist`} onClick={() => void s.addToWatchlist(i.epic)}
                disabled={!watchlist || listed.has(i.epic) || full || s.busy}>{listed.has(i.epic) ? 'Watched' : 'Watch'}</button>
            </li>
          ))}
        </ul>
        {s.searched && s.instruments.length === 0 && <p>No instruments found.</p>}
        {full && <p className="muted">The watchlist holds at most {MAX_WATCH} instruments.</p>}
      </section>

      <section className="panel">
        <h2>Selected: {selectionText(selection)}</h2>
        {instrument && (
          <dl>
            <dt>Name</dt><dd>{instrument.name}</dd>
            <dt>Class</dt><dd>{assetLabel(assetClass)}</dd>
            <dt>Status</dt><dd>{MARKET_STATUS[instrument.status] ?? 'Unknown'}</dd>
            <dt>Currency</dt><dd>{instrument.quoteCurrency}</dd>
            <dt>Deal size</dt><dd>{instrument.minDealSize} to {instrument.maxDealSize}, step {instrument.dealSizeStep}</dd>
            <dt>Stop distance</dt><dd>{show(instrument.minStopDistance, 4)} {DISTANCE_UNIT[instrument.minStopDistanceUnit]} to {show(instrument.maxStopDistance, 4)} {DISTANCE_UNIT[instrument.maxStopDistanceUnit]}</dd>
          </dl>
        )}
        <div className="toolbar">
          <button onClick={() => void s.buildSnapshot()} disabled={!selection}>Build market snapshot</button>
        </div>
        {readiness && <p aria-label="Decision readiness" className={`tone-${readiness.tone}`}>{readiness.text}</p>}
        {snapshot?.outcome === 'NO_TRADE' && (
          <p role="status" className="error">NO_TRADE: {snapshot.reason}
            {snapshot.reason && limitFor(snapshot.reason, assetClass, policy) && <><br /><span className="muted">{limitFor(snapshot.reason, assetClass, policy)}</span></>}
          </p>
        )}
        {snap && (
          <dl>
            <dt>Bid / ask</dt><dd>{show(snap.quote.bid, precision)} / {show(snap.quote.ask, precision)}</dd>
            <dt>Spread</dt><dd>{show(snap.quote.spread, precision)}</dd>
            <dt>Quote time (UTC)</dt><dd>{utcIso(snap.quote.sourceTimestampUtc)} (age {ageSeconds(snap.quote.sourceTimestampUtc, now)} s)</dd>
            {Object.entries(snap.candlesByTimeframe ?? {}).map(([tf, candles]) => {
              const c = candleStats(candles, tf, snap.asOfUtc)
              return (
                <span key={tf} style={{ display: 'contents' }}>
                  <dt>{tf} candles</dt>
                  <dd>{c.count} closed, newest closed {utcIso(c.lastCloseUtc)}{c.ageBars === null ? ', age unknown' : ` (${c.ageBars.toFixed(1)} bars old)`};{' '}
                    {c.gaps === null ? 'gaps unknown' : c.gaps === 0 ? 'no gaps' : <span className="tone-warn">{c.gaps} gap{c.gaps === 1 ? '' : 's'}</span>}</dd>
                </span>
              )
            })}
          </dl>
        )}
      </section>

      <section className="panel wide" aria-labelledby="mk-watch">
        <h2 id="mk-watch">Market monitor</h2>
        <div className="toolbar">
          <button onClick={() => void s.refreshWatch()} disabled={!watchlist || watchlist.epics.length === 0 || s.busy}>Refresh monitor</button>
          <span className="muted">{watchlist ? `${watchlist.epics.length} of ${MAX_WATCH} watched, list version ${watchlist.version}` : ''}</span>
        </div>
        <WatchTable s={s} now={now} />
      </section>

      <section className="panel" aria-labelledby="mk-limits">
        <h2 id="mk-limits">Snapshot limits</h2>
        <Limits policy={policy} />
      </section>
    </div>
  )
}
