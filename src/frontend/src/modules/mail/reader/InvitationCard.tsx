import { useMemo, useState, type JSX } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Trans, useTranslation } from 'react-i18next'
import CalendarIcon from '../../../icons/CalendarIcon'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import { ApiError } from '../../../api.js'
import CalendarSelect from '../../calendar/CalendarSelect'
import { hourCycleOf } from '../../calendar/calendarLocale'
import { useCalendars } from '../../calendar/queries'
import type { InvitationAnswer, InvitationResponse, MailInvitation } from '../api/mailTypes'
import { mailKeys, useAccountId, useRespondInvitation } from '../queries'
import { cardStateOf, noActionKey, whenOf } from './invitationText'

interface Props {
  invitation: MailInvitation
  folderPath: string
  uid: number
  /** The decline's mail left for the trash: the reader moves on as after a delete. */
  onTrashed: () => void
}

/**
 * The card between the header and the body: the event as the organizer wrote it, and what to do
 * about it. Drawn from the block the message carried, redrawn from the answer the API hands back —
 * so the screen never has to guess what the calendar and the organizer now hold.
 */
export default function InvitationCard({ invitation: initial, folderPath, uid, onTrashed }: Props) {
  const { t, i18n } = useTranslation('mail')
  const tz = useMemo(() => Intl.DateTimeFormat().resolvedOptions().timeZone, [])
  const region = navigator.language
  const cycle = useMemo(() => hourCycleOf(region), [region])
  const [invitation, setInvitation] = useState(initial)
  const [outcome, setOutcome] = useState<InvitationResponse | null>(null)
  const [lastAnswer, setLastAnswer] = useState<InvitationAnswer | null>(null)
  const [error, setError] = useState<{ message: string; gone: boolean } | null>(null)
  const [calendarId, setCalendarId] = useState<string | undefined>(undefined)
  const accountId = useAccountId()
  const queryClient = useQueryClient()
  const respond = useRespondInvitation()
  const { data: calendars } = useCalendars(tz)

  const state = cardStateOf(invitation)
  const busy = respond.isPending
  const calendarName = (id?: string) => calendars?.find(c => c.id === id)?.displayName ?? ''

  async function answer(value: InvitationAnswer) {
    setError(null)
    setOutcome(null)
    setLastAnswer(value)
    try {
      const result = await respond.mutateAsync({
        folder: folderPath, uid, part: invitation.part, answer: value, calendarId,
        language: i18n.language.startsWith('fr') ? 'fr' : 'en', timeZone: tz,
      })
      setInvitation(result.invitation)
      setOutcome(result)
      if (result.trashed) onTrashed()
    } catch (caught) {
      const gone = caught instanceof ApiError && caught.status === 404
      setError({ message: apiErrorMessage(caught, t('reader.invitation.failed')), gone })
    }
  }

  // Decided once: the word on the badge and the class under it are two readings of one thing, and
  // two conditionals is how they come to disagree.
  const badgeKind = invitation.method === 'Cancel' ? 'cancel'
    : invitation.inCalendar === 'Outdated' ? 'update' : 'request'
  const badge = badgeKind === 'cancel' ? t('reader.invitation.badgeCancel')
    : badgeKind === 'update' ? t('reader.invitation.badgeUpdate')
      : t('reader.invitation.badgeRequest')

  if (state === 'unreadable') {
    return (
      <section className="invitation-card is-unreadable" aria-label={t('reader.invitation.unreadable')}>
        <div className="invitation-card-head">
          <CalendarIcon size={16} />
          <span className="invitation-card-title">{t('reader.invitation.unreadable')}</span>
        </div>
        <p className="invitation-card-hint">{t('reader.invitation.unreadableHint')}</p>
      </section>
    )
  }

  // The label is passed already translated rather than as a key: a key reaching `t()` through a
  // variable is invisible to both the typed `t` and `locales/keys.test.ts`.
  // The mock-up's shapes: the answer the organizer hopes for is the primary button, the two
  // others are ghosts, and every button is as wide as its label.
  const answerButton = (value: InvitationAnswer, label: string, partStat: string, primary = false) => (
    <button
      type="button"
      className={primary ? 'btn btn-primary btn-auto' : 'btn btn-ghost'}
      disabled={busy}
      onClick={() => answer(value)}
      aria-pressed={state === 'updated' && invitation.savedPartStat === partStat ? true : undefined}
    >
      {label}
    </button>
  )
  const accept = () => answerButton('Accepted', t('reader.invitation.accept'), 'ACCEPTED', true)
  const tentative = () => answerButton('Tentative', t('reader.invitation.tentative'), 'TENTATIVE')
  const decline = () => answerButton('Declined', t('reader.invitation.decline'), 'DECLINED')
  const three = <>{accept()}{tentative()}{decline()}</>
  const addOnlyButton = (
    <button type="button" className="btn btn-primary btn-auto" disabled={busy} onClick={() => answer('AddOnly')}>
      {t('reader.invitation.addOnly')}
    </button>
  )
  // The calendar a creation goes to: the editor's own picker, drawn only when there is a choice.
  const chosenCalendar = calendarId ?? calendars?.find(c => c.isDefault)?.id ?? calendars?.[0]?.id ?? ''
  const calendarPicker = calendars && calendars.length > 1 && (
    <CalendarSelect label={t('reader.invitation.calendar')} calendars={calendars}
      value={chosenCalendar} onChange={setCalendarId} />
  )

  // The three answers a card can name, each in both voices: `filed` is what the organizer recorded
  // ("accepted"), `own` what the user's own calendar entry says ("You accepted") — a refusal has no
  // `own`, since declining deletes the entry. Anything else — `NEEDS-ACTION`, or a word this build
  // has no sentence for — is null, and says nothing rather than printing a key.
  const wordsFor = (partStat: string | undefined) =>
    partStat === 'ACCEPTED'
      ? { filed: t('reader.invitation.partstat.ACCEPTED'), own: t('reader.invitation.answered.ACCEPTED') }
      : partStat === 'TENTATIVE'
        ? { filed: t('reader.invitation.partstat.TENTATIVE'), own: t('reader.invitation.answered.TENTATIVE') }
        : partStat === 'DECLINED'
          ? { filed: t('reader.invitation.partstat.DECLINED'), own: null }
          : null
  const filedAnswer = wordsFor(invitation.filePartStat)?.filed ?? null

  let context: string | null = null
  if (state === 'forwarded') context = t('reader.invitation.forwarded')
  else if (state === 'updated') context = t('reader.invitation.updated')
  else if (state === 'cancelled') context = t('reader.invitation.cancelled')
  else if (state === 'invite' && filedAnswer)
    context = t('reader.invitation.alreadyAnswered', { answer: filedAnswer })

  let foot: JSX.Element
  switch (state) {
    case 'invite':
    case 'updated':
      foot = (
        <div className="invitation-card-actions">
          {state === 'invite' && calendarPicker}
          {invitation.addressedTo ? three : addOnlyButton}
        </div>
      )
      break
    case 'answered': {
      // An entry filed with no answer of its own — AddOnly — names its calendar alone.
      const answeredWord = wordsFor(invitation.savedPartStat)?.own ?? null
      foot = (
        <div className="invitation-card-actions is-answered">
          <span className="invitation-card-answer">
            {answeredWord && <span className="invitation-card-check" aria-hidden="true">✓</span>}
            {answeredWord
              ? (
                <Trans
                  ns="mail"
                  i18nKey="reader.invitation.answered.in"
                  values={{ answer: answeredWord, calendar: calendarName(invitation.calendarId) }}
                  components={{ b: <b /> }}
                />
              )
              : t('reader.invitation.answered.added', { calendar: calendarName(invitation.calendarId) })}
          </span>
          {/* Only an attendee can change an answer: a forwarded invitation has nobody to tell. */}
          {invitation.addressedTo && (
            <span className="invitation-card-others">
              {invitation.savedPartStat !== 'ACCEPTED' && accept()}
              {invitation.savedPartStat !== 'TENTATIVE' && tentative()}
              {decline()}
            </span>
          )}
        </div>
      )
      break
    }
    case 'forwarded':
      foot = <div className="invitation-card-actions">{calendarPicker}{addOnlyButton}</div>
      break
    case 'cancelled':
      foot = (
        <div className="invitation-card-actions is-answered">
          <span className="invitation-card-answer is-muted">
            {t('reader.invitation.answered.added', { calendar: calendarName(invitation.calendarId) })}
          </span>
          <span className="invitation-card-others">
            <button type="button" className="btn btn-danger" disabled={busy} onClick={() => answer('Remove')}>
              {t('reader.invitation.remove')}
            </button>
          </span>
        </div>
      )
      break
    default: {
      const why = noActionKey(invitation)
      foot = (
        <p className="invitation-card-hint">
          {why === 'occurrenceOnly' ? t('reader.invitation.noAction.occurrenceOnly')
            : why === 'newer' ? t('reader.invitation.noAction.newer')
              : t('reader.invitation.noAction.cancelAbsent')}
        </p>
      )
    }
  }

  return (
    <section className="invitation-card" aria-label={badge}>
      <div className="invitation-card-head">
        <CalendarIcon size={16} />
        <span className="invitation-card-title">
          {invitation.summary || t('views.noTitle', { ns: 'calendar' })}
        </span>
        <span className={`invitation-card-badge is-${badgeKind}`}>{badge}</span>
      </div>
      {context && <p className="invitation-card-context">{context}</p>}
      <dl className="invitation-card-rows">
        <dt>{t('reader.invitation.when')}</dt>
        <dd>
          {whenOf(invitation, tz, i18n.language, region, cycle, t)}
          {invitation.repeats && (
            <span className="invitation-card-repeats"> · {t('reader.invitation.repeats')}</span>
          )}
        </dd>
        {invitation.location && <><dt>{t('reader.invitation.where')}</dt><dd>{invitation.location}</dd></>}
        {invitation.organizer && (
          <><dt>{t('reader.invitation.organizer')}</dt>
            <dd>{invitation.organizer.name || invitation.organizer.email}</dd></>
        )}
        {invitation.attendees.length > 0 && (
          <><dt>{t('reader.invitation.attendees')}</dt>
            <dd>{invitation.attendees.map(a => a.name || a.email).join(', ')}</dd></>
        )}
      </dl>
      {foot}
      {outcome && !outcome.replySent && outcome.replyError !== undefined && lastAnswer && (
        <p className="invitation-card-error">
          {t(lastAnswer === 'Declined' ? 'reader.invitation.replyFailed' : 'reader.invitation.addedReplyFailed')}
          <button type="button" className="btn btn-ghost" disabled={busy} onClick={() => answer(lastAnswer)}>
            {t('reader.invitation.resend')}
          </button>
        </p>
      )}
      {/* The refusal left, the mail did not — read from the trash, no trash folder, a move the
          server could not make. The card is back on its three buttons, which says nothing. */}
      {outcome?.replySent && lastAnswer === 'Declined' && !outcome.trashed && (
        <p className="invitation-card-context">{t('reader.invitation.declinedStays')}</p>
      )}
      {error && (
        <p className="invitation-card-error">
          {error.message}
          {/* The message or its part is gone: refetching it is what redraws the reader around
              whatever is actually there now. */}
          {error.gone && (
            <button
              type="button"
              className="btn btn-ghost"
              onClick={() => queryClient.invalidateQueries({ queryKey: mailKeys.message(accountId, folderPath, uid) })}
            >
              {t('reader.invitation.reload')}
            </button>
          )}
        </p>
      )}
    </section>
  )
}
