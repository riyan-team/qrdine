import { useEffect, useState } from 'react'
import { api, money } from './api'
import { auth } from './auth'
import { ui } from './ui.jsx'
import { CheckIcon, KitchenIcon, BellIcon, CloseIcon } from './icons.jsx'

// Shared ordering primitives used by BOTH the guest QR menu (Menu.jsx) and the
// waiter's take-order screen (Waiter.jsx), so the cart/customizer/menu-rendering
// logic lives in exactly one place.

export const needsCustomizing = (it) =>
  (it.variants?.length || 0) > 0 || (it.modifierGroups?.length || 0) > 0

// Cart state keyed by a line "signature" (item + variant + chosen modifiers), so
// identical customizations stack and different ones stay separate.
export function useCart(storageKey) {
  const [cart, setCart] = useState(() => {   // { sig: line }
    if (!storageKey) return {}
    try { return JSON.parse(sessionStorage.getItem(storageKey) || '{}') } catch { return {} }
  })
  useEffect(() => {
    if (!storageKey) return
    try { sessionStorage.setItem(storageKey, JSON.stringify(cart)) } catch { /* private mode: cart just won't survive a reload */ }
  }, [cart, storageKey])

  const addToCart = (line) => setCart((c) => {
    const existing = c[line.sig]
    return { ...c, [line.sig]: { ...line, qty: (existing?.qty || 0) + line.qty } }
  })
  const changeQty = (sig, delta) => setCart((c) => {
    const line = c[sig]; if (!line) return c
    const qty = line.qty + delta
    const n = { ...c }
    if (qty <= 0) delete n[sig]; else n[sig] = { ...line, qty }
    return n
  })
  const clear = () => setCart({})

  const list = Object.values(cart)
  const total = list.reduce((s, x) => s + x.unitPrice * x.qty, 0)
  const count = list.reduce((s, x) => s + x.qty, 0)
  return { list, total, count, addToCart, changeQty, clear }
}

// Turn a customizable item click into a plain add, or open the customizer.
export function plusOrCustomize(it, addToCart, setCustomizing) {
  if (needsCustomizing(it)) { setCustomizing(it); return }
  addToCart({
    sig: `${it.id}`, item: it, name: it.name, variantId: null, variantName: null,
    modifierOptionIds: [], modifierNames: [],
    unitPrice: (it.dealActive && it.dealPrice != null) ? it.dealPrice : it.price, qty: 1,
  })
}

// Map cart lines to the API's order-line shape (shared by guest + waiter placement).
export const cartToItems = (list) => list.map((x) => ({
  menuItemId: x.item.id, qty: x.qty, note: x.note ?? null,
  variantId: x.variantId, modifierOptionIds: x.modifierOptionIds,
}))

// The scrollable menu: categories → items, each with a +/Choose button.
// Each category heading carries a stable anchor id so a quick-nav can scroll to it.
export const catAnchor = (id) => `qdcat-${id}`

export function MenuList({ menu, onPlus }) {
  return (
    <>
      {menu.map((cat) => (
        <div key={cat.id} id={catAnchor(cat.id)} style={{ scrollMarginTop: 110 }}>
          <div className="cat-title">{cat.name}</div>
          <div className="card">
            {cat.items.map((it) => {
              const from = it.variants?.length ? Math.min(...it.variants.map((v) => v.price)) : it.price
              return (
                <div className="item" key={it.id}>
                  <div className="emoji">{it.imageUrl
                    ? <img src={it.imageUrl} alt="" style={{ width: '100%', height: '100%', objectFit: 'cover', borderRadius: 14 }} />
                    : it.emoji}</div>
                  <div style={{ flex: 1 }}>
                    <div className="name">{it.name} <span className="muted" style={{ fontWeight: 400 }}>{it.nameUrdu}</span></div>
                    <div className="muted" style={{ fontSize: 13 }}>{it.description}</div>
                    <div style={{ marginTop: 2 }}>
                      {it.dealActive && it.dealPrice != null && !it.variants?.length ? (
                        <span>
                          <span style={{ textDecoration: 'line-through', color: 'var(--faint)' }}>{money(it.price)}</span>{' '}
                          <b style={{ color: 'var(--accent)' }}>{money(it.dealPrice)}</b>{' '}
                          <span className="pill s-Cancelled">{Math.round((1 - it.dealPrice / it.price) * 100)}% OFF</span>
                        </span>
                      ) : (
                        <>{it.variants?.length ? <span className="muted">from </span> : null}{money(from)}</>
                      )}
                      {needsCustomizing(it) && <span className="muted" style={{ fontSize: 12 }}> · customizable</span>}
                    </div>
                    {it.dealActive && it.dealDescription && <div className="muted" style={{ fontSize: 12, marginTop: 2 }}>{it.dealDescription}</div>}
                  </div>
                  {it.available
                    ? <button className="primary" onClick={() => onPlus(it)}>{needsCustomizing(it) ? 'Choose' : '+'}</button>
                    : <span className="pill s-Served">Out of stock</span>}
                </div>
              )
            })}
          </div>
        </div>
      ))}
    </>
  )
}

// The list of cart lines with quantity steppers (used inside the cart bar).
export function CartLines({ list, changeQty }) {
  return (
    <div style={{ maxHeight: 130, overflowY: 'auto', marginBottom: 8 }}>
      {list.map((x) => (
        <div key={x.sig} className="row between" style={{ padding: '3px 0' }}>
          <div style={{ fontSize: 14 }}>
            {x.name}{x.variantName ? ` (${x.variantName})` : ''}
            {x.modifierNames.length > 0 && <span className="muted" style={{ fontSize: 12 }}> · {x.modifierNames.join(', ')}</span>}
          </div>
          <div className="qty">
            <button onClick={() => changeQty(x.sig, -1)}>−</button>
            <b>{x.qty}</b>
            <button className="primary" onClick={() => changeQty(x.sig, +1)}>+</button>
          </div>
        </div>
      ))}
    </div>
  )
}

export const Spinner = ({ dark }) => <span className={dark ? 'spinner dark' : 'spinner'} aria-label="loading" />

// The single next-step control for an order, gated by the signed-in user's
// permissions. Reused by Reception and the Waiter board so the lifecycle rules
// live in ONE place — a waiter or cashier can advance an order (approve → cook →
// ready → serve) exactly as far as the owner has permitted them, which is what
// makes the "kitchen unavailable" fallback work without duplicating any logic.
export function OrderActions({ o, size = 'sm', onError }) {
  const [busy, setBusy] = useState(false)
  const can = (p) => auth.can(p)
  const style = size === 'lg' ? { flex: 1 } : { fontSize: 12.5, padding: '5px 11px' }
  const cls = size === 'lg' ? '' : 'ghost'

  const run = async (fn) => {
    setBusy(true)
    try { await fn() } catch (e) { onError ? onError(e) : ui.alert(e.message) }
    setBusy(false)
  }
  const cook = () => run(async () => {
    const m = await ui.prompt({ title: 'Start cooking', message: 'Minutes to prepare?', defaultValue: '15' })
    if (m !== null) await api.cook(o.id, Number(m) || 15)
  })

  let action = null
  if (o.status === 'Placed' && can('orders.approve')) action = { fn: () => run(() => api.approve(o.id)), label: <><CheckIcon size={15} /> Approve</>, cls: size === 'lg' ? 'green' : 'ghost' }
  else if (o.status === 'Approved' && can('orders.cook')) action = { fn: cook, label: <><KitchenIcon size={15} /> Start cooking</>, cls: size === 'lg' ? 'primary' : 'ghost' }
  else if (o.status === 'Cooking' && can('orders.cook')) action = { fn: () => run(() => api.ready(o.id)), label: <><BellIcon size={15} /> Mark ready</>, cls: size === 'lg' ? 'primary' : 'ghost' }
  else if (o.status === 'Ready' && can('orders.serve')) action = { fn: () => run(() => api.serve(o.id)), label: <><CheckIcon size={15} /> {o.channel === 'Takeaway' ? 'Hand over' : o.channel === 'Delivery' ? 'Send with rider' : 'Mark served'}</>, cls: size === 'lg' ? 'green' : 'ghost' }
  if (!action) return null

  return (
    <button className={action.cls} style={style} disabled={busy} onClick={action.fn}>
      {busy ? <Spinner dark={action.cls === 'ghost' || size !== 'lg'} /> : action.label}
    </button>
  )
}

// Item customization modal (variants + modifier groups with min/max rules).
export function Customizer({ item, onClose, onAdd }) {
  const [variantId, setVariantId] = useState(item.variants?.[0]?.id ?? null)
  const [selected, setSelected] = useState({})   // { groupId: number[] }

  const toggle = (group, optId) => setSelected((s) => {
    const cur = s[group.id] || []
    let next
    if (group.maxSelect === 1) next = [optId]
    else if (cur.includes(optId)) next = cur.filter((x) => x !== optId)
    else if (cur.length >= group.maxSelect) next = cur
    else next = [...cur, optId]
    return { ...s, [group.id]: next }
  })

  const base = item.variants?.length ? (item.variants.find((v) => v.id === variantId)?.price ?? 0)
    : (item.dealActive && item.dealPrice != null) ? item.dealPrice : item.price
  const allOpts = (item.modifierGroups || []).flatMap((g) => g.options)
  const chosenIds = Object.values(selected).flat()
  const chosen = allOpts.filter((o) => chosenIds.includes(o.id))
  const price = base + chosen.reduce((s, o) => s + o.price, 0)

  const valid = (!item.variants?.length || variantId != null) &&
    (item.modifierGroups || []).every((g) => (selected[g.id]?.length || 0) >= g.minSelect)

  const add = () => onAdd({
    sig: `${item.id}|${variantId ?? ''}|${[...chosenIds].sort((a, b) => a - b).join(',')}`,
    item, name: item.name,
    variantId, variantName: item.variants?.find((v) => v.id === variantId)?.name ?? null,
    modifierOptionIds: chosenIds, modifierNames: chosen.map((o) => o.name),
    unitPrice: price, qty: 1,
  })

  return (
    <div className="overlay" onClick={(e) => { if (e.target.className === 'overlay') onClose() }}>
      <div className="modal">
        <div className="row between"><div className="big">{item.name}</div><button className="ghost" onClick={onClose}><CloseIcon size={16} /></button></div>
        <div className="muted" style={{ fontSize: 13, marginBottom: 6 }}>{item.description}</div>

        {item.variants?.length > 0 && (
          <div style={{ margin: '10px 0' }}>
            <div className="cat-title" style={{ marginTop: 0 }}>Size</div>
            {item.variants.map((v) => (
              <label key={v.id} className="row between" style={{ padding: '7px 0', cursor: 'pointer' }}>
                <span><input type="radio" name="variant" checked={variantId === v.id} onChange={() => setVariantId(v.id)} /> {v.name}</span>
                <span className="muted">{money(v.price)}</span>
              </label>
            ))}
          </div>
        )}

        {(item.modifierGroups || []).map((g) => (
          <div key={g.id} style={{ margin: '10px 0' }}>
            <div className="cat-title" style={{ marginTop: 0 }}>
              {g.name} <span className="muted" style={{ fontSize: 12, fontWeight: 400 }}>
                {g.minSelect > 0 ? 'required' : 'optional'}{g.maxSelect > 1 ? ` · up to ${g.maxSelect}` : ''}
              </span>
            </div>
            {g.options.map((o) => {
              const on = (selected[g.id] || []).includes(o.id)
              return (
                <label key={o.id} className="row between" style={{ padding: '7px 0', cursor: 'pointer' }}>
                  <span>
                    <input type={g.maxSelect === 1 ? 'radio' : 'checkbox'} name={`g${g.id}`}
                      checked={on} onChange={() => toggle(g, o.id)} /> {o.name}
                  </span>
                  <span className="muted">{o.price > 0 ? '+ ' + money(o.price) : '—'}</span>
                </label>
              )
            })}
          </div>
        ))}

        <div className="row between" style={{ marginTop: 12 }}>
          <div className="big">{money(price)}</div>
          <button className="green" disabled={!valid} onClick={add}>Add to order</button>
        </div>
      </div>
    </div>
  )
}
