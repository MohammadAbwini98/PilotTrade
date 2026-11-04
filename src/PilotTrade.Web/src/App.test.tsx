import css from './index.css?raw'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import App from './App'
import { palette } from './theme'

// A fake PilotTrade backend. Every request is recorded, so a test can prove what the page did and did not send.
type Reply = { status: number; body?: unknown } | 'network-error'
type Route = Reply | ((url: URL, init: RequestInit) => Reply | Promise<Reply>)
type Call = { method: string; path: string; url: URL; headers: Headers; body: unknown }

const TOKEN = 'test-owner-token-not-a-real-secret'
const CSRF = 'csrf-123'

const account = { accountId: 'ACC-A', currency: 'USD', balance: 1000, equity: 1010, freeMargin: 900, marginUsed: 100, unrealizedPnL: 10, realizedPnLToday: 0, equityIsDerived: false }
const metadata = (epic: string) => ({
  epic, name: `${epic} market`, status: 1, quoteCurrency: 'USD', pricePrecision: 5,
  minDealSize: 1, maxDealSize: 100, dealSizeStep: 1, minStopDistance: null, maxStopDistance: 50, minStopDistanceUnit: 0, maxStopDistanceUnit: 0,
})
const noOrders = { checkedUtc: '2026-11-04T12:00:00Z', mode: 'Simulation', environment: 'Demo', accountId: 'ACC-A', adapter: 'simulated', executionEnabled: false, liveLocked: true, journalEntries: 0, journalEntriesSentToBroker: 0, journalStates: {}, orderIntents: 0, brokerWriteRequestsSinceStart: null, noOrderSent: true }

let routes: Record<string, Route>
let calls: Call[]

function defaults(): Record<string, Route> {
  return {
    'GET /api/runtime': { status: 200, body: { mode: 'Simulation', environment: 'Demo', accountId: 'ACC-A', executionEnabled: false, liveLocked: true } },
    'GET /api/accounts': { status: 200, body: [
      { accountId: 'ACC-B', name: 'Preferred other', status: 'ENABLED', currency: 'EUR', preferred: true },
      { accountId: 'ACC-A', name: 'Configured', status: 'ENABLED', currency: 'USD', preferred: false },
    ] },
    'GET /api/account': { status: 200, body: account },
    'GET /api/positions': { status: 200, body: [] },
    'GET /api/orders': { status: 200, body: [] },
    'GET /api/instruments/selection': { status: 404 },
    'GET /api/security/csrf-token': { status: 200, body: { csrfToken: CSRF } },
    'GET /api/instruments/search': { status: 200, body: [
      { epic: 'EURUSD', name: 'EUR/USD', symbol: null, assetClass: 0, status: 1, bid: 1.1, offer: null },
      { epic: 'GOLD', name: 'Gold', symbol: null, assetClass: 0, status: 2, bid: null, offer: null },
    ] },
    'POST /api/instruments/select': (_url, init) => {
      const { epic } = JSON.parse(String(init.body)) as { epic: string }
      return { status: 200, body: { selection: { epic, selectionVersion: epic === 'GOLD' ? 3 : 2 }, instrument: metadata(epic) } }
    },
    'POST /api/market/snapshot': { status: 200, body: { outcome: 'NO_TRADE', reason: 'Market closed', snapshot: null } },
    'GET /api/evidence/no-orders': { status: 200, body: noOrders },
  }
}

function routeFor(method: string, path: string): Route | undefined {
  if (routes[`${method} ${path}`]) return routes[`${method} ${path}`]
  // GET /api/instruments/{epic} revalidates a restored selection.
  if (method === 'GET' && path.startsWith('/api/instruments/')) return routes['GET /api/instruments/{epic}']
  return undefined
}

beforeEach(() => {
  routes = defaults()
  calls = []
  vi.stubGlobal('fetch', async (input: string, init: RequestInit = {}) => {
    const url = new URL(input, 'http://127.0.0.1:5099')
    const method = init.method ?? 'GET'
    calls.push({ method, path: url.pathname, url, headers: new Headers(init.headers), body: init.body ? JSON.parse(String(init.body)) : undefined })
    const route = routeFor(method, url.pathname)
    const reply = typeof route === 'function' ? await route(url, init) : route ?? { status: 500, body: { error: 'unexpected request' } }
    if (reply === 'network-error') throw new TypeError('Failed to fetch')
    return { status: reply.status, ok: reply.status < 400, json: async () => reply.body ?? null } as Response
  })
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const deferred = <T,>() => {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(r => { resolve = r })
  return { promise, resolve }
}

async function connect(user = userEvent.setup()) {
  render(<App />)
  await user.type(screen.getByLabelText(/owner token/i), TOKEN)
  await user.click(screen.getByRole('button', { name: 'Connect' }))
  await screen.findByRole('heading', { name: /selected:/i })
  await waitFor(() => expect(screen.getByText('Balance')).toBeTruthy())
  return user
}

async function searchAndSelect(user: ReturnType<typeof userEvent.setup>, epic: string) {
  await user.type(screen.getByRole('textbox', { name: 'Search instruments' }), 'a')
  await user.click(screen.getByRole('button', { name: 'Search' }))
  await user.click(await screen.findByRole('button', { name: new RegExp(`\\(${epic}\\)`) }))
}

const posts = () => calls.filter(c => c.method !== 'GET')

describe('owner token', () => {
  it('cannot connect without a token, hides it while typed and clears it afterwards', async () => {
    const user = userEvent.setup()
    render(<App />)
    const input = screen.getByLabelText(/owner token/i) as HTMLInputElement
    expect(input.type).toBe('password')
    expect((screen.getByRole('button', { name: 'Connect' }) as HTMLButtonElement).disabled).toBe(true)
    await user.type(input, '   ')
    expect((screen.getByRole('button', { name: 'Connect' }) as HTMLButtonElement).disabled).toBe(true)
    await user.clear(input)
    await user.type(input, TOKEN)
    await user.click(screen.getByRole('button', { name: 'Connect' }))
    await screen.findByRole('heading', { name: /selected:/i })
    expect(document.body.textContent).not.toContain(TOKEN)
  })

  it('sends the token as a header on every request and never in a URL', async () => {
    const user = await connect()
    await searchAndSelect(user, 'EURUSD')
    expect(calls.length).toBeGreaterThan(5)
    for (const c of calls) {
      expect(c.headers.get('X-PilotTrade-Owner-Token')).toBe(TOKEN)
      expect(c.url.href).not.toContain(TOKEN)
    }
  })

  it('names the packaged helper that shows the token', () => {
    render(<App />)
    expect(screen.getByText('Show-Owner-Token.cmd')).toBeTruthy()
  })

  it('shows a refusal for a wrong token and stays on the token form', async () => {
    routes['GET /api/runtime'] = { status: 403 }
    const user = userEvent.setup()
    render(<App />)
    await user.type(screen.getByLabelText(/owner token/i), 'wrong')
    await user.click(screen.getByRole('button', { name: 'Connect' }))
    expect((await screen.findByRole('alert')).textContent).toBe('Refused: check the owner token.')
    expect(screen.getByLabelText(/owner token/i)).toBeTruthy()
  })

  it('shows the server error code for other failures', async () => {
    routes['GET /api/runtime'] = { status: 503, body: { error: 'BrokerUnavailable' } }
    const user = userEvent.setup()
    render(<App />)
    await user.type(screen.getByLabelText(/owner token/i), TOKEN)
    await user.click(screen.getByRole('button', { name: 'Connect' }))
    expect((await screen.findByRole('alert')).textContent).toBe('Request failed (503: BrokerUnavailable).')
  })
})

describe('guarded POST', () => {
  it('fetches a CSRF token first and sends it with the owner token on the write', async () => {
    const user = await connect()
    await searchAndSelect(user, 'EURUSD')
    await screen.findByText('EURUSD market')
    const select = calls.findIndex(c => c.method === 'POST' && c.path === '/api/instruments/select')
    expect(select).toBeGreaterThan(0)
    expect(calls[select - 1].path).toBe('/api/security/csrf-token')
    expect(calls[select].headers.get('X-PilotTrade-CSRF')).toBe(CSRF)
    expect(calls[select].headers.get('Content-Type')).toBe('application/json')
    expect(calls[select].body).toEqual({ accountId: 'ACC-A', epic: 'EURUSD' })
  })
})

describe('account filtering', () => {
  it('offers only the configured account, never the preferred or first one', async () => {
    await connect()
    const options = within(screen.getByRole('combobox', { name: 'Account' })).getAllByRole('option')
    expect(options.map(o => (o as HTMLOptionElement).value)).toEqual(['ACC-A'])
    const accountReads = calls.filter(c => ['/api/account', '/api/positions', '/api/orders'].includes(c.path))
    expect(accountReads).toHaveLength(3)
    for (const c of accountReads) expect(c.url.searchParams.get('accountId')).toBe('ACC-A')
    expect(calls.some(c => c.url.search.includes('ACC-B'))).toBe(false)
  })

  it('refuses to continue when the configured account is missing', async () => {
    routes['GET /api/accounts'] = { status: 200, body: [{ accountId: 'ACC-B', name: 'Other', status: 'ENABLED', currency: 'EUR', preferred: true }] }
    const user = userEvent.setup()
    render(<App />)
    await user.type(screen.getByLabelText(/owner token/i), TOKEN)
    await user.click(screen.getByRole('button', { name: 'Connect' }))
    expect((await screen.findByRole('alert')).textContent).toBe('The configured trading account is not available from the broker.')
    expect(calls.some(c => c.path === '/api/account')).toBe(false)
  })
})

describe('instrument search and selection', () => {
  it('lists results, marks the chosen one and shows its metadata', async () => {
    const user = await connect()
    await searchAndSelect(user, 'GOLD')
    await screen.findByText('GOLD market')
    expect(screen.getByRole('heading', { name: 'Selected: GOLD (version 3)' })).toBeTruthy()
    expect(screen.getByRole('button', { name: /\(GOLD\)/ }).getAttribute('aria-pressed')).toBe('true')
    expect(screen.getByRole('button', { name: /\(EURUSD\)/ }).getAttribute('aria-pressed')).toBe('false')
    const search = calls.find(c => c.path === '/api/instruments/search')!
    expect(search.url.searchParams.get('q')).toBe('a')
  })

  it('says so when a search finds nothing', async () => {
    routes['GET /api/instruments/search'] = { status: 200, body: [] }
    const user = await connect()
    await user.type(screen.getByRole('textbox', { name: 'Search instruments' }), 'zzz')
    await user.click(screen.getByRole('button', { name: 'Search' }))
    expect(await screen.findByText('No instruments found.')).toBeTruthy()
  })

  it('restores a saved selection after a restart only once the instrument is checked again', async () => {
    routes['GET /api/instruments/selection'] = { status: 200, body: { selectionId: 'sel_new', epic: 'GOLD', selectionVersion: 57, selectedBy: 'restored' } }
    routes['GET /api/instruments/{epic}'] = { status: 200, body: metadata('GOLD') }
    await connect()
    expect(await screen.findByRole('heading', { name: 'Selected: GOLD (version 57, restored after restart)' })).toBeTruthy()
    expect(screen.getByText('GOLD market')).toBeTruthy()
    const check = calls.find(c => c.path === '/api/instruments/GOLD')!
    expect(check.url.searchParams.get('accountId')).toBe('ACC-A')
    expect(posts()).toHaveLength(0)
  })

  it('starts with no selection when none is saved', async () => {
    await connect()
    expect(screen.getByRole('heading', { name: 'Selected: none' })).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Build market snapshot' }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.queryByRole('alert')).toBeNull()
  })
})

describe('stale responses', () => {
  it('a slow reply for an earlier choice cannot overwrite the current one', async () => {
    const slow = deferred<Reply>()
    routes['POST /api/instruments/select'] = (_url, init) => {
      const { epic } = JSON.parse(String(init.body)) as { epic: string }
      return epic === 'EURUSD' ? slow.promise : { status: 200, body: { selection: { epic, selectionVersion: 3 }, instrument: metadata(epic) } }
    }
    const user = await connect()
    await searchAndSelect(user, 'EURUSD')
    await user.click(screen.getByRole('button', { name: /\(GOLD\)/ }))
    await screen.findByRole('heading', { name: 'Selected: GOLD (version 3)' })
    slow.resolve({ status: 200, body: { selection: { epic: 'EURUSD', selectionVersion: 2 }, instrument: metadata('EURUSD') } })
    await new Promise(r => setTimeout(r, 20))
    expect(screen.getByRole('heading', { name: 'Selected: GOLD (version 3)' })).toBeTruthy()
    expect(screen.queryByText('EURUSD market')).toBeNull()
  })

  it('a slow snapshot for the previous instrument is dropped', async () => {
    const slow = deferred<Reply>()
    routes['POST /api/market/snapshot'] = () => slow.promise
    const user = await connect()
    await searchAndSelect(user, 'EURUSD')
    await screen.findByText('EURUSD market')
    await user.click(screen.getByRole('button', { name: 'Build market snapshot' }))
    await user.click(screen.getByRole('button', { name: /\(GOLD\)/ }))
    await screen.findByText('GOLD market')
    slow.resolve({ status: 200, body: { outcome: 'NO_TRADE', reason: 'For EURUSD', snapshot: null } })
    await new Promise(r => setTimeout(r, 20))
    expect(screen.queryByText(/For EURUSD/)).toBeNull()
  })

  it('a restored selection that arrives after the owner chose again is ignored', async () => {
    const slow = deferred<Reply>()
    routes['GET /api/instruments/selection'] = () => slow.promise
    const user = userEvent.setup()
    render(<App />)
    await user.type(screen.getByLabelText(/owner token/i), TOKEN)
    await user.click(screen.getByRole('button', { name: 'Connect' }))
    await screen.findByRole('heading', { name: /selected:/i })
    await searchAndSelect(user, 'EURUSD')
    await screen.findByRole('heading', { name: 'Selected: EURUSD (version 2)' })
    slow.resolve({ status: 200, body: { selectionId: 'old', epic: 'GOLD', selectionVersion: 1, selectedBy: 'restored' } })
    await new Promise(r => setTimeout(r, 20))
    expect(screen.getByRole('heading', { name: 'Selected: EURUSD (version 2)' })).toBeTruthy()
    expect(calls.some(c => c.path === '/api/instruments/GOLD')).toBe(false)
  })
})

describe('market snapshot', () => {
  it('shows NO_TRADE with its reason and no quote', async () => {
    const user = await connect()
    await searchAndSelect(user, 'EURUSD')
    await screen.findByText('EURUSD market')
    await user.click(screen.getByRole('button', { name: 'Build market snapshot' }))
    expect((await screen.findByText(/NO_TRADE/)).textContent).toBe('NO_TRADE: Market closed')
    expect(screen.queryByText('Bid / ask')).toBeNull()
    const snapshot = calls.find(c => c.path === '/api/market/snapshot')!
    expect(snapshot.body).toMatchObject({ accountId: 'ACC-A', epic: 'EURUSD', selectionVersion: 2 })
  })
})

describe('missing financial values', () => {
  it('shows "not provided" for null broker values and never zero', async () => {
    routes['GET /api/account'] = { status: 200, body: { ...account, equity: null, freeMargin: null, marginUsed: null, realizedPnLToday: null } }
    routes['GET /api/positions'] = { status: 200, body: [{ positionId: 'p1', epic: 'EURUSD', direction: 0, size: 1, openLevel: null, unrealizedPnL: null }] }
    const user = await connect()
    const values = (label: string) => screen.getByText(label).nextElementSibling!.textContent
    expect(values('Equity')).toBe('not provided')
    expect(values('Free margin')).toBe('not provided')
    expect(values('Margin used')).toBe('not provided')
    expect(values('Realized P&L today')).toBe('not provided')
    expect(values('Balance')).toBe('1000.00 USD')
    expect(screen.getByText(/EURUSD BUY 1/).textContent).toBe('EURUSD BUY 1 @ not provided | P&L not provided')
    await user.type(screen.getByRole('textbox', { name: 'Search instruments' }), 'a')
    await user.click(screen.getByRole('button', { name: 'Search' }))
    expect((await screen.findByRole('button', { name: /\(GOLD\)/ })).textContent).toContain('not provided / not provided')
    await user.click(screen.getByRole('button', { name: /\(GOLD\)/ }))
    expect((await screen.findByText('Stop distance')).nextElementSibling!.textContent).toBe('not provided points to 50.0000 points')
  })

  it('shows a reported zero as zero', async () => {
    await connect()
    expect(screen.getByText('Realized P&L today').nextElementSibling!.textContent).toBe('0.00')
  })
})

describe('network failures', () => {
  it('retries a read once after a network error', async () => {
    let failures = 1
    routes['GET /api/runtime'] = () => (failures-- > 0 ? 'network-error' : defaults()['GET /api/runtime'] as Reply)
    await connect()
    expect(calls.filter(c => c.path === '/api/runtime')).toHaveLength(2)
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('reports a read that fails twice', async () => {
    routes['GET /api/runtime'] = 'network-error'
    const user = userEvent.setup()
    render(<App />)
    await user.type(screen.getByLabelText(/owner token/i), TOKEN)
    await user.click(screen.getByRole('button', { name: 'Connect' }))
    expect((await screen.findByRole('alert', {}, { timeout: 3000 })).textContent).toBe('The server could not be reached.')
    expect(calls.filter(c => c.path === '/api/runtime')).toHaveLength(2)
  })

  it('never repeats a write after a network error', async () => {
    routes['POST /api/instruments/select'] = 'network-error'
    const user = await connect()
    await searchAndSelect(user, 'EURUSD')
    expect((await screen.findByRole('alert')).textContent).toBe('The server could not be reached. The request was not repeated.')
    expect(posts()).toHaveLength(1)
    expect(screen.getByRole('heading', { name: 'Selected: none' })).toBeTruthy()
  })
})

describe('zero-order guard', () => {
  it('a full session sends no order and only the three known writes', async () => {
    const user = await connect()
    await searchAndSelect(user, 'EURUSD')
    await screen.findByText('EURUSD market')
    await user.click(screen.getByRole('button', { name: 'Build market snapshot' }))
    await screen.findByText(/NO_TRADE/)
    await user.click(screen.getByRole('button', { name: 'Check that no order was sent' }))
    await screen.findByLabelText('No-order evidence')
    for (const c of posts()) expect(['/api/instruments/select', '/api/market/snapshot', '/api/demo/test-connection']).toContain(c.path)
    expect(calls.some(c => /execut|confirm|deal|positions\/otc|workingorders/i.test(c.path))).toBe(false)
    expect(calls.filter(c => c.path === '/api/orders').every(c => c.method === 'GET')).toBe(true)
  })

  it('shows the backend no-order evidence, and an order when one was sent', async () => {
    const user = await connect()
    await user.click(screen.getByRole('button', { name: 'Check that no order was sent' }))
    const panel = await screen.findByLabelText('No-order evidence')
    expect(within(panel).getByText('No order sent')).toBeTruthy()
    expect(within(panel).getByText('not applicable (simulator)')).toBeTruthy()
    expect(calls.find(c => c.path === '/api/evidence/no-orders')!.url.searchParams.get('accountId')).toBe('ACC-A')

    routes['GET /api/evidence/no-orders'] = { status: 200, body: { ...noOrders, adapter: 'capital.com', journalEntries: 1, journalEntriesSentToBroker: 1, brokerWriteRequestsSinceStart: 1, noOrderSent: false } }
    await user.click(screen.getByRole('button', { name: 'Check that no order was sent' }))
    expect(await within(panel).findByText('AN ORDER WAS SENT')).toBeTruthy()
    expect(within(panel).getByText('1 (1 sent to the broker)')).toBeTruthy()
  })

  it('shows execution disabled and Live locked from the runtime', async () => {
    await connect()
    expect(screen.getByText(/Execution disabled \| Live locked/)).toBeTruthy()
  })
})

describe('accessibility', () => {
  it('is usable from the keyboard alone', async () => {
    const user = userEvent.setup()
    render(<App />)
    await user.tab()
    expect(document.activeElement).toBe(screen.getByLabelText(/owner token/i))
    await user.keyboard(`${TOKEN}{Enter}`)
    await screen.findByRole('heading', { name: /selected:/i })
    await user.click(screen.getByRole('textbox', { name: 'Search instruments' }))
    await user.keyboard('a{Enter}')
    const gold = await screen.findByRole('button', { name: /\(GOLD\)/ })
    gold.focus()
    await user.keyboard('{Enter}')
    expect(await screen.findByRole('heading', { name: 'Selected: GOLD (version 3)' })).toBeTruthy()
  })

  it('gives every control an accessible name', async () => {
    const user = await connect()
    await searchAndSelect(user, 'EURUSD')
    await screen.findByText('EURUSD market')
    for (const b of screen.getAllByRole('button')) expect(b.textContent?.trim()).not.toBe('')
    for (const role of ['textbox', 'combobox'] as const) {
      for (const el of screen.getAllByRole(role)) {
        const named = el.getAttribute('aria-label') || (el.id && document.querySelector(`label[for="${el.id}"]`))
        expect(named).toBeTruthy()
      }
    }
  })

  it('announces loading, and errors as alerts', async () => {
    const slow = deferred<Reply>()
    routes['GET /api/runtime'] = () => slow.promise
    const user = userEvent.setup()
    const { container } = render(<App />)
    await user.type(screen.getByLabelText(/owner token/i), TOKEN)
    await user.click(screen.getByRole('button', { name: 'Connect' }))
    expect(screen.getByRole('status').textContent).toBe('Connecting…')
    expect((screen.getByRole('button', { name: 'Connect' }) as HTMLButtonElement).disabled).toBe(true)
    slow.resolve({ status: 403 })
    expect((await screen.findByRole('alert')).textContent).toContain('Refused')
    expect(screen.queryByRole('status')).toBeNull()
    expect(container.firstElementChild!.getAttribute('aria-busy')).toBeNull()
  })

  it('marks the page busy while account data loads', async () => {
    const slow = deferred<Reply>()
    routes['GET /api/account'] = () => slow.promise
    const user = userEvent.setup()
    const { container } = render(<App />)
    await user.type(screen.getByLabelText(/owner token/i), TOKEN)
    await user.click(screen.getByRole('button', { name: 'Connect' }))
    await screen.findByRole('heading', { name: /selected:/i })
    expect(container.firstElementChild!.getAttribute('aria-busy')).toBe('true')
    expect(screen.getByRole('status').textContent).toBe('Loading…')
    slow.resolve({ status: 200, body: account })
    await waitFor(() => expect(container.firstElementChild!.getAttribute('aria-busy')).toBe('false'))
  })

  it('draws a visible focus ring', () => {
    expect(css).toMatch(/:focus-visible\s*{[^}]*outline:\s*3px solid #ffbf47/)
    expect(palette.focus).toBe('#ffbf47')
  })

  it('meets WCAG AA contrast for text, errors, selection and focus', () => {
    const luminance = (hex: string) => {
      const [r, g, b] = [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16) / 255).map(c => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4))
      return 0.2126 * r + 0.7152 * g + 0.0722 * b
    }
    const ratio = (a: string, b: string) => { const [x, y] = [luminance(a), luminance(b)].sort((m, n) => n - m); return (x + 0.05) / (y + 0.05) }
    expect(ratio(palette.text, palette.background)).toBeGreaterThanOrEqual(4.5)
    expect(ratio(palette.error, palette.background)).toBeGreaterThanOrEqual(4.5)
    expect(ratio(palette.text, palette.selected)).toBeGreaterThanOrEqual(4.5)
    // A focus indicator needs 3:1 against what is next to it.
    expect(ratio(palette.focus, palette.background)).toBeGreaterThanOrEqual(3)
  })
})
