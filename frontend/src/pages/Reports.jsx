import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api, count, money } from '../api'
import { toast } from '../ui.jsx'
import { friendlyError } from '../errors'
import {
  DataTable, Empty, ExportButtons, Figure, Loading, PageHead, Problem, addDays, downloadCsv, fmtDate, monthEnd, monthStart, printPage,
  todayPk, useData, weekStart,
} from '../backoffice.jsx'
import Receipt from './Receipt.jsx'
import { DownloadIcon, PrintIcon, SearchIcon, StarIcon } from '../icons.jsx'

const PRESETS = [
  { key: 'today', label: 'Today', range: (t) => [t, t] },
  { key: 'yesterday', label: 'Yesterday', range: (t) => [addDays(t, -1), addDays(t, -1)] },
  { key: 'week', label: 'This week', range: (t) => [weekStart(t), t] },
  { key: 'month', label: 'This month', range: (t) => [monthStart(t), t] },
  { key: 'last', label: 'Last month', range: (t) => { const end = addDays(monthStart(t), -1); return [monthStart(end), monthEnd(end)] } },
  { key: 'custom', label: 'Custom' },
]

const SECTIONS = [
  { key: 'overview', label: 'Overview' },
  { key: 'sales', label: 'Sales' },
  { key: 'bills', label: 'All bills' },
  { key: 'cash', label: 'Payments & cash' },
  { key: 'staff', label: 'Staff & tables' },
  { key: 'losses', label: 'Cancelled & discounts' },
  { key: 'costs', label: 'Expenses & khata' },
  { key: 'stock', label: 'Stock' },
  { key: 'ratings', label: 'Ratings' },
  { key: 'tax', label: 'Tax' },
]

// ---- table columns ----
const moneyCol = (key, label) => ({ key, label, num: true, render: (r) => money(r[key]), csv: (r) => r[key], print: (r) => money(r[key]) })
const numCol = (key, label) => ({ key, label, num: true })
const col = (key, label) => ({ key, label })
const rowColumns = (nameLabel, countLabel, amountLabel = 'Amount') => [col('name', nameLabel), numCol('count', countLabel), moneyCol('amount', amountLabel)]
const dateCol = { key: 'date', label: 'Date', render: (r) => fmtDate(r.date), csv: (r) => r.date, print: (r) => fmtDate(r.date) }
const sumOf = (rows, key) => rows.reduce((s, r) => s + (r[key] || 0), 0)
const rowTotal = (rows) => ({ count: sumOf(rows, 'count'), amount: money(sumOf(rows, 'amount')) })

// ---- charts ----
function useWidth() {
  const ref = useRef(null)
  const [width, setWidth] = useState(640)
  useLayoutEffect(() => {
    const el = ref.current
    if (!el) return undefined
    setWidth(el.clientWidth)
    const ro = new ResizeObserver(([e]) => setWidth(Math.round(e.contentRect.width)))
    ro.observe(el)
    return () => ro.disconnect()
  }, [])
  return [ref, width]
}

const niceMax = (v) => {
  if (v <= 0) return 100
  const p = 10 ** Math.floor(Math.log10(v))
  const m = v / p
  return (m <= 1 ? 1 : m <= 2 ? 2 : m <= 5 ? 5 : 10) * p
}
const axisLabel = (v, top) => (top >= 10000 ? `${Math.round(v / 1000)}K` : Math.round(v).toLocaleString('en-PK'))

function Tip({ tip }) {
  if (!tip) return null
  const left = Math.min(tip.x + 14, window.innerWidth - 190)
  return (
    <div className="rp-tip" style={{ left, top: tip.y - 46 }}>
      <b>{tip.value}</b> <span style={{ opacity: 0.75 }}>{tip.label}</span>
    </div>
  )
}

// One line, one colour. The pointer only has to find the day: the crosshair snaps to it.
export function TrendChart({ rows, valueKey = 'sales', label = 'sales' }) {
  const [ref, width] = useWidth()
  const [at, setAt] = useState(null)
  const [tip, setTip] = useState(null)
  const H = 210; const M = { l: 44, r: 12, t: 12, b: 24 }
  const innerW = Math.max(40, width - M.l - M.r); const innerH = H - M.t - M.b
  const top = niceMax(Math.max(...rows.map((r) => r[valueKey]), 0))
  const x = (i) => M.l + (rows.length === 1 ? innerW / 2 : (i / (rows.length - 1)) * innerW)
  const y = (v) => M.t + innerH - (v / top) * innerH
  const line = rows.map((r, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)} ${y(r[valueKey]).toFixed(1)}`).join(' ')
  const area = `${line} L${x(rows.length - 1).toFixed(1)} ${M.t + innerH} L${x(0).toFixed(1)} ${M.t + innerH} Z`
  const every = Math.max(1, Math.ceil(rows.length / Math.max(2, Math.floor(innerW / 76))))
  const show = (i, cx, cy) => {
    setAt(i)
    const r = rows[i]
    setTip({ x: cx, y: cy, value: money(r[valueKey]), label: `${fmtDate(r.date)} · ${count(r.bills, 'bill')}` })
  }
  const onMove = (e) => {
    const box = e.currentTarget.getBoundingClientRect()
    const i = Math.round(((e.clientX - box.left - M.l) / innerW) * (rows.length - 1))
    show(Math.max(0, Math.min(rows.length - 1, i)), e.clientX, e.clientY)
  }
  const onKey = (e) => {
    if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return
    e.preventDefault()
    const i = Math.max(0, Math.min(rows.length - 1, (at ?? rows.length - 1) + (e.key === 'ArrowLeft' ? -1 : 1)))
    const box = e.currentTarget.getBoundingClientRect()
    show(i, box.left + x(i), box.top + y(rows[i][valueKey]))
  }
  return (
    <div ref={ref}>
      <svg className="rp-chart" width={width} height={H} tabIndex={0} role="img" aria-label={`${label} by day, from ${fmtDate(rows[0].date)} to ${fmtDate(rows[rows.length - 1].date)}`}
        onPointerMove={onMove} onPointerLeave={() => { setAt(null); setTip(null) }} onKeyDown={onKey} onBlur={() => { setAt(null); setTip(null) }}>
        {[0, top / 2, top].map((t) => (
          <g key={t}>
            <line x1={M.l} x2={width - M.r} y1={y(t)} y2={y(t)} stroke="var(--line)" />
            <text x={M.l - 7} y={y(t) + 4} textAnchor="end">{axisLabel(t, top)}</text>
          </g>
        ))}
        {rows.map((r, i) => (i % every === 0 ? <text key={r.date} x={x(i)} y={H - 6} textAnchor={i === 0 ? 'start' : 'middle'}>{fmtDate(r.date).replace(/ \d{4}$/, '')}</text> : null))}
        <path d={area} fill="var(--accent)" opacity=".1" />
        <path d={line} fill="none" stroke="var(--accent)" strokeWidth="2" strokeLinejoin="round" strokeLinecap="round" />
        {at != null && <line x1={x(at)} x2={x(at)} y1={M.t} y2={M.t + innerH} stroke="var(--line2)" />}
        <circle cx={x(at ?? rows.length - 1)} cy={y(rows[at ?? rows.length - 1][valueKey])} r="4.5" fill="var(--accent)" stroke="var(--surface)" strokeWidth="2" />
      </svg>
      <Tip tip={tip} />
    </div>
  )
}

// When the place is busy. Every hour between the first and last bill gets a slot, so a quiet hour shows as a gap.
function HoursChart({ rows }) {
  const [ref, width] = useWidth()
  const [tip, setTip] = useState(null)
  const [hot, setHot] = useState(null)
  const filled = useMemo(() => {
    if (!rows.length) return []
    const by = new Map(rows.map((r) => [r.hour, r]))
    const out = []
    for (let h = rows[0].hour; h <= rows[rows.length - 1].hour; h++) {
      const label = `${h % 12 || 12}${h < 12 ? 'am' : 'pm'}`
      out.push(by.get(h) ? { ...by.get(h), label } : { hour: h, label, bills: 0, sales: 0 })
    }
    return out
  }, [rows])
  if (!filled.length) return null
  const H = 200; const M = { l: 44, r: 6, t: 10, b: 24 }
  const innerW = Math.max(40, width - M.l - M.r); const innerH = H - M.t - M.b
  const top = niceMax(Math.max(...filled.map((r) => r.sales)))
  const band = innerW / filled.length
  const bar = Math.max(4, Math.min(24, band - 3))
  const y = (v) => M.t + innerH - (v / top) * innerH
  const every = Math.max(1, Math.ceil(filled.length / Math.max(2, Math.floor(innerW / 42))))
  return (
    <div ref={ref}>
      <svg className="rp-chart" width={width} height={H} role="group" aria-label="Sales by hour">
        {[0, top / 2, top].map((t) => (
          <g key={t}>
            <line x1={M.l} x2={width - M.r} y1={y(t)} y2={y(t)} stroke="var(--line)" />
            <text x={M.l - 7} y={y(t) + 4} textAnchor="end">{axisLabel(t, top)}</text>
          </g>
        ))}
        {filled.map((r, i) => {
          const cx = M.l + band * i + band / 2
          const barH = (r.sales / top) * innerH
          const left = cx - bar / 2; const base = M.t + innerH
          const radius = Math.min(4, barH, bar / 2)
          const d = `M${left} ${base}V${base - barH + radius}Q${left} ${base - barH} ${left + radius} ${base - barH}H${left + bar - radius}Q${left + bar} ${base - barH} ${left + bar} ${base - barH + radius}V${base}Z`
          const say = (e) => { setHot(i); setTip({ x: e.clientX, y: e.clientY, value: money(r.sales), label: `${r.label} · ${count(r.bills, 'bill')}` }) }
          return (
            <g key={r.hour}>
              {r.sales > 0 && <path d={d} fill="var(--accent)" opacity={hot === null || hot === i ? 1 : 0.55} />}
              {i % every === 0 && <text x={cx} y={H - 6} textAnchor="middle">{r.label}</text>}
              <rect x={M.l + band * i} y={M.t} width={band} height={innerH} fill="transparent" tabIndex={0} aria-label={`${r.label}: ${money(r.sales)}, ${count(r.bills, 'bill')}`}
                onPointerMove={say} onPointerLeave={() => { setHot(null); setTip(null) }}
                onFocus={(e) => { const b = e.target.getBoundingClientRect(); say({ clientX: b.left, clientY: b.top + 40 }) }} onBlur={() => { setHot(null); setTip(null) }} />
            </g>
          )
        })}
      </svg>
      <Tip tip={tip} />
    </div>
  )
}

// Ranked bars with the number at the tip. Most tables here read better as this than as a pie.
export function RankedBars({ rows, unit = '', top = 8 }) {
  const sorted = [...rows].sort((a, b) => b.amount - a.amount)
  const shown = sorted.slice(0, top)
  const rest = sorted.slice(top)
  const list = rest.length ? [...shown, { name: `${rest.length} more`, count: sumOf(rest, 'count'), amount: sumOf(rest, 'amount'), isRest: true }] : shown
  const max = Math.max(...list.map((r) => r.amount), 1)
  return (
    <div className="rp-bars">
      {list.map((r) => (
        <div className="rp-bar-row" key={r.name} title={`${r.name}: ${money(r.amount)}${r.count ? `, ${r.count}${unit ? ` ${unit}` : ''}` : ''}`}>
          <span className="rp-bar-name">{r.name}</span>
          <span className="rp-bar-track">
            <span className="rp-bar-fill" style={{ width: `${Math.max(r.amount > 0 ? 1 : 0, (r.amount / max) * 100)}%`, background: r.isRest ? 'var(--line2)' : undefined }} />
          </span>
          <span className="rp-bar-val">{money(r.amount)}<small>{r.count}{unit ? ` ${unit}` : ''}</small></span>
        </div>
      ))}
    </div>
  )
}

// A report: an optional picture, then the full table underneath, and a way to take it away.
function Block({ title, note, file, columns, rows, total, chart, empty, range }) {
  return (
    <section className="bo-card rp-block">
      <div className="bo-card-head">
        <div>
          <h2>{title}</h2>
          {note && <div className="bo-small">{note}</div>}
        </div>
        {rows.length > 0 && (
          <ExportButtons onCsv={() => downloadCsv(`${file}_${range.from}_${range.to}`, columns, rows)}
            onPrint={() => printPage({ title, subtitle: range.text, sections: [{ note, columns, rows, total }] })} />
        )}
      </div>
      {rows.length > 0 && chart && <div className="rp-chart-wrap">{chart}</div>}
      <DataTable columns={columns} rows={rows} total={total} empty={empty} />
    </section>
  )
}

// Which reports each heading holds. Written as data so "print all" prints exactly what's on screen.
function blocksFor(section, d) {
  const dayColumns = [dateCol, numCol('bills', 'Bills'), moneyCol('sales', 'Sales'), moneyCol('tax', 'Tax'), moneyCol('discounts', 'Discounts'), moneyCol('expenses', 'Expenses'), moneyCol('purchases', 'Supplier bills')]
  switch (section) {
    case 'sales': return [
      { title: 'Sales by day', file: 'sales_by_day', columns: dayColumns, rows: d.daily, empty: 'No sales in this period.', chart: d.daily.length > 1 ? <TrendChart rows={d.daily} /> : null,
        total: { bills: sumOf(d.daily, 'bills'), sales: money(sumOf(d.daily, 'sales')), tax: money(sumOf(d.daily, 'tax')), discounts: money(sumOf(d.daily, 'discounts')), expenses: money(sumOf(d.daily, 'expenses')), purchases: money(sumOf(d.daily, 'purchases')) } },
      { title: 'Sales by dish', file: 'sales_by_dish', columns: rowColumns('Dish', 'Sold'), rows: d.items, total: rowTotal(d.items), empty: 'Nothing sold in this period.', chart: <RankedBars rows={d.items} unit="sold" /> },
      { title: 'Sales by category', file: 'sales_by_category', columns: rowColumns('Category', 'Sold'), rows: d.categories, total: rowTotal(d.categories), empty: 'Nothing sold in this period.', chart: <RankedBars rows={d.categories} unit="sold" /> },
      { title: 'Busiest hours', file: 'sales_by_hour', columns: [col('label', 'Hour'), numCol('bills', 'Bills'), moneyCol('sales', 'Sales')], rows: d.hours, empty: 'No sales in this period.', chart: <HoursChart rows={d.hours} /> },
    ]
    case 'cash': return [
      { title: 'Payment methods', file: 'payment_methods', columns: rowColumns('Paid by', 'Payments'), rows: d.payments, total: rowTotal(d.payments), empty: 'No payments in this period.', chart: <RankedBars rows={d.payments} unit="paid" /> },
      { title: 'Cash for this period', file: 'cash', note: 'For the night-by-night count, use Day closing.', columns: [col('name', 'Item'), moneyCol('amount', 'Amount')], rows: [
        { name: 'Cash sales', amount: d.closing.cashSales }, { name: 'Cash received on khata', amount: d.closing.cashKhataReceipts },
        { name: 'Cash expenses', amount: -d.closing.cashExpenses }, { name: 'Cash paid to suppliers', amount: -d.closing.cashSupplierPayments },
      ], total: { amount: money(d.closing.cashInHand) } },
      { title: 'Table, parcel or delivery', file: 'by_channel', columns: rowColumns('Type', 'Bills'), rows: d.channels, total: rowTotal(d.channels), empty: 'No bills in this period.', chart: <RankedBars rows={d.channels} unit="bills" /> },
      { title: 'Where orders came from', file: 'by_source', note: 'Guests scanning the QR code, or staff taking the order.', columns: rowColumns('Source', 'Orders'), rows: d.sources, total: rowTotal(d.sources), empty: 'No orders in this period.' },
    ]
    case 'staff': return [
      { title: 'By cashier', file: 'by_cashier', columns: rowColumns('Cashier', 'Bills'), rows: d.cashiers, total: rowTotal(d.cashiers), empty: 'No bills in this period.' },
      { title: 'By waiter', file: 'by_waiter', columns: rowColumns('Waiter', 'Orders'), rows: d.waiters, total: rowTotal(d.waiters), empty: 'No waiter orders in this period.' },
      { title: 'By rider', file: 'by_rider', note: `Delivery charges collected: ${money(d.deliveryCharges)}`, columns: rowColumns('Rider', 'Deliveries'), rows: d.riders, total: rowTotal(d.riders), empty: 'No deliveries in this period.' },
      { title: 'By table', file: 'by_table', columns: rowColumns('Table', 'Bills'), rows: d.tables, total: rowTotal(d.tables), empty: 'No table bills in this period.' },
      ...(d.branches.length > 1 ? [{ title: 'By branch', file: 'by_branch', columns: rowColumns('Branch', 'Bills'), rows: d.branches, total: rowTotal(d.branches) }] : []),
    ]
    case 'losses': return [
      { title: 'Cancelled orders', file: 'cancelled', columns: [col('when', 'When'), col('table', 'Table'), col('items', 'Items'), col('source', 'Source'), col('reason', 'Reason'), moneyCol('value', 'Value')], rows: d.cancelled, total: { value: money(sumOf(d.cancelled, 'value')) }, empty: 'Nothing cancelled in this period.' },
      { title: 'Items removed from orders', file: 'voids', columns: [col('when', 'When'), col('table', 'Table'), col('item', 'Item'), numCol('qty', 'Qty'), col('reason', 'Reason'), col('by', 'By'), moneyCol('value', 'Value')], rows: d.voids, total: { value: money(sumOf(d.voids, 'value')) }, empty: 'Nothing removed in this period.' },
      { title: 'Discounts given', file: 'discounts', columns: [col('number', 'Bill'), col('when', 'When'), col('table', 'Table'), col('cashier', 'Cashier'), moneyCol('before', 'Before'), moneyCol('discount', 'Discount'), moneyCol('total', 'Paid')], rows: d.discounts, total: { before: money(sumOf(d.discounts, 'before')), discount: money(sumOf(d.discounts, 'discount')), total: money(sumOf(d.discounts, 'total')) }, empty: 'No discounts in this period.' },
    ]
    case 'costs': return [
      { title: 'Expenses by category', file: 'expenses', columns: rowColumns('Category', 'Entries'), rows: d.expensesByCategory, total: rowTotal(d.expensesByCategory), empty: 'No expenses in this period.', chart: <RankedBars rows={d.expensesByCategory} unit="entries" /> },
      { title: 'Supplier balances', file: 'suppliers', note: 'Bought and paid in this period. The balance is everything you owe today.', columns: [col('name', 'Supplier'), col('phone', 'Mobile'), moneyCol('bought', 'Bought'), moneyCol('paid', 'Paid'), moneyCol('balance', 'You owe')], rows: d.suppliers, total: { bought: money(sumOf(d.suppliers, 'bought')), paid: money(sumOf(d.suppliers, 'paid')), balance: money(d.supplierBalance) }, empty: 'No suppliers yet.' },
      { title: 'Customer khata', file: 'customers', note: 'Billed and received in this period. The balance is everything they owe today.', columns: [col('name', 'Customer'), col('phone', 'Mobile'), moneyCol('charged', 'Billed'), moneyCol('paid', 'Received'), moneyCol('balance', 'Owes')], rows: d.customers, total: { charged: money(sumOf(d.customers, 'charged')), paid: money(sumOf(d.customers, 'paid')), balance: money(d.customerBalance) }, empty: 'No khata customers yet.' },
    ]
    case 'stock': return [
      { title: 'Stock now', file: 'stock', note: `Worth about ${money(d.stockValue)} at your last buying price.`, columns: [col('name', 'Item'), { key: 'quantity', label: 'In stock', num: true, render: (r) => `${r.quantity} ${r.unit}`, print: (r) => `${r.quantity} ${r.unit}`, csv: (r) => r.quantity }, col('unit', 'Unit'), numCol('lowAt', 'Low at'), moneyCol('lastCost', 'Last cost'), moneyCol('value', 'Value'), { key: 'isLow', label: 'Status', render: (r) => (r.isLow ? 'Low' : 'OK'), print: (r) => (r.isLow ? 'Low' : 'OK'), csv: (r) => (r.isLow ? 'Low' : 'OK') }], rows: d.stock, total: { value: money(d.stockValue) }, empty: 'No stock items yet.' },
      { title: 'Stock movements and waste', file: 'stock_movements', columns: [col('when', 'When'), col('item', 'Item'), { key: 'change', label: 'Change', num: true, render: (r) => `${r.change > 0 ? '+' : ''}${r.change} ${r.unit}`, print: (r) => `${r.change > 0 ? '+' : ''}${r.change} ${r.unit}`, csv: (r) => r.change }, col('type', 'Type'), col('reason', 'Reason')], rows: d.movements, empty: 'No stock moved in this period.' },
    ]
    case 'tax': return [
      { title: 'Tax by day', file: 'tax_by_day', note: 'Sales here include tax. Discounts are already taken off.', columns: [dateCol, numCol('bills', 'Bills'), moneyCol('sales', 'Sales'), moneyCol('tax', 'Tax')], rows: d.daily, total: { bills: sumOf(d.daily, 'bills'), sales: money(sumOf(d.daily, 'sales')), tax: money(sumOf(d.daily, 'tax')) }, empty: 'No bills in this period.' },
    ]
    case 'ratings': return [
      { title: 'What guests said', file: 'ratings', columns: [col('when', 'When'), col('table', 'Table'), numCol('rating', 'Stars'), col('comment', 'Comment')], rows: d.ratings.comments, empty: 'No comments in this period.' },
    ]
    default: return []
  }
}

// Every report in one place, all reading from the same dates and the same numbers.
export default function Reports() {
  const [params, setParams] = useSearchParams()
  const section = SECTIONS.some((s) => s.key === params.get('s')) ? params.get('s') : 'overview'
  const today = todayPk()
  const [preset, setPreset] = useState('today')
  const [custom, setCustom] = useState([monthStart(today), today])
  const [branchId, setBranchId] = useState('')
  const [from, to] = preset === 'custom' ? custom : PRESETS.find((p) => p.key === preset).range(today)
  const { data: branches } = useData(() => api.listBranches().catch(() => []), [])
  const { data, error, loading, reload } = useData(() => api.businessReport({ from, to, branchId: branchId || null }), [from, to, branchId])
  const range = { from, to, text: `${data?.branch ? `${data.branch}. ` : ''}${from === to ? fmtDate(from) : `${fmtDate(from)} to ${fmtDate(to)}`}` }
  const go = (key) => setParams(key === 'overview' ? {} : { s: key }, { replace: true })

  const blocks = data ? blocksFor(section, data) : []
  const printAll = () => printPage({ title: SECTIONS.find((s) => s.key === section).label, subtitle: range.text, sections: blocks.filter((b) => b.rows.length) })
  const invoices = () => api.downloadInvoicesCsv(from, to, branchId || null).catch((e) => toast.error(friendlyError(e)))

  return (
    <div className="bo-wrap">
      <PageHead title="Reports" sub="Pick the dates once. Every number below uses them." />

      <div className="rp-toolbar noprint">
        <div className="bo-chips" style={{ marginBottom: 0 }}>
          {PRESETS.map((p) => <button key={p.key} className={`bo-chip${preset === p.key ? ' on' : ''}`} onClick={() => setPreset(p.key)}>{p.label}</button>)}
        </div>
        {preset === 'custom' && (
          <span className="rp-dates">
            <input type="date" max={custom[1]} value={custom[0]} aria-label="From" onChange={(e) => e.target.value && setCustom([e.target.value, custom[1]])} />
            <span className="bo-small">to</span>
            <input type="date" min={custom[0]} max={today} value={custom[1]} aria-label="To" onChange={(e) => e.target.value && setCustom([custom[0], e.target.value])} />
          </span>
        )}
        {branches?.length > 1 && (
          <select value={branchId} aria-label="Branch" onChange={(e) => setBranchId(e.target.value)}>
            <option value="">All branches</option>
            {branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
          </select>
        )}
      </div>

      <nav className="rp-nav noprint" aria-label="Reports">
        {SECTIONS.map((s) => <button key={s.key} className={section === s.key ? 'on' : ''} aria-current={section === s.key ? 'page' : undefined} onClick={() => go(s.key)}>{s.label}</button>)}
      </nav>

      <Problem onRetry={reload}>{error}</Problem>
      {loading && !data && <Loading />}
      {data && (
        <div style={{ opacity: loading ? 0.55 : 1, transition: 'opacity .2s' }}>
          {section === 'overview' && <Overview d={data} go={go} />}
          {section === 'bills' && <AllBills from={from} to={to} branchId={branchId} />}
          {section === 'ratings' && <RatingSummary r={data.ratings} />}
          {section === 'tax' && (
            <div className="bo-figs">
              <Figure tone="lead" label="Tax collected" value={money(data.summary.tax)} />
              <Figure label="Sales before tax" value={money(data.summary.salesBeforeTax)} />
              <Figure label="Service charge" value={money(data.summary.serviceCharge)} />
              <Figure label="Discounts" value={money(data.summary.discounts)} />
            </div>
          )}
          {section !== 'overview' && section !== 'bills' && (
            <div className="row noprint" style={{ gap: 8, justifyContent: 'flex-end', marginBottom: 10, flexWrap: 'wrap' }}>
              {section === 'tax' && <button className="btn-sm" onClick={invoices}><DownloadIcon size={15} /> All bills for your accountant</button>}
              {blocks.some((b) => b.rows.length) && <button className="ghost btn-sm" onClick={printAll}><PrintIcon size={15} /> Print this page</button>}
            </div>
          )}
          {blocks.map((b) => <Block key={b.file} {...b} range={range} />)}
        </div>
      )}
    </div>
  )
}

// Every settled bill, searchable, with its tax. The download holds exactly what's filtered here.
function AllBills({ from, to, branchId }) {
  const [q, setQ] = useState('')
  const [method, setMethod] = useState('')
  const [page, setPage] = useState(1)
  const [data, setData] = useState(null)
  const [err, setErr] = useState('')
  const [dl, setDl] = useState(false)
  const [view, setView] = useState(null)
  const timer = useRef(null)
  const load = (p = 1, text = q) => api.salesReport({ from, to, branchId: branchId || null, q: text, method, page: p, pageSize: 15 })
    .then((d) => { setData(d); setErr('') }).catch((e) => setErr(friendlyError(e)))
  useEffect(() => { setPage(1); load(1) }, [from, to, branchId, method])
  const onSearch = (v) => { setQ(v); setPage(1); clearTimeout(timer.current); timer.current = setTimeout(() => load(1, v), 300) }
  const go = (p) => { setPage(p); load(p) }
  const download = async () => {
    setDl(true)
    try { await api.downloadInvoicesCsv(from, to, branchId || null, q, method) } catch (e) { toast.error(friendlyError(e)) }
    setDl(false)
  }
  const open = async (id) => { try { setView(await api.invoice(id)) } catch (e) { toast.error(friendlyError(e)) } }
  const rows = data?.rows ?? []
  return (
    <section className="bo-card rp-block">
      <div className="bo-toolbar">
        <div className="search-box grow"><SearchIcon size={15} /><input placeholder="Search bill number, table or cashier" value={q} onChange={(e) => onSearch(e.target.value)} /></div>
        <select value={method} aria-label="Paid by" onChange={(e) => setMethod(e.target.value)}>
          <option value="">All payments</option>
          {['Cash', 'Card', 'JazzCash', 'Easypaisa', 'Khata'].map((m) => <option key={m}>{m}</option>)}
        </select>
        <button className="btn-sm" disabled={dl} onClick={download}><DownloadIcon size={15} /> {dl ? 'Preparing…' : 'Excel'}</button>
      </div>
      <div className="table-scroll bo-table">
        <table className="data-table">
          <thead><tr><th>Bill</th><th>When</th><th>Table</th><th>Paid by</th><th>Cashier</th><th className="num">Before tax</th><th className="num">Discount</th><th className="num">Tax</th><th className="num">Total</th><th></th></tr></thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id}>
                <td>{r.number}</td>
                <td>{new Date(r.issuedAt).toLocaleString('en-PK', { dateStyle: 'short', timeStyle: 'short' })}</td>
                <td>{r.table}</td>
                <td>{r.paymentMethod}</td>
                <td>{r.cashier || '-'}</td>
                <td className="num">{money(r.subtotal)}</td>
                <td className="num">{r.discount > 0 ? money(r.discount) : '-'}</td>
                <td className="num">{money(r.tax)}</td>
                <td className="num"><b>{money(r.total)}</b></td>
                <td><button className="ghost btn-sm" onClick={() => open(r.id)}><PrintIcon size={14} /> View</button></td>
              </tr>
            ))}
            {rows.length === 0 && <tr><td colSpan={10} className="empty-row">{err || 'No bills in this period.'}</td></tr>}
          </tbody>
          {rows.length > 0 && (
            <tfoot><tr><td colSpan={5}>Total, {data.total} bills</td><td className="num">{money(data.sumNet)}</td><td className="num">{money(data.sumDiscount)}</td><td className="num">{money(data.sumTax)}</td><td className="num">{money(data.sumSales)}</td><td /></tr></tfoot>
          )}
        </table>
      </div>
      <div className="pager">
        <button disabled={(data?.page ?? 1) <= 1} onClick={() => go(page - 1)}>Previous</button>
        <span className="bo-small">Page {data?.page ?? 1} of {Math.max(1, data?.totalPages ?? 1)}</span>
        <button disabled={(data?.page ?? 1) >= (data?.totalPages ?? 1)} onClick={() => go(page + 1)}>Next</button>
      </div>
      {view && (
        <div className="overlay" onMouseDown={(e) => { if (e.target === e.currentTarget) setView(null) }}>
          <div className="modal">
            <Receipt invoice={view} />
            <div className="noprint row" style={{ marginTop: 12, gap: 8 }}>
              <button onClick={() => setView(null)}>Close</button>
              <button className="blue" style={{ marginLeft: 'auto' }} onClick={() => window.print()}><PrintIcon size={15} /> Print</button>
            </div>
          </div>
        </div>
      )}
    </section>
  )
}

function Overview({ d, go }) {
  const s = d.summary
  if (s.bills === 0 && s.expenses === 0 && s.purchases === 0) {
    return <div className="bo-card"><Empty title="Nothing in this period yet">Bills, expenses and supplier bills appear here as soon as they're recorded. Try This month.</Empty></div>
  }
  return <>
    <div className="bo-figs">
      <Figure tone="lead" label="Sales" value={money(s.sales)} hint={`${count(s.bills, 'bill')}, average ${money(s.averageBill)}`} />
      <Figure tone={s.estimatedProfit < 0 ? 'bad' : 'good'} label="Estimated profit" value={money(s.estimatedProfit)} hint="Sales less tax, expenses and supplier bills" />
      <Figure label="Expenses" value={money(s.expenses)} />
      <Figure label="Supplier bills" value={money(s.purchases)} hint={`Paid ${money(s.supplierPayments)}`} />
      <Figure label="Cash in hand" value={money(d.closing.cashInHand)} hint="From this period's cash" />
      <Figure label="Discounts" value={money(s.discounts)} />
      <Figure label="Tax collected" value={money(s.tax)} />
      <Figure tone={s.cancelledOrders ? 'bad' : undefined} label="Cancelled" value={s.cancelledOrders} hint={s.cancelledOrders ? `Worth ${money(s.cancelledValue)}` : 'None'} />
    </div>

    {d.daily.length > 1 && (
      <section className="bo-card rp-block">
        <div className="bo-card-head"><h2>Sales by day</h2><button className="ghost btn-sm" onClick={() => go('sales')}>See the table</button></div>
        <TrendChart rows={d.daily} />
      </section>
    )}

    <div className="bo-cols">
      <section className="bo-card rp-block">
        <div className="bo-card-head"><h2>Best sellers</h2><button className="ghost btn-sm" onClick={() => go('sales')}>All dishes</button></div>
        {d.items.length ? <RankedBars rows={d.items} unit="sold" top={6} /> : <div className="bo-small">Nothing sold yet.</div>}
      </section>
      <section className="bo-card rp-block">
        <div className="bo-card-head"><h2>How people paid</h2><button className="ghost btn-sm" onClick={() => go('cash')}>Details</button></div>
        {d.payments.length ? <RankedBars rows={d.payments} unit="paid" top={6} /> : <div className="bo-small">No payments yet.</div>}
      </section>
    </div>

    {d.hours.length > 1 && (
      <section className="bo-card rp-block">
        <div className="bo-card-head"><h2>Busiest hours</h2></div>
        <HoursChart rows={d.hours} />
      </section>
    )}
  </>
}

function RatingSummary({ r }) {
  if (!r.count) return <div className="bo-card rp-block"><Empty title="No ratings yet">Guests can rate their meal after they've eaten. Their ratings show here.</Empty></div>
  return (
    <div className="bo-cols">
      <div className="bo-figs" style={{ gridTemplateColumns: '1fr', marginBottom: 12 }}>
        <Figure tone="lead" label="Average rating" value={<span className="row" style={{ gap: 6, alignItems: 'center' }}>{r.average.toFixed(1)} <StarIcon size={20} /></span>} hint={`From ${count(r.count, 'guest')}`} />
      </div>
      <section className="bo-card rp-block">
        <RankedStars stars={r.stars} />
      </section>
    </div>
  )
}

function RankedStars({ stars }) {
  const max = Math.max(...stars, 1)
  return (
    <div className="rp-bars">
      {[5, 4, 3, 2, 1].map((n) => (
        <div className="rp-bar-row" key={n}>
          <span className="rp-bar-name">{n} {n === 1 ? 'star' : 'stars'}</span>
          <span className="rp-bar-track"><span className="rp-bar-fill" style={{ width: `${(stars[n - 1] / max) * 100}%` }} /></span>
          <span className="rp-bar-val">{stars[n - 1]}</span>
        </div>
      ))}
    </div>
  )
}
