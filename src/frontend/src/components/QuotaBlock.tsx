import type { TFunction } from 'i18next'
import { useTranslation } from 'react-i18next'
import type { Quota } from '../types/account'

const MB = 1024 * 1024
const GB = 1024 * MB

interface QuotaProps {
  quota: Quota | null
}

/** The figures QuotaBlock and QuotaMini both draw — used/total in whichever unit fits,
    the percentage and the danger/warn threshold class. One computation, two renderings. */
function quotaFigures(quota: Quota, t: TFunction<'common'>) {
  const useGb = Math.max(quota.storageBytesUsed, quota.storageBytesLimit) >= GB
  const divisor = useGb ? GB : MB
  const used = quota.storageBytesUsed / divisor
  const total = quota.storageBytesLimit / divisor
  const percent = Math.min(100, Math.max(0, (quota.storageBytesUsed / quota.storageBytesLimit) * 100))
  const format = (v: number) => (v >= 100 ? v.toFixed(0) : v.toFixed(1))
  const size = (v: number) => (useGb
    ? t('sizes.gb', { value: format(v) })
    : t('sizes.mb', { value: format(v) }))
  const levelClass = percent >= 90 ? 'is-danger' : percent >= 75 ? 'is-warn' : ''
  return { used, total, percent, size, format, levelClass }
}

export default function QuotaBlock({ quota }: QuotaProps) {
  const { t } = useTranslation()
  if (!quota || !quota.storageBytesLimit) return null

  const { used, total, percent, size, levelClass } = quotaFigures(quota, t)

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

export function QuotaMini({ quota }: QuotaProps) {
  const { t } = useTranslation()
  if (!quota || !quota.storageBytesLimit) return <span style={{ fontSize: '12px', color: 'var(--text-muted)' }}>—</span>

  const { used, total, percent, size, format, levelClass } = quotaFigures(quota, t)

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
