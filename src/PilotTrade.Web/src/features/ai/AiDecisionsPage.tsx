import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import type { ConnectedSession } from '../../hooks/useOwnerSession'
import type { AiStep, DecisionFilter, DecisionRecord } from '../../types/api'
import type { Tone } from '../../lib/format'
// Full date and time: history spans days, so the time of day alone is not enough.
const utcIso = (iso: string | null | undefined) => (iso ? `${new Date(iso).toISOString().slice(0, 19).replace('T', ' ')} UTC` : 'never')

// Task 02-04. Read-only: this page lists recorded AI decisions and never asks a provider anything or places an order.
const STEP_LABEL: Record<string, { text: string; tone: Tone }> = {
  Disabled: { text: 'AI disabled', tone: 'neutral' },
  Unavailable: { text: 'Unavailable', tone: 'warn' },
  TimedOut: { text: 'Timed out', tone: 'warn' },
  Rejected: { text: 'Rejected: invalid reply', tone: 'danger' },
  Succeeded: { text: 'Succeeded', tone: 'ok' },
  NotRun: { text: 'Not run', tone: 'neutral' },
}
const stepLabel = (status: string) => STEP_LABEL[status] ?? { text: `Unknown (${status})`, tone: 'danger' as Tone }

const DECISION_LABEL: Record<string, string> = { Buy: 'BUY candidate', Sell: 'SELL candidate', Hold: 'HOLD', NoTrade: 'NO_TRADE' }

// A BUY or SELL is shown only when both steps succeeded. Anything else is shown as NO_TRADE, whatever the record says.
function shownDecision(d: DecisionRecord): { text: string; reason: string | null; inconsistent: boolean } {
  const trade = d.decision === 'Buy' || d.decision === 'Sell'
  if (trade && (d.claude.status !== 'Succeeded' || d.jev.status !== 'Succeeded'))
    return { text: 'NO_TRADE', reason: 'Inconsistent record: a step did not succeed', inconsistent: true }
  const known = DECISION_LABEL[d.decision]
  if (!known) return { text: 'NO_TRADE', reason: `Unknown decision (${d.decision})`, inconsistent: true }
  return { text: known, reason: d.noTradeReason, inconsistent: false }
}

const notProvided = (v: number | null, unit = '') => (v === null ? 'not provided' : `${v}${unit}`)

function Step({ step }: { step: AiStep }) {
  const l = stepLabel(step.status)
  return <span className={`tone-${l.tone}`}>{l.text}</span>
}

function StepDetail({ step }: { step: AiStep }) {
  return (
    <section aria-label={`${step.provider} step`}>
      <h3>{step.provider}</h3>
      <dl>
        <dt>Status</dt><dd><Step step={step} />{step.problem ? ` (${step.problem})` : ''}</dd>
        <dt>Answer</dt><dd>{step.output ?? 'none'}</dd>
        <dt>Model</dt><dd>{step.model ?? 'not provided'}</dd>
        <dt>Reply schema</dt><dd>{step.schemaValid === null ? 'no reply parsed' : step.schemaValid ? 'valid' : 'invalid'}</dd>
        <dt>Confidence</dt><dd>{step.confidence === null ? 'not provided' : step.confidence}</dd>
        <dt>Latency</dt><dd>{step.latencyMs === null ? 'not provided' : `${Math.round(step.latencyMs)} ms`}</dd>
        <dt>Tokens in / out</dt><dd>{notProvided(step.inputTokens)} / {notProvided(step.outputTokens)}</dd>
        <dt>Cost</dt><dd>{step.costUsd === null ? 'not provided' : `${step.costUsd} USD`}</dd>
      </dl>
    </section>
  )
}

function Detail({ d, close }: { d: DecisionRecord; close: () => void }) {
  const shown = shownDecision(d)
  return (
    <section className="panel wide" aria-labelledby="ai-detail">
      <h2 id="ai-detail">Decision {d.decisionId}</h2>
      <div className="toolbar"><button onClick={close}>Close details</button></div>
      <dl>
        <dt>Decision</dt><dd className={shown.inconsistent ? 'tone-danger' : ''}>{shown.text}{shown.reason ? `: ${shown.reason}` : ''}</dd>
        <dt>Instrument</dt><dd>{d.epic} (selection version {d.selectionVersion})</dd>
        <dt>Snapshot</dt><dd>{d.snapshotId}, as of {utcIso(d.snapshotAsOfUtc)}</dd>
        <dt>Started / recorded</dt><dd>{utcIso(d.startedUtc)} / {utcIso(d.recordedUtc)}</dd>
        <dt>Audit event</dt><dd>{d.auditEventId}</dd>
        <dt>Strategy</dt><dd>{d.strategyId} {d.strategyVersion}</dd>
        <dt>Risk veto</dt><dd>{d.riskVeto ?? 'not evaluated: decisions are not turned into orders in this build'}</dd>
        <dt>Prompt</dt><dd>{d.promptSha256 ? <>SHA-256 {d.promptSha256}, {d.promptChars} characters</> : 'not sent'}. Prompt text is not stored.</dd>
      </dl>
      <div className="grid">
        <StepDetail step={d.claude} />
        <StepDetail step={d.jev} />
      </div>
    </section>
  )
}

const EMPTY: DecisionFilter = { epic: '', decision: '', status: '' }

export function AiDecisionsPage({ s }: { s: ConnectedSession }) {
  const [filter, setFilter] = useState<DecisionFilter>(EMPTY)
  const { aiStatus, decisions, decision } = s

  useEffect(() => { void s.loadDecisions(EMPTY) }, [s.accountId]) // eslint-disable-line react-hooks/exhaustive-deps

  const submit = (e: FormEvent) => { e.preventDefault(); void s.loadDecisions(filter) }
  const set = (k: keyof DecisionFilter) => (e: { target: { value: string } }) => setFilter(f => ({ ...f, [k]: e.target.value }))

  return (
    <div className="grid">
      <section className="panel" aria-labelledby="ai-status">
        <h2 id="ai-status">AI providers</h2>
        {!aiStatus ? <p className="muted">Status not loaded: AI state is Unknown.</p> : (
          <>
            <p className={aiStatus.runtimeWired ? 'tone-warn' : 'tone-neutral'}>
              {aiStatus.runtimeWired ? 'Runtime AI: connected' : 'Runtime AI: not connected in this build'}
            </p>
            <p className="muted">{aiStatus.note}</p>
            <ul aria-label="Providers">
              {aiStatus.providers.map(p => (
                <li key={p.name}>{p.name}: {p.registered ? 'client registered' : 'client not registered'}, API key {p.credentialPresent ? 'present' : 'missing'}</li>
              ))}
            </ul>
            <p className="muted">Allowed providers: {aiStatus.allowedProviders.join(' and ')}. No other model is used, and no answer is substituted when one fails.</p>
          </>
        )}
      </section>

      <section className="panel wide" aria-labelledby="ai-history">
        <h2 id="ai-history">Decision history</h2>
        <form className="toolbar" onSubmit={submit} aria-label="Decision filters">
          <input aria-label="Instrument" placeholder="Instrument (epic)" value={filter.epic} onChange={set('epic')} />
          <select aria-label="Decision" value={filter.decision} onChange={set('decision')}>
            <option value="">Any decision</option>
            <option value="NoTrade">NO_TRADE</option>
            <option value="Buy">BUY</option>
            <option value="Sell">SELL</option>
            <option value="Hold">HOLD</option>
          </select>
          <select aria-label="Step status" value={filter.status} onChange={set('status')}>
            <option value="">Any step status</option>
            {Object.entries(STEP_LABEL).map(([k, v]) => <option key={k} value={k}>{v.text}</option>)}
          </select>
          <button type="submit" disabled={s.busy}>Refresh</button>
        </form>
        {!decisions ? <p className="muted">History not loaded.</p>
          : decisions.items.length === 0 ? (
            <p aria-label="No decisions">No AI decisions recorded{decisions.total === 0 && !filter.epic && !filter.decision && !filter.status
              ? '. Runtime AI is not connected, so none is expected yet.' : ' for these filters.'}</p>
          ) : (
            <>
              <p className="muted">Showing {decisions.items.length} of {decisions.total}, newest first. A BUY or SELL candidate is not an order.</p>
              <div className="table-wrap">
                <table aria-label="AI decisions">
                  <thead><tr><th>Recorded (UTC)</th><th>Instrument</th><th>Claude</th><th>JEV</th><th>Decision</th><th>NO_TRADE reason</th><th></th></tr></thead>
                  <tbody>
                    {decisions.items.map(d => {
                      const shown = shownDecision(d)
                      return (
                        <tr key={d.decisionId} aria-label={d.decisionId}>
                          <td>{utcIso(d.recordedUtc)}</td>
                          <td>{d.epic} (v{d.selectionVersion})</td>
                          <td><Step step={d.claude} /></td>
                          <td><Step step={d.jev} /></td>
                          <td className={shown.inconsistent ? 'tone-danger' : ''}>{shown.text}</td>
                          <td>{shown.reason ?? ''}</td>
                          <td><button aria-label={`Details for ${d.decisionId}`} onClick={() => void s.openDecision(d.decisionId)}>Details</button></td>
                        </tr>
                      )
                    })}
                  </tbody>
                </table>
              </div>
            </>
          )}
      </section>

      {decision && <Detail d={decision} close={s.closeDecision} />}
    </div>
  )
}
