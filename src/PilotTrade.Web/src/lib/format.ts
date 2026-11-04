import type { Runtime } from '../types/api'
import type { Selection } from '../types/api'

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
