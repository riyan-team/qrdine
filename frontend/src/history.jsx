import { useEffect, useRef, useState } from 'react'
import { api, money, STATUS_LABEL } from './api'
import { friendlyError } from './errors'
import { SearchIcon } from './icons.jsx'

// Simple, reusable server-side pager.
export function Pager({ page, totalPages, total, onPage }) {
  if (!total) return null
  return (
    <div className="pager">
      <button disabled={page <= 1} onClick={() => onPage(page - 1)}>← Prev</button>
      <span className="muted">Page {page} of {Math.max(1, totalPages)} · {total} total</span>
      <button disabled={page >= totalPages} onClick={() => onPage(page + 1)}>Next →</button>
    </div>
  )
}

// Debounce a changing value (used for search-as-you-type without hammering the API).
function useDebounced(value, ms = 350) {
  const [v, setV] = useState(value)
  useEffect(() => { const t = setTimeout(() => setV(value), ms); return () => clearTimeout(t) }, [value, ms])
  return v
}

const todayIso = () => new Date().toISOString().slice(0, 10)
const agoIso = (days) => new Date(Date.now() - days * 86400000).toISOString().slice(0, 10)

// Shared order-history table for Kitchen / Waiter / Reception dashboards.
// Filters by status, date range and a search term (table or waiter), with paging.
export function OrderHistory({ statuses = ['', 'Placed', 'Approved', 'Cooking', 'Ready', 'Served', 'Paid', 'Cancelled'], pageSize = 15 }) {
  const [status, setStatus] = useState('')
  const [from, setFrom] = useState(agoIso(7))
  const [to, setTo] = useState(todayIso())
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [data, setData] = useState(null)
  const [err, setErr] = useState('')
  const dq = useDebounced(q)

  useEffect(() => { setPage(1) }, [status, from, to, dq])
  useEffect(() => {
    let alive = true
    api.ordersHistory({ status, from, to, q: dq, page, pageSize })
      .then((d) => alive && setData(d)).catch((e) => alive && setErr(friendlyError(e)))
    return () => { alive = false }
  }, [status, from, to, dq, page, pageSize])

  const rows = data?.rows ?? []
  return (
    <div className="card hist-card">
      <div className="hist-filters">
        <div className="search-box">
          <SearchIcon size={15} />
          <input placeholder="Search table or waiter…" value={q} onChange={(e) => setQ(e.target.value)} />
        </div>
        <select value={status} onChange={(e) => setStatus(e.target.value)}>
          {statuses.map((s) => <option key={s} value={s}>{s ? (STATUS_LABEL[s] || s) : 'All statuses'}</option>)}
        </select>
        <input type="date" value={from} max={to} onChange={(e) => setFrom(e.target.value)} />
        <span className="muted">→</span>
        <input type="date" value={to} min={from} max={todayIso()} onChange={(e) => setTo(e.target.value)} />
      </div>

      <div className="table-scroll">
        <table className="data-table">
          <thead>
            <tr><th>#</th><th>Table</th><th>Source</th><th>Items</th><th>Total</th><th>Waiter</th><th>Status</th><th>Time</th></tr>
          </thead>
          <tbody>
            {rows.map((o) => (
              <tr key={o.id}>
                <td>#{o.id}</td>
                <td>{o.tableName}</td>
                <td>{o.source}</td>
                <td>{o.items.reduce((s, i) => s + i.qty, 0)}</td>
                <td>{money(o.total)}</td>
                <td>{o.waiterName || '—'}</td>
                <td><span className={'pill s-' + o.status}>{STATUS_LABEL[o.status] || o.status}</span></td>
                <td className="muted">{new Date(o.createdAt).toLocaleString('en-PK', { dateStyle: 'short', timeStyle: 'short' })}</td>
              </tr>
            ))}
            {rows.length === 0 && <tr><td colSpan={8} className="empty-row">{err || 'No orders in this range.'}</td></tr>}
          </tbody>
        </table>
      </div>
      <Pager page={data?.page ?? 1} totalPages={data?.totalPages ?? 1} total={data?.total ?? 0} onPage={setPage} />
    </div>
  )
}
