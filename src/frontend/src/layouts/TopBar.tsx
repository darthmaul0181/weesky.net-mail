import logoCircle from '../assets/logo_circle.jpg'
import wordmark from '../assets/weesky_net.png'
import AvatarMenu from './AvatarMenu'

export default function TopBar() {
  return (
    <header className="app-topbar">
      <div className="topbar-brand">
        <img src={logoCircle} alt="" className="topbar-logo" />
        <img src={wordmark} alt="weesky.net" className="topbar-wordmark" />
      </div>
      <AvatarMenu />
    </header>
  )
}
