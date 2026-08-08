import i18next from 'i18next'
import logo from '../../../assets/logo-192.png'
import newMailSound from '../../../assets/new-mail.mp3'

const CLAIM_PREFIX = 'mail.lastNotifiedUidNext'
const claimKey = (accountId: string) => `${CLAIM_PREFIX}.${accountId}`

let audio: HTMLAudioElement | null = null

/** One element, kept and rewound. Constructing one per notification would re-download on
    some browsers and lose the autoplay engagement earned by the settings click. */
export function playNewMailSound(): void {
  audio ??= new Audio(newMailSound)
  audio.currentTime = 0
  // Browsers block audio from a page nobody has interacted with — which is precisely when a
  // notification plays. A refusal is swallowed: it must not raise a second interruption.
  void audio.play()?.catch(() => {})
}

export function desktopPermission(): NotificationPermission | 'unsupported' {
  return typeof Notification === 'undefined' ? 'unsupported' : Notification.permission
}

export async function requestDesktopPermission(): Promise<NotificationPermission | 'unsupported'> {
  if (typeof Notification === 'undefined') return 'unsupported'
  return Notification.requestPermission()
}

export function showDesktopNotification(body: string, tag: string, onClick: () => void): void {
  if (desktopPermission() !== 'granted') return

  // The tag is what makes two tabs raise one bubble: an identical tag replaces rather than
  // stacks, so the browser dedupes for us.
  const notification = new Notification(i18next.t('mail:notify.title'), { body, tag, icon: logo })
  notification.onclick = onClick
}

interface Claim {
  uidValidity: number
  uidNext: number
}

function storedClaim(accountId: string): Claim | null {
  try {
    const claim = JSON.parse(localStorage.getItem(claimKey(accountId)) ?? 'null') as Claim | null
    return typeof claim?.uidValidity === 'number' && typeof claim.uidNext === 'number'
      ? claim
      : null
  } catch {
    return null
  }
}

/**
 * Cross-tab guard for the sound, which has no equivalent of the notification tag. Not an
 * atomic lock: two tabs poll on independent clocks and practically never land in the same
 * millisecond, and the worst case is one duplicate beep.
 *
 * Scoped to the uidValidity it was banked under: a rebuilt mailbox restarts uidNext near 1, and
 * a bare number from the old numbering would refuse every genuine arrival, in every tab, for good.
 * Keyed by account for the same reason: uidValidity is unique per mailbox, never across servers.
 */
export function claimNotification(
  accountId: string, uidValidity: number, uidNext: number,
): boolean {
  const claim = storedClaim(accountId)
  if (claim && claim.uidValidity === uidValidity && claim.uidNext >= uidNext) return false

  try {
    localStorage.setItem(claimKey(accountId), JSON.stringify({ uidValidity, uidNext }))
  } catch {
    // Storage denied (private mode, blocked cookies): notify rather than stay silent.
  }
  return true
}

/**
 * Dropped when a session ends: the claim is one mailbox's counter, and the next sign-in reaches
 * none of the mailboxes this one banked. Every key under the prefix goes, the legacy unscoped one
 * included — left in a browser by the release before this, it would gag the first arrival.
 */
export function forgetNotificationClaim(): void {
  try {
    Object.keys(localStorage)
      .filter(key => key.startsWith(CLAIM_PREFIX))
      .forEach(key => localStorage.removeItem(key))
  } catch {
    // Storage denied: nothing was banked to begin with.
  }
}
