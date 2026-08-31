import { useEffect, useState } from 'react'
import { api, money, STATUS_LABEL } from '../api'
import { useStaffOrders } from '../useStaffOrders'
import { OrderActions } from '../ordering.jsx'
import { toast } from '../ui.jsx'
import { friendlyError } from '../errors'
import Receipt from './Receipt.jsx'
import { BellIcon, TableIcon, SearchIcon, PrintIcon, ReceiptIcon, KitchenIcon, CheckIcon, CashIcon, QrIcon, UserIcon } from '../icons.jsx'

// Run a status action, surfacing any failure as a toast (never silently).
const run = (p) => Promise.resolve(p).catch((e) => toast.error(friendlyError(e)))

// Reception / cashier: approve new orders, then bill & settle each table.
export default function Cashier() {
  const orders = useStaffOrders()
  const [billFor, setBillFor] = useState(null)   // table code being billed
  const [detailFor, setDetailFor] = useState(null)   // order being viewed
  const [detailInit, setDetailInit] = useState(null)   // { mode, autoPrint } for the detail modal
  // Open the order slip in a given mode, optionally firing the print dialog at once.
  const openDetail = (o, mode = 'kitchen', autoPrint = false) => { setDetailInit({ mode, autoPrint }); setDetailFor(o) }

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

      <div className="cat-title"><BellIcon size={15} /> New — waiting for approval ({incoming.length})</div>
      <div className="grid cols">
        {incoming.map((o) => (
          <div className="card" key={o.id}>
            <Head o={o} /><Lines o={o} />
            <div className="row" style={{ gap: 6, marginTop: 8, flexWrap: 'wrap' }}>
              <button className="ghost" onClick={() => openDetail(o, 'bill', false)}><SearchIcon size={15} /> Details</button>
              <span style={{ marginLeft: 'auto' }} />
              <button className="ghost" title="Print kitchen ticket" onClick={() => openDetail(o, 'kitchen', true)}><KitchenIcon size={15} /> Kitchen</button>
              <button className="ghost" title="Print bill" onClick={() => openDetail(o, 'bill', true)}><ReceiptIcon size={15} /> Bill</button>
              <button className="ghost" title="Print kitchen ticket + bill together" onClick={() => openDetail(o, 'both', true)}><PrintIcon size={15} /> Both</button>
              <button className="green" onClick={() => run(api.approve(o.id))}><CheckIcon size={15} /> Approve</button>
            </div>
          </div>
        ))}
        {incoming.length === 0 && <div className="empty">No new orders.</div>}
      </div>

      <div className="cat-title" style={{ marginTop: 20 }}><TableIcon size={15} /> Occupied tables ({tableList.length})</div>
      <div className="grid cols">
        {tableList.map((t) => {
          const total = t.orders.reduce((s, o) => s + o.total, 0)
          return (
            <div className="card" key={t.code}>
              <div className="row between"><b>{t.name}</b><span className="muted">{t.orders.length} order(s)</span></div>
              <div style={{ margin: '8px 0' }}>
                {t.orders.map((o) => (
                  <div key={o.id} className="row between" style={{ fontSize: 13, padding: '2px 0' }}>
                    <span className="muted link-quiet" style={{ cursor: 'pointer' }} onClick={() => openDetail(o, 'bill', false)} title="View & print this order">
                      <SearchIcon size={13} /> #{o.id} · {o.items.reduce((s, i) => s + i.qty, 0)} items <SourceBadge source={o.source} />
                    </span>
                    <span className="row" style={{ gap: 6 }}>
                      <OrderActions o={o} />
                      <span className={'pill s-' + o.status}>{STATUS_LABEL[o.status]}</span>
                    </span>
                  </div>
                ))}
              </div>
              <div className="row between">
                <b>{money(total)}</b>
                <button className="blue" onClick={() => setBillFor(t.code)}><ReceiptIcon size={15} /> Bill &amp; settle</button>
              </div>
            </div>
          )
        })}
        {tableList.length === 0 && <div className="empty">No occupied tables. Approved orders appear here by table.</div>}
      </div>

      {billFor && <BillModal code={billFor} onClose={() => setBillFor(null)} />}
      {detailFor && <OrderDetail order={detailFor} init={detailInit} onClose={() => { setDetailFor(null); setDetailInit(null) }} />}
    </div>
  )
}

// One printable slip — a kitchen ticket (big items, no prices) or a receipt (with
// prices + total). `brk` starts it on a fresh page so a thermal printer cuts here.
function Slip({ o, kind, brk }) {
  const isKitchen = kind === 'kitchen'
  return (
    <div className={'receipt' + (brk ? ' slip-break' : '')}>
      <div className="r-h">
        <div className="r-name">{isKitchen ? 'KITCHEN' : 'Receipt'} · #{o.id}</div>
        <div style={{ fontSize: 12 }}>{o.tableName}{o.branchName ? ` · ${o.branchName}` : ''}</div>
        <div style={{ fontSize: 12 }}>{new Date(o.createdAt).toLocaleString()}</div>
      </div>
      {o.items.map((i, n) => (
        <div className="r-line" key={n} style={isKitchen ? { fontSize: 16, fontWeight: 700 } : {}}>
          <span>{i.qty}× {i.name}{i.variantName ? ` (${i.variantName})` : ''}
            {i.modifiers?.length ? ` · ${i.modifiers.join(', ')}` : ''}{i.note ? ` — ${i.note}` : ''}</span>
          {!isKitchen && <span>{money(i.price * i.qty)}</span>}
        </div>
      ))}
      {o.customerNote && <div style={{ fontSize: 12, marginTop: 6 }}>Note: {o.customerNote}</div>}
      {!isKitchen && <div className="r-tot r-line r-grand"><span>Total</span><span>{money(o.total)}</span></div>}
      <div className="r-foot">{o.waiterName ? `Waiter: ${o.waiterName} · ` : ''}{STATUS_LABEL[o.status] || o.status}{isKitchen && o.etaMinutes ? ` · ETA ${o.etaMinutes} min` : ''}</div>
    </div>
  )
}

// A running order, with buttons to print a kitchen ticket, a receipt, or BOTH in
// one go (two slips, auto-cut between). Printing never changes the order — it stays
// in the list so it can be reprinted any time. `init` may pre-select the view and
// auto-fire the print dialog (used from Reception's quick-print buttons).
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
            <span style={{ marginLeft: 'auto' }} />
            <button className="blue" onClick={() => doPrint(mode)}><PrintIcon size={15} /> Print {mode === 'both' ? 'both' : mode === 'bill' ? 'receipt' : 'kitchen'}</button>
            <button className="green" onClick={() => doPrint('both')}><PrintIcon size={15} /> Print both</button>
          </div>
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
              <button onClick={() => window.print()} disabled={!bill}><PrintIcon size={15} /> Print bill</button>
              <button className="green" style={{ marginLeft: 'auto' }} disabled={busy || !bill?.hasOpenOrders} onClick={settle}>
                {busy ? 'Settling…' : <><CashIcon size={15} /> {bill?.amountPaid > 0 ? 'Settle rest' : 'Settle full'}</>}
              </button>
            </div>
          </div>
        ) : (
          <div className="noprint row" style={{ marginTop: 14, gap: 8, alignItems: 'center' }}>
            <div className="paid-badge"><CheckIcon size={15} /> Paid — {invoice.number}</div>
            <button style={{ marginLeft: 'auto' }} onClick={onClose}>Done</button>
            <button className="blue" onClick={() => window.print()}><PrintIcon size={15} /> Reprint</button>
          </div>
        )}
      </div>
    </div>
  )
}

// Where the order came from — a self-service QR scan or a waiter taking it.
function SourceBadge({ source }) {
  const waiter = source === 'Waiter'
  return (
    <span className="pill" style={{ background: 'var(--surface2)', fontSize: 11, display: 'inline-flex', alignItems: 'center', gap: 4 }}
      title={waiter ? 'Taken by a waiter' : 'Self-ordered via table QR'}>
      {waiter ? <><UserIcon size={12} /> Waiter</> : <><QrIcon size={12} /> QR</>}
    </span>
  )
}

function Head({ o }) {
  return (
    <div className="row between">
      <div><b>{o.tableName}</b> · #{o.id} <SourceBadge source={o.source} /></div>
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
      {o.customerNote && <div className="muted" style={{ fontSize: 13 }}>Note: {o.customerNote}</div>}
    </div>
  )
}
