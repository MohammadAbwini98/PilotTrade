import { Unavailable } from '../../components/Unavailable'
import type { ConnectedSession } from '../../hooks/useOwnerSession'
import { executionLabel, liveLabel, modeLabel } from '../../lib/format'

export function RiskPage({ s }: { s: ConnectedSession }) {
  return (
    <div className="grid">
      <section className="panel" aria-labelledby="rk-flags">
        <h2 id="rk-flags">Execution guards</h2>
        <dl>
          <dt>Mode</dt><dd>{modeLabel(s.runtime).text}</dd>
          <dt>Demo orders</dt><dd>{executionLabel(s.runtime).text}</dd>
          <dt>Live trading</dt><dd>{liveLabel(s.runtime).text}</dd>
        </dl>
        <p className="muted">Reported by the server. This page has no control that enables orders or Live trading.</p>
      </section>
      <section className="panel" aria-labelledby="rk-kill">
        <h2 id="rk-kill">Kill switch</h2>
        <p>State: {s.status ? (s.status.killSwitchEngaged ? <span className="tone-danger">Engaged</span> : 'Not engaged') : 'Not available'}</p>
        <p className="muted">Read-only. Engaging it from the browser is not available yet (task 02-05).</p>
      </section>
      <Unavailable title="Risk limits">Risk limits cannot be read or edited from the browser yet (task 02-09).</Unavailable>
    </div>
  )
}
