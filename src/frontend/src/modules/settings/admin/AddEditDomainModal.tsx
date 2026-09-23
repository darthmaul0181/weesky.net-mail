import { useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import Modal from '../../../components/Modal'
import PencilIcon from '../../../icons/PencilIcon'
import GlobeIcon from '../../../icons/GlobeIcon'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import type { AdminDomain } from './adminTypes'
import { useCreateAdminDomain, useUpdateAdminDomain } from './useAdminLists'

export const DOMAIN_RE = /^([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}$/

interface Props {
  /** The domain being edited; absent, the dialog creates one. */
  domain?: AdminDomain | null
  onSave: () => void
  onClose: () => void
}

export function AddEditDomainModal({ domain, onSave, onClose }: Props) {
  const { t } = useTranslation('admin')
  const uid = useId()
  const [id, setId] = useState(domain?.id ?? '')
  const [name, setName] = useState(domain?.name ?? '')
  const [error, setError] = useState<string | null>(null)
  const createDomain = useCreateAdminDomain()
  const updateDomain = useUpdateAdminDomain()
  const loading = createDomain.isPending || updateDomain.isPending
  const isEdit = !!domain
  const nameValid = DOMAIN_RE.test(name)

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    setError(null)
    try {
      if (domain) {
        await updateDomain.mutateAsync({ id: domain.id, domain: { id, name } })
      } else {
        await createDomain.mutateAsync({ id, name })
      }
      onSave()
    } catch (err) {
      setError(apiErrorMessage(err, t('errorOccurred')))
    }
  }

  return (
    <Modal icon={isEdit ? <PencilIcon /> : <GlobeIcon />}
      title={t(isEdit ? 'domains.editTitle' : 'domains.addTitle')}
      onClose={onClose} onSubmit={e => void handleSubmit(e)} busy={loading}>
      {error && <div className="alert alert-error" role="alert">{error}</div>}
      <div className="field">
        <label htmlFor={`${uid}-domain-id`}>{t('domains.id')}</label>
        <input id={`${uid}-domain-id`} type="text" value={id} onChange={e => setId(e.target.value.toUpperCase())}
          maxLength={3} disabled={isEdit} required />
      </div>
      <div className="field">
        <label htmlFor={`${uid}-domain-name`}>{t('domains.name')}</label>
        <input id={`${uid}-domain-name`} type="text" value={name} onChange={e => setName(e.target.value)} required
          className={name && !nameValid ? 'is-error' : undefined} />
      </div>
      <button className="btn btn-primary" type="submit"
        disabled={loading || !id.trim() || !nameValid}>
        {loading
          ? <span className="spinner" />
          : (isEdit ? t('actions.saveChanges', { ns: 'common' }) : t('domains.create'))}
      </button>
    </Modal>
  )
}

export default AddEditDomainModal
