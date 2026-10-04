import { useEffect } from 'react'
import { setCurrentLogo } from '../lib/appLogo'
import { setFaviconBase } from '../lib/favicon'
import { useAppLogo } from './useAppLogo'

/** The icons that live outside the React tree: the tab, the iOS home screen, the notifications. */
export function useBrandIcons(): void {
  const logo = useAppLogo()

  useEffect(() => {
    setCurrentLogo(logo)
    setFaviconBase(logo[32])
    document.querySelector('link[rel="apple-touch-icon"]')?.setAttribute('href', logo[192])
  }, [logo])
}
