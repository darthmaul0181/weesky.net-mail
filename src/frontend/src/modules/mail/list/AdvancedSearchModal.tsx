import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import SearchIcon from '../../../icons/SearchIcon'
import { criteriaFromForm, daysSinceYearStart } from './searchCriteria'
import type { AdvancedForm } from './searchCriteria'

interface Props {
  folderTitle: string
  initialSubject: string
  onSearch: (form: AdvancedForm) => void
  onClose: () => void
}

/** The advanced-search popup. Filled fields combine with AND; scope widens to the whole box. */
export default function AdvancedSearchModal({ folderTitle, initialSubject, onSearch, onClose }: Props) {
  const { t } = useTranslation('mail')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [subject, setSubject] = useState(initialSubject)
  const [text, setText] = useState('')
  const [date, setDate] = useState('')
  const [unread, setUnread] = useState(false)
  const [flagged, setFlagged] = useState(false)
  const [hasAttachment, setHasAttachment] = useState(false)
  const [scope, setScope] = useState<'this' | 'all'>('this')

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onClose])

  const form: AdvancedForm = {
    from, to, subject, text,
    sinceDays: date === '' ? null : date === 'year' ? daysSinceYearStart(new Date()) : Number(date),
    unread, flagged, hasAttachment,
    allFolders: scope === 'all',
  }
  // Emptiness is decided by the same rule the criteria builder uses (folderPath is irrelevant to it).
  const empty = criteriaFromForm('', form) === null

  function submit(event: FormEvent) {
    event.preventDefault()
    if (empty) return
    onSearch(form)
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">{t('search.advanced.title')}</span>
          <button className="modal-close" aria-label={t('actions.close', { ns: 'common' })} onClick={onClose}>✕</button>
        </div>

        <form onSubmit={submit}>
          <div className="field-h">
            <label htmlFor="adv-from">{t('search.advanced.from')}</label>
            <input id="adv-from" type="text" value={from} autoFocus onChange={e => setFrom(e.target.value)} />
          </div>
          <div className="field-h">
            <label htmlFor="adv-to">{t('search.advanced.to')}</label>
            <input id="adv-to" type="text" value={to} onChange={e => setTo(e.target.value)} />
          </div>
          <div className="field-h">
            <label htmlFor="adv-subject">{t('search.advanced.subject')}</label>
            <input id="adv-subject" type="text" value={subject} onChange={e => setSubject(e.target.value)} />
          </div>
          <div className="field-h">
            <label htmlFor="adv-body">{t('search.advanced.body')}</label>
            <input id="adv-body" type="text" value={text} onChange={e => setText(e.target.value)} />
          </div>
          <div className="field-h">
            <label htmlFor="adv-date">{t('search.advanced.date')}</label>
            <select id="adv-date" value={date} onChange={e => setDate(e.target.value)}>
              <option value="">{t('search.advanced.allTime')}</option>
              <option value="7">{t('search.advanced.last7')}</option>
              <option value="14">{t('search.advanced.last14')}</option>
              <option value="30">{t('search.advanced.last30')}</option>
              <option value="90">{t('search.advanced.last90')}</option>
              <option value="180">{t('search.advanced.last180')}</option>
              <option value="year">{t('search.advanced.thisYear')}</option>
            </select>
          </div>
          <div className="field-h">
            <label htmlFor="adv-scope">{t('search.advanced.scope')}</label>
            <select id="adv-scope" value={scope} onChange={e => setScope(e.target.value as 'this' | 'all')}>
              <option value="this">{t('search.advanced.thisFolder', { folder: folderTitle })}</option>
              <option value="all">{t('search.advanced.allFolders')}</option>
            </select>
          </div>

          <div className="advanced-search-checks">
            <label>
              <input type="checkbox" checked={unread} onChange={e => setUnread(e.target.checked)} />
              {t('search.advanced.unread')}
            </label>
            <label>
              <input type="checkbox" checked={flagged} onChange={e => setFlagged(e.target.checked)} />
              {t('search.advanced.starred')}
            </label>
            <label>
              <input type="checkbox" checked={hasAttachment} onChange={e => setHasAttachment(e.target.checked)} />
              {t('search.advanced.hasAttachment')}
            </label>
          </div>

          <div className="folder-pick-submit">
            <button type="submit" className="btn btn-primary" disabled={empty}>
              <SearchIcon size={15} /> {t('search.advanced.submit')}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
