// The colours the app draws text on, kept here so a test can hold them to WCAG AA contrast.
// index.css repeats them as custom properties; keep the two in step.
export const palette = {
  background: '#0b1020',
  surface: '#131a2e',
  border: '#2b3654',
  text: '#e6e6e6',
  muted: '#a9b1c6',
  error: '#ff8080',
  warn: '#ffd27a',
  ok: '#7fd99a',
  selected: '#1f2a44',
  focus: '#ffbf47',
} as const
