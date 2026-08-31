import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, money } from '../api'
import { auth } from '../auth'
import { ui } from '../ui.jsx'
import { MenuIcon, BoxIcon, ReceiptIcon, KitchenIcon, UserIcon, QrIcon, GearIcon,
  CashIcon, TrendIcon, CalcIcon, BankIcon, WarnIcon, RefreshIcon, PrintIcon, DownloadIcon, StarIcon } from '../icons.jsx'

// Each tile shows only when the plan includes its feature AND the user has the
// permission — so the Manage grid matches the top nav (no dead modules).
const ACTIONS = [
  { to: '/admin/menu', Icon: MenuIcon, name: 'Menu', sub: 'Items, prices, photos', show: () => auth.can('menu.manage') },
  { to: '/admin/inventory', Icon: BoxIcon, name: 'Inventory', sub: 'Stock & recipes', show: () => auth.can('inventory.manage') && auth.hasFeature('inventory') },
  { to: '/cashier', Icon: ReceiptIcon, name: 'Reception', sub: 'Approve & bill', show: () => (auth.can('orders.approve') || auth.can('orders.pay')) && (auth.hasFeature('ordering') || auth.hasFeature('billing')) },
  { to: '/kitchen', Icon: KitchenIcon, name: 'Kitchen', sub: 'Live tickets', show: () => auth.can('orders.cook') && auth.hasFeature('kitchen') },
  { to: '/waiter', Icon: UserIcon, name: 'Waiter', sub: 'Serve tables', show: () => auth.can('orders.serve') && auth.hasFeature('waiter') },
  { to: '/owner', Icon: QrIcon, name: 'QR codes', sub: 'Print table QRs', show: () => auth.can('settings.manage') },
  { to: '/admin/settings', Icon: GearIcon, name: 'Settings', sub: 'Tables & staff', show: () => auth.can('settings.manage') },
]

export default function Dashboard() {
  const nav = useNavigate()
  const [d, setD] = useState(null)
  const [branchId, setBranchId] = useState('')
  const [err, setErr] = useState('')
  const iso = (dt) => dt.toISOString().slice(0, 10)
  const [from, setFrom] = useState(iso(new Date(Date.now() - 30 * 86400000)))
  const [to, setTo] = useState(iso(new Date()))
  const [dl, setDl] = useState(false)
  const [fb, setFb] = useState(null)
  const load = (bid = branchId) => api.dashboard(7, bid || null).then(setD).catch((e) => setErr(e.message))
  useEffect(() => { load(); api.getFeedback().then(setFb).catch(() => {}) }, [])
  const download = async () => {
    setDl(true)
    try { await api.downloadInvoicesCsv(from, to, branchId || null) } catch (e) { ui.alert(e.message) }
    setDl(false)
  }

  if (!d) return <div className="empty">{err || 'Loading dashboard…'}</div>

  const payTotal = d.payments.reduce((s, p) => s + p.amount, 0) || 1
  const name = auth.getStaff()?.displayName || 'there'
  const hour = new Date().getHours()
  const greet = hour < 12 ? 'Good morning' : hour < 17 ? 'Good afternoon' : 'Good evening'
  const t = d.trend
  const delta = (t[t.length - 2]?.total ?? 0) > 0 ? Math.round((((t[t.length - 1]?.total ?? 0) - t[t.length - 2].total) / t[t.length - 2].total) * 100) : null

  return (
    <div className="wrap">
      <div className="row between noprint" style={{ marginBottom: 8, flexWrap: 'wrap', gap: 12 }}>
        <div>
          <div className="muted">{greet},</div>
          <div className="h" style={{ margin: '2px 0 0' }}>{name}</div>
        </div>
        <div className="row" style={{ gap: 8, flexWrap: 'wrap' }}>
          {d.branches?.length > 1 && (
            <select value={branchId} onChange={(e) => { setBranchId(e.target.value); load(e.target.value) }}>
              <option value="">All branches</option>
              {d.branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
            </select>
          )}
          <button onClick={() => load()}><RefreshIcon size={15} /> Refresh</button>
          <button className="primary" onClick={() => window.print()}><PrintIcon size={15} /> Print report</button>
        </div>
      </div>

      {/* KPI tiles */}
      <div className="grid noprint" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))' }}>
        <Stat Icon={CashIcon} num={money(d.todaySales)} label="Today's sales" delta={delta} />
        <Stat Icon={ReceiptIcon} num={d.todayBills} label="Today's bills" />
        <Stat Icon={TrendIcon} num={money(d.periodSales)} label="7-day sales" />
        <Stat Icon={CalcIcon} num={money(d.periodAvgBill)} label="Avg bill" />
        <Stat Icon={BankIcon} num={money((d.periodTax || 0) + (d.periodFurtherTax || 0))} label="Sales tax · 7d" />
        {auth.hasFeature('inventory') && (
          <div className="card stat" style={{ cursor: 'pointer' }} onClick={() => nav('/admin/inventory')}>
            <div className="chip" style={d.lowStock > 0 ? { background: 'var(--amber-tint)', color: 'var(--amber)' } : {}}><WarnIcon size={20} /></div>
            <div className="num" style={{ color: d.lowStock > 0 ? 'var(--amber)' : 'var(--ink)' }}>{d.lowStock}</div>
            <div className="lbl">Low stock</div>
          </div>
        )}
      </div>

      {/* Reports export */}
      <div className="card noprint" style={{ marginTop: 16 }}>
        <div className="row between" style={{ flexWrap: 'wrap', gap: 10 }}>
          <div>
            <b>Sales &amp; tax report</b>
            <div className="muted" style={{ fontSize: 13 }}>Download a CSV of every settled invoice with the sales-tax breakdown — ready for your tax return.</div>
          </div>
          <div className="row" style={{ gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
            <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
            <span className="muted">→</span>
            <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
            <button className="primary" disabled={dl} onClick={download}>{dl ? 'Preparing…' : <><DownloadIcon size={15} /> Download CSV</>}</button>
          </div>
        </div>
      </div>

      {/* Quick actions */}
      <div className="cat-title noprint">Manage</div>
      <div className="grid noprint" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))' }}>
        {ACTIONS.filter((a) => a.show()).map((a) => (
          <div key={a.to} className="card tile" onClick={() => nav(a.to)}>
            <div className="chip"><a.Icon size={20} /></div>
            <div><div className="t-name">{a.name}</div><div className="t-sub">{a.sub}</div></div>
          </div>
        ))}
      </div>

      {/* Charts */}
      <div className="cat-title noprint">Sales · last 7 days</div>
      <div className="card noprint"><TrendChart trend={d.trend} /></div>

      <div className="grid g2 noprint" style={{ marginTop: 16 }}>
        <div className="card">
          <b>Payment methods · 7 days</b>
          <div style={{ marginTop: 12 }}>
            {d.payments.length === 0 && <div className="muted">No sales yet.</div>}
            {d.payments.map((p) => (
              <div key={p.method} style={{ marginBottom: 12 }}>
                <div className="row between" style={{ fontSize: 14 }}><span>{p.method} <span className="muted">· {p.count}</span></span><b>{money(p.amount)}</b></div>
                <div style={{ height: 8, background: 'var(--surface2)', borderRadius: 6, overflow: 'hidden', marginTop: 5 }}>
                  <div style={{ width: `${(p.amount / payTotal) * 100}%`, height: '100%', background: 'var(--accent)', borderRadius: 6 }} />
                </div>
              </div>
            ))}
          </div>
        </div>
        <div className="card">
          <b>Top items · 7 days</b>
          <div style={{ marginTop: 10 }}>
            {d.topItems.length === 0 && <div className="muted">No sales yet.</div>}
            {d.topItems.map((it, i) => (
              <div key={i} className="row between" style={{ padding: '7px 0', borderBottom: i < d.topItems.length - 1 ? '1px solid var(--line)' : 'none' }}>
                <span><span className="muted" style={{ marginRight: 6 }}>{i + 1}.</span>{it.name} <span className="muted">×{it.qty}</span></span>
                <b>{money(it.revenue)}</b>
              </div>
            ))}
          </div>
        </div>
      </div>

      {fb && fb.count > 0 && <FeedbackPanel fb={fb} />}

      <PrintReport d={d} greet={greet} name={name} />
    </div>
  )
}

// Customer feedback summary: average, star distribution, recent comments.
function FeedbackPanel({ fb }) {
  const maxBar = Math.max(1, ...fb.distribution)
  return (
    <div className="card noprint" style={{ marginTop: 16 }}>
      <div className="row between"><b>Customer feedback</b>
        <span className="muted" style={{ fontSize: 13 }}>{fb.count} rating(s)</span>
      </div>
      <div className="row" style={{ gap: 20, alignItems: 'center', margin: '10px 0', flexWrap: 'wrap' }}>
        <div style={{ textAlign: 'center' }}>
          <div className="big" style={{ color: 'var(--accent)', fontSize: 34 }}>{fb.average.toFixed(1)}</div>
          <div className="row" style={{ gap: 2, justifyContent: 'center', color: 'var(--amber, #e0a500)' }}>
            {Array.from({ length: 5 }).map((_, i) => <StarIcon key={i} size={15} style={{ opacity: i < Math.round(fb.average) ? 1 : .25 }} />)}
          </div>
        </div>
        <div style={{ flex: 1, minWidth: 180 }}>
          {[5, 4, 3, 2, 1].map((star) => (
            <div key={star} className="row" style={{ gap: 8, alignItems: 'center', fontSize: 12 }}>
              <span style={{ width: 24 }}>{star}★</span>
              <div style={{ flex: 1, height: 7, background: 'var(--surface2)', borderRadius: 5, overflow: 'hidden' }}>
                <div style={{ width: `${(fb.distribution[star - 1] / maxBar) * 100}%`, height: '100%', background: 'var(--accent)' }} />
              </div>
              <span className="muted" style={{ width: 22, textAlign: 'right' }}>{fb.distribution[star - 1]}</span>
            </div>
          ))}
        </div>
      </div>
      {fb.recent.length > 0 && (
        <div style={{ marginTop: 6 }}>
          <div className="cat-title" style={{ fontSize: 13 }}>Recent comments</div>
          {fb.recent.slice(0, 8).map((r) => (
            <div key={r.id} style={{ padding: '7px 0', borderBottom: '1px solid var(--line)' }}>
              <div style={{ fontSize: 13 }}>
                <span style={{ color: 'var(--amber, #e0a500)' }}>{Array.from({ length: r.rating }).map((_, i) => <StarIcon key={i} size={13} />)}</span>
                <span className="muted"> · {r.tableCode || 'table'} · {new Date(r.createdAt).toLocaleDateString()}</span>
              </div>
              <div style={{ fontSize: 14 }}>{r.comment}</div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

function Stat({ Icon, num, label, delta }) {
  return (
    <div className="card stat">
      {delta != null && <span className={'delta ' + (delta >= 0 ? 'up' : 'flat')}>{delta >= 0 ? '+' : ''}{delta}%</span>}
      <div className="chip">{Icon && <Icon size={20} />}</div>
      <div className="num">{num}</div>
      <div className="lbl">{label}</div>
    </div>
  )
}

// Print-only clean report.
function PrintReport({ d }) {
  return (
    <div className="print-area" style={{ display: 'none' }}>
      <div className="receipt" style={{ maxWidth: 620, margin: '0 auto' }}>
        <div className="r-h"><div style={{ fontWeight: 800, fontSize: 20 }}>Sales Report</div><div style={{ fontSize: 12 }}>Last 7 days</div></div>
        <div className="r-line"><span>Today's sales</span><b>{money(d.todaySales)}</b></div>
        <div className="r-line"><span>Today's bills</span><b>{d.todayBills}</b></div>
        <div className="r-line"><span>7-day sales</span><b>{money(d.periodSales)}</b></div>
        <div className="r-line"><span>7-day bills</span><b>{d.periodBills}</b></div>
        <div className="r-line"><span>Average bill</span><b>{money(d.periodAvgBill)}</b></div>
        <div className="r-line"><span>Net sales (excl. tax)</span><b>{money(d.periodNetSales)}</b></div>
        <div className="r-line"><span>Sales tax collected</span><b>{money((d.periodTax || 0) + (d.periodFurtherTax || 0))}</b></div>
        <div style={{ marginTop: 10, fontWeight: 700 }}>Payment methods</div>
        {d.payments.map((p) => <div key={p.method} className="r-line"><span>{p.method} ({p.count})</span><span>{money(p.amount)}</span></div>)}
        <div style={{ marginTop: 10, fontWeight: 700 }}>Top items</div>
        {d.topItems.map((it, i) => <div key={i} className="r-line"><span>{i + 1}. {it.name} ×{it.qty}</span><span>{money(it.revenue)}</span></div>)}
        {d.lowStockItems.length > 0 && <><div style={{ marginTop: 10, fontWeight: 700 }}>Low stock</div><div style={{ fontSize: 13 }}>{d.lowStockItems.join(', ')}</div></>}
      </div>
    </div>
  )
}

function TrendChart({ trend }) {
  const max = Math.max(1, ...trend.map((t) => t.total))
  const W = 640, H = 190, pad = 30, n = trend.length
  const gap = (W - pad * 2) / n
  const bw = gap * 0.55
  return (
    <svg viewBox={`0 0 ${W} ${H}`} style={{ width: '100%', height: 'auto' }} role="img" aria-label="Daily sales, last 7 days">
      {trend.map((t, i) => {
        const h = (t.total / max) * (H - pad * 2)
        const x = pad + i * gap + (gap - bw) / 2
        const y = H - pad - h
        const isToday = i === n - 1
        return (
          <g key={i}>
            <rect x={x} y={y} width={bw} height={Math.max(h, 2)} rx="5" fill={isToday ? 'var(--accent)' : '#d9b3c0'} />
            {t.total > 0 && <text x={x + bw / 2} y={y - 6} textAnchor="middle" fontSize="10" fontWeight="700" fill="var(--muted)">{Math.round(t.total / 1000 * 10) / 10}k</text>}
            <text x={x + bw / 2} y={H - pad + 15} textAnchor="middle" fontSize="10" fill="var(--muted)">{t.date.split(' ')[0]}</text>
          </g>
        )
      })}
      <line x1={pad} y1={H - pad} x2={W - pad} y2={H - pad} stroke="var(--line)" />
    </svg>
  )
}
