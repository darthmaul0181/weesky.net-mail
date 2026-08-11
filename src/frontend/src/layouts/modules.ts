import CalendarIcon from '../icons/CalendarIcon'
import ContactsIcon from '../icons/ContactsIcon'
import GearIcon from '../icons/GearIcon'
import MailIcon from '../icons/MailIcon'

export interface ModuleEntry {
  to: string
  labelKey: string
  Icon: (props: { size?: number }) => React.JSX.Element
}

/** The one definition of the module set. AppRail and BottomNav both read it, or a module added
    later shows up on a desktop and vanishes on a phone.
    `satisfies` rather than a `: readonly ModuleEntry[]` annotation: the latter widens each
    `labelKey` to `string`, which the typed `t()` guard then rejects as an unknown key. */
export const MODULES = [
  { to: '/mail', labelKey: 'rail.mail', Icon: MailIcon },
  { to: '/calendar', labelKey: 'rail.calendar', Icon: CalendarIcon },
  { to: '/contacts', labelKey: 'rail.contacts', Icon: ContactsIcon },
] as const satisfies readonly ModuleEntry[]

/** Apart from the list because the rail pushes it to the far end with a spacer. */
export const SETTINGS_MODULE = {
  to: '/settings', labelKey: 'rail.settings', Icon: GearIcon,
} as const satisfies ModuleEntry

/** The union of every entry's own literal type, not `ModuleEntry` itself: a callback typed for
    one member alone (`typeof SETTINGS_MODULE`) rejects the others' `to`/`labelKey` literals, and
    widening back to `ModuleEntry.labelKey: string` is what loses the literal `t()` needs. */
export type ModuleItem = (typeof MODULES)[number] | typeof SETTINGS_MODULE
