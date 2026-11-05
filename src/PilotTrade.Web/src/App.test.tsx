import css from './index.css?raw'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import App from './App'
import { POLL } from './hooks/useOwnerSession'
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

const opsStatus = { checkedUtc: '2026-11-04T12:00:00Z', killSwitchEngaged: false, tradingMode: 'MonitorOnly', reconciliationComplete: true, suspensionReason: null, feed: null }

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
    'GET /api/status': { status: 200, body: opsStatus },
    'GET /api/demo/credential-status': { status: 200, body: { apiKeyPresent: true, identifierPresent: true, passwordPresent: false } },
  }
}

function routeFor(method: string, path: string): Route | undefined {
  if (routes[`${method} ${path}`]) return routes[`${method} ${path}`]
  // GET /api/instruments/{epic} revalidates a restored selection.
  if (method === 'GET' && path.startsWith('/api/instruments/')) return routes['GET /api/instruments/{epic}']
  return undefined
}

beforeEach(() => {
  // Every test starts on Overview, whatever page the previous one left in the address.
  window.history.replaceState(null, '', '/')
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

type User = ReturnType<typeof userEvent.setup>

// Opens a page from the sidebar, the way the owner would.
async function goTo(user: User, label: string) {
  await user.click(within(screen.getByRole('navigation', { name: 'Main' })).getByRole('link', { name: label }))
  await screen.findByRole('heading', { level: 1, name: label })
}

async function searchAndSelect(user: User, epic: string) {
  if (!screen.queryByRole('textbox', { name: 'Search instruments' })) await goTo(user, 'Markets')
  await user.type(screen.getByRole('textbox', { name: 'Search instruments' }), 'a')
  await user.click(screen.getByRole('button', { name: 'Search' }))
  await user.click(await screen.findByRole('button', { name: new RegExp(`\\(${epic}\\)`) }))
}

const PAGE_LABELS = ['Overview', 'Markets', 'AI Decisions', 'Risk Center', 'Orders & Positions', 'Audit & Alerts', 'Broker Settings', 'Strategy & AI Settings', 'System']

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
    await goTo(user, 'Markets')
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
    const user = await connect()
    expect(screen.getByRole('heading', { name: 'Selected: none' })).toBeTruthy()
    await goTo(user, 'Markets')
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
    await goTo(user, 'Orders & Positions')
    expect(screen.getByText(/EURUSD BUY 1/).textContent).toBe('EURUSD BUY 1 @ not provided | P&L not provided')
    await goTo(user, 'Markets')
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
    await goTo(user, 'Orders & Positions')
    await user.click(screen.getByRole('button', { name: 'Check that no order was sent' }))
    await screen.findByLabelText('No-order evidence')
    for (const c of posts()) expect(['/api/instruments/select', '/api/market/snapshot', '/api/demo/test-connection']).toContain(c.path)
    expect(calls.some(c => /execut|confirm|deal|positions\/otc|workingorders/i.test(c.path))).toBe(false)
    expect(calls.filter(c => c.path === '/api/orders').every(c => c.method === 'GET')).toBe(true)
  })

  it('shows the backend no-order evidence, and an order when one was sent', async () => {
    const user = await connect()
    await goTo(user, 'Orders & Positions')
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

  it('shows the server-reported mode, demo execution disabled and Live locked in the header', async () => {
    await connect()
    const badges = within(screen.getByRole('list', { name: 'Safety state' })).getAllByRole('listitem').map(b => b.textContent)
    expect(badges).toEqual(['Simulation', 'Demo execution disabled', 'Live locked'])
  })

  it('labels Capital.com Demo as read-only while demo execution is disabled', async () => {
    routes['GET /api/runtime'] = { status: 200, body: { mode: 'CapitalDemo', environment: 'Demo', accountId: 'ACC-A', executionEnabled: false, liveLocked: true } }
    await connect()
    const badges = within(screen.getByRole('list', { name: 'Safety state' })).getAllByRole('listitem').map(b => b.textContent)
    expect(badges).toEqual(['Capital.com Demo / Read-only', 'Demo execution disabled', 'Live locked'])
  })

  it('shows a loud warning if the server ever reports Live unlocked or demo execution enabled', async () => {
    routes['GET /api/runtime'] = { status: 200, body: { mode: 'CapitalDemo', environment: 'Demo', accountId: 'ACC-A', executionEnabled: true, liveLocked: false } }
    await connect()
    const badges = within(screen.getByRole('list', { name: 'Safety state' })).getAllByRole('listitem').map(b => b.textContent)
    expect(badges).toEqual(['Capital.com Demo', 'DEMO EXECUTION ENABLED', 'LIVE NOT LOCKED'])
  })

  it('a simulation with execution enabled is still only a simulation', async () => {
    routes['GET /api/runtime'] = { status: 200, body: { mode: 'Simulation', environment: 'Demo', accountId: 'ACC-A', executionEnabled: true, liveLocked: true } }
    await connect()
    const badges = within(screen.getByRole('list', { name: 'Safety state' })).getAllByRole('listitem').map(b => b.textContent)
    expect(badges).toEqual(['Simulation', 'Demo execution disabled', 'Live locked'])
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
    screen.getByRole('link', { name: 'Markets' }).focus()
    await user.keyboard('{Enter}')
    expect(document.activeElement).toBe(screen.getByRole('heading', { level: 1, name: 'Markets' }))
    await user.tab()
    expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Search instruments' }))
    await user.keyboard('a{Enter}')
    const gold = await screen.findByRole('button', { name: /\(GOLD\)/ })
    gold.focus()
    await user.keyboard('{Enter}')
    expect(await screen.findByRole('heading', { name: 'Selected: GOLD (version 3)' })).toBeTruthy()
  })

  it('gives every control on every page an accessible name', async () => {
    const user = await connect()
    await searchAndSelect(user, 'EURUSD')
    await screen.findByText('EURUSD market')
    let fields = 0
    for (const label of PAGE_LABELS) {
      await goTo(user, label)
      for (const b of screen.queryAllByRole('button')) expect(b.textContent?.trim()).not.toBe('')
      for (const role of ['textbox', 'combobox'] as const) {
        for (const el of screen.queryAllByRole(role)) {
          const named = el.getAttribute('aria-label') || (el.id && document.querySelector(`label[for="${el.id}"]`))
          expect(named).toBeTruthy()
          fields++
        }
      }
    }
    expect(fields).toBeGreaterThanOrEqual(2)
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

describe('control center shell', () => {
  it('lists every section in the sidebar and marks the open one', async () => {
    await connect()
    const nav = screen.getByRole('navigation', { name: 'Main' })
    const links = within(nav).getAllByRole('link')
    expect(links.map(l => l.textContent)).toEqual(PAGE_LABELS)
    expect(links.filter(l => l.getAttribute('aria-current') === 'page').map(l => l.textContent)).toEqual(['Overview'])
    expect(screen.getByRole('heading', { level: 1 }).textContent).toBe('Overview')
  })

  it('opens a page by pointer, updates the address, and moves focus to the page heading', async () => {
    const user = await connect()
    await goTo(user, 'Risk Center')
    expect(window.location.hash).toBe('#/risk')
    expect(document.activeElement).toBe(screen.getByRole('heading', { level: 1, name: 'Risk Center' }))
    expect(screen.getByRole('link', { name: 'Risk Center' }).getAttribute('aria-current')).toBe('page')
    expect(screen.getByRole('link', { name: 'Overview' }).getAttribute('aria-current')).toBeNull()
  })

  it('opens a page from the keyboard and follows the browser Back button', async () => {
    const user = await connect()
    screen.getByRole('link', { name: 'System' }).focus()
    await user.keyboard('{Enter}')
    expect(await screen.findByRole('heading', { level: 1, name: 'System' })).toBeTruthy()
    window.history.back()
    expect(await screen.findByRole('heading', { level: 1, name: 'Overview' })).toBeTruthy()
  })

  it('keeps loaded data when moving between pages, without fetching it again', async () => {
    const user = await connect()
    const reads = calls.length
    await goTo(user, 'Orders & Positions')
    await goTo(user, 'Overview')
    expect(screen.getByText('Balance').nextElementSibling!.textContent).toBe('1000.00 USD')
    expect(calls.length).toBe(reads)
  })

  it('shows sections without a backend API as not available, with no sample figures', async () => {
    const user = await connect()
    for (const [label, title] of [['AI Decisions', 'AI decisions'], ['Audit & Alerts', 'Audit log and alerts'], ['Strategy & AI Settings', 'Strategy and AI settings']]) {
      await goTo(user, label)
      expect(screen.getByRole('heading', { level: 2, name: `${title}: Not available` })).toBeTruthy()
      expect(document.querySelector('main')!.textContent).not.toMatch(/\d+\.\d{2}/)
    }
    await goTo(user, 'Risk Center')
    // The kill switch is shown as the server reports it, read-only; engaging it arrives with task 02-05.
    expect(screen.getByRole('heading', { level: 2, name: 'Kill switch' }).parentElement!.textContent).toContain('State: Not engaged')
    expect(screen.queryByRole('button', { name: /kill|enable|live|unlock/i })).toBeNull()
  })

  it('names only the allowed AI providers', async () => {
    const user = await connect()
    await goTo(user, 'Strategy & AI Settings')
    expect(document.body.textContent).not.toMatch(/gemini/i)
    expect(document.body.textContent).toContain('Claude API and JEV/TypeSafe AI')
  })

  it('shows the account masked in the header and the account list', async () => {
    await connect()
    const header = screen.getByRole('banner')
    expect(header.textContent).toContain('••••CC-A (Demo)')
    expect(header.textContent).not.toContain('ACC-A')
    const option = within(screen.getByRole('combobox', { name: 'Account' })).getByRole('option')
    expect(option.textContent).not.toContain('ACC-A')
  })

  it('reports the backend link and the last refresh time from real replies only', async () => {
    const user = await connect()
    const header = within(screen.getByRole('banner'))
    expect(header.getByText('Connected')).toBeTruthy()
    expect(header.getByText(/^\d{2}:\d{2}:\d{2} UTC$/)).toBeTruthy()
    // A wrong or rotated token is refused on every call, not just one.
    for (const path of ['/api/account', '/api/positions', '/api/orders', '/api/status']) routes[`GET ${path}`] = { status: 403 }
    await user.click(screen.getByRole('button', { name: 'Refresh' }))
    expect((await screen.findByRole('alert')).textContent).toBe('Refused: check the owner token.')
    expect(header.getByText('Refused')).toBeTruthy()
  })

  it('shows "Not available" for the last refresh before any account data has arrived', async () => {
    const slow = deferred<Reply>()
    routes['GET /api/account'] = () => slow.promise
    const user = userEvent.setup()
    render(<App />)
    await user.type(screen.getByLabelText(/owner token/i), TOKEN)
    await user.click(screen.getByRole('button', { name: 'Connect' }))
    const header = within(await screen.findByRole('banner'))
    expect(header.getByText('Last refresh').nextElementSibling!.textContent).toBe('Not available')
    slow.resolve({ status: 200, body: account })
    await waitFor(() => expect(header.getByText('Last refresh').nextElementSibling!.textContent).toMatch(/UTC$/))
  })

  it('shows credential presence on Broker Settings, never a value, and offers no order controls', async () => {
    const user = await connect()
    await goTo(user, 'Broker Settings')
    const presence = await screen.findByLabelText('Credential presence')
    expect(within(presence).getByText('API key').nextElementSibling!.textContent).toBe('Stored')
    expect(within(presence).getByText('Password').nextElementSibling!.textContent).toBe('Missing')
    expect(screen.queryByRole('button', { name: /order|buy|sell|close|cancel/i })).toBeNull()
  })

  it('a page that fails to draw is replaced by a notice while the shell keeps working', async () => {
    // A reply the page cannot draw must not take the header or the menu down with it.
    routes['GET /api/positions'] = { status: 200, body: [null] }
    const user = await connect()
    const quiet = vi.spyOn(console, 'error').mockImplementation(() => {})
    await goTo(user, 'Orders & Positions')
    expect(screen.getByRole('heading', { level: 2, name: 'This page could not be shown' })).toBeTruthy()
    expect(screen.getByRole('list', { name: 'Safety state' })).toBeTruthy()
    await goTo(user, 'Overview')
    expect(screen.getByText('Balance')).toBeTruthy()
    quiet.mockRestore()
  })

  it('disconnect forgets the session and returns to the token form', async () => {
    const user = await connect()
    await goTo(user, 'System')
    await user.click(screen.getByRole('button', { name: 'Disconnect' }))
    expect(screen.getByLabelText(/owner token/i)).toBeTruthy()
    expect(document.body.textContent).not.toContain('1000.00')
  })

  it('keeps the new palette colours at WCAG AA contrast', () => {
    const luminance = (hex: string) => {
      const [r, g, b] = [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16) / 255).map(c => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4))
      return 0.2126 * r + 0.7152 * g + 0.0722 * b
    }
    const ratio = (a: string, b: string) => { const [x, y] = [luminance(a), luminance(b)].sort((m, n) => n - m); return (x + 0.05) / (y + 0.05) }
    for (const bg of [palette.background, palette.surface, palette.selected]) {
      for (const fg of [palette.text, palette.muted, palette.error, palette.warn, palette.ok]) expect(ratio(fg, bg)).toBeGreaterThanOrEqual(4.5)
      expect(ratio(palette.focus, bg)).toBeGreaterThanOrEqual(3)
    }
  })
})

describe('operations overview', () => {
  const warnings = () => within(screen.getByRole('list', { name: 'Operational warnings' })).queryAllByRole('listitem').map(li => li.textContent)
  const tag = (label: string) => screen.getByText(label).querySelector('.prov')!.textContent
  const value = (label: string) => screen.getByText(label).nextElementSibling!.textContent

  it('labels each figure Reported, Derived or Unavailable exactly as the backend sent it', async () => {
    routes['GET /api/account'] = { status: 200, body: { ...account, equity: 1012.5, equityIsDerived: true, freeMargin: null, realizedPnLToday: 0 } }
    await connect()
    expect([tag('Balance'), value('Balance')]).toEqual(['Reported', '1000.00 USD'])
    expect([tag('Equity'), value('Equity')]).toEqual(['Derived', '1012.50'])
    expect([tag('Free margin'), value('Free margin')]).toEqual(['Unavailable', 'not provided'])
    expect([tag('Realized P&L today'), value('Realized P&L today')]).toEqual(['Reported', '0.00'])
    expect(warnings()).toContain('Not reported by the broker, so risk checks cannot use them: Free margin.')
  })

  it('shows empty holdings as reported zeros, not as missing', async () => {
    await connect()
    expect(value('Open positions')).toBe('0')
    expect(value('Working orders')).toBe('0')
  })

  it('shows the server safety status: kill switch, trading mode, reconciliation and Live locked', async () => {
    routes['GET /api/status'] = { status: 200, body: { ...opsStatus, killSwitchEngaged: true, tradingMode: 'Suspended', reconciliationComplete: false, suspensionReason: 'Broker holdings are not compared while execution is disabled' } }
    await connect()
    expect(value('Kill switch')).toBe('Engaged')
    expect(value('Server trading mode')).toBe('Suspended')
    expect(value('Reconciliation')).toBe('Not complete')
    expect(warnings()).toEqual([
      'Kill switch engaged: the server refuses every new order.',
      'Reconciliation not complete: Broker holdings are not compared while execution is disabled.',
      'Live trading is locked. There is no control to unlock it.',
    ])
    expect(screen.queryByRole('button', { name: /kill|release|unlock|enable|live/i })).toBeNull()
  })

  it('warns about a stale market feed with its last quote time', async () => {
    routes['GET /api/instruments/selection'] = { status: 200, body: { epic: 'EURUSD', selectionVersion: 2 } }
    routes['GET /api/instruments/{epic}'] = { status: 200, body: metadata('EURUSD') }
    routes['GET /api/status'] = { status: 200, body: { ...opsStatus, feed: { epic: 'EURUSD', health: 'Stale', lastQuoteUtc: '2026-11-04T11:58:07Z' } } }
    await connect()
    await waitFor(() => expect(warnings()).toContain('Market feed for EURUSD is stale (last quote 11:58:07 UTC).'))
    expect(value('Feed health')).toBe('Stale')
    expect(value('Last quote')).toBe('11:58:07 UTC')
  })

  it('keeps the figures but marks them stale when the backend is lost, and clears it once it is back', async () => {
    const healthy = { status: 200, body: { ...opsStatus, feed: { epic: 'EURUSD', health: 'Healthy', lastQuoteUtc: '2026-11-04T11:59:59Z' } } }
    routes['GET /api/instruments/selection'] = { status: 200, body: { epic: 'EURUSD', selectionVersion: 2 } }
    routes['GET /api/instruments/{epic}'] = { status: 200, body: metadata('EURUSD') }
    routes['GET /api/status'] = healthy
    const user = await connect()
    await waitFor(() => expect(screen.getByText('Feed health').nextElementSibling!.className).toBe('tone-ok'))
    const updated = within(screen.getByRole('banner')).getByText('Last refresh').nextElementSibling!.textContent!
    const reads = ['GET /api/account', 'GET /api/positions', 'GET /api/orders', 'GET /api/status']
    for (const key of reads) routes[key] = { status: 503 }
    await user.click(screen.getByRole('button', { name: 'Refresh' }))
    await screen.findByRole('alert')
    expect(warnings()[0]).toBe(`Backend server error: the figures below are stale (last successful update ${updated}).`)
    expect(value('Balance')).toBe('1000.00 USD')
    expect(screen.getAllByText(`Stale: as of ${updated}`)).toHaveLength(2)
    // A green state never survives a lost link.
    expect(screen.getByText('Feed health').nextElementSibling!.className).toBe('tone-warn')
    expect(value('Feed health')).toBe('Healthy (stale)')

    for (const key of reads) routes[key] = defaults()[key]
    routes['GET /api/status'] = healthy
    await user.click(screen.getByRole('button', { name: 'Refresh' }))
    await waitFor(() => expect(warnings()).toEqual(['Live trading is locked. There is no control to unlock it.']))
    expect(screen.queryByText(/^Stale:/)).toBeNull()
  })

  it('still shows account figures when only the status read fails, and says what is unknown', async () => {
    routes['GET /api/status'] = { status: 500, body: { error: 'internal_error' } }
    await connect()
    expect(value('Balance')).toBe('1000.00 USD')
    expect(value('Kill switch')).toBe('Not available')
    expect(value('Reconciliation')).toBe('Not available')
    expect(warnings()).toContain('Server safety status could not be read: kill switch, reconciliation and feed health are unknown.')
  })
})

describe('overview polling', () => {
  // The real interval is 15 s; these tests run it at a few milliseconds with real timers.
  const saved = { ...POLL }
  beforeEach(() => { POLL.baseMs = 30; POLL.maxMs = 120 })
  afterEach(() => { Object.assign(POLL, saved); vi.restoreAllMocks() })
  const accountReads = () => calls.filter(c => c.path === '/api/account').length
  const pause = (ms: number) => new Promise(r => setTimeout(r, ms))

  it('updates on the interval, backs off after failures up to the limit, and recovers', async () => {
    const timer = vi.spyOn(globalThis, 'setTimeout')
    await connect()
    const start = accountReads()
    await waitFor(() => expect(accountReads()).toBeGreaterThan(start))
    const delays = () => timer.mock.calls.map(c => c[1]).filter(d => d === 30 || d === 60 || d === 120)

    routes['GET /api/account'] = { status: 503 }
    await waitFor(() => expect(delays()).toContain(60))
    await waitFor(() => expect(delays().filter(d => d === 120).length).toBeGreaterThanOrEqual(2))
    // The delay never grows past the limit: nothing above 120 ms is scheduled after 120 ms.
    expect(delays().slice(delays().indexOf(120)).every(d => d === 120)).toBe(true)
    // A failed background update shows as a stale warning, not as an error alert.
    expect(screen.queryByRole('alert')).toBeNull()
    expect(within(screen.getByRole('list', { name: 'Operational warnings' })).getAllByRole('listitem')[0].textContent).toMatch(/^Update failed at \d\d:\d\d:\d\d UTC: the figures below are stale/)

    routes['GET /api/account'] = defaults()['GET /api/account']
    const before = delays().length
    await waitFor(() => expect(delays().slice(before)).toContain(30))
  })

  it('never runs two updates at once: Refresh during a timer update joins it', async () => {
    const user = await connect()
    const slow = deferred<Reply>()
    routes['GET /api/account'] = () => slow.promise
    const start = accountReads()
    await waitFor(() => expect(accountReads()).toBe(start + 1))
    await user.click(screen.getByRole('button', { name: 'Refresh' }))
    await user.click(screen.getByRole('button', { name: 'Refresh' }))
    await pause(100)
    expect(accountReads()).toBe(start + 1)
    slow.resolve({ status: 200, body: { ...account, balance: 1234 } })
    await waitFor(() => expect(screen.getByText('Balance').nextElementSibling!.textContent).toBe('1234.00 USD'))
  })

  it('stops polling on disconnect', async () => {
    const user = await connect()
    await goTo(user, 'System')
    await user.click(screen.getByRole('button', { name: 'Disconnect' }))
    const after = calls.length
    await pause(200)
    expect(calls.length).toBe(after)
  })
})
