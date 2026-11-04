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
      <Unavailable title="Kill switch">The backend has no kill-switch API yet; it arrives with task 02-05.</Unavailable>
      <Unavailable title="Risk limits">Risk limits cannot be read or edited from the browser yet (task 02-09).</Unavailable>
    </div>
  )
}
