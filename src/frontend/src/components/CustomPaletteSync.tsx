import { useEffect } from 'react'
import { useAuth } from '../contexts/AuthContext'
import { useTheme } from '../contexts/ThemeContext'
import { customPaletteOf, usePreferences } from '../hooks/usePreferences'
import { formatCustomPalette } from '../lib/customPalette'

/** Keeps the <style> and the local mirror on the account's palette. Renders nothing. */
export default function CustomPaletteSync() {
  const { isLoggedIn } = useAuth()
  const { data } = usePreferences({ enabled: isLoggedIn })
  const { declareCustomPalette } = useTheme()
  const def = data ? customPaletteOf(data) : undefined
  const key = def === undefined ? undefined : def && formatCustomPalette(def)

  // `key` stands for `def`, which is a fresh object on every render.
  useEffect(() => {
    if (def !== undefined) declareCustomPalette(def)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, declareCustomPalette])

  return null
}
