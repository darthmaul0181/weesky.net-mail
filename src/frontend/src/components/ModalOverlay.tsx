import { useRef, type HTMLAttributes, type MouseEvent, type ReactNode } from 'react'

type Props = {
  onClose: () => void
  className?: string
  children: ReactNode
} & Omit<HTMLAttributes<HTMLDivElement>, 'onClick' | 'onMouseDown' | 'onMouseUp' | 'role'>

// A click must both start and end on the backdrop to close the dialog — either half landing
// inside the dialog (a text selection or a drag that overruns its edge) must not close it.
// A dialog taller than the window scrolls on the overlay, so its own scrollbar also satisfies
// `target === currentTarget` — offsetX/offsetY past the content box is what tells a scrollbar
// press apart from a real backdrop one, and is a no-op wherever there is no scrollbar to press.
function isBackdropPress(event: MouseEvent<HTMLDivElement>): boolean {
  if (event.target !== event.currentTarget) return false
  const { clientWidth, clientHeight } = event.currentTarget
  const { offsetX, offsetY } = event.nativeEvent
  return offsetX <= clientWidth && offsetY <= clientHeight
}

export default function ModalOverlay({ onClose, className, children, ...rest }: Props) {
  const pressedOnBackdrop = useRef(false)
  return (
    <div {...rest} role="presentation" className={className ? `modal-overlay ${className}` : 'modal-overlay'}
      onMouseDown={event => { pressedOnBackdrop.current = isBackdropPress(event) }}
      onMouseUp={event => { if (event.target !== event.currentTarget) pressedOnBackdrop.current = false }}
      onClick={event => {
        if (pressedOnBackdrop.current && isBackdropPress(event)) onClose()
        pressedOnBackdrop.current = false
      }}>
      {children}
    </div>
  )
}
