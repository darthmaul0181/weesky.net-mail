/*
 * Regenerates docs/palette-overview.html from the theme files.
 * Usage (from anywhere in the repo):  node docs/gen-palettes.mjs
 * Optional first arg overrides the output path.
 *
 * Reads src/frontend/src/styles/theme-*.css, extracts each palette's role
 * tokens (dark = light block + its dark overrides), and renders, per palette
 * and mode, a mini mock-up plus the distinct colours grouped by role.
 */
import { readFileSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const HERE = dirname(fileURLToPath(import.meta.url))
const STYLES = join(HERE, '..', 'src', 'frontend', 'src', 'styles')
const OUT = process.argv[2] || join(HERE, 'palette-overview.html')
const PALETTES = ['night', 'classic', 'forest', 'slate', 'plum', 'ink']

// Colours grouped by functional role, so the ~6 core families read at a glance instead of a flat
// grid of 20+ near-duplicates. Every token must land in exactly one family (guarded below).
const FAMILIES = [
  ['Surfaces & borders', ['--bg', '--surface', '--surface-raised', '--surface-sunken', '--border',
    '--list-separator', '--reader-header-border', '--attachment-chip-bg', '--list-row-hover', '--pane-item-hover']],
  ['Folder column', ['--folders-bg', '--folders-item-hover']],
  ['Text', ['--text', '--text-muted', '--quote-text']],
  ['Top bar & rail', ['--topbar-bg', '--topbar-fg', '--rail-bg', '--rail-fg', '--rail-item',
    '--rail-item-active', '--rail-item-active-fg']],
  ['Accent & selection', ['--action-primary', '--action-primary-hover', '--action-primary-fg',
    '--accent-unread', '--badge-count-bg', '--badge-count-fg', '--icon-hover-accent',
    '--pane-item-active-bg', '--pane-item-active-fg', '--list-row-selected-bg', '--list-row-selected-fg']],
  ['Alerts', ['--danger', '--danger-hover', '--success', '--icon-hover-danger']],
]

function parseBlock(css, selector) {
  const i = css.indexOf(selector)
  if (i === -1) return {}
  const open = css.indexOf('{', i)
  const close = css.indexOf('}', open)
  const body = css.slice(open + 1, close)
  const out = {}
  for (const m of body.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)) out[m[1].trim()] = m[2].trim()
  return out
}

function descOf(css) {
  const m = css.match(/\/\*\s*Palette[^\n]*?—\s*([^.*]+)/)
  return m ? m[1].trim() : ''
}

const data = {}
for (const p of PALETTES) {
  const css = readFileSync(join(STYLES, `theme-${p}.css`), 'utf8')
  const base = parseBlock(css, `[data-palette='${p}'] {`)
  const darkOverrides = parseBlock(css, `[data-palette='${p}'][data-theme='dark']`)
  data[p] = { desc: descOf(css), light: base, dark: { ...base, ...darkOverrides } }
}

const ORDER = Object.keys(data.night.light)
const esc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
const varsStyle = tokens => ORDER.map(t => `${t}:${tokens[t]}`).join(';')

// Guard: catch a token that drifted out of every family (a new theme token would silently vanish).
const mapped = new Set(FAMILIES.flatMap(([, ks]) => ks))
const unmapped = ORDER.filter(t => !mapped.has(t))
if (unmapped.length) console.warn('UNMAPPED tokens (add to a family):', unmapped.join(', '))

function familyColors(tokens, keys) {
  const seen = new Set(), out = []
  for (const k of keys) {
    const v = (tokens[k] || '').toLowerCase()
    if (v && !seen.has(v)) { seen.add(v); out.push(v) }
  }
  return out
}

function swatches(tokens) {
  return FAMILIES.map(([name, keys]) => {
    const cols = familyColors(tokens, keys)
    if (!cols.length) return ''
    const cells = cols.map(v =>
      `<div class="sw"><span class="chip" style="background:${v}"></span><span class="hex">${esc(v)}</span></div>`).join('')
    return `
        <div class="fam"><div class="fam-h">${name} <span class="fam-n">${cols.length}</span></div><div class="fam-row">${cells}</div></div>`
  }).join('')
}

function mockup() {
  return `
      <div class="mock">
        <div class="mk-bar"></div>
        <div class="mk-body">
          <div class="mk-rail"><i></i><i></i><i class="on"></i><span class="mk-sp"></span><i></i></div>
          <div class="mk-fold">
            <div class="mk-f">Inbox <b>3</b></div>
            <div class="mk-f on">Sent</div>
            <div class="mk-f">Archive</div>
          </div>
          <div class="mk-list">
            <div class="mk-row unread"><span class="mk-l1">Alice — Project update</span><span class="mk-l2">Here is the latest…</span></div>
            <div class="mk-row"><span class="mk-l1">Bob — Lunch?</span><span class="mk-l2">Free at noon</span></div>
            <div class="mk-actions"><span class="mk-btn">Compose</span></div>
          </div>
        </div>
      </div>`
}

function panel(mode, tokens) {
  return `
    <div class="panel" style="${varsStyle(tokens)}">
      <div class="panel-h">${mode === 'light' ? 'Light' : 'Dark'}</div>
      ${mockup()}
      ${swatches(tokens)}
    </div>`
}

const cards = PALETTES.map(p => `
  <section class="card">
    <h2>${p}<small>${esc(data[p].desc)}</small></h2>
    <div class="panels">
      ${panel('light', data[p].light)}
      ${panel('dark', data[p].dark)}
    </div>
  </section>`).join('')

const html = `<!doctype html><html lang="en"><head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Weesky mail — palette overview</title>
<style>
  * { box-sizing: border-box; }
  body { margin: 0; font: 14px/1.4 system-ui, -apple-system, 'Segoe UI', sans-serif;
         background: #eceff3; color: #1c1a18; padding: 28px; }
  h1 { font-size: 22px; margin: 0 0 4px; }
  .sub { color: #6f6a64; margin: 0 0 24px; max-width: 74ch; }
  .card { background: #fff; border: 1px solid #dfe3e8; border-radius: 12px;
          padding: 18px 20px; margin-bottom: 22px; box-shadow: 0 1px 3px rgba(0,0,0,.05); }
  .card h2 { font-size: 18px; margin: 0 0 14px; text-transform: capitalize; }
  .card h2 small { font-weight: 400; font-size: 13px; color: #6f6a64; margin-left: 10px; text-transform: none; }
  .panels { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
  @media (max-width: 720px) { .panels { grid-template-columns: 1fr; } }
  .panel { border: 1px solid var(--border); border-radius: 10px; padding: 14px;
           background: var(--bg); color: var(--text); }
  .panel-h { font-size: 12px; font-weight: 700; text-transform: uppercase; letter-spacing: .06em;
             color: var(--text-muted); margin-bottom: 12px; }
  /* mini mockup */
  .mock { border: 1px solid var(--border); border-radius: 8px; overflow: hidden; margin-bottom: 16px; }
  .mk-bar { height: 16px; background: var(--topbar-bg); }
  .mk-body { display: flex; height: 128px; }
  .mk-rail { width: 26px; background: var(--rail-bg); display: flex; flex-direction: column;
             align-items: center; gap: 5px; padding: 6px 0; }
  .mk-rail i { width: 16px; height: 16px; border-radius: 5px; background: var(--rail-item); }
  .mk-rail i.on { background: var(--rail-item-active); }
  .mk-sp { flex: 1; }
  .mk-fold { width: 96px; background: var(--folders-bg); padding: 7px 6px; display: flex;
             flex-direction: column; gap: 4px; }
  .mk-f { font-size: 11px; padding: 4px 7px; border-radius: 4px; color: var(--text); display: flex; align-items: center; }
  .mk-f b { margin-left: auto; background: var(--badge-count-bg); color: var(--badge-count-fg);
            font-size: 9px; border-radius: 8px; padding: 0 5px; }
  .mk-f.on { background: var(--pane-item-active-bg); color: var(--pane-item-active-fg); font-weight: 650; }
  .mk-list { flex: 1; background: var(--surface); padding: 6px; display: flex; flex-direction: column; gap: 5px; }
  .mk-row { padding: 5px 7px; border-radius: 4px; display: flex; flex-direction: column; gap: 2px;
            border-bottom: 1px solid var(--list-separator); }
  .mk-row.unread { background: var(--list-row-selected-bg); color: var(--list-row-selected-fg);
                   box-shadow: inset 3px 0 0 var(--accent-unread); }
  .mk-l1 { font-size: 11px; font-weight: 600; }
  .mk-row.unread .mk-l1 { font-weight: 800; }
  .mk-l2 { font-size: 10px; color: var(--text-muted); }
  .mk-actions { margin-top: auto; }
  .mk-btn { display: inline-block; font-size: 11px; padding: 5px 12px; border-radius: 5px;
            background: var(--action-primary); color: var(--action-primary-fg); }
  /* colour families */
  .fam { margin-bottom: 12px; }
  .fam:last-child { margin-bottom: 0; }
  .fam-h { font-size: 10px; font-weight: 700; text-transform: uppercase; letter-spacing: .05em;
           color: var(--text-muted); margin-bottom: 7px; }
  .fam-n { font-weight: 400; opacity: .7; }
  .fam-row { display: flex; flex-wrap: wrap; gap: 10px; }
  .sw { display: flex; flex-direction: column; align-items: center; gap: 4px; width: 52px; }
  .chip { width: 42px; height: 42px; border-radius: 6px; border: 1px solid rgba(128,128,128,.4); }
  .hex { font-size: 10px; color: var(--text-muted); font-variant-numeric: tabular-nums; }
</style></head><body>
<h1>Weesky mail — palette overview</h1>
<p class="sub">Each palette in light and dark: a mini mock-up drawn with the palette's own tokens, then its distinct colours grouped by role — surfaces, folder column, text, top bar &amp; rail, accent &amp; selection, alerts. Each square is captioned with its hex value. Generated from <code>src/frontend/src/styles/theme-*.css</code>.</p>
${cards}
</body></html>`

writeFileSync(OUT, html)
console.log(`wrote ${OUT}`)
