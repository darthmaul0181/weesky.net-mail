import { useEffect } from 'react'
import { useAuth } from '../contexts/AuthContext'
import { useTheme } from '../contexts/ThemeContext'
import { customPaletteOf, usePreferences } from '../hooks/usePreferences'
import { formatCustomPalette } from '../lib/customPalette'
import { applyCustomPalette } from '../lib/customPaletteStyle'

/** Keeps the <style> and the local mirror on the account's palette. Renders nothing. */
export default function CustomPaletteSync() {
  const { isLoggedIn } = useAuth()
  const { data } = usePreferences({ enabled: isLoggedIn })
  const { palette, setPalette } = useTheme()
  const def = data ? customPaletteOf(data) : undefined
  const key = def === undefined ? undefined : def && formatCustomPalette(def)

  useEffect(() => {
    if (def === undefined) return
    applyCustomPalette(def)
    if (!def && palette === 'custom') setPalette('night')
    // `key` stands for `def`, which is a fresh object on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, palette])

  return null
}
