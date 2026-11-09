import { useRef, useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { auth } from '../auth'
import { CONTACT } from '../contact'
import DineInLogo from '../DineInLogo.jsx'
import Diorama from '../Diorama.jsx'
import { PasswordInput } from '../forms.jsx'
import { useParallax } from '../motion'

// One box for everyone. The owner uses their mobile number, staff use the username the owner
// gave them, and the account already knows which restaurant it belongs to.
export default function Login() {
  const nav = useNavigate()
  const loc = useLocation()
  const scene = useParallax()
  const card = useRef(null)
  const [who, setWho] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [leaving, setLeaving] = useState(false)
  const current = auth.getStaff()

  const shake = () => {
    card.current?.animate([
      { transform: 'rotateY(0)' }, { transform: 'rotateY(-5deg)' }, { transform: 'rotateY(4deg)' },
      { transform: 'rotateY(-2deg)' }, { transform: 'rotateY(0)' },
    ], { duration: 380, easing: 'ease-out' })
  }

  const submit = async (e) => {
    e.preventDefault()
    if (!who.trim() || !password) {
      setError(!who.trim() ? 'Enter your mobile number or username.' : 'Enter your password.')
      shake()
      return
    }
    setBusy(true); setError('')
    try {
      await auth.signIn(who.trim(), password)
      setLeaving(true)
      setTimeout(() => nav(loc.state?.from || auth.home(), { replace: true }), 460)
    } catch (err) {
      setError(err.message || 'Could not sign in. Please try again.')
      setBusy(false)
      shake()
    }
  }

  return (
    <div className={'au' + (leaving ? ' au-leaving' : '')} ref={scene}>
      <div className="au-light" />
      <header className="au-top">
        <Link to="/" className="au-brand" aria-label="Dine In home"><DineInLogo /></Link>
        <Link to="/signup" className="au-top-link">Create your restaurant</Link>
      </header>

      <main className="au-grid">
        <section className="au-pitch">
          <h1 className="au-title">Your whole restaurant,<br /><em>in time.</em></h1>
          <p className="au-sub">Every order, ticket and bill in one place. Sign in and pick up where you left off.</p>
          <Diorama />
        </section>

        <section className="au-side">
          <form className="au-card" ref={card} onSubmit={submit} noValidate>
            <h2>Sign in</h2>
            <p className="au-hint">Use your mobile number or the username your owner gave you.</p>

            {error && <div className="au-error" role="alert">{error}</div>}

            <label className="au-field">
              <span>Mobile number or username</span>
              <input value={who} onChange={(e) => setWho(e.target.value)} autoComplete="username" autoCapitalize="none"
                spellCheck="false" placeholder="0300 1234567" autoFocus disabled={busy} />
            </label>
            <label className="au-field">
              <span>Password</span>
              <PasswordInput value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password"
                placeholder="Your password" disabled={busy} />
            </label>

            <button type="submit" className="au-submit" disabled={busy}>
              {busy ? <><span className="spinner" /> Signing in</> : 'Sign in'}
            </button>

            {current?.token && !busy && (
              <button type="button" className="au-continue" onClick={() => nav(auth.home())}>
                Continue as {current.displayName}
              </button>
            )}

            <div className="au-foot">
              <span>New to Dine In? <Link to="/signup">Create your restaurant</Link></span>
              <span>Forgot your password? <a href={CONTACT.whatsapp} target="_blank" rel="noreferrer">WhatsApp us</a> or ask your owner.</span>
            </div>
          </form>
        </section>
      </main>
    </div>
  )
}
