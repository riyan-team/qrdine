import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { api, money, STATUS_LABEL } from '../api'
import { useHub } from '../useHub'
import { ui } from '../ui.jsx'

export default function Menu() {
  const { slug, code } = useParams()
  const [data, setData] = useState(null)
  const [cart, setCart] = useState({})       // { signature: line }
  const [note, setNote] = useState('')
  const [orders, setOrders] = useState([])
  const [placing, setPlacing] = useState(false)
  const [customizing, setCustomizing] = useState(null)  // item being customized

  useEffect(() => {
    api.getMenu(slug).then(setData).catch(() => {})
    api.tableOrders(slug, code).then(setOrders).catch(() => {})
  }, [slug, code])

  useHub(slug, code, (o) => {
    setOrders((prev) => {
      const others = prev.filter((x) => x.id !== o.id)
      if (o.status === 'Paid' || o.status === 'Cancelled') return others
      return [...others, o].sort((a, b) => a.id - b.id)
    })
  })

  const needsCustomizing = (it) => (it.variants?.length || 0) > 0 || (it.modifierGroups?.length || 0) > 0

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

  const onPlus = (it) => {
    if (needsCustomizing(it)) setCustomizing(it)
    else addToCart({ sig: `${it.id}`, item: it, name: it.name, variantId: null, variantName: null,
      modifierOptionIds: [], modifierNames: [], unitPrice: it.price, qty: 1 })
  }

  const cartList = Object.values(cart)
  const cartTotal = cartList.reduce((s, x) => s + x.unitPrice * x.qty, 0)
  const cartCount = cartList.reduce((s, x) => s + x.qty, 0)

  const place = async () => {
    setPlacing(true)
    try {
      await api.placeOrder(slug, {
        tableCode: code,
        items: cartList.map((x) => ({
          menuItemId: x.item.id, qty: x.qty, note: null,
          variantId: x.variantId, modifierOptionIds: x.modifierOptionIds,
        })),
        note: note || null,
      })
      setCart({}); setNote('')
    } catch (e) { ui.alert('Could not place order: ' + e.message) }
    setPlacing(false)
  }

  if (!data) return <div className="empty">Loading menu…</div>

  return (
    <div className="wrap" style={{ paddingBottom: 130 }}>
      <div className="card" style={{ marginBottom: 12 }}>
        <div className="row between">
          <div>
            <div className="big">{data.restaurant.name}</div>
            <div className="muted">You are at <b>Table {code.replace('T', '')}</b> · scan-to-order</div>
          </div>
          <div style={{ fontSize: 34 }}>🍽️</div>
        </div>
      </div>

      {orders.length > 0 && <LiveTracker orders={orders} />}

      {data.menu.map((cat) => (
        <div key={cat.id}>
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
                      {it.variants?.length ? <span className="muted">from </span> : null}{money(from)}
                      {needsCustomizing(it) && <span className="muted" style={{ fontSize: 12 }}> · customizable</span>}
                    </div>
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

      {cartList.length > 0 && (
        <div className="cart">
          <div className="wrap" style={{ padding: 0 }}>
            <div style={{ maxHeight: 130, overflowY: 'auto', marginBottom: 8 }}>
              {cartList.map((x) => (
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
            <input placeholder="Note for kitchen (e.g. no onion)" value={note}
              onChange={(e) => setNote(e.target.value)} style={{ width: '100%', marginBottom: 8 }} />
            <div className="row between">
              <div><div className="muted">{cartCount} items</div><div className="big">{money(cartTotal)}</div></div>
              <button className="green" disabled={placing} onClick={place} style={{ fontSize: 17 }}>
                {placing ? 'Sending…' : 'Place order →'}
              </button>
            </div>
          </div>
        </div>
      )}

      {customizing && (
        <Customizer item={customizing} onClose={() => setCustomizing(null)}
          onAdd={(line) => { addToCart(line); setCustomizing(null) }} />
      )}
    </div>
  )
}

// ---- Item customization modal ----
function Customizer({ item, onClose, onAdd }) {
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

  const base = item.variants?.length ? (item.variants.find((v) => v.id === variantId)?.price ?? 0) : item.price
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
        <div className="row between"><div className="big">{item.name}</div><button className="ghost" onClick={onClose}>✕</button></div>
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

function LiveTracker({ orders }) {
  return (
    <div className="card" style={{ marginBottom: 12 }}>
      <div className="row between"><b>Your orders</b><span className="muted">updates live</span></div>
      {orders.map((o) => <TrackerRow key={o.id} o={o} />)}
    </div>
  )
}

function TrackerRow({ o }) {
  const [now, setNow] = useState(Date.now())
  useEffect(() => { const t = setInterval(() => setNow(Date.now()), 1000); return () => clearInterval(t) }, [])

  let countdown = null
  if (o.status === 'Cooking' && o.acceptedAt) {
    const done = new Date(o.acceptedAt).getTime() + o.etaMinutes * 60000
    const left = Math.max(0, Math.round((done - now) / 1000))
    countdown = `${String(Math.floor(left / 60)).padStart(2, '0')}:${String(left % 60).padStart(2, '0')}`
  }

  return (
    <div className="tracker">
      <div className="row between">
        <b>Order #{o.id}</b>
        <span className={'pill s-' + o.status}>{STATUS_LABEL[o.status] || o.status}</span>
      </div>
      <div className="muted" style={{ fontSize: 13 }}>
        {o.items.map((i) => `${i.qty}× ${i.name}${i.variantName ? ` (${i.variantName})` : ''}`).join(', ')}
      </div>
      {o.status === 'Cooking' && (
        <div className="row between" style={{ marginTop: 4 }}>
          <span className="muted">Ready in about</span><span className="countdown">{countdown}</span>
        </div>
      )}
      {o.status === 'Ready' && <div style={{ color: 'var(--accent2)', fontWeight: 700 }}>✅ Ready — waiter is bringing it</div>}
      {o.status === 'Served' && <div className="muted">Served. Enjoy! 🍽️</div>}
      {o.waiterName && <div className="muted" style={{ fontSize: 12 }}>Waiter: {o.waiterName}</div>}
    </div>
  )
}
