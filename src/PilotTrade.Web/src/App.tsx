import { ConnectForm } from './app/ConnectForm'
import { Shell } from './app/Shell'
import { useOwnerSession } from './hooks/useOwnerSession'

// The Owner Control Center: a token form until the backend has answered, then the shell with its pages.
export default function App() {
  const s = useOwnerSession()
  if (!s.runtime) return <ConnectForm busy={s.busy} err={s.err} onConnect={s.connect} />
  return <Shell s={{ ...s, runtime: s.runtime }} />
}
