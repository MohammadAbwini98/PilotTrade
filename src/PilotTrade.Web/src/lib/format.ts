import type { Runtime, Selection } from '../types/api'

export const MARKET_STATUS = ['Unknown', 'Open', 'Closed', 'Pre-market', 'Post-market', 'Halted', 'Maintenance']
export const SIDE = ['BUY', 'SELL']
export const ORDER_TYPE = ['MARKET', 'LIMIT', 'STOP', 'STOP-LIMIT']
export const DISTANCE_UNIT = ['points', '%']

// A missing broker value stays visibly missing; it is never shown as zero.
export const show = (value: number | null | undefined, digits = 2) => (value === null || value === undefined ? 'not provided' : value.toFixed(digits))

// Only the last four characters, so a screenshot of the header does not carry the full account identifier.
export const maskAccount = (id: string) => (id.length <= 4 ? '••••' : `••••${id.slice(-4)}`)

export const utcTime = (d: Date) => `${d.toISOString().slice(11, 19)} UTC`

export type Tone = 'neutral' | 'ok' | 'warn' | 'danger'

// Labels follow what the server reports. Simulation is not Capital.com execution, and read-only Demo is not permission to trade.
export function modeLabel(rt: Runtime): { text: string; tone: Tone } {
  if (rt.mode === 'Simulation') return { text: 'Simulation', tone: 'neutral' }
  // "Read-only" is claimed only while the server says demo execution is off.
  if (rt.mode === 'CapitalDemo') return rt.executionEnabled ? { text: 'Capital.com Demo', tone: 'warn' } : { text: 'Capital.com Demo / Read-only', tone: 'neutral' }
  return { text: `Unknown mode (${rt.mode})`, tone: 'danger' }
}

export function executionLabel(rt: Runtime): { text: string; tone: Tone } {
  if (rt.mode === 'CapitalDemo' && rt.executionEnabled) return { text: 'DEMO EXECUTION ENABLED', tone: 'danger' }
  return { text: 'Demo execution disabled', tone: 'ok' }
}

export const liveLabel = (rt: Runtime): { text: string; tone: Tone } =>
  rt.liveLocked ? { text: 'Live locked', tone: 'ok' } : { text: 'LIVE NOT LOCKED', tone: 'danger' }

// Positions and orders come from the simulator in Simulation mode, and from Capital.com otherwise.
export const dataSource = (rt: Runtime) => (rt.mode === 'Simulation' ? 'Simulated' : 'Broker-observed')

// One wording for the chosen instrument wherever it appears, so "restored after restart" is never dropped.
export const selectionText = (s: Selection | null) =>
  s ? `${s.epic} (version ${s.selectionVersion}${s.selectedBy === 'restored' ? ', restored after restart' : ''})` : 'none'

export const LINK_TEXT: Record<string, { text: string; tone: Tone }> = {
  unknown: { text: 'Not checked yet', tone: 'neutral' },
  connected: { text: 'Connected', tone: 'ok' },
  refused: { text: 'Refused', tone: 'danger' },
  'server-error': { text: 'Server error', tone: 'warn' },
  unreachable: { text: 'Unreachable', tone: 'danger' },
}

// Where a figure comes from: sent by the broker or simulator, worked out by the server, or missing.
export type Provenance = 'Reported' | 'Derived' | 'Unavailable'
export const provenance = (value: number | null | undefined, derived = false): Provenance =>
  value === null || value === undefined ? 'Unavailable' : derived ? 'Derived' : 'Reported'

export const TRADING_MODE: Record<string, { text: string; tone: Tone }> = {
  MonitorOnly: { text: 'Monitor only', tone: 'ok' },
  DemoAutomated: { text: 'Demo automated', tone: 'danger' },
  LiveLocked: { text: 'Live locked', tone: 'ok' },
  LiveArmed: { text: 'LIVE ARMED', tone: 'danger' },
  Suspended: { text: 'Suspended', tone: 'warn' },
}
export const tradingModeLabel = (mode: string) => TRADING_MODE[mode] ?? { text: `Unknown (${mode})`, tone: 'danger' as Tone }

export const FEED_TONE: Record<string, Tone> = { Healthy: 'ok', Stale: 'warn', Disconnected: 'danger', Unknown: 'neutral' }

// Task 02-07: a full UTC timestamp, and the browser's local time labelled as such.
export const utcStamp = (iso: string | null | undefined) => (iso ? `${new Date(iso).toISOString().slice(0, 19).replace('T', ' ')} UTC` : 'not recorded')
export const localStamp = (iso: string | null | undefined) => (iso ? new Date(iso).toLocaleString() : '')
export const DIAGNOSTIC_TONE: Record<string, Tone> = { Healthy: 'ok', Degraded: 'warn', Unavailable: 'danger', 'Not Configured': 'neutral', Unknown: 'neutral' }
export const utcIso = (iso: string | null | undefined) => (iso ? utcTime(new Date(iso)) : 'never')

// Task 02-03. Asset classes and market states as the monitor sends them (names), and as the metadata sends them (numbers).
export const ASSET_CLASS = ['Unknown', 'Fx', 'CryptoCfd', 'Commodity', 'Index', 'Share', 'Etf', 'Other']
export const ASSET_LABEL: Record<string, string> = {
  Unknown: 'Unknown', Fx: 'FX', CryptoCfd: 'Crypto CFD', Commodity: 'Commodity', Index: 'Index', Share: 'Share', Etf: 'ETF', Other: 'Other',
}
export const assetLabel = (name: string | undefined) => (name ? ASSET_LABEL[name] ?? `Unknown (${name})` : 'Unknown')
export const MARKET_STATE_LABEL: Record<string, string> = {
  Unknown: 'Unknown', Open: 'Open', Closed: 'Closed', PreMarket: 'Pre-market', PostMarket: 'Post-market', Halted: 'Halted', Maintenance: 'Maintenance',
}

// Length of one bar for a timeframe such as 5m, 1h or 1d; null when the timeframe is not understood.
export function barMs(timeframe: string): number | null {
  const m = /^(\d+)([mhd])$/.exec(timeframe)
  if (!m) return null
  return Number(m[1]) * { m: 60_000, h: 3_600_000, d: 86_400_000 }[m[2] as 'm' | 'h' | 'd']
}

// Age of the newest closed candle at the snapshot time, in bars, and how many times consecutive candles are more than one bar apart.
export function candleStats(candles: { openTimeUtc: string; closeTimeUtc: string; isClosed: boolean }[], timeframe: string, asOfUtc: string) {
  const bar = barMs(timeframe)
  const closed = candles.filter(c => c.isClosed)
  const last = closed.length ? closed[closed.length - 1] : null
  const ageBars = bar && last ? (Date.parse(asOfUtc) - Date.parse(last.closeTimeUtc)) / bar : null
  let gaps: number | null = null
  if (bar) {
    gaps = 0
    for (let i = 1; i < candles.length; i++) if (Date.parse(candles[i].openTimeUtc) - Date.parse(candles[i - 1].openTimeUtc) > bar) gaps++
  }
  return { count: closed.length, lastCloseUtc: last?.closeTimeUtc ?? null, ageBars, gaps }
}

type Limits = { maxQuoteAgeSeconds: number; minClosedCandles: number; maxCandleAgeInBars: number; maxSpreadPercent: Record<string, number> }

// The limit behind a NO_TRADE reason, so the owner sees why the snapshot was refused. Null when no limit applies.
export function limitFor(reason: string, assetClass: string | undefined, limits: Limits | null): string | null {
  if (reason.startsWith('Instrument not open')) return 'The market must be Open.'
  if (reason === 'Selection changed: request outdated') return 'The instrument changed after the request was sent.'
  if (reason === 'Spread limit unknown') return 'No spread limit is set for this asset class, so it is refused.'
  if (!limits) return null
  if (reason === 'Spread too wide') {
    const max = assetClass ? limits.maxSpreadPercent[assetClass] : undefined
    return max === undefined ? null : `Limit for ${assetLabel(assetClass)}: ${max}% of mid.`
  }
  if (reason === 'Stale quote') return `Quotes older than ${limits.maxQuoteAgeSeconds} s are refused.`
  if (/candles stale$/.test(reason)) return `The newest closed candle may be at most ${limits.maxCandleAgeInBars} bars old.`
  if (/candle history insufficient$/.test(reason)) return `At least ${limits.minClosedCandles} closed candles are needed.`
  return null
}
