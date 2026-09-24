import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import type { AccountIdentity } from '../../../lib/accountIdentity'
import { canonicalAddress } from '../../../lib/canonicalAddress'
import DropdownMenu from '../../../components/DropdownMenu'
import ChevronDownIcon from '../../../icons/ChevronDownIcon'
import type { GroupOption } from '../../contacts/contactSearch'
import type { Contact } from '../../contacts/contactTypes'
import type { MailPriority, SendingIdentity } from '../api/mailTypes'
import IdentitySelect from './IdentitySelect'
import RecipientsField, { namesByAddressOf } from './RecipientsField'
import { usableIdentities } from './usableIdentities'

const PRIORITIES: MailPriority[] = ['high', 'normal', 'low']

function priorityLabel(value: MailPriority, t: TFunction<'compose'>): string {
  return value === 'high' ? t('priority.high')
    : value === 'low' ? t('priority.low') : t('priority.normal')
}

interface Props {
  folded: boolean
  onUnfold: () => void
  identity: AccountIdentity | null
  identityList: SendingIdentity[] | undefined
  effectiveFrom: string | null
  changeFrom: (address: string) => void
  to: string[]
  changeTo: (tokens: string[]) => void
  autoFocusTo: boolean
  cc: string[]
  changeCc: (tokens: string[]) => void
  bcc: string[]
  changeBcc: (tokens: string[]) => void
  contacts: Contact[] | undefined
  groupOptions: GroupOption[]
  notifyEmptyGroup: (name: string) => void
  showCc: boolean
  setShowCc: (show: boolean) => void
  showBcc: boolean
  setShowBcc: (show: boolean) => void
  showPriority: boolean
  setShowPriority: (show: boolean) => void
  priority: MailPriority
  changePriority: (priority: MailPriority) => void
  subject: string
  changeSubject: (subject: string) => void
}

/** The composer's header fields — From, To/Cc/Bcc, priority, subject — or their folded summary. */
export default function ComposeFields({
  folded, onUnfold, identity, identityList, effectiveFrom, changeFrom, to, changeTo, autoFocusTo,
  cc, changeCc, bcc, changeBcc, contacts, groupOptions, notifyEmptyGroup, showCc, setShowCc,
  showBcc, setShowBcc, showPriority, setShowPriority, priority, changePriority, subject, changeSubject,
}: Props) {
  const { t } = useTranslation('compose')
  const recipientNames = useMemo(() => namesByAddressOf(contacts ?? []), [contacts])
  const nameOf = (token: string) => recipientNames.get(canonicalAddress(token)) ?? token
  // The sender is named only where it can be wrong: `IdentitySelect` shows a menu on exactly this
  // condition, so the summary and the field cannot disagree about whether there is a choice.
  const choosableFrom = usableIdentities(identityList).length > 1
  const summary = [
    ...(choosableFrom && effectiveFrom ? [`${t('fields.from')} : ${effectiveFrom}`] : []),
    // Send is disabled without a recipient, so a folded line that stayed silent would leave a dead
    // button and no visible reason for it.
    to.length ? `${t('fields.to')} : ${to.map(nameOf).join(', ')}` : t('fields.noRecipient'),
    subject || t('fields.noSubject'),
  ].join(' · ')

  // Unmounted while folded, never hidden: hidden fields keep their place in the tab order. Nothing
  // is lost: the caret reaching the body already blurred the recipients and committed their token.
  return (
    <div className="compose-fields">
      {folded ? (
        <button
          type="button"
          className="compose-summary"
          aria-expanded={false}
          aria-label={t('fields.showFields')}
          onClick={onUnfold}
        >
          <span className="compose-summary-text">{summary}</span>
          <ChevronDownIcon size={14} />
        </button>
      ) : (
      <>
      <div className="compose-from">
        <span className="compose-from-label">{t('fields.from')}</span>
        {effectiveFrom ? (
          // The whole list, not the usable one: the select keeps stale rows out of the menu
          // itself, and needs them to name a choice that went stale under the composer.
          <IdentitySelect identities={identityList ?? []} value={effectiveFrom} onChange={changeFrom} />
        ) : (
          <span className="compose-from-value">
            {identity ? `${identity.displayName} (${identity.email})` : ''}
          </span>
        )}
      </div>
      <div className="compose-to-row">
        <RecipientsField id="compose-to" label={t('fields.to')} tokens={to} onChange={changeTo}
          autoFocus={autoFocusTo} contacts={contacts}
          groups={groupOptions} onEmptyGroup={notifyEmptyGroup} />
        <span className="compose-cc-links">
          {!showCc && <button type="button" className="compose-link-btn" onClick={() => setShowCc(true)}>{t('fields.cc')}</button>}
          {!showBcc && <button type="button" className="compose-link-btn" onClick={() => setShowBcc(true)}>{t('fields.bcc')}</button>}
          {!showPriority && (
            <button type="button" className="compose-link-btn" onClick={() => setShowPriority(true)}>{t('fields.priority')}</button>
          )}
        </span>
      </div>
      {showCc && <RecipientsField id="compose-cc" label={t('fields.cc')} tokens={cc} onChange={changeCc}
        contacts={contacts} groups={groupOptions} onEmptyGroup={notifyEmptyGroup} />}
      {showBcc && <RecipientsField id="compose-bcc" label={t('fields.bcc')} tokens={bcc} onChange={changeBcc}
        contacts={contacts} groups={groupOptions} onEmptyGroup={notifyEmptyGroup} />}
      {/* Stays open while the value is not Normal: folding it would take a live setting off the
          screen while it kept riding on the message. Cc and Bcc are safe to fold — their tokens
          stay visible either way. */}
      {(showPriority || priority !== 'normal') && (
        <div className="compose-priority">
          <span className="compose-priority-label">{t('fields.priority')}</span>
          <DropdownMenu
            ariaLabel={t('fields.priority')}
            className="compose-priority-select"
            align="left"
            // priorityLabel falls back to Normal, so an out-of-union value cannot blank the app.
            trigger={<>{priorityLabel(priority, t)} <ChevronDownIcon size={13} /></>}
            items={PRIORITIES.map(p => ({ label: priorityLabel(p, t), onSelect: () => changePriority(p) }))}
          />
        </div>
      )}
      <div className="field-h">
        <label htmlFor="compose-subject">{t('fields.subject')}</label>
        <input id="compose-subject" type="text" value={subject} onChange={e => changeSubject(e.target.value)} />
      </div>
      </>
      )}
    </div>
  )
}
