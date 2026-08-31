import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { api, money, STATUS_LABEL } from '../api'
import { useHub } from '../useHub'
import { ui } from '../ui.jsx'
import { useCart, plusOrCustomize, cartToItems, MenuList, CartLines, Customizer } from '../ordering.jsx'
import { KitchenIcon, StarIcon, CheckIcon } from '../icons.jsx'

export default function Menu() {
  const { slug, code } = useParams()
  const [data, setData] = useState(null)
  const cart = useCart()
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

  const onPlus = (it) => plusOrCustomize(it, cart.addToCart, setCustomizing)

  const place = async () => {
    setPlacing(true)
    try {
      await api.placeOrder(slug, { tableCode: code, items: cartToItems(cart.list), note: note || null })
      cart.clear(); setNote('')
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
          <div style={{ fontSize: 34, color: 'var(--accent)' }}><KitchenIcon size={34} /></div>
        </div>
      </div>

      {orders.length > 0 && <LiveTracker orders={orders} />}

      <MenuList menu={data.menu} onPlus={onPlus} />

      <FeedbackCard slug={slug} code={code} />

      {cart.list.length > 0 && (
        <div className="cart">
          <div className="wrap" style={{ padding: 0 }}>
            <CartLines list={cart.list} changeQty={cart.changeQty} />
            <input placeholder="Note for kitchen (e.g. no onion)" value={note}
              onChange={(e) => setNote(e.target.value)} style={{ width: '100%', marginBottom: 8 }} />
            <div className="row between">
              <div><div className="muted">{cart.count} items</div><div className="big">{money(cart.total)}</div></div>
              <button className="green" disabled={placing} onClick={place} style={{ fontSize: 17 }}>
                {placing ? 'Sending…' : 'Place order →'}
              </button>
            </div>
          </div>
        </div>
      )}

      {customizing && (
        <Customizer item={customizing} onClose={() => setCustomizing(null)}
          onAdd={(line) => { cart.addToCart(line); setCustomizing(null) }} />
      )}
    </div>
  )
}

// ---- Rate your experience ----
function FeedbackCard({ slug, code }) {
  const [rating, setRating] = useState(0)
  const [hover, setHover] = useState(0)
  const [comment, setComment] = useState('')
  const [done, setDone] = useState(false)
  const [busy, setBusy] = useState(false)

  const submit = async () => {
    if (!rating) return
    setBusy(true)
    try { await api.submitFeedback(slug, { rating, comment: comment.trim(), tableCode: code }); setDone(true) }
    catch { /* ignore — feedback is best-effort */ }
    setBusy(false)
  }

  if (done) return (
    <div className="card" style={{ marginTop: 12, textAlign: 'center' }}>
      <div style={{ fontSize: 28, color: 'var(--accent2)' }}><CheckIcon size={28} /></div>
      <b>Thanks for your feedback!</b>
      <div className="muted" style={{ fontSize: 13 }}>We hope to see you again.</div>
    </div>
  )

  return (
    <div className="card" style={{ marginTop: 12 }}>
      <b>Rate your experience</b>
      <div className="row" style={{ gap: 4, margin: '8px 0', fontSize: 30, cursor: 'pointer', justifyContent: 'center' }}
        onMouseLeave={() => setHover(0)}>
        {[1, 2, 3, 4, 5].map((n) => (
          <span key={n} onClick={() => setRating(n)} onMouseEnter={() => setHover(n)}
            style={{ display: 'inline-flex', color: 'var(--amber, #e0a500)', filter: (hover || rating) >= n ? 'none' : 'grayscale(1) opacity(0.4)' }}><StarIcon size={30} /></span>
        ))}
      </div>
      {rating > 0 && (
        <>
          <textarea placeholder="Tell us more (optional)" value={comment} maxLength={500}
            onChange={(e) => setComment(e.target.value)} style={{ width: '100%', minHeight: 60, marginBottom: 8 }} />
          <button className="primary" disabled={busy} onClick={submit} style={{ width: '100%' }}>
            {busy ? 'Sending…' : 'Submit feedback'}
          </button>
        </>
      )}
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
      {o.status === 'Ready' && <div style={{ color: 'var(--accent2)', fontWeight: 700 }}><CheckIcon size={15} /> Ready — waiter is bringing it</div>}
      {o.status === 'Served' && <div className="muted">Served. Enjoy!</div>}
      {o.waiterName && <div className="muted" style={{ fontSize: 12 }}>Waiter: {o.waiterName}</div>}
    </div>
  )
}
