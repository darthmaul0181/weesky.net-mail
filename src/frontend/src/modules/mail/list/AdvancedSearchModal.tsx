import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
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
      <div className="modal" style={{ maxWidth: '640px' }} onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">Advanced search</span>
          <button className="modal-close" aria-label="Close" onClick={onClose}>✕</button>
        </div>

        <form onSubmit={submit}>
          <div className="field-h">
            <label htmlFor="adv-from">From</label>
            <input id="adv-from" type="text" value={from} autoFocus onChange={e => setFrom(e.target.value)} />
          </div>
          <div className="field-h">
            <label htmlFor="adv-to">To</label>
            <input id="adv-to" type="text" value={to} onChange={e => setTo(e.target.value)} />
          </div>
          <div className="field-h">
            <label htmlFor="adv-subject">Subject</label>
            <input id="adv-subject" type="text" value={subject} onChange={e => setSubject(e.target.value)} />
          </div>
          <div className="field-h">
            <label htmlFor="adv-body">Body</label>
            <input id="adv-body" type="text" value={text} onChange={e => setText(e.target.value)} />
          </div>
          <div className="field-h">
            <label htmlFor="adv-date">Date</label>
            <select id="adv-date" value={date} onChange={e => setDate(e.target.value)}>
              <option value="">All time</option>
              <option value="7">Last 7 days</option>
              <option value="14">Last two weeks</option>
              <option value="30">Last 30 days</option>
              <option value="90">Last 3 months</option>
              <option value="180">Last 6 months</option>
              <option value="year">This year</option>
            </select>
          </div>
          <div className="field-h">
            <label htmlFor="adv-scope">Search in</label>
            <select id="adv-scope" value={scope} onChange={e => setScope(e.target.value as 'this' | 'all')}>
              <option value="this">This folder ({folderTitle})</option>
              <option value="all">All folders</option>
            </select>
          </div>

          <div className="advanced-search-checks">
            <label>
              <input type="checkbox" checked={unread} onChange={e => setUnread(e.target.checked)} />
              Unread
            </label>
            <label>
              <input type="checkbox" checked={flagged} onChange={e => setFlagged(e.target.checked)} />
              Starred
            </label>
            <label>
              <input type="checkbox" checked={hasAttachment} onChange={e => setHasAttachment(e.target.checked)} />
              Has attachment
            </label>
          </div>

          <div className="folder-pick-actions">
            <button type="submit" className="btn btn-primary" style={{ width: 'auto' }} disabled={empty}>
              <SearchIcon size={15} /> Search
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
