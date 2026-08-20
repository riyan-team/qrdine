import { api, money, STATUS_LABEL } from '../api'
import { useStaffOrders } from '../useStaffOrders'
import { auth } from '../auth'

// Waiter screen: take a table, get alerted when food is ready, mark served.
// "You" is the signed-in staff member — assignments are tied to their real name.
export default function Waiter() {
  const orders = useStaffOrders()
  const me = auth.getStaff()?.displayName || 'Waiter'

  const ready = orders.filter((o) => o.status === 'Ready')
  const mine = orders.filter((o) => o.waiterName === me && o.status !== 'Ready')

  return (
    <div className="wrap ops ops-center">
      <div className="h">Waiter</div>
      <div className="row" style={{ marginBottom: 10, justifyContent: 'center' }}>
        <span className="muted">You are:</span> <b>{me}</b>
      </div>

      <div className="cat-title">✅ Ready to serve now ({ready.length})</div>
      <div className="grid cols">
        {ready.map((o) => (
          <div className="card" key={o.id}>
            <div className="row between"><b>{o.tableName} · #{o.id}</b>
              <span className={'pill s-' + o.status}>{STATUS_LABEL[o.status]}</span></div>
            <Lines o={o} />
            <div className="row" style={{ marginTop: 8, gap: 8 }}>
              {!o.waiterName && <button onClick={() => api.assign(o.id, me)}>Take table</button>}
              <button className="green" style={{ marginLeft: 'auto' }}
                onClick={() => api.serve(o.id)}>🍽️ Mark served</button>
            </div>
          </div>
        ))}
        {ready.length === 0 && <div className="empty">No food ready yet. You'll be alerted here the moment the kitchen finishes.</div>}
      </div>

      <div className="cat-title" style={{ marginTop: 20 }}>My tables ({mine.length})</div>
      <div className="grid cols">
        {mine.map((o) => (
          <div className="card" key={o.id}>
            <div className="row between"><b>{o.tableName} · #{o.id}</b>
              <span className={'pill s-' + o.status}>{STATUS_LABEL[o.status]}</span></div>
            <Lines o={o} />
            <div className="muted" style={{ marginTop: 6 }}>Total {money(o.total)}</div>
          </div>
        ))}
        {mine.length === 0 && <div className="empty">Take a table from the "ready" list to see it here.</div>}
      </div>
    </div>
  )
}

function Lines({ o }) {
  return (
    <div style={{ margin: '8px 0' }}>
      {o.items.map((i, n) => <div key={n}><b>{i.qty}×</b> {i.name}</div>)}
    </div>
  )
}
