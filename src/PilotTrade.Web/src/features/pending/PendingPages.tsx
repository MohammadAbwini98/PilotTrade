import { Unavailable } from '../../components/Unavailable'

// Sections whose backend APIs do not exist yet. They say so rather than showing sample data.
export function AuditPage() {
  return (
    <div className="grid">
      <Unavailable title="Audit log and alerts">The backend has no API for reading the audit log or alerts yet (task 02-07).</Unavailable>
    </div>
  )
}

export function StrategyPage() {
  return (
    <div className="grid">
      <Unavailable title="Strategy and AI settings">Settings cannot be read or saved from the browser yet (task 02-09). Allowed AI providers: Claude API and JEV/TypeSafe AI.</Unavailable>
    </div>
  )
}
