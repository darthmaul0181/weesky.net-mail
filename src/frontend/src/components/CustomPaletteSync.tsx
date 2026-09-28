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

  // `key` stands for `def`, which is a fresh object on every render.
  useEffect(() => {
    if (def !== undefined) applyCustomPalette(def)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key])

  useEffect(() => {
    if (key === null && palette === 'custom') setPalette('night')
  }, [key, palette, setPalette])

  return null
}
