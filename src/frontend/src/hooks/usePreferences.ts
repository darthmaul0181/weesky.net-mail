import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api.js'

/**
 * The account's webmail preferences.
 *
 * Shared rather than owned by the settings module, because the mail list reads them too. The
 * backend answers every known key with its default filled in, so there is no copy of the
 * defaults here — a consumer waits for the answer rather than guessing, which is why the
 * accessors below take the map and not an optional one.
 */
export const PREFERENCE_KEYS = {
  pageSize: 'mail.pageSize',
  showPreview: 'mail.showPreview',
} as const

export type Preferences = Record<string, string>

const queryKey = ['preferences'] as const

export function usePreferences() {
  return useQuery({
    queryKey,
    queryFn: ({ signal }) => api.getPreferences({ signal }) as Promise<Preferences>,
    staleTime: 5 * 60 * 1000,
  })
}

export function useSetPreference() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: ({ key, value }: { key: string; value: string }) => api.setPreference(key, value),
    onSuccess: () => {
      client.invalidateQueries({ queryKey })
      // The page size is part of what a message page *is*, so every cached page was computed
      // under the old value and has to go.
      client.invalidateQueries({ queryKey: ['mail'] })
    },
  })
}

export function pageSizeOf(preferences: Preferences): number {
  return Number(preferences[PREFERENCE_KEYS.pageSize])
}

export function showPreviewOf(preferences: Preferences): boolean {
  return preferences[PREFERENCE_KEYS.showPreview] !== 'false'
}
