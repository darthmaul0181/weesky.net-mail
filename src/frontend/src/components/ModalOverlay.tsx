import { useRef, type HTMLAttributes, type ReactNode } from 'react'

type Props = {
  onClose: () => void
  className?: string
  children: ReactNode
} & Omit<HTMLAttributes<HTMLDivElement>, 'onClick' | 'onMouseDown' | 'onMouseUp' | 'role'>

// A click must both start and end on the backdrop to close the dialog — either half landing
// inside the dialog (a text selection or a drag that overruns its edge) must not close it.
export default function ModalOverlay({ onClose, className, children, ...rest }: Props) {
  const pressedOnBackdrop = useRef(false)
  return (
    <div {...rest} role="presentation" className={className ? `modal-overlay ${className}` : 'modal-overlay'}
      onMouseDown={event => { pressedOnBackdrop.current = event.target === event.currentTarget }}
      onMouseUp={event => { if (event.target !== event.currentTarget) pressedOnBackdrop.current = false }}
      onClick={event => {
        if (pressedOnBackdrop.current && event.target === event.currentTarget) onClose()
        pressedOnBackdrop.current = false
      }}>
      {children}
    </div>
  )
}
