import { useState } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { auth } from '../auth'

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
    <div className="auth-page">
      <div className="card auth-card">
        <div className="h" style={{ marginTop: 0, textAlign: 'center' }}>Staff sign in</div>
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

        <div className="center" style={{ marginTop: 18 }}>
          <span className="muted" style={{ fontSize: 14 }}>New here? </span>
          <Link to="/signup" style={{ color: 'var(--accent)', fontWeight: 700 }}>Create your restaurant →</Link>
        </div>
      </div>
    </div>
  )
}
