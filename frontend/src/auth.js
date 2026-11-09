// Auth + tenant store. The staff session remembers which restaurant (slug) the
// user signed into, so all staff calls target that tenant. Guest tokens are keyed
// per restaurant+table and kept in memory only.
import { parseError } from './errors'

const STAFF_KEY = 'qrdine.staff'

// When a token runs out, from its own payload (no network). A token we can't read counts as run out.
export function expiresAt(token) {
  try {
    const part = String(token).split('.')[1].replace(/-/g, '+').replace(/_/g, '/')
    return (JSON.parse(atob(part)).exp || 0) * 1000
  } catch { return 0 }
}
// Still good for at least another minute.
export const fresh = (token) => !!token && expiresAt(token) > Date.now() + 60_000

export const auth = {
  getStaff() { try { return JSON.parse(localStorage.getItem(STAFF_KEY) || 'null') } catch { return null } },
  setStaff(s) { localStorage.setItem(STAFF_KEY, JSON.stringify(s)) },
  clearStaff() { localStorage.removeItem(STAFF_KEY) },
  // A sign-in that has run out is no sign-in: the screens go to the sign-in page, not an empty app.
  get staffToken() {
    const t = this.getStaff()?.token || null
    if (t && expiresAt(t) <= Date.now()) { this.clearStaff(); return null }
    return t
  },
  staffSlug() { return this.getStaff()?.slug || '' },
  role() { return this.getStaff()?.role || null },
  perms() { return this.getStaff()?.permissions || [] },
  can(p) { return this.perms().includes(p) },
  // Plan-driven module flags. Missing (older session) → treat as enabled so nothing
  // vanishes until the next login refreshes the flags.
  features() { return this.getStaff()?.features || null },
  hasFeature(f) { const x = this.features(); return x ? !!x[f] : true },

  // One box: a mobile number or username, and a password. The account says which restaurant.
  async signIn(username, password) {
    const res = await fetch('/api/auth/login', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    })
    if (!res.ok) throw await parseError(res)
    const session = await res.json()          // { token, displayName, role, permissions, features, restaurantName, slug }
    this.setStaff(session)
    return session
  },

  // Where this person starts their day, going by what they're allowed to do.
  home() {
    if (this.can('reports.view') && this.hasFeature('reports')) return '/admin/dashboard'
    if ((this.can('orders.approve') || this.can('orders.pay')) && (this.hasFeature('ordering') || this.hasFeature('billing'))) return '/cashier'
    if (this.can('orders.cook') && this.hasFeature('kitchen')) return '/kitchen'
    if (this.can('orders.serve') && this.hasFeature('waiter')) return '/waiter'
    if (this.can('menu.manage')) return '/admin/menu'
    return '/admin/settings'
  },

  async login(slug, username, password) {
    const res = await fetch(`/api/r/${slug}/auth/login`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    })
    if (!res.ok) throw await parseError(res)   // friendly ApiError
    const session = await res.json()          // { token, displayName, role }
    this.setStaff({ ...session, slug })
    return session
  },

  // A completed sign-up returns an owner session; store it with its slug.
  setSession(session, slug) { this.setStaff({ ...session, slug }) },

  _guestTokens: {},
  rememberTable(slug, tableCode) {
    if (slug && tableCode) {
      try { sessionStorage.setItem(`qrdine.table.${slug}`, tableCode) } catch {}
    }
  },
  getTable(slug) {
    try { return sessionStorage.getItem(`qrdine.table.${slug}`) || '' } catch { return '' }
  },
  // A guest's table pass (6 hours). A pass that has run out is replaced, so a phone left on the
  // menu all evening keeps working.
  async guestToken(slug, tableCode) {
    const code = tableCode || this.getTable(slug)
    if (!code) {
      const cached = Object.keys(this._guestTokens).find(k => k.startsWith(`${slug}:`) && fresh(this._guestTokens[k]))
      if (cached) return this._guestTokens[cached]
      try {
        const stored = sessionStorage.getItem(`qrdine.guestToken.${slug}`)
        if (fresh(stored)) return stored
      } catch {}
      return ''
    }
    this.rememberTable(slug, code)
    const key = `${slug}:${code}`
    if (fresh(this._guestTokens[key])) return this._guestTokens[key]
    try {
      const stored = sessionStorage.getItem(`qrdine.guestToken.${key}`)
      if (fresh(stored)) {
        this._guestTokens[key] = stored
        return stored
      }
    } catch {}
    // The menu and the live feed ask at the same moment: one request serves both.
    if (!this._pending[key]) {
      this._pending[key] = (async () => {
        const res = await fetch(`/api/r/${slug}/tables/${code}/session`, { method: 'POST' })
        if (!res.ok) throw new Error('Could not start table session')
        const { token } = await res.json()
        this._guestTokens[key] = token
        try {
          sessionStorage.setItem(`qrdine.guestToken.${slug}`, token)
          sessionStorage.setItem(`qrdine.guestToken.${key}`, token)
        } catch {}
        return token
      })().finally(() => { delete this._pending[key] })
    }
    return this._pending[key]
  },
  _pending: {},
}

export const staffAuth = () => {
  const t = auth.staffToken
  return t ? { Authorization: `Bearer ${t}` } : {}
}

// Platform operator (SaaS owner) session — kept entirely separate from staff.
const PLATFORM_KEY = 'qrdine.platform'
export const platformAuth = {
  get() { try { return JSON.parse(localStorage.getItem(PLATFORM_KEY) || 'null') } catch { return null } },
  set(s) { localStorage.setItem(PLATFORM_KEY, JSON.stringify(s)) },
  clear() { localStorage.removeItem(PLATFORM_KEY) },
  get token() { return this.get()?.token || null },
  header() { const t = this.token; return t ? { Authorization: `Bearer ${t}` } : {} },
}
