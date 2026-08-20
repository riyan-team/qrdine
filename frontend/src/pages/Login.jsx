import { useState } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { auth } from '../auth'
import DineInLogo from '../DineInLogo.jsx'

const HOME_FOR = { Cashier: '/cashier', Kitchen: '/kitchen', Waiter: '/waiter', Manager: '/admin/dashboard', Owner: '/admin/dashboard' }

export default function Login() {
  const nav = useNavigate()
  const stored = auth.getStaff()?.slug
  const [slug, setSlug] = useState(stored && stored !== 'desidhaba' ? stored : '')
  const [username, setU] = useState('')
  const [password, setP] = useState('')
  const [err, setErr] = useState('')
  const [busy, setBusy] = useState(false)

  const doLogin = async (e) => {
    e.preventDefault()
    setErr(''); setBusy(true)
    try {
      const session = await auth.login(slug.trim().toLowerCase(), username.trim(), password)
      nav(HOME_FOR[session.role] || '/cashier')
    } catch (e) { setErr(e.message) }
    setBusy(false)
  }

  return (
    <div className="auth-split">
      {/* Brand panel */}
      <aside className="auth-side">
        <DineInLogo className="auth-logo" />
        <div className="auth-side-tag">QR ordering, kitchen timing &amp; billing — all in one.</div>
      </aside>

      {/* Sign-in panel */}
      <main className="auth-main">
        <div className="auth-card">
          <div className="h" style={{ marginTop: 0 }}>Welcome back</div>
          <p className="muted" style={{ marginTop: 0, fontSize: 14 }}>Sign in to your restaurant.</p>
          <form onSubmit={doLogin}>
            <label className="muted" style={{ fontSize: 13 }}>Restaurant address</label>
            <input value={slug} onChange={(e) => setSlug(e.target.value)} style={{ width: '100%', margin: '4px 0 12px' }} placeholder="your restaurant address" />
            <label className="muted" style={{ fontSize: 13 }}>Username</label>
            <input value={username} onChange={(e) => setU(e.target.value)} style={{ width: '100%', margin: '4px 0 12px' }} />
            <label className="muted" style={{ fontSize: 13 }}>Password</label>
            <input type="password" value={password} onChange={(e) => setP(e.target.value)} style={{ width: '100%', margin: '4px 0 12px' }} />
            {err && <div style={{ color: 'var(--red)', fontSize: 14, marginBottom: 8 }}>{err}</div>}
            <button className="primary" disabled={busy || !slug.trim() || !username.trim() || !password} style={{ width: '100%' }}>
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
