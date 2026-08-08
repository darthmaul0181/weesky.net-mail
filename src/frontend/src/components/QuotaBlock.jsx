import { useTranslation } from 'react-i18next'

const MB = 1024 * 1024
const GB = 1024 * MB

export function QuotaBlock({ quota }) {
  const { t } = useTranslation()
  if (!quota || !quota.storageBytesLimit) return null

  const useGb = Math.max(quota.storageBytesUsed, quota.storageBytesLimit) >= GB
  const divisor = useGb ? GB : MB
  const used = quota.storageBytesUsed / divisor
  const total = quota.storageBytesLimit / divisor
  const percent = Math.min(100, Math.max(0, (quota.storageBytesUsed / quota.storageBytesLimit) * 100))
  const format = v => (v >= 100 ? v.toFixed(0) : v.toFixed(1))
  const size = v => (useGb
    ? t('sizes.gb', { value: format(v) })
    : t('sizes.mb', { value: format(v) }))
  const levelClass = percent >= 90 ? 'is-danger' : percent >= 75 ? 'is-warn' : ''

  return (
    // No heading of its own: the only consumer already puts one above it, and the block
    // printed a second "Storage" right under it.
    <div className="panel-quota">
      <div className="panel-quota-values">
        <span className="panel-quota-used">{size(used)}</span>
        <span className="panel-quota-sep"> / </span>
        <span className="panel-quota-total">{size(total)}</span>
        <span className="panel-quota-percent">{percent.toFixed(0)}%</span>
      </div>
      <div className={`panel-quota-bar ${levelClass}`}>
        <div className="panel-quota-bar-fill" style={{ width: `${percent}%` }} />
      </div>
    </div>
  )
}

export function QuotaMini({ quota }) {
  const { t } = useTranslation()
  if (!quota || !quota.storageBytesLimit) return <span style={{ fontSize: '12px', color: 'var(--text-muted)' }}>—</span>

  const useGb = Math.max(quota.storageBytesUsed, quota.storageBytesLimit) >= GB
  const divisor = useGb ? GB : MB
  const used = quota.storageBytesUsed / divisor
  const total = quota.storageBytesLimit / divisor
  const percent = Math.min(100, Math.max(0, (quota.storageBytesUsed / quota.storageBytesLimit) * 100))
  const format = v => (v >= 100 ? v.toFixed(0) : v.toFixed(1))
  const size = v => (useGb
    ? t('sizes.gb', { value: format(v) })
    : t('sizes.mb', { value: format(v) }))
  const levelClass = percent >= 90 ? 'is-danger' : percent >= 75 ? 'is-warn' : ''

  return (
    <div style={{ width: '145px' }}>
      <div style={{ fontSize: '11px', color: 'var(--text-muted)', marginBottom: '4px' }}>
        {format(used)} / {size(total)}
      </div>
      <div className={`panel-quota-bar ${levelClass}`}>
        <div className="panel-quota-bar-fill" style={{ width: `${percent}%` }} />
      </div>
    </div>
  )
}

export default QuotaBlock
