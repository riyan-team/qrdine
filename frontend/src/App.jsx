import { Component, lazy, Suspense, useEffect, useState } from 'react'
import { Routes, Route, NavLink, Navigate, useLocation, Link } from 'react-router-dom'
import { DialogHost, ToastHost } from './ui.jsx'
import { auth } from './auth'
import { api, getPlans, roleLabel } from './api'
import { CONTACT } from './contact'
import DineInLogo from './DineInLogo.jsx'
import {
  BookIcon, BoxIcon, CashIcon, ChartIcon, GearIcon, KitchenIcon, ListIcon, LogoutIcon, MenuIcon, NoteIcon,
  QrIcon, ReceiptIcon, StarIcon, TrendIcon, UsersIcon, WalletIcon, BankIcon, CloseIcon,
} from './icons.jsx'

// Each screen is fetched when it's first opened. If a new version went live while this tab was open,
// the old file names are gone, so reload once to pick up the new ones.
const screen = (load) => lazy(() => load().then((m) => {
  try { sessionStorage.removeItem('dinein.reloaded') } catch { /* private mode */ }
  return m
}).catch((e) => {
  let tried = true
  try { tried = !!sessionStorage.getItem('dinein.reloaded'); sessionStorage.setItem('dinein.reloaded', '1') } catch { /* private mode */ }
  if (tried) throw e
  location.reload()
  return new Promise(() => {})
}))

const Owner = screen(() => import('./pages/Owner.jsx'))
const Menu = screen(() => import('./pages/Menu.jsx'))
const Cashier = screen(() => import('./pages/Cashier.jsx'))
const Kitchen = screen(() => import('./pages/Kitchen.jsx'))
const Waiter = screen(() => import('./pages/Waiter.jsx'))
const Login = screen(() => import('./pages/Login.jsx'))
const MenuAdmin = screen(() => import('./pages/MenuAdmin.jsx'))
const Dashboard = screen(() => import('./pages/Dashboard.jsx'))
const Settings = screen(() => import('./pages/Settings.jsx'))
const Signup = screen(() => import('./pages/Signup.jsx'))
const Inventory = screen(() => import('./pages/Inventory.jsx'))
const FbrInvoices = screen(() => import('./pages/FbrInvoices.jsx'))
const PlatformLogin = screen(() => import('./pages/PlatformLogin.jsx'))
const PlatformConsole = screen(() => import('./pages/PlatformConsole.jsx'))
const Landing = screen(() => import('./pages/Landing.jsx'))
const OrderDetails = screen(() => import('./pages/OrderDetails.jsx'))
const Loyalty = screen(() => import('./pages/Loyalty.jsx'))
const Reports = screen(() => import('./pages/Reports.jsx'))
const Suppliers = screen(() => import('./pages/Suppliers.jsx'))
const Customers = screen(() => import('./pages/Customers.jsx'))
const Expenses = screen(() => import('./pages/Expenses.jsx'))
const Payroll = screen(() => import('./pages/Payroll.jsx'))
const DayClose = screen(() => import('./pages/DayClose.jsx'))

// The server has the last word on a saved sign-in: a removed account, a new password or a deleted
// restaurant all end it. Ask once per sign-in per page load, before any staff screen shows, so an
// old sign-in never opens an empty app. No answer at all (offline) lets the screen open: the
// server still checks every request.
const sessionVerdicts = new Map()   // token → 'ok' | 'ended'
const sessionChecks = new Map()     // token → Promise of the verdict
function checkSession(token, slug) {
  if (!sessionChecks.has(token)) {
    sessionChecks.set(token, window.fetch(`/api/r/${encodeURIComponent(slug)}/staff/plan`, { headers: { Authorization: `Bearer ${token}` } })
      .then((r) => (r.status === 401 || r.status === 404 ? 'ended' : 'ok'), () => 'ok')
      .then((v) => { sessionVerdicts.set(token, v); return v }))
  }
  return sessionChecks.get(token)
}

// Guards a staff-only route: no token sends them to /login, remembering where they were going.
// A screen they aren't allowed sends them to their own home rather than a page of errors.
function RequireStaff({ children, perm, anyOf, feature, anyFeature }) {
  const loc = useLocation()
  const token = auth.staffToken
  const [verdict, setVerdict] = useState(() => (token ? sessionVerdicts.get(token) ?? null : null))
  useEffect(() => {
    if (!token) return undefined
    let live = true
    checkSession(token, auth.staffSlug()).then((v) => {
      if (v === 'ended' && auth.staffToken === token) auth.clearStaff()
      if (live) setVerdict(v)
    })
    return () => { live = false }
  }, [token])
  if (!token || verdict === 'ended') return <Navigate to="/login" state={{ from: loc.pathname }} replace />
  if (verdict === null) return null
  const allowed = (!perm || auth.can(perm)) && (!anyOf || anyOf.some((p) => auth.can(p))) && (!feature || auth.hasFeature(feature)) && (!anyFeature || anyFeature.some((f) => auth.hasFeature(f)))
  if (!allowed) return <Navigate to={auth.home()} replace />
  return children
}

// Catches any render-time crash so a bug shows a friendly recovery screen instead of
// a blank white page. The technical error is logged to the console for developers.
class ErrorBoundary extends Component {
  constructor(props) { super(props); this.state = { failed: false } }
  static getDerivedStateFromError() { return { failed: true } }
  componentDidCatch(error, info) { console.error('UI error:', error, info) }
  render() {
    if (!this.state.failed) return this.props.children
    return (
      <div className="wrap" style={{ maxWidth: 460, marginTop: 80, textAlign: 'center' }}>
        <div className="card">
          <div className="big" style={{ marginBottom: 8 }}>Something went wrong</div>
          <p className="muted" style={{ fontSize: 14 }}>
            The page ran into an unexpected problem. Reloading usually fixes it.
          </p>
          <button className="primary" onClick={() => { location.href = '/' }}>Reload the app</button>
        </div>
      </div>
    )
  }
}

// Everything this person may open, in the order a restaurant thinks about it. A screen appears
// only if both their role and the restaurant's plan allow it, so nobody sees a link that leads nowhere.
function navGroups() {
  const can = (p) => auth.can(p)
  const has = (f) => auth.hasFeature(f)
  const groups = [
    { items: [can('reports.view') && has('reports') && { to: '/admin/dashboard', label: 'Today', Icon: ChartIcon }] },
    {
      title: 'Orders',
      items: [
        (can('orders.approve') || can('orders.pay')) && (has('ordering') || has('billing')) && { to: '/cashier', label: 'Reception', Icon: ReceiptIcon },
        can('orders.cook') && has('kitchen') && { to: '/kitchen', label: 'Kitchen', Icon: KitchenIcon },
        can('orders.serve') && has('waiter') && { to: '/waiter', label: 'Waiter', Icon: NoteIcon },
        can('orders.pay') && has('billing') && { to: '/admin/closing', label: 'Day closing', Icon: CashIcon },
      ],
    },
    {
      title: 'Menu',
      items: [
        can('menu.manage') && { to: '/admin/menu', label: 'Menu', Icon: MenuIcon },
        can('settings.manage') && { to: '/owner', label: 'Table QR codes', Icon: QrIcon },
      ],
    },
    {
      title: 'Back office',
      items: [
        can('reports.view') && has('reports') && { to: '/admin/reports', label: 'Reports', Icon: TrendIcon },
        can('inventory.manage') && has('inventory') && { to: '/admin/inventory', label: 'Inventory', Icon: BoxIcon },
        can('suppliers.manage') && has('inventory') && { to: '/admin/suppliers', label: 'Supplier khata', Icon: BookIcon },
        can('customers.manage') && has('billing') && { to: '/admin/customers', label: 'Customer khata', Icon: BookIcon },
        can('expenses.manage') && has('reports') && { to: '/admin/expenses', label: 'Expenses', Icon: WalletIcon },
        can('payroll.manage') && has('reports') && { to: '/admin/payroll', label: 'Staff & salaries', Icon: UsersIcon },
      ],
    },
    {
      title: 'Setup',
      items: [
        can('settings.manage') && { to: '/admin/loyalty', label: 'Rewards', Icon: StarIcon },
        has('fbr') && (can('reports.view') || can('invoice.sync')) && { to: '/admin/fbr', label: 'FBR invoices', Icon: BankIcon },
        can('settings.manage') && { to: '/admin/settings', label: 'Settings', Icon: GearIcon },
      ],
    },
  ]
  return groups.map((g) => ({ ...g, items: g.items.filter(Boolean) })).filter((g) => g.items.length)
}

function signOut() { auth.clearStaff(); location.href = '/login' }

function Sidebar({ groups, open, onClose }) {
  const staff = auth.getStaff()
  return (
    <>
      <div className={`sx-scrim noprint${open ? ' is-open' : ''}`} onClick={onClose} />
      <aside className={`sx-side noprint${open ? ' is-open' : ''}`}>
        <div className="sx-brand">
          <Link to={auth.home()} aria-label="Dine In home"><DineInLogo className="sx-logo" /></Link>
          <button className="ghost sx-close" onClick={onClose} aria-label="Close menu"><CloseIcon size={20} /></button>
        </div>
        {staff?.restaurantName && <div className="sx-place" title={staff.restaurantName}>{staff.restaurantName}</div>}
        <nav className="sx-nav">
          {groups.map((g, i) => (
            <div className="sx-group" key={g.title || i}>
              {g.title && <div className="sx-group-title">{g.title}</div>}
              {g.items.map((it) => (
                <NavLink key={it.to} to={it.to} className={({ isActive }) => `sx-link${isActive ? ' is-active' : ''}`} onClick={onClose}>
                  <it.Icon size={17} /><span>{it.label}</span>
                </NavLink>
              ))}
            </div>
          ))}
        </nav>
        <div className="sx-user">
          <div className="sx-user-text">
            <b>{staff?.displayName}</b>
            <span>{staff ? roleLabel(staff.role) : ''}</span>
          </div>
          <button className="ghost sx-out" onClick={signOut} title="Sign out" aria-label="Sign out"><LogoutIcon size={17} /></button>
        </div>
      </aside>
    </>
  )
}

const days = (n) => `${n} day${n === 1 ? '' : 's'}`

// Warns the owner/manager before the paid date runs out, and tells all staff why nothing
// works once the restaurant is paused (any failed call announces that; see api.js).
function PlanBanner() {
  const [msg, setMsg] = useState(null)
  useEffect(() => {
    const onLocked = (e) => setMsg({ text: e.detail, urgent: true })
    window.addEventListener('qrdine:locked', onLocked)
    // A plan change reaches screens that are already signed in. The reload only runs when something differs.
    api.staffPlan().then((features) => {
      const staff = auth.getStaff()
      if (staff && Object.keys(features).some((k) => staff.features?.[k] !== features[k])) {
        auth.setStaff({ ...staff, features })
        location.reload()
      }
    }).catch(() => {})
    if (auth.can('settings.manage')) {
      api.getSubscription().then((s) => {
        if (s.state === 'EndingSoon') setMsg({ text: `Your Dine In plan ends in ${days(s.daysLeft)}. To renew, call ${CONTACT.phone}.` })
        if (s.state === 'Overdue') setMsg({ text: `Your Dine In plan has ended. It will stop working in ${days(s.daysLeft)} unless renewed. Call ${CONTACT.phone}.`, urgent: true })
      }).catch(() => {})
    }
    return () => window.removeEventListener('qrdine:locked', onLocked)
  }, [])
  if (!msg) return null
  return <div className={`sx-banner noprint${msg.urgent ? ' is-urgent' : ''}`} role="alert">{msg.text}</div>
}

// Says so when the server can't be reached, and keeps trying until it can.
function ConnectionBar() {
  const [down, setDown] = useState(false)
  useEffect(() => {
    const on = (e) => setDown(e.detail.down)
    window.addEventListener('dinein:net', on)
    return () => window.removeEventListener('dinein:net', on)
  }, [])
  useEffect(() => {
    if (!down) return undefined
    const timer = setInterval(() => { getPlans().catch(() => {}) }, 5000)
    return () => clearInterval(timer)
  }, [down])
  if (!down) return null
  return <div className="sx-banner noprint is-urgent" role="alert">Can't reach the server. This screen may be out of date. Trying again…</div>
}

// The staff frame. A sidebar for anyone with a few screens, and just a top bar for someone like
// a cook who has only one. Under print it gets out of the way so slips still fill the paper.
function StaffShell({ children }) {
  const loc = useLocation()
  const [open, setOpen] = useState(false)
  const groups = navGroups()
  const links = groups.flatMap((g) => g.items)
  const here = links.find((l) => loc.pathname.startsWith(l.to))
  const staff = auth.getStaff()
  const solo = links.length <= 1

  useEffect(() => { setOpen(false) }, [loc.pathname])

  return (
    <div className={`sx${solo ? ' is-solo' : ''}`}>
      {!solo && <Sidebar groups={groups} open={open} onClose={() => setOpen(false)} />}
      <div className="sx-main">
        <div className="sx-top noprint">
          {!solo && <button className="ghost sx-burger" onClick={() => setOpen(true)} aria-label="Open menu"><ListIcon size={22} /></button>}
          {solo
            ? <><DineInLogo className="sx-top-logo" /><span className="sx-top-place">{staff?.restaurantName}</span></>
            : <span className="sx-top-title">{here?.label || staff?.restaurantName}</span>}
          {solo && (
            <span className="row" style={{ marginLeft: 'auto', gap: 8 }}>
              <span className="sx-top-who">{staff?.displayName}</span>
              <button className="ghost sx-out" onClick={signOut}><LogoutIcon size={17} /> Sign out</button>
            </span>
          )}
        </div>
        <PlanBanner />
        <ConnectionBar />
        <div className="sx-page" key={loc.pathname}><Suspense fallback={null}>{children}</Suspense></div>
      </div>
    </div>
  )
}

const page = (el, guard = {}) => <RequireStaff {...guard}><StaffShell>{el}</StaffShell></RequireStaff>

export default function App() {
  return (
    <ErrorBoundary>
      <Suspense fallback={null}>
      <Routes>
        <Route path="/" element={<Landing />} />
        <Route path="/login" element={<Login />} />
        <Route path="/signup" element={<Signup />} />
        <Route path="/platform/login" element={<PlatformLogin />} />
        <Route path="/platform" element={<PlatformConsole />} />
        <Route path="/r/:slug/t/:code" element={<Menu />} />
        <Route path="/r/:slug/order/:id" element={<OrderDetails />} />
        <Route path="/r/:slug/t/:code/order/:id" element={<OrderDetails />} />

        <Route path="/admin/dashboard" element={page(<Dashboard />, { perm: 'reports.view', feature: 'reports' })} />
        <Route path="/admin/reports" element={page(<Reports />, { perm: 'reports.view', feature: 'reports' })} />
        <Route path="/admin/menu" element={page(<MenuAdmin />, { perm: 'menu.manage' })} />
        <Route path="/owner" element={page(<Owner />, { perm: 'settings.manage' })} />
        <Route path="/admin/inventory" element={page(<Inventory />, { perm: 'inventory.manage', feature: 'inventory' })} />
        <Route path="/admin/suppliers" element={page(<Suppliers />, { perm: 'suppliers.manage', feature: 'inventory' })} />
        <Route path="/admin/customers" element={page(<Customers />, { perm: 'customers.manage', feature: 'billing' })} />
        <Route path="/admin/expenses" element={page(<Expenses />, { perm: 'expenses.manage', feature: 'reports' })} />
        <Route path="/admin/payroll" element={page(<Payroll />, { perm: 'payroll.manage', feature: 'reports' })} />
        <Route path="/admin/closing" element={page(<DayClose />, { perm: 'orders.pay', feature: 'billing' })} />
        <Route path="/admin/fbr" element={page(<FbrInvoices />, { feature: 'fbr' })} />
        <Route path="/admin/settings" element={page(<Settings />)} />
        <Route path="/admin/loyalty" element={page(<Loyalty />, { perm: 'settings.manage' })} />
        <Route path="/cashier" element={page(<Cashier />, { anyOf: ['orders.approve', 'orders.pay'], anyFeature: ['ordering', 'billing'] })} />
        <Route path="/kitchen" element={page(<Kitchen />, { perm: 'orders.cook', feature: 'kitchen' })} />
        <Route path="/waiter" element={page(<Waiter />, { perm: 'orders.serve', feature: 'waiter' })} />
        {/* Typing /admin: signed in goes to your own first screen, otherwise to sign-in. */}
        <Route path="/admin" element={<Navigate to={auth.staffToken ? auth.home() : '/login'} replace />} />
        <Route path="/admin/*" element={<Navigate to={auth.staffToken ? auth.home() : '/login'} replace />} />
        <Route path="*" element={<Navigate to={auth.staffToken ? auth.home() : '/'} replace />} />
      </Routes>
      </Suspense>
      <DialogHost />
      <ToastHost />
    </ErrorBoundary>
  )
}
