import type { Tone } from '../lib/format'

// A short state label. The tone is also carried by the words, never by colour alone.
export function Badge({ text, tone }: { text: string; tone: Tone }) {
  return <li className={`badge tone-${tone}`}>{text}</li>
}
