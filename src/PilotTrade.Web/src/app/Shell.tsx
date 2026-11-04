import { useEffect, useRef } from 'react'
import type { MouseEvent } from 'react'
import { Badge } from '../components/Badge'
import { AiDecisionsPage, AuditPage, StrategyPage } from '../features/pending/PendingPages'
import { BrokerPage } from '../features/broker/BrokerPage'
import { MarketsPage } from '../features/markets/MarketsPage'
import { OrdersPage } from '../features/orders/OrdersPage'
import { OverviewPage } from '../features/overview/OverviewPage'
import { RiskPage } from '../features/risk/RiskPage'
import { SystemPage } from '../features/system/SystemPage'
import type { ConnectedSession } from '../hooks/useOwnerSession'
import { LINK_TEXT, executionLabel, liveLabel, maskAccount, modeLabel, utcTime } from '../lib/format'
import { ErrorBoundary } from './ErrorBoundary'
import { PAGES } from './pages'
import type { PageId } from './pages'
import { useHashPage } from './useHashPage'

function Page({ id, s }: { id: PageId; s: ConnectedSession }) {
  switch (id) {
    case 'overview': return <OverviewPage s={s} />
    case 'markets': return <MarketsPage s={s} />
    case 'ai': return <AiDecisionsPage />
    case 'risk': return <RiskPage s={s} />
    case 'orders': return <OrdersPage s={s} />
    case 'audit': return <AuditPage />
    case 'broker': return <BrokerPage s={s} />
    case 'strategy': return <StrategyPage />
    case 'system': return <SystemPage s={s} />
  }
}

export function Shell({ s }: { s: ConnectedSession }) {
  const [page, go] = useHashPage()
  const heading = useRef<HTMLHeadingElement>(null)
  const firstRender = useRef(true)
  const label = PAGES.find(p => p.id === page)!.label
  const link = LINK_TEXT[s.link]

  // After a page change, focus moves to the new page's heading so a screen reader announces it.
  useEffect(() => {
    if (firstRender.current) { firstRender.current = false; return }
    heading.current?.focus()
  }, [page])

  const open = (id: PageId) => (e: MouseEvent<HTMLAnchorElement>) => {
    if (e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return
    e.preventDefault()
    go(id)
  }

  return (
    <div className="shell" aria-busy={s.busy}>
      <header className="shell-header">
        <span className="brand">PilotTrade</span>
        <ul className="badges" aria-label="Safety state">
          <Badge {...modeLabel(s.runtime)} />
          <Badge {...executionLabel(s.runtime)} />
          <Badge {...liveLabel(s.runtime)} />
        </ul>
        <dl aria-label="Session">
          <div><dt>Account</dt><dd>{maskAccount(s.runtime.accountId)} ({s.runtime.environment})</dd></div>
          <div><dt>Instrument</dt><dd>{s.selection?.epic ?? 'none'}</dd></div>
          <div><dt>Backend</dt><dd className={`tone-${link.tone}`}>{link.text}</dd></div>
          <div><dt>Last refresh</dt><dd>{s.lastRefresh ? utcTime(s.lastRefresh) : 'Not available'}</dd></div>
        </dl>
      </header>

      <nav className="sidebar" aria-label="Main">
        <ul>
          {PAGES.map(p => (
            <li key={p.id}>
              <a href={`#/${p.id}`} aria-current={p.id === page ? 'page' : undefined} onClick={open(p.id)}>{p.label}</a>
            </li>
          ))}
        </ul>
      </nav>

      <main className="main">
        <h1 ref={heading} tabIndex={-1}>{label}</h1>
        <div className="messages">
          <p role="status">{s.busy ? 'Loading…' : ''}</p>
          {s.err && <p role="alert" className="error">{s.err}</p>}
        </div>
        <ErrorBoundary key={page}>
          <Page id={page} s={s} />
        </ErrorBoundary>
      </main>
    </div>
  )
}
