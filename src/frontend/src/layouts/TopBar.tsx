import { useAppLogo } from '../hooks/useAppLogo'
import { PRODUCT } from '../lib/product'
import WhatsNewButton from '../modules/whatsnew/WhatsNewButton'

/** The brand, and the novelties at the far right; the logo is the administrator's. The product name
 * is text, so it takes the palette's foregrounds; the administrator's application name names the tab
 * and the installed app, never this. */
export default function TopBar() {
  const logo = useAppLogo()
  return (
    <header className="app-topbar">
      <div className="topbar-brand">
        <img src={logo[192]} alt="" className="topbar-logo" />
        <span className="topbar-name">
          {PRODUCT.name} <span className="topbar-name-kind">{PRODUCT.kind}</span>
        </span>
      </div>
      <WhatsNewButton />
    </header>
  )
}
