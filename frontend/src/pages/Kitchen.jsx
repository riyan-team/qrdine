import { useState } from 'react'
import { api, money, STATUS_LABEL } from '../api'
import { useStaffOrders } from '../useStaffOrders'

// Kitchen Display Screen (KDS): accept approved orders, set the cooking time,
// then mark ready. The time you set drives the countdown on the customer's phone.
export default function Kitchen() {
  const orders = useStaffOrders()
  const queue = orders.filter((o) => o.status === 'Approved')
  const cooking = orders.filter((o) => o.status === 'Cooking')

  return (
    <div className="wrap ops">
      <div className="h">Kitchen display</div>
      <p className="muted">Approved orders appear here. Set how many minutes it will take — the customer sees a live countdown.</p>

      <div className="cat-title">👨‍🍳 To start ({queue.length})</div>
      <div className="grid cols">
        {queue.map((o) => <CookCard key={o.id} o={o} />)}
        {queue.length === 0 && <div className="empty">Nothing waiting. Reception approves orders into this queue.</div>}
      </div>

      <div className="cat-title" style={{ marginTop: 20 }}>🔥 Cooking ({cooking.length})</div>
      <div className="grid cols">
        {cooking.map((o) => (
          <div className="card" key={o.id}>
            <div className="row between"><b>{o.tableName} · #{o.id}</b>
              <span className={'pill s-' + o.status}>{STATUS_LABEL[o.status]}</span></div>
            <Lines o={o} />
            <div className="muted">Promised: {o.etaMinutes} min</div>
            <button className="green" style={{ width: '100%', marginTop: 8 }}
              onClick={() => api.ready(o.id)}>🔔 Mark ready</button>
          </div>
        ))}
      </div>
    </div>
  )
}

function CookCard({ o }) {
  // Default ETA = the largest prep time is unknown here, so start at a sensible 15.
  const [eta, setEta] = useState(15)
  return (
    <div className="card">
      <div className="row between"><b>{o.tableName} · #{o.id}</b>
        <span className="pill s-Approved">{STATUS_LABEL[o.status]}</span></div>
      <Lines o={o} />
      <div className="row" style={{ marginTop: 8 }}>
        <span className="muted">Time:</span>
        <select value={eta} onChange={(e) => setEta(+e.target.value)}>
          {[5, 10, 15, 20, 25, 30, 40, 45, 60].map((m) => <option key={m} value={m}>{m} min</option>)}
        </select>
        <button className="primary" style={{ marginLeft: 'auto' }}
          onClick={() => api.cook(o.id, eta)}>Start cooking →</button>
      </div>
    </div>
  )
}

function Lines({ o }) {
  return (
    <div style={{ margin: '8px 0' }}>
      {o.items.map((i, n) => (
        <div key={n}>
          <b>{i.qty}×</b> {i.name}{i.variantName ? ` (${i.variantName})` : ''}
          {i.modifiers?.length > 0 && <span className="muted"> · {i.modifiers.join(', ')}</span>}
          {i.note ? <span className="muted"> — {i.note}</span> : ''}
        </div>
      ))}
      {o.customerNote && <div className="muted" style={{ fontSize: 13, marginTop: 4 }}>📝 {o.customerNote}</div>}
    </div>
  )
}
