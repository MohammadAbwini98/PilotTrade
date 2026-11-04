// The Owner Control Center's sections, in sidebar order. Each has its own address, so the browser's Back button works.
export const PAGES = [
  { id: 'overview', label: 'Overview' },
  { id: 'markets', label: 'Markets' },
  { id: 'ai', label: 'AI Decisions' },
  { id: 'risk', label: 'Risk Center' },
  { id: 'orders', label: 'Orders & Positions' },
  { id: 'audit', label: 'Audit & Alerts' },
  { id: 'broker', label: 'Broker Settings' },
  { id: 'strategy', label: 'Strategy & AI Settings' },
  { id: 'system', label: 'System' },
] as const

export type PageId = (typeof PAGES)[number]['id']

export const pageFromHash = (hash: string): PageId => PAGES.find(p => hash === `#/${p.id}`)?.id ?? 'overview'
