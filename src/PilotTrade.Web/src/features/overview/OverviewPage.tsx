import type { ReactNode } from 'react'
import type { ConnectedSession } from '../../hooks/useOwnerSession'
import { POLL } from '../../hooks/useOwnerSession'
import type { Tone } from '../../lib/format'
import {
  FEED_TONE, LINK_TEXT, MARKET_STATUS, dataSource, executionLabel, liveLabel, maskAccount, modeLabel, provenance, selectionText, show,
  tradingModeLabel, utcIso, utcTime,
} from '../../lib/format'

type Banner = { key: string; tone: Tone; text: string }

// The label of a figure, with where it came from. The value itself is never replaced by zero.
function Field({ label, value, derived = false, children }: { label: string; value: number | null | undefined; derived?: boolean; children: ReactNode }) {
  const source = provenance(value, derived)
  return (
    <>
      <dt>{label}<span className={`prov prov-${source.toLowerCase()}`}>{source}</span></dt>
      <dd>{children}</dd>
    </>
  )
}

// Collects the conditions the owner must see first. Each one comes from a reply, or from a reply that failed.
function banners(s: ConnectedSession, stale: boolean): Banner[] {
  const list: Banner[] = []
  const since = s.lastRefresh ? `last successful update ${utcTime(s.lastRefresh)}` : 'no successful update yet'
  if (s.link === 'refused' || s.link === 'unreachable' || s.link === 'server-error')
    list.push({ key: 'link', tone: 'danger', text: `Backend ${LINK_TEXT[s.link].text.toLowerCase()}: the figures below are stale (${since}).` })
  else if (stale && s.updateFailure)
    list.push({ key: 'update', tone: 'danger', text: `Update failed at ${utcTime(s.updateFailure.at)}: the figures below are stale (${since}).` })
  const feed = s.status?.feed
  if (feed && (feed.health === 'Stale' || feed.health === 'Disconnected'))
    list.push({ key: 'feed', tone: feed.health === 'Disconnected' ? 'danger' : 'warn', text: `Market feed for ${feed.epic} is ${feed.health.toLowerCase()} (last quote ${utcIso(feed.lastQuoteUtc)}).` })
  const a = s.account
  if (a) {
    const missing = [['Equity', a.equity], ['Free margin', a.freeMargin], ['Margin used', a.marginUsed], ['Realized P&L today', a.realizedPnLToday]]
      .filter(([, v]) => v === null).map(([k]) => k)
    if (missing.length) list.push({ key: 'risk', tone: 'warn', text: `Not reported by the broker, so risk checks cannot use them: ${missing.join(', ')}.` })
  }
  if (s.status?.killSwitchEngaged) list.push({ key: 'kill', tone: 'danger', text: 'Kill switch engaged: the server refuses every new order.' })
  if (s.status && !s.status.reconciliationComplete)
    list.push({ key: 'recon', tone: 'warn', text: `Reconciliation not complete${s.status.suspensionReason ? `: ${s.status.suspensionReason}` : ''}.` })
  if (s.statusFailed) list.push({ key: 'status', tone: 'warn', text: 'Server safety status could not be read: kill switch, reconciliation and feed health are unknown.' })
  const live = liveLabel(s.runtime)
  list.push({ key: 'live', tone: live.tone === 'ok' ? 'neutral' : 'danger', text: s.runtime.liveLocked ? 'Live trading is locked. There is no control to unlock it.' : 'The server reports Live trading as NOT locked.' })
  return list
}

export function OverviewPage({ s }: { s: ConnectedSession }) {
  const { account, status } = s
  const stale = s.link !== 'connected' || s.updateFailure !== null
  const staleNote = stale && account ? <p className="tone-warn">Stale: {s.lastRefresh ? `as of ${utcTime(s.lastRefresh)}` : 'never updated'}</p> : null
  const feed = status?.feed
  return (
    <>
      <ul className="banners" aria-label="Operational warnings">
        {banners(s, stale).map(b => <li key={b.key} className={`banner banner-${b.tone}`}>{b.text}</li>)}
      </ul>
      <div className="grid">
        <section className="panel" aria-labelledby="ov-account">
          <h2 id="ov-account">Account</h2>
          <div className="toolbar">
            <select aria-label="Account" value={s.accountId} onChange={e => void s.switchAccount(e.target.value)}>
              {s.accounts.map(a => <option key={a.accountId} value={a.accountId}>{a.name ?? 'Account'} ({maskAccount(a.accountId)}) {a.currency ?? ''}</option>)}
            </select>
            <button onClick={() => void s.refreshAccount()} disabled={!s.accountId}>Refresh</button>
          </div>
          {staleNote}
          {account ? (
            <dl>
              <Field label="Balance" value={account.balance}>{show(account.balance)} {account.currency}</Field>
              <Field label="Equity" value={account.equity} derived={account.equityIsDerived}>{show(account.equity)}</Field>
              <Field label="Free margin" value={account.freeMargin}>{show(account.freeMargin)}</Field>
              <Field label="Margin used" value={account.marginUsed}>{show(account.marginUsed)}</Field>
              <Field label="Unrealized P&L" value={account.unrealizedPnL}>{show(account.unrealizedPnL)}</Field>
              <Field label="Realized P&L today" value={account.realizedPnLToday}>{show(account.realizedPnLToday)}</Field>
            </dl>
          ) : <p className="muted">Account figures not loaded.</p>}
          <p className="muted">Source: {dataSource(s.runtime)}. Updates every {POLL.baseMs / 1000} s, waiting up to {POLL.maxMs / 1000} s after failures.</p>
        </section>

        <section className="panel" aria-labelledby="ov-safety">
          <h2 id="ov-safety">Safety state</h2>
          <dl>
            <dt>Mode</dt><dd>{modeLabel(s.runtime).text}</dd>
            <dt>Demo orders</dt><dd>{executionLabel(s.runtime).text}</dd>
            <dt>Live trading</dt><dd>{liveLabel(s.runtime).text}</dd>
            <dt>Kill switch</dt>
            <dd>{status ? (status.killSwitchEngaged ? <span className="tone-danger">Engaged</span> : 'Not engaged') : 'Not available'}</dd>
            <dt>Server trading mode</dt>
            <dd>{status ? <span className={`tone-${tradingModeLabel(status.tradingMode).tone}`}>{tradingModeLabel(status.tradingMode).text}</span> : 'Not available'}</dd>
            <dt>Reconciliation</dt>
            <dd>{status ? (status.reconciliationComplete ? 'Complete' : <span className="tone-warn">Not complete</span>) : 'Not available'}</dd>
          </dl>
          <p className="muted">{status ? `Reported by the server at ${utcIso(status.checkedUtc)}.` : 'Server safety status not loaded.'} Simulation capability is not Capital.com execution permission.</p>
        </section>

        <section className="panel" aria-labelledby="ov-activity">
          <h2 id="ov-activity">Positions and orders</h2>
          {staleNote}
          <dl>
            <dt>Open positions</dt><dd>{account ? s.positions.length : 'not loaded'}</dd>
            <dt>Working orders</dt><dd>{account ? s.orders.length : 'not loaded'}</dd>
          </dl>
          <p className="muted">Details are on the Orders &amp; Positions page.</p>
        </section>

        <section className="panel" aria-labelledby="ov-market">
          <h2 id="ov-market">Selected: {selectionText(s.selection)}</h2>
          {s.instrument
            ? <dl><dt>Instrument</dt><dd>{s.instrument.name}</dd><dt>Market</dt><dd>{MARKET_STATUS[s.instrument.status] ?? 'Unknown'}</dd></dl>
            : <p className="muted">No instrument chosen. Choose one on the Markets page.</p>}
          {feed && (
            <dl aria-label="Market feed">
              <dt>Feed health</dt>
              <dd className={`tone-${stale ? 'warn' : FEED_TONE[feed.health] ?? 'neutral'}`}>{feed.health}{stale ? ' (stale)' : ''}</dd>
              <dt>Last quote</dt><dd>{utcIso(feed.lastQuoteUtc)}</dd>
            </dl>
          )}
          {feed?.health === 'Unknown' && <p className="muted">No quote has been requested for this instrument yet; build a snapshot on the Markets page.</p>}
        </section>
      </div>
    </>
  )
}
