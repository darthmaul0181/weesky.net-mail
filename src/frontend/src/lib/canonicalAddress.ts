/** The client half of the folding rule, mirroring the backend's `IdentityResolver.Canonical`: the
 * table collates in binary, so both sides must fold alike or an approved sender stops matching. */
export function canonicalAddress(address: string | null | undefined): string {
  return (address ?? '').trim().toLowerCase()
}
