import type { TFunction } from 'i18next'

// Mirrors Uri.CheckHostName loosely: a dotted DNS name or an IPv4/IPv6 literal. It only needs to
// catch the common typos before they round-trip — the backend's own check is the real gate.
const HOSTNAME_RE = /^(?!-)[A-Za-z0-9-]{1,63}(?<!-)(\.(?!-)[A-Za-z0-9-]{1,63}(?<!-))*$/
const IPV4_RE = /^(25[0-5]|2[0-4]\d|1?\d?\d)(\.(25[0-5]|2[0-4]\d|1?\d?\d)){3}$/
const IPV6_RE = /^[0-9a-fA-F:]+:[0-9a-fA-F:]*$/

// Trims first: a value that will itself be `.trim()`-med before it is sent must not be flagged
// invalid for carrying the whitespace that trimming would have removed anyway.
export function isValidHost(host: string): boolean {
  const trimmed = host.trim()
  if (!trimmed || trimmed.length > 255) return false
  return HOSTNAME_RE.test(trimmed) || IPV4_RE.test(trimmed) || IPV6_RE.test(trimmed)
}

export function isValidPort(value: string): boolean {
  if (!/^\d+$/.test(value.trim())) return false
  const port = Number(value)
  return port >= 1 && port <= 65535
}

// STARTTLS and SSL/TLS are protocol names; only "None" is prose. Shared by every mail endpoint
// dialog (external domains, the scheduling service account) so the three choices never drift.
export const SECURITY_OPTIONS = [
  { value: 'None', labelKey: 'external.securityNone' },
  { value: 'StartTls', label: 'STARTTLS' },
  { value: 'SslOnConnect', label: 'SSL/TLS' },
] as const satisfies Array<{ value: string; label?: string; labelKey?: string }>

export type SecurityOption = (typeof SECURITY_OPTIONS)[number]

export function securityLabel(option: SecurityOption, t: TFunction<'admin'>): string {
  return 'labelKey' in option ? t(option.labelKey) : option.label
}
