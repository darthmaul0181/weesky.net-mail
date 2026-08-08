import i18next from 'i18next'

/**
 * Turns a backend failure into something a French reader can read.
 *
 * The API answers in English and stays that way; what it does guarantee is a stable `code` on the
 * failures a client is expected to branch on. Those are translated here. Everything else falls back
 * to the caller's own local message — which every call site already spelled out, as the second
 * operand of the `err.message || '…'` this replaces. Server prose stops reaching the screen; it is
 * still on the error object for the console and the logs, where a symbol is what a developer wants.
 */
// `as const` so the values are a literal union rather than `string`: the typed t() accepts them
// straight, with no cast to smuggle an unchecked key past the compiler.
const CODES = {
  credentials_unavailable: 'errors:credentialsUnavailable',
  account_not_found: 'errors:accountNotFound',
  connected_credentials_invalid: 'errors:connectedCredentialsInvalid',
  'Message not found': 'errors:messageNotFound',
  domain_in_use: 'errors:domainInUse',
  attachment_too_large: 'errors:attachmentTooLarge',
  invalid_folder_name: 'errors:invalidFolderName',
  csv_no_recognised_column: 'errors:csvNoRecognisedColumn',
  already_signed_in: 'errors:alreadySignedIn',
  invalid_email_address: 'errors:invalidEmailAddress',
  unknown_domain: 'errors:unknownDomain',
  provider_domain: 'errors:providerDomain',
  provider_not_available: 'errors:providerNotAvailable',
  account_uses_password: 'errors:accountUsesPassword',
  account_uses_provider: 'errors:accountUsesProvider',
  reconnect_mismatch: 'errors:reconnectMismatch',
  oauth_handshake_incomplete: 'errors:oauthHandshakeIncomplete',
  oauth_start_target_required: 'errors:oauthStartTargetRequired',
  not_a_provider_domain: 'errors:notAProviderDomain',
  address_too_long: 'errors:addressTooLong',
  password_required: 'errors:passwordRequired',
  password_too_long: 'errors:passwordTooLong',
} as const

export function apiErrorMessage(error: unknown, fallback: string): string {
  const code = error instanceof Error ? (error as { code?: string }).code : undefined
  // hasOwnProperty, not `CODES[code]` directly: `code` comes off the wire, and 'constructor'
  // resolves to an inherited function — truthy, and not a translation key.
  const key = code && Object.prototype.hasOwnProperty.call(CODES, code)
    ? CODES[code as keyof typeof CODES] : undefined
  return key ? i18next.t(key) : fallback
}
