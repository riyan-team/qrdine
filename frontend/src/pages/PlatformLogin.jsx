import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { platform } from '../api'
import { platformAuth } from '../auth'

// SaaS operator sign-in — separate from restaurant staff.
export default function PlatformLogin() {
  const nav = useNavigate()
  const [u, setU] = useState('')
  const [p, setP] = useState('')
  const [err, setErr] = useState('')
  const [busy, setBusy] = useState(false)

  const go = async (e) => {
    e.preventDefault(); setErr(''); setBusy(true)
    try {
      const s = await platform.login(u.trim(), p)
      platformAuth.set(s)
      nav('/platform')
    } catch (e) { setErr(e.message) }
    setBusy(false)
  }

  return (
    <div className="wrap" style={{ maxWidth: 400 }}>
      <div className="h" style={{ marginTop: 40 }}>QrDine — Operator</div>
      <div className="card">
        <p className="muted" style={{ marginTop: 0, fontSize: 14 }}>Platform control panel. Manage all restaurants, plans and subscriptions.</p>
        <form onSubmit={go}>
          <input placeholder="Username" value={u} onChange={(e) => setU(e.target.value)} style={{ width: '100%', marginBottom: 10 }} autoFocus />
          <input type="password" placeholder="Password" value={p} onChange={(e) => setP(e.target.value)} style={{ width: '100%', marginBottom: 10 }} />
          {err && <div style={{ color: 'var(--red)', fontSize: 14, marginBottom: 8 }}>{err}</div>}
          <button className="primary" disabled={busy} style={{ width: '100%' }}>{busy ? 'Signing in…' : 'Sign in'}</button>
        </form>
        <div className="muted" style={{ fontSize: 12, marginTop: 12 }}>Demo: superadmin / super123</div>
      </div>
    </div>
  )
}
