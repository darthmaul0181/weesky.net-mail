import { useCallback, useEffect, useMemo, useRef, useState, type JSX } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Trans, useTranslation } from 'react-i18next'
import CalendarIcon from '../../../icons/CalendarIcon'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import { ApiError } from '../../../api.js'
import CalendarSelect from '../../calendar/CalendarSelect'
import { hourCycleOf } from '../../calendar/calendarLocale'
import { useCalendars } from '../../calendar/queries'
import type { InvitationAnswer, InvitationResponse, MailInvitation, MailMessageDetail } from '../api/mailTypes'
import { mailKeys, useAccountId, useApplyInvitationReply, useRespondInvitation } from '../queries'
import { cardStateOf, noActionKey, replyPending, replySentenceOf, whenOf } from './invitationText'

/** The `replyError`s that are not failures: the file itself leaves no reply to send, so no retry would. */
const NO_ORGANIZER = 'invitation_no_organizer'
const UID_UNWRITABLE = 'invitation_uid_unwritable'
/** Refusals that mean the block on screen is out of date: only the message as it now reads can say why. */
const OUTDATED_REPLY = ['reply_not_applicable', 'reply_not_a_reply']

interface Props {
  invitation: MailInvitation
  folderPath: string
  uid: number
  /** The decline's mail left for the trash: the reader moves on as after a delete. */
  onTrashed: () => void
}

// Drawn from the block the message carried, then redrawn from the API's answer, so the screen
// never has to guess what the calendar and the organizer now hold.
export default function InvitationCard({ invitation: initial, folderPath, uid, onTrashed }: Props) {
  const { t, i18n } = useTranslation('mail')
  const tz = useMemo(() => Intl.DateTimeFormat().resolvedOptions().timeZone, [])
  const region = navigator.language
  const cycle = useMemo(() => hourCycleOf(region), [region])
  const [invitation, setInvitation] = useState(initial)
  const [outcome, setOutcome] = useState<InvitationResponse | null>(null)
  const [lastAnswer, setLastAnswer] = useState<InvitationAnswer | null>(null)
  const [error, setError] = useState<{ message: string; gone: boolean; retry?: boolean } | null>(null)
  const [calendarId, setCalendarId] = useState<string | undefined>(undefined)
  const [reloading, setReloading] = useState(false)
  const accountId = useAccountId()
  const queryClient = useQueryClient()
  const respond = useRespondInvitation()
  const { mutateAsync: applyMutation, isPending: applying } = useApplyInvitationReply()
  const attempted = useRef(false)
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

  // A refusal only a later state could lift (4xx) offers no retry: asking again earns the same one.
  // One that says the block is out of date offers Reload instead, as a message gone does.
  const applyReply = useCallback(async () => {
    setError(null)
    try {
      const result = await applyMutation({ folder: folderPath, uid, part: invitation.part })
      setInvitation(result.invitation)
      if (!result.applied) setError({ message: t('reader.invitation.reply.notApplied'), gone: false, retry: true })
    } catch (caught) {
      const status = caught instanceof ApiError ? caught.status : 0
      const outdated = caught instanceof ApiError && OUTDATED_REPLY.includes(caught.code ?? '')
      setError({
        message: apiErrorMessage(caught, t('reader.invitation.reply.notApplied')),
        gone: status === 404 || outdated, retry: status === 0 || status >= 500,
      })
    }
  }, [applyMutation, folderPath, uid, invitation.part, t])

  // Once per mounting: the reader's periodic refresh, StrictMode's second mount and the block the
  // answer hands back all come through here again, and the ref turns them away.
  const pending = replyPending(invitation)
  useEffect(() => {
    if (!pending || attempted.current) return
    attempted.current = true
    void applyReply()
  }, [pending, applyReply])

  // The card holds its own copy of the block, so a refetch alone would redraw nothing: it takes the
  // block the message now carries, and a reply that still applies is carried in on the user's asking.
  // A refetch that did not answer leaves the cache on the old block, which confirms nothing.
  async function reload() {
    const key = mailKeys.message(accountId, folderPath, uid)
    setReloading(true)
    try {
      await queryClient.invalidateQueries({ queryKey: key })
    } finally {
      setReloading(false)
    }
    const state = queryClient.getQueryState<MailMessageDetail>(key)
    const fresh = state?.data?.invitation
    if (state?.status !== 'success' || state.isInvalidated || !fresh) return
    setError(null)
    setInvitation(fresh)
    if (replyPending(fresh)) void applyReply()
  }

  // Decided once: the word on the badge and the class under it are two readings of one thing, and
  // two conditionals is how they come to disagree.
  const badgeKind = invitation.method === 'Cancel' ? 'cancel' : invitation.method === 'Reply' ? 'reply'
    : invitation.inCalendar === 'Outdated' ? 'update' : 'request'
  const badge = badgeKind === 'cancel' ? t('reader.invitation.badgeCancel')
    : badgeKind === 'reply' ? t('reader.invitation.badgeReply')
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

  // The label arrives translated rather than as a key: a key reaching `t()` through a variable is
  // invisible to both the typed `t` and `locales/keys.test.ts`.
  const answerButton = (value: InvitationAnswer, label: string, partStat: string, primary = false) => (
    <button
      type="button"
      className={primary ? 'btn btn-primary btn-auto' : 'btn btn-ghost'}
      disabled={busy}
      onClick={() => void answer(value)}
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
    <button type="button" className="btn btn-primary btn-auto" disabled={busy} onClick={() => void answer('AddOnly')}>
      {t('reader.invitation.addOnly')}
    </button>
  )
  // The calendar a creation goes to: the editor's own picker, drawn only when there is a choice.
  const chosenCalendar = calendarId ?? calendars?.find(c => c.isDefault)?.id ?? calendars?.[0]?.id ?? ''
  const calendarPicker = calendars && calendars.length > 1 && (
    <CalendarSelect label={t('reader.invitation.calendar')} calendars={calendars}
      value={chosenCalendar} onChange={setCalendarId} />
  )

  // `filed` is the organizer's record ("accepted"), `own` the user's entry ("You accepted"); a refusal
  // has no `own`, since declining deletes the entry. Any other value is null rather than a raw key.
  const wordsFor = (partStat: string | undefined) =>
    partStat === 'ACCEPTED'
      ? { filed: t('reader.invitation.partstat.ACCEPTED'), own: t('reader.invitation.answered.ACCEPTED') }
      : partStat === 'TENTATIVE'
        ? { filed: t('reader.invitation.partstat.TENTATIVE'), own: t('reader.invitation.answered.TENTATIVE') }
        : partStat === 'DECLINED'
          ? { filed: t('reader.invitation.partstat.DECLINED'), own: null }
          : null
  const filedAnswer = wordsFor(invitation.filePartStat)?.filed ?? null

  const people = invitation.reply ? [invitation.reply] : invitation.attendees
  // An answer that should have mailed the organizer and did not: a failure worth a Resend, or a
  // file no reply can be built from, which no retry will change.
  const unsent = outcome && !outcome.replySent && outcome.replyError !== undefined && lastAnswer
    ? { answer: lastAnswer, declined: lastAnswer === 'Declined', cause: outcome.replyError } : null
  const unanswerable = unsent?.cause === NO_ORGANIZER || unsent?.cause === UID_UNWRITABLE

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
    case 'reply': {
      // Nothing to decide: the answer, checked once the calendar holds it, or why it stays out.
      const reply = invitation.reply
      foot = (
        <div className="invitation-card-actions is-answered">
          {reply && (
            <span className={reply.status === 'Applicable' ? 'invitation-card-answer' : 'invitation-card-answer is-muted'}>
              {reply.applied && <span className="invitation-card-check" aria-hidden="true">✓</span>}
              {replySentenceOf(reply, t)}
            </span>
          )}
        </div>
      )
      break
    }
    case 'cancelled':
      foot = (
        <div className="invitation-card-actions is-answered">
          <span className="invitation-card-answer is-muted">
            {t('reader.invitation.answered.added', { calendar: calendarName(invitation.calendarId) })}
          </span>
          <span className="invitation-card-others">
            <button type="button" className="btn btn-danger" disabled={busy} onClick={() => void answer('Remove')}>
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
        {/* A reply names the guest who answered; a delegation's delegate is not theirs to show. */}
        {people.length > 0 && (
          <><dt>{t('reader.invitation.attendees')}</dt>
            <dd>{people.map(a => a.name || a.email).join(', ')}</dd></>
        )}
      </dl>
      {foot}
      {unsent && unanswerable && (
        <p className="invitation-card-context">
          {unsent.cause === NO_ORGANIZER
            ? t(unsent.declined ? 'reader.invitation.declinedNoOrganizer' : 'reader.invitation.addedNoOrganizer')
            : t(unsent.declined ? 'reader.invitation.declinedUidUnwritable' : 'reader.invitation.addedUidUnwritable')}
        </p>
      )}
      {unsent && !unanswerable && (
        <p className="invitation-card-error" role="alert">
          {t(unsent.declined ? 'reader.invitation.replyFailed' : 'reader.invitation.addedReplyFailed')}
          <button type="button" className="btn btn-ghost" disabled={busy} onClick={() => void answer(unsent.answer)}>
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
        <p className="invitation-card-error" role="alert">
          {error.message}
          {/* The message or its part is gone: refetching it is what redraws the reader around
              whatever is actually there now. */}
          {error.gone && (
            <button
              type="button"
              className="btn btn-ghost"
              disabled={reloading || applying}
              onClick={() => void reload()}
            >
              {t('reader.invitation.reload')}
            </button>
          )}
          {error.retry && (
            <button type="button" className="btn btn-ghost" disabled={applying} onClick={() => void applyReply()}>
              {t('reader.invitation.reply.retry')}
            </button>
          )}
        </p>
      )}
    </section>
  )
}
