import { describe, it, expect } from 'vitest'
import css from '../../../../styles/mail.css?raw'

function constantsOf(selector: string): Record<string, number> {
  const at = css.indexOf(`${selector} {`)
  const body = css.slice(at, css.indexOf('}', at))
  return Object.fromEntries([...body.matchAll(/(--avatar-[sl]|--avatar-bg-l|--avatar-fg-l)\s*:\s*([\d.]+)%/g)]
    .map(m => [m[1]!, Number(m[2]) / 100]))
}

function luminance(h: number, s: number, l: number): number {
  const a = s * Math.min(l, 1 - l)
  const channel = (n: number) => {
    const k = (n + h / 30) % 12
    const c = l - a * Math.max(-1, Math.min(k - 3, 9 - k, 1))
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4
  }
  return 0.2126 * channel(0) + 0.7152 * channel(8) + 0.0722 * channel(4)
}

function minContrast(selector: string): number {
  const { '--avatar-s': s, '--avatar-bg-l': bg, '--avatar-fg-l': fg } = constantsOf(selector)
  expect([s, bg, fg].every(v => v !== undefined)).toBe(true)
  let min = Infinity
  for (let hue = 0; hue < 360; hue++) {
    const [a, b] = [luminance(hue, s!, bg!), luminance(hue, s!, fg!)]
    min = Math.min(min, (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05))
  }
  return min
}

describe('sender avatar contrast', () => {
  it('reaches 4.5:1 for every hue in the light theme', () => {
    expect(minContrast('.message-row-avatar.is-sender')).toBeGreaterThanOrEqual(4.5)
  })

  it('reaches 4.5:1 for every hue in the dark theme', () => {
    expect(minContrast(`[data-theme='dark'] .message-row-avatar.is-sender`)).toBeGreaterThanOrEqual(4.5)
  })
})
