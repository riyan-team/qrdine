import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { platform, money } from '../api'
import { platformAuth } from '../auth'
import { ui } from '../ui.jsx'
import { BankIcon, CheckIcon, WarnIcon, CashIcon, ReceiptIcon, TrashIcon, CloseIcon } from '../icons.jsx'

const FEATURES = [['ordering', 'Order'], ['kitchen', 'Kitchen'], ['waiter', 'Waiter'], ['billing', 'Billing'], ['inventory', 'Inventory'], ['reports', 'Reports']]

export default function PlatformConsole() {
  const nav = useNavigate()
  const [stats, setStats] = useState(null)
  const [rows, setRows] = useState([])
  const [plans, setPlans] = useState([])
  const [editPlan, setEditPlan] = useState(null)   // plan object or 'new'
  const [payFor, setPayFor] = useState(null)       // restaurant being paid
  const [err, setErr] = useState('')

  const load = () => {
    platform.stats().then(setStats).catch((e) => setErr(e.message))
    platform.restaurants().then(setRows).catch((e) => setErr(e.message))
    platform.plans().then(setPlans).catch(() => {})
  }
  useEffect(() => { if (!platformAuth.token) { nav('/platform/login'); return } load() }, [])

  const changePlan = async (r, planId) => { await platform.setPlan(r.id, Number(planId)); load() }
  const toggle = async (r) => {
    const next = r.status === 'Active' ? 'Suspended' : 'Active'
    if (next === 'Suspended' && !await ui.confirm({ title: 'Lock restaurant', message: `Lock ${r.name}? Their app will stop working until unlocked.`, confirmText: 'Lock', danger: true })) return
    await platform.setStatus(r.id, next); load()
  }
  const toggleFbr = async (r) => {
    if (r.featureFbr && !await ui.confirm({ title: 'Turn off FBR', message: `Turn OFF FBR for ${r.name}? Their invoice sync will stop.`, confirmText: 'Turn off', danger: true })) return
    await platform.setFbr(r.id, !r.featureFbr); load()
  }
  const delPlan = async (p) => { if (await ui.confirm({ title: 'Delete plan', message: `Delete plan "${p.name}"?`, confirmText: 'Delete', danger: true })) { try { await platform.deletePlan(p.id); load() } catch (e) { ui.alert(e.message) } } }
  const delRestaurant = async (r) => {
    const typed = await ui.prompt({
      title: `Delete ${r.name}`,
      message: `This permanently deletes ${r.name} and ALL its data — menu, tables, staff, orders, invoices, payments. This cannot be undone.\n\nType the restaurant name to confirm:`,
      placeholder: r.name, confirmText: 'Delete forever', danger: true, required: true,
    })
    if (typed == null) return
    if (typed.trim().toLowerCase() !== r.name.toLowerCase()) { ui.alert("The name didn't match — nothing was deleted."); return }
    try { await platform.deleteRestaurant(r.id); load() } catch (e) { ui.alert(e.message) }
  }
  const resetPassword = async (r) => {
    if (!await ui.confirm({ title: `Reset ${r.name}'s password?`, message: "The owner's current password stops working straight away. You'll see the new one once.", confirmText: 'Reset password', danger: true })) return
    try {
      const res = await platform.resetOwnerPassword(r.id)
      await ui.alert(`Sign-in: ${res.username}\nNew password: ${res.password}\n\nGive this to the owner now. It won't be shown again.`)
    } catch (e) { ui.alert(e.message) }
  }
  const logout = () => { platformAuth.clear(); nav('/platform/login') }
  const admin = platformAuth.get()

  return (
    <div>
      <div className="topbar">
        <span className="brand">Dine In Operator</span>
        <span className="muted" style={{ marginLeft: 'auto', fontSize: 13 }}>{admin?.displayName}</span>
        <button className="ghost" onClick={logout}>Sign out</button>
      </div>
      <div className="wrap">
        {err && <div style={{ color: 'var(--red)', marginBottom: 10 }}>{err}</div>}

        {stats && (
          <div className="grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))' }}>
            <Stat icon={<BankIcon size={20} />} num={stats.restaurants} label="Restaurants" />
            <Stat icon={<CheckIcon size={20} />} num={stats.active} label="Active" />
            <Stat icon={<WarnIcon size={20} />} num={stats.suspended} label="Suspended" />
            <Stat icon={<CashIcon size={20} />} num={money(stats.totalSales)} label="Total sales" />
            <Stat icon={<ReceiptIcon size={20} />} num={stats.totalBills} label="Total bills" />
          </div>
        )}

        {/* Plan catalogue */}
        <div className="row between" style={{ marginTop: 8 }}>
          <div className="cat-title" style={{ margin: 0 }}>Subscription plans</div>
          <button className="primary" onClick={() => setEditPlan('new')}>+ New plan</button>
        </div>
        <div className="grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))' }}>
          {plans.map((p) => (
            <div key={p.id} className="card">
              <div className="row between">
                <b>{p.name}</b>
                {!p.isActive && <span className="pill s-Served">hidden</span>}
              </div>
              <div className="big" style={{ color: 'var(--accent)', margin: '4px 0' }}>{p.pricePkr > 0 ? money(p.pricePkr) : 'Free'}<span className="muted" style={{ fontSize: 12, fontWeight: 400 }}>{p.pricePkr > 0 ? '/mo' : ''}</span></div>
              {p.setupFeePkr > 0 && <div className="muted" style={{ fontSize: 12, marginBottom: 4 }}>+ {money(p.setupFeePkr)} one-time setup</div>}
              <div style={{ fontSize: 12 }}>{FEATURES.filter(([k]) => p[k]).map(([, l]) => l).join(' · ') || 'Menu only'}</div>
              <div className="row" style={{ gap: 6, marginTop: 10 }}>
                <button className="ghost" onClick={() => setEditPlan(p)}>Edit</button>
                <button className="ghost" onClick={() => delPlan(p)}><TrashIcon size={15} /></button>
              </div>
            </div>
          ))}
        </div>

        {/* Restaurants */}
        <div className="cat-title">Restaurants</div>
        <div className="card" style={{ overflowX: 'auto' }}>
          <table className="tbl">
            <thead><tr><th>Restaurant</th><th>Plan</th><th>Billing</th><th>Subscription</th><th>Sales</th><th>FBR</th><th>Access</th></tr></thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.id}>
                  <td><b>{r.name}</b><div className="muted" style={{ fontSize: 12 }}>/{r.slug}</div></td>
                  <td>
                    <select value={plans.find((p) => p.name === r.plan)?.id || ''} onChange={(e) => changePlan(r, e.target.value)} style={{ padding: '5px 8px' }}>
                      <option value="" disabled>{r.plan}</option>
                      {plans.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                    </select>
                  </td>
                  <td style={{ fontSize: 13 }}>
                    <div>{money(r.monthlyPkr)}<span className="muted">/mo</span></div>
                    <div className="muted" style={{ fontSize: 12 }}>
                      {r.setupFeePaid ? <><CheckIcon size={12} /> setup paid</> : `setup ${money(r.setupFeePkr)} due`}
                    </div>
                  </td>
                  <td>
                    {subBadge(r)}
                    <button className="ghost" style={{ display: 'block', marginTop: 4, padding: '3px 8px', fontSize: 12 }} onClick={() => setPayFor(r)}><CashIcon size={14} /> Record payment</button>
                  </td>
                  <td>{money(r.sales)} <span className="muted" style={{ fontSize: 12 }}>· {r.bills}</span></td>
                  <td>
                    <button className={r.featureFbr ? 'primary' : 'ghost'} onClick={() => toggleFbr(r)} title="Grant/revoke FBR fiscal integration">
                      {r.featureFbr ? <><CheckIcon size={13} /> On</> : 'Off'}
                    </button>
                  </td>
                  <td>
                    <div className="row" style={{ gap: 4 }}>
                      <button className={r.status === 'Active' ? 'ghost' : 'green'} onClick={() => toggle(r)}>
                        {r.status === 'Active' ? 'Lock' : 'Unlock'}
                      </button>
                      <button className="ghost" onClick={() => resetPassword(r)}>Reset password</button>
                      <button className="ghost" title="Delete restaurant and all its data" onClick={() => delRestaurant(r)}><TrashIcon size={15} /></button>
                    </div>
                  </td>
                </tr>
              ))}
              {rows.length === 0 && <tr><td colSpan="7" className="muted center">No restaurants yet.</td></tr>}
            </tbody>
          </table>
        </div>
      </div>

      {editPlan && <PlanEditor plan={editPlan === 'new' ? null : editPlan} onClose={() => setEditPlan(null)} onSaved={() => { setEditPlan(null); load() }} />}
      {payFor && <PaymentModal r={payFor} onClose={() => setPayFor(null)} onSaved={() => { setPayFor(null); load() }} />}
    </div>
  )
}

function PlanEditor({ plan, onClose, onSaved }) {
  const [f, setF] = useState(() => plan || { name: '', description: '', pricePkr: 0, setupFeePkr: 0, sortOrder: 99, isActive: true, ordering: false, kitchen: false, waiter: false, billing: false, inventory: false, reports: false })
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')
  const set = (k, v) => setF((s) => ({ ...s, [k]: v }))

  const save = async () => {
    setBusy(true); setErr('')
    const payload = { ...f, pricePkr: Number(f.pricePkr), setupFeePkr: Number(f.setupFeePkr) || 0, sortOrder: Number(f.sortOrder) }
    try { if (plan) await platform.updatePlan(plan.id, payload); else await platform.createPlan(payload); onSaved() }
    catch (e) { setErr(e.message) }
    setBusy(false)
  }

  return (
    <div className="overlay" onClick={(e) => { if (e.target.className === 'overlay') onClose() }}>
      <div className="modal">
        <div className="row between"><div className="big">{plan ? 'Edit plan' : 'New plan'}</div><button className="ghost" onClick={onClose}><CloseIcon size={16} /></button></div>
        <input placeholder="Plan name" value={f.name} onChange={(e) => set('name', e.target.value)} style={{ width: '100%', margin: '10px 0' }} />
        <input placeholder="Description" value={f.description} onChange={(e) => set('description', e.target.value)} style={{ width: '100%', marginBottom: 10 }} />
        <div className="row" style={{ gap: 10, marginBottom: 10 }}>
          <div style={{ flex: 1 }}><label className="muted" style={{ fontSize: 12 }}>Monthly (Rs/mo)</label><input type="number" value={f.pricePkr} onChange={(e) => set('pricePkr', e.target.value)} style={{ width: '100%' }} /></div>
          <div style={{ flex: 1 }}><label className="muted" style={{ fontSize: 12 }}>Setup fee (one-time)</label><input type="number" value={f.setupFeePkr} onChange={(e) => set('setupFeePkr', e.target.value)} style={{ width: '100%' }} /></div>
          <div style={{ width: 80 }}><label className="muted" style={{ fontSize: 12 }}>Order</label><input type="number" value={f.sortOrder} onChange={(e) => set('sortOrder', e.target.value)} style={{ width: '100%' }} /></div>
        </div>
        <div className="cat-title" style={{ fontSize: 13 }}>Included features</div>
        <div className="grid" style={{ gridTemplateColumns: '1fr 1fr' }}>
          {FEATURES.map(([k, label]) => (
            <label key={k} className="row" style={{ gap: 8, cursor: 'pointer', padding: '4px 0' }}>
              <input type="checkbox" checked={!!f[k]} onChange={(e) => set(k, e.target.checked)} /> {label}
            </label>
          ))}
        </div>
        <p className="muted" style={{ fontSize: 12, margin: '6px 0 0' }}>
          Order + Billing also unlock the takeaway &amp; delivery QR. Billing unlocks loyalty points. Star ratings and the Google review button come with every plan.
        </p>
        <label className="row" style={{ gap: 8, cursor: 'pointer', margin: '10px 0' }}>
          <input type="checkbox" checked={f.isActive} onChange={(e) => set('isActive', e.target.checked)} /> Show on sign-up
        </label>
        {err && <div style={{ color: 'var(--red)', fontSize: 14, marginBottom: 8 }}>{err}</div>}
        <div className="row" style={{ gap: 8 }}>
          <button onClick={onClose}>Cancel</button>
          <button className="green" style={{ marginLeft: 'auto' }} disabled={busy || !f.name.trim()} onClick={save}>Save plan</button>
        </div>
      </div>
    </div>
  )
}

// Subscription state badge derived from the paid-through date.
function subBadge(r) {
  if (!r.paidThrough) return <span className="pill s-Cancelled">Unpaid</span>
  const d = Math.ceil((new Date(r.paidThrough) - Date.now()) / 86400000)
  const till = new Date(r.paidThrough).toLocaleDateString()
  if (d < 0) return <span className="pill s-Cancelled" title={`Expired ${till}`}>Overdue {-d}d</span>
  if (d <= 7) return <span className="pill s-Placed" title={`Paid till ${till}`}>Due in {d}d</span>
  return <span className="pill s-Served" title={`Paid till ${till}`}>Paid · {till}</span>
}

// Operator records an offline subscription payment (setup fee or N months).
function PaymentModal({ r, onClose, onSaved }) {
  const [kind, setKind] = useState(r.setupFeePaid ? 'Monthly' : 'Setup')
  const [months, setMonths] = useState(1)
  const [amount, setAmount] = useState('')
  const [note, setNote] = useState('')
  const [hist, setHist] = useState([])
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')

  useEffect(() => { platform.payments(r.id).then(setHist).catch(() => {}) }, [r.id])
  // Suggest the amount from the plan when the operator hasn't typed one.
  const suggested = kind === 'Setup' ? r.setupFeePkr : r.monthlyPkr * (Number(months) || 1)
  const amt = amount === '' ? suggested : Number(amount)

  const save = async () => {
    setBusy(true); setErr('')
    try {
      await platform.recordPayment(r.id, { kind, months: kind === 'Monthly' ? Number(months) || 1 : 0, amountPkr: amt, note })
      onSaved()
    } catch (e) { setErr(e.message) }
    setBusy(false)
  }

  return (
    <div className="overlay" onMouseDown={(e) => { if (e.target.classList.contains('overlay')) onClose() }}>
      <div className="modal">
        <div className="row between"><div className="big">Record payment — {r.name}</div><button className="ghost" onClick={onClose}><CloseIcon size={16} /></button></div>
        <div className="muted" style={{ fontSize: 13, margin: '4px 0 12px' }}>
          {subBadge(r)} · Plan {r.plan} · {money(r.monthlyPkr)}/mo · setup {r.setupFeePaid ? 'paid' : money(r.setupFeePkr)}
        </div>

        <div className="row" style={{ gap: 8, marginBottom: 10, flexWrap: 'wrap' }}>
          <select value={kind} onChange={(e) => { setKind(e.target.value); setAmount('') }}>
            <option value="Setup" disabled={r.setupFeePaid}>One-time setup fee{r.setupFeePaid ? ' (paid)' : ''}</option>
            <option value="Monthly">Monthly subscription</option>
          </select>
          {kind === 'Monthly' && (
            <label className="row" style={{ gap: 6 }}>
              <span className="muted" style={{ fontSize: 13 }}>Months</span>
              <input type="number" min="1" value={months} onChange={(e) => { setMonths(e.target.value); setAmount('') }} style={{ width: 70 }} />
            </label>
          )}
        </div>
        <div className="row" style={{ gap: 8, marginBottom: 10, flexWrap: 'wrap' }}>
          <label className="row" style={{ gap: 6 }}>
            <span className="muted" style={{ fontSize: 13 }}>Amount Rs</span>
            <input type="number" value={amt} onChange={(e) => setAmount(e.target.value)} style={{ width: 120 }} />
          </label>
          <input placeholder="Note (bank ref, JazzCash id…)" value={note} onChange={(e) => setNote(e.target.value)} style={{ flex: 1, minWidth: 160 }} />
        </div>
        {kind === 'Monthly' && <div className="muted" style={{ fontSize: 12, marginBottom: 8 }}>Extends the paid-through date by {Number(months) || 1} {(Number(months) || 1) === 1 ? 'month' : 'months'} and unlocks the restaurant.</div>}
        {err && <div style={{ color: 'var(--red)', fontSize: 14, marginBottom: 8 }}>{err}</div>}
        <div className="row" style={{ gap: 8 }}>
          <button onClick={onClose}>Cancel</button>
          <button className="green" style={{ marginLeft: 'auto' }} disabled={busy || amt <= 0} onClick={save}>{busy ? 'Saving…' : `Record ${money(amt)}`}</button>
        </div>

        {hist.length > 0 && (
          <>
            <div className="cat-title" style={{ fontSize: 13, marginTop: 14 }}>Payment history</div>
            <div style={{ maxHeight: 180, overflowY: 'auto' }}>
              {hist.map((p) => (
                <div key={p.id} className="row between" style={{ fontSize: 13, padding: '5px 0', borderBottom: '1px solid var(--line)' }}>
                  <span>{new Date(p.paidAt).toLocaleDateString()} · {p.kind}{p.months ? ` ×${p.months}` : ''}{p.note ? ` · ${p.note}` : ''}</span>
                  <b>{money(p.amountPkr)}</b>
                </div>
              ))}
            </div>
          </>
        )}
      </div>
    </div>
  )
}

function Stat({ icon, num, label }) {
  return (
    <div className="card stat"><div className="chip">{icon}</div><div className="num">{num}</div><div className="lbl">{label}</div></div>
  )
}
