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
})
