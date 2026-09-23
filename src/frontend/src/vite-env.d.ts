/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE: string
}

// Stamped by vite.config.js at build time.
declare const __APP_VERSION__: string
declare const __APP_COMMIT__: string | null
declare const __APP_BUILT_AT__: string
