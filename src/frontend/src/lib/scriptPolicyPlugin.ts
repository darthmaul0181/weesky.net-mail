import { createHash } from 'node:crypto'
import type { Plugin } from 'vite'

const INLINE_SCRIPT = /<script>([\s\S]*?)<\/script>/g

/** The page runs only its own files and the inline scripts it shipped with, each pinned by its
    hash: an injected script — a paste the sanitiser missed, landing in the composer's div — runs
    nowhere. Hashed as the parser reads it, line ends normalised. A meta rather than a header, so
    Apache and the Docker image carry it alike. */
export function scriptPolicy(html: string): string {
  const hashes = [...html.matchAll(INLINE_SCRIPT)].map(([, body]) => {
    const text = (body ?? '').replace(/\r\n?/g, '\n')
    return `'sha256-${createHash('sha256').update(text).digest('base64')}'`
  })
  return ["script-src 'self'", ...hashes].join(' ') + "; object-src 'none'; base-uri 'none'"
}

/** Build only: the dev server injects scripts of its own (React refresh) that no hash could pin. */
export function scriptPolicyPlugin(): Plugin {
  return {
    name: 'script-policy',
    apply: 'build',
    transformIndexHtml: {
      order: 'post',
      handler: html => html.replace('<meta charset="UTF-8" />',
        `<meta charset="UTF-8" />\n    <meta http-equiv="Content-Security-Policy" content="${scriptPolicy(html)}" />`),
    },
  }
}
