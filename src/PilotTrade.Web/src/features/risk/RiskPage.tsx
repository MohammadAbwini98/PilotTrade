import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import type { ConnectedSession } from '../../hooks/useOwnerSession'
import type { ClearCheck, RiskMetric, RiskProvenance } from '../../types/api'
import { executionLabel, liveLabel, modeLabel } from '../../lib/format'
import type { Tone } from '../../lib/format'

// Full date and time: a kill switch can stay engaged for days.
const utcIso = (iso: string | null | undefined) => (iso ? `${new Date(iso).toISOString().slice(0, 19).replace('T', ' ')} UTC` : 'not provided')

const PROVENANCE_TONE: Record<RiskProvenance, Tone> = { Known: 'ok', Derived: 'neutral', Unknown: 'warn' }
function Provenance({ p }: { p: RiskProvenance }) {
  return <span className={`badge tone-${PROVENANCE_TONE[p] ?? 'danger'}`}>{PROVENANCE_TONE[p] ? p : `Unknown (${p})`}</span>
}

const RESULT_TONE: Record<string, Tone> = { Pass: 'ok', Veto: 'danger', Unknown: 'warn' }

function amount(v: number | null, unit: string) {
  if (v === null) return 'Unknown'
  if (unit === 'count') return String(v)
  if (unit === '%') return `${v}%`
  if (unit === 's') return `${v} s`
  return `${v.toFixed(2)} ${unit}`
}

function MetricRow({ m }: { m: RiskMetric }) {
  return (
    <tr>
      <th scope="row">{m.label}</th>
      <td>{amount(m.current, m.unit)}</td>
      <td>{m.limit === null ? 'No limit' : amount(m.limit, m.unit)}</td>
      <td>{m.utilizationPercent === null ? '—' : `${m.utilizationPercent}%`}</td>
      <td>{m.breached === null ? '—' : m.breached ? <span className="tone-danger">At or over limit</span> : 'Within limit'}</td>
      <td><Provenance p={m.provenance} /></td>
      <td>{m.note}</td>
    </tr>
  )
}

function Checks({ checks, label }: { checks: ClearCheck[]; label: string }) {
  return (
    <ul aria-label={label}>
      {checks.map(c => (
        <li key={c.name}><span className={c.passed ? 'tone-ok' : 'tone-danger'}>{c.passed ? 'Pass' : 'Fail'}</span>: {c.name}. {c.detail}</li>
      ))}
    </ul>
  )
}

// Task 02-05. The stop is always offered, whatever else on this page failed to load. Clearing it needs the typed phrase and every
// server-side check; it enables nothing. This page has no control that enables orders, Demo execution or Live trading.
function KillSwitchPanel({ s }: { s: ConnectedSession }) {
  const [reason, setReason] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const k = s.killSwitch
  // The server's state decides whether a clear is offered; it is not offered while that state is unknown.
  const engaged = k?.engaged ?? s.status?.killSwitchEngaged ?? null
  const phrase = k?.clearPhrase ?? 'CLEAR KILL SWITCH'
  const maxLength = k?.maxReasonLength ?? 200

  const engage = (e: FormEvent) => { e.preventDefault(); if (reason.trim()) void s.engageKillSwitch(reason.trim()).then(() => setReason('')) }
  const clear = (e: FormEvent) => { e.preventDefault(); if (confirmation === phrase) void s.clearKillSwitch(confirmation).then(() => setConfirmation('')) }

  return (
    <section className="panel" aria-labelledby="rk-kill">
      <h2 id="rk-kill">Kill switch</h2>
      <p>State: {engaged === null ? 'Not available' : engaged ? <span className="tone-danger">Engaged</span> : 'Not engaged'}</p>
      {k && (
        <dl>
          <dt>Reason</dt><dd>{k.reason ?? 'none recorded'}</dd>
          <dt>Changed</dt><dd>{utcIso(k.changedUtc)}{k.changedBy ? ` by ${k.changedBy}` : ''}</dd>
          <dt>Startup reconciliation</dt><dd>{k.reconciliation}</dd>
        </dl>
      )}
      {s.killReadFailure && <p role="alert" className="tone-warn">Kill-switch state could not be read: {s.killReadFailure}</p>}
      {s.killFailure && <p role="alert" className="tone-danger">{s.killFailure}</p>}
      {s.killMessage && <p role="status">{s.killMessage}</p>}

      <form onSubmit={engage} aria-label="Engage kill switch">
        <label htmlFor="rk-reason">Reason for the stop</label>
        <input id="rk-reason" value={reason} maxLength={maxLength} onChange={e => setReason(e.target.value)} />
        <button type="submit" disabled={s.killBusy || !reason.trim()}>Engage kill switch</button>
        <p className="muted">Blocks every new order at once and is saved on the server, so it holds after a restart. It does not cancel or close anything at the broker.</p>
      </form>

      {engaged === true && (
        <form onSubmit={clear} aria-label="Clear kill switch">
          {(s.refusedChecks ?? k?.clearChecks)?.length ? <Checks checks={s.refusedChecks ?? k!.clearChecks} label="Checks before a clear" /> : null}
          <label htmlFor="rk-confirm">Type {phrase} to clear</label>
          <input id="rk-confirm" value={confirmation} autoComplete="off" onChange={e => setConfirmation(e.target.value)} />
          <button type="submit" disabled={s.killBusy || confirmation !== phrase}>Clear kill switch</button>
          <p className="muted">The server refuses the clear unless every check passes. Clearing does not enable orders: Demo order submission stays disabled and Live stays locked.</p>
        </form>
      )}
    </section>
  )
}

export function RiskPage({ s }: { s: ConnectedSession }) {
  useEffect(() => { void s.loadRisk() }, [s.accountId]) // eslint-disable-line react-hooks/exhaustive-deps
  const r = s.risk

  return (
    <div className="grid">
      <KillSwitchPanel s={s} />
      <section className="panel" aria-labelledby="rk-flags">
        <h2 id="rk-flags">Execution guards</h2>
        <dl>
          <dt>Mode</dt><dd>{modeLabel(s.runtime).text}</dd>
          <dt>Demo orders</dt><dd>{executionLabel(s.runtime).text}</dd>
          <dt>Live trading</dt><dd>{liveLabel(s.runtime).text}</dd>
        </dl>
        <p className="muted">Reported by the server. This page has no control that enables orders or Live trading.</p>
      </section>

      <section className="panel wide" aria-labelledby="rk-limits">
        <h2 id="rk-limits">Risk limits and utilization</h2>
        <button type="button" onClick={() => void s.loadRisk()}>Refresh risk</button>
        {s.riskFailure && <p role="alert" className="tone-danger">Risk data could not be loaded: {s.riskFailure}</p>}
        {!r ? <p className="muted">{s.riskFailure ? 'No risk figures are shown.' : 'Loading…'}</p> : (
          <>
            <p className="muted">
              Limits: {r.policySource}, read-only here (editing is task 02-09). Source: {r.brokerSource}. Checked {utcIso(r.checkedUtc)}.
              {' '}Unknown means a needed value is missing, so no figure is shown and the rule blocks.
            </p>
            {r.readErrors.length > 0 && (
              <ul aria-label="Read errors" className="tone-warn">{r.readErrors.map(e => <li key={e}>{e}</li>)}</ul>
            )}
            <div className="table-wrap">
              <table aria-label="Risk utilization">
                <thead><tr><th>Measure</th><th>Current</th><th>Limit</th><th>Used</th><th>State</th><th>Provenance</th><th>How</th></tr></thead>
                <tbody>{r.metrics.map(m => <MetricRow key={m.key} m={m} />)}</tbody>
              </table>
            </div>
          </>
        )}
      </section>

      {r && (
        <>
          <section className="panel" aria-labelledby="rk-account">
            <h2 id="rk-account">Account fields</h2>
            <div className="table-wrap">
              <table aria-label="Account fields">
                <thead><tr><th>Field</th><th>Value</th><th>Provenance</th><th>Source</th><th>As of</th></tr></thead>
                <tbody>
                  {r.accountFields.map(f => (
                    <tr key={f.name}>
                      <th scope="row">{f.name}</th>
                      <td>{f.value === null ? f.note ?? 'Unknown' : `${f.value.toFixed(2)} ${r.accountCurrency ?? ''}`.trim()}</td>
                      <td><Provenance p={f.provenance} /></td>
                      <td>{f.source}</td>
                      <td>{utcIso(f.asOfUtc)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>

          <section className="panel" aria-labelledby="rk-alerts">
            <h2 id="rk-alerts">Flags</h2>
            <ul aria-label="Risk flags">
              {r.flags.map(f => (
                <li key={f.key}>
                  <span className={f.raised === null ? 'tone-warn' : f.raised ? 'tone-danger' : 'tone-ok'}>{f.raised === null ? 'Unknown' : f.raised ? 'Raised' : 'Clear'}</span>: {f.label}. {f.detail}
                </li>
              ))}
            </ul>
          </section>

          <section className="panel wide" aria-labelledby="rk-rules">
            <h2 id="rk-rules">Risk rules</h2>
            <p className="muted">Account-level rules, checked now. Rules for a specific order run only when an order is proposed, and order submission is disabled in this build.</p>
            <div className="table-wrap">
              <table aria-label="Risk rules">
                <thead><tr><th>Rule</th><th>Result</th><th>Reason</th><th>Checked</th><th>Data as of</th></tr></thead>
                <tbody>
                  {r.rules.map(x => (
                    <tr key={x.rule}>
                      <th scope="row">{x.rule}</th>
                      <td><span className={`tone-${RESULT_TONE[x.result] ?? 'danger'}`}>{RESULT_TONE[x.result] ? x.result : `Unknown (${x.result})`}</span></td>
                      <td>{x.reason}</td>
                      <td>{utcIso(x.evaluatedUtc)}</td>
                      <td>{utcIso(x.inputAsOfUtc)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        </>
      )}
    </div>
  )
}
