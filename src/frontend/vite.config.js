import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    allowedHosts: ['.mail.weesky.net'],
  },
  test: {
    environment: 'jsdom',
    globals: true,
    // Palette parity and the responsive contract test parse these stylesheets for their actual
    // text; Vitest mocks a CSS import to '' otherwise, which passes every check vacuously.
    css: { include: [/src\/styles\/theme-.*\.css/, /src\/styles\/shell\.css/, /src\/index\.css/] },
    setupFiles: ['./src/test-setup.js'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'cobertura'],
      reportsDirectory: './coverage',
    },
  },
})
