import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, money } from '../api'
import { auth, ticketLabel } from '../auth'
import { NOT_LOADED, loadUntilOk, useStaffOrders } from '../useStaffOrders'
import { toast } from '../ui.jsx'
import { friendlyError } from '../errors'
import { waLink } from '../backoffice.jsx'
import MapView, { directionsUrl } from '../MapView.jsx'
import { BagIcon, BikeIcon, CallIcon, ChatIcon, CheckIcon, ClockIcon, PinIcon, ReceiptIcon, TruckIcon } from '../icons.jsx'
import '../design/delivery.css'

// The delivery board: every takeaway and delivery from the order to the door. Reception sets the time
// promised to the customer, sends the rider a private link, and marks it out for delivery and delivered.
const clock = (d) => new Date(d).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })
const PROMISE = { Delivery: [20, 30, 45, 60], Takeaway: [10, 15, 20, 30] }
const run = (p) => Promise.resolve(p).catch((e) => { toast.error(friendlyError(e)); throw e })

function stageOf(t, orders) {
  const d = t.channel === 'Delivery'
  if (t.deliveredAt) return { key: 'done', label: d ? `Delivered ${clock(t.deliveredAt)}` : `Collected ${clock(t.deliveredAt)}` }
  if (t.dispatchedAt) return { key: 'way', label: 'On the way' }
  if (orders.length === 0) return { key: 'empty', label: 'No order yet' }
  if (orders.some((o) => o.status === 'Placed')) return { key: 'new', label: 'New: approve it' }
  if (orders.some((o) => o.status === 'Approved' || o.status === 'Cooking')) return { key: 'kitchen', label: 'In the kitchen' }
  if (orders.every((o) => o.status === 'Served' || o.status === 'Paid')) return d ? { key: 'way', label: 'On the way' } : { key: 'done', label: 'Collected' }
  return { key: 'ready', label: d ? 'Food ready' : 'Ready to collect' }
}

export default function Deliveries() {
  const orders = useStaffOrders()
  const [tickets, setTickets] = useState(null)
  const [shop, setShop] = useState(null)
  const [now, setNow] = useState(Date.now())
  const load = () => api.tickets().then(setTickets).catch(() => {})

  useEffect(() => loadUntilOk(() => api.shopPoint(), setShop), [])
  useEffect(() => {
    const stop = loadUntilOk(() => api.tickets(), setTickets)
    const t = setInterval(() => { load(); setNow(Date.now()) }, 10000)   // riders move; times count down
    return () => { stop?.(); clearInterval(t) }
  }, [])
  // a new order, an approval or the kitchen's "ready" changes the board straight away
  const sig = useMemo(() => orders.map((o) => `${o.id}:${o.status}`).join(), [orders])
  useEffect(() => { if (tickets) load() }, [sig])

  const rows = useMemo(() => (tickets || []).map((t) => {
    const list = orders === NOT_LOADED ? [] : orders.filter((o) => o.tableCode === t.tableCode)
    const food = list.reduce((s, o) => s + o.total, 0)
    const left = t.promisedAt ? Math.round((new Date(t.promisedAt) - now) / 60000) : null
    return { t, list, total: food + (t.deliveryCharge || 0), stage: stageOf(t, list), left }
  }).sort((a, b) => {
    const rank = (x) => (x.stage.key === 'done' ? 3 : x.left != null && x.left < 0 ? 0 : 1)
    return rank(a) - rank(b) || (a.t.promisedAt || a.t.openedAt).localeCompare(b.t.promisedAt || b.t.openedAt)
  }), [tickets, orders, now])

  const deliveries = rows.filter((x) => x.t.channel === 'Delivery')
  const takeaways = rows.filter((x) => x.t.channel === 'Takeaway')
  const active = rows.filter((x) => x.stage.key !== 'done')
  const late = active.filter((x) => x.left != null && x.left < 0).length
  const soon = active.filter((x) => x.left != null && x.left >= 0 && x.left <= 15).length
  const points = [
    shop && { ...shop, kind: 'shop', label: auth.getStaff()?.restaurantName || 'Restaurant' },
    ...deliveries.filter((x) => x.t.home && x.stage.key !== 'done').map((x) => ({ ...x.t.home, kind: 'home', label: ticketLabel(x.t.tableCode) })),
    ...deliveries.filter((x) => x.t.rider).map((x) => ({ ...x.t.rider, kind: 'rider', label: x.t.riderName || 'Rider' })),
  ].filter(Boolean)

  return (
    <div className="bo-wrap dl">
      <div className="fl-head">
        <div>
          <h1 className="h">Deliveries</h1>
          <p className="dl-sub">Takeaway and delivery, from the order to the door</p>
        </div>
        <Link to="/cashier"><button className="btn-sm"><ReceiptIcon size={15} /> Reception</button></Link>
      </div>

      <div className="bo-figs dl-figs">
        <div className="bo-fig is-lead"><div className="bo-fig-label">Running</div><div className="bo-fig-value">{tickets ? active.length : '–'}</div></div>
        <div className="bo-fig"><div className="bo-fig-label">On the way</div><div className="bo-fig-value">{active.filter((x) => x.stage.key === 'way').length}</div></div>
        <div className="bo-fig"><div className="bo-fig-label">Due in 15 min</div><div className="bo-fig-value">{soon}</div></div>
        <div className={'bo-fig' + (late ? ' dl-late' : '')}><div className="bo-fig-label">Late</div><div className="bo-fig-value">{late}</div></div>
      </div>

      {points.length > 0 && <MapView className="dl-map" height={typeof window !== 'undefined' && window.innerWidth < 640 ? 230 : 320} points={points} route={false} />}

      <div className="fl-section"><h2>Deliveries</h2><span>{deliveries.length || 'None running'}</span></div>
      <div className="dl-grid">{deliveries.map((x) => <TicketCard key={x.t.tableCode} x={x} onChanged={load} />)}</div>

      <div className="fl-section"><h2>Takeaway</h2><span>{takeaways.length || 'None waiting'}</span></div>
      <div className="dl-grid">{takeaways.map((x) => <TicketCard key={x.t.tableCode} x={x} onChanged={load} />)}</div>
    </div>
  )
}

function TicketCard({ x, onChanged }) {
  const { t, stage, total, left, list } = x
  const d = t.channel === 'Delivery'
  const [rider, setRider] = useState({ name: t.riderName || '', phone: t.riderPhone || '' })
  const [link, setLink] = useState(null)
  const [busy, setBusy] = useState('')
  const done = stage.key === 'done'
  const ready = stage.key === 'ready'
  const act = async (name, fn) => {
    setBusy(name)
    try { await run(fn()); onChanged() } catch { /* toast shown */ }
    setBusy('')
  }
  const makeLink = () => act('link', async () => {
    const res = await api.riderLink(t.tableCode, rider.name.trim(), rider.phone.trim())
    setLink(location.origin + res.path)
  })
  const riderText = link && [
    `Delivery from ${auth.getStaff()?.restaurantName || 'us'}: ${ticketLabel(t.tableCode)}`,
    `Customer: ${t.customerName || '-'} ${t.customerPhone || ''}`,
    `Address: ${t.address || '-'}`,
    t.home && `Map: ${directionsUrl(t.home)}`,
    `Collect: ${money(total)}`,
    t.promisedAt && `At the door by: ${clock(t.promisedAt)}`,
    `Your delivery page: ${link}`,
  ].filter(Boolean).join('\n')

  return (
    <article className={`dl-card s-${stage.key}` + (left != null && left < 0 && !done ? ' is-late' : '')}>
      <div className="dl-card-top">
        <span className="dl-ico">{d ? (stage.key === 'way' ? <BikeIcon size={18} /> : <TruckIcon size={18} />) : <BagIcon size={18} />}</span>
        <b>{ticketLabel(t.tableCode)}</b>
        <span className={`dl-chip s-${stage.key}`}>{stage.label}</span>
      </div>

      <div className="dl-who">
        <span><b>{t.customerName || 'Walk-in'}</b>{t.customerPhone && <> · {t.customerPhone}</>}</span>
        {t.customerPhone && <a className="dl-mini" href={`tel:${t.customerPhone}`} aria-label="Call the customer"><CallIcon size={15} /></a>}
      </div>
      {d && (
        <p className="dl-addr"><PinIcon size={14} /> <span>{t.address || 'No address'}{t.distanceKm != null && <em> · {t.distanceKm.toFixed(1)} km</em>}</span>
          {!t.home && <em className="dl-nopin">no map pin</em>}</p>
      )}
      <div className="dl-amt"><span>{d ? 'Rider collects' : 'To pay'}</span><b>{money(total)}</b></div>

      {!done && (
        <div className="dl-time">
          {t.promisedAt ? (
            <div className={'dl-due' + (left < 0 ? ' late' : left <= 10 ? ' soon' : '')}>
              <ClockIcon size={15} /> {d ? 'At the door by' : 'Ready by'} <b>{clock(t.promisedAt)}</b>
              <em>{left >= 0 ? `in ${left} min` : `${-left} min late`}</em>
            </div>
          ) : <small className="dl-hint">Tell the customer when: {d ? 'delivered in' : 'ready in'}</small>}
          <div className="dl-promise" role="group" aria-label="Promised time">
            {PROMISE[t.channel].map((m) => (
              <button key={m} disabled={!!busy} onClick={() => act('p' + m, () => api.promiseTicket(t.tableCode, m))}>{m} min</button>
            ))}
          </div>
        </div>
      )}

      {d && !done && (
        <div className="dl-rider">
          <div className="dl-rider-in">
            <input value={rider.name} onChange={(e) => setRider({ ...rider, name: e.target.value })} placeholder="Rider's name" maxLength={60} />
            <input value={rider.phone} onChange={(e) => setRider({ ...rider, phone: e.target.value })} placeholder="Rider's phone" inputMode="tel" maxLength={16} />
          </div>
          {link ? (
            <div className="dl-link">
              <a href={waLink(rider.phone, riderText)} target="_blank" rel="noreferrer"><button className="btn-sm green"><ChatIcon size={15} /> WhatsApp the rider</button></a>
              <button className="btn-sm" onClick={() => navigator.clipboard?.writeText(link).then(() => toast.success('Rider link copied'))}>Copy link</button>
            </div>
          ) : (
            <button className="btn-sm" disabled={!!busy || !rider.name.trim()} onClick={makeLink}>
              <BikeIcon size={15} /> {t.hasRiderLink ? 'New rider link' : 'Send to rider'}
            </button>
          )}
          {t.hasRiderLink && !link && <small className="dl-hint">{t.riderName || 'The rider'} has the link.{t.rider ? ' Sharing location now.' : ''}</small>}
        </div>
      )}

      <div className="dl-actions">
        {d && ready && <button className="primary" disabled={!!busy} onClick={() => act('go', () => api.dispatchTicket(t.tableCode))}><BikeIcon size={16} /> Out for delivery</button>}
        {d && stage.key === 'way' && <button className="primary green" disabled={!!busy} onClick={() => act('done', () => api.deliveredTicket(t.tableCode))}><CheckIcon size={16} /> Delivered</button>}
        {!d && ready && <button className="primary green" disabled={!!busy} onClick={() => act('done', () => api.deliveredTicket(t.tableCode))}><CheckIcon size={16} /> Collected</button>}
        {stage.key === 'new' && auth.can('orders.approve') && (
          <button className="primary" disabled={!!busy} onClick={() => act('ok', async () => { for (const o of list.filter((o) => o.status === 'Placed')) await api.approve(o.id) })}>
            <CheckIcon size={16} /> Approve · send to kitchen</button>
        )}
        {(stage.key === 'kitchen' || stage.key === 'empty' || (stage.key === 'new' && !auth.can('orders.approve'))) && <small className="dl-hint">{stage.key === 'new' ? 'Approve it at Reception to start cooking.' : stage.key === 'kitchen' ? 'The kitchen is on it.' : 'Add the order at Reception.'}</small>}
        {done && <small className="dl-hint">Settle the bill at Reception to close it.</small>}
      </div>
    </article>
  )
}
