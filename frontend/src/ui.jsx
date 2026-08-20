import { useEffect, useRef, useState } from 'react'

// In-app replacement for the browser's native prompt()/confirm()/alert() dialogs,
// which render as ugly OS boxes. These return Promises and render inside our own
// themed modal. Mount <DialogHost/> once (in App); call ui.prompt/confirm/alert
// from anywhere.
const listeners = new Set()
const emit = (dialog) => listeners.forEach((l) => l(dialog))
const norm = (o) => (typeof o === 'string' ? { message: o } : (o || {}))

export const ui = {
  // Resolves to the entered string, or null if cancelled.
  prompt: (opts) => new Promise((resolve) => emit({ kind: 'prompt', ...norm(opts), resolve })),
  // Resolves to true / false.
  confirm: (opts) => new Promise((resolve) => emit({ kind: 'confirm', ...norm(opts), resolve })),
  // Resolves when dismissed.
  alert: (opts) => new Promise((resolve) => emit({ kind: 'alert', ...norm(opts), resolve })),
}

export function DialogHost() {
  const [d, setD] = useState(null)
  const [val, setVal] = useState('')
  const inputRef = useRef(null)

  useEffect(() => {
    const l = (dialog) => { setD(dialog); setVal(dialog.defaultValue ?? '') }
    listeners.add(l)
    return () => listeners.delete(l)
  }, [])

  useEffect(() => { if (d?.kind === 'prompt') setTimeout(() => inputRef.current?.focus(), 30) }, [d])

  if (!d) return null

  const done = (result) => { d.resolve(result); setD(null) }
  const onCancel = () => done(d.kind === 'confirm' ? false : null)
  const onOk = () => {
    if (d.kind === 'prompt') { const v = val.trim(); if (d.required && !v) return; done(v || null) }
    else if (d.kind === 'confirm') done(true)
    else done(undefined)
  }
  const onKey = (e) => { if (e.key === 'Enter') { e.preventDefault(); onOk() } if (e.key === 'Escape') onCancel() }

  return (
    <div className="overlay" onMouseDown={(e) => { if (e.target.classList.contains('overlay')) onCancel() }}>
      <div className="modal" style={{ maxWidth: 400 }} onKeyDown={onKey}>
        {d.title && <div className="big" style={{ marginBottom: 6 }}>{d.title}</div>}
        {d.message && <div style={{ fontSize: 14, color: 'var(--ink)', marginBottom: d.kind === 'prompt' ? 10 : 16, whiteSpace: 'pre-wrap' }}>{d.message}</div>}

        {d.kind === 'prompt' && (
          <input ref={inputRef} value={val} placeholder={d.placeholder || ''}
            onChange={(e) => setVal(e.target.value)} style={{ width: '100%', marginBottom: 4 }} />
        )}

        <div className="row" style={{ gap: 8, marginTop: 14, justifyContent: 'flex-end' }}>
          {d.kind !== 'alert' && <button onClick={onCancel}>{d.cancelText || 'Cancel'}</button>}
          <button className={d.danger ? 'red' : 'primary'} onClick={onOk}>
            {d.confirmText || (d.kind === 'confirm' ? 'Confirm' : d.kind === 'alert' ? 'OK' : 'Save')}
          </button>
        </div>
      </div>
    </div>
  )
}
