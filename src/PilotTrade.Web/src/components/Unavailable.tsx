import type { ReactNode } from 'react'

// Shown where the backend has no data or API yet. It never stands in with sample figures.
export function Unavailable({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="panel unavailable" aria-label={title}>
      <h2>{title}: Not available</h2>
      <p>{children}</p>
    </section>
  )
}
