import { useEffect } from 'react'
import type { ConnectedSession } from '../../hooks/useOwnerSession'
import { DIAGNOSTIC_TONE, LINK_TEXT, localStamp, maskAccount, modeLabel, utcStamp, utcTime } from '../../lib/format'

const statusTone = (status: string) => DIAGNOSTIC_TONE[status] ?? 'danger'

// Task 02-07. One GET; nothing here changes the server. A failed check shows Unknown, never a remembered Healthy.
function Diagnostics({ s }: { s: ConnectedSession }) {
  const { diagnostics: d, diagnosticsFailure, diagnosticsLoading } = s
  useEffect(() => { void s.loadDiagnostics() }, [s.accountId]) // eslint-disable-line react-hooks/exhaustive-deps
  return (
    <section className="panel wide" aria-labelledby="sy-diagnostics" aria-busy={diagnosticsLoading}>
      <h2 id="sy-diagnostics">Diagnostics</h2>
      <div className="toolbar">
        <button type="button" onClick={() => void s.loadDiagnostics()} disabled={diagnosticsLoading}>{diagnosticsLoading ? 'Checking…' : 'Run diagnostics'}</button>
      </div>
      {diagnosticsFailure && <p role="alert" className="tone-danger">Diagnostics could not be read: {diagnosticsFailure}. Component status is Unknown.</p>}
      {!d ? (!diagnosticsFailure && <p className="muted">{diagnosticsLoading ? 'Checking components…' : 'Diagnostics not loaded.'}</p>) : (
        <>
          <dl aria-label="Diagnostics summary">
            <dt>Overall</dt><dd className={`tone-${statusTone(d.overall)}`}>{d.overall}</dd>
            <dt>Version</dt><dd>{d.version}</dd>
            <dt>Runtime mode</dt><dd>{d.runtimeMode}</dd>
            <dt>Order submission</dt><dd className="tone-neutral">{d.execution}</dd>
            <dt>Live trading</dt><dd className="tone-neutral">{d.live}</dd>
            <dt>Account</dt><dd>{d.accountRef}</dd>
            <dt>Checked</dt><dd>{utcStamp(d.checkedUtc)} <span className="muted">(local time: {localStamp(d.checkedUtc)})</span></dd>
          </dl>
          <div className="table-wrap">
            <table className="watch" aria-label="Component status">
              <thead><tr><th>Component</th><th>Status</th><th>Detail</th><th>As of (UTC)</th></tr></thead>
              <tbody>
                {d.components.map(c => (
                  <tr key={c.key} aria-label={`Component ${c.label}`}>
                    <td>{c.label}</td>
                    <td className={`tone-${statusTone(c.status)}`}>{c.status}</td>
                    <td>{c.detail}</td>
                    <td>{c.asOfUtc ? utcStamp(c.asOfUtc) : 'not recorded'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <h3>Recent failures</h3>
          {d.failures.length === 0 ? <p aria-label="No failures">No operational failures in this check.</p> : (
            <ul aria-label="Operational failures">
              {d.failures.map((f, i) => <li key={i}>{f.occurredUtc ? utcStamp(f.occurredUtc) : 'Time not recorded'}: {f.source}: {f.detail}</li>)}
            </ul>
          )}
          <p className="muted">{d.note}</p>
        </>
      )}
    </section>
  )
}

export function SystemPage({ s }: { s: ConnectedSession }) {
  return (
    <div className="grid">
      <section className="panel" aria-labelledby="sy-runtime">
        <h2 id="sy-runtime">Runtime</h2>
        <dl>
          <dt>Mode</dt><dd>{modeLabel(s.runtime).text}</dd>
          <dt>Environment</dt><dd>{s.runtime.environment}</dd>
          <dt>Configured account</dt><dd>{maskAccount(s.runtime.accountId)}</dd>
          <dt>Backend link</dt><dd>{LINK_TEXT[s.link].text}</dd>
          <dt>Last account refresh</dt><dd>{s.lastRefresh ? utcTime(s.lastRefresh) : 'Not available'}</dd>
        </dl>
      </section>
      <section className="panel" aria-labelledby="sy-session">
        <h2 id="sy-session">Session</h2>
        <p>The owner token is held in this tab's memory only. Reloading or closing the tab forgets it.</p>
        <div className="toolbar">
          <button onClick={s.disconnect}>Disconnect</button>
        </div>
      </section>
      <Diagnostics s={s} />
    </div>
  )
}
