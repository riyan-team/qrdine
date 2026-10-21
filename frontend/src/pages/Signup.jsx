import { useEffect, useState } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { onboard, getPlans, money, checkout } from '../api'
import { auth } from '../auth'
import { useForm, FormField, required, minLen, maxLen, username as vUsername, slug as vSlug } from '../forms.jsx'
import { friendlyError } from '../errors'
import AuthBrand from '../AuthBrand.jsx'
import BackHome from '../BackHome.jsx'
import { CheckIcon, WarnIcon } from '../icons.jsx'

// Redirect the browser to JazzCash's hosted checkout by POSTing the signed fields.
function submitToGateway(pay) {
  const form = document.createElement('form')
  form.method = 'POST'; form.action = pay.redirectUrl
  Object.entries(pay.fields || {}).forEach(([k, v]) => {
    const input = document.createElement('input'); input.type = 'hidden'; input.name = k; input.value = v; form.appendChild(input)
  })
  document.body.appendChild(form); form.submit()
}

const FEATURES = [
  ['ordering', 'Self-ordering'], ['kitchen', 'Kitchen display'], ['waiter', 'Waiter app'],
  ['billing', 'Billing & tax'], ['inventory', 'Inventory'], ['reports', 'Sales reports'],
]

const deriveSlug = (name) => name.toLowerCase().replace(/[^a-z0-9]+/g, '').slice(0, 30)

// Public sign-up: pick a plan, (mock) pay, create the restaurant, land in the dashboard.
export default function Signup() {
  const nav = useNavigate()
  const origin = typeof window !== 'undefined' ? window.location.origin : ''
  const [step, setStep] = useState('plan')      // plan → details → pay
  const [plans, setPlans] = useState([])
  const [planId, setPlanId] = useState(null)
  const [slugEdited, setSlugEdited] = useState(false)
  const [err, setErr] = useState('')
  const [busy, setBusy] = useState(false)

  const form = useForm(
    { restaurantName: '', slug: '', ownerDisplayName: '', ownerUsername: '', ownerPassword: '' },
    {
      restaurantName: (v) => required(v, 'Restaurant name') || maxLen(v, 120, 'Restaurant name'),
      slug: (v) => required(v, 'Address') || minLen(v, 3, 'Address') || maxLen(v, 60, 'Address') || vSlug(v),
      ownerDisplayName: (v) => required(v, 'Your name') || maxLen(v, 60, 'Your name'),
      ownerUsername: (v) => required(v, 'Username') || minLen(v, 3, 'Username') || maxLen(v, 60, 'Username') || vUsername(v),
      ownerPassword: (v) => required(v, 'Password') || minLen(v, 6, 'Password'),
    },
  )

  useEffect(() => { getPlans().then(setPlans).catch(() => {}) }, [])
  const plan = plans.find((p) => p.id === planId)

  // Restaurant name drives the address until the owner edits the address themselves.
  const onName = (e) => {
    const v = e.target.value
    form.setValue('restaurantName', v)
    if (!slugEdited) form.setValue('slug', deriveSlug(v))
  }
  const onSlug = (e) => {
    setSlugEdited(true)
    form.setValue('slug', e.target.value.toLowerCase().replace(/[^a-z0-9-]+/g, ''))
  }

  const finish = async () => {
    setErr('')
    if (!form.validateAll()) { setStep('details'); return }
    setBusy(true)
    try {
      const f = { ...form.values, slug: form.values.slug.trim().toLowerCase() }
      // Take payment for paid plans.
      if (plan && plan.pricePkr > 0) {
        const pay = await checkout(plan.pricePkr, f.slug, `QrDine ${plan.name} plan`)
        if (pay.status === 'redirect') { submitToGateway(pay); return }        // leaves for JazzCash
        if (pay.status === 'unconfigured') { setErr(pay.message || 'Payment gateway not configured.'); setBusy(false); return }
        // status 'paid' (mock) → continue
      }
      const session = await onboard({ ...f, planId })
      auth.setSession(session, f.slug)
      nav('/admin/dashboard')
    } catch (e) {
      // Field-specific server errors land on their inputs; everything else is a banner.
      const mapped = form.setServerErrors(e.fieldErrors)
      setErr(mapped ? 'Please fix the highlighted fields and try again.' : friendlyError(e))
      setStep('details')
      setBusy(false)
    }
  }
  const proceed = () => {
    if (!plan) return
    if (!form.validateAll()) return
    plan.pricePkr > 0 ? setStep('pay') : finish()
  }

  const STEP_ORDER = ['plan', 'details', 'pay']
  const stepIndex = STEP_ORDER.indexOf(step)

  return (
    <div className={'auth-split' + (step === 'plan' ? ' plan-mode' : '')}>
      {/* Brand panel — hidden on the wide plan-picker step */}
      {step !== 'plan' && <AuthBrand />}

      {/* Sign-up panel */}
      <main className="auth-main auth-scroll">
        <div className={'auth-card auth-card-wide' + (step === 'plan' ? ' auth-card-plans' : '')}>
          <BackHome />
          <div className="h" style={{ marginTop: 0, marginBottom: 2 }}>Create your restaurant</div>
          <p className="muted" style={{ marginTop: 0, fontSize: 14 }}>
            {step === 'plan' ? 'Pick a plan — you can change it anytime.'
              : step === 'details' ? 'Tell us about your restaurant.'
                : 'Confirm your plan to get started.'}
          </p>

          {/* Step indicator */}
          <div className="auth-steps">
            {['Plan', 'Details', plan?.pricePkr > 0 ? 'Payment' : 'Finish'].map((label, i) => (
              <div key={label} className={'auth-step' + (i === stepIndex ? ' on' : '') + (i < stepIndex ? ' done' : '')}>
                <span className="auth-step-dot">{i < stepIndex ? <CheckIcon size={13} /> : i + 1}</span>
                <span className="auth-step-lbl">{label}</span>
              </div>
            ))}
          </div>

          {/* Step 1 — choose a plan */}
          {step === 'plan' && (
            <>
              <div className="plan-grid">
                {plans.map((p, i) => (
                  <button type="button" key={p.id} className={'plan-card fade-up' + (planId === p.id ? ' sel' : '')}
                    style={{ animationDelay: `${i * 60}ms` }} onClick={() => setPlanId(p.id)}>
                    {i === 2 && <span className="plan-badge">Most popular</span>}
                    <span className="pc-name">{p.name}</span>
                    <span className="pc-price">{p.pricePkr > 0 ? money(p.pricePkr) : 'Free'}{p.pricePkr > 0 && <small>/mo</small>}</span>
                    <span className="pc-setup">{p.setupFeePkr > 0 ? `+ ${money(p.setupFeePkr)} one-time setup` : 'No setup fee'}</span>
                    <span className="pc-desc">{p.description}</span>
                    <span className="pc-feats">
                      {FEATURES.map(([k, label]) => (
                        <span key={k} className={'pc-feat' + (p[k] ? '' : ' off')}>
                          {p[k] ? <CheckIcon size={13} /> : <span className="pc-x">×</span>} {label}
                        </span>
                      ))}
                    </span>
                    <span className="pc-select">{planId === p.id ? <><CheckIcon size={14} /> Selected</> : 'Choose plan'}</span>
                  </button>
                ))}
                {plans.length === 0 && <div className="empty">Loading plans…</div>}
              </div>
              <button className="primary" disabled={!planId} style={{ width: '100%', marginTop: 16 }} onClick={() => setStep('details')}>Continue →</button>
            </>
          )}

          {/* Step 2 — restaurant details */}
          {step === 'details' && (
            <div style={{ textAlign: 'left' }}>
              <div className="plan-chip">
                Plan: <b>{plan?.name}</b> · {plan?.pricePkr > 0 ? money(plan.pricePkr) + '/mo' : 'Free'}{plan?.setupFeePkr > 0 ? ` + ${money(plan.setupFeePkr)} setup` : ''}
                <button className="ghost" onClick={() => setStep('plan')}>change</button>
              </div>

              <FormField label="Restaurant name" placeholder="e.g. Karachi Biryani House"
                {...form.register('restaurantName')} onChange={onName} autoFocus />

              <FormField label="Your menu address" placeholder="karachibiryani"
                hint={`Your QR menu opens at ${origin}/r/${form.values.slug || 'address'}/t/T1 — you'll also use this to sign in.`}
                {...form.register('slug')} onChange={onSlug} />

              <FormField label="Your name" placeholder="e.g. Ali Khan" {...form.register('ownerDisplayName')} />

              <FormField label="Username" placeholder="e.g. ali (no spaces)"
                hint="You'll sign in with this. Letters, numbers, dots, underscores and dashes only."
                autoComplete="username" {...form.register('ownerUsername')} />

              <FormField label="Password" type="password" placeholder="At least 6 characters"
                autoComplete="new-password" {...form.register('ownerPassword')} />

              {err && <div className="form-error">{err}</div>}
              <div className="row" style={{ gap: 8, marginTop: 6 }}>
                <button onClick={() => setStep('plan')}>Back</button>
                <button className="primary" style={{ flex: 1 }} disabled={busy} onClick={proceed}>
                  {plan?.pricePkr > 0 ? 'Continue to payment →' : (busy ? 'Creating…' : 'Create restaurant')}
                </button>
              </div>
            </div>
          )}

          {/* Step 3 — mock payment */}
          {step === 'pay' && (
            <div style={{ textAlign: 'left' }}>
              <div className="plan-chip">{plan?.name} · <b>{money(plan?.pricePkr)}/month</b></div>
              <label className="fld"><span className="muted" style={{ fontSize: 13 }}>Card number</span>
                <input placeholder="Card number" defaultValue="4242 4242 4242 4242" /></label>
              <div className="row" style={{ gap: 10, marginTop: 10 }}>
                <label className="fld" style={{ flex: 1 }}><span className="muted" style={{ fontSize: 13 }}>Expiry</span><input placeholder="MM/YY" defaultValue="12/28" /></label>
                <label className="fld" style={{ flex: 1 }}><span className="muted" style={{ fontSize: 13 }}>CVC</span><input placeholder="CVC" defaultValue="123" /></label>
              </div>
              <div className="muted" style={{ fontSize: 12, margin: '12px 0' }}><WarnIcon size={13} /> Demo payment — no real charge. Real gateways (JazzCash / Easypaisa / Safepay) plug in here.</div>
              {err && <div className="form-error">{err}</div>}
              <div className="row" style={{ gap: 8 }}>
                <button onClick={() => setStep('details')}>Back</button>
                <button className="green" style={{ marginLeft: 'auto' }} disabled={busy} onClick={finish}>{busy ? 'Processing…' : `Pay ${money(plan?.pricePkr)} & start`}</button>
              </div>
            </div>
          )}

          <div className="auth-divider"><span>or</span></div>
          <div className="center">
            <span className="muted" style={{ fontSize: 14 }}>Already have an account? </span>
            <Link to="/login" style={{ color: 'var(--accent)', fontWeight: 700 }}>Sign in →</Link>
          </div>
        </div>
      </main>
    </div>
  )
}
