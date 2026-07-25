import logoCircle from '../assets/logo-192.png'
import wordmark from '../assets/weesky_net.png'

/** Brand only: the account block moved to the foot of the folder column (IdentityMenu). */
export default function TopBar() {
  return (
    <header className="app-topbar">
      <div className="topbar-brand">
        <img src={logoCircle} alt="" className="topbar-logo" />
        <img src={wordmark} alt="weesky.net" className="topbar-wordmark" />
      </div>
    </header>
  )
}
