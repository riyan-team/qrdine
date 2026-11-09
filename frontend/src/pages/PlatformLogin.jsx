import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { platform } from '../api'
import { platformAuth } from '../auth'
import { useForm, FormField, required } from '../forms.jsx'
import { friendlyError } from '../errors'
import BackHome from '../BackHome.jsx'

// SaaS operator sign-in — separate from restaurant staff.
export default function PlatformLogin() {
  const nav = useNavigate()
  const form = useForm(
    { username: '', password: '' },
    { username: (v) => required(v, 'Username'), password: (v) => required(v, 'Password') },
  )
  const [err, setErr] = useState('')
  const [busy, setBusy] = useState(false)

  const go = async (e) => {
    e.preventDefault(); setErr('')
    if (!form.validateAll()) return
    setBusy(true)
    try {
      const s = await platform.login(form.values.username.trim(), form.values.password)
      platformAuth.set(s)
      nav('/platform')
    } catch (e2) {
      if (e2.fieldErrors) form.setServerErrors(e2.fieldErrors)
      setErr(friendlyError(e2))
      setBusy(false)
    }
  }

  return (
    <div className="wrap" style={{ maxWidth: 400 }}>
      <div style={{ marginTop: 28 }}><BackHome /></div>
      <div className="h" style={{ marginTop: 8 }}>Dine In operator</div>
      <div className="card">
        <p className="muted" style={{ marginTop: 0, fontSize: 14 }}>Platform control panel. Manage all restaurants, plans and subscriptions.</p>
        <form onSubmit={go} noValidate>
          <FormField label="Username" autoComplete="username" autoFocus {...form.register('username')} />
          <FormField label="Password" type="password" autoComplete="current-password" {...form.register('password')} />
          {err && <div className="form-error">{err}</div>}
          <button className="primary" disabled={busy} style={{ width: '100%' }}>{busy ? 'Signing in…' : 'Sign in'}</button>
        </form>
      </div>
    </div>
  )
}
