import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, money, statusLabel } from '../api'
import { auth } from '../auth'
import { NOT_LOADED, loadUntilOk, useStaffOrders, useCalls } from '../useStaffOrders'
import { OrderActions, useCart, plusOrCustomize, cartToItems, Customizer } from '../ordering.jsx'
import { toast, playBell, ui } from '../ui.jsx'
import { friendlyError } from '../errors'
import { Modal, Tabs, waLink } from '../backoffice.jsx'
import Receipt from './Receipt.jsx'
import { Requests as LoyaltyRequests, BillMember } from './Loyalty.jsx'
import {
  BagIcon, BellIcon, CashIcon, ChatIcon, CheckIcon, CloseIcon, KitchenIcon, PlusIcon, PrintIcon, QrIcon, ReceiptIcon,
  SearchIcon, SwapIcon, TableIcon, TruckIcon, UserIcon,
} from '../icons.jsx'

// Run a status action, surfacing any failure as a toast (never silently).
const run = (p) => Promise.resolve(p).catch((e) => toast.error(friendlyError(e)))

const minutesSince = (iso) => Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 60000))
const ago = (iso) => {
  const m = minutesSince(iso)
  if (m < 1) return 'just now'
  if (m < 60) return `${m} min`
  return `${Math.floor(m / 60)} hr ${m % 60} min`
}

// The one word a table's tile needs to say, taken from the furthest-behind order on it.
const tableState = (list) => {
  if (list.some((o) => o.status === 'Placed')) return { key: 'new', label: 'New order' }
  if (list.some((o) => o.status === 'Ready')) return { key: 'ready', label: 'Food ready' }
  if (list.some((o) => o.status === 'Cooking' || o.status === 'Approved')) return { key: 'cooking', label: 'In kitchen' }
  return { key: 'served', label: 'Eating' }
}

// Reception: what's happening on the floor right now, and the till.
export default function Cashier() {
  const { calls, onCall, done } = useCalls()
  const orders = useStaffOrders(onCall)
  const [tables, setTables] = useState([])
  const [tablesLoaded, setTablesLoaded] = useState(false)
  const [tickets, setTickets] = useState([])
  const [sheetFor, setSheetFor] = useState(null)     // table code whose sheet is open
  const [billFor, setBillFor] = useState(null)       // table code being billed
  const [taking, setTaking] = useState(null)         // { channel, table } for a new order
  const [slipFor, setSlipFor] = useState(null)       // { order, mode, autoPrint }
  const [tab, setTab] = useState('live')
  const [sum, setSum] = useState(null)
  const prevSnap = useRef(null)
  const canTake = auth.can('orders.create')

  useEffect(() => loadUntilOk(() => api.getTables(), (t) => { setTables(t); setTablesLoaded(true) }), [])

  // Chime when something needs a person.
  useEffect(() => {
    if (orders === NOT_LOADED) return
    const snap = new Map(orders.map((o) => [o.id, o.status]))
    const before = prevSnap.current
    if (before) {
      for (const o of orders) {
        const was = before.get(o.id)
        if (!was && o.status === 'Placed' && o.source !== 'Counter' && o.waiterName !== auth.getStaff()?.displayName) {
          playBell()
          toast.info(`New order for ${o.tableName}`)
        } else if (was && o.status === 'Ready' && was !== 'Ready') {
          playBell()
          toast.success(`${o.tableName}: food is ready`)
        }
      }
    }
    prevSnap.current = snap
  }, [orders])

  // Parcel and delivery details live on the ticket, so re-read them whenever the list of running bills changes.
  const openCodes = useMemo(() => [...new Set(orders.map((o) => o.tableCode))].sort().join(), [orders])
  const loadTickets = () => api.tickets().then(setTickets).catch(() => {})
  useEffect(() => loadUntilOk(() => api.tickets(), setTickets), [openCodes])
  useEffect(() => loadUntilOk(() => api.receptionDashboard('', 1, 1), setSum), [orders.length, billFor])

  const incoming = orders.filter((o) => o.status === 'Placed')
  const byTable = useMemo(() => {
    const m = new Map()
    for (const o of orders) {
      if (!m.has(o.tableCode)) m.set(o.tableCode, { code: o.tableCode, name: o.tableName, channel: o.channel || 'Dine', orders: [] })
      m.get(o.tableCode).orders.push(o)
    }
    for (const g of m.values()) {
      g.total = g.orders.reduce((s, o) => s + o.total, 0)
      g.since = g.orders.reduce((a, o) => (a < o.createdAt ? a : o.createdAt), g.orders[0].createdAt)
      g.state = tableState(g.orders)
    }
    return m
  }, [orders])

  const running = [...byTable.values()]
  const dineCount = running.filter((g) => g.channel === 'Dine').length
  const parcels = running.filter((g) => g.channel === 'Takeaway')
  const deliveries = running.filter((g) => g.channel === 'Delivery')
  const ticketFor = (code) => tickets.find((t) => t.tableCode === code)
  const sheetGroup = sheetFor ? byTable.get(sheetFor) : null

  // If the last order on an open sheet was settled or cancelled elsewhere, the sheet closes itself.
  useEffect(() => { if (sheetFor && !sheetGroup) setSheetFor(null) }, [sheetFor, sheetGroup])

  return (
    <div className="bo-wrap">
      <div className="fl-head">
        <div>
          <h1 className="h">Reception</h1>
        </div>
        <div className="fl-actions noprint">
          <Tabs value={tab} onChange={setTab} options={[{ value: 'live', label: 'Live floor' }, { value: 'history', label: 'Settled bills' }]} />
          {canTake && <button className="primary" onClick={() => setTaking({ channel: 'Dine' })}><PlusIcon size={16} /> New order</button>}
        </div>
      </div>

      <div className="bo-figs">
        <div className="bo-fig is-lead"><div className="bo-fig-label">Today's sales</div><div className="bo-fig-value">{sum ? money(sum.todaySales) : '–'}</div></div>
        <div className="bo-fig"><div className="bo-fig-label">Bills settled today</div><div className="bo-fig-value">{sum ? sum.todayBills : '–'}</div></div>
        <div className="bo-fig"><div className="bo-fig-label">Tables running</div><div className="bo-fig-value">{orders === NOT_LOADED ? '–' : dineCount}{tablesLoaded && <span className="bo-small"> of {tables.length}</span>}</div></div>
        <div className="bo-fig"><div className="bo-fig-label">Parcels and deliveries</div><div className="bo-fig-value">{orders === NOT_LOADED ? '–' : parcels.length + deliveries.length}</div></div>
      </div>

      {tab === 'history' && <ReceptionHistory />}
      {tab === 'live' && <>
        {calls.length > 0 && (
          <div className="fl-calls" role="status">
            {calls.map((c) => (
              <div key={c.id} className={`fl-call${c.kind === 'Bill' ? ' is-bill' : ''}`}>
                <BellIcon size={15} /> {c.tableName}: {c.kind === 'Bill' ? 'wants the bill' : 'calling a waiter'}
                {c.kind === 'Bill' && byTable.has(c.tableCode) && auth.can('orders.pay') && (
                  <button onClick={() => { done(c.id); setBillFor(c.tableCode) }}>Open bill</button>
                )}
                <button onClick={() => done(c.id)}>Done</button>
              </div>
            ))}
          </div>
        )}

        <LoyaltyRequests compact />

        {incoming.length > 0 && <>
          <div className="fl-section"><h2>New orders</h2><span>Approve to send them to the kitchen</span></div>
          <div className="fl-new">
            {incoming.map((o) => (
              <div className="fl-newcard" key={o.id}>
                <div className="fl-newcard-top"><b>{o.tableName}</b><SourceBadge source={o.source} /></div>
                <Lines o={o} />
                <div className="fl-newcard-actions">
                  <button onClick={() => setSheetFor(o.tableCode)}>View</button>
                  {auth.can('orders.approve') && <button className="green" onClick={() => run(api.approve(o.id))}><CheckIcon size={15} /> Approve · {money(o.total)}</button>}
                </div>
              </div>
            ))}
          </div>
        </>}

        <div className="fl-section"><h2>Tables</h2>{tablesLoaded && <span>{dineCount} running, {Math.max(0, tables.length - dineCount)} free</span>}</div>
        <div className="fl-grid">
          {tables.map((t, i) => {
            const g = byTable.get(t.code)
            return g ? (
              <button key={t.code} style={{ animationDelay: `${Math.min(i, 16) * 22}ms` }} className={`fl-tile is-${g.state.key}`} onClick={() => setSheetFor(t.code)}>
                <b>{t.name}</b>
                <small>{g.state.label} · {ago(g.since)}</small>
                <span className="fl-amt">{money(g.total)}</span>
              </button>
            ) : (
              <button key={t.code} style={{ animationDelay: `${Math.min(i, 16) * 22}ms` }} className="fl-tile is-free" disabled={!canTake}
                onClick={() => setTaking({ channel: 'Dine', table: { code: t.code, name: t.name } })}>
                <b>{t.name}</b>
                <small>Free</small>
              </button>
            )
          })}
          {tablesLoaded && tables.length === 0 && <div className="bo-small">No tables yet. Add them in Settings.</div>}
        </div>

        <TicketRow title="Parcels" empty="No parcels waiting." list={parcels} loaded={orders !== NOT_LOADED} Icon={BagIcon} ticketFor={ticketFor} onOpen={setSheetFor}
          onNew={canTake ? () => setTaking({ channel: 'Takeaway' }) : null} />
        <TicketRow title="Deliveries" empty="No deliveries running." list={deliveries} loaded={orders !== NOT_LOADED} Icon={TruckIcon} ticketFor={ticketFor} onOpen={setSheetFor}
          onNew={canTake ? () => setTaking({ channel: 'Delivery' }) : null} />
      </>}

      {sheetGroup && (
        <TableSheet group={sheetGroup} ticket={ticketFor(sheetGroup.code)} tables={tables} byTable={byTable}
          onClose={() => setSheetFor(null)}
          onBill={() => { setSheetFor(null); setBillFor(sheetGroup.code) }}
          onAdd={() => { setSheetFor(null); setTaking({ channel: sheetGroup.channel, table: { code: sheetGroup.code, name: sheetGroup.name }, adding: true }) }}
          onSlip={(order, mode, autoPrint) => setSlipFor({ order, mode, autoPrint })}
          onTicketChanged={loadTickets} />
      )}
      {taking && <NewOrder init={taking} tables={tables} byTable={byTable} onClose={() => setTaking(null)} />}
      {billFor && <BillModal code={billFor} onClose={() => setBillFor(null)} />}
      {slipFor && <OrderDetail order={slipFor.order} init={slipFor} onClose={() => setSlipFor(null)} />}
    </div>
  )
}

function TicketRow({ title, empty, list, loaded, Icon, ticketFor, onOpen, onNew }) {
  return (
    <>
      <div className="fl-section" style={{ justifyContent: 'space-between' }}>
        <div className="row" style={{ gap: 8, alignItems: 'baseline' }}><h2 style={{ margin: 0 }}>{title}</h2><span>{list.length || ''}</span></div>
        {onNew && <button className="btn-sm noprint" onClick={onNew}><PlusIcon size={14} /> New {title === 'Parcels' ? 'parcel' : 'delivery'}</button>}
      </div>
      {loaded && list.length === 0 && <div className="bo-small">{empty}</div>}
      <div className="fl-grid">
        {list.map((g) => {
          const t = ticketFor(g.code)
          return (
            <button key={g.code} className={`fl-tile is-${g.state.key === 'served' ? 'served' : g.state.key}`} onClick={() => onOpen(g.code)}>
              <b><Icon size={15} /> {g.name}</b>
              <span className="fl-ticket-who">{t?.customerName || t?.customerPhone || 'Walk-in'}</span>
              <small>{statusLabel(g.orders[g.orders.length - 1])} · {ago(g.since)}</small>
              <span className="fl-amt">{money(g.total + (t?.deliveryCharge || 0))}</span>
            </button>
          )
        })}
      </div>
    </>
  )
}

// Everything about one table or ticket in one place: what's on it, where each order has got to, and what to do next.
function TableSheet({ group, ticket, tables, byTable, onClose, onBill, onAdd, onSlip, onTicketChanged }) {
  const isTicket = group.channel !== 'Dine'
  const [voiding, setVoiding] = useState(null)   // { order, item }
  const [moving, setMoving] = useState(false)
  const canCancel = auth.can('orders.cancel')
  const canMove = !isTicket && auth.can('orders.create')

  const cancelOrder = async (o) => {
    const reason = await ui.prompt({
      title: `Cancel ${group.name}'s order?`, message: 'Why is it being cancelled? This goes on the report.',
      placeholder: 'Guest left, wrong table…', required: true, confirmText: 'Cancel order', cancelText: 'Keep it', danger: true,
    })
    if (reason) run(api.cancel(o.id, reason))
  }

  const moveTo = async (t) => {
    const merging = byTable.has(t.code)
    if (merging && !(await ui.confirm({
      title: `Join with ${t.name}?`, message: `${t.name} already has a running bill. ${group.name}'s orders will join it, and they'll pay one bill together.`,
      confirmText: 'Join the bills',
    }))) return
    try {
      await api.moveTable(group.code, t.code)
      toast.success(merging ? `${group.name} joined ${t.name}` : `${group.name} moved to ${t.name}`)
      onClose()
    } catch (e) { toast.error(friendlyError(e)) }
  }

  const total = group.total + (ticket?.deliveryCharge || 0)
  return (
    <>
    <Modal wide title={group.name} sub={minutesSince(group.since) < 1 ? 'Just started' : `Running for ${ago(group.since)}`} onClose={onClose} footer={<>
      {auth.can('orders.create') && <button onClick={onAdd}><PlusIcon size={15} /> Add items</button>}
      {canMove && <button onClick={() => setMoving((v) => !v)}><SwapIcon size={15} /> Shift table</button>}
      <span className="ts-total push">{money(total)}</span>
      {auth.can('orders.pay') && <button className="primary" onClick={onBill}><ReceiptIcon size={15} /> Bill &amp; settle</button>}
    </>}>
      {moving && (
        <div className="bo-card" style={{ marginBottom: 12 }}>
          <div className="bo-small" style={{ marginBottom: 8 }}>Move {group.name} to:</div>
          <div className="no-tables">
            {tables.filter((t) => t.code !== group.code).map((t) => (
              <button key={t.code} className={`no-table${byTable.has(t.code) ? ' is-busy' : ''}`} onClick={() => moveTo(t)}>{t.name}</button>
            ))}
          </div>
          <div className="bo-small">Tables marked in pink have a running bill. Moving there joins the two bills.</div>
        </div>
      )}

      {isTicket && ticket && <TicketDetails ticket={ticket} total={total} onChanged={onTicketChanged} />}

      {group.orders.map((o) => (
        <div className="ts-order" key={o.id}>
          <div className="ts-order-top">
            <span className="row" style={{ gap: 7 }}>
              <span className={`pill s-${o.status}`}>{statusLabel(o)}</span>
              <SourceBadge source={o.source} />
              {o.waiterName && <span className="bo-small">{o.waiterName}</span>}
            </span>
            <span className="bo-small">Order #{o.id} · {ago(o.createdAt)}</span>
          </div>
          {o.items.map((i) => (
            <div className="ts-line" key={i.id || i.name}>
              <span className="ts-line-name">
                {i.qty} × {i.name}{i.variantName ? ` (${i.variantName})` : ''}
                {(i.modifiers?.length > 0 || i.note) && <small>{[i.modifiers?.join(', '), i.note].filter(Boolean).join(' · ')}</small>}
              </span>
              <span className="ts-line-amt">{money(i.price * i.qty)}</span>
              {canCancel && i.id > 0 && (
                <button className="ghost ts-x" title={`Remove ${i.name}`} aria-label={`Remove ${i.name}`} onClick={() => setVoiding({ order: o, item: i })}><CloseIcon size={15} /></button>
              )}
            </div>
          ))}
          {o.customerNote && <div className="bo-small" style={{ marginTop: 3 }}>Note: {o.customerNote}</div>}
          <div className="ts-order-actions noprint">
            <OrderActions o={o} onError={(e) => toast.error(friendlyError(e))} />
            <button className="ghost btn-sm" onClick={() => onSlip(o, 'kitchen', true)}><KitchenIcon size={15} /> Kitchen slip</button>
            <button className="ghost btn-sm" onClick={() => onSlip(o, 'bill', false)}><PrintIcon size={15} /> Slips</button>
            {canCancel && o.status !== 'Served' && (
              <button className="ghost btn-sm" style={{ marginLeft: 'auto', color: 'var(--red)' }} onClick={() => cancelOrder(o)}>Cancel order</button>
            )}
          </div>
        </div>
      ))}
    </Modal>
    {voiding && <VoidDialog {...voiding} onClose={() => setVoiding(null)} />}
    </>
  )
}

const VOID_REASONS = ['Guest changed mind', 'Wrong item', 'Dish finished', 'Kitchen mistake']

function VoidDialog({ order, item, onClose }) {
  const [qty, setQty] = useState(1)
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')
  const save = async () => {
    setBusy(true); setErr('')
    try { await api.voidItem(order.id, item.id, qty, reason.trim()); onClose() }
    catch (e) { setErr(friendlyError(e)); setBusy(false) }
  }
  return (
    <Modal title={`Remove ${item.name}`} sub={`From ${order.tableName}. This shows on the owner's report.`} onClose={onClose} footer={<>
      <button onClick={onClose}>Keep it</button>
      <button className="red push" disabled={busy || !reason.trim()} onClick={save}>Remove {qty > 1 ? `${qty} ` : ''}· {money(item.price * qty)}</button>
    </>}>
      {item.qty > 1 && (
        <div className="bl-row" style={{ marginTop: 0 }}>
          <span className="bl-label">How many?</span>
          <span className="no-step">
            <button onClick={() => setQty(Math.max(1, qty - 1))}>−</button><b>{qty}</b><button onClick={() => setQty(Math.min(item.qty, qty + 1))}>+</button>
          </span>
          <span className="bo-small"> of {item.qty}</span>
        </div>
      )}
      <div className="bl-row">
        <span className="bl-label">Why?</span>
        <div className="bo-chips" style={{ marginBottom: 8 }}>
          {VOID_REASONS.map((r) => <button key={r} className={`bo-chip${reason === r ? ' on' : ''}`} onClick={() => setReason(r)}>{r}</button>)}
        </div>
        <input style={{ width: '100%' }} placeholder="Or type a reason" value={reason} onChange={(e) => setReason(e.target.value)} />
      </div>
      {err && <div className="form-error" style={{ marginTop: 10 }}>{err}</div>}
    </Modal>
  )
}

function TicketDetails({ ticket, total, onChanged }) {
  const [f, setF] = useState({
    customerName: ticket.customerName, customerPhone: ticket.customerPhone, address: ticket.address,
    riderName: ticket.riderName, deliveryCharge: ticket.deliveryCharge || '',
  })
  const isDelivery = ticket.channel === 'Delivery'
  const dirty = f.customerName !== ticket.customerName || f.customerPhone !== ticket.customerPhone || f.address !== ticket.address
    || f.riderName !== ticket.riderName || Number(f.deliveryCharge || 0) !== (ticket.deliveryCharge || 0)
  const save = async () => {
    try {
      await api.updateTicket(ticket.tableCode, { ...f, deliveryCharge: Number(f.deliveryCharge) || 0 })
      toast.success('Saved')
      onChanged()
    } catch (e) { toast.error(friendlyError(e)) }
  }
  const riderText = `Delivery from ${auth.getStaff()?.restaurantName || 'us'}\n${ticket.tableName}\nCustomer: ${f.customerName || '-'} ${f.customerPhone || ''}\nAddress: ${f.address || '-'}\nCollect: ${money(total)}`
  const set = (k) => (e) => setF({ ...f, [k]: e.target.value })
  return (
    <div className="ts-who">
      <div className="field"><label className="field-label">Customer</label><input value={f.customerName} onChange={set('customerName')} placeholder="Name" /></div>
      <div className="field"><label className="field-label">Phone</label><input value={f.customerPhone} onChange={set('customerPhone')} inputMode="tel" placeholder="03xx xxxxxxx" /></div>
      {isDelivery && <>
        <div className="field is-full"><label className="field-label">Address</label><input value={f.address} onChange={set('address')} /></div>
        <div className="field"><label className="field-label">Rider</label><input value={f.riderName} onChange={set('riderName')} placeholder="Rider's name" /></div>
        <div className="field"><label className="field-label">Delivery charge (Rs)</label><input type="number" min="0" value={f.deliveryCharge} onChange={set('deliveryCharge')} /></div>
      </>}
      <div className="is-full row" style={{ gap: 8 }}>
        {isDelivery && <a className="btn-link" href={waLink('', riderText)} target="_blank" rel="noreferrer"><button className="btn-sm"><ChatIcon size={15} /> Send to rider</button></a>}
        {f.customerPhone && <a href={`tel:${f.customerPhone}`}><button className="btn-sm">Call customer</button></a>}
        {dirty && <button className="primary btn-sm" style={{ marginLeft: 'auto' }} onClick={save}>Save details</button>}
      </div>
    </div>
  )
}

// Ringing up an order at the counter: pick where it's going, tap the dishes, send. Staff tiles are
// plain and quick on purpose, since speed matters more here than pictures.
function NewOrder({ init, tables, byTable, onClose }) {
  const [channel, setChannel] = useState(init.channel || 'Dine')
  const [table, setTable] = useState(init.table || null)
  const [who, setWho] = useState({ name: '', phone: '', address: '', charge: '' })
  const [matches, setMatches] = useState([])
  const [data, setData] = useState(null)
  const cart = useCart()
  const [note, setNote] = useState('')
  const [customizing, setCustomizing] = useState(null)
  const [query, setQuery] = useState('')
  const [cat, setCat] = useState(null)
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')
  const lookup = useRef(null)
  const adding = !!init.adding

  useEffect(() => { api.getMenu(auth.staffSlug()).then(setData).catch((e) => setErr(friendlyError(e))) }, [])

  const cats = data?.menu || []
  const q = query.trim().toLowerCase()
  const shown = q
    ? cats.flatMap((c) => c.items).filter((i) => i.name.toLowerCase().includes(q) || (i.nameUrdu || '').includes(query.trim()))
    : (cats.find((c) => c.id === cat) || cats[0])?.items || []

  const search = (text) => {
    clearTimeout(lookup.current)
    if (text.trim().length < 3) { setMatches([]); return }
    lookup.current = setTimeout(() => api.findCustomers(text).then(setMatches).catch(() => setMatches([])), 250)
  }
  const pick = (c) => { setWho({ ...who, name: c.name, phone: c.phone, address: c.address || who.address }); setMatches([]) }

  const needsWho = channel === 'Delivery' && (!who.phone.trim() || !who.address.trim())
  const blocked = cart.count === 0 || busy || (channel === 'Dine' && !table) || (!adding && needsWho)
  const hint = channel === 'Dine' && !table ? 'Choose a table first.'
    : !adding && needsWho ? "A delivery needs the customer's phone and address."
      : cart.count === 0 ? 'Tap a dish to add it.' : ''

  const place = async () => {
    setBusy(true); setErr('')
    try {
      const items = cartToItems(cart.list)
      let order
      if (channel === 'Dine' || adding) {
        order = await api.placeStaffOrder({ tableCode: table.code, items, note: note.trim() || null })
      } else {
        const res = await api.placeTicket({
          channel, customerName: who.name.trim(), customerPhone: who.phone.trim(), address: who.address.trim(),
          deliveryCharge: Number(who.charge) || 0, branchId: null, items, note: note.trim() || null,
        })
        order = res.order
      }
      // Whoever rings it up at the counter is the one who'd approve it, so it goes straight to the kitchen.
      if (auth.can('orders.approve')) {
        await api.approve(order.id).catch((e) => toast.error(`${order.tableName} is saved but still needs approval: ${friendlyError(e)}`))
      }
      toast.success(`${order.tableName}: order sent`)
      onClose()
    } catch (e) { setErr(friendlyError(e)); setBusy(false) }
  }

  return (
    <>
    <Modal wide title={adding ? `Add to ${init.table.name}` : 'New order'} onClose={onClose}>
      {!adding && (
        <div className="no-channels">
          <button className={`no-channel${channel === 'Dine' ? ' on' : ''}`} onClick={() => setChannel('Dine')}><TableIcon size={17} /> Table</button>
          <button className={`no-channel${channel === 'Takeaway' ? ' on' : ''}`} onClick={() => setChannel('Takeaway')}><BagIcon size={17} /> Parcel</button>
          <button className={`no-channel${channel === 'Delivery' ? ' on' : ''}`} onClick={() => setChannel('Delivery')}><TruckIcon size={17} /> Delivery</button>
        </div>
      )}

      {!adding && channel === 'Dine' && (
        <div className="no-tables">
          {tables.map((t) => (
            <button key={t.code} className={`no-table${table?.code === t.code ? ' on' : ''}${byTable.has(t.code) ? ' is-busy' : ''}`}
              onClick={() => setTable({ code: t.code, name: t.name })}>{t.name}</button>
          ))}
        </div>
      )}

      {!adding && channel !== 'Dine' && (
        <div className="ts-who">
          <div className="field">
            <label className="field-label">Phone{channel === 'Takeaway' ? ' (optional)' : ''}</label>
            <input inputMode="tel" placeholder="03xx xxxxxxx" value={who.phone} onChange={(e) => { setWho({ ...who, phone: e.target.value }); search(e.target.value) }} />
            {matches.length > 0 && (
              <div className="no-suggest">
                {matches.map((c) => (
                  <button key={c.id} onClick={() => pick(c)}>{c.name} · {c.phone}<small>{c.address}{c.balance > 0 ? ` · owes ${money(c.balance)}` : ''}</small></button>
                ))}
              </div>
            )}
          </div>
          <div className="field">
            <label className="field-label">Name{channel === 'Takeaway' ? ' (optional)' : ''}</label>
            <input value={who.name} onChange={(e) => setWho({ ...who, name: e.target.value })} />
          </div>
          {channel === 'Delivery' && <>
            <div className="field is-full"><label className="field-label">Address</label><input value={who.address} onChange={(e) => setWho({ ...who, address: e.target.value })} /></div>
            <div className="field"><label className="field-label">Delivery charge (Rs)</label><input type="number" min="0" inputMode="numeric" value={who.charge} onChange={(e) => setWho({ ...who, charge: e.target.value })} /></div>
          </>}
        </div>
      )}

      <div className="no-grid">
        <div>
          <input style={{ width: '100%', marginBottom: 8 }} placeholder="Search dishes" value={query} onChange={(e) => setQuery(e.target.value)} />
          {!q && (
            <div className="qm-cats">
              {cats.map((c) => (
                <button key={c.id} className={`bo-chip${(cat ?? cats[0]?.id) === c.id ? ' on' : ''}`} onClick={() => setCat(c.id)}>{c.name}</button>
              ))}
            </div>
          )}
          <div className="qm-grid">
            {shown.map((it) => {
              const from = it.variants?.length ? Math.min(...it.variants.map((v) => v.price)) : (it.dealActive && it.dealPrice != null ? it.dealPrice : it.price)
              return (
                <button key={it.id} className={`qm-tile${it.available ? '' : ' is-off'}`} onClick={() => plusOrCustomize(it, cart.addToCart, setCustomizing)}>
                  <span>{it.name}</span>
                  <small>{it.variants?.length ? 'from ' : ''}{money(from)}</small>
                </button>
              )
            })}
            {data && shown.length === 0 && <div className="bo-small">No dishes found.</div>}
          </div>
        </div>

        <div className="no-cart">
          <div className="no-cart-title">{cart.count ? `${cart.count} item${cart.count === 1 ? '' : 's'}` : 'Order'}</div>
          {cart.list.map((x) => (
            <div className="no-cart-line" key={x.sig}>
              <span>
                {x.name}{x.variantName ? ` (${x.variantName})` : ''}
                {x.modifierNames.length > 0 && <small>{x.modifierNames.join(', ')}</small>}
              </span>
              <span className="no-step" style={{ flex: 'none' }}>
                <button onClick={() => cart.changeQty(x.sig, -1)} aria-label="One less">−</button><b>{x.qty}</b>
                <button onClick={() => cart.changeQty(x.sig, 1)} aria-label="One more">+</button>
              </span>
            </div>
          ))}
          {cart.count > 0 && <input style={{ width: '100%', marginTop: 8 }} placeholder="Note for the kitchen" value={note} onChange={(e) => setNote(e.target.value)} />}
          <div className="no-sum"><span>Total</span><b>{money(cart.total + (channel === 'Delivery' ? Number(who.charge) || 0 : 0))}</b></div>
          {err && <div className="form-error">{err}</div>}
          {hint && <div className="bo-small" style={{ marginBottom: 8 }}>{hint}</div>}
          <button className="primary" style={{ width: '100%' }} disabled={blocked} onClick={place}>{busy ? 'Sending…' : 'Send to kitchen'}</button>
        </div>
      </div>

    </Modal>
    {customizing && <Customizer item={customizing} onClose={() => setCustomizing(null)} onAdd={(l) => { cart.addToCart(l); setCustomizing(null) }} />}
    </>
  )
}

// One printable slip: a kitchen ticket (big items, no prices) or a receipt (with prices and
// total). `brk` starts it on a fresh page so a thermal printer cuts here.
function Slip({ o, kind, brk }) {
  const isKitchen = kind === 'kitchen'
  return (
    <div className={'receipt' + (brk ? ' slip-break' : '')}>
      <div className="r-h">
        <div className="r-name">{isKitchen ? 'KITCHEN' : 'Receipt'} · #{o.id}</div>
        <div style={{ fontSize: isKitchen ? 15 : 12, fontWeight: isKitchen ? 700 : 400 }}>{o.tableName}{o.branchName ? ` · ${o.branchName}` : ''}</div>
        <div style={{ fontSize: 12 }}>{new Date(o.createdAt).toLocaleString()}</div>
      </div>
      {o.items.map((i, n) => (
        <div className="r-line" key={n} style={isKitchen ? { fontSize: 16, fontWeight: 700 } : {}}>
          <span>{i.qty}× {i.name}{i.variantName ? ` (${i.variantName})` : ''}
            {i.modifiers?.length ? ` · ${i.modifiers.join(', ')}` : ''}{i.note ? `: ${i.note}` : ''}</span>
          {!isKitchen && <span>{money(i.price * i.qty)}</span>}
        </div>
      ))}
      {o.customerNote && <div style={{ fontSize: 12, marginTop: 6 }}>Note: {o.customerNote}</div>}
      {!isKitchen && <div className="r-tot r-line r-grand"><span>Total</span><span>{money(o.total)}</span></div>}
      <div className="r-foot">{o.waiterName ? `Waiter: ${o.waiterName} · ` : ''}{statusLabel(o)}{isKitchen && o.etaMinutes ? ` · ETA ${o.etaMinutes} min` : ''}</div>
      {!isKitchen && <div className="r-brand">Powered by Dine In · 0309 8480389</div>}
    </div>
  )
}

// Printing never changes the order: it stays in the list and can be reprinted any time.
function OrderDetail({ order: o, init, onClose }) {
  const [mode, setMode] = useState(init?.mode || 'kitchen')   // 'kitchen' | 'bill' | 'both'
  const doPrint = (m) => { setMode(m); setTimeout(() => window.print(), 90) }
  useEffect(() => { if (init?.autoPrint) setTimeout(() => window.print(), 160) }, [])   // print on open when asked
  return (
    <div className="overlay" onMouseDown={(e) => { if (e.target.classList.contains('overlay')) onClose() }}>
      <div className="modal">
        {mode === 'both' ? (
          <div className="print-area">
            <Slip o={o} kind="kitchen" />
            <Slip o={o} kind="bill" brk />
          </div>
        ) : (
          <Slip o={o} kind={mode === 'bill' ? 'bill' : 'kitchen'} />
        )}
        <div className="noprint" style={{ marginTop: 14 }}>
          <div className="row" style={{ gap: 8, marginBottom: 8, flexWrap: 'wrap' }}>
            <button className={mode === 'kitchen' ? 'primary' : ''} onClick={() => setMode('kitchen')}><KitchenIcon size={15} /> Kitchen</button>
            <button className={mode === 'bill' ? 'primary' : ''} onClick={() => setMode('bill')}><ReceiptIcon size={15} /> Receipt</button>
            <button className={mode === 'both' ? 'primary' : ''} onClick={() => setMode('both')}>Both</button>
          </div>
          <div className="row" style={{ gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
            <button onClick={onClose}>Close</button>
            <button className="blue" style={{ marginLeft: 'auto' }} onClick={() => doPrint(mode)}><PrintIcon size={15} /> Print {mode === 'both' ? 'both' : mode === 'bill' ? 'receipt' : 'kitchen slip'}</button>
          </div>
        </div>
      </div>
    </div>
  )
}

const DISCOUNT_STEPS = [5, 10, 15, 20]

function BillModal({ code, onClose }) {
  const [bill, setBill] = useState(null)
  const [invoice, setInvoice] = useState(null)
  const [method, setMethod] = useState('Cash')
  const [discount, setDiscount] = useState('')       // '' means use the owner's standing discount
  const [tendered, setTendered] = useState('')
  const [ways, setWays] = useState(2)
  const [amount, setAmount] = useState('')
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')
  const [member, setMember] = useState(null)         // loyalty: who this bill earns points for
  const [customer, setCustomer] = useState(null)     // khata: whose account it goes on
  const timer = useRef(null)
  const canDiscount = auth.can('orders.discount')
  const canKhata = auth.can('customers.manage') && auth.hasFeature('billing')
  const methods = ['Cash', 'Card', 'JazzCash', 'Easypaisa', ...(canKhata ? ['Khata'] : [])]

  // Load the bill; a discount or a different way of paying re-prices it on the server (tax on net).
  const load = (d, m = method) => api.getBill(code, d, m).then((b) => {
    setBill(b)
    if (d == null && discount === '') setDiscount(b.discountAmount > 0 ? String(b.discountAmount) : '')
  }).catch((e) => setErr(friendlyError(e)))
  useEffect(() => { load(null) }, [code])

  const currentDiscount = () => (discount === '' ? null : Number(discount) || 0)

  const onDiscount = (v) => {
    setDiscount(v)
    clearTimeout(timer.current)
    timer.current = setTimeout(() => load(v === '' ? 0 : Number(v) || 0), 300)
  }
  const onMethod = (m) => { setMethod(m); setErr(''); load(currentDiscount(), m) }

  const foodTotal = (bill?.lines || []).filter((l) => l.name !== 'Delivery charge').reduce((s, l) => s + l.unitPrice * l.qty, 0)
  const applyPercent = (pct) => onDiscount(String(Math.round((foodTotal * pct) / 100)))

  const printIfWanted = (inv) => { if (inv.receiptEnabled) setTimeout(() => window.print(), 300) }

  const settle = async () => {
    setBusy(true); setErr('')
    try {
      const inv = await api.settle(code, method, Number(discount) || 0, Number(tendered) || 0, customer?.id ?? null)
      setInvoice(inv)
      printIfWanted(inv)
    } catch (e) { setErr(friendlyError(e)) }
    setBusy(false)
  }

  const pay = async (amt) => {
    setBusy(true); setErr('')
    try {
      const res = await api.payPart(code, Number(amt), method, currentDiscount())
      if (res.settled) { setInvoice(res.invoice); printIfWanted(res.invoice) }
      else { await load(currentDiscount()); setAmount('') }
      setBusy(false)
      return true
    } catch (e) { setErr(friendlyError(e)) }
    setBusy(false)
    return false
  }

  const remaining = bill?.remaining ?? 0
  const share = ways > 0 ? Math.ceil((remaining / ways) * 100) / 100 : 0
  const isCash = method === 'Cash'
  const isKhata = method === 'Khata'
  const tenderedNum = Number(tendered) || 0
  const change = Math.max(0, tenderedNum - remaining)
  const short = isCash && tenderedNum > 0 && tenderedNum < remaining
  const khataBlocked = isKhata && !customer
  const shareText = (d) => `${d.restaurantName}\n${d.tableName}\n${d.lines.map((l) => `${l.qty} x ${l.name}: ${money(l.lineTotal)}`).join('\n')}\nTotal: ${money(d.total)}`

  return (
    <div className="overlay" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose() }}>
      <div className="modal bill-modal">
        {/* Before settling, always show the working bill. After, only if receipts are switched on. */}
        {!invoice && <Receipt bill={bill} />}
        {invoice && invoice.receiptEnabled && <Receipt invoice={invoice} />}

        {!invoice ? (
          <div className="noprint bill-pay" style={{ marginTop: 14 }}>
            {bill?.amountPaid > 0 && (
              <div className="card" style={{ padding: 10, marginBottom: 10 }}>
                <div className="row between" style={{ fontSize: 14 }}><span>Paid so far</span><b>{money(bill.amountPaid)}</b></div>
                <div className="row between" style={{ fontSize: 14 }}><span>Remaining</span><b style={{ color: 'var(--accent)' }}>{money(remaining)}</b></div>
                <div style={{ height: 8, background: 'var(--surface2)', borderRadius: 6, overflow: 'hidden', marginTop: 6 }}>
                  <div style={{ width: `${bill.total ? (bill.amountPaid / bill.total) * 100 : 0}%`, height: '100%', background: 'var(--accent2)' }} />
                </div>
              </div>
            )}

            <BillMember code={code} total={bill?.total ?? 0} onChange={setMember} />

            <span className="bl-label">Paid by</span>
            <div className="bl-methods">
              {methods.map((m) => <button key={m} className={`bl-method${method === m ? ' on' : ''}`} onClick={() => onMethod(m)}>{m}</button>)}
            </div>

            {isKhata && <KhataPicker customer={customer} onPick={setCustomer} amount={remaining} initialPhone={bill?.customerPhone} initialName={bill?.customerName} />}

            {canDiscount && (
              <div className="bl-row">
                <span className="bl-label">Discount</span>
                <div className="bl-disc">
                  <input type="number" min="0" inputMode="numeric" placeholder="Rs" value={discount} onChange={(e) => onDiscount(e.target.value)} />
                  {DISCOUNT_STEPS.map((p) => <button key={p} className="bo-chip" onClick={() => applyPercent(p)}>{p}%</button>)}
                  {Number(discount) > 0 && <button className="ghost btn-sm" onClick={() => onDiscount('0')}>Clear</button>}
                </div>
              </div>
            )}

            {isCash && (
              <div className="bl-row">
                <label className="bl-label" htmlFor="bl-tendered">Cash received</label>
                <input id="bl-tendered" style={{ width: '100%' }} type="number" min="0" inputMode="decimal" placeholder={money(remaining)} value={tendered} onChange={(e) => setTendered(e.target.value)} />
              </div>
            )}

            {isCash && tenderedNum > 0 && (
              <div className={'change-box' + (short ? ' short' : '')}>
                <span>{short ? 'Still due' : 'Change to return'}</span>
                <b>{money(short ? remaining - tenderedNum : change)}</b>
              </div>
            )}

            {!isKhata && (
              <details className="split-wrap">
                <summary className="muted">Split the bill</summary>
                <div className="row" style={{ gap: 8, margin: '8px 0', flexWrap: 'wrap', alignItems: 'center' }}>
                  <span className="muted" style={{ fontSize: 13 }}>People left to pay</span>
                  <input type="number" min="1" value={ways} onChange={(e) => setWays(Number(e.target.value))} style={{ width: 60 }} />
                  <span className="muted" style={{ fontSize: 13 }}>= {money(share)} each</span>
                  <button disabled={busy || remaining <= 0} onClick={async () => { if (await pay(share)) setWays((w) => Math.max(1, w - 1)) }}>Take one share</button>
                </div>
                <div className="row" style={{ gap: 8, flexWrap: 'wrap' }}>
                  <input type="number" placeholder="Custom amount" value={amount} onChange={(e) => setAmount(e.target.value)} style={{ flex: 1, minWidth: 120 }} />
                  <button disabled={busy || !amount || Number(amount) <= 0} onClick={() => pay(amount)}>Take this amount</button>
                </div>
              </details>
            )}

            {err && <div className="form-error" style={{ marginTop: 8 }}>{err}</div>}
            <div className="row bill-actions" style={{ gap: 8, marginTop: 12, flexWrap: 'wrap' }}>
              <button onClick={onClose}>Back</button>
              <button onClick={() => window.print()} disabled={!bill}><PrintIcon size={15} /> Print bill</button>
              {bill && <a href={waLink(bill.customerPhone, shareText(bill))} target="_blank" rel="noreferrer"><button><ChatIcon size={15} /> WhatsApp</button></a>}
              <button className="green" style={{ marginLeft: 'auto' }} disabled={busy || !bill?.hasOpenOrders || short || khataBlocked} onClick={settle}>
                {busy ? 'Settling…' : <><CashIcon size={15} /> {isKhata ? 'Put on khata' : bill?.amountPaid > 0 ? 'Settle the rest' : 'Settle'}</>}
              </button>
            </div>
          </div>
        ) : (
          <div className="noprint" style={{ marginTop: 14 }}>
            <div className="paid-badge" style={{ marginBottom: 10 }}><CheckIcon size={15} /> {invoice.paymentMethod === 'Khata' ? 'On khata' : 'Paid'}: {invoice.number}
              {invoice.changeGiven > 0 && <span style={{ marginLeft: 8 }}>· change {money(invoice.changeGiven)}</span>}</div>
            {member?.enabled && member.member && member.perAmount > 0 && invoice.total >= member.minBillAmount && (
              <div className="lx-earned fade-up">About {(Math.floor(invoice.total / member.perAmount) * member.earnPoints).toLocaleString('en-PK')} points added for {member.member.name}</div>
            )}
            <div className="row" style={{ gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
              {invoice.receiptEnabled && <button onClick={() => window.print()}><PrintIcon size={15} /> Reprint</button>}
              <a href={waLink(invoice.customerPhone, shareText(invoice))} target="_blank" rel="noreferrer"><button><ChatIcon size={15} /> WhatsApp</button></a>
              <button className="primary" style={{ marginLeft: 'auto' }} onClick={onClose}>Done</button>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}

// Choose whose khata a bill goes on, adding them on the spot if they're new.
function KhataPicker({ customer, onPick, amount, initialPhone, initialName }) {
  const [q, setQ] = useState(initialPhone || '')
  const [rows, setRows] = useState([])
  const [adding, setAdding] = useState(false)
  const [name, setName] = useState(initialName || '')
  const timer = useRef(null)
  const look = (text) => {
    clearTimeout(timer.current)
    if (text.trim().length < 2) { setRows([]); return }
    timer.current = setTimeout(() => api.findCustomers(text).then(setRows).catch(() => setRows([])), 220)
  }
  useEffect(() => { if (initialPhone) look(initialPhone) }, [])
  const add = async () => {
    try {
      const phone = /^[+\d\s-]{7,}$/.test(q.trim()) ? q.trim() : ''
      const res = await api.addCustomer({ name: name.trim(), phone, address: '', notes: '', openingBalance: 0, creditLimit: 0 })
      onPick({ id: res.id, name: name.trim(), phone, balance: 0, creditLimit: 0 })
    } catch (e) { toast.error(friendlyError(e)) }
  }
  if (customer) {
    const after = customer.balance + amount
    return (
      <div className="bl-row bl-picked">
        <span><b>{customer.name}</b><br /><span className="bo-small">Owes {money(customer.balance)} now, {money(after)} after this{customer.creditLimit > 0 ? ` · limit ${money(customer.creditLimit)}` : ''}</span></span>
        <button className="ghost btn-sm" onClick={() => onPick(null)}>Change</button>
      </div>
    )
  }
  return (
    <div className="bl-row">
      <span className="bl-label">Whose khata?</span>
      <div className="search-box"><SearchIcon size={15} /><input autoFocus placeholder="Name or mobile" value={q} onChange={(e) => { setQ(e.target.value); look(e.target.value) }} /></div>
      {rows.length > 0 && (
        <div className="no-suggest">
          {rows.map((c) => <button key={c.id} onClick={() => onPick(c)}>{c.name} · {c.phone}<small>Owes {money(c.balance)}</small></button>)}
        </div>
      )}
      {adding ? (
        <div className="row" style={{ gap: 6, marginTop: 8 }}>
          <input style={{ flex: 1 }} autoFocus placeholder="Customer's name" value={name} onChange={(e) => setName(e.target.value)} />
          <button className="primary" disabled={!name.trim()} onClick={add}>Add</button>
        </div>
      ) : (
        <button className="ghost btn-sm" style={{ marginTop: 6 }} onClick={() => setAdding(true)}><UserIcon size={14} /> New customer</button>
      )}
    </div>
  )
}

// Settled bills: searchable, paginated, with reprint.
function ReceptionHistory() {
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [data, setData] = useState(null)
  const [err, setErr] = useState('')
  const [view, setView] = useState(null)   // invoice being reprinted
  const timer = useRef(null)

  const load = (query = q, p = page) => api.receptionDashboard(query, p, 12).then(setData).catch((e) => setErr(friendlyError(e)))
  useEffect(() => { load('', 1) }, [])
  const onSearch = (v) => {
    setQ(v); setPage(1)
    clearTimeout(timer.current); timer.current = setTimeout(() => load(v, 1), 300)
  }
  const go = (p) => { setPage(p); load(q, p) }
  const openInvoice = async (id) => { try { setView(await api.invoice(id)) } catch (e) { toast.error(friendlyError(e)) } }

  const rows = data?.history ?? []
  return (
    <div className="card hist-card">
      <div className="hist-filters">
        <div className="search-box">
          <SearchIcon size={15} />
          <input placeholder="Search bill number, table or cashier" value={q} onChange={(e) => onSearch(e.target.value)} />
        </div>
      </div>
      <div className="table-scroll">
        <table className="data-table">
          <thead>
            <tr><th>Bill</th><th>Table</th><th>Paid by</th><th>Total</th><th>Received</th><th>Change</th><th>Cashier</th><th>Time</th><th></th></tr>
          </thead>
          <tbody>
            {rows.map((b) => (
              <tr key={b.id}>
                <td>{b.number}</td>
                <td>{b.tableName}</td>
                <td>{b.paymentMethod}</td>
                <td>{money(b.total)}</td>
                <td>{b.amountTendered > 0 ? money(b.amountTendered) : '-'}</td>
                <td>{b.changeGiven > 0 ? money(b.changeGiven) : '-'}</td>
                <td className="muted">{b.cashier || '-'}</td>
                <td className="muted">{new Date(b.issuedAt).toLocaleString('en-PK', { dateStyle: 'short', timeStyle: 'short' })}</td>
                <td>
                  <button className="ghost btn-sm" onClick={() => openInvoice(b.id)}><PrintIcon size={14} /> Reprint</button>
                </td>
              </tr>
            ))}
            {rows.length === 0 && <tr><td colSpan={9} className="empty-row">{err || 'No settled bills yet.'}</td></tr>}
          </tbody>
        </table>
      </div>
      <div className="pager">
        <button disabled={page <= 1} onClick={() => go(page - 1)}>Previous</button>
        <span className="muted">Page {data?.page ?? 1} of {Math.max(1, data?.totalPages ?? 1)} · {data?.historyTotal ?? 0} bills</span>
        <button disabled={(data?.page ?? 1) >= (data?.totalPages ?? 1)} onClick={() => go(page + 1)}>Next</button>
      </div>

      {view && (
        <div className="overlay" onMouseDown={(e) => { if (e.target === e.currentTarget) setView(null) }}>
          <div className="modal">
            <Receipt invoice={view} />
            <div className="noprint row" style={{ marginTop: 12, gap: 8 }}>
              <button onClick={() => setView(null)}>Close</button>
              <button className="blue" style={{ marginLeft: 'auto' }} onClick={() => window.print()}><PrintIcon size={15} /> Reprint</button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

// Where the order came from.
function SourceBadge({ source }) {
  const label = source === 'Waiter' ? <><UserIcon size={12} /> Staff</> : source === 'Counter' ? <><ReceiptIcon size={12} /> Counter</> : <><QrIcon size={12} /> QR</>
  return <span className="pill" style={{ background: 'var(--surface2)', fontSize: 11, display: 'inline-flex', alignItems: 'center', gap: 4 }}>{label}</span>
}

function Lines({ o }) {
  return (
    <div className="fl-lines">
      {o.items.map((i, n) => (
        <div key={n}>
          {i.qty} × {i.name}{i.variantName ? ` (${i.variantName})` : ''}
          {i.modifiers?.length > 0 && <span className="muted"> · {i.modifiers.join(', ')}</span>}
          {i.note ? `: ${i.note}` : ''}
        </div>
      ))}
      {o.customerNote && <div className="muted" style={{ fontSize: 13 }}>Note: {o.customerNote}</div>}
    </div>
  )
}
