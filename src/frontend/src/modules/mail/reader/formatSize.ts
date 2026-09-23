import i18next from 'i18next'

/** Attachment sizes at a chip's precision. The unit comes from the global instance: every caller
 * subscribes through useTranslation, so it re-renders on a language change. `value`, never
 * `count`, which would trigger plural resolution a size does not have. */
export function formatSize(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) return ''
  if (bytes < 1024) return i18next.t('common:sizes.b', { value: bytes })
  if (bytes < 1024 * 1024) return i18next.t('common:sizes.kb', { value: Math.round(bytes / 1024) })
  return i18next.t('common:sizes.mb', { value: (bytes / (1024 * 1024)).toFixed(1) })
}
