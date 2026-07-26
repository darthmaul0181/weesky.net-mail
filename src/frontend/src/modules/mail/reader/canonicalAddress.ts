/**
 * The client half of the folding rule, mirroring `IdentityResolver.Canonical` on the backend.
 * The table collates in binary, so the two sides must fold identically or an approved sender
 * quietly stops matching the message it was approved from.
 */
export function canonicalAddress(address: string | null | undefined): string {
  return (address ?? '').trim().toLowerCase()
}
