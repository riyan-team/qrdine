import { useEffect, useMemo, useRef, useState } from 'react'
import { api, money, STATUS_LABEL } from '../api'
import { useStaffOrders } from '../useStaffOrders'
import { auth } from '../auth'
import { ui, toast } from '../ui.jsx'

// A short chime for "food ready" alerts — synthesised, so there's no audio asset to
// ship. Browsers need a prior user interaction before audio plays; staff have that.
let _audioCtx
function chime() {
  try {
    _audioCtx = _audioCtx || new (window.AudioContext || window.webkitAudioContext)()
    const ctx = _audioCtx
    ;[880, 1175].forEach((freq, i) => {
      const osc = ctx.createOscillator(), gain = ctx.createGain()
      const t = ctx.currentTime + i * 0.18
      osc.type = 'sine'; osc.frequency.value = freq
      gain.gain.setValueAtTime(0.0001, t)
      gain.gain.exponentialRampToValueAtTime(0.22, t + 0.02)
      gain.gain.exponentialRampToValueAtTime(0.0001, t + 0.3)
      osc.connect(gain); gain.connect(ctx.destination)
      osc.start(t); osc.stop(t + 0.32)
    })
  } catch { /* audio blocked — the toast still shows */ }
}
import {
  useCart, plusOrCustomize, cartToItems, MenuList, CartLines, Customizer,
  OrderActions, Spinner, catAnchor,
} from '../ordering.jsx'
import { NoteIcon, PlusIcon, CheckIcon, TableIcon, SearchIcon } from '../icons.jsx'

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
  const [preTable, setPreTable] = useState(null)   // table to pre-select in Take order

  // Live alerts: chime + toast when food becomes Ready, and a toast when a table is
  // newly assigned to me. Diffs each live update against the previous snapshot.
  const prevSnap = useRef(null)
  useEffect(() => {
    const snap = new Map(orders.map((o) => [o.id, { status: o.status, waiter: o.waiterName }]))
    const before = prevSnap.current
    if (before) {
      for (const o of orders) {
        const b = before.get(o.id)
        if (!b) continue   // first time we see it — don't alert on initial load
        if (o.status === 'Ready' && b.status !== 'Ready') { chime(); toast.success(`Ready to serve — ${o.tableName} · #${o.id}`) }
        if (o.waiterName === me && b.waiter !== me) toast.info(`Assigned to you — ${o.tableName} · #${o.id}`)
      }
    }
    prevSnap.current = snap
  }, [orders, me])

  // Jump straight into Take order for a table that already has a running tab —
  // the extra items attach to the same open session and bill together.
  const addItemsTo = (o) => { setPreTable({ code: o.tableCode, name: o.tableName }); setTab('order') }

  return (
    <div className="wrap ops ops-center">
      <div className="row between" style={{ marginBottom: 12, flexWrap: 'wrap', gap: 10 }}>
        <div className="row" style={{ gap: 12 }}>
          <div className="h" style={{ margin: 0 }}>Waiter</div>
          <span className="muted" style={{ fontSize: 13 }}>· {me}</span>
        </div>
        {canOrder && (
          <div className="seg">
            <button className={tab === 'board' ? 'on' : ''} onClick={() => { setTab('board'); setPreTable(null) }}><NoteIcon size={15} /> Board</button>
            <button className={tab === 'order' ? 'on' : ''} onClick={() => setTab('order')}><PlusIcon size={15} /> Take order</button>
          </div>
        )}
      </div>

      {tab === 'order' && canOrder
        ? <TakeOrder orders={orders} initialTable={preTable} />
        : <Board orders={orders} me={me} canOrder={canOrder} onAddItems={addItemsTo} />}
    </div>
  )
}

// ---- The live board: ready-to-serve + the tables you're handling ----
function Board({ orders, me, canOrder, onAddItems }) {
  const ready = orders.filter((o) => o.status === 'Ready')
  const mine = orders.filter((o) => o.waiterName === me && o.status !== 'Ready')

  return (
    <>
      <div className="cat-title"><CheckIcon size={15} /> Ready to serve now ({ready.length})</div>
      <div className="grid cols stagger">
        {ready.map((o) => (
          <div className="card lift fade-up" key={o.id}>
            <div className="row between"><b>{o.tableName} · #{o.id}</b>
              <span className={'pill s-' + o.status}>{STATUS_LABEL[o.status]}</span></div>
            <Lines o={o} />
            <div className="row" style={{ marginTop: 8, gap: 8 }}>
              {!o.waiterName && <button onClick={() => api.assign(o.id, me).catch((e) => ui.alert(e.message))}>Take table</button>}
              <button className="green" style={{ marginLeft: 'auto' }}
                onClick={() => api.serve(o.id).catch((e) => ui.alert(e.message))}><CheckIcon size={15} /> Mark served</button>
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
            <div className="row between" style={{ marginTop: 8, flexWrap: 'wrap', gap: 8 }}>
              <div className="row" style={{ gap: 8 }}>
                <span className="muted">{money(o.total)}</span>
                <CookTimer o={o} />
              </div>
              <div className="row" style={{ gap: 6 }}>
                {/* Customer wants more after ordering — add items to this same tab. */}
                {canOrder && <button className="ghost" title="Add more items to this table" onClick={() => onAddItems(o)}><PlusIcon size={15} /> Add items</button>}
                {/* When the owner permits it, the waiter can advance the order here
                    (kitchen-unavailable fallback) — same central endpoints, one button. */}
                <OrderActions o={o} />
              </div>
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
  return <span className="countdown" style={{ fontSize: 17 }} title="Time left to cook">{label}</span>
}

// ---- Take an order on a table's behalf ----
function TakeOrder({ orders, initialTable }) {
  const [tables, setTables] = useState(null)
  const [table, setTable] = useState(initialTable || null)   // { code, name }
  // If the screen is asked to pre-select a table (e.g. "Add items" from the board).
  useEffect(() => { if (initialTable) setTable(initialTable) }, [initialTable])
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
              <div className="tico"><TableIcon size={26} /></div>
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
            <div className="big">{table.name} <span className="muted" style={{ fontSize: 14 }}>· {table.code}</span>
              {busyTables.has(table.code) && <span className="pill s-Approved" style={{ marginLeft: 8, fontSize: 11 }}>running tab — adds to it</span>}
            </div>
          </div>
          <button onClick={() => { setTable(null); cart.clear(); setNote(''); setQuery('') }}>Change table</button>
        </div>
      </div>

      {placed && placed.tableName === table.name && (
        <div className="ok-banner">
          <span className="ok-check">✓</span>
          <div>Order <b>#{placed.id}</b> sent to reception. Add more below, or change table.</div>
        </div>
      )}

      <div className="search" style={{ marginBottom: 4 }}>
        <span className="s-ico"><SearchIcon size={16} /></span>
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
