import { useEffect, useMemo, useRef, useState } from 'react'
import { api, money, STATUS_LABEL } from '../api'
import { useStaffOrders } from '../useStaffOrders'
import { auth } from '../auth'
import { ui } from '../ui.jsx'
import {
  useCart, plusOrCustomize, cartToItems, MenuList, CartLines, Customizer,
  OrderActions, Spinner, catAnchor,
} from '../ordering.jsx'

// Waiter screen. Two jobs:
//  • Board — get alerted when food is ready, take a table, advance/serve orders.
//  • Take order — browse the same live menu the customer sees and place an order
//    on a table's behalf (flows to reception → kitchen like any QR order).
// "You" is the signed-in staff member — assignments are tied to their real name.
export default function Waiter() {
  const orders = useStaffOrders()
  const me = auth.getStaff()?.displayName || 'Waiter'
  const canOrder = auth.can('orders.create')
  const [tab, setTab] = useState('board')   // 'board' | 'order'

  return (
    <div className="wrap ops ops-center">
      <div className="row between" style={{ marginBottom: 12, flexWrap: 'wrap', gap: 10 }}>
        <div className="row" style={{ gap: 12 }}>
          <div className="h" style={{ margin: 0 }}>Waiter</div>
          <span className="muted" style={{ fontSize: 13 }}>· {me}</span>
        </div>
        {canOrder && (
          <div className="seg">
            <button className={tab === 'board' ? 'on' : ''} onClick={() => setTab('board')}>📋 Board</button>
            <button className={tab === 'order' ? 'on' : ''} onClick={() => setTab('order')}>➕ Take order</button>
          </div>
        )}
      </div>

      {tab === 'order' && canOrder ? <TakeOrder orders={orders} /> : <Board orders={orders} me={me} />}
    </div>
  )
}

// ---- The live board: ready-to-serve + the tables you're handling ----
function Board({ orders, me }) {
  const ready = orders.filter((o) => o.status === 'Ready')
  const mine = orders.filter((o) => o.waiterName === me && o.status !== 'Ready')

  return (
    <>
      <div className="cat-title">✅ Ready to serve now ({ready.length})</div>
      <div className="grid cols stagger">
        {ready.map((o) => (
          <div className="card lift fade-up" key={o.id}>
            <div className="row between"><b>{o.tableName} · #{o.id}</b>
              <span className={'pill s-' + o.status}>{STATUS_LABEL[o.status]}</span></div>
            <Lines o={o} />
            <div className="row" style={{ marginTop: 8, gap: 8 }}>
              {!o.waiterName && <button onClick={() => api.assign(o.id, me).catch((e) => ui.alert(e.message))}>Take table</button>}
              <button className="green" style={{ marginLeft: 'auto' }}
                onClick={() => api.serve(o.id).catch((e) => ui.alert(e.message))}>🍽️ Mark served</button>
            </div>
          </div>
        ))}
        {ready.length === 0 && <div className="empty">No food ready yet. You'll be alerted here the moment the kitchen finishes.</div>}
      </div>

      <div className="cat-title" style={{ marginTop: 22 }}>My tables ({mine.length})</div>
      <div className="grid cols stagger">
        {mine.map((o) => (
          <div className="card lift fade-up" key={o.id}>
            <div className="row between">
              <b>{o.tableName} · #{o.id}</b>
              <span className={'pill s-' + o.status}>{STATUS_LABEL[o.status]}</span>
            </div>
            <Lines o={o} />
            <div className="row between" style={{ marginTop: 8 }}>
              <div className="row" style={{ gap: 8 }}>
                <span className="muted">{money(o.total)}</span>
                <CookTimer o={o} />
              </div>
              {/* When the owner permits it, the waiter can advance the order here
                  (kitchen-unavailable fallback) — same central endpoints, one button. */}
              <OrderActions o={o} />
            </div>
          </div>
        ))}
        {mine.length === 0 && <div className="empty">Orders you take (or tables you pick up) show here until they're served.</div>}
      </div>
    </>
  )
}

// Live cooking countdown for a card (anchored to the order's cook-start time).
function CookTimer({ o }) {
  const [now, setNow] = useState(Date.now())
  useEffect(() => {
    if (o.status !== 'Cooking') return
    const t = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(t)
  }, [o.status])
  if (o.status !== 'Cooking' || !o.acceptedAt) return null
  const left = Math.max(0, Math.round((new Date(o.acceptedAt).getTime() + o.etaMinutes * 60000 - now) / 1000))
  const label = left > 0 ? `${String(Math.floor(left / 60)).padStart(2, '0')}:${String(left % 60).padStart(2, '0')}` : 'Due'
  return <span className="countdown" style={{ fontSize: 17 }} title="Time left to cook">⏱ {label}</span>
}

// ---- Take an order on a table's behalf ----
function TakeOrder({ orders }) {
  const [tables, setTables] = useState(null)
  const [table, setTable] = useState(null)   // { code, name }
  const [data, setData] = useState(null)      // { restaurant, menu }
  const cart = useCart()
  const [note, setNote] = useState('')
  const [customizing, setCustomizing] = useState(null)
  const [placing, setPlacing] = useState(false)
  const [placed, setPlaced] = useState(null)  // { id, tableName }
  const [query, setQuery] = useState('')
  const [activeCat, setActiveCat] = useState(null)
  const [bump, setBump] = useState(false)

  useEffect(() => { api.getTables().then(setTables).catch(() => setTables([])) }, [])
  useEffect(() => { api.getMenu(auth.staffSlug()).then(setData).catch(() => setData({ menu: [] })) }, [])

  // Pop the cart badge whenever the item count changes.
  useEffect(() => {
    if (cart.count === 0) return
    setBump(true); const t = setTimeout(() => setBump(false), 320); return () => clearTimeout(t)
  }, [cart.count])

  const onPlus = (it) => { setPlaced(null); plusOrCustomize(it, cart.addToCart, setCustomizing) }

  // Tables with at least one active (unpaid) order — shown as "busy" in the picker.
  const busyTables = useMemo(() => new Set(orders.map((o) => o.tableCode)), [orders])

  // Filter the menu by the search box; drop categories that end up empty.
  const q = query.trim().toLowerCase()
  const menu = useMemo(() => {
    const cats = data?.menu || []
    if (!q) return cats
    return cats
      .map((c) => ({ ...c, items: c.items.filter((it) =>
        it.name.toLowerCase().includes(q) || (it.nameUrdu || '').includes(query) || (it.description || '').toLowerCase().includes(q)) }))
      .filter((c) => c.items.length > 0)
  }, [data, q, query])

  const goCat = (id) => {
    setActiveCat(id)
    document.getElementById(catAnchor(id))?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }

  const submit = async () => {
    if (!table || cart.list.length === 0) return
    setPlacing(true)
    try {
      const o = await api.placeStaffOrder({ tableCode: table.code, items: cartToItems(cart.list), note: note || null })
      cart.clear(); setNote(''); setPlaced({ id: o.id, tableName: table.name })
      window.scrollTo({ top: 0, behavior: 'smooth' })
    } catch (e) { ui.alert('Could not place order: ' + e.message) }
    setPlacing(false)
  }

  // Step 1 — pick a table.
  if (!table) {
    if (tables === null) return <div className="loading"><Spinner dark /><span>Loading tables…</span></div>
    return (
      <div className="fade-in">
        {placed && (
          <div className="ok-banner">
            <span className="ok-check">✓</span>
            <div>Order <b>#{placed.id}</b> for <b>{placed.tableName}</b> sent to reception.</div>
          </div>
        )}
        <div className="cat-title" style={{ marginTop: 0 }}>Choose a table</div>
        <div className="tablegrid stagger">
          {tables.map((t) => (
            <div key={t.code} className="card tcard lift" onClick={() => { setPlaced(null); setTable(t) }}>
              {busyTables.has(t.code) && <span className="tbusy"><span className="dot" />busy</span>}
              <div className="tico">🍽️</div>
              <div className="tname">{t.name}</div>
              <div className="tsub">{t.code}{t.branch ? ` · ${t.branch}` : ''}</div>
            </div>
          ))}
          {tables.length === 0 && <div className="empty">No tables set up yet. Add tables in Settings.</div>}
        </div>
      </div>
    )
  }

  // Step 2 — browse the menu and build the order for the chosen table.
  if (!data) return <div className="loading"><Spinner dark /><span>Loading menu…</span></div>

  return (
    <div className="fade-in" style={{ paddingBottom: cart.list.length ? 200 : 24, textAlign: 'left' }}>
      <div className="card" style={{ marginBottom: 12 }}>
        <div className="row between">
          <div>
            <div className="muted" style={{ fontSize: 13 }}>Ordering for</div>
            <div className="big">{table.name} <span className="muted" style={{ fontSize: 14 }}>· {table.code}</span></div>
          </div>
          <button onClick={() => { setTable(null); cart.clear(); setNote(''); setQuery('') }}>↩ Change table</button>
        </div>
      </div>

      {placed && placed.tableName === table.name && (
        <div className="ok-banner">
          <span className="ok-check">✓</span>
          <div>Order <b>#{placed.id}</b> sent to reception. Add more below, or change table.</div>
        </div>
      )}

      <div className="search" style={{ marginBottom: 4 }}>
        <span className="s-ico">🔍</span>
        <input placeholder="Search the menu…" value={query} onChange={(e) => setQuery(e.target.value)} />
      </div>

      {!q && menu.length > 1 && (
        <div className="catnav">
          {menu.map((c) => (
            <div key={c.id} className={'chip-nav' + (activeCat === c.id ? ' active' : '')} onClick={() => goCat(c.id)}>{c.name}</div>
          ))}
        </div>
      )}

      {menu.length === 0
        ? <div className="empty">No items match “{query}”.</div>
        : <MenuList menu={menu} onPlus={onPlus} />}

      {cart.list.length > 0 && (
        <div className="sheet">
          <div className="row between" style={{ marginBottom: 8 }}>
            <div className="row" style={{ gap: 8 }}>
              <b>Order</b>
              <span className={'cart-badge' + (bump ? ' bump' : '')} style={{ background: 'var(--accent)', color: '#fff' }}>{cart.count}</span>
              <span className="muted" style={{ fontSize: 13 }}>· {table.name}</span>
            </div>
            <div className="big" style={{ fontSize: 22 }}>{money(cart.total)}</div>
          </div>
          <div className="sheet-lines"><CartLines list={cart.list} changeQty={cart.changeQty} /></div>
          <input placeholder="Note for kitchen (e.g. no onion)" value={note}
            onChange={(e) => setNote(e.target.value)} style={{ width: '100%', marginBottom: 8 }} />
          <button className="green" disabled={placing} onClick={submit}
            style={{ width: '100%', fontSize: 16, padding: '12px' }}>
            {placing ? <><Spinner /> Sending…</> : 'Send to reception →'}
          </button>
        </div>
      )}

      {customizing && (
        <Customizer item={customizing} onClose={() => setCustomizing(null)}
          onAdd={(line) => { cart.addToCart(line); setCustomizing(null) }} />
      )}
    </div>
  )
}

function Lines({ o }) {
  return (
    <div style={{ margin: '8px 0' }}>
      {o.items.map((i, n) => <div key={n}><b>{i.qty}×</b> {i.name}{i.variantName ? ` (${i.variantName})` : ''}</div>)}
    </div>
  )
}
