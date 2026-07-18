import { Link } from 'react-router-dom'

export default function ComingSoon({ module }: { module: string }) {
  return (
    <div className="coming-soon">
      <h1>{module}</h1>
      <p>This module is coming soon.</p>
      {module === 'Mail' && (
        <p className="coming-soon-links">
          In the meantime: <Link to="/settings/aliases">Aliases</Link>
          {' · '}
          <Link to="/settings/rules">Rules</Link>
        </p>
      )}
    </div>
  )
}
