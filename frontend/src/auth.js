// Auth + tenant store. The staff session remembers which restaurant (slug) the
// user signed into, so all staff calls target that tenant. Guest tokens are keyed
// per restaurant+table and kept in memory only.
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

  async login(slug, username, password) {
    const res = await fetch(`/api/r/${slug}/auth/login`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    })
    if (!res.ok) {
      const b = await res.json().catch(() => ({}))
      throw new Error(b.detail || 'Login failed')
    }
    const session = await res.json()          // { token, displayName, role }
    this.setStaff({ ...session, slug })
    return session
  },

  // A completed sign-up returns an owner session; store it with its slug.
  setSession(session, slug) { this.setStaff({ ...session, slug }) },

  _guestTokens: {},
  async guestToken(slug, tableCode) {
    const key = `${slug}:${tableCode}`
    if (this._guestTokens[key]) return this._guestTokens[key]
    const res = await fetch(`/api/r/${slug}/tables/${tableCode}/session`, { method: 'POST' })
    if (!res.ok) throw new Error('Could not start table session')
    const { token } = await res.json()
    this._guestTokens[key] = token
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
