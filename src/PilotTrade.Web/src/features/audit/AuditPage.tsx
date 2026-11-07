import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import type { ConnectedSession } from '../../hooks/useOwnerSession'
import { AUDIT_PAGE } from '../../hooks/useOwnerSession'
import type { AlertItem, AuditFilter } from '../../types/api'
import { localStamp, utcStamp } from '../../lib/format'
import type { Tone } from '../../lib/format'

// Task 02-07. Read-only: this page sends GET requests only. Alerts are the conditions the server observes now; nothing here
// acknowledges, resolves, notifies or acts on them. Audit records show no stored details, and references are masked.
const EMPTY: AuditFilter = { from: '', to: '', category: '', epic: '', correlation: '', q: '', offset: 0 }
const SEVERITY: Record<string, { text: string; tone: Tone }> = {
  danger: { text: 'Critical', tone: 'danger' }, warn: { text: 'Warning', tone: 'warn' }, info: { text: 'Information', tone: 'neutral' },
}
const severity = (s: string) => SEVERITY[s] ?? { text: `Unknown (${s})`, tone: 'danger' as Tone }

function When({ iso }: { iso: string | null }) {
  if (!iso) return <span className="muted">Time not recorded</span>
  return <>{utcStamp(iso)}<br /><span className="muted">Local time: {localStamp(iso)}</span></>
}

function Alert({ a }: { a: AlertItem }) {
  const sv = severity(a.severity)
  return (
    <tr aria-label={`Alert ${a.title}`}>
      <td className={`tone-${sv.tone}`}>{sv.text}</td>
      <td>{a.category}</td>
      <td><strong>{a.title}</strong><br /><span className="muted">{a.detail}</span></td>
      <td><When iso={a.observedUtc} /></td>
      <td>{a.source}</td>
    </tr>
  )
}

export function AuditPage({ s }: { s: ConnectedSession }) {
  const { audit, auditFailure, auditLoading, alerts, alertsFailure, alertsLoading } = s
  const [filter, setFilter] = useState<AuditFilter>(EMPTY)
  const [applied, setApplied] = useState<AuditFilter>(EMPTY)
  const [formError, setFormError] = useState('')
  useEffect(() => {
    setFilter(EMPTY); setApplied(EMPTY); setFormError('')
    void s.loadAlerts(); void s.loadAudit(EMPTY)
  }, [s.accountId]) // eslint-disable-line react-hooks/exhaustive-deps

  const set = (k: Exclude<keyof AuditFilter, 'offset'>) => (ev: { target: { value: string } }) => setFilter(f => ({ ...f, [k]: ev.target.value }))
  const go = (f: AuditFilter) => { setApplied(f); void s.loadAudit(f) }
  const submit = (ev: FormEvent) => {
    ev.preventDefault()
    if (filter.from && filter.to && filter.from > filter.to) { setFormError('The From date must not be after the To date.'); return }
    if (/\s/.test(filter.correlation.trim())) { setFormError('A correlation ID has no spaces.'); return }
    setFormError('')
    const f = { ...filter, offset: 0 }
    setFilter(f); go(f)
  }
  const clear = () => { setFilter(EMPTY); setFormError(''); go(EMPTY) }
  const filtered = !!(applied.from || applied.to || applied.category || applied.epic || applied.correlation || applied.q)

  return (
    <div className="grid">
      <section className="panel wide" aria-labelledby="au-alerts" aria-busy={alertsLoading}>
        <h2 id="au-alerts">Alerts</h2>
        <div className="toolbar">
          <button type="button" onClick={() => void s.loadAlerts()} disabled={alertsLoading}>{alertsLoading ? 'Checking…' : 'Refresh alerts'}</button>
        </div>
        {alertsFailure && <p role="alert" className="tone-danger">Alerts could not be read: {alertsFailure}. Their state is Unknown, not clear.</p>}
        {!alerts ? (!alertsFailure && <p className="muted">{alertsLoading ? 'Checking current conditions…' : 'Alerts not loaded.'}</p>) : (
          <>
            <p className="muted">Checked {utcStamp(alerts.checkedUtc)} (local time: {localStamp(alerts.checkedUtc)}).</p>
            {alerts.alerts.length === 0 ? <p aria-label="No alerts">No alert conditions were observed in this check.</p> : (
              <div className="table-wrap">
                <table className="watch" aria-label="Current alerts">
                  <thead><tr><th>Severity</th><th>Category</th><th>Alert</th><th>Observed</th><th>Source</th></tr></thead>
                  <tbody>{alerts.alerts.map(a => <Alert key={a.key} a={a} />)}</tbody>
                </table>
              </div>
            )}
            <p className="muted">{alerts.note}</p>
          </>
        )}
      </section>

      <section className="panel wide" aria-labelledby="au-audit" aria-busy={auditLoading}>
        <h2 id="au-audit">Audit explorer</h2>
        <form className="toolbar" onSubmit={submit} aria-label="Audit filters">
          <label>From (UTC date) <input type="date" value={filter.from} onChange={set('from')} /></label>
          <label>To (UTC date) <input type="date" value={filter.to} onChange={set('to')} /></label>
          <select aria-label="Category" value={filter.category} onChange={set('category')}>
            <option value="">Any category</option>
            {(audit?.categories ?? []).map(c => <option key={c} value={c}>{c}</option>)}
          </select>
          <input aria-label="Instrument" placeholder="Instrument (epic)" maxLength={64} value={filter.epic} onChange={set('epic')} />
          <input aria-label="Correlation ID" placeholder="Correlation ID" maxLength={128} value={filter.correlation} onChange={set('correlation')} />
          <input aria-label="Search" placeholder="Search category, action, actor" maxLength={64} value={filter.q} onChange={set('q')} />
          <button type="submit">Apply filters</button>
          <button type="button" onClick={clear}>Clear</button>
        </form>
        {formError && <p role="alert" className="tone-warn">{formError}</p>}
        {auditFailure && <p role="alert" className="tone-danger">Audit log could not be read: {auditFailure}.</p>}
        {!audit ? (!auditFailure && <p className="muted">{auditLoading ? 'Reading the audit log…' : 'Audit log not loaded.'}</p>)
          : audit.items.length === 0 ? (
            <p aria-label="No audit records">{filtered ? 'No audit records match these filters.' : 'No audit records for this account yet.'}</p>
          ) : (
            <>
              <p className="muted">Showing {audit.offset + 1}–{audit.offset + audit.items.length} of {audit.total}, newest first. Search is case-sensitive. References show their last four characters only, and stored details are never shown.</p>
              <div className="table-wrap">
                <table className="watch" aria-label="Audit records">
                  <thead><tr><th>Time</th><th>Category</th><th>Action</th><th>Actor</th><th>Scope</th><th>Instrument</th><th>Reference</th><th>Correlation</th></tr></thead>
                  <tbody>
                    {audit.items.map((a, i) => (
                      <tr key={i}>
                        <td><When iso={a.occurredUtc} /></td>
                        <td>{a.category}</td>
                        <td>{a.action}</td>
                        <td>{a.actor}</td>
                        <td>{a.scope}</td>
                        <td>{a.epic ?? ''}</td>
                        <td>{a.reference ?? 'none'}{a.sensitiveRedacted ? <><br /><span className="muted">Sensitive values redacted</span></> : null}</td>
                        <td>{a.correlationRef ?? 'none'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          )}
        {audit && audit.total > AUDIT_PAGE && (
          <div className="toolbar" aria-label="Audit pages">
            <button type="button" disabled={auditLoading || audit.offset === 0} onClick={() => go({ ...applied, offset: Math.max(0, audit.offset - AUDIT_PAGE) })}>Previous page</button>
            <button type="button" disabled={auditLoading || audit.offset + audit.items.length >= audit.total} onClick={() => go({ ...applied, offset: audit.offset + AUDIT_PAGE })}>Next page</button>
          </div>
        )}
        {audit && (
          <dl aria-label="Audit data handling">
            <dt>Classification</dt><dd>{audit.classification}</dd>
            <dt>Retention</dt><dd>{audit.retention}</dd>
          </dl>
        )}
      </section>
    </div>
  )
}
