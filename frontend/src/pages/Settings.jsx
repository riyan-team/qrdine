import { useEffect, useRef, useState } from 'react'
import { api, roleLabel } from '../api'
import { auth } from '../auth'
import { ui, toast } from '../ui.jsx'
import { friendlyError } from '../errors'
import { PasswordInput } from '../forms.jsx'
import { EditIcon, TrashIcon, KeyIcon, PlusIcon, ImageIcon } from '../icons.jsx'

const ROLES = ['Owner', 'Manager', 'Cashier', 'Kitchen', 'Waiter']

const PERM_LABELS = {
  'orders.create': 'Take orders (waiter/reception)',
  'orders.approve': 'Approve orders', 'orders.cook': 'Kitchen: cook & ready',
  'orders.serve': 'Waiter: assign & serve', 'orders.pay': 'Billing & payment',
  'orders.cancel': 'Cancel orders', 'menu.manage': 'Manage menu',
  'inventory.manage': 'Manage inventory', 'settings.manage': 'Manage tables & staff',
  'reports.view': 'View reports & dashboard', 'invoice.sync': 'Sync invoices with FBR',
  'expenses.manage': 'Record expenses', 'suppliers.manage': 'Supplier khata & purchases',
  'orders.discount': 'Give discounts on bills', 'customers.manage': 'Customer khata', 'payroll.manage': 'Salaries & attendance',
}

// Which plan feature each permission belongs to (unlisted = always available).
// Permissions for modules not in the plan are hidden from the editor.
const PERM_FEATURE = {
  'orders.create': 'ordering',
  'orders.approve': 'ordering', 'orders.pay': 'ordering', 'orders.cancel': 'ordering',
  'orders.cook': 'kitchen', 'orders.serve': 'waiter',
  'inventory.manage': 'inventory', 'reports.view': 'reports', 'invoice.sync': 'fbr',
  'expenses.manage': 'reports', 'suppliers.manage': 'inventory',
  'orders.discount': 'billing', 'customers.manage': 'billing', 'payroll.manage': 'reports',
}
const permInPlan = (perm) => { const f = PERM_FEATURE[perm]; return !f || auth.hasFeature(f) }

// Owner/manager settings, grouped into tabs so related settings sit together
// instead of one long, confusing list.
const SETTINGS_TABS = [
  { id: 'billing', label: 'Billing & tax' },
  { id: 'receipt', label: 'Receipt & branding' },
  { id: 'venue', label: 'Branches & tables' },
  { id: 'staff', label: 'Staff & roles' },
  { id: 'service', label: 'Service' },
  { id: 'fbr', label: 'FBR / tax filing' },
  { id: 'account', label: 'Your account' },
]

export default function Settings() {
  const isOwner = auth.role() === 'Owner'
  const [tab, setTab] = useState('billing')
  return (
    <div className="wrap">
      <div className="h">Settings</div>
      <p className="muted" style={{ marginTop: -4 }}>Pick a section.</p>

      <div className="settings-nav">
        {SETTINGS_TABS.map((t) => (
          <button key={t.id} className={tab === t.id ? 'on' : ''} onClick={() => setTab(t.id)}>{t.label}</button>
        ))}
      </div>

      <div className="settings-panel">
        {tab === 'billing' && <BillingSettings />}
        {tab === 'receipt' && <ReceiptSettings />}
        {tab === 'venue' && <><Branches /><Tables /></>}
        {tab === 'staff' && <><Staff />{isOwner && <Permissions />}</>}
        {tab === 'service' && <ServiceSettings />}
        {tab === 'fbr' && <FbrSettings />}
        {tab === 'account' && <AccountSettings />}
      </div>
    </div>
  )
}

// The signed-in user changes their own password.
function AccountSettings() {
  const [cur, setCur] = useState('')
  const [nw, setNw] = useState('')
  const [msg, setMsg] = useState('')
  const [err, setErr] = useState('')
  const [busy, setBusy] = useState(false)
  const staff = auth.getStaff()
  const save = async () => {
    setErr(''); setMsg(''); setBusy(true)
    try {
      // A new password signs out every other device; this one carries on with the fresh pass.
      const res = await api.changePassword(cur, nw)
      if (res?.token) auth.setStaff({ ...auth.getStaff(), token: res.token })
      setMsg('Password updated.'); setCur(''); setNw(''); setTimeout(() => setMsg(''), 2000)
    }
    catch (e) { setErr(e.message) }
    setBusy(false)
  }
  return (
    <div className="card" style={{ marginBottom: 16 }}>
      <div className="row between"><b>Your account</b><span className="muted" style={{ fontSize: 13 }}>{staff?.displayName} · {roleLabel(staff?.role)}</span></div>
      <p className="muted" style={{ fontSize: 13, margin: '4px 0 10px' }}>Change your own password.</p>
      <div className="row" style={{ gap: 8, flexWrap: 'wrap' }}>
        <PasswordInput placeholder="Current password" value={cur} onChange={(e) => setCur(e.target.value)} style={{ flex: 1, minWidth: 160 }} />
        <PasswordInput placeholder="New password (6+)" value={nw} onChange={(e) => setNw(e.target.value)} style={{ flex: 1, minWidth: 160 }} />
        <button className="primary" disabled={busy || !cur || nw.length < 6} onClick={save}>{msg || 'Update password'}</button>
      </div>
      {err && <div style={{ color: 'var(--red)', fontSize: 13, marginTop: 6 }}>{err}</div>}
    </div>
  )
}

// Billing rules: Tax/GST on/off + name + rate, discount on/off (%/fixed), receipt
// on/off and stock tracking on/off. These drive every bill automatically. Only
// reachable by staff with settings.manage (route-gated + server-enforced).
function BillingSettings() {
  const [s, setS] = useState(null)
  const [msg, setMsg] = useState('')
  const [err, setErr] = useState('')
  const [busy, setBusy] = useState(false)
  useEffect(() => { api.getBillingSettings().then(setS).catch((e) => setErr(friendlyError(e))) }, [])
  if (!s) return null
  const set = (k, v) => setS((x) => ({ ...x, [k]: v }))
  const flash = (m) => { setMsg(m); setTimeout(() => setMsg(''), 1500) }

  const save = async () => {
    setErr(''); setBusy(true)
    try {
      await api.setBillingSettings({
        taxEnabled: !!s.taxEnabled, taxName: (s.taxName || 'GST').trim(),
        taxPercent: Number(s.taxPercent) || 0, taxPercentCard: s.taxPercentCard === '' || s.taxPercentCard == null ? null : Number(s.taxPercentCard),
        serviceChargePercent: Number(s.serviceChargePercent) || 0,
        discountEnabled: !!s.discountEnabled, discountIsPercent: !!s.discountIsPercent,
        discountValue: Number(s.discountValue) || 0,
        receiptEnabled: !!s.receiptEnabled, stockTrackingEnabled: !!s.stockTrackingEnabled,
      })
      flash('Saved.')
    } catch (e) { setErr(friendlyError(e)) }
    setBusy(false)
  }

  return (
    <div className="card" style={{ marginBottom: 16 }}>
      <div className="row between"><b>Billing, tax &amp; receipt</b>{msg && <span className="muted" style={{ fontSize: 13 }}>{msg}</span>}</div>
      <p className="muted" style={{ fontSize: 13, margin: '4px 0 12px' }}>These apply to every bill automatically. Tax is worked out after any discount.</p>

      <div className="set-grid">
        {/* Tax / GST */}
        <div className="set-block">
          <Toggle label="Charge Tax / GST" checked={s.taxEnabled} onChange={(v) => set('taxEnabled', v)} />
          {s.taxEnabled && (
            <div className="row" style={{ gap: 8, marginTop: 8, flexWrap: 'wrap' }}>
              <label className="fld"><span>Tax name</span>
                <input value={s.taxName || ''} placeholder="GST" onChange={(e) => set('taxName', e.target.value)} /></label>
              <label className="fld"><span>Rate %</span>
                <input type="number" min="0" max="100" step="0.01" value={s.taxPercent} onChange={(e) => set('taxPercent', e.target.value)} /></label>
              <label className="fld"><span>Card rate %</span>
                <input type="number" min="0" max="100" step="0.01" placeholder="Same" value={s.taxPercentCard ?? ''} onChange={(e) => set('taxPercentCard', e.target.value)} /></label>
              <label className="fld"><span>Service %</span>
                <input type="number" min="0" max="100" step="0.01" value={s.serviceChargePercent} onChange={(e) => set('serviceChargePercent', e.target.value)} /></label>
            </div>
          )}
        </div>

        {/* Discount */}
        <div className="set-block">
          <Toggle label="Apply a discount" checked={s.discountEnabled} onChange={(v) => set('discountEnabled', v)} />
          {s.discountEnabled && (
            <div className="row" style={{ gap: 8, marginTop: 8, flexWrap: 'wrap', alignItems: 'flex-end' }}>
              <label className="fld"><span>Type</span>
                <select value={s.discountIsPercent ? 'pct' : 'fixed'} onChange={(e) => set('discountIsPercent', e.target.value === 'pct')}>
                  <option value="pct">Percentage</option>
                  <option value="fixed">Fixed ({s.currency || 'Rs'})</option>
                </select></label>
              <label className="fld"><span>{s.discountIsPercent ? 'Percent %' : `Amount (${s.currency || 'Rs'})`}</span>
                <input type="number" min="0" step="0.01" value={s.discountValue} onChange={(e) => set('discountValue', e.target.value)} /></label>
            </div>
          )}
        </div>

        {/* Receipt + stock */}
        <div className="set-block">
          <Toggle label="Show / print receipt after settlement" checked={s.receiptEnabled} onChange={(v) => set('receiptEnabled', v)} />
          <div style={{ height: 8 }} />
          <Toggle label="Track stock (deduct recipe ingredients on orders)" checked={s.stockTrackingEnabled} onChange={(v) => set('stockTrackingEnabled', v)} />
        </div>
      </div>

      {err && <div className="form-err" style={{ marginTop: 8 }}>{err}</div>}
      <div className="row" style={{ marginTop: 12 }}>
        <button className="primary" style={{ marginLeft: 'auto' }} disabled={busy} onClick={save}>{busy ? 'Saving…' : 'Save billing settings'}</button>
      </div>
    </div>
  )
}

// A small on/off switch (accessible label + checkbox).
function Toggle({ label, checked, onChange }) {
  return (
    <label className="switch-row">
      <span className={'switch' + (checked ? ' on' : '')} onClick={(e) => { e.preventDefault(); onChange(!checked) }}>
        <span className="knob" />
      </span>
      <span>{label}</span>
      <input type="checkbox" checked={!!checked} onChange={(e) => onChange(e.target.checked)} style={{ display: 'none' }} />
    </label>
  )
}

// Receipt branding: a centred logo plus the printed header (address/phone) and
// footer (thank-you) lines. Shown on every 80mm bill and invoice.
function ReceiptSettings() {
  const [s, setS] = useState(null)
  const [msg, setMsg] = useState('')
  const [err, setErr] = useState('')
  const [busy, setBusy] = useState(false)
  const fileRef = useRef(null)
  const load = () => api.getReceiptSettings().then(setS).catch((e) => setErr(e.message))
  useEffect(() => { load() }, [])
  if (!s) return null

  const set = (k, v) => setS((x) => ({ ...x, [k]: v }))
  const flash = (m) => { setMsg(m); setTimeout(() => setMsg(''), 1500) }

  const save = async () => {
    setErr(''); setBusy(true)
    try {
      await api.setReceiptSettings({ receiptHeader: s.receiptHeader || '', receiptFooter: s.receiptFooter || '', logoUrl: s.logoUrl || '' })
      flash('Saved.')
    } catch (e) { setErr(friendlyError(e)) }
    setBusy(false)
  }
  const onFile = async (e) => {
    const file = e.target.files?.[0]; if (!file) return
    setErr(''); setBusy(true)
    try { const { logoUrl } = await api.uploadLogo(file); set('logoUrl', logoUrl); flash('Logo uploaded.') }
    catch (e2) { setErr(friendlyError(e2)) }
    setBusy(false); if (fileRef.current) fileRef.current.value = ''
  }
  const removeLogo = async () => {
    setBusy(true)
    try { await api.setReceiptSettings({ receiptHeader: s.receiptHeader || '', receiptFooter: s.receiptFooter || '', logoUrl: '' }); set('logoUrl', ''); flash('Logo removed.') }
    catch (e) { setErr(friendlyError(e)) }
    setBusy(false)
  }

  return (
    <div className="card" style={{ marginBottom: 16 }}>
      <div className="row between"><b>Receipt &amp; branding</b>{msg && <span className="muted" style={{ fontSize: 13 }}>{msg}</span>}</div>
      <p className="muted" style={{ fontSize: 13, margin: '4px 0 12px' }}>Your logo prints centred at the top of every 80&nbsp;mm bill, with the header under it and the footer at the foot.</p>

      <div className="receipt-brand">
        {/* Logo — always centred */}
        <div className="logo-box">
          {s.logoUrl
            ? <img src={s.logoUrl} alt="Logo" className="logo-preview" />
            : <div className="logo-empty"><ImageIcon size={26} /><span>No logo</span></div>}
          <div className="row" style={{ gap: 8, marginTop: 10, justifyContent: 'center' }}>
            <input ref={fileRef} type="file" accept="image/png,image/jpeg,image/webp" onChange={onFile} style={{ display: 'none' }} />
            <button disabled={busy} onClick={() => fileRef.current?.click()}>{s.logoUrl ? 'Replace logo' : 'Upload logo'}</button>
            {s.logoUrl && <button className="ghost" disabled={busy} onClick={removeLogo}>Remove</button>}
          </div>
          <div className="muted" style={{ fontSize: 11, marginTop: 6, textAlign: 'center' }}>PNG, JPG or WebP · under 2&nbsp;MB</div>
        </div>

        <div className="brand-fields">
          <label className="fld">
            <span className="muted" style={{ fontSize: 13 }}>Header: address, phone, tagline</span>
            <textarea rows={2} placeholder={`${s.restaurantName || 'Your restaurant'}\nMain Boulevard, Lahore · 042-000000`}
              value={s.receiptHeader || ''} onChange={(e) => set('receiptHeader', e.target.value)} />
          </label>
          <label className="fld">
            <span className="muted" style={{ fontSize: 13 }}>Footer: thank-you or policy note</span>
            <textarea rows={2} placeholder="Thank you, please come again!"
              value={s.receiptFooter || ''} onChange={(e) => set('receiptFooter', e.target.value)} />
          </label>
          <div className="row" style={{ gap: 8 }}>
            <button className="primary" disabled={busy} onClick={save}>{busy ? 'Saving…' : 'Save'}</button>
          </div>
        </div>
      </div>
      {err && <div style={{ color: 'var(--red)', fontSize: 13, marginTop: 6 }}>{err}</div>}
    </div>
  )
}

// How waiters get attached to tables. Manual (default) vs automatic least-busy.
function ServiceSettings() {
  const [s, setS] = useState(null)
  const [msg, setMsg] = useState('')
  useEffect(() => { api.getServiceSettings().then(setS).catch(() => {}) }, [])
  if (!s) return null
  const save = async (val) => {
    setS({ waiterAutoAssign: val })
    try { await api.setServiceSettings({ waiterAutoAssign: val }); setMsg('Saved'); setTimeout(() => setMsg(''), 1200) }
    catch (e) { ui.alert(e.message) }
  }
  return (
    <div className="card" style={{ marginBottom: 16 }}>
      <div className="row between"><b>Waiter assignment</b>{msg && <span className="muted" style={{ fontSize: 13 }}>{msg}</span>}</div>
      <p className="muted" style={{ fontSize: 13, margin: '4px 0 10px' }}>How a waiter gets attached to a table’s orders.</p>
      <label className="row" style={{ gap: 8, marginBottom: 8, cursor: 'pointer', alignItems: 'flex-start' }}>
        <input type="radio" name="wa" checked={!s.waiterAutoAssign} onChange={() => save(false)} style={{ marginTop: 3 }} />
        <span><b>Manual:</b> waiters take their own tables (or a manager assigns). <span className="muted">Default. Best if you run fixed sections.</span></span>
      </label>
      <label className="row" style={{ gap: 8, cursor: 'pointer', alignItems: 'flex-start' }}>
        <input type="radio" name="wa" checked={s.waiterAutoAssign} onChange={() => save(true)} style={{ marginTop: 3 }} />
        <span><b>Automatic:</b> each approved order goes to the least-busy waiter. <span className="muted">Needs staff with the Waiter role.</span></span>
      </label>
    </div>
  )
}

// Restaurant-level FBR settings. The enable switch only works when the platform
// operator has turned FBR on for this account (featureFbr).
function FbrSettings() {
  const [s, setS] = useState(null)
  const [msg, setMsg] = useState('')
  const [err, setErr] = useState('')
  const load = () => api.getFbrSettings().then(setS).catch((e) => setErr(e.message))
  useEffect(() => { load() }, [])
  if (!s) return null

  const set = (k, v) => setS((x) => ({ ...x, [k]: v }))
  const save = async () => {
    setErr(''); setMsg('')
    try {
      await api.setFbrSettings({ fbrEnabled: s.fbrEnabled, businessName: s.businessName, ntn: s.ntn, strn: s.strn })
      setMsg('Saved.'); setTimeout(() => setMsg(''), 1500)
    } catch (e) { setErr(e.message) }
  }

  return (
    <div className="card" style={{ marginBottom: 16 }}>
      <div className="row between">
        <b>FBR fiscal integration</b>
        {!s.featureFbr && <span className="pill s-Placed">Not in your plan</span>}
      </div>
      <p className="muted" style={{ fontSize: 13, margin: '4px 0 10px' }}>
        {s.featureFbr
          ? 'File each settled invoice with FBR. Turn it on, set your seller identity, then configure each branch’s POS (mode + POSID) below.'
          : 'FBR filing isn’t enabled for your account. Ask the operator to enable it for your restaurant.'}
      </p>
      <label className="row" style={{ gap: 8, marginBottom: 10 }}>
        <input type="checkbox" disabled={!s.featureFbr} checked={s.fbrEnabled} onChange={(e) => set('fbrEnabled', e.target.checked)} />
        <span>Sync invoices with FBR</span>
      </label>
      <div className="row" style={{ gap: 8, flexWrap: 'wrap' }}>
        <input placeholder="Registered business name" value={s.businessName || ''} onChange={(e) => set('businessName', e.target.value)} style={{ flex: 2, minWidth: 180 }} />
        <input placeholder="NTN" value={s.ntn || ''} onChange={(e) => set('ntn', e.target.value)} style={{ flex: 1, minWidth: 110 }} />
        <input placeholder="STRN" value={s.strn || ''} onChange={(e) => set('strn', e.target.value)} style={{ flex: 1, minWidth: 110 }} />
        <button className="primary" disabled={!s.featureFbr} onClick={save}>{msg || 'Save'}</button>
      </div>
      {err && <div style={{ color: 'var(--red)', fontSize: 13, marginTop: 6 }}>{err}</div>}
    </div>
  )
}

function Branches() {
  const [rows, setRows] = useState([])
  const [f, setF] = useState({ name: '', address: '' })
  const [err, setErr] = useState('')
  const [fbrOn, setFbrOn] = useState(false)   // is FBR integration enabled for this restaurant?
  const [fbrFor, setFbrFor] = useState(null)
  const [editFor, setEditFor] = useState(null)
  const load = () => {
    api.listBranches().then(setRows).catch((e) => setErr(e.message))
    api.getFbrSettings().then((s) => setFbrOn(!!(s.featureFbr && s.fbrEnabled))).catch(() => {})
  }
  useEffect(() => { load() }, [])

  const add = async () => {
    setErr('')
    try { await api.addBranch({ name: f.name.trim(), address: f.address.trim(), isActive: true }); setF({ name: '', address: '' }); load() }
    catch (e) { setErr(e.message) }
  }
  const del = async (b) => { if (await ui.confirm({ title: 'Delete branch', message: `Delete branch "${b.name}"?`, confirmText: 'Delete', danger: true })) { try { await api.deleteBranch(b.id); load() } catch (e) { ui.alert(e.message) } } }

  return (
    <div className="card" style={{ marginBottom: 16 }}>
      <b>Branches</b>
      <p className="muted" style={{ fontSize: 13, margin: '4px 0 10px' }}>
        Each location has its own tables, QR codes and (when FBR is on) its own POS registration. Sales report per branch.
      </p>
      {rows.map((b) => (
        <div key={b.id} style={{ borderBottom: '1px solid var(--line)' }}>
          {editFor === b.id ? (
            <BranchEdit b={b} onDone={() => { setEditFor(null); load() }} onCancel={() => setEditFor(null)} />
          ) : (
            <div className="row between list-row" style={{ padding: '7px 0' }} onDoubleClick={() => setEditFor(b.id)}>
              <span><b>{b.name}</b> <span className="muted">· {b.address || 'no address'} · {b.tables} {b.tables === 1 ? 'table' : 'tables'}
                {fbrOn && b.fbrPosId ? ` · FBR ${b.fbrMode} POS ${b.fbrPosId}` : ''}</span></span>
              <div className="row" style={{ gap: 4 }}>
                {fbrOn && <button className="ghost btn-sm" onClick={() => setFbrFor(fbrFor === b.id ? null : b.id)}>FBR POS</button>}
                <button className="ghost icon-btn" title="Edit branch" onClick={() => setEditFor(b.id)}><EditIcon /></button>
                <button className="ghost icon-btn danger" title="Delete branch" onClick={() => del(b)}><TrashIcon /></button>
              </div>
            </div>
          )}
          {fbrOn && fbrFor === b.id && editFor !== b.id && <BranchFbr b={b} onSaved={() => { setFbrFor(null); load() }} />}
        </div>
      ))}
      <div className="cat-title" style={{ fontSize: 13, margin: '10px 0 6px' }}>Add branch</div>
      <div className="row" style={{ gap: 8 }}>
        <input placeholder="Branch name" value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} style={{ flex: 1 }} />
        <input placeholder="Address" value={f.address} onChange={(e) => setF({ ...f, address: e.target.value })} style={{ flex: 1 }} />
        <button className="primary" disabled={!f.name.trim()} onClick={add}><PlusIcon /> Add</button>
      </div>
      {err && <div style={{ color: 'var(--red)', fontSize: 13, marginTop: 6 }}>{err}</div>}
    </div>
  )
}

// Inline editor for a branch's name + address (opened by the Edit button or a
// double-click). FBR POS config stays on its own panel.
function BranchEdit({ b, onDone, onCancel }) {
  const [name, setName] = useState(b.name)
  const [address, setAddress] = useState(b.address || '')
  const [err, setErr] = useState('')
  const [busy, setBusy] = useState(false)
  const save = async () => {
    setErr(''); setBusy(true)
    try { await api.updateBranch(b.id, { ...b, name: name.trim(), address: address.trim() }); onDone() }
    catch (e) { setErr(friendlyError(e)); setBusy(false) }
  }
  return (
    <div className="edit-panel">
      <div className="row" style={{ gap: 8, flexWrap: 'wrap' }}>
        <input autoFocus placeholder="Branch name" value={name} onChange={(e) => setName(e.target.value)} style={{ flex: 1, minWidth: 140 }} />
        <input placeholder="Address" value={address} onChange={(e) => setAddress(e.target.value)} style={{ flex: 1, minWidth: 140 }} />
        <button className="primary" disabled={busy || !name.trim()} onClick={save}>Save</button>
        <button disabled={busy} onClick={onCancel}>Cancel</button>
      </div>
      {err && <div style={{ color: 'var(--red)', fontSize: 13, marginTop: 6 }}>{err}</div>}
    </div>
  )
}

// Per-branch FBR POS registration: Local (IMS component) vs Cloud gateway, the
// branch's default POSID/token, and (cloud) the static IP registered with FBR.
function BranchFbr({ b, onSaved }) {
  const [f, setF] = useState({
    name: b.name, address: b.address, isActive: b.isActive,
    fbrMode: b.fbrMode || 'Local', fbrSandbox: b.fbrSandbox ?? true,
    fbrPosId: b.fbrPosId || '', fbrToken: b.fbrToken || '',
    fbrStaticIp: b.fbrStaticIp || '', fbrEndpointOverride: b.fbrEndpointOverride || '',
  })
  const [err, setErr] = useState('')
  const set = (k, v) => setF((x) => ({ ...x, [k]: v }))
  const save = async () => {
    setErr('')
    try {
      await api.updateBranch(b.id, {
        ...f, fbrPosId: f.fbrPosId ? Number(f.fbrPosId) : null,
        fbrToken: f.fbrToken || null, fbrEndpointOverride: f.fbrEndpointOverride || null,
      })
      onSaved()
    } catch (e) { setErr(e.message) }
  }

  return (
    <div style={{ background: 'var(--surface2)', borderRadius: 8, margin: '4px 0 8px', padding: 12 }}>
      <div className="row" style={{ gap: 8, flexWrap: 'wrap', marginBottom: 8 }}>
        <label className="row" style={{ gap: 6 }}>
          <span className="muted" style={{ fontSize: 13 }}>Mode</span>
          <select value={f.fbrMode} onChange={(e) => set('fbrMode', e.target.value)}>
            <option value="Local">Local (IMS on POS PC)</option>
            <option value="Cloud">Cloud (FBR gateway)</option>
          </select>
        </label>
        <label className="row" style={{ gap: 6 }}>
          <input type="checkbox" checked={f.fbrSandbox} onChange={(e) => set('fbrSandbox', e.target.checked)} />
          <span style={{ fontSize: 13 }}>Sandbox (test)</span>
        </label>
      </div>
      <div className="row" style={{ gap: 8, flexWrap: 'wrap', marginBottom: 8 }}>
        <input placeholder="POSID (from FBR)" value={f.fbrPosId} onChange={(e) => set('fbrPosId', e.target.value)} style={{ width: 150 }} />
        {f.fbrMode === 'Cloud' && <input placeholder="Bearer token" value={f.fbrToken} onChange={(e) => set('fbrToken', e.target.value)} style={{ flex: 1, minWidth: 160 }} />}
        {f.fbrMode === 'Cloud' && <input placeholder="Registered static IP" value={f.fbrStaticIp} onChange={(e) => set('fbrStaticIp', e.target.value)} style={{ width: 150 }} />}
      </div>
      <div className="muted" style={{ fontSize: 12, marginBottom: 8 }}>
        {f.fbrMode === 'Local'
          ? 'Local mode posts to the FBR IMS component on this PC, with no IP whitelisting. Owner/manager can sync.'
          : 'Cloud mode posts to FBR’s gateway with the token above; the static IP must be the one registered with FBR.'}
      </div>
      <div className="row" style={{ gap: 8 }}>
        <button className="primary" onClick={save}>Save FBR settings</button>
      </div>
      {err && <div style={{ color: 'var(--red)', fontSize: 13, marginTop: 6 }}>{err}</div>}
    </div>
  )
}

function Permissions() {
  const [matrix, setMatrix] = useState(null)
  const [draft, setDraft] = useState({})   // { role: Set(perms) }
  const [savedRole, setSavedRole] = useState('')
  const roles = ['Manager', 'Cashier', 'Kitchen', 'Waiter']

  const load = () => api.getPermissions().then((m) => {
    setMatrix(m)
    const d = {}; roles.forEach((r) => { d[r] = new Set(m.roles[r] || []) }); setDraft(d)
  }).catch(() => {})
  useEffect(() => { load() }, [])
  if (!matrix) return null

  const toggle = (role, perm) => setDraft((d) => {
    const s = new Set(d[role]); s.has(perm) ? s.delete(perm) : s.add(perm)
    return { ...d, [role]: s }
  })
  const save = async (role) => {
    try {
      await api.setPermissions(role, [...draft[role]])
      setSavedRole(role); setTimeout(() => setSavedRole(''), 1500)
    } catch (e) { toast.error(friendlyError(e)) }
  }

  return (
    <div className="card" style={{ marginTop: 16 }}>
      <b>Role permissions</b>
      <p className="muted" style={{ fontSize: 13, margin: '4px 0 12px' }}>Choose what each role can do. Owner always has full access. Staff see changes next time they sign in.</p>
      <div style={{ overflowX: 'auto' }}>
        <table className="tbl" style={{ minWidth: 560 }}>
          <thead>
            <tr><th>Permission</th>{roles.map((r) => <th key={r} style={{ textAlign: 'center' }}>{roleLabel(r)}</th>)}</tr>
          </thead>
          <tbody>
            {matrix.all.filter(permInPlan).map((perm) => (
              <tr key={perm}>
                <td>{PERM_LABELS[perm] || perm}</td>
                {roles.map((r) => (
                  <td key={r} style={{ textAlign: 'center' }}>
                    <input type="checkbox" checked={draft[r]?.has(perm) || false} onChange={() => toggle(r, perm)} />
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="row" style={{ gap: 8, marginTop: 12, flexWrap: 'wrap' }}>
        {roles.map((r) => (
          <button key={r} className="primary" onClick={() => save(r)}>
            {savedRole === r ? '✓ Saved' : `Save ${roleLabel(r)}`}
          </button>
        ))}
      </div>
    </div>
  )
}

function Tables() {
  const [rows, setRows] = useState([])
  const [branches, setBranches] = useState([])
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [branchId, setBranchId] = useState('')
  const [err, setErr] = useState('')
  const load = () => {
    api.listTables().then(setRows).catch((e) => setErr(e.message))
    api.listBranches().then((b) => { setBranches(b); if (!branchId && b[0]) setBranchId(b[0].id) }).catch(() => {})
  }
  useEffect(() => { load() }, [])

  const add = async () => {
    setErr('')
    try { await api.addTable(code.trim(), name.trim(), Number(branchId)); setCode(''); setName(''); load() }
    catch (e) { setErr(e.message) }
  }
  const rename = async (t) => {
    const name = await ui.prompt({ title: `Rename ${t.code}`, defaultValue: t.name, confirmText: 'Rename', required: true })
    if (name && name !== t.name) { try { await api.updateTable(t.id, t.code, name, t.branchId); load() } catch (e) { toast.error(friendlyError(e)) } }
  }
  const moveBranch = async (t, bid) => { try { await api.updateTable(t.id, t.code, t.name, Number(bid)); load() } catch (e) { toast.error(friendlyError(e)) } }
  const del = async (t) => {
    if (!await ui.confirm({ title: 'Delete table', message: `Delete ${t.code}?`, confirmText: 'Delete', danger: true })) return
    try { await api.deleteTable(t.id); load() } catch (e) { ui.alert(e.message) }
  }

  return (
    <div className="card">
      <b>Tables</b>
      <div style={{ margin: '10px 0' }}>
        {rows.map((t) => (
          <div key={t.id} className="row between list-row" style={{ padding: '7px 0', borderBottom: '1px solid var(--line)' }} onDoubleClick={() => rename(t)}>
            <span><b>{t.code}</b> <span className="muted">· {t.name}</span></span>
            <div className="row" style={{ gap: 4 }}>
              {branches.length > 1 && (
                <select value={t.branchId} onChange={(e) => moveBranch(t, e.target.value)} style={{ padding: '4px 6px', fontSize: 12 }}>
                  {branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
                </select>
              )}
              <button className="ghost icon-btn" title="Rename table" onClick={() => rename(t)}><EditIcon /></button>
              <button className="ghost icon-btn danger" title="Delete table" onClick={() => del(t)}><TrashIcon /></button>
            </div>
          </div>
        ))}
        {rows.length === 0 && <div className="muted" style={{ fontSize: 13 }}>No tables yet.</div>}
      </div>
      <div className="row" style={{ gap: 8, flexWrap: 'wrap' }}>
        <input placeholder="Code (T7)" value={code} onChange={(e) => setCode(e.target.value)} style={{ width: 90 }} />
        <input placeholder="Name" value={name} onChange={(e) => setName(e.target.value)} style={{ flex: 1, minWidth: 100 }} />
        {branches.length > 1 && (
          <select value={branchId} onChange={(e) => setBranchId(e.target.value)}>
            {branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
          </select>
        )}
        <button className="primary" disabled={!code.trim() || !name.trim()} onClick={add}><PlusIcon /> Add</button>
      </div>
      {err && <div style={{ color: 'var(--red)', fontSize: 13, marginTop: 6 }}>{err}</div>}
    </div>
  )
}

function Staff() {
  const [rows, setRows] = useState([])
  const [f, setF] = useState({ username: '', displayName: '', role: 'Cashier', password: '', fbrPosId: '', fbrToken: '' })
  const [err, setErr] = useState('')
  const [editFor, setEditFor] = useState(null)
  const [fbrOn, setFbrOn] = useState(false)
  const load = () => {
    api.listStaff().then(setRows).catch((e) => setErr(e.message))
    api.getFbrSettings().then((s) => setFbrOn(!!(s.featureFbr && s.fbrEnabled))).catch(() => {})
  }
  useEffect(() => { load() }, [])
  const set = (k, v) => setF((s) => ({ ...s, [k]: v }))

  const add = async () => {
    setErr('')
    try {
      await api.addStaff({
        username: f.username.trim(), displayName: f.displayName.trim(), role: f.role, password: f.password,
        fbrPosId: f.fbrPosId ? Number(f.fbrPosId) : null, fbrToken: f.fbrToken || null,
      })
      setF({ username: '', displayName: '', role: 'Cashier', password: '', fbrPosId: '', fbrToken: '' }); load()
    } catch (e) { setErr(e.message) }
  }
  const resetPw = async (s) => {
    const pw = await ui.prompt({ title: `New password for ${s.username}`, placeholder: 'New password (6+ characters)', confirmText: 'Update', required: true })
    if (pw) {
      try { await api.resetStaffPassword(s.id, pw); toast.success('Password updated.') }
      catch (e) { toast.error(friendlyError(e)) }
    }
  }
  const del = async (s) => {
    if (!await ui.confirm({ title: 'Remove staff', message: `Remove ${s.username}?`, confirmText: 'Remove', danger: true })) return
    try { await api.deleteStaff(s.id); load() } catch (e) { ui.alert(e.message) }
  }

  return (
    <div className="card">
      <b>Staff</b>
      <div style={{ margin: '8px 0' }}>
        {rows.map((s) => (
          <div key={s.id} style={{ borderBottom: '1px solid var(--line)' }}>
            {editFor === s.id ? (
              <StaffEdit s={s} fbrOn={fbrOn} onDone={() => { setEditFor(null); load() }} onCancel={() => setEditFor(null)} />
            ) : (
              <div className="row between list-row" style={{ padding: '7px 0' }} onDoubleClick={() => setEditFor(s.id)}>
                <span><b>{s.displayName}</b> <span className="muted">· {s.username} · {roleLabel(s.role)}
                  {fbrOn && s.fbrPosId ? ` · POS ${s.fbrPosId}` : ''}</span></span>
                <div className="row" style={{ gap: 4 }}>
                  <button className="ghost icon-btn" title="Edit member" onClick={() => setEditFor(s.id)}><EditIcon /></button>
                  <button className="ghost icon-btn" title="Reset password" onClick={() => resetPw(s)}><KeyIcon /></button>
                  <button className="ghost icon-btn danger" title="Remove member" onClick={() => del(s)}><TrashIcon /></button>
                </div>
              </div>
            )}
          </div>
        ))}
        {rows.length === 0 && <div className="muted" style={{ fontSize: 13 }}>No staff yet.</div>}
      </div>
      <div className="cat-title" style={{ fontSize: 13, margin: '6px 0' }}>Add staff</div>
      <div className="row" style={{ gap: 8, marginBottom: 6 }}>
        <input placeholder="Their name" value={f.displayName} onChange={(e) => set('displayName', e.target.value)} style={{ flex: 1 }} />
        <input placeholder="Mobile number or username" value={f.username} onChange={(e) => set('username', e.target.value)} style={{ flex: 1 }} autoCapitalize="none" spellCheck="false" />
      </div>
      <div className="row" style={{ gap: 8, marginBottom: 6 }}>
        <select value={f.role} onChange={(e) => set('role', e.target.value)}>{ROLES.map((r) => <option key={r} value={r}>{roleLabel(r)}</option>)}</select>
        <PasswordInput placeholder="Password (6+ characters)" value={f.password} onChange={(e) => set('password', e.target.value)} style={{ flex: 1 }} />
      </div>
      {fbrOn && (
        <div className="row" style={{ gap: 8, marginBottom: 6 }}>
          <input placeholder="FBR POSID (optional)" value={f.fbrPosId} onChange={(e) => set('fbrPosId', e.target.value)} style={{ width: 150 }} />
          <input placeholder="FBR token (cloud, optional)" value={f.fbrToken} onChange={(e) => set('fbrToken', e.target.value)} style={{ flex: 1 }} />
        </div>
      )}
      <div className="row"><button className="primary" style={{ marginLeft: 'auto' }} disabled={!f.username.trim() || !f.displayName.trim() || !f.password} onClick={add}><PlusIcon /> Add member</button></div>
      {err && <div style={{ color: 'var(--red)', fontSize: 13, marginTop: 6 }}>{err}</div>}
    </div>
  )
}

// Full inline editor for a staff member: username, display name, role and (when FBR
// is on) their POS registration. Opened by the Edit button or a double-click.
function StaffEdit({ s, fbrOn, onDone, onCancel }) {
  const [f, setF] = useState({ username: s.username, displayName: s.displayName, role: s.role, fbrPosId: s.fbrPosId || '', fbrToken: '' })
  const [err, setErr] = useState('')
  const [busy, setBusy] = useState(false)
  const set = (k, v) => setF((x) => ({ ...x, [k]: v }))
  const save = async () => {
    setErr(''); setBusy(true)
    try {
      await api.updateStaff(s.id, {
        username: f.username.trim(), displayName: f.displayName.trim(), role: f.role,
        fbrPosId: f.fbrPosId ? Number(f.fbrPosId) : null,
        ...(f.fbrToken !== '' ? { fbrToken: f.fbrToken } : {}),   // blank = keep token
      })
      onDone()
    } catch (e) { setErr(friendlyError(e)); setBusy(false) }
  }
  return (
    <div className="edit-panel">
      <div className="row" style={{ gap: 8, marginBottom: 8, flexWrap: 'wrap' }}>
        <input autoFocus placeholder="Their name" value={f.displayName} onChange={(e) => set('displayName', e.target.value)} style={{ flex: 1, minWidth: 130 }} />
        <input placeholder="Mobile number or username" value={f.username} onChange={(e) => set('username', e.target.value)} style={{ flex: 1, minWidth: 130 }} autoCapitalize="none" spellCheck="false" />
        <select value={f.role} onChange={(e) => set('role', e.target.value)}>{ROLES.map((r) => <option key={r} value={r}>{roleLabel(r)}</option>)}</select>
      </div>
      {fbrOn && (
        <div className="row" style={{ gap: 8, marginBottom: 8, flexWrap: 'wrap' }}>
          <input placeholder="FBR POSID" value={f.fbrPosId} onChange={(e) => set('fbrPosId', e.target.value)} style={{ width: 130 }} />
          <input placeholder={s.hasFbrToken ? 'Token set. Type to change' : 'FBR token (cloud)'} value={f.fbrToken} onChange={(e) => set('fbrToken', e.target.value)} style={{ flex: 1, minWidth: 150 }} />
        </div>
      )}
      <div className="row" style={{ gap: 8 }}>
        <button className="primary" disabled={busy || !f.username.trim() || !f.displayName.trim()} onClick={save}>Save changes</button>
        <button disabled={busy} onClick={onCancel}>Cancel</button>
      </div>
      {err && <div style={{ color: 'var(--red)', fontSize: 13, marginTop: 6 }}>{err}</div>}
    </div>
  )
}
