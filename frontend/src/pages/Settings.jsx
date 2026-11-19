import { Link } from 'react-router-dom'
import { useEffect, useRef, useState } from 'react'
import { api, roleLabel } from '../api'
import { auth } from '../auth'
import { ui, toast } from '../ui.jsx'
import { friendlyError } from '../errors'
import { PasswordInput } from '../forms.jsx'
import { EditIcon, TrashIcon, KeyIcon, PlusIcon, ImageIcon, LocateIcon } from '../icons.jsx'
import MapView, { myLocation } from '../MapView.jsx'
import { usernameProblem } from '../phone.js'

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

// 0 to 100, as the server allows. Blank is fine unless the box must be filled.
const isPercent = (v, required) => {
  if (v === '' || v == null) return !required
  const n = Number(v)
  return Number.isFinite(n) && n >= 0 && n <= 100
}
// A whole number above 0, or blank (for optional ids like an FBR POS ID).
const isPosIdOrBlank = (v) => v === '' || v == null || (/^\d+$/.test(String(v).trim()) && Number(v) > 0)

// Owner/manager settings, grouped into tabs so related settings sit together
// instead of one long, confusing list.
const SETTINGS_TABS = [
  { id: 'billing', label: 'Billing & tax' },
  { id: 'receipt', label: 'Receipt & branding' },
  { id: 'venue', label: 'Branches & tables' },
  { id: 'staff', label: 'Staff & roles' },
  { id: 'service', label: 'Service' },
  { id: 'fbr', label: 'Tax office (FBR / PRA)' },
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
        {tab === 'service' && <><ServiceSettings /><PickupSettings /></>}
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
      <div className="row" style={{ gap: 8, flexWrap: 'wrap', alignItems: 'flex-end' }}>
        <label className="fld" style={{ flex: 1, minWidth: 160 }}><span>Current password</span>
          <PasswordInput required autoComplete="current-password" maxLength={200} value={cur} onChange={(e) => setCur(e.target.value)} /></label>
        <label className="fld" style={{ flex: 1, minWidth: 160 }}><span>New password</span>
          <PasswordInput required autoComplete="new-password" maxLength={200} placeholder="6 or more characters" value={nw} onChange={(e) => setNw(e.target.value)} /></label>
        <button className="primary" disabled={busy || !cur || nw.length < 6} onClick={save}>{busy ? 'Saving…' : msg || 'Update password'}</button>
      </div>
      {!busy && !msg && (!cur || nw.length < 6) && <div className="muted" style={{ fontSize: 12, marginTop: 6 }}>{!cur ? 'Enter your current password first.' : 'The new password needs 6 or more characters.'}</div>}
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
  const isOwner = auth.role() === 'Owner'
  const hasCardRate = s.taxPercentCard !== '' && s.taxPercentCard != null

  // The same limits the server holds. A wrong number is stopped here with a reason, never changed quietly.
  const problem = () => {
    if (s.taxEnabled) {
      if ((s.taxName || '').trim().length > 40) return 'Tax name must be 40 characters or fewer.'
      if (!isPercent(s.taxPercent, true)) return 'Tax % must be between 0 and 100.'
      if (!isPercent(s.taxPercentCard, false)) return 'Card tax % must be between 0 and 100.'
    }
    if (!isPercent(s.serviceChargePercent, false)) return 'Service charge % must be between 0 and 100.'
    if (s.discountEnabled) {
      const d = Number(s.discountValue || 0)
      if (!Number.isFinite(d) || d < 0) return "Discount can't be negative."
      if (s.discountIsPercent && d > 100) return 'Discount % must be between 0 and 100.'
    }
    return ''
  }
  const save = async () => {
    const why = problem()
    if (why) { setErr(why); return }
    setErr(''); setBusy(true)
    try {
      await api.setBillingSettings({
        taxEnabled: !!s.taxEnabled, taxName: (s.taxName || 'GST').trim(),
        taxPercent: Number(s.taxPercent) || 0, taxPercentCard: s.taxPercentCard === '' || s.taxPercentCard == null ? null : Number(s.taxPercentCard),
        serviceChargePercent: Number(s.serviceChargePercent) || 0,
        discountEnabled: !!s.discountEnabled, discountIsPercent: !!s.discountIsPercent,
        discountValue: Number(s.discountValue) || 0,
        receiptEnabled: !!s.receiptEnabled, stockTrackingEnabled: !!s.stockTrackingEnabled,
        serviceChargeTaxable: !!s.serviceChargeTaxable, serviceAfterDiscount: !!s.serviceAfterDiscount,
        roundTotals: !!s.roundTotals, cardRateForAllItems: !!s.cardRateForAllItems,
        oneTaxForAllItems: !!s.oneTaxForAllItems,
        // Only the owner can change the cancelling rules; anyone else sends them back as they are.
        ...(isOwner ? {
          allowCancelAfterCooking: !!s.allowCancelAfterCooking, restockCookedCancel: !!s.restockCookedCancel,
          cashierCancelAnyStage: !!s.cashierCancelAnyStage,
        } : {}),
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
              <label className="fld"><span>Tax name <span className="opt">(optional)</span></span>
                <input value={s.taxName || ''} placeholder="GST" maxLength={40} onChange={(e) => set('taxName', e.target.value)} /></label>
              <label className="fld"><span>Rate %</span>
                <input type="number" required min="0" max="100" step="0.01" inputMode="decimal" value={s.taxPercent} onChange={(e) => set('taxPercent', e.target.value)} /></label>
              <label className="fld"><span>Card rate % <span className="opt">(optional)</span></span>
                <input type="number" min="0" max="100" step="0.01" inputMode="decimal" placeholder="Same" value={s.taxPercentCard ?? ''} onChange={(e) => set('taxPercentCard', e.target.value)} /></label>
            </div>
          )}
          {s.taxEnabled && (
            <div style={{ marginTop: 10 }}>
              <div className="set-head" style={{ marginBottom: 4 }}>Tax on items</div>
              <div className="seg" role="radiogroup" aria-label="Tax on items">
                <button type="button" className={!s.oneTaxForAllItems ? 'on' : ''} aria-pressed={!s.oneTaxForAllItems} onClick={() => set('oneTaxForAllItems', false)}>Each item's own rate</button>
                <button type="button" className={s.oneTaxForAllItems ? 'on' : ''} aria-pressed={!!s.oneTaxForAllItems} onClick={() => set('oneTaxForAllItems', true)}>One rate for all items</button>
              </div>
              <div className="muted" style={{ fontSize: 12, marginTop: 4 }}>
                {s.oneTaxForAllItems
                  ? `Every item is charged ${Number(s.taxPercent) || 0}%${hasCardRate ? ` (${Number(s.taxPercentCard) || 0}% by card)` : ''}, whatever rate it has in the menu. Tax-free items are taxed too.`
                  : `An item with its own rate in the menu (Menu → item → Tax %) uses it; 0 means tax-free. The rest use ${Number(s.taxPercent) || 0}%.`}
              </div>
            </div>
          )}
        </div>

        {/* Service charge: its own setting, so it can be on while tax is off */}
        <div className="set-block">
          <label className="fld"><span>Service charge % (dine-in)</span>
            <input type="number" min="0" max="100" step="0.01" inputMode="decimal" placeholder="0" value={s.serviceChargePercent ?? ''} onChange={(e) => set('serviceChargePercent', e.target.value)} style={{ maxWidth: 160 }} /></label>
          <div className="muted" style={{ fontSize: 12, marginTop: 4 }}>Added to dine-in bills only. 0 means no service charge.</div>
        </div>

        {/* How the bill is worked out. All off is how bills always worked. */}
        <div className="set-block">
          <div className="set-head">Bill options</div>
          <Toggle label="Charge tax on the service charge" checked={s.serviceChargeTaxable} disabled={!s.taxEnabled}
            onChange={(v) => set('serviceChargeTaxable', v)}
            hint={!s.taxEnabled ? 'Turn on Tax / GST first.' : s.serviceChargeTaxable
              ? `The service charge is taxed at the bill's ${s.taxName || 'GST'} rate (the card rate when paid by card).`
              : 'The service charge is added without tax.'} />
          <Toggle label="Work out the service charge after the discount" checked={s.serviceAfterDiscount}
            onChange={(v) => set('serviceAfterDiscount', v)}
            hint={s.serviceAfterDiscount ? 'Service is charged on the food price after the discount.' : 'Service is charged on the full food price, before any discount.'} />
          <Toggle label="Round each bill to the nearest rupee" checked={s.roundTotals}
            onChange={(v) => set('roundTotals', v)}
            hint={s.roundTotals ? 'The bill shows a "Rounding" line (at most 50 paisa). FBR still gets the exact amounts.' : 'Bills keep their paisa.'} />
          <Toggle label="Card rate replaces an item's own tax rate too" checked={s.cardRateForAllItems || s.oneTaxForAllItems} disabled={!s.taxEnabled || !hasCardRate || s.oneTaxForAllItems}
            onChange={(v) => set('cardRateForAllItems', v)}
            hint={!s.taxEnabled || !hasCardRate ? 'Set a card rate first.' : s.oneTaxForAllItems ? 'On: with one rate for all items, card bills use the card rate on everything.' : s.cardRateForAllItems
              ? 'Paid by card or wallet: every taxed item uses the card rate. Tax-free items stay tax-free.'
              : 'Paid by card or wallet: items with their own rate keep it; the rest use the card rate.'} />
        </div>

        {/* Cancelling. Only the owner can change these: they say what everyone else may do. */}
        <div className="set-block">
          <div className="set-head">Cancelling orders{!isOwner && <span className="muted" style={{ fontWeight: 500 }}> · only the owner can change these</span>}</div>
          <Toggle label="Allow cancelling once cooking has started" checked={s.allowCancelAfterCooking} disabled={!isOwner}
            onChange={(v) => set('allowCancelAfterCooking', v)}
            hint={s.allowCancelAfterCooking
              ? 'Staff with the cancel right can cancel food that is cooking or ready. Guests can cancel until it is ready.'
              : 'Until the kitchen starts cooking, cancelling works as normal. Once cooking starts, only the owner can cancel the order or take a dish off it.'} />
          <Toggle label="Cashier can cancel at any stage" checked={s.cashierCancelAnyStage} disabled={!isOwner}
            onChange={(v) => set('cashierCancelAnyStage', v)}
            hint={s.cashierCancelAnyStage
              ? 'The counter (cashier, manager and owner) can cancel any unpaid order or take off any dish: placed, cooking, ready or served. Cashiers don’t need the “Cancel orders” right for this.'
              : 'Cashiers cancel only if their role has the “Cancel orders” right, and the rule above applies to them.'} />
          {s.stockTrackingEnabled && (
            <Toggle label="Put ingredients back in stock when cooking food is cancelled" checked={s.restockCookedCancel} disabled={!isOwner}
              onChange={(v) => set('restockCookedCancel', v)}
              hint={s.restockCookedCancel
                ? 'Cancelled food goes back into stock, cooked or not.'
                : 'Food cancelled after cooking started counts as used up. Food the kitchen had not started always goes back.'} />
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
                <input type="number" required min="0" max={s.discountIsPercent ? 100 : undefined} step="0.01" inputMode="decimal" value={s.discountValue} onChange={(e) => set('discountValue', e.target.value)} /></label>
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

// A small on/off switch (accessible label + checkbox), with an optional line saying what it does now.
function Toggle({ label, checked, onChange, disabled = false, hint }) {
  const sw = (
    <label className={'switch-row' + (disabled ? ' is-off' : '')} aria-disabled={disabled || undefined}>
      <span className={'switch' + (checked ? ' on' : '')} onClick={(e) => { e.preventDefault(); if (!disabled) onChange(!checked) }}>
        <span className="knob" />
      </span>
      <span>{label}</span>
      <input type="checkbox" checked={!!checked} disabled={disabled} onChange={(e) => onChange(e.target.checked)} style={{ display: 'none' }} />
    </label>
  )
  if (hint == null) return sw
  return <div className="switch-item">{sw}<div className="switch-hint">{hint}</div></div>
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
            <span className="muted" style={{ fontSize: 13 }}>Header: address, phone, tagline <span className="opt">(optional)</span></span>
            <textarea rows={2} maxLength={500} placeholder={`${s.restaurantName || 'Your restaurant'}\nMain Boulevard, Lahore · 042-000000`}
              value={s.receiptHeader || ''} onChange={(e) => set('receiptHeader', e.target.value)} />
          </label>
          <label className="fld">
            <span className="muted" style={{ fontSize: 13 }}>Footer: thank-you or policy note <span className="opt">(optional)</span></span>
            <textarea rows={2} maxLength={500} placeholder="Thank you, please come again!"
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
    const before = s
    setS({ ...s, waiterAutoAssign: val })
    try { await api.setServiceSettings({ waiterAutoAssign: val }); setMsg('Saved'); setTimeout(() => setMsg(''), 1200) }
    catch (e) { setS(before); toast.error(friendlyError(e)) }   // put the choice back: it wasn't saved
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

// Takeaway and delivery from the guest's own phone: one "order QR" for the counter, a flyer,
// Instagram or WhatsApp. Orders land on reception as Parcel / Delivery tickets and need approving.
function PickupSettings() {
  const [s, setS] = useState(null)
  const [fee, setFee] = useState('')
  const [note, setNote] = useState('')
  const [msg, setMsg] = useState('')
  const [busy, setBusy] = useState(false)
  const [feeErr, setFeeErr] = useState('')
  useEffect(() => {
    api.getServiceSettings().then((x) => { setS(x); setFee(String(x.deliveryFee ?? 0)); setNote(x.deliveryNote || '') }).catch(() => {})
  }, [])
  if (!s) return null
  const save = async (changes) => {
    const before = s
    setS((x) => ({ ...x, ...changes }))
    setBusy(true)
    try { await api.setServiceSettings(changes); setMsg('Saved'); setTimeout(() => setMsg(''), 1200) }
    catch (e) { setS(before); toast.error(friendlyError(e)) }   // the boxes go back to what is really saved
    setBusy(false)
  }
  const saveDelivery = () => {
    const n = Number(fee)
    if (fee.trim() === '' || !Number.isFinite(n) || n < 0 || n > 100000) { setFeeErr('Enter the delivery charge in rupees, 0 to 100,000 (0 means free delivery).'); return }
    setFeeErr('')
    save({ deliveryFee: n, deliveryNote: note.trim() })
  }
  return (
    <div className="card" style={{ marginBottom: 16 }}>
      <div className="row between"><b>Takeaway &amp; delivery QR</b>
        {!s.pickupInPlan ? <span className="pill s-Placed">Not in your plan</span> : msg && <span className="muted" style={{ fontSize: 13 }}>{msg}</span>}</div>
      <p className="muted" style={{ fontSize: 13, margin: '4px 0 10px' }}>
        One QR for your counter, flyers, Instagram or WhatsApp. Customers order on their own phone, choose takeaway or delivery,
        and give their name and number. The order arrives on Reception as a Parcel or Delivery ticket, for you to approve.
        Print it from <Link to="/owner">Table QR codes</Link>.
      </p>
      <label className="row" style={{ gap: 8, marginBottom: 8, cursor: 'pointer' }}>
        <input type="checkbox" disabled={!s.pickupInPlan || busy} checked={s.guestTakeaway} onChange={(e) => save({ guestTakeaway: e.target.checked })} />
        <span><b>Takeaway:</b> customers order ahead and collect it ready.</span>
      </label>
      <label className="row" style={{ gap: 8, cursor: 'pointer' }}>
        <input type="checkbox" disabled={!s.pickupInPlan || busy} checked={s.guestDelivery} onChange={(e) => save({ guestDelivery: e.target.checked })} />
        <span><b>Delivery:</b> customers give their address; you send a rider.</span>
      </label>
      {s.guestDelivery && (
        <div className="row" style={{ gap: 10, marginTop: 12, flexWrap: 'wrap', alignItems: 'flex-end' }}>
          <label className="fld" style={{ flex: '0 0 150px' }}><span style={{ fontSize: 12.5 }}>Delivery charge (Rs)</span>
            <input type="number" required min="0" max="100000" step="10" inputMode="numeric" value={fee}
              className={feeErr ? 'invalid' : undefined} aria-invalid={feeErr ? true : undefined}
              onChange={(e) => { setFee(e.target.value); setFeeErr('') }} /></label>
          <label className="fld" style={{ flex: '1 1 240px' }}><span style={{ fontSize: 12.5 }}>Note for customers <span className="opt">(optional)</span></span>
            <input value={note} maxLength={120} onChange={(e) => setNote(e.target.value)} placeholder="Within 5 km · about 45 minutes" /></label>
          <button className="primary" disabled={busy} onClick={saveDelivery}>{busy ? 'Saving…' : 'Save'}</button>
        </div>
      )}
      {feeErr && <div className="field-error" role="alert">{feeErr}</div>}
      {s.pickupInPlan && <ShopLocation s={s} save={save} />}
    </div>
  )
}

// Where the restaurant is: riders start here, customers see how far they are, and pins outside the
// delivery area are turned away before the order is placed.
function ShopLocation({ s, save }) {
  const [at, setAt] = useState(s.latitude != null ? { lat: s.latitude, lng: s.longitude } : null)
  const [km, setKm] = useState(String(s.deliveryRadiusKm || 0))
  const [busy, setBusy] = useState(false)
  const dirty = at && (at.lat !== s.latitude || at.lng !== s.longitude)
  const locate = async () => {
    setBusy(true)
    try { setAt(await myLocation()) } catch (e) { ui.alert(e.message) }
    setBusy(false)
  }
  const saveAll = async () => {
    const r = Number(km)
    if (km.trim() === '' || !Number.isFinite(r) || r < 0 || r > 100) return ui.alert('The delivery area is in km, from 0 (no limit) to 100.')
    setBusy(true)
    await save({ ...(at ? { latitude: +at.lat.toFixed(6), longitude: +at.lng.toFixed(6) } : {}), deliveryRadiusKm: r })
    setBusy(false)
  }
  return (
    <div style={{ marginTop: 16, paddingTop: 14, borderTop: '1px solid var(--line)' }}>
      <div className="row between" style={{ gap: 8, flexWrap: 'wrap' }}>
        <b>Your restaurant on the map</b>
        <button className="btn-sm" disabled={busy} onClick={locate}><LocateIcon size={14} /> Use this device's location</button>
      </div>
      <p className="muted" style={{ fontSize: 13, margin: '4px 0 10px' }}>
        Riders start here and customers see how far away they are. Tap the map or drag the pin onto your door.
      </p>
      <MapView height={240} center={[30.3753, 69.3451]} area={at && Number(km) > 0 ? { ...at, km: Number(km) } : null}
        pick={{ ...(at || { lat: NaN, lng: NaN }), kind: 'shop', label: 'Your restaurant', onMove: (p) => setAt({ lat: p.lat, lng: p.lng }) }} />
      <div className="row" style={{ gap: 10, marginTop: 10, flexWrap: 'wrap', alignItems: 'flex-end' }}>
        <label className="fld" style={{ flex: '0 0 190px' }}><span style={{ fontSize: 12.5 }}>Deliver within (km, 0 = no limit)</span>
          <input type="number" required min="0" max="100" step="0.5" inputMode="decimal" value={km} onChange={(e) => setKm(e.target.value)} /></label>
        <button className="primary" disabled={busy || (!dirty && Number(km) === Number(s.deliveryRadiusKm || 0))} onClick={saveAll}>{busy ? 'Saving…' : 'Save location'}</button>
        {at && <span className="muted" style={{ fontSize: 12.5 }}>{at.lat.toFixed(5)}, {at.lng.toFixed(5)}</span>}
      </div>
    </div>
  )
}

// Restaurant-level tax office settings: filing on/off, the seller's identity, and when bills are sent.
// Filing only works when the platform operator has it in this restaurant's plan (featureFbr).
// Each branch then chooses FBR or PRA and Practice / Test / Live (TaxBranches below).
function FbrSettings() {
  const [s, setS] = useState(null)
  const [msg, setMsg] = useState('')
  const [err, setErr] = useState('')
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(0)   // bumps the branch list after a save (filing switched on/off)
  const load = () => api.getFbrSettings().then(setS).catch((e) => setErr(e.message))
  useEffect(() => { load() }, [])
  if (!s) return null

  const set = (k, v) => setS((x) => ({ ...x, [k]: v }))
  const save = async () => {
    if (!/^[0-9 -]*$/.test(s.ntn || '')) return setErr('NTN can only have numbers and dashes.')
    if (!/^[0-9 -]*$/.test(s.strn || '')) return setErr('STRN can only have numbers and dashes.')
    setErr(''); setMsg(''); setBusy(true)
    try {
      await api.setFbrSettings({
        fbrEnabled: s.fbrEnabled, businessName: (s.businessName || '').trim(), ntn: (s.ntn || '').trim(), strn: (s.strn || '').trim(),
        sendAtSettle: !!s.sendAtSettle, autoRetry: !!s.autoRetry,
      })
      setMsg('Saved.'); setTimeout(() => setMsg(''), 1500); setSaved((n) => n + 1)
    } catch (e) { setErr(friendlyError(e)) }
    setBusy(false)
  }

  return (
    <>
    <div className="card" style={{ marginBottom: 16 }}>
      <div className="row between">
        <b>Tax office filing (FBR / PRA)</b>
        {!s.featureFbr && <span className="pill s-Placed">Not in your plan</span>}
      </div>
      <p className="muted" style={{ fontSize: 13, margin: '4px 0 10px' }}>
        {s.featureFbr
          ? 'Send each settled bill to FBR or PRA. Turn it on, add your NTN, then set up each branch below: which office, its POSID and token, and Practice, Test or Live.'
          : 'Filing with FBR or PRA isn’t in your plan. Ask Dine In to turn it on for your restaurant.'}
      </p>
      {s.forcedPractice && s.featureFbr && (
        <div className="card" style={{ background: 'var(--surface2)', fontSize: 13, marginBottom: 10 }}>
          <b>This system is in practice mode.</b> No bill is sent to FBR or PRA, whatever a branch says: bills get a
          PRACTICE number and the receipt says “not a tax invoice”. Ask Dine In to switch real filing on when you are ready.
        </div>
      )}
      <Toggle label="File bills with the tax office" checked={s.fbrEnabled} disabled={!s.featureFbr}
        onChange={(v) => set('fbrEnabled', v)}
        hint={s.fbrEnabled ? 'Every settled bill is filed with the office its branch is set to.' : 'Bills are not filed. Bills settled while this is off are never sent later.'} />
      <Toggle label="Send each bill as soon as it is settled" checked={s.sendAtSettle} disabled={!s.featureFbr || !s.fbrEnabled}
        onChange={(v) => set('sendAtSettle', v)}
        hint={s.sendAtSettle
          ? 'The bill is sent at settle and the receipt prints with its tax office number. If the office can’t be reached, the bill still prints and waits.'
          : 'Bills wait in Tax office invoices → Waiting until someone presses Send there (one, the picked ones, or all).'} />
      <Toggle label="Keep trying waiting bills automatically" checked={s.autoRetry} disabled={!s.featureFbr || !s.fbrEnabled}
        onChange={(v) => set('autoRetry', v)}
        hint={s.autoRetry
          ? 'Every few minutes, bills that couldn’t reach the office are sent again. Refused bills are tried 3 more times, in case you fixed the setup.'
          : 'Waiting bills are only sent when someone presses Send.'} />
      <div className="row" style={{ gap: 8, flexWrap: 'wrap', alignItems: 'flex-end', marginTop: 8 }}>
        <label className="fld" style={{ flex: 2, minWidth: 180 }}><span>Registered business name <span className="opt">(optional)</span></span>
          <input maxLength={150} value={s.businessName || ''} onChange={(e) => set('businessName', e.target.value)} /></label>
        <label className="fld" style={{ flex: 1, minWidth: 110 }}><span>NTN <span className="opt">(optional)</span></span>
          <input maxLength={15} inputMode="numeric" placeholder="1234567-8" value={s.ntn || ''} onChange={(e) => set('ntn', e.target.value)} /></label>
        <label className="fld" style={{ flex: 1, minWidth: 110 }}><span>STRN <span className="opt">(optional)</span></span>
          <input maxLength={20} inputMode="numeric" value={s.strn || ''} onChange={(e) => set('strn', e.target.value)} /></label>
        <button className="primary" disabled={!s.featureFbr || busy} onClick={save}>{busy ? 'Saving…' : msg || 'Save'}</button>
      </div>
      {!s.featureFbr && <div className="muted" style={{ fontSize: 12, marginTop: 6 }}>Save is off because filing isn't in your plan.</div>}
      {err && <div style={{ color: 'var(--red)', fontSize: 13, marginTop: 6 }}>{err}</div>}
    </div>
    {s.featureFbr && <TaxBranches key={saved} localAvailable={!!s.localAvailable} forcedPractice={!!s.forcedPractice} />}
    </>
  )
}

const ENV_TEXT = {
  Practice: 'Nothing is sent. Bills get a made-up PRACTICE number, to learn the screens.',
  Test: 'Bills go to the office’s test (sandbox) server with your test POSID and token. Not real tax invoices.',
  Live: 'Every bill is filed as a real tax invoice.',
}
// Practice amber, Test blue, Live red (real tax invoices).
const envPill = (env) => (env === 'Live' ? 's-Cooking' : env === 'Test' ? 's-Approved' : 's-Placed')

// Each branch's tax office setup, and the check that says what is missing, in plain words.
function TaxBranches({ localAvailable, forcedPractice }) {
  const [rows, setRows] = useState([])
  const [checks, setChecks] = useState({})
  const [openFor, setOpenFor] = useState(null)
  const [err, setErr] = useState('')
  const [checking, setChecking] = useState(false)
  const load = () => {
    api.listBranches().then(setRows).catch((e) => setErr(friendlyError(e)))
    runCheck()
  }
  const runCheck = async () => {
    setChecking(true)
    try { const list = await api.fbrCheck(); setChecks(Object.fromEntries(list.map((c) => [c.branchId, c]))) }
    catch (e) { setErr(friendlyError(e)) }
    setChecking(false)
  }
  useEffect(() => { load() }, [])

  return (
    <div className="card" style={{ marginBottom: 16 }}>
      <div className="row between">
        <b>Branches</b>
        <button className="ghost btn-sm" disabled={checking} onClick={runCheck}>{checking ? 'Checking…' : 'Check setup'}</button>
      </div>
      <p className="muted" style={{ fontSize: 13, margin: '4px 0 10px' }}>
        Start in Practice, move to Test with the test POSID and token from FBR or PRA, send a test bill, then switch to Live.
      </p>
      {rows.map((b) => {
        const c = checks[b.id]
        const env = c?.environment || b.fiscalEnvironment
        return (
          <div key={b.id} style={{ borderBottom: '1px solid var(--line)', padding: '8px 0' }}>
            <div className="row between" style={{ gap: 8, flexWrap: 'wrap' }}>
              <span>
                <b>{b.name}</b>{' '}
                <span className="pill">{b.fiscalAuthority}</span>{' '}
                <span className={'pill ' + envPill(env)}>{env}{forcedPractice && b.fiscalEnvironment !== 'Practice' ? ` (set to ${b.fiscalEnvironment})` : ''}</span>{' '}
                {c && <span className={'pill ' + (c.ready ? 's-Ready' : 's-Cancelled')}>{c.ready ? 'Ready' : 'Needs setup'}</span>}
                <span className="muted" style={{ fontSize: 13 }}>
                  {b.fbrPosId ? ` · POSID ${b.fbrPosId}` : ''}
                  {b.fiscalAuthority === 'FBR' ? ` · ${b.fbrMode === 'Local' ? 'on this computer' : 'online'}` : ''}
                </span>
              </span>
              <button className={openFor === b.id ? 'primary btn-sm' : 'ghost btn-sm'} onClick={() => setOpenFor(openFor === b.id ? null : b.id)}>
                {openFor === b.id ? 'Close' : 'Set up'}
              </button>
            </div>
            {c && (c.problems.length > 0 || c.warnings.length > 0) && (
              <ul style={{ margin: '6px 0 0', paddingLeft: 18, fontSize: 13 }}>
                {c.problems.map((p, k) => <li key={'p' + k} style={{ color: 'var(--red)' }}>{p}</li>)}
                {c.warnings.map((w, k) => <li key={'w' + k} className="muted">{w}</li>)}
              </ul>
            )}
            {openFor === b.id && <BranchFbr b={b} localAvailable={localAvailable} onSaved={() => load()} />}
          </div>
        )
      })}
      {rows.length === 0 && <div className="muted" style={{ fontSize: 13 }}>No branches yet. Add one in Branches &amp; tables.</div>}
      {err && <div style={{ color: 'var(--red)', fontSize: 13, marginTop: 6 }}>{err}</div>}
    </div>
  )
}

function Branches() {
  const [rows, setRows] = useState([])
  const [f, setF] = useState({ name: '', address: '' })
  const [err, setErr] = useState('')
  const [fbrOn, setFbrOn] = useState(false)   // is tax office filing on for this restaurant?
  const [fbrLocal, setFbrLocal] = useState(false)
  const [fbrFor, setFbrFor] = useState(null)
  const [editFor, setEditFor] = useState(null)
  const load = () => {
    api.listBranches().then(setRows).catch((e) => setErr(e.message))
    api.getFbrSettings().then((s) => { setFbrOn(!!(s.featureFbr && s.fbrEnabled)); setFbrLocal(!!s.localAvailable) }).catch(() => {})
  }
  useEffect(() => { load() }, [])

  const [busy, setBusy] = useState(false)
  const add = async () => {
    if (busy) return
    setErr(''); setBusy(true)
    try { await api.addBranch({ name: f.name.trim(), address: f.address.trim(), isActive: true }); setF({ name: '', address: '' }); load() }
    catch (e) { setErr(friendlyError(e)) }
    setBusy(false)
  }
  const del = async (b) => { if (await ui.confirm({ title: 'Delete branch', message: `Delete branch "${b.name}"?`, confirmText: 'Delete', danger: true })) { try { await api.deleteBranch(b.id); load() } catch (e) { ui.alert(e.message) } } }

  return (
    <div className="card" style={{ marginBottom: 16 }}>
      <b>Branches</b>
      <p className="muted" style={{ fontSize: 13, margin: '4px 0 10px' }}>
        Each location has its own tables, QR codes and (when tax filing is on) its own FBR or PRA registration. Sales report per branch.
      </p>
      {rows.map((b) => (
        <div key={b.id} style={{ borderBottom: '1px solid var(--line)' }}>
          {editFor === b.id ? (
            <BranchEdit b={b} onDone={() => { setEditFor(null); load() }} onCancel={() => setEditFor(null)} />
          ) : (
            <div className="row between list-row" style={{ padding: '7px 0' }} onDoubleClick={() => setEditFor(b.id)}>
              <span><b>{b.name}</b> <span className="muted">· {b.address || 'no address'} · {b.tables} {b.tables === 1 ? 'table' : 'tables'}
                {fbrOn ? ` · ${b.fiscalAuthority} ${b.fiscalEnvironment}${b.fbrPosId ? ` POSID ${b.fbrPosId}` : ''}` : ''}</span></span>
              <div className="row" style={{ gap: 4 }}>
                {fbrOn && <button className="ghost btn-sm" onClick={() => setFbrFor(fbrFor === b.id ? null : b.id)}>Tax office</button>}
                <button className="ghost icon-btn" title="Edit branch" onClick={() => setEditFor(b.id)}><EditIcon /></button>
                <button className="ghost icon-btn danger" title="Delete branch" onClick={() => del(b)}><TrashIcon /></button>
              </div>
            </div>
          )}
          {fbrOn && fbrFor === b.id && editFor !== b.id && <BranchFbr b={b} localAvailable={fbrLocal} onSaved={() => { setFbrFor(null); load() }} />}
        </div>
      ))}
      <div className="cat-title" style={{ fontSize: 13, margin: '10px 0 6px' }}>Add branch</div>
      <div className="row" style={{ gap: 8, flexWrap: 'wrap', alignItems: 'flex-end' }}>
        <label className="fld" style={{ flex: 1, minWidth: 140 }}><span>Branch name</span>
          <input required maxLength={80} placeholder="e.g. Gulberg" value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} /></label>
        <label className="fld" style={{ flex: 1, minWidth: 140 }}><span>Address <span className="opt">(optional)</span></span>
          <input maxLength={200} value={f.address} onChange={(e) => setF({ ...f, address: e.target.value })} /></label>
        <button className="primary" disabled={busy || !f.name.trim()} onClick={add}><PlusIcon /> {busy ? 'Adding…' : 'Add'}</button>
      </div>
      {err && <div style={{ color: 'var(--red)', fontSize: 13, marginTop: 6 }}>{err}</div>}
    </div>
  )
}

// Inline editor for a branch's name + address (opened by the Edit button or a
// double-click). The tax office setup stays on its own panel and is left as it is.
function BranchEdit({ b, onDone, onCancel }) {
  const [name, setName] = useState(b.name)
  const [address, setAddress] = useState(b.address || '')
  const [err, setErr] = useState('')
  const [busy, setBusy] = useState(false)
  const save = async () => {
    setErr(''); setBusy(true)
    try { await api.updateBranch(b.id, { name: name.trim(), address: address.trim(), isActive: b.isActive, keepFiscal: true }); onDone() }
    catch (e) { setErr(friendlyError(e)); setBusy(false) }
  }
  return (
    <div className="edit-panel">
      <div className="row" style={{ gap: 8, flexWrap: 'wrap', alignItems: 'flex-end' }}>
        <label className="fld" style={{ flex: 1, minWidth: 140 }}><span>Branch name</span>
          <input autoFocus required maxLength={80} value={name} onChange={(e) => setName(e.target.value)} /></label>
        <label className="fld" style={{ flex: 1, minWidth: 140 }}><span>Address <span className="opt">(optional)</span></span>
          <input maxLength={200} value={address} onChange={(e) => setAddress(e.target.value)} /></label>
        <button className="primary" disabled={busy || !name.trim()} onClick={save}>{busy ? 'Saving…' : 'Save'}</button>
        <button disabled={busy} onClick={onCancel}>Cancel</button>
      </div>
      {err && <div style={{ color: 'var(--red)', fontSize: 13, marginTop: 6 }}>{err}</div>}
    </div>
  )
}

// One branch's tax office setup: FBR or PRA; Practice, Test or Live; for FBR, online (its internet gateway) or
// on this computer (FBR's software on the counter PC, PC edition only); the POSID, token and registered address.
// The saved token never comes back to the screen: typing a new one replaces it, "Remove" clears it.
function BranchFbr({ b, localAvailable, onSaved }) {
  const start = {
    // "On this computer" only works in the PC edition: elsewhere a branch saved that way (the old default) starts online.
    office: b.fiscalAuthority || 'FBR', env: b.fiscalEnvironment || 'Practice',
    mode: b.fbrMode === 'Local' && localAvailable ? 'Local' : 'Cloud',
    posId: b.fbrPosId || '', token: '', clearToken: false,
    ip: b.fbrStaticIp || '', testUrl: b.fbrEndpointOverride || '',
  }
  const [f, setF] = useState(start)
  const [err, setErr] = useState('')
  const [busy, setBusy] = useState(false)
  const [test, setTest] = useState(null)
  const set = (k, v) => { setF((x) => ({ ...x, [k]: v })); setTest(null) }
  const dirty = JSON.stringify(f) !== JSON.stringify(start)
  const online = f.office === 'PRA' || f.mode === 'Cloud'
  const hasToken = (b.hasFbrToken && !f.clearToken) || !!f.token.trim()

  const problem = () => {
    if (!isPosIdOrBlank(f.posId)) return 'POSID must be a whole number above 0.'
    if (f.env === 'Live') {
      if (!String(f.posId).trim()) return `Add the live POSID from ${f.office} before choosing Live.`
      if (online && !hasToken) return `Add the live ${f.office} token before choosing Live.`
    }
    if (f.env === 'Test' && f.testUrl.trim() && !/^https?:\/\//i.test(f.testUrl.trim())) return 'The test server address must start with https://'
    return ''
  }
  const save = async () => {
    const why = problem()
    if (why) return setErr(why)
    if (f.env === 'Live' && start.env !== 'Live') {
      const ok = await ui.confirm({
        title: `File real tax invoices with ${f.office}?`,
        message: `From now on every bill settled at ${b.name} is filed with ${f.office} as a real tax invoice. Check the POSID and token are the LIVE ones ${f.office} gave you, not the test ones.`,
        confirmText: 'Switch to Live',
      })
      if (!ok) return
    }
    setErr(''); setBusy(true)
    try {
      await api.updateBranch(b.id, {
        name: b.name, address: b.address || '', isActive: b.isActive,
        fiscalAuthority: f.office, fiscalEnvironment: f.env, fbrMode: f.office === 'PRA' ? 'Cloud' : f.mode,
        fbrSandbox: f.env !== 'Live',
        fbrPosId: String(f.posId).trim() ? Number(f.posId) : null,
        // null keeps the saved token; '' removes it.
        fbrToken: f.token.trim() ? f.token.trim() : f.clearToken ? '' : null,
        fbrStaticIp: f.ip.trim(),
        fbrEndpointOverride: f.env === 'Test' && f.testUrl.trim() ? f.testUrl.trim() : null,
      })
      toast.success(`${b.name}: saved.`)
      setF((x) => ({ ...x, token: '', clearToken: false }))   // the saved token is now "saved, ends …"
      onSaved()
    } catch (e) { setErr(friendlyError(e)) }
    setBusy(false)
  }
  const sendTest = async () => {
    setErr(''); setTest({ busy: true })
    try { setTest(await api.fbrTest(b.id)) }
    catch (e) { setTest(null); setErr(friendlyError(e)) }
  }

  return (
    <div style={{ background: 'var(--surface2)', borderRadius: 8, margin: '8px 0', padding: 12 }}>
      <div className="row" style={{ gap: 12, flexWrap: 'wrap', marginBottom: 10, alignItems: 'flex-end' }}>
        <label className="fld"><span>Tax office</span>
          <select value={f.office} onChange={(e) => set('office', e.target.value)}>
            <option value="FBR">FBR (federal sales tax)</option>
            <option value="PRA">PRA (Punjab Revenue Authority)</option>
          </select></label>
        {f.office === 'FBR' && (
          <label className="fld"><span>How bills reach FBR</span>
            <select value={f.mode} onChange={(e) => set('mode', e.target.value)}>
              <option value="Cloud">Online (FBR’s internet gateway)</option>
              <option value="Local">On this computer (FBR software on the counter PC)</option>
            </select></label>
        )}
      </div>
      <div className="set-head" style={{ marginBottom: 4 }}>Where bills go</div>
      <div className="seg" role="radiogroup" aria-label="Where bills go" style={{ marginBottom: 4 }}>
        {['Practice', 'Test', 'Live'].map((e) => (
          <button key={e} type="button" className={f.env === e ? 'on' : ''} aria-pressed={f.env === e} onClick={() => set('env', e)}>{e}</button>
        ))}
      </div>
      <div className="muted" style={{ fontSize: 12, marginBottom: 10 }}>{ENV_TEXT[f.env]}</div>

      {f.env !== 'Practice' && (
        <div className="row" style={{ gap: 8, flexWrap: 'wrap', marginBottom: 8, alignItems: 'flex-end' }}>
          <label className="fld" style={{ width: 150 }}><span>{f.env === 'Live' ? 'Live' : 'Test'} POSID</span>
            <input type="number" min="1" step="1" inputMode="numeric" value={f.posId} onChange={(e) => set('posId', e.target.value)} /></label>
          {online && (
            <label className="fld" style={{ flex: 1, minWidth: 200 }}><span>{f.env === 'Live' ? 'Live' : 'Test'} token{b.hasFbrToken && !f.clearToken && !f.token ? ` (saved, ends …${b.tokenEnds})` : ''}</span>
              <input maxLength={200} autoComplete="off" spellCheck={false}
                placeholder={b.hasFbrToken && !f.clearToken ? 'Type a new token to replace the saved one' : `Paste the token from ${f.office}'s portal`}
                value={f.token} onChange={(e) => set('token', e.target.value)} /></label>
          )}
          {online && b.hasFbrToken && !f.clearToken && (
            <button type="button" className="ghost btn-sm" onClick={() => { set('clearToken', true); set('token', '') }}>Remove saved token</button>
          )}
          {online && (
            <label className="fld" style={{ width: 160 }}><span>Registered IP <span className="opt">(optional)</span></span>
              <input maxLength={45} inputMode="decimal" placeholder="203.0.113.10" value={f.ip} onChange={(e) => set('ip', e.target.value)} /></label>
          )}
        </div>
      )}
      {f.clearToken && <div className="muted" style={{ fontSize: 12, marginBottom: 8 }}>The saved token will be removed when you save.</div>}
      {f.env === 'Test' && (
        <label className="fld" style={{ marginBottom: 8, maxWidth: 460 }}><span>Test server address <span className="opt">(optional: only if {f.office} gave you a different one)</span></span>
          <input maxLength={300} placeholder="Leave blank for the office's own test server" value={f.testUrl} onChange={(e) => set('testUrl', e.target.value)} /></label>
      )}
      <div className="muted" style={{ fontSize: 12, marginBottom: 10 }}>
        {f.env === 'Practice' ? 'No POSID or token needed in Practice.'
          : !online ? (localAvailable
              ? 'Bills go to the FBR software on this PC. It must be installed and signed in with this POSID.'
              : '“On this computer” only works in the PC edition, which runs on the counter PC. The online system can’t reach a PC’s FBR software, so choose “Online”.')
          : `${f.office} only accepts bills from the internet address it has on record for you. If bills are refused with “didn't accept the token”, ask ${f.office} to add this system's address.`}
      </div>
      <div className="row" style={{ gap: 8, flexWrap: 'wrap' }}>
        <button className="primary" disabled={busy || !dirty} onClick={save}>{busy ? 'Saving…' : 'Save'}</button>
        {start.env !== 'Live' && (
          <button className="ghost" disabled={busy || dirty || test?.busy} title={dirty ? 'Save first' : ''} onClick={sendTest}>
            {test?.busy ? 'Sending…' : 'Send a test bill'}
          </button>
        )}
        {dirty && start.env !== 'Live' && <span className="muted" style={{ fontSize: 12, alignSelf: 'center' }}>Save first, then send a test bill.</span>}
      </div>
      {test && !test.busy && (
        <div className="card" style={{ marginTop: 8, fontSize: 13, borderColor: test.accepted ? 'var(--accent2)' : 'var(--red)' }}>
          {test.accepted
            ? <><b>Accepted by {test.office} {test.environment}.</b> Number: <span style={{ fontFamily: 'monospace', wordBreak: 'break-all' }}>{test.number}</span>
                <div className="muted">A Rs {test.total} test bill (Rs {test.tax} tax). Nothing was saved.</div></>
            : <><b style={{ color: 'var(--red)' }}>Not accepted ({test.office} {test.environment}).</b> <span>{test.error}</span></>}
        </div>
      )}
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

  const [busy, setBusy] = useState(false)
  const add = async () => {
    if (busy) return
    // The code goes into the table's QR link, so no spaces or symbols.
    if (!/^[A-Za-z0-9_-]+$/.test(code.trim())) return setErr('Table code can only have letters, numbers, dashes and underscores. No spaces.')
    setErr(''); setBusy(true)
    try { await api.addTable(code.trim(), name.trim(), Number(branchId)); setCode(''); setName(''); load() }
    catch (e) { setErr(friendlyError(e)) }
    setBusy(false)
  }
  const rename = async (t) => {
    const name = await ui.prompt({ title: `Rename ${t.code}`, label: 'Table name', defaultValue: t.name, confirmText: 'Rename',
      required: true, requiredMessage: 'Enter a table name.', maxLength: 60 })
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
      <div className="row" style={{ gap: 8, flexWrap: 'wrap', alignItems: 'flex-end' }}>
        <label className="fld" style={{ width: 110 }}><span>Code</span>
          <input required maxLength={20} placeholder="T7" autoCapitalize="characters" spellCheck="false" value={code} onChange={(e) => setCode(e.target.value)} /></label>
        <label className="fld" style={{ flex: 1, minWidth: 120 }}><span>Name</span>
          <input required maxLength={60} placeholder="Table 7" value={name} onChange={(e) => setName(e.target.value)} /></label>
        {branches.length > 1 && (
          <label className="fld"><span>Branch</span>
            <select required value={branchId} onChange={(e) => setBranchId(e.target.value)}>
              {branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
            </select></label>
        )}
        <button className="primary" disabled={busy || !code.trim() || !name.trim()} onClick={add}><PlusIcon /> {busy ? 'Adding…' : 'Add'}</button>
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

  const [busy, setBusy] = useState(false)
  const add = async () => {
    if (busy) return
    const why = usernameProblem(f.username) || (f.password.length < 6 ? 'Password must be at least 6 characters.' : '')
      || (isPosIdOrBlank(f.fbrPosId) ? '' : 'POS ID must be a whole number above 0.')
    if (why) return setErr(why)
    setErr(''); setBusy(true)
    try {
      await api.addStaff({
        username: f.username.trim(), displayName: f.displayName.trim(), role: f.role, password: f.password,
        fbrPosId: String(f.fbrPosId).trim() ? Number(f.fbrPosId) : null, fbrToken: f.fbrToken.trim() || null,
      })
      setF({ username: '', displayName: '', role: 'Cashier', password: '', fbrPosId: '', fbrToken: '' }); load()
    } catch (e) { setErr(friendlyError(e)) }
    setBusy(false)
  }
  const resetPw = async (s) => {
    const pw = await ui.prompt({ title: `New password for ${s.username}`, label: 'New password', type: 'password', placeholder: '6 or more characters',
      confirmText: 'Update', required: true, requiredMessage: 'Enter a new password.', maxLength: 200,
      validate: (v) => (v.length < 6 ? 'Password must be at least 6 characters.' : '') })
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
      <div className="row" style={{ gap: 8, marginBottom: 6, flexWrap: 'wrap' }}>
        <label className="fld" style={{ flex: 1, minWidth: 140 }}><span>Their name</span>
          <input required maxLength={60} value={f.displayName} onChange={(e) => set('displayName', e.target.value)} /></label>
        <label className="fld" style={{ flex: 1, minWidth: 140 }}><span>Mobile number or username</span>
          <input required maxLength={60} placeholder="03xx xxxxxxx" value={f.username} onChange={(e) => set('username', e.target.value)} autoCapitalize="none" spellCheck="false" /></label>
      </div>
      <div className="row" style={{ gap: 8, marginBottom: 6, flexWrap: 'wrap' }}>
        <label className="fld"><span>Role</span>
          <select required value={f.role} onChange={(e) => set('role', e.target.value)}>{ROLES.map((r) => <option key={r} value={r}>{roleLabel(r)}</option>)}</select></label>
        <label className="fld" style={{ flex: 1, minWidth: 160 }}><span>Password</span>
          <PasswordInput required maxLength={200} autoComplete="new-password" placeholder="6 or more characters" value={f.password} onChange={(e) => set('password', e.target.value)} /></label>
      </div>
      {fbrOn && (
        <div className="row" style={{ gap: 8, marginBottom: 6, flexWrap: 'wrap' }}>
          <label className="fld" style={{ width: 150 }}><span>FBR POS ID <span className="opt">(optional)</span></span>
            <input type="number" min="1" step="1" inputMode="numeric" value={f.fbrPosId} onChange={(e) => set('fbrPosId', e.target.value)} /></label>
          <label className="fld" style={{ flex: 1, minWidth: 160 }}><span>FBR token <span className="opt">(cloud, optional)</span></span>
            <input maxLength={200} value={f.fbrToken} onChange={(e) => set('fbrToken', e.target.value)} /></label>
        </div>
      )}
      <div className="row" style={{ gap: 8, alignItems: 'center' }}>
        {!busy && (!f.username.trim() || !f.displayName.trim() || !f.password) && <span className="muted" style={{ fontSize: 12 }}>Fill in the name, username and password to add them.</span>}
        <button className="primary" style={{ marginLeft: 'auto' }} disabled={busy || !f.username.trim() || !f.displayName.trim() || !f.password} onClick={add}><PlusIcon /> {busy ? 'Adding…' : 'Add member'}</button>
      </div>
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
    // The username is only checked, and only sent, when it was changed: an owner who signed up before the rule
    // (with a space, say) can still edit their name and role without having to rename their sign-in.
    const renamed = f.username.trim() !== s.username
    const why = (renamed ? usernameProblem(f.username) : '') || (isPosIdOrBlank(f.fbrPosId) ? '' : 'POS ID must be a whole number above 0.')
    if (why) return setErr(why)
    setErr(''); setBusy(true)
    try {
      await api.updateStaff(s.id, {
        username: renamed ? f.username.trim() : null, displayName: f.displayName.trim(), role: f.role,
        fbrPosId: String(f.fbrPosId).trim() ? Number(f.fbrPosId) : null,
        ...(f.fbrToken !== '' ? { fbrToken: f.fbrToken } : {}),   // blank = keep token
      })
      onDone()
    } catch (e) { setErr(friendlyError(e)); setBusy(false) }
  }
  return (
    <div className="edit-panel">
      <div className="row" style={{ gap: 8, marginBottom: 8, flexWrap: 'wrap', alignItems: 'flex-end' }}>
        <label className="fld" style={{ flex: 1, minWidth: 130 }}><span>Their name</span>
          <input autoFocus required maxLength={60} value={f.displayName} onChange={(e) => set('displayName', e.target.value)} /></label>
        <label className="fld" style={{ flex: 1, minWidth: 130 }}><span>Mobile number or username</span>
          <input required maxLength={60} value={f.username} onChange={(e) => set('username', e.target.value)} autoCapitalize="none" spellCheck="false" /></label>
        <label className="fld"><span>Role</span>
          <select required value={f.role} onChange={(e) => set('role', e.target.value)}>{ROLES.map((r) => <option key={r} value={r}>{roleLabel(r)}</option>)}</select></label>
      </div>
      {fbrOn && (
        <div className="row" style={{ gap: 8, marginBottom: 8, flexWrap: 'wrap' }}>
          <label className="fld" style={{ width: 130 }}><span>FBR POS ID <span className="opt">(optional)</span></span>
            <input type="number" min="1" step="1" inputMode="numeric" value={f.fbrPosId} onChange={(e) => set('fbrPosId', e.target.value)} /></label>
          <label className="fld" style={{ flex: 1, minWidth: 150 }}><span>FBR token <span className="opt">(optional)</span></span>
            <input maxLength={200} placeholder={s.hasFbrToken ? 'Token set. Type to change' : 'Cloud mode only'} value={f.fbrToken} onChange={(e) => set('fbrToken', e.target.value)} /></label>
        </div>
      )}
      <div className="row" style={{ gap: 8 }}>
        <button className="primary" disabled={busy || !f.username.trim() || !f.displayName.trim()} onClick={save}>{busy ? 'Saving…' : 'Save changes'}</button>
        <button disabled={busy} onClick={onCancel}>Cancel</button>
      </div>
      {err && <div style={{ color: 'var(--red)', fontSize: 13, marginTop: 6 }}>{err}</div>}
    </div>
  )
}
