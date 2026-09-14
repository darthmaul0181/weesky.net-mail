import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { canonicalAddress } from '../../lib/canonicalAddress'
import { useContacts } from '../contacts/queries'
import RecipientsField, { namesByAddressOf } from '../mail/compose/RecipientsField'
import type { AttendeeWrite } from './calendarTypes'

interface Props {
  value: AttendeeWrite[]
  onChange(next: AttendeeWrite[]): void
  /** Which chips are drawn as a mistake; the composer's own check when not given. */
  isValid?(email: string): boolean
}

/** The composer's recipients field, on the guest list: chips, the book's suggestions, and the
    name the book knows carried along as the CN the server will write (décision 8). */
export default function AttendeesField({ value, onChange, isValid }: Props) {
  const { t } = useTranslation('calendar')
  const { data: contacts } = useContacts()
  const book = useMemo(() => contacts ?? [], [contacts])
  const names = useMemo(() => namesByAddressOf(book), [book])
  const change = (tokens: string[]) => onChange(tokens.map(email => ({
    email, name: value.find(a => a.email === email)?.name ?? names.get(canonicalAddress(email)),
  })))
  return (
    <RecipientsField id="event-attendees" label={t('editor.attendees')}
      tokens={value.map(a => a.email)} onChange={change} contacts={book} isValid={isValid} />
  )
}
