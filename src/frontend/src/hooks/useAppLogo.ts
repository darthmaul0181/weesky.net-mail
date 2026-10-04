import { useEffect, useMemo } from 'react'
import { logoUrls, rememberLogoVersion, rememberedLogoVersion, type LogoUrls } from '../lib/appLogo'
import { APP_SETTING_KEYS, useAppSettings } from './useAppSettings'

/** The logo's addresses. The last version seen stands in until the settings answer: no Scotty flash. */
export function useAppLogo(): LogoUrls {
  const { data } = useAppSettings()
  const answered = data?.[APP_SETTING_KEYS.logo]

  useEffect(() => {
    if (answered !== undefined) rememberLogoVersion(answered)
  }, [answered])

  return useMemo(() => logoUrls(answered ?? rememberedLogoVersion()), [answered])
}
