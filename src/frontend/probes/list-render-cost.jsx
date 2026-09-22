/*
  Probe: what one interaction costs in the two long lists, now that a row is a `role="row"` of
  four `role="gridcell"`s driven by `useGridNav`.

  `probes/contact-tile-grid.html`'s bench timed the HOOK alone — `rowsIn` and `repoint`'s attribute
  rewrite over cloned markup — and said so. That bound is what this lifts: here the REAL
  `MessageList` and `ContactList` are mounted, with their real hooks, their real preferences gate
  and their real `useGridNav`, and the three things a reader actually does are timed end to end:
  the list appearing, one checkbox being ticked, one arrow key.

  What is mocked, and where. The ONLY seam is `window.fetch`: every module below it is the shipped
  one — `api.js`, TanStack Query with `App.tsx`'s own defaults, `AuthProvider`, `usePreferences`,
  `useMessageList`, `useSelection`, `useGridNav`. `ContactList` takes its rows as a prop, so it
  needs no seam at all. A number taken through a mock that skips real work is a number about the
  mock; here the mock skips the network and nothing else.

  Row count. Preferences report a legitimate `mail.pageSize` — one of the offered steps, since
  Task 5 bounds `requestSizeOf` to them — so the app always asks the API for a real, capped amount.
  Paged mode's fetch stub answers with `state.rows` messages regardless of that request: no real
  backend does this, and it is the one place this file deliberately diverges from the shipped
  request/response contract, kept solely so a single-page mount of N rows stays reachable for
  measurement now that the account-side bug that used to reach it for real (an unbounded stored
  page size) is fixed. Production reaches 1000 or 2000 rows through streaming ("All") instead,
  which accumulates blocks of 100 and additionally mounts one `LoadMoreSentinel`; the rendered row
  is the same row either way, and one extra zero-height div is not what a 2000-row figure is about.

  Honesty conventions, from `contact-tile-grid.html`: median of N, one discarded warm-up with its
  reason, and every number says which run it came from. Two further ones this file needs:

    - `mountMs` is measured on a SECOND mount, against a warm query cache, so it is the cost of
      React building and committing N rows — a folder switch or a page change — and not the
      network. The hook's own first `repoint()` runs inside that commit (it is a layout effect), so
      at mount the two cannot be separated; `walkMs` below is what attributes it. **Passive
      effects are inside that figure too**: React 18.3.1's `commitRootImpl` flushes them
      synchronously after a SyncLane commit whenever the root is not a legacy root, which is what a
      `flushSync` mount on `createRoot` is. So `mountMs`, `tickReactMs` and `keystrokeReactMs` each
      hold every row's `useEffect` as well as its render and its layout effects — a thousand
      `useTranslation` subscriptions among them. What a flush does NOT hold is a render an effect
      *schedules*: that update is lowered to Default priority and rides the Scheduler's own
      macrotask, which is why the keystroke below has a settle window at all.
    - `reactMs` / `hookMs` for an interaction ARE separable, and the split is a fact about when
      each runs. `flushSync` completes React's render, commit, layout effects and — per the
      paragraph above — its passive effects synchronously; `useGridNav`'s `MutationObserver`
      callback is a microtask, so it has NOT run when `flushSync` returns. This file registers a
      second observer on the same grid with the same options — it fires after the hook's, observers
      running in registration order — and reads the clock there.
      `records: 0` therefore means the tick produced no mutation the hook watches at all.

  Driving it: `npm run dev`, then /probes/list-render-cost.html, and `await window.benchAll()` from
  the console. That is the React DEVELOPMENT build behind `@vitejs/plugin-react`'s Fast Refresh
  wrappers, which is slower than what ships — build the page with a production config and serve
  `dist` to get the shipping number. Both runs are worth having; say which one a number came from.
*/
import { useState } from 'react'
import { createRoot } from 'react-dom/client'
import { flushSync } from 'react-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'

// The shipped modules, imported as the app imports them. A static import is hoisted above the
// fetch stub below, which costs nothing: `api.js` resolves the global `fetch` at CALL time, and
// nothing calls it until `run()` mounts a tree.
import { AuthProvider } from '../src/contexts/AuthContext'
import MessageList from '../src/modules/mail/list/MessageList'
import { useRowExit } from '../src/modules/mail/list/useRowExit'
import ContactList from '../src/modules/contacts/ContactList'
import { focusablesIn } from '../src/lib/layerStack'
import { reachable } from '../src/hooks/useLayer'
import { ROW } from '../src/hooks/useGridNav'
import { BLOCK_SIZE } from '../src/hooks/usePreferences'
import { initI18n } from '../src/lib/i18n'

import '../src/styles/tokens.css'
import '../src/styles/theme-night.css'
import '../src/index.css'
import '../src/styles/modal.css'
import '../src/styles/selection.css'
import '../src/styles/shell.css'
import '../src/styles/tooltip.css'
import '../src/styles/mail.css'

// ---------------------------------------------------------------------------- the one seam

/** Rows the next answer carries. Set before a mount; read by the stub below. `streaming` picks
    the mode: paged reports the largest offered step (`BLOCK_SIZE`) and the stub answers with
    `rows` messages in that one page regardless — see the file header — while streaming reports
    `all` and accumulates blocks of 100 the way production reaches 1000 or 2000 rows. */
const state = { rows: 100, streaming: false }

const json = body => new Response(JSON.stringify(body), {
  status: 200, headers: { 'content-type': 'application/json' },
})

const FOLDERS = [{
  path: 'INBOX', name: 'Inbox', specialUse: 'inbox', selectable: true, subscribed: true,
  total: 2000, unread: 12, uidValidity: 1, uidNext: 2001, highestModSeq: 1, children: [],
}, {
  path: 'Archive', name: 'Archive', specialUse: 'archive', selectable: true, subscribed: true,
  total: 10, unread: 0, uidValidity: 1, uidNext: 11, highestModSeq: 1, children: [],
}, {
  path: 'Junk', name: 'Junk', specialUse: 'junk', selectable: true, subscribed: true,
  total: 3, unread: 0, uidValidity: 1, uidNext: 4, highestModSeq: 1, children: [],
}, {
  path: 'Trash', name: 'Trash', specialUse: 'trash', selectable: true, subscribed: true,
  total: 4, unread: 0, uidValidity: 1, uidNext: 5, highestModSeq: 1, children: [],
}]

const SENDERS = ['Alice Dupont', 'Bruno Mertens', 'Chloé Vandenberghe', 'GitHub', 'Facturation SPRL']
const SUBJECTS = [
  'Re: facture 2026-0431 à régler avant vendredi',
  'Votre commande a été expédiée',
  '[weesky.net-mail] Accessibility lot 2c merged (#83)',
  'Rappel : réunion de cadrage lundi 9h',
  'Newsletter — ce qui change ce mois-ci',
]

/** One page of messages. Long enough to ellipsise, which is what a real row's layout costs.
    `offset` is the block's first index: a stream's blocks must carry distinct uids or
    `dedupeByUid` collapses them into one. It defaults to 0, so a paged answer is unchanged. */
function messagesOf(count, offset = 0) {
  return Array.from({ length: count }, (unused, i) => ({
    uid: 10_000 - offset - i,
    subject: SUBJECTS[i % SUBJECTS.length],
    fromName: SENDERS[i % SENDERS.length],
    fromAddress: `sender${i % 37}@example.be`,
    to: [{ name: 'Michaël', address: 'me@weesky.be' }],
    date: new Date(Date.UTC(2026, 8, 1 + (i % 20), 8, (i * 7) % 60)).toISOString(),
    seen: i % 5 !== 0,
    flagged: i % 11 === 0,
    answered: false,
    hasAttachments: i % 7 === 0,
    size: 4096 + i,
    preview: 'Bonjour, voici les éléments demandés pour le dossier en cours. '
      + 'Merci de confirmer la réception avant la fin de la semaine.',
    priority: i % 23 === 0 ? 'high' : 'normal',
  }))
}

const books = new Map()

/** Cached per row count, at module level rather than in a harness ref: the second mount is a fresh
    `ContactsHarness`, so a ref would build the fixture inside the `flushSync` this file times.
    It removes the fixture build from the timed window; the published deltas hold because that
    build sat in both halves of every before/after pair. */
function contactsOf(count) {
  const cached = books.get(count)
  if (cached) return cached
  const book = Array.from({ length: count }, (unused, i) => ({
    id: `c-${String(i).padStart(6, '0')}-0000-4000-8000-000000000000`,
    firstName: SENDERS[i % SENDERS.length].split(' ')[0],
    lastName: SENDERS[i % SENDERS.length].split(' ')[1] ?? null,
    nickname: null,
    isFavorite: i % 9 === 0,
    addresses: i % 3 === 0
      ? [`person${i}@example.be`, `alt${i}@example.com`]
      : [`person${i}@example.be`],
  }))
  books.set(count, book)
  return book
}

const PREFERENCES = () => ({
  // Always one of the six literals `requestSizeOf` honours (Task 5) — the app never asks the API
  // for more than that, in either mode. Reaching `state.rows` in paged mode is the fetch stub's
  // job below, not this preference's.
  'mail.pageSize': state.streaming ? 'all' : String(BLOCK_SIZE),
  'mail.showPreview': 'true',
  'mail.notifySound': 'false',
  'mail.notifyDesktop': 'false',
  'mail.alwaysShowImages': 'false',
  'mail.showSpamScore': 'true',
  'mail.readingPane': 'right',
  // The shipped default — three icons, not four: the row a fresh account draws.
  'mail.rowActions': 'seen,archive,delete',
  'mail.composeFormat': 'html',
  'mail.showFolderIcons': 'false',
  'mail.groupConversations': 'false',
  'contacts.captureRecipients': 'true',
  'mail.trustContacts': 'false',
  'ui.language': 'en',
})

const ACCOUNT = {
  email: 'me@weesky.be', fullName: 'Michaël', isAdmin: false, domainName: 'weesky.be',
  quotaUsed: 1024, quotaLimit: 10_240, subDomains: [],
}

/** Every request this page can make, counted and answered from memory. Nothing leaves the tab. */
window.__probeFetches = []
window.fetch = async input => {
  const url = String(input && input.url ? input.url : input)
  window.__probeFetches.push(url)
  if (url.includes('/api/Preferences')) return json(PREFERENCES())
  if (url.includes('/api/Mail/Folders')) return json(FOLDERS)
  if (url.includes('/api/Mail/Messages')) {
    const params = new URL(url, location.origin).searchParams
    const page = Number(params.get('page') || 0)
    // Paged mode's request is capped at BLOCK_SIZE too (Task 5), so the stub over-answers the
    // single page it returns rather than honouring it — the file header says why. Streaming
    // honours its own request as sent, already capped at BLOCK_SIZE for real.
    const size = state.streaming ? Number(params.get('pageSize') || BLOCK_SIZE) : state.rows
    // Task 4 built total to equal the paged request on purpose, so lastPage collapses to 0 and
    // MessageList.tsx's `paging.lastPage > 0` guard renders no footer. requestSize is now pinned
    // at BLOCK_SIZE (Task 5), not state.rows, so total must be capped at it too, not echo rows.
    const total = state.streaming ? 2000 : Math.min(state.rows, BLOCK_SIZE)
    return json({
      folderPath: 'INBOX', uidValidity: 1, total, page, pageSize: size,
      messages: messagesOf(size, page * size),
    })
  }
  if (url.includes('/api/ConnectedAccounts')) return json([])
  if (url.includes('/api/Capabilities')) return new Response('', { status: 404 })
  if (url.includes('/api/Account')) return json(ACCOUNT)
  if (url.includes('/api/Contacts')) return json({ contacts: contactsOf(state.rows) })
  return new Response('{}', { status: 200, headers: { 'content-type': 'application/json' } })
}

// ---------------------------------------------------------------------------- the two harnesses

const noop = () => {}

function MailHarness() {
  const [selected, setSelected] = useState(null)
  const rowExit = useRowExit()
  return (
    <div className="mail-layout is-right" style={{ height: '100vh' }}>
      <div className="mail-list" style={{ width: 380, flex: 'none' }}>
        <MessageList
          folderPath="INBOX"
          folderName="Inbox"
          folderRole="inbox"
          selectedUid={selected}
          onSelect={setSelected}
          rowExit={rowExit}
          search={null}
          onSearchChange={noop}
        />
      </div>
    </div>
  )
}

function ContactsHarness() {
  const [selected, setSelected] = useState(null)
  const contacts = contactsOf(state.rows)
  return (
    <div
      className="contacts-list"
      style={{ width: 380, height: '100vh', display: 'flex', flexDirection: 'column', overflow: 'hidden' }}
    >
      <ContactList
        contacts={contacts}
        selectedId={selected}
        scope="all"
        onSelect={setSelected}
        onToggleFavorite={noop}
        onEdit={noop}
        onDelete={noop}
        onDeleteMany={noop}
      />
    </div>
  )
}

// ---------------------------------------------------------------------------- the bench

const round = (n, places = 3) => {
  const factor = 10 ** places
  return Math.round(n * factor) / factor
}
const median = runs => runs.slice().sort((a, b) => a - b)[Math.floor(runs.length / 2)]
const now = () => performance.now()
/** A macrotask that is NOT a timer. Measured: a tab the driver is not looking at falls under
    Chrome's intensive timer throttling — one `setTimeout` wake a minute — which turned a 5-second
    run into a stall and is what made the first attempt at this bench look hung. A MessagePort
    message is not throttled that way. Nothing timed rides on it; it only lets React, TanStack
    Query and the browser get on with their own work between phases. */
const tick = () => new Promise(resolve => {
  const channel = new MessageChannel()
  channel.port1.onmessage = () => resolve()
  channel.port2.postMessage(0)
})
/** Two macrotasks, which is more than React's Scheduler — itself a MessageChannel — needs to run a
    pending passive effect and the render that effect schedules. */
const drain = async () => { await tick(); await tick() }
/** A microtask, not a task: a MutationObserver callback IS a microtask, so this is the shortest
    wait that is guaranteed to be after one — and it is not throttled the way a hidden tab's
    timers are. */
const micro = () => Promise.resolve()
/** The next frame, or null after 300ms. A tab the driver is not looking at runs no
    requestAnimationFrame at all — `contact-tile-grid.html` forces a reflow for that reason — so
    this must never be the thing a run waits on. `null` means "this browser window was not in
    front", not "instant". */
const frame = () => new Promise(resolve => {
  let settled = false
  requestAnimationFrame(() => { if (!settled) { settled = true; resolve(now()) } })
  // The give-up path is a run of macrotasks rather than a timer, for the reason above.
  let left = 60
  const spin = () => {
    if (settled) return
    if (left-- <= 0) { settled = true; resolve(null); return }
    void tick().then(spin)
  }
  void tick().then(spin)
})

let root = null
let client = null

function mountPoint() {
  const host = document.getElementById('root')
  host.replaceChildren()
  return host
}

function tree(kind) {
  const Harness = kind === 'mail' ? MailHarness : ContactsHarness
  return (
    <QueryClientProvider client={client}>
      <AuthProvider>
        <Harness />
      </AuthProvider>
    </QueryClientProvider>
  )
}

/** Waits until the grid holds `want` rows, or gives up. Nothing here is timed.

    In streaming mode the rows arrive a block at a time, and only when `LoadMoreSentinel` comes
    into view — it is an IntersectionObserver rooted on `.mail-list-scroll`. This is the shipped
    arrival path and not a shortcut around it: nothing here calls `fetchNextPage`. Two things about
    it were measured rather than assumed, and each was wrong first.

    `sentinelIndexOf` puts the marker PREFETCH_ROWS before the LAST loaded row, so slamming the
    band to its own bottom scrolls straight PAST it: at 100 rows the marker sits 1700px above a
    741px viewport and the observer never fires at all. The marker is brought into view instead,
    which is what a reader scrolling down does to it.

    And an IntersectionObserver is delivered with the rendering step, so a block needs a FRAME to
    arrive and not a macrotask. A tight `tick()` loop ran its whole budget in ~250ms having never
    let one be delivered, which is what "the list never reached 1000 rows" was. */
async function settle(want, tries = 1200) {
  for (let i = 0; i < tries; i += 1) {
    const grid = document.querySelector('[role="grid"]')
    if (grid && grid.querySelectorAll('[role="row"]').length >= want) return grid
    if (state.streaming) {
      const mark = document.querySelector('.message-list-sentinel')
      const band = document.querySelector('.mail-list-scroll')
      if (mark) mark.scrollIntoView({ block: 'center' })
      else if (band) band.scrollTop += band.clientHeight
      if (await frame() === null) await tick()
    } else {
      await tick()
    }
  }
  throw new Error(`the list never reached ${want} rows`)
}

/** A second observer on the same grid, with `useGridNav`'s own options. It registers after the
    hook's, so it runs after it: the clock read here is the far side of the hook's `repoint`. */
function watch(grid) {
  const seen = { records: 0, at: null, fired: false }
  const observer = new MutationObserver(records => {
    seen.records += records.length
    seen.at = now()
    seen.fired = true
  })
  observer.observe(grid, {
    childList: true, subtree: true, attributes: true, attributeFilter: ['disabled'],
  })
  return { seen, stop: () => observer.disconnect() }
}

/**
 * One list at one row count. Returns milliseconds, never a verdict.
 *
 * `mountMs` is the second mount's, warm cache: React's render + commit of N rows, the hook's
 * first `repoint()` inside it. `layoutMs` is the forced reflow straight after, `toFrameMs` the
 * wait to the next frame — style, layout and paint, as the browser schedules them.
 */
async function run(kind, rows, iterations = 9, streaming = false) {
  state.rows = rows
  state.streaming = streaming
  const fetchesBefore = window.__probeFetches.length
  const blocksOf = () => window.__probeFetches
    .slice(fetchesBefore).filter(u => u.includes('/api/Mail/Messages')).length
  client = new QueryClient({
    // App.tsx's own defaults, minus the retry policy, which no answer here needs.
    defaultOptions: { queries: { refetchOnWindowFocus: true, staleTime: 30_000, retry: false } },
  })
  root = createRoot(mountPoint())
  root.render(tree(kind))
  const beforeSettle = now()
  await settle(rows)
  const settleMs = now() - beforeSettle
  const blocksToFill = blocksOf()

  // Second mount, cache warm: the first render already holds every row, so the clock below is
  // React's own and not the network's.
  root.unmount()
  root = createRoot(mountPoint())
  const beforeMount = now()
  flushSync(() => root.render(tree(kind)))
  const mountMs = now() - beforeMount
  const grid = document.querySelector('[role="grid"]')
  const beforeLayout = now()
  void grid.getBoundingClientRect().height
  const layoutMs = now() - beforeLayout
  const frameAt = await frame()
  const toFrameMs = frameAt === null ? null : frameAt - beforeMount

  const drawn = grid.querySelectorAll('[role="row"]').length
  if (drawn !== rows) throw new Error(`the bench asked for ${rows} rows and drew ${drawn}`)

  // ---- one keystroke in the search field. The contacts list alone: the mail list's field lives
  // behind a closed loupe, so it is not on screen. `useSelection` is keyed on the query there, so a
  // letter rebuilds the selection, re-runs `filterContacts` and redraws every surviving tile.
  const search = document.querySelector('input.search-input')
  const setSearch = value => {
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set
    setter.call(search, value)
    search.dispatchEvent(new Event('input', { bubbles: true }))
  }
  /** Every prefix of this matches every fixture address (`person12@example.be`), so the tile count
      never moves: the figure is the re-render, not a shorter list. */
  const TYPED = 'example.be'
  const typeOne = async length => {
    const before = now()
    flushSync(() => setSearch(TYPED.slice(0, length)))
    const react = now() - before
    // The query is `useSelection`'s reset key, so a passive effect — already flushed inside the
    // window above — empties the selection and schedules a SECOND render, which is lowered to
    // Default priority and therefore the Scheduler's macrotask. Draining is the only way to see it.
    const beforeSettle = now()
    await tick()
    const settle = now() - beforeSettle
    // The identical window with nothing left pending: the instrument's own floor, so a reader can
    // tell a second render from one macrotask's scheduling latency instead of taking it on faith.
    const beforeIdle = now()
    await tick()
    return { react, settle, idle: now() - beforeIdle }
  }
  let keystrokes = null
  let keystrokeRows = null
  if (search) {
    await typeOne(1)  // warm-up: the first letter also pays the band's own `filtering` change.
    keystrokes = []
    for (let i = 0; i < iterations; i += 1) {
      keystrokes.push(await typeOne(2 + (i % (TYPED.length - 1))))
    }
    keystrokeRows = grid.querySelectorAll('[role="row"]').length
    setSearch('')  // untimed: the ticks below are the unfiltered list's, as the baseline read them.
    await drain()
  }

  const checkSelector = kind === 'mail' ? '.message-row-check' : '.contact-tile-check'
  const boxes = Array.from(grid.querySelectorAll(checkSelector))

  // ---- one checkbox. The first tick is measured apart: it takes the list from nothing selected
  // to something, which also redraws the band above the rows. Every later tick is the steady
  // state, and that is the one a user pays over and over.
  const watcher = watch(grid)

  const tickOne = async box => {
    const before = now()
    flushSync(() => box.click())
    const react = now() - before
    const beforeTickLayout = now()
    void grid.getBoundingClientRect().height
    const layout = now() - beforeTickLayout
    const marker = now()
    watcher.seen.fired = false
    watcher.seen.at = null
    watcher.seen.records = 0
    await micro()
    await micro()
    return {
      react,
      layout,
      hook: watcher.seen.fired ? watcher.seen.at - marker : 0,
      records: watcher.seen.records,
    }
  }

  const firstTick = await tickOne(boxes[0])
  // One warm-up tick is dropped before timing, for `contact-tile-grid.html`'s reason: the first
  // pass after a build pays the allocation and the style recalc the `has-selection` class brings.
  await tickOne(boxes[1])
  const laterTicks = []
  for (let i = 0; i < iterations; i += 1) {
    laterTicks.push(await tickOne(boxes[2 + (i % Math.max(1, boxes.length - 2))]))
  }
  watcher.stop()

  // ---- one arrow key, over the shipped hook. Focus is put on a content cell first, which is
  // where Tab into the list lands; ArrowDown then walks to the next row's cell.
  const cellSelector = kind === 'mail' ? '.message-row-content' : '.contact-tile-content'
  const cells = Array.from(grid.querySelectorAll(cellSelector))
  cells[0].focus()
  const arrow = () => {
    const at = document.activeElement
    const before = now()
    at.dispatchEvent(new KeyboardEvent('keydown', {
      key: 'ArrowDown', bubbles: true, cancelable: true,
    }))
    return now() - before
  }
  arrow()  // warm-up, dropped: the first keydown pays the hook's matrix build.
  const arrows = Array.from({ length: iterations }, () => arrow())

  // ---- the hook's own two loops, restated exactly as `probes/contact-tile-grid.html` restates
  // them, so the two lots' numbers are comparable. This is the ONLY figure below that is not
  // taken through the shipped path.
  const rowsIn = g => Array.from(g.querySelectorAll(ROW))
    .map(row => focusablesIn(row).filter(reachable))
    .filter(widgets => widgets.length > 0)
  const rewrite = (widgets, target) => widgets.forEach(widget => {
    const want = widget === target ? '0' : '-1'
    if (widget.getAttribute('tabindex') !== want) widget.setAttribute('tabindex', want)
  })
  const matrix = rowsIn(grid)
  const walkRuns = []
  rowsIn(grid)
  for (let i = 0; i < 5; i += 1) {
    const before = now()
    rowsIn(grid)
    walkRuns.push(now() - before)
  }
  const repointRuns = []
  const flat = matrix.flat()
  rewrite(flat, flat[1])
  for (let i = 0; i < 5; i += 1) {
    const before = now()
    rewrite(rowsIn(grid).flat(), flat[1])
    repointRuns.push(now() - before)
  }

  // The stream is the mail list's alone — contacts take their rows as a prop — so a contacts run
  // under `streaming` must not report a mode, a block count or a fill it never had.
  const streams = kind === 'mail' && streaming
  const answer = {
    list: kind,
    rows,
    iterations,
    streaming: streams,
    // Not a figure: the blocks the first mount needed, the blocks the SECOND mount added (0 means
    // it read the cache rather than replaying the stream), and how long filling took — if that
    // passes 30s the pages are stale and a remount would replay them, which would be visible here.
    blocksToFill: streams ? blocksToFill : null,
    blocksOnRemount: streams ? blocksOf() - blocksToFill : null,
    fillMs: streams ? round(settleMs, 0) : null,
    widgetsPerRow: matrix[0].length,
    cellsPerRow: grid.querySelectorAll('[role="row"]')[0].querySelectorAll('[role="gridcell"]').length,
    mountMs: round(mountMs, 1),
    mountLayoutMs: round(layoutMs, 1),
    mountToFrameMs: toFrameMs === null ? null : round(toFrameMs, 1),
    firstTickReactMs: round(firstTick.react, 2),
    firstTickLayoutMs: round(firstTick.layout, 2),
    firstTickHookMs: round(firstTick.hook, 3),
    firstTickRecords: firstTick.records,
    tickReactMs: round(median(laterTicks.map(t => t.react)), 2),
    tickLayoutMs: round(median(laterTicks.map(t => t.layout)), 2),
    tickHookMs: round(median(laterTicks.map(t => t.hook)), 3),
    tickRecords: median(laterTicks.map(t => t.records)),
    tickReactWorstMs: round(Math.max(...laterTicks.map(t => t.react)), 2),
    // null where the list draws no search field. A letter costs TWO renders of the same list: the
    // one its own state schedules (`keystrokeReactMs`) and the one `useSelection`'s reset schedules
    // behind it, inside `keystrokeSettleMs`. `keystrokeMs` nets the idle floor out of the pair.
    keystrokeReactMs: keystrokes && round(median(keystrokes.map(k => k.react)), 2),
    keystrokeSettleMs: keystrokes && round(median(keystrokes.map(k => k.settle)), 2),
    keystrokeIdleMs: keystrokes && round(median(keystrokes.map(k => k.idle)), 2),
    keystrokeMs: keystrokes && round(median(keystrokes.map(k => k.react + k.settle - k.idle)), 2),
    keystrokeWorstMs: keystrokes
      && round(Math.max(...keystrokes.map(k => k.react + k.settle - k.idle)), 2),
    keystrokeRowsDrawn: keystrokeRows,
    arrowMs: round(median(arrows), 3),
    arrowWorstMs: round(Math.max(...arrows), 3),
    walkMs: round(median(walkRuns), 2),
    repointMs: round(median(repointRuns), 2),
    frameBudgetMs: 16.7,
  }
  root.unmount()
  document.getElementById('out').textContent += `${JSON.stringify(answer)}\n`
  return answer
}

window.bench = run

/**
 * The bench's own two assumptions, checked rather than assumed. `arrowMs` over a keydown that
 * moved nothing would be a number about nothing, and `records: 0` has to mean the hook was given
 * no mutation to answer — not that the watcher above is deaf. Run it once per browser before
 * quoting any figure from `bench`.
 */
window.check = async (kind = 'mail', rows = 100) => {
  state.rows = rows
  state.streaming = false
  client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  root = createRoot(mountPoint())
  root.render(tree(kind))
  const grid = await settle(rows)
  const watcher = watch(grid)

  // The watcher can hear: one appended node, then taken away again.
  const canary = document.createElement('div')
  grid.appendChild(canary)
  await micro()
  await micro()
  const heard = watcher.seen.records
  canary.remove()
  await micro()
  await micro()

  // A letter has to leave every row standing, or `keystrokeMs` is a figure about a shorter list.
  // Before the tick below, because a checked box takes the field off the band.
  const field = document.querySelector('input.search-input')
  let typedKeptEveryRow = null
  let typedValue = null
  if (field) {
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set
    const type = value => flushSync(() => {
      setter.call(field, value)
      field.dispatchEvent(new Event('input', { bubbles: true }))
    })
    type('exam')
    flushSync(() => {})
    typedKeptEveryRow = grid.querySelectorAll(ROW).length === rows
    typedValue = field.value
    type('')
    await tick()
  }

  const cellSelector = kind === 'mail' ? '.message-row-content' : '.contact-tile-content'
  const cells = Array.from(grid.querySelectorAll(cellSelector))
  cells[0].focus()
  const from = document.activeElement
  cells[0].dispatchEvent(new KeyboardEvent('keydown', {
    key: 'ArrowDown', bubbles: true, cancelable: true,
  }))
  const to = document.activeElement

  watcher.seen.records = 0
  const box = grid.querySelectorAll(
    kind === 'mail' ? '.message-row-check' : '.contact-tile-check')[3]
  flushSync(() => box.click())
  await micro()
  await micro()

  const answer = {
    watcherHeardAnAppend: heard,
    typedKeptEveryRow,
    typedValue,
    arrowMovedFocus: to !== from && grid.contains(to),
    arrowLandedOneRowDown: to.closest(ROW) === cells[1].closest(ROW),
    focusAfterArrow: to.className,
    tickCheckedTheBox: box.checked,
    tickMutationRecords: watcher.seen.records,
    rowsAfterTick: grid.querySelectorAll(ROW).length,
  }
  watcher.stop()
  root.unmount()
  return answer
}

window.benchAll = async (sizes = [100, 500, 1000, 2000], iterations = 9, streaming = false) => {
  const rows = []
  for (const kind of ['mail', 'contacts']) {
    for (const size of sizes) rows.push(await run(kind, size, iterations, streaming))
  }
  const report = {
    userAgent: navigator.userAgent,
    viewport: `${innerWidth}x${innerHeight}`,
    hardwareConcurrency: navigator.hardwareConcurrency,
    reactMode: import.meta.env.MODE,
    strictMode: false,
    at: new Date().toISOString(),
    rows,
  }
  document.getElementById('out').textContent = JSON.stringify(report, null, 2)
  window.__probeReport = report
  return report
}

// The catalogue has to be in hand before a component renders, or every label is its own key and
// the rows are the wrong width. Awaited once, here; `window.ready` is what a driver waits on.
window.ready = initI18n('en').then(() => {
  localStorage.setItem('sessionActive', '1')
  localStorage.removeItem('mail.activeAccount')
  document.getElementById('out').textContent =
    'ready — call await window.benchAll() (or window.bench("mail", 1000))\n'
  return true
})
