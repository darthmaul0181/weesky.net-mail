import { useState, type RefObject } from 'react'
import { useTranslation } from 'react-i18next'
import i18next from 'i18next'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal'
import LoadingBlock from '../../../components/LoadingBlock'
import GlobeIcon from '../../../icons/GlobeIcon'
import PencilIcon from '../../../icons/PencilIcon'
import TrashIcon from '../../../icons/TrashIcon'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import ExternalDomainDialog from './ExternalDomainDialog'
import ListLoadFailed from '../../../components/ListLoadFailed'
import { useListLoad } from './useAdminLists'
import { useDeleteExternalDomain, useExternalDomains, type ExternalDomain } from './useExternalDomains'
import type { AddToast } from '../../../hooks/useToasts'

interface Props {
  addToast: AddToast
  /** The tab content, where focus goes when a confirmed delete takes the row's own button. */
  returnFocusRef?: RefObject<HTMLElement | null>
}

/** The admin-curated external providers a connected account may be attached from, the product's
 * only source of external endpoints. Name-only tiles: every detail lives in the dialog. */
export default function ExternalDomainsTab({ addToast, returnFocusRef }: Props) {
  const { t } = useTranslation('admin')
  const domainsQuery = useExternalDomains()
  const domains = domainsQuery.data
  const deleteDomain = useDeleteExternalDomain()
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState<ExternalDomain | null>(null)
  const [deleting, setDeleting] = useState<ExternalDomain | null>(null)

  const firstLoad = useListLoad(domainsQuery, addToast, i18next.t('admin:external.loadFailed'))

  if (firstLoad) return <LoadingBlock />
  if (!domains) return <ListLoadFailed>{t('external.loadFailedBody')}</ListLoadFailed>

  async function confirmDelete() {
    if (!deleting) return
    try {
      await deleteDomain.mutateAsync(deleting.id)
      addToast(t('external.deleted', { name: deleting.name }))
      setDeleting(null)
    } catch (err) {
      addToast(apiErrorMessage(err, t('external.deleteFailed')), 'error')
    }
  }

  return (
    <div>
      <div className="admin-list-header">
        <span className="admin-list-title">{t('external.title', { total: domains.length })}</span>
        <button type="button" className="btn btn-primary"
          style={{ width: 'auto', display: 'inline-flex', alignItems: 'center', gap: '6px' }}
          onClick={() => setAdding(true)}>
          <GlobeIcon /> {t('actions.add', { ns: 'common' })}
        </button>
      </div>
      {domains.length === 0
        ? <p className="settings-note">{t('external.empty')}</p>
        : (
          <div className="admin-list">
            {domains.map(domain => (
              <div key={domain.id} className="admin-list-item">
                <span className="admin-list-item-name">{domain.name}</span>
                {domain.authMode === 'OAuth2' && <span className="row-tag">OAuth</span>}
                <div className="admin-list-item-actions">
                  <button type="button" className="admin-icon-btn" title={t('actions.edit', { ns: 'common' })}
                    onClick={() => setEditing(domain)}>
                    <PencilIcon />
                  </button>
                  <button type="button" className="admin-icon-btn is-danger"
                    title={t('actions.delete', { ns: 'common' })}
                    onClick={() => setDeleting(domain)}>
                    <TrashIcon />
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}

      {adding && (
        <ExternalDomainDialog
          onSave={() => { setAdding(false); addToast(t('external.created')) }}
          onClose={() => setAdding(false)}
        />
      )}
      {editing && (
        <ExternalDomainDialog
          domain={editing}
          onSave={() => { setEditing(null); addToast(t('external.updated')) }}
          onClose={() => setEditing(null)}
        />
      )}
      {deleting && (
        <DeleteConfirmModal entityLabel={deleting.name} loading={deleteDomain.isPending}
          onConfirm={() => void confirmDelete()} onClose={() => setDeleting(null)}
          returnFocusRef={returnFocusRef} />
      )}
    </div>
  )
}
