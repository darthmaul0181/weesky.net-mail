/** `GET /api/Capabilities`. Every field is optional: an older backend answers 404 or omits fields,
 * which must read like the weesky platform, so every gate reads `!== false`. */
export interface Capabilities {
  platform?: 'weesky' | 'generic'
  admin?: boolean
  aliases?: boolean
  passwordChange?: boolean
  profileEditing?: boolean
  strictIdentities?: boolean
  quota?: boolean
  rules?: boolean
  /** Whether this deployment publishes a synchronisation address. Read `!== false` like the rest. */
  dav?: boolean
}
