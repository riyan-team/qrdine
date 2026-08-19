import { useState } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { auth } from '../auth'

const HOME_FOR = { Cashier: '/cashier', Kitchen: '/kitchen', Waiter: '/waiter', Manager: '/admin/dashboard', Owner: '/admin/dashboard' }

// Demo accounts for the seeded "desidhaba" restaurant.
const DEMO = [
  { username: 'owner', password: 'owner123', label: 'Owner' },
  { username: 'cashier', password: 'cash123', label: 'Reception' },
  { username: 'kitchen', password: 'kit123', label: 'Kitchen' },
  { username: 'waiter', password: 'wait123', label: 'Waiter' },
]

export default function Login() {
  const nav = useNavigate()
  const [slug, setSlug] = useState(auth.staffSlug())
  const [username, setU] = useState('')
  const [password, setP] = useState('')
  const [err, setErr] = useState('')
  const [busy, setBusy] = useState(false)

  const doLogin = async (s, u, p) => {
    setErr(''); setBusy(true)
    try {
      const session = await auth.login(s, u, p)
      nav(HOME_FOR[session.role] || '/cashier')
    } catch (e) { setErr(e.message) }
    setBusy(false)
  }

  return (
    <div className="wrap" style={{ maxWidth: 460 }}>
      <div className="h" style={{ marginTop: 20 }}>Staff sign in</div>
      <div className="card">
        <form onSubmit={(e) => { e.preventDefault(); doLogin(slug.trim(), username, password) }}>
          <label className="muted" style={{ fontSize: 13 }}>Restaurant address</label>
          <input value={slug} onChange={(e) => setSlug(e.target.value)} style={{ width: '100%', margin: '4px 0 12px' }} placeholder="e.g. desidhaba" />
          <label className="muted" style={{ fontSize: 13 }}>Username</label>
          <input value={username} onChange={(e) => setU(e.target.value)} style={{ width: '100%', margin: '4px 0 12px' }} />
          <label className="muted" style={{ fontSize: 13 }}>Password</label>
          <input type="password" value={password} onChange={(e) => setP(e.target.value)} style={{ width: '100%', margin: '4px 0 12px' }} />
          {err && <div style={{ color: 'var(--red)', fontSize: 14, marginBottom: 8 }}>{err}</div>}
          <button className="primary" disabled={busy} style={{ width: '100%' }}>{busy ? 'Signing in…' : 'Sign in'}</button>
        </form>
      </div>

      <div className="muted" style={{ margin: '18px 0 8px', fontSize: 13 }}>Demo (restaurant “desidhaba”) — one tap:</div>
      <div className="grid" style={{ gridTemplateColumns: '1fr 1fr' }}>
        {DEMO.map((d) => (
          <button key={d.username} disabled={busy} onClick={() => doLogin('desidhaba', d.username, d.password)}>
            {d.label} <span className="muted">· {d.username}</span>
          </button>
        ))}
      </div>

      <div className="center" style={{ marginTop: 20 }}>
        <span className="muted" style={{ fontSize: 14 }}>New here? </span>
        <Link to="/signup" style={{ color: 'var(--accent)', fontWeight: 700 }}>Create your restaurant →</Link>
      </div>
    </div>
  )
}
