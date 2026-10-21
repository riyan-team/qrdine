import { useState } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { auth } from '../auth'
import { lookupRestaurant } from '../api'
import { useForm, FormField, required } from '../forms.jsx'
import { friendlyError } from '../errors'
import AuthBrand from '../AuthBrand.jsx'
import BackHome from '../BackHome.jsx'

const HOME_FOR = { Cashier: '/cashier', Kitchen: '/kitchen', Waiter: '/waiter', Manager: '/admin/dashboard', Owner: '/admin/dashboard' }

export default function Login() {
  const nav = useNavigate()
  const stored = auth.getStaff()?.slug
  const form = useForm(
    { place: stored && stored !== 'desidhaba' ? stored : '', username: '', password: '' },
    {
      place: (v) => required(v, 'Restaurant name or address'),
      username: (v) => required(v, 'Username'),
      password: (v) => required(v, 'Password'),
    },
  )
  const [err, setErr] = useState('')
  const [busy, setBusy] = useState(false)

  const doLogin = async (e) => {
    e.preventDefault()
    setErr('')
    if (!form.validateAll()) return
    setBusy(true)
    try {
      const { place, username, password } = form.values
      // Resolve the typed restaurant name/address to its sign-in address (slug).
      const found = await lookupRestaurant(place.trim())
      if (!found) {
        setErr("We couldn't find a restaurant with that name or address. Enter the exact restaurant name you registered, or your menu address (the part after /r/ in your QR link).")
        setBusy(false)
        return
      }
      const session = await auth.login(found.slug, username.trim(), password)
      nav(HOME_FOR[session.role] || '/cashier')
    } catch (e2) {
      // Surface any per-field messages inline, plus a friendly summary.
      if (e2.fieldErrors) form.setServerErrors(e2.fieldErrors)
      setErr(friendlyError(e2))
      setBusy(false)
    }
  }

  return (
    <div className="auth-split">
      {/* Brand panel */}
      <AuthBrand />

      {/* Sign-in panel */}
      <main className="auth-main">
        <div className="auth-card">
          <BackHome />
          <div className="h" style={{ marginTop: 0 }}>Welcome back</div>
          <p className="muted" style={{ marginTop: 0, fontSize: 14 }}>Sign in to your restaurant.</p>
          <form onSubmit={doLogin} noValidate>
            <FormField
              label="Restaurant name or address"
              placeholder="e.g. Karachi Biryani House"
              hint="The exact name you registered, or your menu address (after /r/ in your QR link)."
              autoFocus
              {...form.register('place')}
            />
            <FormField
              label="Username"
              placeholder="The username you created at sign-up"
              autoComplete="username"
              {...form.register('username')}
            />
            <FormField
              label="Password"
              type="password"
              autoComplete="current-password"
              {...form.register('password')}
            />
            {err && <div className="form-error">{err}</div>}
            <button className="primary" disabled={busy} style={{ width: '100%' }}>
              {busy ? 'Signing in…' : 'Sign in'}
            </button>
          </form>

          <div className="auth-divider"><span>or</span></div>

          <div className="center">
            <span className="muted" style={{ fontSize: 14 }}>New to Dine In? </span>
            <Link to="/signup" style={{ color: 'var(--accent)', fontWeight: 700 }}>Create your restaurant →</Link>
          </div>

          <div className="center" style={{ marginTop: 14 }}>
            <Link to="/platform/login" className="muted" style={{ fontSize: 13, textDecoration: 'underline' }}>
              Platform operator sign in
            </Link>
          </div>
        </div>
      </main>
    </div>
  )
}
