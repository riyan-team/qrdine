import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { getPlans, onboard } from '../api'
import { auth } from '../auth'
import DineInLogo from '../DineInLogo.jsx'
import { Phone3D, PhoneScreen } from '../Diorama.jsx'
import { ArrowLeftIcon, CheckIcon, EyeIcon, EyeOffIcon } from '../icons.jsx'
import { useParallax, tilt } from '../motion'

// Sign-up, one question at a time, with the owner's own menu taking shape beside it.
// There's no address to choose and no card to enter: the address comes from the name, and the
// first 14 days are free. Their mobile number is what they sign in with from then on.

const STEPS = ['name', 'you', 'password', 'plan', 'making']

const FEATURES = [
  ['ordering', 'Guests order from their phone'],
  [['ordering', 'billing'], 'Takeaway & delivery QR'],
  ['kitchen', 'Kitchen screen'],
  ['waiter', 'Waiter ordering'],
  ['billing', 'Billing and tax'],
  ['billing', 'Loyalty points'],
  ['inventory', 'Stock and supplier khata'],
  ['reports', 'Sales reports and expenses'],
]

const MAKING = [
  'Creating your restaurant',
  'Laying out a starter menu you can edit',
  'Setting up four tables with QR codes',
  'Preparing your reports',
]

const mobileProblem = (value) => {
  const digits = value.replace(/\D/g, '').replace(/^92/, '0')
  return /^0?3\d{9}$/.test(digits) ? '' : 'Enter a Pakistani mobile number, like 0300 1234567.'
}

export default function Signup() {
  const nav = useNavigate()
  const scene = useParallax()
  const input = useRef(null)
  const [step, setStep] = useState(0)
  const [dir, setDir] = useState('fwd')
  const [plans, setPlans] = useState([])
  const [planId, setPlanId] = useState(null)
  const [show, setShow] = useState(false)
  const [error, setError] = useState('')
  const [madeUpTo, setMadeUpTo] = useState(0)
  const [f, setF] = useState({ restaurantName: '', ownerDisplayName: '', mobile: '', password: '' })

  useEffect(() => {
    getPlans().then((list) => {
      const sorted = [...list].sort((a, b) => a.pricePkr - b.pricePkr)
      setPlans(sorted)
      // The plan with the whole system is the sensible start for a restaurant that wants to try it all.
      setPlanId(sorted[sorted.length - 1]?.id ?? null)
    }).catch(() => {})
  }, [])

  useEffect(() => {
    const t = setTimeout(() => input.current?.focus(), 420)
    return () => clearTimeout(t)
  }, [step])

  const set = (k) => (e) => { setError(''); setF((cur) => ({ ...cur, [k]: e.target.value })) }
  const go = (n) => { setDir(n > step ? 'fwd' : 'back'); setError(''); setStep(n) }

  const check = () => {
    if (STEPS[step] === 'name' && f.restaurantName.trim().length < 2) return 'Enter your restaurant\'s name.'
    if (STEPS[step] === 'you') {
      if (f.ownerDisplayName.trim().length < 2) return 'Enter your name.'
      return mobileProblem(f.mobile)
    }
    if (STEPS[step] === 'password' && f.password.length < 6) return 'Use at least 6 characters.'
    return ''
  }

  const next = (e) => {
    e?.preventDefault()
    const problem = check()
    if (problem) { setError(problem); return }
    if (STEPS[step] === 'plan') { create(); return }
    go(step + 1)
  }

  const create = async () => {
    go(STEPS.indexOf('making'))
    setMadeUpTo(0)
    const ticking = setInterval(() => setMadeUpTo((n) => Math.min(n + 1, MAKING.length - 1)), 560)
    const started = Date.now()
    try {
      const session = await onboard({
        restaurantName: f.restaurantName.trim(),
        ownerDisplayName: f.ownerDisplayName.trim(),
        ownerUsername: f.mobile.trim(),
        ownerPassword: f.password,
        planId,
      })
      auth.setSession(session, session.slug)
      await new Promise((r) => setTimeout(r, Math.max(0, 2400 - (Date.now() - started))))
      clearInterval(ticking)
      setMadeUpTo(MAKING.length)
      setTimeout(() => nav('/admin/dashboard?welcome=1', { replace: true }), 600)
    } catch (err) {
      clearInterval(ticking)
      const message = err.message || 'Could not create your restaurant. Please try again.'
      setDir('back')
      setStep(/mobile/i.test(message) ? STEPS.indexOf('you') : STEPS.indexOf('plan'))
      setError(message)
    }
  }

  const name = STEPS[step]
  const progress = Math.min(step, 3)

  return (
    <div className="su" ref={scene}>
      <div className="au-light" />
      <header className="au-top">
        <Link to="/" className="au-brand" aria-label="Dine In home"><DineInLogo /></Link>
        <Link to="/login" className="au-top-link">I already have an account</Link>
      </header>

      <main className={'su-grid' + (name === 'plan' ? ' wide' : '')}>
        <section className="su-main">
          {name !== 'making' && (
            <div className="su-progress" aria-label={`Step ${progress + 1} of 4`}>
              {[0, 1, 2, 3].map((i) => <i key={i} className={i < progress ? 'done' : i === progress ? 'on' : ''} />)}
            </div>
          )}

          <div className="su-deck">
            <form key={name} className={`su-card su-${dir}`} onSubmit={next} noValidate>
              {name === 'name' && (
                <>
                  <h1>What's your restaurant called?</h1>
                  <p>This is what your guests will see at the top of the menu.</p>
                  <input ref={input} className="su-big" value={f.restaurantName} onChange={set('restaurantName')}
                    placeholder="Karachi Biryani House" maxLength={120} autoComplete="organization" />
                </>
              )}

              {name === 'you' && (
                <>
                  <h1>Good to meet you. Who's running {f.restaurantName.trim() || 'it'}?</h1>
                  <p>Your mobile number is how you'll sign in. We don't send anything to it.</p>
                  <label className="au-field"><span>Your name</span>
                    <input ref={input} value={f.ownerDisplayName} onChange={set('ownerDisplayName')} placeholder="Ahmed Raza" maxLength={60} autoComplete="name" />
                  </label>
                  <label className="au-field"><span>Mobile number</span>
                    <input value={f.mobile} onChange={set('mobile')} placeholder="0300 1234567" inputMode="tel" autoComplete="tel" />
                  </label>
                </>
              )}

              {name === 'password' && (
                <>
                  <h1>Choose a password</h1>
                  <p>At least 6 characters. You can change it any time in Settings.</p>
                  <div className="pw-wrap su-pw">
                    <input ref={input} className="su-big" type={show ? 'text' : 'password'} value={f.password} onChange={set('password')}
                      autoComplete="new-password" placeholder="Your password" />
                    <button type="button" className="pw-eye" onClick={() => setShow((s) => !s)} aria-label={show ? 'Hide password' : 'Show password'} tabIndex={-1}>
                      {show ? <EyeOffIcon size={20} /> : <EyeIcon size={20} />}
                    </button>
                  </div>
                </>
              )}

              {name === 'plan' && (
                <>
                  <h1>Pick where to start</h1>
                  <p>Free for your first 14 days. No card needed. You only pay if you decide to stay.</p>
                  <div className="su-plans">
                    {plans.map((p) => {
                      const top = p.id === plans[plans.length - 1]?.id
                      return (
                        <button type="button" key={p.id} {...tilt(5)} className={'su-plan' + (planId === p.id ? ' on' : '')} onClick={() => setPlanId(p.id)}>
                          {top && <span className="su-plan-flag">Whole system</span>}
                          <b>{p.name}</b>
                          <span className="su-plan-price">Rs {Number(p.pricePkr).toLocaleString('en-PK')}<small> / month</small></span>
                          {p.setupFeePkr > 0 && <span className="su-plan-setup">Rs {Number(p.setupFeePkr).toLocaleString('en-PK')} one-time setup, after the trial</span>}
                          <ul>
                            <li><CheckIcon size={14} /> Menu and table QR codes</li>
                            {FEATURES.filter(([key]) => [].concat(key).every((k) => !!p[k])).map(([, label]) => <li key={label}><CheckIcon size={14} /> {label}</li>)}
                          </ul>
                        </button>
                      )
                    })}
                  </div>
                </>
              )}

              {name === 'making' && (
                <div className="su-making">
                  <h1>Setting up {f.restaurantName.trim()}</h1>
                  <ul>
                    {MAKING.map((text, i) => (
                      <li key={text} className={i < madeUpTo ? 'done' : i === madeUpTo ? 'on' : ''}>
                        <span className="su-dot">{i < madeUpTo ? <CheckIcon size={14} /> : i === madeUpTo ? <span className="spinner" /> : null}</span>
                        {text}
                      </li>
                    ))}
                  </ul>
                </div>
              )}

              {error && <div className="au-error" role="alert">
                {error}{/already has an account/i.test(error) && <> <Link to="/login">Sign in</Link></>}
              </div>}

              {name !== 'making' && (
                <div className="su-actions">
                  {step > 0 && <button type="button" className="su-back" onClick={() => go(step - 1)}><ArrowLeftIcon size={16} /> Back</button>}
                  <button type="submit" className="au-submit su-next">
                    {name === 'plan' ? 'Create my restaurant' : 'Continue'}
                  </button>
                </div>
              )}
            </form>
          </div>
        </section>

        {name !== 'plan' && (
          <section className="su-preview" aria-hidden="true">
            <div className="su-preview-stage">
              <Phone3D><PhoneScreen name={f.restaurantName.trim() || 'Your restaurant'} looping={false} /></Phone3D>
            </div>
          </section>
        )}
      </main>
    </div>
  )
}
