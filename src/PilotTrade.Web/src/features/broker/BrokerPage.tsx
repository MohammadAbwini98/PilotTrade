import { useEffect } from 'react'
import type { ConnectedSession } from '../../hooks/useOwnerSession'

const present = (v: boolean) => (v ? 'Stored' : 'Missing')

export function BrokerPage({ s }: { s: ConnectedSession }) {
  const { credentials, evidence, loadCredentialStatus } = s
  // Presence only: the backend reports whether each credential is stored in Windows Credential Manager, never the value.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => { void loadCredentialStatus() }, [])

  return (
    <div className="grid">
      <section className="panel" aria-labelledby="bk-creds">
        <h2 id="bk-creds">Capital.com Demo credentials</h2>
        {credentials ? (
          <dl aria-label="Credential presence">
            <dt>API key</dt><dd>{present(credentials.apiKeyPresent)}</dd>
            <dt>Identifier</dt><dd>{present(credentials.identifierPresent)}</dd>
            <dt>Password</dt><dd>{present(credentials.passwordPresent)}</dd>
          </dl>
        ) : <p className="muted">Credential status not loaded.</p>}
        <p className="muted">Stored in Windows Credential Manager. Changing them from this page is not available yet (task 02-08).</p>
      </section>

      <section className="panel" aria-labelledby="bk-check">
        <h2 id="bk-check">Demo connection</h2>
        <p className="muted">Runs read-only calls against Capital.com Demo. It never calls an order endpoint.</p>
        <div className="toolbar">
          <button onClick={() => void s.testConnection()}>Run read-only check</button>
        </div>
        {evidence && (
          <>
            <p>{evidence.allPassed ? 'All steps passed' : 'One or more steps failed'} | Order endpoints called: {evidence.orderEndpointsCalled ? 'YES' : 'no'}</p>
            <ul>{evidence.steps.map(st => <li key={st.name}>{st.passed ? 'PASS' : 'FAIL'} {st.name}{st.detail ? `: ${st.detail}` : ''}</li>)}</ul>
          </>
        )}
      </section>
    </div>
  )
}
