const MB = 1024 * 1024
const GB = 1024 * MB

export function QuotaBlock({ quota }) {
  if (!quota || !quota.storageBytesLimit) return null

  const useGb = Math.max(quota.storageBytesUsed, quota.storageBytesLimit) >= GB
  const divisor = useGb ? GB : MB
  const unit = useGb ? 'GB' : 'MB'
  const used = quota.storageBytesUsed / divisor
  const total = quota.storageBytesLimit / divisor
  const percent = Math.min(100, Math.max(0, (quota.storageBytesUsed / quota.storageBytesLimit) * 100))
  const format = v => (v >= 100 ? v.toFixed(0) : v.toFixed(1))
  const levelClass = percent >= 90 ? 'is-danger' : percent >= 75 ? 'is-warn' : ''

  return (
    // No heading of its own: the only consumer already puts one above it, and the block
    // printed a second "Storage" right under it.
    <div className="panel-quota">
      <div className="panel-quota-values">
        <span className="panel-quota-used">{format(used)} {unit}</span>
        <span className="panel-quota-sep"> / </span>
        <span className="panel-quota-total">{format(total)} {unit}</span>
        <span className="panel-quota-percent">{percent.toFixed(0)}%</span>
      </div>
      <div className={`panel-quota-bar ${levelClass}`}>
        <div className="panel-quota-bar-fill" style={{ width: `${percent}%` }} />
      </div>
    </div>
  )
}

export function QuotaMini({ quota }) {
  if (!quota || !quota.storageBytesLimit) return <span style={{ fontSize: '12px', color: 'var(--text-muted)' }}>—</span>

  const useGb = Math.max(quota.storageBytesUsed, quota.storageBytesLimit) >= GB
  const divisor = useGb ? GB : MB
  const unit = useGb ? 'GB' : 'MB'
  const used = quota.storageBytesUsed / divisor
  const total = quota.storageBytesLimit / divisor
  const percent = Math.min(100, Math.max(0, (quota.storageBytesUsed / quota.storageBytesLimit) * 100))
  const format = v => (v >= 100 ? v.toFixed(0) : v.toFixed(1))
  const levelClass = percent >= 90 ? 'is-danger' : percent >= 75 ? 'is-warn' : ''

  return (
    <div style={{ width: '145px' }}>
      <div style={{ fontSize: '11px', color: 'var(--text-muted)', marginBottom: '4px' }}>
        {format(used)} / {format(total)} {unit}
      </div>
      <div className={`panel-quota-bar ${levelClass}`}>
        <div className="panel-quota-bar-fill" style={{ width: `${percent}%` }} />
      </div>
    </div>
  )
}

export default QuotaBlock
