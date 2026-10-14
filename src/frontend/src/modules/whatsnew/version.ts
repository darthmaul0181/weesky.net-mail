const parts = (version: string) => version.replace(/-dev$/, '').split('.').map(Number)

/** Negative, zero or positive as `a` is older than, the same as or newer than `b`. */
export function compareVersions(a: string, b: string): number {
  const x = parts(a), y = parts(b)
  for (let i = 0; i < 3; i++) {
    const delta = (x[i] ?? 0) - (y[i] ?? 0)
    if (delta !== 0) return delta
  }
  return 0
}
