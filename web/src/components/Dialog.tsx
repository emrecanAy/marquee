import { type ReactNode, useEffect, useRef } from 'react'
import './Dialog.css'

/** A modal built on <dialog>: focus is trapped, Escape closes it, the page behind is inert. */
export function Dialog({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  const ref = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const dialog = ref.current
    dialog?.showModal()
    return () => dialog?.close()
  }, [])

  return (
    <dialog ref={ref} className="dialog" aria-label={title} onCancel={onClose}>
      <header className="dialog__header">
        <h2>{title}</h2>
        <button type="button" className="dialog__close" aria-label="Close" onClick={onClose}>
          ×
        </button>
      </header>
      {children}
    </dialog>
  )
}
