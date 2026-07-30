import { describe, it, expect } from 'vitest'
import { render } from '@testing-library/react'
import MailIcon from './MailIcon'
import GearIcon from './GearIcon'
import FolderIcon from './FolderIcon'
import ChevronRightIcon from './ChevronRightIcon'
import PaperclipIcon from './PaperclipIcon'
import RefreshIcon from './RefreshIcon'
import FolderPlusIcon from './FolderPlusIcon'
import SlidersIcon from './SlidersIcon'
import SunIcon from './SunIcon'
import MoonIcon from './MoonIcon'
import KebabIcon from './KebabIcon'
import ExternalLinkIcon from './ExternalLinkIcon'
import StarIcon from './StarIcon'
import MailOpenIcon from './MailOpenIcon'
import ArchiveIcon from './ArchiveIcon'
import FolderMoveIcon from './FolderMoveIcon'
import CopyIcon from './CopyIcon'
import JunkIcon from './JunkIcon'
import ShieldCheckIcon from './ShieldCheckIcon'
import ShieldAlertIcon from './ShieldAlertIcon'
import RocketIcon from './RocketIcon'
import SignOutIcon from './SignOutIcon'
import KeyIcon from './KeyIcon'
import UserIcon from './UserIcon'
import DropletIcon from './DropletIcon'
import AtSignIcon from './AtSignIcon'
import FunnelIcon from './FunnelIcon'
import PersonPlusIcon from './PersonPlusIcon'
import ShieldIcon from './ShieldIcon'
import CodeIcon from './CodeIcon'

const icons = [
  { name: 'MailIcon', Icon: MailIcon, defaultSize: '20' },
  { name: 'GearIcon', Icon: GearIcon, defaultSize: '20' },
  { name: 'FolderIcon', Icon: FolderIcon, defaultSize: '16' },
  { name: 'ChevronRightIcon', Icon: ChevronRightIcon, defaultSize: '14' },
  { name: 'PaperclipIcon', Icon: PaperclipIcon, defaultSize: '14' },
  { name: 'RefreshIcon', Icon: RefreshIcon, defaultSize: '16' },
  { name: 'FolderPlusIcon', Icon: FolderPlusIcon, defaultSize: '16' },
  { name: 'SlidersIcon', Icon: SlidersIcon, defaultSize: '16' },
  { name: 'SunIcon', Icon: SunIcon, defaultSize: '16' },
  { name: 'MoonIcon', Icon: MoonIcon, defaultSize: '16' },
  { name: 'KebabIcon', Icon: KebabIcon, defaultSize: '16' },
  { name: 'ExternalLinkIcon', Icon: ExternalLinkIcon, defaultSize: '11' },
  { name: 'StarIcon', Icon: StarIcon, defaultSize: '16' },
  { name: 'MailOpenIcon', Icon: MailOpenIcon, defaultSize: '16' },
  { name: 'ArchiveIcon', Icon: ArchiveIcon, defaultSize: '16' },
  { name: 'FolderMoveIcon', Icon: FolderMoveIcon, defaultSize: '16' },
  { name: 'CopyIcon', Icon: CopyIcon, defaultSize: '16' },
  { name: 'JunkIcon', Icon: JunkIcon, defaultSize: '16' },
  { name: 'ShieldCheckIcon', Icon: ShieldCheckIcon, defaultSize: '16' },
  { name: 'ShieldAlertIcon', Icon: ShieldAlertIcon, defaultSize: '16' },
  { name: 'RocketIcon', Icon: RocketIcon, defaultSize: '15' },
  { name: 'SignOutIcon', Icon: SignOutIcon, defaultSize: '15' },
  { name: 'KeyIcon', Icon: KeyIcon, defaultSize: '16' },
  { name: 'UserIcon', Icon: UserIcon, defaultSize: '16' },
  { name: 'DropletIcon', Icon: DropletIcon, defaultSize: '16' },
  { name: 'AtSignIcon', Icon: AtSignIcon, defaultSize: '16' },
  { name: 'FunnelIcon', Icon: FunnelIcon, defaultSize: '16' },
  { name: 'PersonPlusIcon', Icon: PersonPlusIcon, defaultSize: '15' },
  { name: 'ShieldIcon', Icon: ShieldIcon, defaultSize: '15' },
  { name: 'CodeIcon', Icon: CodeIcon, defaultSize: '16' },
]

describe('icons', () => {
  it.each(icons)('$name renders at its default size', ({ Icon, defaultSize }) => {
    const { container } = render(<Icon />)
    const svg = container.querySelector('svg')

    expect(svg).toHaveAttribute('width', defaultSize)
    expect(svg).toHaveAttribute('height', defaultSize)
  })

  it.each(icons)('$name accepts a size override', ({ Icon }) => {
    const { container } = render(<Icon size={11} />)
    const svg = container.querySelector('svg')

    expect(svg).toHaveAttribute('width', '11')
    expect(svg).toHaveAttribute('height', '11')
  })

  it.each(icons)('$name inherits colour from the surrounding text', ({ Icon }) => {
    const { container } = render(<Icon />)

    expect(container.querySelector('svg')).toHaveAttribute('stroke', 'currentColor')
  })

  it('StarIcon is unfilled by default', () => {
    const { container } = render(<StarIcon />)

    expect(container.querySelector('svg')).toHaveAttribute('fill', 'none')
  })

  it('StarIcon fills with currentColor when filled', () => {
    const { container } = render(<StarIcon filled />)

    expect(container.querySelector('svg')).toHaveAttribute('fill', 'currentColor')
  })
})
