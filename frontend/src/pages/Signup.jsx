import { useEffect, useState } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { onboard, getPlans, money, checkout } from '../api'
import { auth } from '../auth'
import { useForm, FormField, required, minLen, maxLen, username as vUsername, slug as vSlug } from '../forms.jsx'
import { friendlyError } from '../errors'
import DineInLogo from '../DineInLogo.jsx'

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

  return (
    <div className="wrap" style={{ maxWidth: 900 }}>
      <div className="center" style={{ marginTop: 24, color: 'var(--accent)' }}>
        <DineInLogo style={{ width: 180, height: 'auto' }} />
      </div>
      <div className="h" style={{ marginTop: 12, textAlign: 'center' }}>Start your restaurant on QrDine</div>

      {/* Step 1 — choose a plan (dynamic banners) */}
      {step === 'plan' && (
        <>
          <p className="muted" style={{ textAlign: 'center' }}>Choose a plan. You can change it anytime.</p>
          <div className="grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(210px, 1fr))' }}>
            {plans.map((p) => (
              <div key={p.id} className="card" onClick={() => setPlanId(p.id)}
                style={{ cursor: 'pointer', borderColor: planId === p.id ? 'var(--accent)' : 'var(--line)', borderWidth: 2, transition: 'transform .12s', transform: planId === p.id ? 'translateY(-2px)' : 'none' }}>
                <div className="row between">
                  <b style={{ fontSize: 17 }}>{p.name}</b>
                  {planId === p.id && <span className="pill p-flow">Selected</span>}
                </div>
                <div className="big" style={{ color: 'var(--accent)', margin: '6px 0 0' }}>
                  {p.pricePkr > 0 ? money(p.pricePkr) : 'Free'}<span className="muted" style={{ fontSize: 13, fontWeight: 400 }}>{p.pricePkr > 0 ? '/mo' : ''}</span>
                </div>
                {p.setupFeePkr > 0 && <div className="muted" style={{ fontSize: 12, marginBottom: 6 }}>+ {money(p.setupFeePkr)} one-time setup</div>}
                <div className="muted" style={{ fontSize: 13, margin: '6px 0 8px' }}>{p.description}</div>
                {FEATURES.map(([k, label]) => (
                  <div key={k} style={{ fontSize: 13, color: p[k] ? 'var(--ink)' : 'var(--faint)', padding: '1px 0' }}>
                    {p[k] ? '✓' : '·'} {label}
                  </div>
                ))}
              </div>
            ))}
            {plans.length === 0 && <div className="empty">Loading plans…</div>}
          </div>
          <div className="row" style={{ marginTop: 16, justifyContent: 'center' }}>
            <button className="primary" disabled={!planId} onClick={() => setStep('details')}>Continue →</button>
          </div>
        </>
      )}

      {/* Step 2 — restaurant details */}
      {step === 'details' && (
        <div className="card" style={{ maxWidth: 480, margin: '0 auto', textAlign: 'left' }}>
          <div className="muted" style={{ fontSize: 13 }}>Plan: <b style={{ color: 'var(--accent)' }}>{plan?.name}</b> · {plan?.pricePkr > 0 ? money(plan.pricePkr) + '/mo' : 'Free'}{plan?.setupFeePkr > 0 ? ` + ${money(plan.setupFeePkr)} setup` : ''} <button className="ghost" onClick={() => setStep('plan')}>change</button></div>
          <div style={{ height: 12 }} />

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
          <button className="primary" disabled={busy} style={{ width: '100%', marginTop: 6 }} onClick={proceed}>
            {plan?.pricePkr > 0 ? 'Continue to payment →' : (busy ? 'Creating…' : 'Create restaurant')}
          </button>
        </div>
      )}

      {/* Step 3 — mock payment */}
      {step === 'pay' && (
        <div className="card" style={{ maxWidth: 420, margin: '0 auto', textAlign: 'left' }}>
          <b>Payment</b>
          <div className="muted" style={{ fontSize: 13, margin: '4px 0 12px' }}>{plan?.name} · {money(plan?.pricePkr)}/month</div>
          <input placeholder="Card number" defaultValue="4242 4242 4242 4242" style={{ width: '100%', marginBottom: 10 }} />
          <div className="row" style={{ gap: 10 }}>
            <input placeholder="MM/YY" defaultValue="12/28" style={{ flex: 1 }} />
            <input placeholder="CVC" defaultValue="123" style={{ flex: 1 }} />
          </div>
          <div className="muted" style={{ fontSize: 12, margin: '10px 0' }}>⚠ Demo payment — no real charge. Real gateways (JazzCash / Easypaisa / Safepay) plug in here.</div>
          {err && <div className="form-error">{err}</div>}
          <div className="row" style={{ gap: 8 }}>
            <button onClick={() => setStep('details')}>Back</button>
            <button className="green" style={{ marginLeft: 'auto' }} disabled={busy} onClick={finish}>{busy ? 'Processing…' : `Pay ${money(plan?.pricePkr)} & start`}</button>
          </div>
        </div>
      )}

      <div className="center" style={{ marginTop: 18 }}>
        <span className="muted" style={{ fontSize: 14 }}>Already have one? </span>
        <Link to="/login" style={{ color: 'var(--accent)', fontWeight: 700 }}>Sign in</Link>
      </div>
    </div>
  )
}
