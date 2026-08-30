import { useEffect, useState } from 'react'
import { api } from '../api'
import { ui, toast } from '../ui.jsx'
import { friendlyError } from '../errors'

// Owner/manager inventory: stock levels, recipes (what each dish consumes), and
// the movement log. Stock auto-deducts when orders are approved.
export default function Inventory() {
  const [stock, setStock] = useState([])
  const [menu, setMenu] = useState([])
  const [moves, setMoves] = useState([])
  const [ns, setNs] = useState({ name: '', unit: 'kg', quantity: '', lowThreshold: '' })
  const [recipeFor, setRecipeFor] = useState(null)
  const [err, setErr] = useState('')

  const load = () => {
    api.listStock().then(setStock).catch((e) => setErr(e.message))
    api.movements().then(setMoves).catch(() => {})
  }
  useEffect(() => { load(); api.adminMenu().then((m) => setMenu(m.menu)).catch(() => {}) }, [])

  const add = async () => {
    setErr('')
    try {
      await api.addStock({ name: ns.name.trim(), unit: ns.unit.trim(), quantity: Number(ns.quantity) || 0, lowThreshold: Number(ns.lowThreshold) || 0 })
      setNs({ name: '', unit: 'kg', quantity: '', lowThreshold: '' }); load()
    } catch (e) { setErr(e.message) }
  }
  const restock = async (s) => { const q = await ui.prompt({ title: `Add stock — ${s.name}`, placeholder: s.unit, confirmText: 'Add', required: true }); if (q && Number(q) > 0) { try { await api.restock(s.id, Number(q), 'Manual restock'); load() } catch (e) { toast.error(friendlyError(e)) } } }
  const adjust = async (s) => { const q = await ui.prompt({ title: `Set count — ${s.name}`, defaultValue: String(s.quantity), placeholder: s.unit, confirmText: 'Set' }); if (q != null && Number(q) >= 0) { try { await api.adjustStock(s.id, Number(q), 'Stock count'); load() } catch (e) { toast.error(friendlyError(e)) } } }
  const editThreshold = async (s) => { const q = await ui.prompt({ title: `Low-stock alert — ${s.name}`, defaultValue: String(s.lowThreshold), placeholder: s.unit, confirmText: 'Save' }); if (q != null) { try { await api.updateStock(s.id, { name: s.name, unit: s.unit, quantity: s.quantity, lowThreshold: Number(q) || 0 }); load() } catch (e) { toast.error(friendlyError(e)) } } }
  const del = async (s) => { if (await ui.confirm({ title: 'Delete stock item', message: `Delete ${s.name}?`, confirmText: 'Delete', danger: true })) { try { await api.deleteStock(s.id); load() } catch (e) { toast.error(friendlyError(e)) } } }

  const lowCount = stock.filter((s) => s.isLow).length

  return (
    <div className="wrap">
      <div className="row between">
        <div className="h" style={{ margin: 0 }}>Inventory</div>
        {lowCount > 0 && <span className="pill s-Placed">⚠ {lowCount} low</span>}
      </div>
      <p className="muted">Track ingredients and what each dish uses. Stock deducts automatically when an order is sent to the kitchen.</p>

      <div className="grid g2">
        <div className="card">
          <b>Stock</b>
          <div style={{ margin: '10px 0' }}>
            {stock.map((s) => (
              <div key={s.id} className="row between" style={{ padding: '8px 0', borderBottom: '1px solid var(--line)' }}>
                <div>
                  <b>{s.name}</b> {s.isLow && <span className="pill s-Placed" style={{ marginLeft: 4 }}>LOW</span>}
                  <div className="muted" style={{ fontSize: 13 }}>{s.quantity} {s.unit} · alert ≤ {s.lowThreshold}</div>
                </div>
                <div className="row" style={{ gap: 4 }}>
                  <button className="ghost" onClick={() => restock(s)}>+ Stock</button>
                  <button className="ghost" onClick={() => adjust(s)}>Count</button>
                  <button className="ghost" onClick={() => editThreshold(s)}>Alert</button>
                  <button className="ghost" onClick={() => del(s)}>🗑</button>
                </div>
              </div>
            ))}
            {stock.length === 0 && <div className="muted" style={{ fontSize: 13 }}>No stock items yet.</div>}
          </div>
          <div className="cat-title" style={{ fontSize: 13, margin: '6px 0' }}>Add stock item</div>
          <div className="row" style={{ gap: 8, marginBottom: 6 }}>
            <input placeholder="Name" value={ns.name} onChange={(e) => setNs({ ...ns, name: e.target.value })} style={{ flex: 2 }} />
            <input placeholder="Unit" value={ns.unit} onChange={(e) => setNs({ ...ns, unit: e.target.value })} style={{ width: 70 }} />
          </div>
          <div className="row" style={{ gap: 8 }}>
            <input type="number" placeholder="Qty" value={ns.quantity} onChange={(e) => setNs({ ...ns, quantity: e.target.value })} style={{ flex: 1 }} />
            <input type="number" placeholder="Low at" value={ns.lowThreshold} onChange={(e) => setNs({ ...ns, lowThreshold: e.target.value })} style={{ flex: 1 }} />
            <button className="primary" disabled={!ns.name.trim()} onClick={add}>Add</button>
          </div>
          {err && <div style={{ color: 'var(--red)', fontSize: 13, marginTop: 6 }}>{err}</div>}
        </div>

        <div className="card">
          <b>Recipes</b>
          <p className="muted" style={{ fontSize: 13, margin: '4px 0 10px' }}>Set what each dish consumes per serving.</p>
          {menu.map((c) => (
            <div key={c.id} style={{ marginBottom: 8 }}>
              <div className="muted" style={{ fontSize: 12, textTransform: 'uppercase', letterSpacing: '.04em' }}>{c.name}</div>
              {c.items.map((it) => (
                <div key={it.id} className="row between" style={{ padding: '5px 0' }}>
                  <span>{it.emoji} {it.name}</span>
                  <button className="ghost" onClick={() => setRecipeFor(it)}>Edit recipe</button>
                </div>
              ))}
            </div>
          ))}
        </div>
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
    </div>
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
  const save = async () => {
    setBusy(true)
    try { await api.setRecipe(item.id, lines.filter((l) => l.stockItemId && Number(l.qtyPerServing) > 0).map((l) => ({ stockItemId: Number(l.stockItemId), qtyPerServing: Number(l.qtyPerServing) }))); onClose() }
    catch (e) { ui.alert(e.message) }
    setBusy(false)
  }

  return (
    <div className="overlay" onClick={(e) => { if (e.target.className === 'overlay') onClose() }}>
      <div className="modal">
        <div className="row between"><div className="big">{item.name} — recipe</div><button className="ghost" onClick={onClose}>✕</button></div>
        {stock.length === 0 && <div className="muted" style={{ margin: '10px 0' }}>Add stock items first.</div>}
        <div style={{ margin: '10px 0' }}>
          {lines.map((l, i) => (
            <div key={i} className="row" style={{ gap: 8, marginBottom: 6 }}>
              <select value={l.stockItemId} onChange={(e) => setLine(i, 'stockItemId', e.target.value)} style={{ flex: 2 }}>
                {stock.map((s) => <option key={s.id} value={s.id}>{s.name} ({s.unit})</option>)}
              </select>
              <input type="number" value={l.qtyPerServing} onChange={(e) => setLine(i, 'qtyPerServing', e.target.value)} style={{ width: 90 }} />
              <button className="ghost" onClick={() => del(i)}>✕</button>
            </div>
          ))}
        </div>
        <button className="ghost" onClick={add} disabled={stock.length === 0}>+ ingredient</button>
        <div className="row" style={{ gap: 8, marginTop: 12 }}>
          <button onClick={onClose}>Cancel</button>
          <button className="green" style={{ marginLeft: 'auto' }} disabled={busy} onClick={save}>Save recipe</button>
        </div>
      </div>
    </div>
  )
}
