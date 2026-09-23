import logoCircle from '../assets/logo-192.png'
import { PRODUCT } from '../lib/product'

/** Brand only. The product name is text, so it takes the palette's foregrounds; the administrator's
 * application name names the tab and the installed app, never this. */
export default function TopBar() {
  return (
    <header className="app-topbar">
      <div className="topbar-brand">
        <img src={logoCircle} alt="" className="topbar-logo" />
        <span className="topbar-name">
          {PRODUCT.name} <span className="topbar-name-kind">{PRODUCT.kind}</span>
        </span>
      </div>
    </header>
  )
}
