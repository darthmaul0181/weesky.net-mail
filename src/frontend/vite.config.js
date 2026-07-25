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
    // Palette parity test parses theme-*.css files for token lists; needs real content, not Vitest's empty CSS mock.
    css: { include: [/src\/styles\/theme-.*\.css/] },
    setupFiles: ['./src/test-setup.js'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'cobertura'],
      reportsDirectory: './coverage',
    },
  },
})
