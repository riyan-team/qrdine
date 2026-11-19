import { useEffect, useState } from 'react'
import { api, money } from '../api'
import { Modal } from '../backoffice.jsx'
import { ui, toast } from '../ui.jsx'
import { friendlyError } from '../errors'
import { WarnIcon, TrashIcon, CloseIcon } from '../icons.jsx'

// Owner/manager inventory: stock levels, recipes (what each dish consumes), and
// the movement log. Stock auto-deducts when orders are approved.
export default function Inventory() {
  const [stock, setStock] = useState([])
  const [menu, setMenu] = useState([])
  const [moves, setMoves] = useState([])
  const [ns, setNs] = useState({ name: '', unit: 'kg', quantity: '', lowThreshold: '' })
  const [recipeFor, setRecipeFor] = useState(null)
  const [wasting, setWasting] = useState(null)
  const [recipesOn, setRecipesOn] = useState(false)   // driven by the stock-tracking setting
  const [err, setErr] = useState('')

  const load = () => {
    api.listStock().then(setStock).catch((e) => setErr(e.message))
    api.movements().then(setMoves).catch(() => {})
  }
  useEffect(() => {
    load()
    api.adminMenu().then((m) => setMenu(m.menu)).catch(() => {})
    // Recipes only make sense when stock tracking is on — mirror the setting here.
    api.getBillingSettings().then((s) => setRecipesOn(!!s.stockTrackingEnabled)).catch(() => {})
  }, [])

  const [busy, setBusy] = useState(false)
  const add = async () => {
    if (busy) return
    const q = Number(ns.quantity || 0), low = Number(ns.lowThreshold || 0)
    if (!ns.unit.trim()) return setErr('Enter the unit, like kg or pcs.')
    if (!Number.isFinite(q) || q < 0) return setErr("Quantity can't be negative.")
    if (!Number.isFinite(low) || low < 0) return setErr("The low-stock alert can't be negative.")
    setErr(''); setBusy(true)
    try {
      await api.addStock({ name: ns.name.trim(), unit: ns.unit.trim(), quantity: q, lowThreshold: low })
      setNs({ name: '', unit: 'kg', quantity: '', lowThreshold: '' }); load()
    } catch (e) { setErr(friendlyError(e)) }
    setBusy(false)
  }
  // Every amount box asks for a number and says what's wrong, instead of doing nothing.
  const amount = (s, extra) => ({ type: 'number', inputMode: 'decimal', label: `Amount (${s.unit})`, required: true, requiredMessage: 'Enter an amount.', ...extra })
  const restock = async (s) => { const q = await ui.prompt({ title: `Add stock: ${s.name}`, placeholder: s.unit, confirmText: 'Add', ...amount(s, { validate: (v) => (Number(v) > 0 ? '' : 'Enter how much came in.') }) }); if (q) { try { await api.restock(s.id, Number(q), 'Manual restock'); load() } catch (e) { toast.error(friendlyError(e)) } } }
  const adjust = async (s) => { const q = await ui.prompt({ title: `Set count: ${s.name}`, defaultValue: String(s.quantity), placeholder: s.unit, confirmText: 'Set', ...amount(s, { label: `Counted (${s.unit})`, min: 0 }) }); if (q != null) { try { await api.adjustStock(s.id, Number(q), 'Stock count'); load() } catch (e) { toast.error(friendlyError(e)) } } }
  const editThreshold = async (s) => { const q = await ui.prompt({ title: `Low-stock alert: ${s.name}`, message: 'Warn me when stock falls to this amount. 0 turns the alert off.', defaultValue: String(s.lowThreshold), placeholder: s.unit, confirmText: 'Save', ...amount(s, { label: `Alert at (${s.unit})`, min: 0 }) }); if (q != null) { try { await api.updateStock(s.id, { name: s.name, unit: s.unit, quantity: s.quantity, lowThreshold: Number(q) }); load() } catch (e) { toast.error(friendlyError(e)) } } }
  const del = async (s) => { if (await ui.confirm({ title: 'Delete stock item', message: `Delete ${s.name}?`, confirmText: 'Delete', danger: true })) { try { await api.deleteStock(s.id); load() } catch (e) { toast.error(friendlyError(e)) } } }

  const lowCount = stock.filter((s) => s.isLow).length

  return (
    <div className="wrap">
      <div className="row between">
        <div className="h" style={{ margin: 0 }}>Inventory</div>
        {lowCount > 0 && <span className="pill s-Placed"><WarnIcon size={13} /> {lowCount} low</span>}
      </div>
      <p className="muted">{recipesOn
        ? 'Track ingredients and what each dish uses. Stock deducts automatically when an order is sent to the kitchen.'
        : 'Track your ingredient stock levels. Turn on stock tracking in Settings to link dishes to ingredients (recipes).'}</p>

      {lowCount > 0 && (
        <div className="alert-banner warn" role="alert">
          <WarnIcon size={18} />
          <div>
            <b>{lowCount} item{lowCount > 1 ? 's' : ''} running low.</b> Reorder soon —{' '}
            {stock.filter((s) => s.isLow).map((s) => `${s.name} (${s.quantity} ${s.unit})`).join(', ')}.
          </div>
        </div>
      )}

      <div className={recipesOn ? 'grid g2' : ''}>
        <div className="card">
          <b>Stock</b>
          <div style={{ margin: '10px 0' }}>
            {stock.map((s) => (
              <div key={s.id} className="row between" style={{ padding: '8px 0', borderBottom: '1px solid var(--line)' }}>
                <div>
                  <b>{s.name}</b> {s.isLow && <span className="pill s-Placed" style={{ marginLeft: 4 }}>LOW</span>}
                  <div className="muted" style={{ fontSize: 13 }}>{s.quantity} {s.unit} · low at {s.lowThreshold}{s.lastCost > 0 ? ` · last bought at ${money(s.lastCost)}` : ''}</div>
                </div>
                <div className="row" style={{ gap: 4 }}>
                  <button className="ghost" onClick={() => restock(s)}>+ Stock</button>
                  <button className="ghost" onClick={() => setWasting(s)}>Waste</button>
                  <button className="ghost" onClick={() => adjust(s)}>Count</button>
                  <button className="ghost" onClick={() => editThreshold(s)}>Alert</button>
                  <button className="ghost" onClick={() => del(s)}><TrashIcon size={15} /></button>
                </div>
              </div>
            ))}
            {stock.length === 0 && <div className="muted" style={{ fontSize: 13 }}>No stock items yet.</div>}
          </div>
          <div className="cat-title" style={{ fontSize: 13, margin: '6px 0' }}>Add stock item</div>
          <div className="row" style={{ gap: 8, marginBottom: 6, alignItems: 'flex-end' }}>
            <label className="fld" style={{ flex: 2 }}><span>Name</span>
              <input required maxLength={80} placeholder="e.g. Chicken" value={ns.name} onChange={(e) => setNs({ ...ns, name: e.target.value })} /></label>
            <label className="fld" style={{ width: 80 }}><span>Unit</span>
              <input required maxLength={20} value={ns.unit} onChange={(e) => setNs({ ...ns, unit: e.target.value })} style={{ minWidth: 0 }} /></label>
          </div>
          <div className="row" style={{ gap: 8, alignItems: 'flex-end' }}>
            <label className="fld" style={{ flex: 1 }}><span>In stock now <span className="opt">(optional)</span></span>
              <input type="number" min="0" step="any" inputMode="decimal" placeholder="0" value={ns.quantity} onChange={(e) => setNs({ ...ns, quantity: e.target.value })} style={{ minWidth: 0 }} /></label>
            <label className="fld" style={{ flex: 1 }}><span>Warn when at <span className="opt">(optional)</span></span>
              <input type="number" min="0" step="any" inputMode="decimal" placeholder="0" value={ns.lowThreshold} onChange={(e) => setNs({ ...ns, lowThreshold: e.target.value })} style={{ minWidth: 0 }} /></label>
            <button className="primary" disabled={busy || !ns.name.trim()} onClick={add}>{busy ? 'Adding…' : 'Add'}</button>
          </div>
          {err && <div style={{ color: 'var(--red)', fontSize: 13, marginTop: 6 }}>{err}</div>}
        </div>

        {recipesOn && <div className="card">
          <b>Recipes</b>
          <p className="muted" style={{ fontSize: 13, margin: '4px 0 10px' }}>Set what each dish consumes per serving.</p>
          {menu.map((c) => (
            <div key={c.id} style={{ marginBottom: 8 }}>
              <div className="muted" style={{ fontSize: 12, textTransform: 'uppercase', letterSpacing: '.04em' }}>{c.name}</div>
              {c.items.map((it) => (
                <div key={it.id} className="row between" style={{ padding: '5px 0' }}>
                  <span>{it.name}</span>
                  <button className="ghost" onClick={() => setRecipeFor(it)}>Edit recipe</button>
                </div>
              ))}
            </div>
          ))}
        </div>}
      </div>

      <div className="card" style={{ marginTop: 16 }}>
        <b>Recent stock movements</b>
        <div style={{ marginTop: 8 }}>
          {moves.length === 0 && <div className="muted" style={{ fontSize: 13 }}>No movements yet.</div>}
          {moves.map((m, i) => (
            <div key={i} className="row between" style={{ fontSize: 13, padding: '4px 0', borderBottom: '1px solid var(--line)' }}>
              <span>{m.item} <span className="muted">· {m.type}{m.reason ? ` · ${m.reason}` : ''}</span></span>
              <b style={{ color: m.delta < 0 ? 'var(--red)' : 'var(--accent2)' }}>{m.delta > 0 ? '+' : ''}{m.delta}</b>
            </div>
          ))}
        </div>
      </div>

      {recipeFor && <RecipeEditor item={recipeFor} stock={stock} onClose={() => setRecipeFor(null)} />}
      {wasting && <WasteForm item={wasting} onClose={() => setWasting(null)} onSaved={() => { setWasting(null); load() }} />}
    </div>
  )
}

const WASTE_REASONS = ['Went bad', 'Spilled', 'Burnt', 'Staff meal', 'Expired']

function WasteForm({ item, onClose, onSaved }) {
  const [qty, setQty] = useState('')
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')
  const save = async () => {
    if (busy) return
    setBusy(true); setErr('')
    try { await api.wasteStock(item.id, Number(qty), reason.trim()); onSaved() }
    catch (e) { setErr(friendlyError(e)); setBusy(false) }
  }
  const missing = !(Number(qty) > 0) ? 'Enter how much was wasted.' : !reason.trim() ? 'Pick or type a reason.' : ''
  return (
    <Modal title={`Record waste: ${item.name}`} sub={`${item.quantity} ${item.unit} in stock. This shows on your stock report.`} onClose={onClose} footer={<>
      <button onClick={onClose}>Cancel</button>
      {missing && !busy && <span className="muted" style={{ fontSize: 12 }}>{missing}</span>}
      <button className="primary push" disabled={busy || !!missing} onClick={save}>{busy ? 'Saving…' : 'Record'}</button>
    </>}>
      <div className="bo-form">
        <div className="field is-full"><label className="field-label" htmlFor="waste-qty">How much ({item.unit})</label><input id="waste-qty" autoFocus required type="number" min="0" step="any" inputMode="decimal" value={qty} onChange={(e) => setQty(e.target.value)} /></div>
      </div>
      <div className="field-label req" id="waste-reason-label" style={{ marginTop: 12 }}>Reason</div>
      <div className="bo-chips" role="group" aria-labelledby="waste-reason-label" style={{ margin: '4px 0 8px' }}>
        {WASTE_REASONS.map((r) => <button key={r} aria-pressed={reason === r} className={`bo-chip${reason === r ? ' on' : ''}`} onClick={() => setReason(r)}>{r}</button>)}
      </div>
      <input style={{ width: '100%' }} aria-labelledby="waste-reason-label" maxLength={200} placeholder="Or type a reason" value={reason} onChange={(e) => setReason(e.target.value)} />
      {err && <div className="form-error" style={{ marginTop: 10 }}>{err}</div>}
    </Modal>
  )
}

function RecipeEditor({ item, stock, onClose }) {
  const [lines, setLines] = useState([])
  const [busy, setBusy] = useState(false)
  useEffect(() => {
    api.getRecipe(item.id).then((r) => setLines(r.map((l) => ({ stockItemId: l.stockItemId, qtyPerServing: l.qtyPerServing })))).catch(() => {})
  }, [item.id])

  const add = () => setLines([...lines, { stockItemId: stock[0]?.id || 0, qtyPerServing: 1 }])
  const setLine = (i, k, v) => setLines(lines.map((l, n) => n === i ? { ...l, [k]: v } : l))
  const del = (i) => setLines(lines.filter((_, n) => n !== i))
  const [err, setErr] = useState('')
  const save = async () => {
    if (busy) return
    if (lines.some((l) => !(Number(l.qtyPerServing) > 0))) return setErr('Each ingredient needs an amount above 0, or remove that line.')
    setErr(''); setBusy(true)
    try { await api.setRecipe(item.id, lines.filter((l) => l.stockItemId && Number(l.qtyPerServing) > 0).map((l) => ({ stockItemId: Number(l.stockItemId), qtyPerServing: Number(l.qtyPerServing) }))); onClose() }
    catch (e) { setErr(friendlyError(e)) }
    setBusy(false)
  }

  return (
    <div className="overlay" onClick={(e) => { if (e.target.className === 'overlay') onClose() }}>
      <div className="modal">
        <div className="row between"><div className="big">{item.name}: recipe</div><button className="ghost" onClick={onClose}><CloseIcon size={16} /></button></div>
        {stock.length === 0 && <div className="muted" style={{ margin: '10px 0' }}>Add stock items first.</div>}
        <div style={{ margin: '10px 0' }}>
          {lines.map((l, i) => (
            <div key={i} className="row" style={{ gap: 8, marginBottom: 6 }}>
              <select aria-label={`Ingredient ${i + 1}`} value={l.stockItemId} onChange={(e) => setLine(i, 'stockItemId', e.target.value)} style={{ flex: 2 }}>
                {stock.map((s) => <option key={s.id} value={s.id}>{s.name} ({s.unit})</option>)}
              </select>
              <input aria-label={`Amount per serving, ingredient ${i + 1}`} required type="number" min="0" step="any" inputMode="decimal" value={l.qtyPerServing} onChange={(e) => setLine(i, 'qtyPerServing', e.target.value)} style={{ width: 90 }} />
              <button className="ghost" aria-label={`Remove ingredient ${i + 1}`} onClick={() => del(i)}><CloseIcon size={15} /></button>
            </div>
          ))}
          {lines.length > 0 && <div className="muted" style={{ fontSize: 12 }}>The amount is per serving, in the ingredient's unit.</div>}
        </div>
        <button className="ghost" onClick={add} disabled={stock.length === 0}>+ ingredient</button>
        {err && <div role="alert" style={{ color: 'var(--red)', fontSize: 13, marginTop: 8 }}>{err}</div>}
        <div className="row" style={{ gap: 8, marginTop: 12 }}>
          <button onClick={onClose}>Cancel</button>
          <button className="green" style={{ marginLeft: 'auto' }} disabled={busy} onClick={save}>{busy ? 'Saving…' : 'Save recipe'}</button>
        </div>
      </div>
    </div>
  )
}
