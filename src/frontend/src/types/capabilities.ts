/**
 * What the backend platform wires up (`GET /api/Capabilities`, Task 6). Every field is optional:
 * an older backend answers 404 or omits fields entirely, and the absence must read exactly like
 * the weesky platform — every gate below reads `!== false`, never `=== true`, for that reason.
 */
export interface Capabilities {
  platform?: 'weesky' | 'generic'
  admin?: boolean
  aliases?: boolean
  passwordChange?: boolean
  profileEditing?: boolean
  strictIdentities?: boolean
  quota?: boolean
  rules?: boolean
}
