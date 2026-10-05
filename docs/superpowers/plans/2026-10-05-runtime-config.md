# Configuration lue au démarrage — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rendre le front et l'API configurables sans reconstruire : adresse d'API lue dans `/config.js`, clé de session générée si absente, logs sur la console au choix, proxy désigné par une plage.

**Architecture:** Côté front, un module `runtimeConfig` lit et valide `window.SCOTTY_CONFIG` une fois, sans jamais lever d'exception ; `main.tsx` affiche un écran d'erreur texte au lieu de l'application quand la configuration est mauvaise. Un plugin Vite émet `config.js` au build (sauf en mode `container`) et le sert en développement. Côté API, le dossier d'état est calculé une fois dans `Program.cs` et partagé entre le trousseau et la clé de session ; la clé est injectée par `PostConfigure<TokenConstants>`, avant la validation existante. Les logs et les plages de proxy se règlent dans leurs extensions existantes.

**Tech Stack:** React + TypeScript, Vite 8, Vitest ; ASP.NET Core .NET 10, Serilog, xUnit 2.9, Moq.

**Spec:** `docs/superpowers/2026-10-05-runtime-config-design.md`

## Global Constraints

- `deploy.yml` n'est **pas** modifié. Le serveur du propriétaire (clé fournie, logs en fichiers, `KnownProxies`) ne voit aucune différence.
- `config.js` contient exactement : `window.SCOTTY_CONFIG = { apiBase: <JSON.stringify(valeur)> }`.
- Mode de build exempté : `container`, et lui seul. Hors `container`, un `VITE_API_BASE` vide arrête le build avec le message actuel.
- Tests front : `window.SCOTTY_CONFIG = { apiBase: 'https://api.example.test' }`.
- Clé de session : fichier `session-signing.key` dans le dossier d'état, 64 octets aléatoires en base64, `0600` sous Unix, jamais réécrit, jamais journalisé.
- Dossier d'état : `STATE_DIRECTORY` (premier élément), sinon `<ContentRoot>/keys` en développement, sinon refus de démarrer (message actuel). Trousseau : `<dossier d'état>/keys`.
- `Logs:Output` : `file` (défaut, absent ou vide) ou `console`, casse ignorée ; toute autre valeur refuse le démarrage.
- `ForwardedHeaders:KnownNetworks` : plages CIDR ; `/0` refusé ; au moins une entrée dans `KnownProxies` ou `KnownNetworks` hors développement ; `ForwardLimit = 1` inchangé.
- Les textes affichés et les messages d'erreur sont en anglais (l'UI et les logs le sont).
- Commentaires : seulement quand le code ne s'explique pas, 3 lignes max. Pas de duplication.
- Tests backend : `dotnet test` (jamais `--no-build` quand des fichiers de test sont ajoutés). Avant chaque commit, `git checkout -- '**/ApiDocumentation.xml'` si `git status` le montre modifié sans raison.
- Le dépôt est en `autocrlf=true` : pour un remplacement multi-lignes dans un fichier existant, préférer un script python plutôt que l'outil Edit, et vérifier `git diff` (aucune ligne modifiée hors du changement voulu).
- Messages de commit : deux lignes max, en anglais, via heredoc `git commit -F - <<'EOF'`, terminés par `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Ne jamais pousser.

## Review Focus

1. Une adresse d'API avec une barre finale ou des espaces (`" https://api.example.net/ "`) : base `https://api.example.net`, pas de `//api`. Test dans la tâche 1.
2. Une valeur hostile dans `apiBase` (`<img src=x onerror=alert(1)>`) : l'écran d'erreur l'affiche en texte, aucun élément HTML créé. Une valeur avec guillemets dans `VITE_API_BASE` : `config.js` reste un script valide qui redonne exactement la valeur. Tests dans les tâches 1 et 2.
3. Deux démarrages simultanés sans clé (un redémarrage pendant qu'une seconde instance démarre) : une seule clé, la même pour les deux, aucun fichier temporaire laissé. Test dans la tâche 3.
4. Une plage écrite avec une adresse qui n'est pas le début de la plage (`10.1.2.3/8`) : refus de démarrer avec un message qui dit quoi écrire, pas un proxy silencieusement ignoré. Test dans la tâche 5.
5. `Logs__Output=console` : la ligne de requête garde l'adresse du client, les requêtes `Microsoft.AspNetCore` en Information restent filtrées, et aucun fichier n'est créé. Test dans la tâche 4.

---

### Task 1: Le front lit son adresse d'API dans `window.SCOTTY_CONFIG`

**Files:**
- Create: `src/frontend/src/lib/runtimeConfig.ts`
- Create: `src/frontend/src/test-runtime-config.ts`
- Modify: `src/frontend/src/api.ts:43`
- Modify: `src/frontend/src/lib/appLogo.ts:14`
- Modify: `src/frontend/src/main.tsx`
- Modify: `src/frontend/src/vite-env.d.ts`
- Modify: `src/frontend/src/test-setup.ts:1`
- Modify: `src/frontend/vite.config.js` (retirer `test.env`)
- Modify: `src/frontend/src/api.test.ts:708-710` (commentaire seulement)
- Test: `src/frontend/src/lib/runtimeConfig.test.ts` (create)
- Test: `src/frontend/src/main.test.tsx` (create)

**Interfaces:**
- Produces (`src/lib/runtimeConfig.ts`) :
  - `export type RuntimeConfig = { apiBase: string } | { error: string }`
  - `export function parseRuntimeConfig(config: unknown): RuntimeConfig`
  - `export function showConfigError(root: HTMLElement, message: string): void`
  - `export const runtimeConfig: RuntimeConfig`
  - `export const configuredApiBase: string`
- Produces (`vite-env.d.ts`) : `interface Window { SCOTTY_CONFIG?: { apiBase?: unknown } }`

- [ ] **Step 1: Write the failing tests**

`src/frontend/src/lib/runtimeConfig.test.ts` :

```ts
import { describe, expect, it } from 'vitest'
import { parseRuntimeConfig, showConfigError } from './runtimeConfig'

describe('parseRuntimeConfig', () => {
  it.each([
    ['https://api.example.net', 'https://api.example.net'],
    ['http://localhost:5000', 'http://localhost:5000'],
    ['https://api.example.net/', 'https://api.example.net'],
    ['https://api.example.net//', 'https://api.example.net'],
    ['  https://api.example.net/  ', 'https://api.example.net'],
    ['https://example.net/webmail/', 'https://example.net/webmail'],
    ['HTTPS://API.example.net', 'HTTPS://API.example.net'],
  ])('keeps the absolute address %j as %j', (value, expected) => {
    expect(parseRuntimeConfig({ apiBase: value })).toEqual({ apiBase: expected })
  })

  it.each([[''], ['   ']])('reads %j as this site\'s own address', value => {
    expect(parseRuntimeConfig({ apiBase: value })).toEqual({ apiBase: '' })
  })

  it('reads a missing apiBase as this site\'s own address', () => {
    expect(parseRuntimeConfig({})).toEqual({ apiBase: '' })
  })

  it.each([
    ['api.example.net'],
    ['//api.example.net'],
    ['ftp://api.example.net'],
    ['https://'],
    ['https://api.example.net/?x=1'],
    ['https://api.example.net/#top'],
    ['https://user:secret@api.example.net'],
  ])('refuses %j and quotes it', value => {
    const result = parseRuntimeConfig({ apiBase: value })
    expect(result).toHaveProperty('error')
    expect((result as { error: string }).error).toContain(JSON.stringify(value))
  })

  it('refuses a value that is not a string', () => {
    expect(parseRuntimeConfig({ apiBase: 42 })).toEqual({ error: expect.stringContaining('42') })
  })

  it.each([[undefined], [null], ['https://api.example.net']])(
    'reports a missing configuration when SCOTTY_CONFIG is %j',
    config => {
      expect(parseRuntimeConfig(config)).toEqual({ error: expect.stringContaining('/config.js') })
    },
  )
})

describe('showConfigError', () => {
  it('shows the message as text, never as markup', () => {
    const root = document.createElement('div')
    root.innerHTML = '<span>app</span>'

    showConfigError(root, 'Invalid API address in /config.js: "<img src=x onerror=alert(1)>"')

    expect(root.textContent).toContain('<img src=x onerror=alert(1)>')
    expect(root.querySelector('img')).toBeNull()
    expect(root.querySelector('span')).toBeNull()
  })
})
```

`src/frontend/src/main.test.tsx` :

```tsx
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const initI18n = vi.hoisted(() => vi.fn(() => new Promise<never>(() => {})))
vi.mock('./lib/i18n', async importOriginal => ({ ...await importOriginal<object>(), initI18n }))

describe('main', () => {
  const saved = window.SCOTTY_CONFIG

  beforeEach(() => {
    document.body.innerHTML = '<div id="root"></div>'
    initI18n.mockClear()
    vi.resetModules()
  })

  afterEach(() => {
    window.SCOTTY_CONFIG = saved
  })

  it('shows the configuration error instead of the app when /config.js did not run', async () => {
    delete window.SCOTTY_CONFIG

    await import('./main')

    expect(document.getElementById('root')!.textContent).toContain('/config.js')
    expect(initI18n).not.toHaveBeenCalled()
  })

  it('shows the configuration error, quoting the value, for an invalid address', async () => {
    window.SCOTTY_CONFIG = { apiBase: 'api.example.net' }

    await import('./main')

    expect(document.getElementById('root')!.textContent).toContain('"api.example.net"')
    expect(initI18n).not.toHaveBeenCalled()
  })

  it('starts the app on a valid configuration', async () => {
    window.SCOTTY_CONFIG = { apiBase: 'https://api.example.test' }

    await import('./main')

    expect(initI18n).toHaveBeenCalledOnce()
  })
})
```

Note : `initI18n` est remplacé par une promesse qui ne se résout jamais, pour que le troisième test ne monte pas toute l'application.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd src/frontend && npx vitest run src/lib/runtimeConfig.test.ts src/main.test.tsx`
Expected: FAIL — `Failed to resolve import "./runtimeConfig"`.

- [ ] **Step 3: Implement**

`src/frontend/src/lib/runtimeConfig.ts` :

```ts
/** What /config.js left in window.SCOTTY_CONFIG, checked once. Never throws: api.ts reads it while
    the module graph loads, and a throw there would leave a blank page instead of the error screen. */
export type RuntimeConfig = { apiBase: string } | { error: string }

export function parseRuntimeConfig(config: unknown): RuntimeConfig {
  if (typeof config !== 'object' || config === null) {
    return { error: 'Configuration missing: /config.js did not define window.SCOTTY_CONFIG.' }
  }
  const value = (config as { apiBase?: unknown }).apiBase
  if (value === undefined) return { apiBase: '' }
  if (typeof value === 'string') {
    const trimmed = value.trim()
    if (trimmed === '') return { apiBase: '' }
    if (isApiBase(trimmed)) return { apiBase: trimmed.replace(/\/+$/, '') }
  }
  return {
    error: `Invalid API address in /config.js: ${JSON.stringify(value)}. ` +
      'Expected an http:// or https:// address, or "" for this site\'s own address.',
  }
}

// Every route is `${base}/api/...`: a query, a fragment or credentials would end up mid-URL.
function isApiBase(value: string): boolean {
  if (!/^https?:\/\//i.test(value)) return false
  try {
    const url = new URL(value)
    return !url.search && !url.hash && !url.username && !url.password
  } catch {
    return false
  }
}

/** Plain text, no translations, no API: this runs exactly when neither can be relied on. */
export function showConfigError(root: HTMLElement, message: string): void {
  const text = document.createElement('p')
  text.style.cssText = 'padding:2rem;font:16px system-ui'
  text.textContent = message
  root.replaceChildren(text)
}

export const runtimeConfig = parseRuntimeConfig(window.SCOTTY_CONFIG)
export const configuredApiBase = 'apiBase' in runtimeConfig ? runtimeConfig.apiBase : ''
```

Note : `new URL('https://api.example.net/?')` a un `search` vide ; ce cas limite passe et donne `https://api.example.net/?` sans barre retirée. Le test `?x=1` couvre le cas réel ; ne pas compliquer davantage.

`src/frontend/src/api.ts:43` — remplacer la ligne par :

```ts
const BASE: string = configuredApiBase
```

et ajouter `import { configuredApiBase } from './lib/runtimeConfig'` au bloc d'imports (ordre alphabétique des chemins, après `./lib/appLogo`).

`src/frontend/src/lib/appLogo.ts:14` — idem :

```ts
const BASE: string = configuredApiBase
```

avec `import { configuredApiBase } from './runtimeConfig'`.

`src/frontend/src/main.tsx` — remplacer le bloc final (de `// Awaited before the first render` à la fin) par :

```tsx
const root = document.getElementById('root')!

// Awaited before the first render, or the app paints its own keys; `.then`, not top-level await,
// so the bundle needs no TLA target. The `.catch` covers a hashed chunk failing to load (a
// redeploy under an open tab), which would otherwise leave every route a blank document.
if ('error' in runtimeConfig) showConfigError(root, runtimeConfig.error)
else void initI18n(resolveLocale(undefined, readLanguageMirror(), navigator.languages))
  .then(() => {
    createRoot(root).render(
      <StrictMode>
        <App />
      </StrictMode>,
    )
  })
  .catch(() => {
    root.innerHTML =
      '<p style="padding:2rem;font:16px system-ui">Something went wrong loading the app. ' +
      '<button onclick="location.reload()">Reload</button></p>'
  })
```

et ajouter `import { runtimeConfig, showConfigError } from './lib/runtimeConfig'` après l'import de `./lib/locale`.

`src/frontend/src/vite-env.d.ts` — remplacer le bloc `interface ImportMetaEnv { … }` par :

```ts
interface Window {
  /** Set by /config.js, which index.html loads before the app's module. */
  SCOTTY_CONFIG?: { apiBase?: unknown }
}
```

`src/frontend/src/test-runtime-config.ts` :

```ts
// Imported first by test-setup.ts: api.ts reads this as soon as anything imports it.
window.SCOTTY_CONFIG = { apiBase: 'https://api.example.test' }
```

`src/frontend/src/test-setup.ts` — nouvelle première ligne, avant `import './test-polyfills'` :

```ts
import './test-runtime-config'
```

`src/frontend/vite.config.js` — dans `test`, supprimer les deux lignes :

```js
      // Reserved .test host; tests assert against the exported API_BASE, never this literal.
      env: { VITE_API_BASE: 'https://api.example.test' },
```

`src/frontend/src/api.test.ts:708-710` — dans le commentaire, remplacer la mention de `test.env` / `import.meta.env` par `test-runtime-config.ts`. Ne toucher à aucune assertion.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd src/frontend && npx vitest run src/lib/runtimeConfig.test.ts src/main.test.tsx src/api.test.ts src/lib/appLogo.test.ts src/modules/mail/compose`
Expected: PASS.

- [ ] **Step 5: Run the whole front suite, typecheck and lint**

Run: `cd src/frontend && npm run typecheck && npm run lint && npm test`
Expected: tout vert, aucune attente de test modifiée (le seul changement dans `api.test.ts` est le commentaire).

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src/lib/runtimeConfig.ts src/frontend/src/lib/runtimeConfig.test.ts \
  src/frontend/src/main.tsx src/frontend/src/main.test.tsx src/frontend/src/api.ts \
  src/frontend/src/lib/appLogo.ts src/frontend/src/vite-env.d.ts src/frontend/src/test-setup.ts \
  src/frontend/src/test-runtime-config.ts src/frontend/vite.config.js src/frontend/src/api.test.ts
git commit -F - <<'EOF'
Read the API address from window.SCOTTY_CONFIG, with a plain error screen when it is wrong

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 2: `config.js` émis au build, servi en développement

**Files:**
- Create: `src/frontend/src/lib/runtimeConfigPlugin.ts`
- Modify: `src/frontend/vite.config.js:1-10` (import) et `:155-166` (garde-fou, plugins)
- Modify: `src/frontend/index.html`
- Test: `src/frontend/src/lib/runtimeConfigPlugin.test.ts` (create)

**Interfaces:**
- Consumes: le format `window.SCOTTY_CONFIG = { apiBase }` lu par la tâche 1.
- Produces (`src/lib/runtimeConfigPlugin.ts`) :
  - `export const CONTAINER_MODE = 'container'`
  - `export function configScript(apiBase: string): string`
  - `export function assertApiBase(command: string, mode: string, apiBase: string | undefined): void`
  - `export function runtimeConfigPlugin(mode: string, apiBase: string | undefined): Plugin`

- [ ] **Step 1: Write the failing tests**

`src/frontend/src/lib/runtimeConfigPlugin.test.ts` :

```ts
import { describe, expect, it, vi } from 'vitest'
import { assertApiBase, configScript, runtimeConfigPlugin } from './runtimeConfigPlugin'

function evaluate(script: string): unknown {
  const target: { SCOTTY_CONFIG?: unknown } = {}
  new Function('window', script)(target)
  return target.SCOTTY_CONFIG
}

function emitted(mode: string, apiBase = 'https://api.example.net') {
  const emitFile = vi.fn()
  const hook = runtimeConfigPlugin(mode, apiBase).generateBundle as unknown as (this: unknown) => void
  hook.call({ emitFile })
  return emitFile.mock.calls.map(([file]) => file as { fileName: string; source: string })
}

describe('configScript', () => {
  it('writes the address the front reads back', () => {
    expect(evaluate(configScript('https://api.example.net'))).toEqual({ apiBase: 'https://api.example.net' })
  })

  it('cannot be broken out of by quotes in the address', () => {
    const hostile = 'https://x"}; window.hacked = 1; //'
    const target: Record<string, unknown> = {}
    new Function('window', configScript(hostile))(target)

    expect(target).toEqual({ SCOTTY_CONFIG: { apiBase: hostile } })
  })
})

describe('runtimeConfigPlugin', () => {
  it.each([['production'], ['dev']])('emits config.js in a %s build', mode => {
    const files = emitted(mode)

    expect(files).toHaveLength(1)
    expect(files[0]!.fileName).toBe('config.js')
    expect(evaluate(files[0]!.source)).toEqual({ apiBase: 'https://api.example.net' })
  })

  it('emits nothing in a container build: the container writes it on start', () => {
    expect(emitted('container', '')).toEqual([])
  })

  it('serves config.js, uncached, from the dev server', () => {
    const use = vi.fn()
    const hook = runtimeConfigPlugin('development', 'https://api.example.net').configureServer as unknown as
      (server: unknown) => void
    hook({ middlewares: { use } })

    const [path, handler] = use.mock.calls[0]!
    const response = { setHeader: vi.fn(), end: vi.fn() }
    handler({}, response)

    expect(path).toBe('/config.js')
    expect(response.setHeader).toHaveBeenCalledWith('Cache-Control', 'no-cache')
    expect(evaluate(response.end.mock.calls[0]![0] as string)).toEqual({ apiBase: 'https://api.example.net' })
  })
})

describe('assertApiBase', () => {
  it.each([[undefined], [''], ['   ']])('stops a production build when VITE_API_BASE is %j', value => {
    expect(() => assertApiBase('build', 'production', value)).toThrow(/VITE_API_BASE is not set/)
  })

  it('lets a container build through without an address', () => {
    expect(() => assertApiBase('build', 'container', undefined)).not.toThrow()
  })

  it('lets the dev server start without an address', () => {
    expect(() => assertApiBase('serve', 'development', undefined)).not.toThrow()
  })
})
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd src/frontend && npx vitest run src/lib/runtimeConfigPlugin.test.ts`
Expected: FAIL — `Failed to resolve import "./runtimeConfigPlugin"`.

- [ ] **Step 3: Implement**

`src/frontend/src/lib/runtimeConfigPlugin.ts` :

```ts
import type { Plugin } from 'vite'

/** The one build that ships no config.js: the container writes it on start, from its environment. */
export const CONTAINER_MODE = 'container'
const CONFIG_FILE = 'config.js'

/** JSON.stringify, so no address can close the string and run code of its own. */
export function configScript(apiBase: string): string {
  return `window.SCOTTY_CONFIG = { apiBase: ${JSON.stringify(apiBase)} }\n`
}

/** No built-in default: a forgotten .env.production would ship a build posting credentials to
    someone else's API. A blank value is trimmed so it does not count as set. */
export function assertApiBase(command: string, mode: string, apiBase: string | undefined): void {
  if (command === 'build' && mode !== CONTAINER_MODE && !apiBase?.trim()) {
    throw new Error(`VITE_API_BASE is not set. Write it to .env.${mode} before building (install/README.md, step 1.2).`)
  }
}

export function runtimeConfigPlugin(mode: string, apiBase: string | undefined): Plugin {
  const script = configScript(apiBase?.trim() ?? '')
  return {
    name: 'runtime-config',
    configureServer(server) {
      server.middlewares.use(`/${CONFIG_FILE}`, (_request, response) => {
        response.setHeader('Content-Type', 'text/javascript; charset=utf-8')
        response.setHeader('Cache-Control', 'no-cache')
        response.end(script)
      })
    },
    generateBundle() {
      if (mode !== CONTAINER_MODE) this.emitFile({ type: 'asset', fileName: CONFIG_FILE, source: script })
    },
  }
}
```

`src/frontend/vite.config.js` :
- ajouter `import { assertApiBase, runtimeConfigPlugin } from './src/lib/runtimeConfigPlugin.ts'` après l'import de `versionStamp` ;
- remplacer le bloc du garde-fou (le commentaire `// No built-in default…` et le `if (command === 'build' && …) { throw … }`) par :

```js
  // Both read envDir (this folder, whatever the cwd).
  const envDir = fileURLToPath(new URL('.', import.meta.url))
  const apiBase = loadEnv(mode, envDir, 'VITE_').VITE_API_BASE
  assertApiBase(command, mode, apiBase)
```

- remplacer `plugins: [react()],` par `plugins: [react(), runtimeConfigPlugin(mode, apiBase)],`.

`src/frontend/index.html` — dernière ligne du `<head>`, juste avant `</head>` :

```html
    <script src="/config.js"></script>
```

Une balise classique (sans `type="module"`) s'exécute pendant l'analyse de la page, donc toujours avant le module de l'application, qui est différé.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd src/frontend && npx vitest run src/lib/runtimeConfigPlugin.test.ts`
Expected: PASS.

- [ ] **Step 5: Verify the three builds for real**

Run (PowerShell, depuis `src/frontend`) :

```powershell
npm run build -- --mode dev
Get-Content dist/config.js
Select-String -Path dist/index.html -Pattern 'config.js'
Remove-Item dist/config.js
npm run build -- --mode container
Test-Path dist/config.js
$env:VITE_API_BASE=''; Rename-Item .env.dev .env.dev.bak; npm run build -- --mode dev; Rename-Item .env.dev.bak .env.dev
```

Expected :
1. `dist/config.js` = `window.SCOTTY_CONFIG = { apiBase: "https://api-dev.mail.weesky.net" }`, et `dist/index.html` contient `<script src="/config.js"></script>` avant le `<script type="module" …>`.
2. Build `container` : réussit, `Test-Path` répond `False`.
3. Sans `.env.dev` : le build s'arrête sur `VITE_API_BASE is not set. Write it to .env.dev …`. **Remettre `.env.dev` en place même si la commande échoue.**

Un avertissement Vite du type `<script src="/config.js"> … can't be bundled without type="module"` est attendu et sans conséquence : c'est précisément ce qu'on veut (le fichier n'est pas intégré au bundle).

- [ ] **Step 6: Verify the dev server**

Run: `cd src/frontend && npm run dev -- --mode dev`, puis ouvrir `http://localhost:5173/config.js`.
Expected: le script avec l'adresse de `.env.dev`, en-tête `Cache-Control: no-cache`. Arrêter le serveur.

- [ ] **Step 7: Run the whole front suite, typecheck and lint**

Run: `cd src/frontend && npm run typecheck && npm run lint && npm test`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/frontend/src/lib/runtimeConfigPlugin.ts src/frontend/src/lib/runtimeConfigPlugin.test.ts \
  src/frontend/vite.config.js src/frontend/index.html
git commit -F - <<'EOF'
Emit config.js from VITE_API_BASE at build time, except in container mode, and serve it in dev

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 3: Dossier d'état partagé et clé de session générée

**Files:**
- Create: `src/scotty.microservice/Configuration/StateDirectory.cs`
- Create: `src/scotty.microservice/Configuration/SessionSigningKey.cs`
- Modify: `src/scotty.microservice/Configuration/SecurityConfiguration.cs:210-240` (`AddCredentialKeyRing`)
- Modify: `src/scotty.microservice/Authentication/Extensions/AuthorizationExtension.cs:38` (commentaire : la longueur peut venir du fichier)
- Modify: `src/scotty.microservice.host/Program.cs`
- Test: `src/scotty.microservice/scotty.microservice.Tests/Configuration/StateDirectoryTests.cs` (create)
- Test: `src/scotty.microservice/scotty.microservice.Tests/Configuration/SessionSigningKeyTests.cs` (create)

**Interfaces:**
- Produces :
  - `internal static class StateDirectory` (namespace `weesky.Scotty.Microservice.Configuration`)
    - `public static string Resolve(IHostEnvironment environment)`
    - `internal static string Resolve(IHostEnvironment environment, string? variable)`
  - `internal static class SessionSigningKey`
    - `public const string FileName = "session-signing.key";`
    - `public static (string Key, string? GeneratedIn) Resolve(string? configured, string stateDirectory)`
  - `AddCredentialKeyRing(this IServiceCollection services, IWebHostEnvironment environment, string stateDirectory)` → `string` (chemin du trousseau, inchangé dans son rôle)

- [ ] **Step 1: Write the failing tests**

`src/scotty.microservice/scotty.microservice.Tests/Configuration/StateDirectoryTests.cs` :

```csharp
using Microsoft.Extensions.Hosting;
using Moq;
using weesky.Scotty.Microservice.Configuration;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Configuration;

public sealed class StateDirectoryTests
{
    private static IHostEnvironment Environment(string name)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(name);
        environment.SetupGet(e => e.ContentRootPath).Returns(Path.Combine("srv", "app"));
        return environment.Object;
    }

    [Fact]
    public void Resolve_TakesTheFirstDirectorySystemdPasses()
    {
        Assert.Equal("/var/lib/scotty.microservice",
            StateDirectory.Resolve(Environment(Environments.Production), "/var/lib/scotty.microservice:/var/lib/other"));
    }

    [Fact]
    public void Resolve_FallsBackToTheIgnoredKeysFolderInDevelopment()
    {
        Assert.Equal(Path.Combine("srv", "app", "keys"),
            StateDirectory.Resolve(Environment(Environments.Development), null));
    }

    [Fact]
    public void Resolve_RefusesToStartOutsideDevelopmentWithoutStateDirectory()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => StateDirectory.Resolve(Environment(Environments.Production), ""));

        Assert.Contains("STATE_DIRECTORY is not set", error.Message);
    }
}
```

`src/scotty.microservice/scotty.microservice.Tests/Configuration/SessionSigningKeyTests.cs` :

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using weesky.Scotty.Microservice.Authentication.Models;
using weesky.Scotty.Microservice.Configuration;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Configuration;

public sealed class SessionSigningKeyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"scotty-state-{Guid.NewGuid():N}");
    private string KeyFile => Path.Combine(_directory, SessionSigningKey.FileName);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Resolve_UsesTheConfiguredKeyAndWritesNothing()
    {
        var configured = new string('k', 64);

        Assert.Equal((configured, (string?)null), SessionSigningKey.Resolve(configured, _directory));
        Assert.False(File.Exists(KeyFile));
    }

    [Fact]
    public void Resolve_GeneratesSixtyFourRandomBytesWhenNoKeyIsConfigured()
    {
        var (key, generatedIn) = SessionSigningKey.Resolve("", _directory);

        Assert.Equal(KeyFile, generatedIn);
        Assert.Equal(64, Convert.FromBase64String(key).Length);
        Assert.Equal(key, File.ReadAllText(KeyFile));
    }

    [Fact]
    public void Resolve_ReadsTheSameKeyBackOnTheNextStart()
    {
        var (first, _) = SessionSigningKey.Resolve(null, _directory);

        var (second, generatedIn) = SessionSigningKey.Resolve(null, _directory);

        Assert.Equal(first, second);
        Assert.Null(generatedIn);
    }

    [Fact]
    public void Resolve_NeverRewritesAnExistingFile()
    {
        Directory.CreateDirectory(_directory);
        var existing = new string('e', 48);
        File.WriteAllText(KeyFile, existing + "\n");

        Assert.Equal((existing, (string?)null), SessionSigningKey.Resolve(null, _directory));
        Assert.Equal(existing + "\n", File.ReadAllText(KeyFile));
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    public void Resolve_RefusesToStartOnAnUnusableFileAndLeavesItAlone(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(KeyFile, content);

        var error = Assert.Throws<InvalidOperationException>(() => SessionSigningKey.Resolve(null, _directory));

        Assert.Contains(KeyFile, error.Message);
        Assert.Equal(content, File.ReadAllText(KeyFile));
    }

    [Fact]
    public void Resolve_RefusesToStartOnAnUnreadableFile()
    {
        Directory.CreateDirectory(KeyFile);   // a directory where the file should be: unreadable on every OS

        var error = Assert.Throws<InvalidOperationException>(() => SessionSigningKey.Resolve(null, _directory));

        Assert.Contains(KeyFile, error.Message);
    }

    [Fact]
    public async Task Resolve_GivesConcurrentStartsOneKeyAndLeavesNoTemporaryFile()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => SessionSigningKey.Resolve(null, _directory))));

        Assert.Single(results.Select(r => r.Key).Distinct());
        Assert.Single(results, r => r.GeneratedIn is not null);
        Assert.Equal(new[] { KeyFile }, Directory.GetFiles(_directory));
    }

    [Fact]
    public void Resolve_CreatesTheFileReadableByTheServiceUserOnly()
    {
        if (OperatingSystem.IsWindows()) return;   // Unix permissions; CI runs this on Linux

        SessionSigningKey.Resolve(null, _directory);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(KeyFile));
    }

    [Fact]
    public void AGeneratedKeyPassesTheStartupValidation()
    {
        var (key, _) = SessionSigningKey.Resolve(null, _directory);

        Assert.Equal(key, Compose(configuredKey: "", resolvedKey: key).Value.Key);
    }

    [Fact]
    public void AShortConfiguredKeyIsStillRefused()
    {
        var (key, _) = SessionSigningKey.Resolve("short", _directory);

        Assert.Throws<OptionsValidationException>(() => Compose(configuredKey: "short", resolvedKey: key).Value);
    }

    // The composition Program.cs runs: bound from configuration, then the resolved key, then validated.
    private static IOptions<TokenConstants> Compose(string configuredKey, string resolvedKey)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TokenConstants:Key"] = configuredKey })
            .Build();
        var provider = new ServiceCollection()
            .AddScottyOptions(configuration)
            .PostConfigure<TokenConstants>(constants => constants.Key = resolvedKey)
            .BuildServiceProvider();
        return provider.GetRequiredService<IOptions<TokenConstants>>();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter "FullyQualifiedName~StateDirectoryTests|FullyQualifiedName~SessionSigningKeyTests"`
Expected: FAIL à la compilation — `StateDirectory` et `SessionSigningKey` n'existent pas.

- [ ] **Step 3: Implement**

`src/scotty.microservice/Configuration/StateDirectory.cs` :

```csharp
namespace weesky.Scotty.Microservice.Configuration;

/// <summary>
/// Where the service keeps what must survive a restart: the Data Protection key ring and the
/// session signing key. systemd's StateDirectory= provides it outside the deployment path — which
/// the release chmod/chown walk recursively — and owned by the service user.
/// </summary>
internal static class StateDirectory
{
    public static string Resolve(IHostEnvironment environment) =>
        Resolve(environment, Environment.GetEnvironmentVariable("STATE_DIRECTORY"));

    internal static string Resolve(IHostEnvironment environment, string? variable)
    {
        var stateDirectory = variable?.Split(':')[0];
        if (!string.IsNullOrEmpty(stateDirectory)) return stateDirectory;

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "STATE_DIRECTORY is not set. Add 'StateDirectory=scotty.microservice' to the systemd unit. " +
                "Refusing to start rather than falling back to a key ring under the deployment directory.");
        }

        // Under keys/, which git ignores: the session key cannot end up in a commit.
        return Path.Combine(environment.ContentRootPath, "keys");
    }
}
```

`src/scotty.microservice/Configuration/SessionSigningKey.cs` :

```csharp
using System.Security.Cryptography;
using System.Text;
using weesky.Scotty.Microservice.Authentication.Extensions;

namespace weesky.Scotty.Microservice.Configuration;

/// <summary>
/// The key that signs session tokens when TokenConstants:Key gives none: generated once and kept
/// in the state directory, so sessions survive a restart. A file that is there but unusable stops
/// the start — generating over it would sign everyone out with nothing saying why.
/// </summary>
internal static class SessionSigningKey
{
    public const string FileName = "session-signing.key";
    private const int GeneratedBytes = 64;

    /// <returns>The key, and the file's path when this call created it, for the startup log.</returns>
    public static (string Key, string? GeneratedIn) Resolve(string? configured, string stateDirectory)
    {
        if (!string.IsNullOrEmpty(configured)) return (configured, null);

        Directory.CreateDirectory(stateDirectory);
        var path = Path.Combine(stateDirectory, FileName);
        var created = TryCreate(path);
        return (Read(path), created ? path : null);
    }

    // Written aside, then moved in without overwriting: a concurrent start either wins the move or
    // reads the winner's complete file, never a half-written one.
    private static bool TryCreate(string path)
    {
        if (Path.Exists(path)) return false;

        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var writer = new StreamWriter(temporary, Encoding.ASCII, options))
                writer.Write(Convert.ToBase64String(RandomNumberGenerator.GetBytes(GeneratedBytes)));

            File.Move(temporary, path, overwrite: false);
            return true;
        }
        catch (IOException) when (Path.Exists(path))
        {
            return false;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static string Read(string path)
    {
        string key;
        try
        {
            key = File.ReadAllText(path).Trim();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"The session signing key {path} cannot be read: {e.Message} " +
                "Give the service user read access to it.", e);
        }

        if (Encoding.UTF8.GetByteCount(key) < AuthorizationExtension.MinimumSigningKeyBytes)
        {
            throw new InvalidOperationException(
                $"The session signing key {path} is empty or shorter than " +
                $"{AuthorizationExtension.MinimumSigningKeyBytes} bytes. Restore it from a backup, or delete " +
                "it to have a new one generated — which signs every user out.");
        }

        return key;
    }
}
```

Si `IHostEnvironment` / `IsDevelopment` ne compilent pas faute d'using global, ajouter `using Microsoft.Extensions.Hosting;` en tête de `StateDirectory.cs`.

`SecurityConfiguration.AddCredentialKeyRing` — remplacer la signature et le corps jusqu'à `Directory.CreateDirectory(keyRingPath);` par :

```csharp
    public static string AddCredentialKeyRing(
        this IServiceCollection services, IWebHostEnvironment environment, string stateDirectory)
    {
        var keyRingPath = Path.Combine(stateDirectory, "keys");

        Directory.CreateDirectory(keyRingPath);
```

Le reste (`AddDataProtection()…`, `return keyRingPath;`) ne change pas. Dans le `<summary>` de la méthode, supprimer la phrase sur systemd (elle est maintenant sur `StateDirectory`) et garder celle sur la survie aux redémarrages. Dans `SecurityConfiguration.cs:81` et dans les messages qui citent « AddCredentialKeyRing refuses without STATE_DIRECTORY », remplacer par `StateDirectory.Resolve`.

`AuthorizationExtension.cs:38` — le commentaire devient : `// Length is guaranteed by AddScottyOptions, which refuses to start on a short key — configured or read from its file.`

`src/scotty.microservice.host/Program.cs` :
- ajouter `using weesky.Scotty.Microservice.Authentication.Models;` ;
- juste après `var isWeesky = …;`, ajouter :

```csharp
var stateDirectory = StateDirectory.Resolve(builder.Environment);
var sessionKey = SessionSigningKey.Resolve(builder.Configuration["TokenConstants:Key"], stateDirectory);
```

- dans la chaîne `builder.Services`, juste après `.AddScottyOptions(builder.Configuration)`, ajouter :

```csharp
    .PostConfigure<TokenConstants>(constants => constants.Key = sessionKey.Key)
```

- remplacer `var keyRingPath = builder.Services.AddCredentialKeyRing(builder.Environment);` par `var keyRingPath = builder.Services.AddCredentialKeyRing(builder.Environment, stateDirectory);` ;
- après la ligne `app.Logger.LogInformation("Data Protection key ring: …")`, ajouter :

```csharp
if (sessionKey.GeneratedIn is not null)
    app.Logger.LogInformation("New session signing key generated in {Path}", sessionKey.GeneratedIn);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter "FullyQualifiedName~StateDirectoryTests|FullyQualifiedName~SessionSigningKeyTests"`
Expected: PASS (sur Windows, le test des droits Unix passe sans rien vérifier ; la CI Linux le vérifie).

- [ ] **Step 5: Verify a real start without a key**

Run (PowerShell, depuis la racine) :

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'; $env:TokenConstants__Key=''
dotnet run --project src/scotty.microservice.host
```

Expected : le journal affiche `New session signing key generated in …\src\scotty.microservice.host\keys\session-signing.key` puis `Data Protection key ring: …\keys\keys`. Arrêter, relancer : plus de ligne « New session signing key ». `git status` ne montre aucun fichier sous `keys/`. Si le démarrage échoue plus loin pour une autre raison (base de données locale absente), ces deux lignes suffisent : elles sont écrites avant ; sinon, s'appuyer sur les tests.

Note : le trousseau local passe de `keys/` à `keys/keys/` ; une reconnexion est demandée une fois en local. C'est prévu par la spec.

- [ ] **Step 6: Run the whole backend suite**

Run: `dotnet test src/scotty.microservice.sln`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git checkout -- '**/ApiDocumentation.xml' 2>/dev/null; git status --short
git add src/scotty.microservice/Configuration/StateDirectory.cs src/scotty.microservice/Configuration/SessionSigningKey.cs \
  src/scotty.microservice/Configuration/SecurityConfiguration.cs \
  src/scotty.microservice/Authentication/Extensions/AuthorizationExtension.cs src/scotty.microservice.host/Program.cs \
  src/scotty.microservice/scotty.microservice.Tests/Configuration/StateDirectoryTests.cs \
  src/scotty.microservice/scotty.microservice.Tests/Configuration/SessionSigningKeyTests.cs
git commit -F - <<'EOF'
Generate the session signing key in the state directory when TokenConstants:Key is empty

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 4: `Logs:Output` choisit fichiers ou console

**Files:**
- Modify: `src/scotty.microservice/Configuration/LoggingConfiguration.cs:12-55`
- Modify: `src/scotty.microservice.host/Program.cs:8`
- Test: `src/scotty.microservice/scotty.microservice.Tests/Configuration/LoggingConfigurationTests.cs` (create)

**Interfaces:**
- Produces :
  - `internal enum LogOutput { File, Console }` (dans `LoggingConfiguration.cs`)
  - `public static IHostBuilder UseScottyLogging(this IHostBuilder host, IConfiguration configuration)`
  - `internal static LogOutput ParseOutput(string? value)`
  - `internal static LoggerConfiguration Configure(LoggerConfiguration logger, LogOutput output, string logDirectory, string logPrefix)`

- [ ] **Step 1: Write the failing tests**

`src/scotty.microservice/scotty.microservice.Tests/Configuration/LoggingConfigurationTests.cs` :

```csharp
using Serilog;
using Serilog.Core;
using weesky.Scotty.Microservice.Configuration;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Configuration;

// Console.SetOut is process-wide: kept out of parallel runs with anything else that writes there.
[CollectionDefinition(nameof(LoggingConfigurationTests), DisableParallelization = true)]
public sealed class LoggingConsoleCollection;

[Collection(nameof(LoggingConfigurationTests))]
public sealed class LoggingConfigurationTests : IDisposable
{
    private const string RequestSource = "Serilog.AspNetCore.RequestLoggingMiddleware";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"scotty-logs-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Theory]
    [InlineData(null, LogOutput.File)]
    [InlineData("", LogOutput.File)]
    [InlineData("file", LogOutput.File)]
    [InlineData("console", LogOutput.Console)]
    [InlineData("Console", LogOutput.Console)]
    [InlineData("FILE", LogOutput.File)]
    public void ParseOutput_ReadsTheSetting(string? value, LogOutput expected)
    {
        Assert.Equal(expected, LoggingConfiguration.ParseOutput(value));
    }

    [Fact]
    public void ParseOutput_RefusesToStartOnAnUnknownValue()
    {
        var error = Assert.Throws<InvalidOperationException>(() => LoggingConfiguration.ParseOutput("stdout"));

        Assert.Contains("Logs:Output is \"stdout\"", error.Message);
        Assert.Contains("file, console", error.Message);
    }

    [Fact]
    public void Console_WritesBothStreamsWithTheSameFiltersAndCreatesNoFolder()
    {
        var original = Console.Out;
        var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            using (var logger = LoggingConfiguration
                       .Configure(new LoggerConfiguration(), LogOutput.Console, _directory, "").CreateLogger())
                Write(logger);
        }
        finally
        {
            Console.SetOut(original);
        }

        var text = output.ToString();
        Assert.Contains("HTTP GET /api/Mail from 203.0.113.7", text);
        Assert.Contains("Mailbox opened", text);
        Assert.DoesNotContain("Kestrel chatter", text);
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void File_KeepsRequestsApartFromTheRest()
    {
        using (var logger = LoggingConfiguration
                   .Configure(new LoggerConfiguration(), LogOutput.File, _directory, "").CreateLogger())
            Write(logger);

        var http = File.ReadAllText(Assert.Single(Directory.GetFiles(_directory, "log-http-*.log")));
        var rest = File.ReadAllText(Assert.Single(Directory.GetFiles(_directory, "log-2*.log")));
        Assert.Contains("from 203.0.113.7", http);
        Assert.DoesNotContain("Mailbox opened", http);
        Assert.Contains("Mailbox opened", rest);
        Assert.DoesNotContain("Kestrel chatter", rest);
    }

    private static void Write(Logger logger)
    {
        logger.ForContext(Constants.SourceContextPropertyName, RequestSource)
            .Information("HTTP {RequestMethod} {RequestPath} from {ClientIp}", "GET", "/api/Mail", "203.0.113.7");
        logger.ForContext(Constants.SourceContextPropertyName, "weesky.Scotty.Mail").Information("Mailbox opened");
        logger.ForContext(Constants.SourceContextPropertyName, "Microsoft.AspNetCore.Server.Kestrel")
            .Information("Kestrel chatter");
    }
}
```

Note : `log-2*.log` sélectionne le fichier `log-<date>.log` sans attraper `log-http-<date>.log` (la date commence par `2`).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter "FullyQualifiedName~LoggingConfigurationTests"`
Expected: FAIL à la compilation — `LogOutput`, `ParseOutput`, `Configure` n'existent pas.

- [ ] **Step 3: Implement**

Dans `LoggingConfiguration.cs`, remplacer `UseScottyLogging` (résumé compris) par :

```csharp
    // The file sink's own default, kept for the console so a line reads the same wherever it lands.
    private const string OutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    public static IHostBuilder UseScottyLogging(this IHostBuilder host, IConfiguration configuration)
    {
        var output = ParseOutput(configuration["Logs:Output"]);
        var logDirectory = OperatingSystem.IsWindows()
            ? Path.Combine(AppContext.BaseDirectory, "logs")
            : "/var/log/scotty.microservice";

        return host.UseSerilog((ctx, cfg) => Configure(cfg, output, logDirectory,
            ctx.HostingEnvironment.IsProduction() ? "" : $"{ctx.HostingEnvironment.EnvironmentName.ToLowerInvariant()}-"));
    }

    internal static LogOutput ParseOutput(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "file" => LogOutput.File,
        "console" => LogOutput.Console,
        _ => throw new InvalidOperationException($"Logs:Output is \"{value}\"; possible values: file, console."),
    };

    /// <summary>
    /// Information, with two categories turned down that must stay down: Microsoft.AspNetCore, and
    /// EF Core's Database.Command, which logs every statement at Information and buried the log under
    /// SQL (Warning still surfaces its failures). Files: two daily ones, HTTP requests apart from the
    /// rest, so neither buries the other. Console: one stream, for a container's log collector.
    /// </summary>
    internal static LoggerConfiguration Configure(
        LoggerConfiguration logger, LogOutput output, string logDirectory, string logPrefix)
    {
        logger
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            .Enrich.FromLogContext();

        if (output == LogOutput.Console) return logger.WriteTo.Console(outputTemplate: OutputTemplate, theme: ConsoleTheme.None);

        Directory.CreateDirectory(logDirectory);
        return logger
            .WriteTo.Logger(l => l
                .Filter.ByIncludingOnly(Matching.FromSource(RequestLoggerSource))
                .WriteTo.File(
                    Path.Combine(logDirectory, $"log-{logPrefix}http-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 31,
                    shared: true))
            .WriteTo.Logger(l => l
                .Filter.ByExcluding(Matching.FromSource(RequestLoggerSource))
                .WriteTo.File(
                    Path.Combine(logDirectory, $"log-{logPrefix}.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 31,
                    shared: true));
    }
```

et, après la classe (même fichier, même namespace) :

```csharp
internal enum LogOutput { File, Console }
```

Ajouter `using Serilog.Sinks.SystemConsole.Themes;` : sans couleurs, la ligne reste lisible par un collecteur de logs et par le test. Le sink console est fourni par `Serilog.AspNetCore` (dépendance `Serilog.Sinks.Console`) : aucun paquet à ajouter. Si la compilation dit le contraire, ajouter `Serilog.Sinks.Console` à `scotty.microservice.core.csproj` à la version que `dotnet list package --include-transitive` montre déjà.

`Program.cs:8` : `builder.Host.UseScottyLogging(builder.Configuration);`

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter "FullyQualifiedName~LoggingConfigurationTests"`
Expected: PASS.

- [ ] **Step 5: Run the whole backend suite**

Run: `dotnet test src/scotty.microservice.sln`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git checkout -- '**/ApiDocumentation.xml' 2>/dev/null; git status --short
git add src/scotty.microservice/Configuration/LoggingConfiguration.cs src/scotty.microservice.host/Program.cs \
  src/scotty.microservice/scotty.microservice.Tests/Configuration/LoggingConfigurationTests.cs
git commit -F - <<'EOF'
Let Logs:Output send the log to the console instead of the two files

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 5: `ForwardedHeaders:KnownNetworks` accepte des plages

**Files:**
- Modify: `src/scotty.microservice/Configuration/SecurityConfiguration.cs:110-160` (`AddProxyForwardedHeaders` et son résumé)
- Test: `src/scotty.microservice/scotty.microservice.Tests/Configuration/ForwardedHeadersConfigurationTests.cs`

**Interfaces:**
- Consumes: rien des tâches précédentes.
- Produces: réglage `ForwardedHeaders:KnownNetworks:<n>` (variable `ForwardedHeaders__KnownNetworks__0`), lu par la tâche 6 dans la documentation.

- [ ] **Step 1: Write the failing tests**

Dans `ForwardedHeadersConfigurationTests.cs`, ajouter après `Configuration(...)` :

```csharp
    private static IConfiguration Networks(params string[] knownNetworks)
    {
        var values = new Dictionary<string, string?>();
        for (var i = 0; i < knownNetworks.Length; i++)
            values[$"ForwardedHeaders:KnownNetworks:{i}"] = knownNetworks[i];

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
```

Dans `AddProxyForwardedHeaders_RefusesToStartOutsideDevelopmentWithNoProxyNamed`, ajouter :

```csharp
        Assert.Contains("ForwardedHeaders__KnownNetworks__0", error.Message);
```

Puis ajouter ces tests à la classe :

```csharp
    /// <summary>
    /// A container's proxy gets a new address on every restart, inside its network's range: naming the
    /// range is the only way to trust it without editing the settings each time.
    /// </summary>
    [Fact]
    public void AddProxyForwardedHeaders_TrustsTheConfiguredRanges()
    {
        var options = Build(Networks("172.16.0.0/12", "fd00::/8"), Environments.Production);

        Assert.Contains(System.Net.IPNetwork.Parse("172.16.0.0/12"), options.KnownIPNetworks);
        Assert.Contains(System.Net.IPNetwork.Parse("fd00::/8"), options.KnownIPNetworks);
        Assert.Empty(options.KnownProxies);
        Assert.Equal(1, options.ForwardLimit);
    }

    [Theory]
    [InlineData("172.16.0.0")]
    [InlineData("172.16.0.0/33")]
    [InlineData("not-a-range")]
    [InlineData("10.1.2.3/8")]
    public void AddProxyForwardedHeaders_RefusesToStartOnAMalformedRange(string range)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Build(Networks(range), Environments.Production));

        Assert.Contains($"'{range}'", error.Message);
    }

    /// <summary>
    /// A zero-length range trusts every address: any caller could then pick the address the login
    /// limiter counts, which is the limiter switched off.
    /// </summary>
    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    public void AddProxyForwardedHeaders_RefusesARangeThatTrustsEveryone(string range)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Build(Networks(range), Environments.Production));

        Assert.Contains($"'{range}'", error.Message);
        Assert.Contains("every address", error.Message);
    }

    [Fact]
    public void AddProxyForwardedHeaders_RefusesToStartOnAMalformedProxy()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Build(Configuration("proxy.local"), Environments.Production));

        Assert.Contains("'proxy.local'", error.Message);
    }
```

Le dernier test vérifie que la validation des IP exactes, déplacée au démarrage, refuse toujours une valeur invalide.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter "FullyQualifiedName~ForwardedHeadersConfigurationTests"`
Expected: FAIL — `TrustsTheConfiguredRanges` (refus « No reverse proxy is configured »), les deux théories sur les plages (aucune exception) et l'assertion `KnownNetworks__0`.

- [ ] **Step 3: Implement**

Remplacer le corps de `AddProxyForwardedHeaders` par :

```csharp
    {
        var knownProxies = (configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
            .Select(ParseProxy).ToList();
        var knownNetworks = (configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
            .Select(ParseNetwork).ToList();

        // A proxy the configuration does not name makes the middleware drop the header silently:
        // the address stays the proxy's, the limiter goes back to one global bucket, and nothing
        // says so. Refusing to start names the cause instead.
        if (knownProxies.Count == 0 && knownNetworks.Count == 0 && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "No reverse proxy is configured. Set ForwardedHeaders__KnownProxies__0 in the service's " +
                "EnvironmentFile to the address the proxy connects from — 127.0.0.1 when it runs on this " +
                "host, plus ::1 if Kestrel listens on the IPv6 loopback — or ForwardedHeaders__KnownNetworks__0 " +
                "to the range it connects from when that address changes. See install/README.md. Refusing to " +
                "start rather than rate-limiting every account against one shared bucket.");
        }

        return services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // One hop: the proxy's own entry. Accepting more would let its client prepend an
            // address of its choosing and pick which partition it lands in.
            options.ForwardLimit = 1;

            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in knownProxies) options.KnownProxies.Add(proxy);
            foreach (var network in knownNetworks) options.KnownIPNetworks.Add(network);
        });
    }

    private static IPAddress ParseProxy(string value) =>
        IPAddress.TryParse(value, out var address)
            ? address
            : throw new InvalidOperationException(
                $"ForwardedHeaders:KnownProxies holds '{value}', which is not an IP address.");

    private static System.Net.IPNetwork ParseNetwork(string value)
    {
        if (!System.Net.IPNetwork.TryParse(value, out var network))
        {
            throw new InvalidOperationException(
                $"ForwardedHeaders:KnownNetworks holds '{value}', which is not a range. Write the range's " +
                "first address and its length, such as 172.16.0.0/12 or fd00::/8.");
        }

        if (network.PrefixLength == 0)
        {
            throw new InvalidOperationException(
                $"ForwardedHeaders:KnownNetworks holds '{value}', which trusts every address: any caller " +
                "could then choose the address the login limiter counts. Name the proxy's own range.");
        }

        return network;
    }
```

`System.Net.IPNetwork` est écrit en entier : `Microsoft.AspNetCore.HttpOverrides` a aussi un `IPNetwork` (obsolète), et les deux `using` sont présents.

Dans le `<summary>` de `AddProxyForwardedHeaders`, ajouter une phrase : les plages servent quand l'adresse du proxy change (conteneur), et une plage de longueur zéro est refusée.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter "FullyQualifiedName~ForwardedHeadersConfigurationTests"`
Expected: PASS, y compris les tests existants (IP exactes seules, développement sans proxy).

- [ ] **Step 5: Run the whole backend suite**

Run: `dotnet test src/scotty.microservice.sln`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git checkout -- '**/ApiDocumentation.xml' 2>/dev/null; git status --short
git add src/scotty.microservice/Configuration/SecurityConfiguration.cs \
  src/scotty.microservice/scotty.microservice.Tests/Configuration/ForwardedHeadersConfigurationTests.cs
git commit -F - <<'EOF'
Accept proxy ranges in ForwardedHeaders:KnownNetworks, refusing malformed and zero-length ones

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 6: Guide d'installation, fichier de réglages, versions

**Files:**
- Modify: `install/README.md` (étapes 1.2, 3.3, 4.2, encadré « Web server on another machine? », tableau « When the service won't start », paragraphe « The encryption keys »)
- Modify: `install/scotty.microservice.env`
- Modify: `src/frontend/VERSION` (`2.1.0` → `2.2.0`)
- Modify: `src/scotty.microservice/VERSION` (`1.1.0` → `1.2.0`)

**Interfaces:**
- Consumes: les messages exacts des tâches 3, 4 et 5 ; le fichier `config.js` de la tâche 2.

Pas de test automatique : chaque étape se vérifie en relisant le rendu Markdown et en comparant les messages cités à ceux du code (`grep`). Le guide reste en anglais, simple, sans mention du serveur du propriétaire.

- [ ] **Step 1: Étape 1.2**

Remplacer :

```
**1.2 Build the web interface.** It has to know where the API is, and it writes that address into
the pages while building them — so set it first:
```

par :

```
**1.2 Build the web interface.** It has to know where the API is, and writes that address into
`dist/config.js` while building — so set it first:
```

et remplacer :

```
If the API's address ever changes, build the web interface again. Without this file the build
stops with an error naming `VITE_API_BASE` rather than shipping a page pointed nowhere.

✅ **Check:** both `out/api/scotty.microservice` and `src/frontend/dist/index.html` exist.
```

par :

```
If the API's address ever changes, correct it in `config.js` on the server
(`/var/www/scotty/config.js`, step 4.1): no need to build again. Without the `.env.production`
file the build stops with an error naming `VITE_API_BASE` rather than shipping a page pointed
nowhere.

✅ **Check:** `out/api/scotty.microservice`, `src/frontend/dist/index.html` and
`src/frontend/dist/config.js` exist.
```

- [ ] **Step 2: Étape 3.3 et fichier de réglages**

Tableau de l'étape 3.3 : supprimer la ligne `TokenConstants__Key`. Sous le tableau, avant « Scotty reaches IMAP on port **143** », ajouter :

```
The key that signs users' sessions needs nothing from you: on first start, the service generates
one and keeps it in `/var/lib/scotty.microservice`. To choose your own instead, add
`TokenConstants__Key=` followed by a long random value (`openssl rand -base64 48`).
```

`install/scotty.microservice.env` : supprimer le bloc

```
# --- CHANGE: a long random value that signs users' sessions --------------------------------------
# Generate one with:  openssl rand -base64 48
TokenConstants__Key=CHANGE_ME

```

et l'ajouter, réécrit, en tête de la section `# --- Optional ---` (avant `# Sync contacts and calendars…`) :

```
# The key that signs users' sessions. Left out, the service generates one on first start and keeps
# it in /var/lib/scotty.microservice/session-signing.key. To set your own:  openssl rand -base64 48
# TokenConstants__Key=

```

Mettre à jour l'en-tête du fichier : « It holds a database password and a signing key » → « It holds a database password ».

- [ ] **Step 3: Étape 4 — `config.js` jamais mis en cache, plages de proxy**

Apache, dans le `<VirtualHost>` de l'interface, après le bloc `<Directory /var/www/scotty/assets>…</Directory>` :

```apache
    <Files "config.js">
        Header set Cache-Control "no-cache"
    </Files>
```

nginx, dans le `server` de l'interface, après `location /assets/ { … }` :

```nginx
    location = /config.js {
        add_header Cache-Control "no-cache";
    }
```

Sous chacun des deux blocs de configuration (Apache : dans la phrase « Apache adds `X-Forwarded-For` by itself… » ; nginx : avant « Then: »), une phrase : « `config.js` holds the API's address: `no-cache` makes browsers pick up a corrected one on the next visit. » Une seule formulation, répétée à l'identique. `headers` fait déjà partie du `a2enmod` de l'étape 4 : rien à ajouter.

Encadré « Web server on another machine? » — ajouter à la fin :

```
> If that address changes (a container, a cluster), name its range instead:
> `ForwardedHeaders__KnownNetworks__0=10.0.0.0/24`.
```

- [ ] **Step 4: Tableau « When the service won't start » et sauvegardes**

Remplacer la ligne `TokenConstants:Key must be at least 32 bytes` par :

```
| `TokenConstants:Key must be at least 32 bytes` | `TokenConstants__Key` is too short. Remove the line to let the service generate a key, or generate one with `openssl rand -base64 48` |
```

Ajouter, après la ligne `STATE_DIRECTORY is not set` :

```
| `The session signing key … cannot be read` | Give the service access to it: `chown scotty:scotty /var/lib/scotty.microservice/session-signing.key` |
| `The session signing key … is empty or shorter than 32 bytes` | Restore the file from a backup, or delete it: a new key is generated, and everyone signs in again |
| `ForwardedHeaders:KnownNetworks holds '…'` | Write the range as its first address and a length, such as `172.16.0.0/12`. `/0` is refused: it would trust every address |
| `Logs:Output is "…"` | Remove the line, or set it to `file` or `console` |
```

Paragraphe « The encryption keys » : « **The encryption keys** in `/var/lib/scotty.microservice` need no backup. » devient « **The encryption keys and the session key** in `/var/lib/scotty.microservice` need no backup. » (la suite, « If they are lost, users just sign in again. », ne change pas).

- [ ] **Step 5: Versions**

`src/frontend/VERSION` : `2.2.0`. `src/scotty.microservice/VERSION` : `1.2.0`. Vérifier que `git tag -l 'web-v2.2.0' 'api-v1.2.0'` ne renvoie rien.

- [ ] **Step 6: Vérifier les messages cités**

Run :

```bash
grep -n "cannot be read\|is empty or shorter than\|KnownNetworks holds\|Logs:Output is\|must be at least" \
  src/scotty.microservice/Configuration/*.cs
```

Expected : chaque début de message cité dans le tableau apparaît tel quel dans le code.

- [ ] **Step 7: Commit**

```bash
git add install/README.md install/scotty.microservice.env src/frontend/VERSION src/scotty.microservice/VERSION
git commit -F - <<'EOF'
Document config.js, the generated session key and proxy ranges; bump web 2.2.0 and API 1.2.0

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```
