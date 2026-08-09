import i18next from 'i18next'

/** Attachment sizes, at the precision a chip has room for. The unit is read from the global
    instance rather than taken as an argument: every call site sits in a component that already
    subscribes through useTranslation, so they re-render when the language changes.

    The variable is `value`, deliberately not `count`: `count` would put i18next into plural
    resolution, and a size has no plural — "1 Ko" and "3 Ko" take the same word. */
export function formatSize(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) return ''
  if (bytes < 1024) return i18next.t('common:sizes.b', { value: bytes })
  if (bytes < 1024 * 1024) return i18next.t('common:sizes.kb', { value: Math.round(bytes / 1024) })
  return i18next.t('common:sizes.mb', { value: (bytes / (1024 * 1024)).toFixed(1) })
}
