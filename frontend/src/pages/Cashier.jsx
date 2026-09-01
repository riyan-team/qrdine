import { useEffect, useRef, useState } from 'react'
import { api, money, STATUS_LABEL } from '../api'
import { useStaffOrders } from '../useStaffOrders'
import { OrderActions } from '../ordering.jsx'
import { toast } from '../ui.jsx'
import { friendlyError } from '../errors'
import Receipt from './Receipt.jsx'
import { BellIcon, TableIcon, SearchIcon, PrintIcon, ReceiptIcon, KitchenIcon, CheckIcon, CashIcon, QrIcon, UserIcon, TrendIcon } from '../icons.jsx'

// Run a status action, surfacing any failure as a toast (never silently).
const run = (p) => Promise.resolve(p).catch((e) => toast.error(friendlyError(e)))

// Reception / cashier: approve new orders, then bill & settle each table.
export default function Cashier() {
  const orders = useStaffOrders()
  const [billFor, setBillFor] = useState(null)   // table code being billed
  const [detailFor, setDetailFor] = useState(null)   // order being viewed
  const [detailInit, setDetailInit] = useState(null)   // { mode, autoPrint } for the detail modal
  const [tab, setTab] = useState('live')
  const [sum, setSum] = useState(null)
  // Open the order slip in a given mode, optionally firing the print dialog at once.
  const openDetail = (o, mode = 'kitchen', autoPrint = false) => { setDetailInit({ mode, autoPrint }); setDetailFor(o) }

  const incoming = orders.filter((o) => o.status === 'Placed')
  const active = orders.filter((o) => o.status !== 'Placed')

  // Group active orders by table.
  const tables = {}
  for (const o of active) (tables[o.tableCode] ??= { code: o.tableCode, name: o.tableName, orders: [] }).orders.push(o)
  const tableList = Object.values(tables)

  // Live takings summary — reloads whenever the live orders change (a settle removes one).
  useEffect(() => { api.receptionDashboard('', 1, 1).then(setSum).catch(() => {}) }, [orders.length, billFor])

  return (
    <div className="wrap ops ops-center">
      <div className="h">Reception</div>
      <p className="muted">Approve new orders to send them to the kitchen. Bill and settle each table when the guests are done.</p>

      <div className="grid recept-kpis">
        <Kpi Icon={CashIcon} num={money(sum?.todaySales ?? 0)} label="Today's sales" />
        <Kpi Icon={ReceiptIcon} num={sum?.todayBills ?? 0} label="Today's bills" />
        <Kpi Icon={TableIcon} num={tableList.length} label="Pending tables" />
        <Kpi Icon={TrendIcon} num={money(sum?.totalSales ?? 0)} label="Total sales" />
      </div>

      <div className="seg noprint" style={{ marginTop: 14 }}>
        <button className={tab === 'live' ? 'on' : ''} onClick={() => setTab('live')}>Live floor</button>
        <button className={tab === 'history' ? 'on' : ''} onClick={() => setTab('history')}>Sales history</button>
      </div>

      {tab === 'history' && <ReceptionHistory />}
      {tab === 'live' && <>
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
      </>}

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
  const [discount, setDiscount] = useState('')       // '' → use the settings default
  const [tendered, setTendered] = useState('')
  const [ways, setWays] = useState(2)
  const [amount, setAmount] = useState('')
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')
  const timer = useRef(null)

  // Load the bill; an explicit discount override re-prices server-side (tax on net).
  const load = (d) => api.getBill(code, d).then((b) => {
    setBill(b)
    if (d == null && discount === '') setDiscount(b.discountEnabled ? String(b.discountAmount) : '')
  }).catch((e) => setErr(e.message))
  useEffect(() => { load() }, [code])

  // Re-price (debounced) when the cashier edits the discount.
  const onDiscount = (v) => {
    setDiscount(v)
    clearTimeout(timer.current)
    timer.current = setTimeout(() => load(Number(v) || 0), 300)
  }

  const settle = async () => {
    setBusy(true); setErr('')
    try {
      const inv = await api.settle(code, method, Number(discount) || 0, Number(tendered) || 0)
      setInvoice(inv)
      if (inv.receiptEnabled) setTimeout(() => window.print(), 300)
    } catch (e) { setErr(e.message) }
    setBusy(false)
  }

  const pay = async (amt) => {
    setBusy(true); setErr('')
    try {
      const res = await api.payPart(code, Number(amt), method)
      if (res.settled) { setInvoice(res.invoice); if (res.invoice.receiptEnabled) setTimeout(() => window.print(), 300) }
      else { await load(Number(discount) || null); setAmount('') }
    } catch (e) { setErr(e.message) }
    setBusy(false)
  }

  const total = bill?.total ?? 0
  const remaining = bill?.remaining ?? 0
  const share = ways > 0 ? Math.ceil((remaining / ways) * 100) / 100 : 0
  const isCash = /cash/i.test(method)
  const tenderedNum = Number(tendered) || 0
  const change = Math.max(0, tenderedNum - total)
  const short = isCash && tenderedNum > 0 && tenderedNum < total

  return (
    <div className="overlay" onClick={(e) => { if (e.target.className === 'overlay') onClose() }}>
      <div className="modal bill-modal">
        {/* Pre-settle: always show the working bill. Post-settle: show the receipt
            only when receipts are enabled (item 8: OFF → don't show/print). */}
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

            <div className="pay-grid">
              <label className="muted">Pay via</label>
              <select value={method} onChange={(e) => setMethod(e.target.value)}>
                {['Cash', 'Card', 'JazzCash', 'Easypaisa'].map((m) => <option key={m}>{m}</option>)}
              </select>

              {bill?.discountEnabled && <>
                <label className="muted">Discount (Rs)</label>
                <input type="number" min="0" value={discount} onChange={(e) => onDiscount(e.target.value)} />
              </>}

              {isCash && <>
                <label className="muted">Amount received</label>
                <input type="number" min="0" inputMode="decimal" placeholder={money(total)} value={tendered}
                  onChange={(e) => setTendered(e.target.value)} />
              </>}
            </div>

            {/* Live change / balance for cash */}
            {isCash && tenderedNum > 0 && (
              <div className={'change-box' + (short ? ' short' : '')}>
                <span>{short ? 'Still due' : 'Change to return'}</span>
                <b>{money(short ? total - tenderedNum : change)}</b>
              </div>
            )}

            {/* Split-bill */}
            <details className="split-wrap">
              <summary className="muted">Split the bill</summary>
              <div className="row" style={{ gap: 8, margin: '8px 0', flexWrap: 'wrap', alignItems: 'center' }}>
                <span className="muted" style={{ fontSize: 13 }}>Evenly into</span>
                <input type="number" min="2" value={ways} onChange={(e) => setWays(Number(e.target.value))} style={{ width: 60 }} />
                <span className="muted" style={{ fontSize: 13 }}>= {money(share)}/each</span>
                <button disabled={busy || remaining <= 0} onClick={() => pay(share)}>Pay 1 share</button>
              </div>
              <div className="row" style={{ gap: 8, flexWrap: 'wrap' }}>
                <input type="number" placeholder="Custom amount" value={amount} onChange={(e) => setAmount(e.target.value)} style={{ flex: 1, minWidth: 120 }} />
                <button disabled={busy || !amount || Number(amount) <= 0} onClick={() => pay(amount)}>Pay amount</button>
              </div>
            </details>

            {err && <div className="form-err" style={{ marginTop: 8 }}>{err}</div>}
            <div className="row" style={{ gap: 8, marginTop: 12 }}>
              <button onClick={onClose}>Cancel</button>
              <button onClick={() => window.print()} disabled={!bill}><PrintIcon size={15} /> Print bill</button>
              <button className="green" style={{ marginLeft: 'auto' }} disabled={busy || !bill?.hasOpenOrders || short} onClick={settle}>
                {busy ? 'Settling…' : <><CashIcon size={15} /> {bill?.amountPaid > 0 ? 'Settle rest' : 'Settle'}</>}
              </button>
            </div>
          </div>
        ) : (
          <div className="noprint" style={{ marginTop: 14 }}>
            <div className="paid-badge" style={{ marginBottom: 10 }}><CheckIcon size={15} /> Paid — {invoice.number}
              {invoice.changeGiven > 0 && <span style={{ marginLeft: 8 }}>· change {money(invoice.changeGiven)}</span>}</div>
            <div className="row" style={{ gap: 8, alignItems: 'center' }}>
              <button style={{ marginLeft: 'auto' }} onClick={onClose}>Done</button>
              {invoice.receiptEnabled && <button className="blue" onClick={() => window.print()}><PrintIcon size={15} /> Reprint</button>}
            </div>
          </div>
        )}
      </div>
    </div>
  )
}

function Kpi({ Icon, num, label }) {
  return (
    <div className="card stat">
      <div className="chip">{Icon && <Icon size={20} />}</div>
      <div className="num">{num}</div>
      <div className="lbl">{label}</div>
    </div>
  )
}

// Settled-bills history: searchable, paginated, with reprint. Data comes from the
// reception dashboard endpoint (gated to the reception's own permission).
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
          <input placeholder="Search bill #, table, cashier…" value={q} onChange={(e) => onSearch(e.target.value)} />
        </div>
      </div>
      <div className="table-scroll">
        <table className="data-table">
          <thead>
            <tr><th>Bill</th><th>Table</th><th>Payment</th><th>Total</th><th>Received</th><th>Change</th><th>Cashier</th><th>Time</th><th></th></tr>
          </thead>
          <tbody>
            {rows.map((b) => (
              <tr key={b.id}>
                <td>{b.number}</td>
                <td>{b.tableName}</td>
                <td>{b.paymentMethod}</td>
                <td>{money(b.total)}</td>
                <td>{b.amountTendered > 0 ? money(b.amountTendered) : '—'}</td>
                <td>{b.changeGiven > 0 ? money(b.changeGiven) : '—'}</td>
                <td className="muted">{b.cashier || '—'}</td>
                <td className="muted">{new Date(b.issuedAt).toLocaleString('en-PK', { dateStyle: 'short', timeStyle: 'short' })}</td>
                <td><button className="ghost" onClick={() => openInvoice(b.id)}><PrintIcon size={14} /></button></td>
              </tr>
            ))}
            {rows.length === 0 && <tr><td colSpan={9} className="empty-row">{err || 'No settled bills yet.'}</td></tr>}
          </tbody>
        </table>
      </div>
      <div className="pager">
        <button disabled={page <= 1} onClick={() => go(page - 1)}>← Prev</button>
        <span className="muted">Page {data?.page ?? 1} of {Math.max(1, data?.totalPages ?? 1)} · {data?.historyTotal ?? 0} bills</span>
        <button disabled={(data?.page ?? 1) >= (data?.totalPages ?? 1)} onClick={() => go(page + 1)}>Next →</button>
      </div>

      {view && (
        <div className="overlay" onClick={(e) => { if (e.target.className === 'overlay') setView(null) }}>
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
