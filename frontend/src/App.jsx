import { Routes, Route, NavLink, Navigate, useLocation } from 'react-router-dom'
import Owner from './pages/Owner.jsx'
import Menu from './pages/Menu.jsx'
import Cashier from './pages/Cashier.jsx'
import Kitchen from './pages/Kitchen.jsx'
import Waiter from './pages/Waiter.jsx'
import Login from './pages/Login.jsx'
import MenuAdmin from './pages/MenuAdmin.jsx'
import Dashboard from './pages/Dashboard.jsx'
import Settings from './pages/Settings.jsx'
import Signup from './pages/Signup.jsx'
import Inventory from './pages/Inventory.jsx'
import FbrInvoices from './pages/FbrInvoices.jsx'
import PlatformLogin from './pages/PlatformLogin.jsx'
import PlatformConsole from './pages/PlatformConsole.jsx'
import { DialogHost } from './ui.jsx'
import { auth } from './auth'

// Guards a staff-only route: no token → bounce to /login, remembering where we were.
function RequireStaff({ children }) {
  const loc = useLocation()
  if (!auth.staffToken) return <Navigate to="/login" state={{ from: loc.pathname }} replace />
  return children
}

function Nav() {
  const link = ({ isActive }) => (isActive ? 'active' : '')
  const staff = auth.getStaff()
  const can = (p) => auth.can(p)
  const has = (f) => auth.hasFeature(f)   // plan-driven module gate
  return (
    <div className="topbar noprint">
      <span className="brand">🍽️ QrDine</span>
      {can('reports.view') && has('reports') && <NavLink to="/admin/dashboard" className={link}>Dashboard</NavLink>}
      {can('settings.manage') && <NavLink to="/owner" className={link}>QR codes</NavLink>}
      {can('menu.manage') && <NavLink to="/admin/menu" className={link}>Menu</NavLink>}
      {can('inventory.manage') && has('inventory') && <NavLink to="/admin/inventory" className={link}>Inventory</NavLink>}
      {has('fbr') && (can('reports.view') || can('invoice.sync')) && <NavLink to="/admin/fbr" className={link}>FBR</NavLink>}
      {(can('orders.approve') || can('orders.pay')) && (has('ordering') || has('billing')) && <NavLink to="/cashier" className={link}>Reception</NavLink>}
      {can('orders.cook') && has('kitchen') && <NavLink to="/kitchen" className={link}>Kitchen</NavLink>}
      {can('orders.serve') && has('waiter') && <NavLink to="/waiter" className={link}>Waiter</NavLink>}
      {can('settings.manage') && <NavLink to="/admin/settings" className={link}>Settings</NavLink>}
      <span style={{ marginLeft: 'auto' }} />
      {staff ? (
        <span className="row" style={{ gap: 8, fontSize: 13 }}>
          <span className="muted">{staff.displayName} · {staff.role}</span>
          <button className="ghost" onClick={() => { auth.clearStaff(); location.href = '/login' }}>Sign out</button>
        </span>
      ) : (
        <NavLink to="/login" className={link}>Staff sign in</NavLink>
      )}
    </div>
  )
}

export default function App() {
  const loc = useLocation()
  const isPlatform = loc.pathname.startsWith('/platform')
  // Auth + public menu screens carry their own branding — no staff topbar.
  const isBare = ['/login', '/signup'].includes(loc.pathname)
  return (
    <>
      {!isPlatform && !isBare && <Nav />}
      <Routes>
        <Route path="/" element={<Navigate to="/login" />} />
        <Route path="/owner" element={<RequireStaff><Owner /></RequireStaff>} />
        <Route path="/login" element={<Login />} />
        <Route path="/signup" element={<Signup />} />
        <Route path="/platform/login" element={<PlatformLogin />} />
        <Route path="/platform" element={<PlatformConsole />} />
        <Route path="/r/:slug/t/:code" element={<Menu />} />
        <Route path="/admin/dashboard" element={<RequireStaff><Dashboard /></RequireStaff>} />
        <Route path="/admin/menu" element={<RequireStaff><MenuAdmin /></RequireStaff>} />
        <Route path="/admin/inventory" element={<RequireStaff><Inventory /></RequireStaff>} />
        <Route path="/admin/fbr" element={<RequireStaff><FbrInvoices /></RequireStaff>} />
        <Route path="/admin/settings" element={<RequireStaff><Settings /></RequireStaff>} />
        <Route path="/cashier" element={<RequireStaff><Cashier /></RequireStaff>} />
        <Route path="/kitchen" element={<RequireStaff><Kitchen /></RequireStaff>} />
        <Route path="/waiter" element={<RequireStaff><Waiter /></RequireStaff>} />
      </Routes>
      <DialogHost />
    </>
  )
}
