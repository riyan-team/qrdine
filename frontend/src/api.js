import { auth, staffAuth, platformAuth } from './auth'

// Staff calls target the signed-in restaurant; guest calls take an explicit slug.
const S = () => auth.staffSlug()

async function j(res) {
  if (!res.ok) {
    if (res.status === 401) { auth.clearStaff(); if (location.pathname !== '/login') location.href = '/login' }
    const text = await res.text()
    try { throw new Error(JSON.parse(text).detail || text) } catch { throw new Error(text || res.statusText) }
  }
  const t = await res.text()
  return t ? JSON.parse(t) : null
}

const json = (extra = {}) => ({ 'Content-Type': 'application/json', ...extra })

// Staff-authed requests against the signed-in restaurant.
const staffPost = (path, body) => fetch(`/api/r/${S()}${path}`, {
  method: 'POST', headers: json(staffAuth()), body: body === undefined ? undefined : JSON.stringify(body),
}).then(j)
const staffPut = (path, body) => fetch(`/api/r/${S()}${path}`, {
  method: 'PUT', headers: json(staffAuth()), body: JSON.stringify(body),
}).then(j)
const staffDelete = (path) => fetch(`/api/r/${S()}${path}`, { method: 'DELETE', headers: staffAuth() }).then(j)
const staffGet = (path) => fetch(`/api/r/${S()}${path}`, { headers: staffAuth() }).then(j)

// Public: resolve a typed restaurant name or address to its slug (login helper).
export const lookupRestaurant = (q) =>
  fetch(`/api/restaurants/lookup?q=${encodeURIComponent(q)}`).then(async (res) => {
    if (res.status === 404) return null
    if (!res.ok) throw new Error('Lookup failed')
    return res.json()   // { slug, name }
  })

export const api = {
  // ---- guest (slug from the QR URL) ----
  getMenu: (slug) => fetch(`/api/r/${slug}`).then(j),
  tableOrders: (slug, code) => fetch(`/api/r/${slug}/tables/${code}/orders`).then(j),
  placeOrder: async (slug, body) => {
    const token = await auth.guestToken(slug, body.tableCode)
    return fetch(`/api/r/${slug}/orders`, {
      method: 'POST', headers: json({ Authorization: `Bearer ${token}` }), body: JSON.stringify(body),
    }).then(j)
  },

  // ---- staff (slug from session) ----
  getTables: () => staffGet(`/tables`),
  staffOrders: () => staffGet(`/staff/orders`),
  approve: (id) => staffPost(`/orders/${id}/approve`),
  cook: (id, etaMinutes) => staffPost(`/orders/${id}/cook`, { etaMinutes }),
  ready: (id) => staffPost(`/orders/${id}/ready`),
  assign: (id, waiterName) => staffPost(`/orders/${id}/assign`, { waiterName }),
  serve: (id) => staffPost(`/orders/${id}/serve`),
  pay: (id) => staffPost(`/orders/${id}/pay`),
  cancel: (id) => staffPost(`/orders/${id}/cancel`),

  // billing
  getBill: (code) => staffGet(`/tables/${code}/bill`),
  settle: (code, method, discount) => staffPost(`/tables/${code}/settle`, { method, discount }),
  payPart: (code, amount, method) => staffPost(`/tables/${code}/pay-part`, { amount, method }),

  // menu admin
  adminMenu: () => staffGet(`/admin/menu`),
  addCategory: (name, sortOrder) => staffPost(`/admin/categories`, { name, sortOrder }),
  updateCategory: (id, name, sortOrder) => staffPut(`/admin/categories/${id}`, { name, sortOrder }),
  deleteCategory: (id) => staffDelete(`/admin/categories/${id}`),
  createItem: (item) => staffPost(`/admin/items`, item),
  updateItem: (id, item) => staffPut(`/admin/items/${id}`, item),
  setAvailability: (id, available) => staffPost(`/admin/items/${id}/availability`, { available }),
  deleteItem: (id) => staffDelete(`/admin/items/${id}`),
  uploadItemPhoto: (id, file) => { const fd = new FormData(); fd.append('file', file); return fetch(`/api/r/${S()}/admin/items/${id}/photo`, { method: 'POST', headers: staffAuth(), body: fd }).then(j) },

  // dashboard
  dashboard: (days = 7, branchId = null) => staffGet(`/admin/dashboard?days=${days}${branchId ? `&branchId=${branchId}` : ''}`),
  downloadInvoicesCsv: async (from, to, branchId = null) => {
    const qs = new URLSearchParams()
    if (from) qs.set('from', from); if (to) qs.set('to', to); if (branchId) qs.set('branchId', branchId)
    const res = await fetch(`/api/r/${S()}/admin/reports/invoices.csv?${qs}`, { headers: staffAuth() })
    if (!res.ok) throw new Error('Could not download the report.')
    const blob = await res.blob()
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a'); a.href = url; a.download = `invoices_${from || 'all'}_${to || 'now'}.csv`
    document.body.appendChild(a); a.click(); a.remove(); URL.revokeObjectURL(url)
  },

  // branches
  listBranches: () => staffGet(`/admin/branches`),
  addBranch: (b) => staffPost(`/admin/branches`, b),
  updateBranch: (id, b) => staffPut(`/admin/branches/${id}`, b),
  deleteBranch: (id) => staffDelete(`/admin/branches/${id}`),

  // tables admin
  listTables: () => staffGet(`/admin/tables`),
  addTable: (code, name, branchId) => staffPost(`/admin/tables`, { code, name, branchId }),
  updateTable: (id, code, name, branchId) => staffPut(`/admin/tables/${id}`, { code, name, branchId }),
  deleteTable: (id) => staffDelete(`/admin/tables/${id}`),

  // staff admin
  listStaff: () => staffGet(`/admin/staff`),
  addStaff: (s) => staffPost(`/admin/staff`, s),
  updateStaff: (id, s) => staffPut(`/admin/staff/${id}`, s),
  resetStaffPassword: (id, password) => staffPost(`/admin/staff/${id}/password`, { password }),
  deleteStaff: (id) => staffDelete(`/admin/staff/${id}`),

  // FBR fiscal
  getFbrSettings: () => staffGet(`/admin/fbr`),
  setFbrSettings: (s) => staffPut(`/admin/fbr`, s),
  getServiceSettings: () => staffGet(`/admin/service-settings`),
  setServiceSettings: (s) => staffPut(`/admin/service-settings`, s),
  changePassword: (currentPassword, newPassword) => staffPost(`/auth/change-password`, { currentPassword, newPassword }),
  fbrSynced: () => staffGet(`/admin/fbr/invoices/synced`),
  fbrPending: () => staffGet(`/admin/fbr/invoices/pending`),
  fbrRetry: (id) => staffPost(`/admin/fbr/invoices/${id}/retry`),

  // permissions (owner only)
  getPermissions: () => staffGet(`/admin/permissions`),
  setPermissions: (role, permissions) => staffPut(`/admin/permissions`, { role, permissions }),

  // inventory
  listStock: () => staffGet(`/admin/stock`),
  addStock: (s) => staffPost(`/admin/stock`, s),
  updateStock: (id, s) => staffPut(`/admin/stock/${id}`, s),
  deleteStock: (id) => staffDelete(`/admin/stock/${id}`),
  restock: (id, quantity, reason) => staffPost(`/admin/stock/${id}/restock`, { quantity, reason }),
  adjustStock: (id, newQuantity, reason) => staffPost(`/admin/stock/${id}/adjust`, { newQuantity, reason }),
  getRecipe: (itemId) => staffGet(`/admin/items/${itemId}/recipe`),
  setRecipe: (itemId, lines) => staffPut(`/admin/items/${itemId}/recipe`, { lines }),
  movements: () => staffGet(`/admin/movements`),
}

// Public sign-up.
export const onboard = (body) => fetch(`/api/onboard`, {
  method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
}).then(j)

// Public: active subscription plans (for the sign-up banners).
export const getPlans = () => fetch(`/api/plans`).then(j)

// Start a payment (buy a plan).
export const checkout = (amountPkr, reference, description) => fetch('/api/payments/checkout', {
  method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ amountPkr, reference, description }),
}).then(j)

// ---- platform operator API (separate auth + 401 handling) ----
async function pj(res) {
  if (!res.ok) {
    if (res.status === 401) { platformAuth.clear(); if (location.pathname !== '/platform/login') location.href = '/platform/login' }
    const text = await res.text()
    try { throw new Error(JSON.parse(text).detail || text) } catch { throw new Error(text || res.statusText) }
  }
  const t = await res.text(); return t ? JSON.parse(t) : null
}
const ph = () => ({ 'Content-Type': 'application/json', ...platformAuth.header() })
export const platform = {
  login: (username, password) => fetch('/api/platform/login', {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ username, password }),
  }).then(pj),
  stats: () => fetch('/api/platform/stats', { headers: platformAuth.header() }).then(pj),
  restaurants: () => fetch('/api/platform/restaurants', { headers: platformAuth.header() }).then(pj),
  setPlan: (id, planId) => fetch(`/api/platform/restaurants/${id}/plan`, { method: 'POST', headers: ph(), body: JSON.stringify({ planId }) }).then(pj),
  setStatus: (id, status) => fetch(`/api/platform/restaurants/${id}/status`, { method: 'POST', headers: ph(), body: JSON.stringify({ status }) }).then(pj),
  recordPayment: (id, body) => fetch(`/api/platform/restaurants/${id}/payment`, { method: 'POST', headers: ph(), body: JSON.stringify(body) }).then(pj),
  payments: (id) => fetch(`/api/platform/restaurants/${id}/payments`, { headers: platformAuth.header() }).then(pj),
  setFbr: (id, enabled) => fetch(`/api/platform/restaurants/${id}/fbr`, { method: 'POST', headers: ph(), body: JSON.stringify({ enabled }) }).then(pj),
  plans: () => fetch('/api/platform/plans', { headers: platformAuth.header() }).then(pj),
  createPlan: (p) => fetch('/api/platform/plans', { method: 'POST', headers: ph(), body: JSON.stringify(p) }).then(pj),
  updatePlan: (id, p) => fetch(`/api/platform/plans/${id}`, { method: 'PUT', headers: ph(), body: JSON.stringify(p) }).then(pj),
  deletePlan: (id) => fetch(`/api/platform/plans/${id}`, { method: 'DELETE', headers: platformAuth.header() }).then(pj),
  deleteRestaurant: (id) => fetch(`/api/platform/restaurants/${id}`, { method: 'DELETE', headers: platformAuth.header() }).then(pj),
}

export const money = (n) => 'Rs ' + Number(n).toLocaleString('en-PK')

export const STATUS_LABEL = {
  Placed: 'Sent to reception', Approved: 'Approved', Cooking: 'Being cooked',
  Ready: 'Ready to serve', Served: 'Served', Paid: 'Paid', Cancelled: 'Cancelled',
}
