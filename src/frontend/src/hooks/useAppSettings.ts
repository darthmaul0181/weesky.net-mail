import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api.js'

/**
 * The instance's settings, not the account's: they decide whether the webmail advertises itself
 * as an installable app and under what name. The read is anonymous, so this hook also serves the
 * login page.
 *
 * The backend answers every known key with its default already filled in — no copy of those
 * defaults here, or the two would drift apart at the first change.
 */
export const APP_SETTING_KEYS = {
  installable: 'app.installable',
  name: 'app.name',
  shortName: 'app.shortName',
} as const

export type AppSettings = Record<string, string>

const queryKey = ['appSettings'] as const

export function useAppSettings() {
  return useQuery({
    queryKey,
    queryFn: ({ signal }) => api.getAppSettings({ signal }) as Promise<AppSettings>,
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

/** Exactly 'true': an absent or malformed value leaves the app discreet. */
export function installableOf(settings: AppSettings): boolean {
  return settings[APP_SETTING_KEYS.installable] === 'true'
}
