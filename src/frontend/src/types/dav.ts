/** `GET /api/DavCredentials`. `password` comes only on enabling and regenerating (the backend keeps
 * a digest). Optional fields are `undefined`, never `null`: the API omits null fields. */
export interface DavCredentials {
  serverUrl: string
  username: string
  configured: boolean
  cardDavEnabled: boolean
  calDavEnabled: boolean
  lastUsedAt?: string
  password?: string
}
