/// <reference types="vite/client" />

interface Window {
  /** Set by /config.js, which index.html loads before the app's module. */
  SCOTTY_CONFIG?: { apiBase?: unknown }
}

// Stamped by vite.config.js at build time.
declare const __APP_VERSION__: string
declare const __APP_COMMIT__: string | null
declare const __APP_BUILT_AT__: string
