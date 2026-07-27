import { useAuth } from '../contexts/AuthContext'

/** The active account's id, the scope every module's query keys carry. Shared rather than owned
    by the mail module: contacts key their cache on it too, and a module reaching into another
    module's data layer for the account identity is a coupling nothing justifies. */
export function useAccountId(): string {
  return useAuth().activeAccount?.id ?? 'primary'
}
