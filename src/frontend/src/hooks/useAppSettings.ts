import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api.js'
import type { LogoSize } from '../lib/appLogo'

/** The instance's settings (installable app, its name), not the account's; the read is anonymous,
 * so the login page uses it too. The backend fills every default: no copy here to drift. */
export const APP_SETTING_KEYS = {
  dailyImage: 'app.dailyImage',
  installable: 'app.installable',
  logo: 'app.logo',
  name: 'app.name',
  shortName: 'app.shortName',
} as const

export type AppSettings = Record<string, string>

const queryKey = ['appSettings'] as const

export function useAppSettings() {
  return useQuery({
    queryKey,
    queryFn: ({ signal }) => api.getAppSettings({ signal }),
    staleTime: 5 * 60 * 1000,
  })
}

export function useSetAppSetting() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: ({ key, value }: { key: string; value: string }) => api.setAppSetting(key, value),
    // onSettled, not onSuccess: a refused write must leave the screen on server state rather
    // than on an optimistic lie.
    onSettled: () => client.invalidateQueries({ queryKey }),
  })
}

export function useSetAppLogo() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (images: Record<LogoSize, Blob>) => api.setAppLogo(images),
    onSettled: () => client.invalidateQueries({ queryKey }),
  })
}

export function useDeleteAppLogo() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: () => api.deleteAppLogo(),
    onSettled: () => client.invalidateQueries({ queryKey }),
  })
}

/** Exactly 'true': an absent or malformed value never calls Bing. */
export function dailyImageOf(settings: AppSettings): boolean {
  return settings[APP_SETTING_KEYS.dailyImage] === 'true'
}

/** Exactly 'true': an absent or malformed value leaves the app discreet. */
export function installableOf(settings: AppSettings): boolean {
  return settings[APP_SETTING_KEYS.installable] === 'true'
}
