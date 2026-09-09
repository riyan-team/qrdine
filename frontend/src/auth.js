// Auth + tenant store. The staff session remembers which restaurant (slug) the
// user signed into, so all staff calls target that tenant. Guest tokens are keyed
// per restaurant+table and kept in memory only.
import { parseError } from './errors'

const STAFF_KEY = 'qrdine.staff'

export const auth = {
  getStaff() { try { return JSON.parse(localStorage.getItem(STAFF_KEY) || 'null') } catch { return null } },
  setStaff(s) { localStorage.setItem(STAFF_KEY, JSON.stringify(s)) },
  clearStaff() { localStorage.removeItem(STAFF_KEY) },
  get staffToken() { return this.getStaff()?.token || null },
  staffSlug() { return this.getStaff()?.slug || 'desidhaba' },
  role() { return this.getStaff()?.role || null },
  perms() { return this.getStaff()?.permissions || [] },
  can(p) { return this.perms().includes(p) },
  // Plan-driven module flags. Missing (older session) → treat as enabled so nothing
  // vanishes until the next login refreshes the flags.
  features() { return this.getStaff()?.features || null },
  hasFeature(f) { const x = this.features(); return x ? !!x[f] : true },

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
  async guestToken(slug, tableCode) {
    const code = tableCode || this.getTable(slug)
    if (!code) {
      const cached = Object.keys(this._guestTokens).find(k => k.startsWith(`${slug}:`))
      if (cached && this._guestTokens[cached]) return this._guestTokens[cached]
      try {
        const stored = sessionStorage.getItem(`qrdine.guestToken.${slug}`)
        if (stored) return stored
      } catch {}
      return ''
    }
    this.rememberTable(slug, code)
    const key = `${slug}:${code}`
    if (this._guestTokens[key]) return this._guestTokens[key]
    try {
      const stored = sessionStorage.getItem(`qrdine.guestToken.${key}`)
      if (stored) {
        this._guestTokens[key] = stored
        return stored
      }
    } catch {}
    const res = await fetch(`/api/r/${slug}/tables/${code}/session`, { method: 'POST' })
    if (!res.ok) throw new Error('Could not start table session')
    const { token } = await res.json()
    this._guestTokens[key] = token
    try {
      sessionStorage.setItem(`qrdine.guestToken.${slug}`, token)
      sessionStorage.setItem(`qrdine.guestToken.${key}`, token)
    } catch {}
    return token
  },
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
