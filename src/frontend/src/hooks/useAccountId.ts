import { useAuth } from '../contexts/AuthContext'

/** The active account's id, the scope every module's query keys carry. Shared rather than owned
    by the mail module: contacts key their cache on it too, and a module reaching into another
    module's data layer for the account identity is a coupling nothing justifies.

    The id, not `activeAccount.id`: the metadata needs the accounts fetch, the id does not, and
    deriving it from the resolved account would send every query to the primary mailbox until the
    list lands. Readers wait on `accountsLoading` rather than on an id that is already correct. */
export function useAccountId(): string {
  return useAuth().activeAccountId
}
