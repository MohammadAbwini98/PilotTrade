import { useState } from 'react'
import type { FormEvent } from 'react'

type Props = { busy: boolean; err: string; onConnect: (token: string) => void }

export function ConnectForm({ busy, err, onConnect }: Props) {
  const [token, setToken] = useState('')

  const submit = (e: FormEvent) => {
    e.preventDefault()
    const value = token.trim()
    if (!value) return
    // The typed token leaves the input straight away and lives only in the session's memory.
    setToken('')
    onConnect(value)
  }

  return (
    <div className="connect">
      <h1>PilotTrade</h1>
      <form onSubmit={submit}>
        <label htmlFor="owner-token">Owner token (run <code>Show-Owner-Token.cmd</code> in the PilotTrade folder)</label>
        <div className="row">
          <input id="owner-token" type="password" autoComplete="off" value={token} onChange={e => setToken(e.target.value)} />
          <button type="submit" disabled={!token.trim() || busy}>Connect</button>
        </div>
      </form>
      {busy && <p role="status">Connecting…</p>}
      {err && <p role="alert" className="error">{err}</p>}
    </div>
  )
}
