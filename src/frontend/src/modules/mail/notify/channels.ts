import newMailSound from '../../../assets/new-mail.mp3'

const CLAIM_KEY = 'mail.lastNotifiedUidNext'

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
  const notification = new Notification('New mail', { body, tag })
  notification.onclick = onClick
}

interface Claim {
  uidValidity: number
  uidNext: number
}

function storedClaim(): Claim | null {
  try {
    const claim = JSON.parse(localStorage.getItem(CLAIM_KEY) ?? 'null') as Claim | null
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
 */
export function claimNotification(uidValidity: number, uidNext: number): boolean {
  const claim = storedClaim()
  if (claim && claim.uidValidity === uidValidity && claim.uidNext >= uidNext) return false

  try {
    localStorage.setItem(CLAIM_KEY, JSON.stringify({ uidValidity, uidNext }))
  } catch {
    // Storage denied (private mode, blocked cookies): notify rather than stay silent.
  }
  return true
}
