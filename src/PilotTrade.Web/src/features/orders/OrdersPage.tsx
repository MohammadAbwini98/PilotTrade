import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import type { ConnectedSession } from '../../hooks/useOwnerSession'
import { LEDGER_PAGE } from '../../hooks/useOwnerSession'
import type { LedgerEntry, LedgerFilter } from '../../types/api'
import { show, utcIso } from '../../lib/format'
import type { Tone } from '../../lib/format'

// Task 02-06. Read-only: this page only reads the ledger. It has no control that could place, amend, cancel or close an order,
// and it sends no writes: the filters, the page buttons and Refresh each send one GET.
const STATUS_GROUPS = ['Pending', 'Unknown', 'Rejected', 'Cancelled', 'Completed']
const GROUP_TONE: Record<string, Tone> = { Pending: 'warn', Unknown: 'danger', Rejected: 'neutral', Cancelled: 'neutral', Completed: 'ok' }
const RECON_TONE: Record<string, Tone> = { Matched: 'ok', Partial: 'warn', 'Not held': 'warn', Blocked: 'danger', Unknown: 'danger', 'Not sent': 'neutral', 'Not applicable': 'neutral' }
const EVIDENCE_TONE: Record<string, Tone> = { 'Broker-observed': 'ok', 'Locally inferred': 'warn', Simulator: 'neutral', 'Unknown source': 'danger' }
const tone = (map: Record<string, Tone>, key: string): Tone => map[key] ?? 'danger'
const EMPTY: LedgerFilter = { q: '', epic: '', status: '', offset: 0 }

// A completed order whose reconciliation is blocked or unknown is never shown as completed.
function statusText(e: LedgerEntry): { text: string; tone: Tone } {
  if (e.statusGroup === 'Completed' && (e.reconciliation === 'Blocked' || e.reconciliation === 'Unknown'))
    return { text: `${e.state}, not reconciled`, tone: 'danger' }
  if (e.statusGroup === 'Unknown') return { text: `${e.state}: outcome unknown`, tone: 'danger' }
  return { text: e.state, tone: tone(GROUP_TONE, e.statusGroup) }
}

function Entry({ e }: { e: LedgerEntry }) {
  const st = statusText(e)
  return (
    <tr aria-label={`Order ${e.commandRef}`}>
      <td>{utcIso(e.createdUtc)}</td>
      <td>{e.commandRef}</td>
      <td>{e.epic}</td>
      <td>{e.side ?? 'Unknown'} {e.size ?? 'Unknown'}</td>
      <td className={`tone-${st.tone}`}>{st.text}</td>
      <td className={`tone-${tone(EVIDENCE_TONE, e.evidence)}`}>{e.evidence}</td>
      <td><span className={`tone-${tone(RECON_TONE, e.reconciliation)}`}>{e.reconciliation}</span><br /><span className="muted">{e.reconciliationDetail}</span></td>
      <td>
        <details>
          <summary>History and audit</summary>
          <dl aria-label={`Details for ${e.commandRef}`}>
            <dt>Intent</dt><dd>{e.intentRef}</dd>
            <dt>Account</dt><dd>{e.accountRef}</dd>
            <dt>Deal reference</dt><dd>{e.dealReference ?? 'none'}</dd>
            <dt>Deal ids</dt><dd>{e.dealIds ?? 'none'}</dd>
            <dt>Submitted (UTC)</dt><dd>{e.submittedUtc ? utcIso(e.submittedUtc) : 'not submitted'}</dd>
            <dt>Confirmed (UTC)</dt><dd>{e.confirmedUtc ? utcIso(e.confirmedUtc) : 'not confirmed'}</dd>
            <dt>Last change (UTC)</dt><dd>{utcIso(e.updatedUtc)}</dd>
            <dt>Reason</dt><dd>{e.reason ?? 'none'}</dd>
            {e.retryState && <><dt>Retry</dt><dd>{e.retryState}</dd></>}
          </dl>
          <table className="watch" aria-label={`State history for ${e.commandRef}`}>
            <thead><tr><th>When (UTC)</th><th>From</th><th>To</th><th>Source</th><th>Reason</th></tr></thead>
            <tbody>
              {e.transitions.length === 0
                ? <tr><td colSpan={5}>No recorded transitions. Entries created before this build have no history.</td></tr>
                : e.transitions.map((t, i) => (
                  <tr key={i}><td>{utcIso(t.occurredUtc)}</td><td>{t.fromState ?? 'created'}</td><td>{t.toState}</td><td>{t.source}</td><td>{t.reason ?? ''}</td></tr>
                ))}
            </tbody>
          </table>
          {e.audit.length === 0 ? <p className="muted">No audit events refer to this order.</p> : (
            <ul aria-label={`Audit events for ${e.commandRef}`}>
              {e.audit.map(a => (
                <li key={a.id}>{utcIso(a.occurredUtc)} {a.category}/{a.action} by {a.actor} (event {a.id}, matched by {a.refers}){a.sensitiveRedacted ? ', details redacted' : ''}</li>
              ))}
            </ul>
          )}
        </details>
      </td>
    </tr>
  )
}

export function OrdersPage({ s }: { s: ConnectedSession }) {
  const { noOrders, ledger, ledgerFailure } = s
  const [filter, setFilter] = useState<LedgerFilter>(EMPTY)
  useEffect(() => { setFilter(EMPTY); void s.loadLedger(EMPTY) }, [s.accountId]) // eslint-disable-line react-hooks/exhaustive-deps

  const set = (k: 'q' | 'epic' | 'status') => (ev: { target: { value: string } }) => setFilter(f => ({ ...f, [k]: ev.target.value }))
  const go = (f: LedgerFilter) => { setFilter(f); void s.loadLedger(f) }
  const submit = (ev: FormEvent) => { ev.preventDefault(); go({ ...filter, offset: 0 }) }
  const page = ledger?.journal
  const filtered = !!(filter.q || filter.epic || filter.status)

  return (
    <div className="grid">
      <section className="panel wide" aria-labelledby="op-ledger">
        <h2 id="op-ledger">Order ledger</h2>
        <div className="toolbar">
          <button type="button" onClick={() => void s.loadLedger(filter)}>Refresh ledger</button>
        </div>
        {ledgerFailure && <p role="alert" className="tone-danger">Ledger could not be read: {ledgerFailure}. Order states are Unknown, not empty.</p>}
        {!ledger ? <p className="muted">Ledger not loaded.</p> : (
          <>
            <dl aria-label="Ledger state">
              <dt>Source</dt><dd>{ledger.source}</dd>
              <dt>Account</dt><dd>{ledger.accountRef}</dd>
              <dt>Order submission</dt><dd className={ledger.executionEnabled ? 'tone-danger' : 'tone-neutral'}>{ledger.executionEnabled ? 'Enabled on the server' : 'Disabled (read-only)'}</dd>
              <dt>Startup reconciliation</dt><dd className={ledger.startup.complete ? 'tone-ok' : 'tone-warn'}>{ledger.startup.outcome}{ledger.startup.suspensionReason ? `: ${ledger.startup.suspensionReason}` : ''}</dd>
              <dt>Checked (UTC)</dt><dd>{utcIso(ledger.checkedUtc)}</dd>
            </dl>
            <ul className="badges" aria-label="Orders by status">
              {STATUS_GROUPS.map(g => <li key={g} className={`badge tone-${(ledger.statusCounts[g] ?? 0) > 0 ? GROUP_TONE[g] : 'neutral'}`}>{g}: {ledger.statusCounts[g] ?? 0}</li>)}
            </ul>
            {ledger.alerts.length > 0 && (
              <ul aria-label="Ledger alerts">
                {ledger.alerts.map((a, i) => <li key={i} className={`tone-${a.severity === 'info' ? 'neutral' : a.severity}`}>{a.message}</li>)}
              </ul>
            )}
          </>
        )}
      </section>

      <section className="panel wide" aria-labelledby="op-journal">
        <h2 id="op-journal">Order journal</h2>
        <form className="toolbar" onSubmit={submit} aria-label="Journal filters">
          <input aria-label="Search" placeholder="Search instrument or reference" maxLength={64} value={filter.q} onChange={set('q')} />
          <input aria-label="Instrument" placeholder="Instrument (epic)" maxLength={64} value={filter.epic} onChange={set('epic')} />
          <select aria-label="Status" value={filter.status} onChange={set('status')}>
            <option value="">Any status</option>
            {STATUS_GROUPS.map(g => <option key={g} value={g}>{g}</option>)}
          </select>
          <button type="submit">Apply filters</button>
        </form>
        {!page ? <p className="muted">Journal not loaded.</p>
          : page.items.length === 0 ? (
            <p aria-label="No journal entries">{page.total === 0 && !filtered && ledger!.journalTotal === 0
              ? 'No orders in the journal yet. Order submission is disabled in this build, so none is expected.'
              : 'No journal entries match these filters.'}</p>
          ) : (
            <>
              <p className="muted">Showing {page.offset + 1}–{page.offset + page.items.length} of {page.total}, newest first. IDs show their last four characters only.</p>
              <div className="table-wrap">
                <table className="watch" aria-label="Order journal">
                  <thead><tr><th>Created (UTC)</th><th>Command</th><th>Instrument</th><th>Side and size</th><th>State</th><th>Evidence</th><th>Reconciliation</th><th></th></tr></thead>
                  <tbody>{page.items.map(e => <Entry key={`${e.commandRef}-${e.createdUtc}`} e={e} />)}</tbody>
                </table>
              </div>
            </>
          )}
        {page && page.total > LEDGER_PAGE && (
          <div className="toolbar" aria-label="Journal pages">
            <button type="button" disabled={page.offset === 0} onClick={() => go({ ...filter, offset: Math.max(0, page.offset - LEDGER_PAGE) })}>Previous page</button>
            <button type="button" disabled={page.offset + page.items.length >= page.total} onClick={() => go({ ...filter, offset: page.offset + LEDGER_PAGE })}>Next page</button>
          </div>
        )}
      </section>

      <section className="panel wide" aria-labelledby="op-positions">
        <h2 id="op-positions">Positions</h2>
        {!ledger ? <p className="muted">Not loaded.</p> : ledger.positions === null ? <p className="tone-warn">Positions could not be read: Unknown.</p>
          : ledger.positions.length === 0 ? <p>None held at the broker.</p> : (
            <div className="table-wrap">
              <table className="watch" aria-label="Positions">
                <thead><tr><th>Position</th><th>Instrument</th><th>Side and size</th><th>Open level</th><th>Current level</th><th>Unrealized P&amp;L</th><th>Source</th><th>In journal</th><th>As of (UTC)</th></tr></thead>
                <tbody>
                  {ledger.positions.map(p => (
                    <tr key={p.positionRef}>
                      <td>{p.positionRef}</td><td>{p.epic}</td><td>{p.direction} {p.size}</td><td>{show(p.openLevel, 5)}</td><td>{show(p.currentLevel, 5)}</td>
                      <td>{show(p.unrealizedPnL)}{p.unrealizedPnL !== null && p.currency ? ` ${p.currency}` : ''}</td><td>{p.provenance}</td>
                      <td className={p.inJournal ? '' : 'tone-warn'}>{p.inJournal ? 'Yes' : 'No'}</td><td>{utcIso(p.asOfUtc)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        <h2>Working orders</h2>
        {!ledger ? <p className="muted">Not loaded.</p> : ledger.workingOrders === null ? <p className="tone-warn">Working orders could not be read: Unknown.</p>
          : ledger.workingOrders.length === 0 ? <p>None at the broker.</p> : (
            <div className="table-wrap">
              <table className="watch" aria-label="Working orders">
                <thead><tr><th>Order</th><th>Instrument</th><th>Side and size</th><th>Type</th><th>Level</th><th>Source</th><th>In journal</th><th>As of (UTC)</th></tr></thead>
                <tbody>
                  {ledger.workingOrders.map(o => (
                    <tr key={o.orderRef}>
                      <td>{o.orderRef}</td><td>{o.epic}</td><td>{o.direction} {o.size}</td><td>{o.type}</td><td>{show(o.level, 5)}</td><td>{o.provenance}</td>
                      <td className={o.inJournal ? '' : 'tone-warn'}>{o.inJournal ? 'Yes' : 'No'}</td><td>{utcIso(o.asOfUtc)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
      </section>

      <section className="panel wide" aria-labelledby="op-confirms">
        <h2 id="op-confirms">Broker confirmations</h2>
        <p className="muted">Journal entries that carry a broker deal reference. A simulator entry is labelled Simulator and is not a Capital.com confirmation.</p>
        {!ledger ? <p className="muted">Not loaded.</p> : ledger.confirms.length === 0 ? <p>No broker confirmations recorded.</p> : (
          <div className="table-wrap">
            <table className="watch" aria-label="Broker confirmations">
              <thead><tr><th>Command</th><th>Instrument</th><th>Deal reference</th><th>Deal ids</th><th>State</th><th>Evidence</th><th>Confirmed (UTC)</th></tr></thead>
              <tbody>
                {ledger.confirms.map(c => (
                  <tr key={`${c.commandRef}-${c.updatedUtc}`}>
                    <td>{c.commandRef}</td><td>{c.epic}</td><td>{c.dealReference}</td><td>{c.dealIds ?? 'none'}</td><td>{c.state}</td>
                    <td className={`tone-${tone(EVIDENCE_TONE, c.evidence)}`}>{c.evidence}</td><td>{c.confirmedUtc ? utcIso(c.confirmedUtc) : 'not confirmed'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
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
