import { useAuth } from '../contexts/AuthContext'

/** The active account's id, the scope of every module's query keys. The id, not
 * `activeAccount.id`: it is known before the accounts fetch, while the resolved account would
 * send every query to the primary until the list lands. */
export function useAccountId(): string {
  return useAuth().activeAccountId
}
