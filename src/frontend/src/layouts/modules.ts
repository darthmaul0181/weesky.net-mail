import CalendarIcon from '../icons/CalendarIcon'
import ContactsIcon from '../icons/ContactsIcon'
import GearIcon from '../icons/GearIcon'
import MailIcon from '../icons/MailIcon'

export interface ModuleEntry {
  to: string
  labelKey: string
  Icon: (props: { size?: number }) => React.JSX.Element
  /** A what's-new anchor, literal: the catalogue test reads it off the source. */
  anchor: string
}

/** The one module set, read by AppRail and BottomNav alike. `satisfies`, not an annotation, which
 * would widen `labelKey` to `string` and fail the typed `t()`. */
export const MODULES = [
  { to: '/mail', labelKey: 'rail.mail', Icon: MailIcon, anchor: 'rail:mail' },
  { to: '/calendar', labelKey: 'rail.calendar', Icon: CalendarIcon, anchor: 'rail:calendar' },
  { to: '/contacts', labelKey: 'rail.contacts', Icon: ContactsIcon, anchor: 'rail:contacts' },
] as const satisfies readonly ModuleEntry[]

/** Apart from the list because the rail pushes it to the far end with a spacer. */
export const SETTINGS_MODULE = {
  to: '/settings', labelKey: 'rail.settings', Icon: GearIcon, anchor: 'rail:settings',
} as const satisfies ModuleEntry

/** The union of every entry's own literal type, not `ModuleEntry` itself: a callback typed for
    one member alone (`typeof SETTINGS_MODULE`) rejects the others' `to`/`labelKey` literals, and
    widening back to `ModuleEntry.labelKey: string` is what loses the literal `t()` needs. */
export type ModuleItem = (typeof MODULES)[number] | typeof SETTINGS_MODULE
