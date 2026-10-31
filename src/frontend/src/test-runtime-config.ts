// Imported first by the test setups: api.ts reads this as soon as anything imports it.
globalThis.window ??= globalThis as typeof globalThis & Window
window.SCOTTY_CONFIG = { apiBase: 'https://api.example.test' }
