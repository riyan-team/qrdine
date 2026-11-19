import { useEffect, useRef, useState } from 'react'

// In-app replacement for the browser's native prompt()/confirm()/alert() dialogs,
// which render as ugly OS boxes. These return Promises and render inside our own
// themed modal. Mount <DialogHost/> once (in App); call ui.prompt/confirm/alert
// from anywhere.
const listeners = new Set()
const emit = (dialog) => listeners.forEach((l) => l(dialog))
const norm = (o) => (typeof o === 'string' ? { message: o } : (o || {}))

export const ui = {
  // Resolves to the entered string, or null if cancelled. Options beyond title/message/placeholder:
  //   label, required, requiredMessage, maxLength, type ('number' | 'password' | …), inputMode,
  //   min, max, integer (whole numbers only), validate(value) => error text or null.
  // A wrong answer shows a short message under the box and keeps the dialog open.
  prompt: (opts) => new Promise((resolve) => emit({ kind: 'prompt', ...norm(opts), resolve })),
  // Resolves to true / false.
  confirm: (opts) => new Promise((resolve) => emit({ kind: 'confirm', ...norm(opts), resolve })),
  // Resolves when dismissed.
  alert: (opts) => new Promise((resolve) => emit({ kind: 'alert', ...norm(opts), resolve })),
}

// Synthesised service bell / chime
let _audioCtx = null
export function playBell() {
  try {
    _audioCtx = _audioCtx || new (window.AudioContext || window.webkitAudioContext)()
    if (_audioCtx.state === 'suspended') _audioCtx.resume()
    const ctx = _audioCtx
    const now = ctx.currentTime
    const notes = [
      { freq: 880, start: 0, dur: 0.55, gain: 0.25 },
      { freq: 1175, start: 0.15, dur: 0.55, gain: 0.22 },
      { freq: 1318, start: 0.32, dur: 0.75, gain: 0.28 }
    ]
    notes.forEach(({ freq, start, dur, gain: vol }) => {
      const osc = ctx.createOscillator()
      const gain = ctx.createGain()
      osc.type = 'sine'
      osc.frequency.setValueAtTime(freq, now + start)
      gain.gain.setValueAtTime(0.0001, now + start)
      gain.gain.exponentialRampToValueAtTime(vol, now + start + 0.02)
      gain.gain.exponentialRampToValueAtTime(0.0001, now + start + dur)
      osc.connect(gain)
      gain.connect(ctx.destination)
      osc.start(now + start)
      osc.stop(now + start + dur)
    })
  } catch { /* browser autoplay block */ }
}

// Transient corner notifications for background/non-form actions (a save that
// succeeded, an action that failed) — so nothing fails silently.
const toastListeners = new Set()
let toastId = 0
export const toast = {
  show: (message, tone = 'info', ms = 4000) => {
    const t = { id: ++toastId, message: typeof message === 'string' ? message : String(message ?? ''), tone, ms }
    toastListeners.forEach((l) => l(t))
    return t.id
  },
  error: (message, ms = 5000) => toast.show(message, 'error', ms),
  success: (message, ms = 3500) => toast.show(message, 'success', ms),
  info: (message, ms = 4000) => toast.show(message, 'info', ms),
}

export function ToastHost() {
  const [items, setItems] = useState([])
  useEffect(() => {
    const l = (t) => {
      setItems((cur) => [...cur, t])
      if (t.ms > 0) setTimeout(() => setItems((cur) => cur.filter((x) => x.id !== t.id)), t.ms)
    }
    toastListeners.add(l)
    return () => toastListeners.delete(l)
  }, [])
  if (!items.length) return null
  const dismiss = (id) => setItems((cur) => cur.filter((x) => x.id !== id))
  return (
    <div className="toast-host noprint" aria-live="polite">
      {items.map((t) => (
        <div key={t.id} className={`toast toast-${t.tone}`} onClick={() => dismiss(t.id)} role="status">
          <span className="toast-dot" />
          <span className="toast-msg">{t.message}</span>
        </div>
      ))}
    </div>
  )
}

export function DialogHost() {
  const [d, setD] = useState(null)
  const [val, setVal] = useState('')
  const [err, setErr] = useState('')
  const inputRef = useRef(null)

  useEffect(() => {
    const l = (dialog) => { setD(dialog); setVal(dialog.defaultValue ?? ''); setErr('') }
    listeners.add(l)
    return () => listeners.delete(l)
  }, [])

  useEffect(() => { if (d?.kind === 'prompt') setTimeout(() => inputRef.current?.focus(), 30) }, [d])

  if (!d) return null

  const done = (result) => { d.resolve(result); setD(null) }
  const onCancel = () => done(d.kind === 'confirm' ? false : null)
  const problem = (v) => {
    if (!v) return d.required ? (d.requiredMessage || 'Please fill this in.') : ''
    if (d.type === 'number') {
      const n = Number(v)
      if (!Number.isFinite(n)) return 'Enter a number.'
      if (d.integer && !Number.isInteger(n)) return 'Enter a whole number.'
      if (d.min != null && n < d.min) return `Enter ${d.min} or more.`
      if (d.max != null && n > d.max) return `Enter ${d.max} or less.`
    }
    if (d.maxLength && v.length > d.maxLength) return `Keep it to ${d.maxLength} characters.`
    return d.validate ? (d.validate(v) || '') : ''
  }
  const onOk = () => {
    if (d.kind === 'prompt') {
      const v = val.trim()
      const why = problem(v)
      if (why) { setErr(why); inputRef.current?.focus(); return }
      done(v || null)
    }
    else if (d.kind === 'confirm') done(true)
    else done(undefined)
  }
  const onKey = (e) => { if (e.key === 'Enter') { e.preventDefault(); onOk() } if (e.key === 'Escape') onCancel() }
  const label = d.label || d.title || 'Your answer'

  return (
    <div className="overlay" onMouseDown={(e) => { if (e.target.classList.contains('overlay')) onCancel() }}>
      <div className="modal" style={{ maxWidth: 400 }} onKeyDown={onKey}>
        {d.title && <div className="big" style={{ marginBottom: 6 }}>{d.title}</div>}
        {d.message && <div style={{ fontSize: 14, color: 'var(--ink)', marginBottom: d.kind === 'prompt' ? 10 : 16, whiteSpace: 'pre-wrap' }}>{d.message}</div>}

        {d.kind === 'prompt' && (
          <div className="field" style={{ marginBottom: 4 }}>
            {d.label && <label className={`field-label${d.required ? ' req' : ''}`} htmlFor="ui-prompt-input">{d.label}</label>}
            <input id="ui-prompt-input" ref={inputRef} value={val} placeholder={d.placeholder || ''}
              type={d.type || 'text'} inputMode={d.inputMode} maxLength={d.maxLength}
              min={d.min} max={d.max} step={d.integer ? 1 : undefined}
              aria-label={d.label ? undefined : label} aria-required={d.required ? true : undefined}
              aria-invalid={err ? true : undefined} aria-describedby={err ? 'ui-prompt-err' : undefined}
              className={err ? 'invalid' : undefined}
              onChange={(e) => { setVal(e.target.value); if (err) setErr('') }} style={{ width: '100%' }} />
            {err && <div id="ui-prompt-err" className="field-error" role="alert">{err}</div>}
          </div>
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
