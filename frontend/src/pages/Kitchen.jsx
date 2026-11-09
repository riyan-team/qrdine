import { useEffect, useRef, useState } from 'react'
import { api } from '../api'
import { NOT_LOADED, useStaffOrders } from '../useStaffOrders'
import { toast, playBell } from '../ui.jsx'
import { friendlyError } from '../errors'
import { OrderHistory } from '../history.jsx'
import { Tabs } from '../backoffice.jsx'
import { BellIcon, KitchenIcon } from '../icons.jsx'

// Run a status action, surfacing any failure as a toast (never silently).
const run = (p) => Promise.resolve(p).catch((e) => toast.error(friendlyError(e)))

const minutesSince = (iso) => Math.max(0, Math.floor((Date.now() - new Date(iso).getTime()) / 60000))
const MINUTES = [5, 10, 15, 20, 30, 45]

// A clock that moves on its own, so a ticket's time is right without anyone touching the screen.
function useMinuteTick() {
  const [, setTick] = useState(0)
  useEffect(() => {
    const t = setInterval(() => setTick((n) => n + 1), 30000)
    return () => clearInterval(t)
  }, [])
}

// The kitchen's screen: what to start, what's on the fire, what's waiting to go out.
// The minutes chosen here drive the countdown on the guest's phone.
export default function Kitchen() {
  const orders = useStaffOrders()
  const [tab, setTab] = useState('live')
  useMinuteTick()
  const queue = orders.filter((o) => o.status === 'Approved')
  const cooking = orders.filter((o) => o.status === 'Cooking')
  const ready = orders.filter((o) => o.status === 'Ready')

  // Chime when a ticket lands in the queue, so nothing is missed while the cook is looking away.
  const prevSnap = useRef(null)
  useEffect(() => {
    if (orders === NOT_LOADED) return
    const snap = new Map(orders.map((o) => [o.id, o.status]))
    const before = prevSnap.current
    if (before) {
      for (const o of orders) {
        if (o.status === 'Approved' && before.get(o.id) !== 'Approved') {
          playBell()
          toast.info(`New order: ${o.tableName}`)
        }
      }
    }
    prevSnap.current = snap
  }, [orders])

  return (
    <div className="bo-wrap">
      <div className="fl-head">
        <h1 className="h">Kitchen</h1>
        <div className="fl-actions noprint">
          <Tabs value={tab} onChange={setTab} options={[{ value: 'live', label: 'Live' }, { value: 'history', label: 'Earlier orders' }]} />
        </div>
      </div>

      {tab === 'history' ? (
        <OrderHistory statuses={['', 'Approved', 'Cooking', 'Ready', 'Served', 'Paid', 'Cancelled']} />
      ) : (
        <div className="kd-board">
          <Column title="To start" list={queue} empty="Nothing waiting." render={(o) => <StartTicket key={o.id} o={o} />} />
          <Column title="Cooking" list={cooking} empty="Nothing cooking." render={(o) => <CookingTicket key={o.id} o={o} />} />
          <Column title="Ready to go out" list={ready} empty="Nothing waiting to go out." render={(o) => <ReadyTicket key={o.id} o={o} />} />
        </div>
      )}
    </div>
  )
}

function Column({ title, list, empty, render }) {
  return (
    <section>
      <h2 className="kd-col-head">{title} <b>{list.length}</b></h2>
      <div className="kd-list">
        {list.map(render)}
        {list.length === 0 && <div className="kd-empty">{empty}</div>}
      </div>
    </section>
  )
}

function StartTicket({ o }) {
  const [eta, setEta] = useState(15)
  return (
    <article className="kd-ticket">
      <Top o={o} right={`${minutesSince(o.createdAt)} min ago`} />
      <Lines o={o} />
      <div className="kd-mins" role="group" aria-label="Minutes to prepare">
        <span>Minutes</span>
        {MINUTES.map((m) => <button key={m} className={m === eta ? 'on' : ''} aria-pressed={m === eta} onClick={() => setEta(m)}>{m}</button>)}
      </div>
      <button className="primary kd-act" onClick={() => run(api.cook(o.id, eta))}><KitchenIcon size={17} /> Start cooking</button>
    </article>
  )
}

function CookingTicket({ o }) {
  const since = minutesSince(o.acceptedAt || o.createdAt)
  const left = (o.etaMinutes || 0) - since
  const late = o.etaMinutes > 0 && left < 0
  return (
    <article className={`kd-ticket${late ? ' is-late' : ''}`}>
      <Top o={o} right={late ? `${-left} min late` : o.etaMinutes > 0 ? `${left} min left` : `${since} min`} late={late} />
      <Lines o={o} />
      <button className="green kd-act" onClick={() => run(api.ready(o.id))}><BellIcon size={17} /> Food is ready</button>
    </article>
  )
}

function ReadyTicket({ o }) {
  return (
    <article className="kd-ticket">
      <Top o={o} />
      <Lines o={o} />
      {o.waiterName && <div className="kd-by">{o.waiterName}</div>}
    </article>
  )
}

function Top({ o, right, late }) {
  return (
    <div className="kd-top">
      <b>{o.tableName}</b>
      {o.channel === 'Takeaway' && <span className="kd-tag">Parcel</span>}
      {o.channel === 'Delivery' && <span className="kd-tag">Delivery</span>}
      {right && <span className={`kd-time${late ? ' late' : ''}`}>{right}</span>}
    </div>
  )
}

function Lines({ o }) {
  return (
    <>
      <div className="kd-lines">
        {o.items.map((i, n) => (
          <div key={i.id || n}>
            <b>{i.qty}×</b> {i.name}{i.variantName ? ` (${i.variantName})` : ''}
            {(i.modifiers?.length > 0 || i.note) && <small>{[i.modifiers?.join(', '), i.note].filter(Boolean).join(' · ')}</small>}
          </div>
        ))}
      </div>
      {o.customerNote && <div className="kd-note">{o.customerNote}</div>}
    </>
  )
}
