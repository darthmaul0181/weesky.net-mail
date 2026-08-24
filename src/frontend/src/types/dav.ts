/**
 * `GET /api/DavCredentials`. `password` is present on exactly two responses — enabling for the
 * first time, and regenerating — and never again: the backend stores a digest, so there is nothing
 * to reveal. Both optional fields are `undefined` and never `null`: the API omits null fields.
 */
export interface DavCredentials {
  serverUrl: string
  username: string
  configured: boolean
  cardDavEnabled: boolean
  lastUsedAt?: string
  password?: string
}
