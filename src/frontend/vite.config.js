import { execSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import license from 'rollup-plugin-license'
import { versionStamp } from './src/lib/versionStamp.js'

// One product version for the web app and the API, read from the repository's VERSION file.
const PRODUCT_VERSION = readFileSync(new URL('../../VERSION', import.meta.url), 'utf8').trim()
const OWN_NOTICES = readFileSync(new URL('./THIRD-PARTY.md', import.meta.url), 'utf8')

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

/** The notices every bundled library's licence requires, plus our own assets' (THIRD-PARTY.md).
    The About tab links the file this pair produces. */
const NOTICES_FILE = 'third-party-licenses.txt'
let notices = ''

const collectNotices = license({
  thirdParty: {
    output: dependencies => {
      notices = [
        // No brackets of our own: an SPDX expression brings its own — `(MPL-2.0 OR Apache-2.0)`.
        ...dependencies.map(d => `${d.name} ${d.version} — ${d.license}\n\n${d.licenseText ?? ''}`.trim()),
        OWN_NOTICES.trim(),
      ].join(`\n\n${'-'.repeat(72)}\n\n`)
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

export default defineConfig(() => ({
  plugins: [react()],
  // Build-only plugins, so they belong to the output rather than to Vite's own list.
  build: { rollupOptions: { plugins: [collectNotices, emitNotices] } },
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
    globals: true,
    // Palette parity and the responsive contract test parse stylesheets for their actual text;
    // Vitest mocks a CSS import to '' otherwise, which passes every check vacuously. Keep this
    // broad enough to cover every sheet either test globs — the responsive contract's `./*.css`
    // spans the whole styles directory, so a narrower list silently blinds it to whichever file
    // falls outside the pattern.
    css: { include: [/src\/.*\.css/] },
    setupFiles: ['./src/test-setup.js'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'cobertura'],
      reportsDirectory: './coverage',
    },
  },
}))
