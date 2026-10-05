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
