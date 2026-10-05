/** The session most screens only read for its account id, signed in to the primary account. It
    stands in for the whole module: `vi.mock('…/contexts/AuthContext', () => import('…/test-auth'))`. */
export const useAuth = () => ({ activeAccount: { id: 'primary' }, activeAccountId: 'primary' })
