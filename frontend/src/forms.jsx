import { useRef, useState } from 'react'

// Small, dependency-free form toolkit used across the app for consistent validation:
//  - validate on blur and on submit (never aggressively while first typing)
//  - once a field has been touched, its error clears live as the value becomes valid
//  - entered values are always preserved on error
//  - on submit, focus + scroll to the first invalid field
//  - server-side field errors (ApiError.fieldErrors) map straight onto the inputs
//
// Usage:
//   const form = useForm({ username: '' }, { username: v => required(v) || maxLen(v, 60) })
//   <FormField label="Username" {...form.register('username')} />
//   if (!form.validateAll()) return
//   ... catch (e) { form.setServerErrors(e.fieldErrors) }

// ---- reusable validators: each returns an error string, or '' when valid ----
export const required = (v, label = 'This field') =>
  (v === undefined || v === null || String(v).trim() === '') ? `${label} is required.` : ''
export const minLen = (v, n, label = 'This') =>
  String(v ?? '').trim().length < n ? `${label} must be at least ${n} characters.` : ''
export const maxLen = (v, n, label = 'This') =>
  String(v ?? '').length > n ? `${label} must be ${n} characters or fewer.` : ''
export const pattern = (v, re, msg) => (re.test(String(v ?? '')) ? '' : msg)
export const username = (v) =>
  /^[a-zA-Z0-9_.-]+$/.test(String(v ?? '')) ? '' : 'Use only letters, numbers, dots, underscores and dashes (no spaces).'
export const slug = (v) =>
  /^[a-z0-9-]+$/.test(String(v ?? '')) ? '' : 'Use only lowercase letters, numbers and dashes.'

export function useForm(initial, schema = {}) {
  const [values, setValues] = useState(initial)
  const [errors, setErrors] = useState({})
  const [touched, setTouched] = useState({})
  const refs = useRef({})

  const check = (name, value, all) => {
    const rule = schema[name]
    return rule ? (rule(value, all) || '') : ''
  }

  const setValue = (name, value) => {
    setValues((prev) => {
      const next = { ...prev, [name]: value }
      // Only surface/clear errors for a field the user has already left once.
      if (touched[name]) setErrors((e) => ({ ...e, [name]: check(name, value, next) }))
      return next
    })
  }

  const onBlur = (name) => {
    setTouched((t) => ({ ...t, [name]: true }))
    setErrors((e) => ({ ...e, [name]: check(name, values[name], values) }))
  }

  const focusFirst = (errs) => {
    const first = Object.keys(schema).find((k) => errs[k]) || Object.keys(errs)[0]
    const el = first && refs.current[first]
    if (el) { try { el.focus(); el.scrollIntoView({ behavior: 'smooth', block: 'center' }) } catch { /* jsdom */ } }
  }

  const validateAll = () => {
    const errs = {}
    for (const name of Object.keys(schema)) {
      const msg = check(name, values[name], values)
      if (msg) errs[name] = msg
    }
    setErrors(errs)
    setTouched(Object.fromEntries(Object.keys(schema).map((k) => [k, true])))
    if (Object.keys(errs).length) focusFirst(errs)
    return Object.keys(errs).length === 0
  }

  // Map server-reported field errors (camelCase keys) onto the form.
  const setServerErrors = (fieldErrors) => {
    if (!fieldErrors) return false
    setErrors((e) => ({ ...e, ...fieldErrors }))
    setTouched((t) => ({ ...t, ...Object.fromEntries(Object.keys(fieldErrors).map((k) => [k, true])) }))
    focusFirst(fieldErrors)
    return true
  }

  // Spread onto <FormField/>.
  const register = (name) => ({
    name,
    value: values[name] ?? '',
    onChange: (e) => setValue(name, e && e.target ? e.target.value : e),
    onBlur: () => onBlur(name),
    error: touched[name] ? (errors[name] || '') : '',
    inputRef: (el) => { refs.current[name] = el },
  })

  return { values, setValue, setValues, errors, touched, register, validateAll, setServerErrors }
}

// A labelled input with an inline error (red border + message below) or a hint.
// Works for text, password, email, etc. Pass `as="textarea"` for a textarea.
export function FormField({
  label, hint, error, inputRef, as = 'input', className = '', style, ...rest
}) {
  const invalid = !!error
  const cls = `${invalid ? 'invalid' : ''} ${className}`.trim()
  const Tag = as
  return (
    <div className="field" style={style}>
      {label && <label className="field-label">{label}</label>}
      <Tag ref={inputRef} className={cls} aria-invalid={invalid || undefined}
        {...rest} />
      {error
        ? <div className="field-error" role="alert">{error}</div>
        : hint ? <div className="field-hint">{hint}</div> : null}
    </div>
  )
}
