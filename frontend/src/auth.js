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

/** A takeaway/delivery ticket (P251112-3, D251112-1) rather than a real table. */
export const isTicketCode = (code) => /^[PD]\d{6}-\d+$/i.test(code || '')
/** "Takeaway #3" / "Delivery #1" for a ticket code. */
export const ticketLabel = (code) => `${/^p/i.test(code) ? 'Takeaway' : 'Delivery'} #${String(code).split('-')[1] || ''}`

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
  // The secret part of a table's QR code (?k=…). A pass is only given with it, so "T1" alone opens nothing.
  rememberTableKey(slug, tableCode, k) {
    if (!slug || !tableCode || !k) return
    // A fresh scan starts a fresh pass: an older one on this phone may have ended while the page was closed.
    const same = (c) => String(c).toUpperCase() === String(tableCode).toUpperCase()
    for (const t of Object.keys(this._guestTokens)) if (t.startsWith(`${slug}:`) && same(t.slice(slug.length + 1))) delete this._guestTokens[t]
    try {
      for (let i = sessionStorage.length - 1; i >= 0; i--) {
        const s = sessionStorage.key(i) || ''
        if (s.startsWith(`qrdine.guestToken.${slug}:`) && same(s.slice(`qrdine.guestToken.${slug}:`.length))) sessionStorage.removeItem(s)
      }
      sessionStorage.setItem(`qrdine.tablekey.${slug}:${String(tableCode).toUpperCase()}`, String(k).trim())
      sessionStorage.removeItem(`qrdine.passEnded.${slug}`)
    } catch {}
  },
  // Whether this phone can order at this table: it scanned the table's QR code, or still holds a pass from it.
  canOrderAt(slug, tableCode) {
    if (this.staffToken && this.staffSlug() === slug) return true
    if (this.tableKey(slug, tableCode)) return true
    const key = `${slug}:${tableCode}`
    if (fresh(this._guestTokens[key])) return true
    try { return fresh(sessionStorage.getItem(`qrdine.guestToken.${key}`)) } catch { return false }
  },
  tableKey(slug, tableCode) {
    try { return sessionStorage.getItem(`qrdine.tablekey.${slug}:${String(tableCode).toUpperCase()}`) || '' } catch { return '' }
  },
  // The table's bill was settled (or the guests moved): this phone's table passes and keys go, so it can't
  // follow the next guests' orders. Scanning the table's QR code again starts fresh.
  endTablePasses(slug) {
    for (const k of Object.keys(this._guestTokens)) if (k.startsWith(`${slug}:`) && !isTicketCode(k.slice(slug.length + 1))) delete this._guestTokens[k]
    try {
      for (let i = sessionStorage.length - 1; i >= 0; i--) {
        const k = sessionStorage.key(i) || ''
        if (k === `qrdine.guestToken.${slug}`
          || (k.startsWith(`qrdine.guestToken.${slug}:`) && !isTicketCode(k.slice(`qrdine.guestToken.${slug}:`.length)))
          || k.startsWith(`qrdine.tablekey.${slug}:`)) sessionStorage.removeItem(k)
      }
      sessionStorage.setItem(`qrdine.passEnded.${slug}`, '1')
    } catch {}
  },
  passEnded(slug) {
    try { return sessionStorage.getItem(`qrdine.passEnded.${slug}`) === '1' } catch { return false }
  },
  // A takeaway/delivery ticket's pass. The server hands it out once, when the guest places the order,
  // and never again (ticket numbers are easy to guess), so it is kept on this phone to follow the order.
  keepTicket(slug, ticket, token) {
    const key = `${slug}:${ticket.tableCode}`
    this._guestTokens[key] = token
    try {
      localStorage.setItem(`qrdine.ticket.${key}`, JSON.stringify({
        token, code: ticket.tableCode, name: ticket.tableName, channel: ticket.channel, at: Date.now(),
      }))
      sessionStorage.setItem(`qrdine.guestToken.${key}`, token)
    } catch {}
  },
  /** This phone's takeaway/delivery orders at this restaurant that can still be followed, newest first. */
  myTickets(slug) {
    const out = []
    try {
      for (let i = localStorage.length - 1; i >= 0; i--) {
        const k = localStorage.key(i)
        if (!k || !k.startsWith(`qrdine.ticket.${slug}:`)) continue
        const t = JSON.parse(localStorage.getItem(k) || 'null')
        if (t && fresh(t.token)) out.push(t)
        else localStorage.removeItem(k)
      }
    } catch {}
    return out.sort((a, b) => b.at - a.at)
  },
  _ticketToken(key) {
    try { const t = JSON.parse(localStorage.getItem(`qrdine.ticket.${key}`) || 'null'); return fresh(t?.token) ? t.token : '' } catch { return '' }
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
    const kept = this._ticketToken(key)
    if (kept) { this._guestTokens[key] = kept; return kept }
    if (isTicketCode(code)) throw new Error('This order can only be followed on the phone that placed it.')
    // The menu and the live feed ask at the same moment: one request serves both.
    if (!this._pending[key]) {
      this._pending[key] = (async () => {
        const k = this.tableKey(slug, code)
        if (!k) throw Object.assign(new Error('Please scan the QR code on your table to order.'), { needScan: true })
        const res = await fetch(`/api/r/${slug}/tables/${code}/session`, {
          method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ key: k }),
        })
        if (!res.ok) {
          let msg = 'Please scan the QR code on your table to order.'
          try { const b = await res.json(); if (typeof b?.detail === 'string') msg = b.detail } catch {}
          throw Object.assign(new Error(msg), { needScan: true })
        }
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
