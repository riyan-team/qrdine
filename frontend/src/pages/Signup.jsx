import { useEffect, useState } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { onboard, getPlans, money, checkout } from '../api'
import { auth } from '../auth'
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

// Public sign-up: pick a plan, (mock) pay, create the restaurant, land in the dashboard.
export default function Signup() {
  const nav = useNavigate()
  const origin = typeof window !== 'undefined' ? window.location.origin : ''
  const [step, setStep] = useState('plan')      // plan → details → pay
  const [plans, setPlans] = useState([])
  const [planId, setPlanId] = useState(null)
  const [f, setF] = useState({ restaurantName: '', slug: '', ownerDisplayName: '', ownerUsername: '', ownerPassword: '' })
  const [err, setErr] = useState('')
  const [busy, setBusy] = useState(false)
  const set = (k, v) => setF((s) => ({ ...s, [k]: v }))

  useEffect(() => { getPlans().then(setPlans).catch(() => {}) }, [])
  const plan = plans.find((p) => p.id === planId)

  const onName = (v) => setF((s) => ({ ...s, restaurantName: v, slug: s.slug || v.toLowerCase().replace(/[^a-z0-9]+/g, '').slice(0, 30) }))
  const ok = f.restaurantName && f.slug && f.ownerDisplayName && f.ownerUsername && f.ownerPassword.length >= 6

  const finish = async () => {
    setBusy(true); setErr('')
    try {
      // Take payment for paid plans.
      if (plan && plan.pricePkr > 0) {
        const pay = await checkout(plan.pricePkr, f.slug.trim().toLowerCase(), `QrDine ${plan.name} plan`)
        if (pay.status === 'redirect') { submitToGateway(pay); return }        // leaves for JazzCash
        if (pay.status === 'unconfigured') { setErr(pay.message || 'Payment gateway not configured.'); setBusy(false); return }
        // status 'paid' (mock) → continue
      }
      const session = await onboard({ ...f, slug: f.slug.trim().toLowerCase(), planId })
      auth.setSession(session, f.slug.trim().toLowerCase())
      nav('/admin/dashboard')
    } catch (e) { setErr(e.message); setStep('details') }
    setBusy(false)
  }
  const proceed = () => { if (!plan) return; plan.pricePkr > 0 ? setStep('pay') : finish() }

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
                <div className="big" style={{ color: 'var(--accent)', margin: '6px 0' }}>
                  {p.pricePkr > 0 ? money(p.pricePkr) : 'Free'}<span className="muted" style={{ fontSize: 13, fontWeight: 400 }}>{p.pricePkr > 0 ? '/mo' : ''}</span>
                </div>
                <div className="muted" style={{ fontSize: 13, marginBottom: 8 }}>{p.description}</div>
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
          <div className="muted" style={{ fontSize: 13 }}>Plan: <b style={{ color: 'var(--accent)' }}>{plan?.name}</b> · {plan?.pricePkr > 0 ? money(plan.pricePkr) + '/mo' : 'Free'} <button className="ghost" onClick={() => setStep('plan')}>change</button></div>
          <div style={{ height: 10 }} />
          <input placeholder="Restaurant name" value={f.restaurantName} onChange={(e) => onName(e.target.value)} style={{ width: '100%', marginBottom: 10 }} />
          <label className="muted" style={{ fontSize: 13 }}>Your address (used in your menu link)</label>
          <div className="addr-field" style={{ margin: '4px 0 6px' }}>
            <span className="addr-prefix">{origin}/r/</span>
            <input placeholder="address" value={f.slug}
              onChange={(e) => set('slug', e.target.value.toLowerCase().replace(/[^a-z0-9]+/g, ''))} />
          </div>
          <div className="muted" style={{ fontSize: 12, marginBottom: 10 }}>
            Customers scan a QR that opens <b>{origin}/r/{f.slug || 'address'}/t/T1</b>
          </div>
          <input placeholder="Your name" value={f.ownerDisplayName} onChange={(e) => set('ownerDisplayName', e.target.value)} style={{ width: '100%', marginBottom: 10 }} />
          <div className="row" style={{ gap: 10 }}>
            <input placeholder="Username" value={f.ownerUsername} onChange={(e) => set('ownerUsername', e.target.value)} style={{ flex: 1 }} />
            <input type="password" placeholder="Password (6+)" value={f.ownerPassword} onChange={(e) => set('ownerPassword', e.target.value)} style={{ flex: 1 }} />
          </div>
          {err && <div style={{ color: 'var(--red)', fontSize: 14, margin: '8px 0' }}>{err}</div>}
          <button className="primary" disabled={!ok || busy} style={{ width: '100%', marginTop: 12 }} onClick={proceed}>
            {plan?.pricePkr > 0 ? `Continue to payment →` : (busy ? 'Creating…' : 'Create restaurant')}
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
          {err && <div style={{ color: 'var(--red)', fontSize: 14, marginBottom: 8 }}>{err}</div>}
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
