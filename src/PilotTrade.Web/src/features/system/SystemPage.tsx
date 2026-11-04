import type { ConnectedSession } from '../../hooks/useOwnerSession'
import { LINK_TEXT, maskAccount, modeLabel, utcTime } from '../../lib/format'

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
    </div>
  )
}
