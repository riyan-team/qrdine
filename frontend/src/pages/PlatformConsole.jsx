import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { platform, money } from '../api'
import { platformAuth } from '../auth'

const FEATURES = [['ordering', 'Order'], ['kitchen', 'Kitchen'], ['waiter', 'Waiter'], ['billing', 'Billing'], ['inventory', 'Inventory'], ['reports', 'Reports']]

export default function PlatformConsole() {
  const nav = useNavigate()
  const [stats, setStats] = useState(null)
  const [rows, setRows] = useState([])
  const [plans, setPlans] = useState([])
  const [editPlan, setEditPlan] = useState(null)   // plan object or 'new'
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
    if (next === 'Suspended' && !window.confirm(`Suspend ${r.name}? Their app will stop working.`)) return
    await platform.setStatus(r.id, next); load()
  }
  const toggleFbr = async (r) => {
    if (r.featureFbr && !window.confirm(`Turn OFF FBR for ${r.name}? Their invoice sync will stop.`)) return
    await platform.setFbr(r.id, !r.featureFbr); load()
  }
  const delPlan = async (p) => { if (window.confirm(`Delete plan "${p.name}"?`)) { try { await platform.deletePlan(p.id); load() } catch (e) { alert(e.message) } } }
  const logout = () => { platformAuth.clear(); nav('/platform/login') }
  const admin = platformAuth.get()

  return (
    <div>
      <div className="topbar">
        <span className="brand">◱ QrDine Operator</span>
        <span className="muted" style={{ marginLeft: 'auto', fontSize: 13 }}>{admin?.displayName}</span>
        <button className="ghost" onClick={logout}>Sign out</button>
      </div>
      <div className="wrap">
        {err && <div style={{ color: 'var(--red)', marginBottom: 10 }}>{err}</div>}

        {stats && (
          <div className="grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))' }}>
            <Stat icon="🏪" num={stats.restaurants} label="Restaurants" />
            <Stat icon="✅" num={stats.active} label="Active" />
            <Stat icon="⛔" num={stats.suspended} label="Suspended" />
            <Stat icon="💰" num={money(stats.totalSales)} label="Total sales" />
            <Stat icon="🧾" num={stats.totalBills} label="Total bills" />
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
              <div className="big" style={{ color: 'var(--accent)', margin: '4px 0' }}>{p.pricePkr > 0 ? money(p.pricePkr) : 'Free'}</div>
              <div style={{ fontSize: 12 }}>{FEATURES.filter(([k]) => p[k]).map(([, l]) => l).join(' · ') || 'Menu only'}</div>
              <div className="row" style={{ gap: 6, marginTop: 10 }}>
                <button className="ghost" onClick={() => setEditPlan(p)}>Edit</button>
                <button className="ghost" onClick={() => delPlan(p)}>🗑</button>
              </div>
            </div>
          ))}
        </div>

        {/* Restaurants */}
        <div className="cat-title">Restaurants</div>
        <div className="card" style={{ overflowX: 'auto' }}>
          <table className="tbl">
            <thead><tr><th>Restaurant</th><th>Plan</th><th>Staff</th><th>Tables</th><th>Sales</th><th>FBR</th><th>Status</th></tr></thead>
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
                  <td>{r.staff}</td>
                  <td>{r.tables}</td>
                  <td>{money(r.sales)} <span className="muted" style={{ fontSize: 12 }}>· {r.bills}</span></td>
                  <td>
                    <button className={r.featureFbr ? 'primary' : 'ghost'} onClick={() => toggleFbr(r)} title="Grant/revoke FBR fiscal integration">
                      {r.featureFbr ? '✓ On' : 'Off'}
                    </button>
                  </td>
                  <td>
                    <button className={r.status === 'Active' ? 'ghost' : 'primary'} onClick={() => toggle(r)}>
                      {r.status === 'Active' ? '⛔ Suspend' : '▶ Activate'}
                    </button>
                  </td>
                </tr>
              ))}
              {rows.length === 0 && <tr><td colSpan="7" className="muted center">No restaurants yet.</td></tr>}
            </tbody>
          </table>
        </div>
      </div>

      {editPlan && <PlanEditor plan={editPlan === 'new' ? null : editPlan} onClose={() => setEditPlan(null)} onSaved={() => { setEditPlan(null); load() }} />}
    </div>
  )
}

function PlanEditor({ plan, onClose, onSaved }) {
  const [f, setF] = useState(() => plan || { name: '', description: '', pricePkr: 0, sortOrder: 99, isActive: true, ordering: false, kitchen: false, waiter: false, billing: false, inventory: false, reports: false })
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')
  const set = (k, v) => setF((s) => ({ ...s, [k]: v }))

  const save = async () => {
    setBusy(true); setErr('')
    const payload = { ...f, pricePkr: Number(f.pricePkr), sortOrder: Number(f.sortOrder) }
    try { if (plan) await platform.updatePlan(plan.id, payload); else await platform.createPlan(payload); onSaved() }
    catch (e) { setErr(e.message) }
    setBusy(false)
  }

  return (
    <div className="overlay" onClick={(e) => { if (e.target.className === 'overlay') onClose() }}>
      <div className="modal">
        <div className="row between"><div className="big">{plan ? 'Edit plan' : 'New plan'}</div><button className="ghost" onClick={onClose}>✕</button></div>
        <input placeholder="Plan name" value={f.name} onChange={(e) => set('name', e.target.value)} style={{ width: '100%', margin: '10px 0' }} />
        <input placeholder="Description" value={f.description} onChange={(e) => set('description', e.target.value)} style={{ width: '100%', marginBottom: 10 }} />
        <div className="row" style={{ gap: 10, marginBottom: 10 }}>
          <div style={{ flex: 1 }}><label className="muted" style={{ fontSize: 12 }}>Price (Rs/mo)</label><input type="number" value={f.pricePkr} onChange={(e) => set('pricePkr', e.target.value)} style={{ width: '100%' }} /></div>
          <div style={{ width: 90 }}><label className="muted" style={{ fontSize: 12 }}>Order</label><input type="number" value={f.sortOrder} onChange={(e) => set('sortOrder', e.target.value)} style={{ width: '100%' }} /></div>
        </div>
        <div className="cat-title" style={{ fontSize: 13 }}>Included features</div>
        <div className="grid" style={{ gridTemplateColumns: '1fr 1fr' }}>
          {FEATURES.map(([k, label]) => (
            <label key={k} className="row" style={{ gap: 8, cursor: 'pointer', padding: '4px 0' }}>
              <input type="checkbox" checked={!!f[k]} onChange={(e) => set(k, e.target.checked)} /> {label}
            </label>
          ))}
        </div>
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

function Stat({ icon, num, label }) {
  return (
    <div className="card stat"><div className="chip">{icon}</div><div className="num">{num}</div><div className="lbl">{label}</div></div>
  )
}
