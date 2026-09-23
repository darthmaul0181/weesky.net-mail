import { execSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import license from 'rollup-plugin-license'
import { versionStamp } from './src/lib/versionStamp.ts'

// One product version for the web app and the API, read from the repository's VERSION file.
const PRODUCT_VERSION = readFileSync(new URL('../../VERSION', import.meta.url), 'utf8').trim()
/** Our own borrowed assets, one row each. The full notices stay in THIRD-PARTY.md, which is
    the record; this is the list the page shows. */
const OWN_ASSETS = [{
  name: 'new-mail.mp3 (notification sound)',
  version: 'from RainLoop Webmail',
  license: 'MIT',
  author: 'RainLoopTeam',
  homepage: 'https://github.com/RainLoop/rainloop-webmail',
}]

/** The same switch the API reads as -p:ReleaseBuild, set by the deploy on master: anything else
    — a dev deployment, `npm run dev`, a local build — is -dev, so the two halves cannot disagree
    about what a release is. */
const RELEASE = process.env.RELEASE_BUILD === 'true'

function shortCommit() {
  if (process.env.GITHUB_SHA) return process.env.GITHUB_SHA.slice(0, 7)
  try {
    return execSync('git rev-parse --short=7 HEAD', { encoding: 'utf8' }).trim()
  } catch {
    return null
  }
}

/** The page the About tab links: what is bundled, who wrote it and under which licence. The
    licence TEXTS are deliberately not reproduced — they are a wall nobody reads, and each row
    links the project that carries its own. THIRD-PARTY.md keeps the full notices in the repo. */
const NOTICES_FILE = 'third-party-licenses.html'
let notices = ''

const escape = value => String(value ?? '')
  .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')

/** `author` is a person object, a string, or absent; `repository` a string or an object. */
function creditOf(dependency) {
  const author = dependency.author?.name ?? dependency.author
  if (typeof author === 'string' && author.trim()) return author.trim()
  const contributors = (dependency.contributors ?? []).map(c => c?.name ?? c).filter(Boolean)
  return contributors.length ? contributors.join(', ') : null
}

function linkOf(dependency) {
  const repository = dependency.repository?.url ?? dependency.repository
  const url = dependency.homepage ?? repository
  if (typeof url !== 'string') return null
  return url.replace(/^git\+/, '').replace(/\.git$/, '').replace(/^git:\/\//, 'https://')
}

function rowOf(dependency) {
  const credit = creditOf(dependency)
  const link = linkOf(dependency)
  const name = escape(dependency.name)
  return `      <li class="row">
        <div class="who">
          <span class="name">${link ? `<a href="${escape(link)}">${name}</a>` : name}</span>
          <span class="version">${escape(dependency.version)}</span>
        </div>
        <span class="author">${credit ? escape(credit) : '—'}</span>
        <span class="licence">${escape(dependency.license ?? 'see project')}</span>
      </li>`
}

function noticesPage(rows) {
  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>Scotty webmail — third-party components</title>
<style>
  :root { color-scheme: light dark; --bg: #f6f3ef; --card: #fdfbf8; --text: #1c1a18;
          --muted: #6f6a64; --line: #e5e0da; --band: #182238; --band-text: #ffffff;
          --band-muted: #c3ccdd; }
  @media (prefers-color-scheme: dark) {
    :root { --bg: #17191d; --card: #212429; --text: #e9e5e1; --muted: #9b968f; --line: #2c3038;
            --band: #0f1626; --band-text: #e9e5e1; --band-muted: #aab6cc; }
  }
  * { box-sizing: border-box; }
  body { margin: 0; background: var(--bg); color: var(--text);
         font: 14px/1.5 system-ui, -apple-system, 'Segoe UI', sans-serif; }
  header { padding: 28px 24px; background: var(--band); color: var(--band-text); }
  .wrap { max-width: 860px; margin: 0 auto; }
  h1 { margin: 0; font-size: 22px; font-weight: 600; letter-spacing: -0.01em; }
  h1 span { font-weight: 300; color: var(--band-muted); }
  header p { margin: 8px 0 0; font-size: 13px; color: var(--band-muted); }
  main { padding: 24px; }
  ul { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 6px; }
  .head, .row { display: grid; grid-template-columns: minmax(0, 2fr) minmax(0, 1.4fr) max-content;
                gap: 16px; align-items: center; }
  .head { padding: 0 14px 6px; font-size: 12px; font-weight: 600; text-transform: uppercase;
          letter-spacing: 0.06em; color: var(--muted); }
  .row { padding: 10px 14px; background: var(--card); border: 1px solid var(--line);
         border-radius: 4px; }
  .who { display: flex; flex-wrap: wrap; align-items: baseline; gap: 8px; min-width: 0; }
  .name { font-weight: 600; word-break: break-word; }
  .name a { color: inherit; }
  .version, .author { color: var(--muted); font-size: 13px; }
  .version { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 12px; }
  .licence { padding: 2px 10px; border: 1px solid var(--line); border-radius: 999px;
             font-size: 12px; white-space: nowrap; }
  footer { padding: 0 24px 32px; font-size: 12px; color: var(--muted); }
  @media (max-width: 599px) {
    .head { display: none; }
    .row { grid-template-columns: 1fr; gap: 4px; }
    .licence { justify-self: start; }
  }
</style>
</head>
<body>
<header><div class="wrap">
  <h1>Scotty <span>webmail</span></h1>
  <p>Third-party components shipped with this application. Each project carries its own licence
     text; follow a name to read it.</p>
</div></header>
<main><div class="wrap">
  <div class="head"><span>Component</span><span>Author</span><span>Licence</span></div>
  <ul>
${rows.join('\n')}
  </ul>
</div></main>
<footer><div class="wrap">© ${new Date().getFullYear()} darth-weesky.net</div></footer>
</body>
</html>
`
}

const collectNotices = license({
  thirdParty: {
    output: dependencies => {
      const sorted = [...dependencies].sort((a, b) => a.name.localeCompare(b.name))
      notices = noticesPage([...sorted, ...OWN_ASSETS].map(rowOf))
    },
  },
})

// Emitted as a bundle ASSET rather than written to disk by the licence plugin itself: Vite empties
// the output directory after that hook, deleting the file the plugin had just written.
const emitNotices = {
  name: 'third-party-notices',
  generateBundle() {
    this.emitFile({ type: 'asset', fileName: NOTICES_FILE, source: notices })
  },
}

export default defineConfig(({ command, mode }) => {
  // No built-in default: a forgotten .env.production would ship a build posting credentials to
  // someone else's API. The gate and Vite both read envDir (this folder, whatever the cwd), and a
  // blank value is trimmed so it does not count as set.
  const envDir = fileURLToPath(new URL('.', import.meta.url))
  if (command === 'build' && !loadEnv(mode, envDir, 'VITE_').VITE_API_BASE?.trim()) {
    throw new Error(`VITE_API_BASE is not set. Write it to .env.${mode} before building (install/README.md, step 1.2).`)
  }

  return {
    envDir,
    plugins: [react()],
    // Build-only plugins, so they belong to the output rather than to Vite's own list.
    build: { rolldownOptions: { plugins: [collectNotices, emitNotices] } },
    define: {
      __APP_VERSION__: JSON.stringify(versionStamp(PRODUCT_VERSION, RELEASE)),
      __APP_COMMIT__: JSON.stringify(shortCommit()),
      __APP_BUILT_AT__: JSON.stringify(new Date().toISOString()),
    },
    server: {
      port: 5173,
      allowedHosts: ['.mail.weesky.net'],
    },
    test: {
      environment: 'jsdom',
      globals: false,
      // Palette parity and the responsive contract test parse stylesheets for their actual text;
      // Vitest mocks a CSS import to '' otherwise, which passes every check vacuously. Keep this
      // broad enough to cover every sheet either test globs — the responsive contract's `./*.css`
      // spans the whole styles directory, so a narrower list silently blinds it to whichever file
      // falls outside the pattern.
      css: { include: [/src\/.*\.css/] },
      setupFiles: ['./src/test-setup.ts'],
      // Reserved .test host; tests assert against the exported API_BASE, never this literal.
      env: { VITE_API_BASE: 'https://api.example.test' },
      coverage: {
        provider: 'v8',
        reporter: ['text', 'cobertura'],
        reportsDirectory: './coverage',
      },
    },
  }
})
