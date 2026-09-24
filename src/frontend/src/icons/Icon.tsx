import type { ReactNode, SVGProps } from 'react'

type IconProps = Omit<SVGProps<SVGSVGElement>, 'width' | 'height' | 'title' | 'children'> & {
  size: number
  title?: string
  children: ReactNode
}

// `role`/`aria-label`/`aria-hidden` follow `title`: an icon naming its own meaning is announced,
// one that doesn't stays out of assistive tech and the tab order (`focusable="false"`).
export default function Icon({
  size, viewBox = '0 0 20 20', title, fill = 'none', stroke = 'currentColor', strokeWidth, children, ...rest
}: IconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox={viewBox}
      fill={fill}
      stroke={stroke}
      strokeWidth={strokeWidth}
      role={title ? 'img' : undefined}
      aria-label={title}
      aria-hidden={title ? undefined : true}
      focusable="false"
      {...rest}
    >
      {children}
    </svg>
  )
}
