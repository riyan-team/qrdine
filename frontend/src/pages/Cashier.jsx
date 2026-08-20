import { useEffect, useState } from 'react'
import { api, money, STATUS_LABEL } from '../api'
import { useStaffOrders } from '../useStaffOrders'
import Receipt from './Receipt.jsx'

// Reception / cashier: approve new orders, then bill & settle each table.
export default function Cashier() {
  const orders = useStaffOrders()
  const [billFor, setBillFor] = useState(null)   // table code being billed
  const [detailFor, setDetailFor] = useState(null)   // order being viewed

  const incoming = orders.filter((o) => o.status === 'Placed')
  const active = orders.filter((o) => o.status !== 'Placed')

  // Group active orders by table.
  const tables = {}
  for (const o of active) (tables[o.tableCode] ??= { code: o.tableCode, name: o.tableName, orders: [] }).orders.push(o)
  const tableList = Object.values(tables)

  return (
    <div className="wrap ops ops-center">
      <div className="h">Reception</div>
      <p className="muted">Approve new orders to send them to the kitchen. Bill and settle each table when the guests are done.</p>

      <div className="cat-title">🔔 New — waiting for approval ({incoming.length})</div>
      <div className="grid cols">
        {incoming.map((o) => (
          <div className="card" key={o.id}>
            <Head o={o} /><Lines o={o} />
            <div className="row" style={{ gap: 8, marginTop: 8 }}>
              <button className="ghost" onClick={() => setDetailFor(o)}>🔍 Details</button>
              <button className="green" style={{ marginLeft: 'auto' }} onClick={() => api.approve(o.id)}>✔ Approve → kitchen</button>
            </div>
          </div>
        ))}
        {incoming.length === 0 && <div className="empty">No new orders.</div>}
      </div>

      <div className="cat-title" style={{ marginTop: 20 }}>🪑 Occupied tables ({tableList.length})</div>
      <div className="grid cols">
        {tableList.map((t) => {
          const total = t.orders.reduce((s, o) => s + o.total, 0)
          return (
            <div className="card" key={t.code}>
              <div className="row between"><b>{t.name}</b><span className="muted">{t.orders.length} order(s)</span></div>
              <div style={{ margin: '8px 0' }}>
                {t.orders.map((o) => (
                  <div key={o.id} className="row between" style={{ fontSize: 13, cursor: 'pointer', padding: '2px 0' }}
                    onClick={() => setDetailFor(o)} title="View & print this order">
                    <span className="muted">🔍 #{o.id} · {o.items.reduce((s, i) => s + i.qty, 0)} items</span>
                    <span className={'pill s-' + o.status}>{STATUS_LABEL[o.status]}</span>
                  </div>
                ))}
              </div>
              <div className="row between">
                <b>{money(total)}</b>
                <button className="blue" onClick={() => setBillFor(t.code)}>🧾 Bill & settle</button>
              </div>
            </div>
          )
        })}
        {tableList.length === 0 && <div className="empty">No occupied tables. Approved orders appear here by table.</div>}
      </div>

      {billFor && <BillModal code={billFor} onClose={() => setBillFor(null)} />}
      {detailFor && <OrderDetail order={detailFor} onClose={() => setDetailFor(null)} />}
    </div>
  )
}

// A running order, in full, with a print button (prints an order slip).
function OrderDetail({ order: o, onClose }) {
  return (
    <div className="overlay" onMouseDown={(e) => { if (e.target.classList.contains('overlay')) onClose() }}>
      <div className="modal">
        <div className="receipt">
          <div className="r-h">
            <div style={{ fontWeight: 800, fontSize: 18 }}>Order #{o.id}</div>
            <div style={{ fontSize: 12 }}>{o.tableName}{o.branchName ? ` · ${o.branchName}` : ''}</div>
            <div style={{ fontSize: 12 }}>{new Date(o.createdAt).toLocaleString()}</div>
          </div>
          {o.items.map((i, n) => (
            <div className="r-line" key={n}>
              <span>{i.qty}× {i.name}{i.variantName ? ` (${i.variantName})` : ''}
                {i.modifiers?.length ? ` · ${i.modifiers.join(', ')}` : ''}{i.note ? ` — ${i.note}` : ''}</span>
              <span>{money(i.price * i.qty)}</span>
            </div>
          ))}
          {o.customerNote && <div style={{ fontSize: 12, marginTop: 6 }}>📝 {o.customerNote}</div>}
          <div className="r-tot r-line r-grand"><span>Total</span><span>{money(o.total)}</span></div>
          <div className="r-foot">Status: {STATUS_LABEL[o.status] || o.status}{o.waiterName ? ` · Waiter: ${o.waiterName}` : ''}{o.etaMinutes ? ` · ETA ${o.etaMinutes} min` : ''}</div>
        </div>
        <div className="noprint row" style={{ marginTop: 14, gap: 8, alignItems: 'center' }}>
          <button onClick={onClose}>Close</button>
          <span className={'pill s-' + o.status} style={{ marginLeft: 'auto' }}>{STATUS_LABEL[o.status] || o.status}</span>
          <button className="blue" onClick={() => window.print()}>🖨️ Print</button>
        </div>
      </div>
    </div>
  )
}

function BillModal({ code, onClose }) {
  const [bill, setBill] = useState(null)
  const [invoice, setInvoice] = useState(null)
  const [method, setMethod] = useState('Cash')
  const [discount, setDiscount] = useState(0)
  const [ways, setWays] = useState(2)
  const [amount, setAmount] = useState('')
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')

  const reload = () => api.getBill(code).then(setBill).catch((e) => setErr(e.message))
  useEffect(() => { reload() }, [code])

  const settle = async () => {
    setBusy(true); setErr('')
    try {
      const inv = await api.settle(code, method, Number(discount) || 0)
      setInvoice(inv); setTimeout(() => window.print(), 300)
    } catch (e) { setErr(e.message) }
    setBusy(false)
  }

  const pay = async (amt) => {
    setBusy(true); setErr('')
    try {
      const res = await api.payPart(code, Number(amt), method)
      if (res.settled) { setInvoice(res.invoice); setTimeout(() => window.print(), 300) }
      else { await reload(); setAmount('') }
    } catch (e) { setErr(e.message) }
    setBusy(false)
  }

  const remaining = bill?.remaining ?? 0
  const share = ways > 0 ? Math.ceil((remaining / ways) * 100) / 100 : 0

  return (
    <div className="overlay" onClick={(e) => { if (e.target.className === 'overlay') onClose() }}>
      <div className="modal">
        <Receipt bill={invoice ? null : bill} invoice={invoice} />

        {!invoice ? (
          <div className="noprint" style={{ marginTop: 14 }}>
            {bill?.amountPaid > 0 && (
              <div className="card" style={{ padding: 10, marginBottom: 10 }}>
                <div className="row between" style={{ fontSize: 14 }}><span>Paid so far</span><b>{money(bill.amountPaid)}</b></div>
                <div className="row between" style={{ fontSize: 14 }}><span>Remaining</span><b style={{ color: 'var(--accent)' }}>{money(remaining)}</b></div>
                <div style={{ height: 8, background: 'var(--surface2)', borderRadius: 6, overflow: 'hidden', marginTop: 6 }}>
                  <div style={{ width: `${bill.total ? (bill.amountPaid / bill.total) * 100 : 0}%`, height: '100%', background: 'var(--accent2)' }} />
                </div>
              </div>
            )}
            <div className="row" style={{ gap: 8, marginBottom: 8, flexWrap: 'wrap' }}>
              <span className="muted" style={{ fontSize: 13 }}>Pay via</span>
              <select value={method} onChange={(e) => setMethod(e.target.value)}>
                {['Cash', 'Card', 'JazzCash', 'Easypaisa'].map((m) => <option key={m}>{m}</option>)}
              </select>
              <span className="muted" style={{ fontSize: 13, marginLeft: 'auto' }}>Discount</span>
              <input type="number" min="0" value={discount} onChange={(e) => setDiscount(e.target.value)} style={{ width: 80 }} />
            </div>

            {/* Split-bill */}
            <div className="cat-title" style={{ fontSize: 13, marginTop: 4 }}>Split the bill</div>
            <div className="row" style={{ gap: 8, marginBottom: 8, flexWrap: 'wrap' }}>
              <span className="muted" style={{ fontSize: 13 }}>Evenly into</span>
              <input type="number" min="2" value={ways} onChange={(e) => setWays(Number(e.target.value))} style={{ width: 60 }} />
              <span className="muted" style={{ fontSize: 13 }}>= {money(share)}/each</span>
              <button disabled={busy || remaining <= 0} onClick={() => pay(share)}>Pay 1 share</button>
            </div>
            <div className="row" style={{ gap: 8, marginBottom: 8, flexWrap: 'wrap' }}>
              <input type="number" placeholder="Custom amount" value={amount} onChange={(e) => setAmount(e.target.value)} style={{ flex: 1, minWidth: 120 }} />
              <button disabled={busy || !amount || Number(amount) <= 0} onClick={() => pay(amount)}>Pay amount</button>
            </div>

            {err && <div style={{ color: 'var(--red)', fontSize: 14 }}>{err}</div>}
            <div className="row" style={{ gap: 8, marginTop: 6 }}>
              <button onClick={onClose}>Cancel</button>
              <button onClick={() => window.print()} disabled={!bill}>🖨️ Print bill</button>
              <button className="green" style={{ marginLeft: 'auto' }} disabled={busy || !bill?.hasOpenOrders} onClick={settle}>
                {busy ? 'Settling…' : bill?.amountPaid > 0 ? '💵 Settle rest' : '💵 Settle full'}
              </button>
            </div>
          </div>
        ) : (
          <div className="noprint row" style={{ marginTop: 14, gap: 8 }}>
            <div className="green" style={{ padding: '6px 10px', borderRadius: 8 }}>✅ Paid — {invoice.number}</div>
            <button style={{ marginLeft: 'auto' }} onClick={onClose}>Done</button>
            <button className="blue" onClick={() => window.print()}>🖨️ Reprint</button>
          </div>
        )}
      </div>
    </div>
  )
}

function Head({ o }) {
  return (
    <div className="row between">
      <div><b>{o.tableName}</b> · #{o.id}</div>
      <span className={'pill s-' + o.status}>{STATUS_LABEL[o.status] || o.status}</span>
    </div>
  )
}
function Lines({ o }) {
  return (
    <div style={{ margin: '8px 0' }}>
      {o.items.map((i, n) => (
        <div className="kv" key={n}>
          <span>{i.qty}× {i.name}{i.variantName ? ` (${i.variantName})` : ''}
            {i.modifiers?.length > 0 && <span className="muted"> · {i.modifiers.join(', ')}</span>}
            {i.note ? ` — ${i.note}` : ''}</span>
          <span className="muted">{money(i.price * i.qty)}</span>
        </div>
      ))}
      {o.customerNote && <div className="muted" style={{ fontSize: 13 }}>📝 {o.customerNote}</div>}
    </div>
  )
}
