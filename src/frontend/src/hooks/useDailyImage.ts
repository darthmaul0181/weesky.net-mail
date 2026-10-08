import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { api } from '../api.js'
import { dailyImageUrl } from '../lib/dailyImage'
import type { Locale } from '../lib/locale'
import { dailyImageOf, useAppSettings } from './useAppSettings'

export interface DailyImage {
  src: string
  title: string
  copyright: string
}

/** Null until the photo itself has loaded: nothing ever shows a broken or half-drawn image. The load
 * is part of the query, so a pane mounted again finds it done and draws the photo on its first paint. */
export function useDailyImage(wanted = true): DailyImage | null {
  const { i18n } = useTranslation()
  const lang: Locale = i18n.language === 'fr' ? 'fr' : 'en'
  const { data: settings } = useAppSettings()
  const enabled = wanted && !!settings && dailyImageOf(settings)

  const { data: info } = useQuery({
    queryKey: ['dailyImage', lang],
    queryFn: async ({ signal }) => {
      const info = await api.getDailyImage(lang, { signal })
      await preload(dailyImageUrl(info.version, lang))
      return info
    },
    enabled,
    staleTime: 60 * 60 * 1000,
    retry: false,
  })

  return enabled && info ? { src: dailyImageUrl(info.version, lang), title: info.title, copyright: info.copyright } : null
}

function preload(src: string): Promise<void> {
  return new Promise((resolve, reject) => {
    const image = new Image()
    image.onload = () => resolve()
    image.onerror = () => reject(new Error('The image of the day did not load'))
    image.src = src
  })
}
