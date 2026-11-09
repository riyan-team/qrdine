import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { api, money } from '../api'
import { auth } from '../auth'
import { addDays, Figure, Loading, Problem, todayPk, useData } from '../backoffice.jsx'
import { useStaffOrders } from '../useStaffOrders'
import { tilt } from '../motion'
import { RankedBars, TrendChart } from './Reports.jsx'
import {
  BookIcon, BoxIcon, CashIcon, CheckIcon, KitchenIcon, MenuIcon, NoteIcon, QrIcon, ReceiptIcon, TrendIcon, UsersIcon, WalletIcon, WarnIcon,
} from '../icons.jsx'

const TILES = [
  { to: '/cashier', Icon: ReceiptIcon, name: 'Reception', sub: 'Tables, parcels, bills', show: () => (auth.can('orders.approve') || auth.can('orders.pay')) && (auth.hasFeature('ordering') || auth.hasFeature('billing')) },
  { to: '/kitchen', Icon: KitchenIcon, name: 'Kitchen', sub: 'Live tickets', show: () => auth.can('orders.cook') && auth.hasFeature('kitchen') },
  { to: '/waiter', Icon: NoteIcon, name: 'Waiter', sub: 'Serve and take orders', show: () => auth.can('orders.serve') && auth.hasFeature('waiter') },
  { to: '/admin/closing', Icon: CashIcon, name: 'Day closing', sub: 'Count the cash', show: () => auth.can('orders.pay') && auth.hasFeature('billing') },
  { to: '/admin/reports', Icon: TrendIcon, name: 'Reports', sub: 'Sales, profit, staff', show: () => auth.can('reports.view') && auth.hasFeature('reports') },
  { to: '/admin/expenses', Icon: WalletIcon, name: 'Add an expense', sub: 'Rent, gas, bijli', show: () => auth.can('expenses.manage') && auth.hasFeature('reports') },
  { to: '/admin/suppliers', Icon: BookIcon, name: 'Supplier khata', sub: 'Bills and payments', show: () => auth.can('suppliers.manage') && auth.hasFeature('inventory') },
  { to: '/admin/inventory', Icon: BoxIcon, name: 'Inventory', sub: 'Stock levels', show: () => auth.can('inventory.manage') && auth.hasFeature('inventory') },
  { to: '/admin/payroll', Icon: UsersIcon, name: 'Staff & salaries', sub: 'Pay and attendance', show: () => auth.can('payroll.manage') && auth.hasFeature('reports') },
  { to: '/admin/menu', Icon: MenuIcon, name: 'Menu', sub: 'Dishes and prices', show: () => auth.can('menu.manage') },
  { to: '/owner', Icon: QrIcon, name: 'Table QR codes', sub: 'Print for your tables', show: () => auth.can('settings.manage') },
]

const canSeeFloor = () => auth.hasFeature('ordering') && (auth.can('orders.approve') || auth.can('orders.pay'))

// The owner's front page: how today is going, what needs attention now, and a way into everything else.
export default function Dashboard() {
  const [params] = useSearchParams()
  const [branchId, setBranchId] = useState('')
  const { data: d, error, loading, reload } = useData(() => api.dashboard(7, branchId || null), [branchId])

  const name = auth.getStaff()?.displayName || ''
  const hour = (new Date().getUTCHours() + 5) % 24
  const greet = hour < 12 ? 'Good morning' : hour < 17 ? 'Good afternoon' : 'Good evening'

  return (
    <div className="bo-wrap">
      <header className="bo-head">
        <div className="bo-head-text">
          <h1 className="h">{greet}{name ? `, ${name.split(' ')[0]}` : ''}</h1>
          <p>{auth.getStaff()?.restaurantName}</p>
        </div>
        {d?.branches?.length > 1 && (
          <div className="bo-head-actions">
            <select value={branchId} aria-label="Branch" onChange={(e) => setBranchId(e.target.value)}>
              <option value="">All branches</option>
              {d.branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
            </select>
          </div>
        )}
      </header>

      <GettingStarted welcome={params.get('welcome') === '1'} />
      <Problem onRetry={reload}>{error}</Problem>
      {loading && !d && <Loading />}

      {d && (
        <div className="bo-figs">
          <Figure tone="lead" label="Sales today" value={money(d.todaySales)} hint={`${d.todayBills} ${d.todayBills === 1 ? 'bill' : 'bills'}`} />
          {canSeeFloor() && <RightNow />}
          <Figure label="Last 7 days" value={money(d.periodSales)} hint={`Average bill ${money(d.periodAvgBill)}`} />
          {auth.hasFeature('inventory') && (
            <Link to="/admin/inventory" style={{ display: 'contents' }}>
              <Figure tone={d.lowStock > 0 ? 'bad' : undefined} label="Stock running low" value={d.lowStock}
                hint={d.lowStock > 0 ? <><WarnIcon size={13} /> Check before service</> : 'All fine'} />
            </Link>
          )}
        </div>
      )}

      <div className="td-tiles">
        {TILES.filter((t) => t.show()).map((t) => (
          <Link key={t.to} to={t.to} className="td-tile" {...tilt(6)}>
            <i><t.Icon size={18} /></i>
            <span>{t.name}<small>{t.sub}</small></span>
          </Link>
        ))}
      </div>

      {d && d.trend?.length > 1 && (
        <section className="bo-card rp-block">
          <div className="bo-card-head"><h2>Sales, last 7 days</h2></div>
          <TrendChart rows={d.trend.map((t, i) => ({ date: addDays(todayPk(), i - (d.trend.length - 1)), sales: t.total, bills: t.bills ?? 0 }))} />
        </section>
      )}

      {d && (
        <div className="bo-cols">
          <section className="bo-card rp-block">
            <div className="bo-card-head"><h2>Best sellers, last 7 days</h2></div>
            {d.topItems.length
              ? <RankedBars rows={d.topItems.map((i) => ({ name: i.name, count: i.qty, amount: i.revenue }))} unit="sold" top={6} />
              : <div className="bo-small">No sales yet. They show here after your first bill.</div>}
          </section>
          <section className="bo-card rp-block">
            <div className="bo-card-head"><h2>How people paid, last 7 days</h2></div>
            {d.payments.length
              ? <RankedBars rows={d.payments.map((p) => ({ name: p.method, count: p.count, amount: p.amount }))} unit="paid" />
              : <div className="bo-small">No payments yet.</div>}
          </section>
        </div>
      )}
    </div>
  )
}

function RightNow() {
  const orders = useStaffOrders()
  const tables = new Set(orders.map((o) => o.tableCode)).size
  const waiting = orders.filter((o) => o.status === 'Placed').length
  const ready = orders.filter((o) => o.status === 'Ready').length
  return (
    <Link to="/cashier" style={{ display: 'contents' }}>
      <Figure tone={waiting > 0 ? 'bad' : undefined} label="Running now" value={`${tables} ${tables === 1 ? 'table' : 'tables'}`}
        hint={waiting > 0 ? `${waiting} waiting to be approved` : ready > 0 ? `${ready} ready to serve` : 'Nothing waiting'} />
    </Link>
  )
}

// A short list for someone who has just signed up. It goes away when they've finished with it.
function GettingStarted({ welcome }) {
  const key = `dinein.setup.${auth.staffSlug()}`
  const read = () => { try { return JSON.parse(localStorage.getItem(key) || 'null') } catch { return null } }
  const [state, setState] = useState(() => read() || (welcome ? { done: [] } : null))
  const [table, setTable] = useState(null)

  const save = (next) => {
    setState(next)
    try { localStorage.setItem(key, JSON.stringify(next)) } catch { /* private mode: it just won't be remembered */ }
  }
  useEffect(() => { if (welcome && !read()) save({ done: [] }) }, [welcome])
  useEffect(() => { if (state && !state.hidden) api.getTables().then((t) => setTable(t[0] || null)).catch(() => {}) }, [state?.hidden])
  if (!state || state.hidden) return null

  const steps = [
    { id: 'menu', title: 'Check your menu', sub: "We've put in a starter menu. Change it to yours, or read it from a photo.", to: '/admin/menu', show: auth.can('menu.manage') },
    { id: 'qr', title: 'Print your table QR codes', sub: 'One for each table.', to: '/owner', show: auth.can('settings.manage') },
    { id: 'try', title: 'Order something yourself', sub: 'Open a table on your phone and see it reach Reception.', to: table ? `/r/${auth.staffSlug()}/t/${table.code}` : '/owner', show: true },
    { id: 'staff', title: 'Add your staff', sub: 'Give your cashier, kitchen and waiters their own sign-in.', to: '/admin/settings', show: auth.can('settings.manage') },
  ].filter((s) => s.show)
  const tick = (id) => { if (!state.done.includes(id)) save({ ...state, done: [...state.done, id] }) }

  return (
    <section className="bo-card rp-block">
      <div className="bo-card-head">
        <h2>{state.done.length >= steps.length ? "You're ready to open" : 'Four things before you open'}</h2>
        <button className="ghost btn-sm" onClick={() => save({ ...state, hidden: true })}>Hide this</button>
      </div>
      <div className="td-steps">
        {steps.map((s) => {
          const done = state.done.includes(s.id)
          return (
            <Link key={s.id} to={s.to} className={`td-step${done ? ' is-done' : ''}`} onClick={() => tick(s.id)}>
              <i>{done && <CheckIcon size={13} />}</i>
              <span><b>{s.title}</b><small>{s.sub}</small></span>
            </Link>
          )
        })}
      </div>
    </section>
  )
}
