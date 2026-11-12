import { auth, staffAuth, platformAuth } from './auth'
import { parseError } from './errors'
import { shrinkImage } from './imageShrink'

// Two failures in a row mean the server can't be reached; one success means it's back (see ConnectionBar).
let netFails = 0
const report = (good) => {
  const was = netFails >= 2
  netFails = good ? 0 : netFails + 1
  const now = netFails >= 2
  if (was !== now) window.dispatchEvent(new CustomEvent('dinein:net', { detail: { down: now } }))
}
const fetch = (...args) => window.fetch(...args).then(
  (res) => { report(res.status < 500); return res },
  (err) => { report(false); throw err },
)

// Staff calls target the signed-in restaurant; guest calls take an explicit slug.
const S = () => auth.staffSlug()

async function j(res) {
  if (!res.ok) {
    if (res.status === 401) { auth.clearStaff(); if (location.pathname !== '/login') location.href = '/login' }
    const err = await parseError(res)   // friendly ApiError — never the raw body
    // Paused restaurant: tell the staff screens why nothing is working (see PlanBanner in App.jsx).
    if (err.locked) window.dispatchEvent(new CustomEvent('qrdine:locked', { detail: err.message }))
    throw err
  }
  const t = await res.text()
  return t ? JSON.parse(t) : null
}

// Guest answers: like j(), but a refused table pass never sends a customer to the staff sign-in page.
async function g(res) {
  if (!res.ok) {
    const err = await parseError(res)
    if (err.locked) window.dispatchEvent(new CustomEvent('qrdine:locked', { detail: err.message }))
    throw err
  }
  const t = await res.text()
  return t ? JSON.parse(t) : null
}

// Staff signed in to THIS restaurant act as staff; anyone else on a guest page uses the table pass.
const passFor = async (slug, code = '') =>
  (auth.staffToken && auth.staffSlug() === slug) ? auth.staffToken : await auth.guestToken(slug, code)
const bearer = (token) => (token ? { Authorization: `Bearer ${token}` } : {})

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

// ---- Loyalty & reviews ----
// A guest who joins rewards keeps a member token on this phone (per restaurant); guest calls send it
// as X-Member-Token. Spending points always needs staff approval on the server.
const memberKey = (slug) => `qrdine.member.${slug}`
export const memberStore = {
  get: (slug) => { try { return localStorage.getItem(memberKey(slug)) || '' } catch { return '' } },
  set: (slug, token) => { try { localStorage.setItem(memberKey(slug), token) } catch { /* private mode */ } },
  clear: (slug) => { try { localStorage.removeItem(memberKey(slug)) } catch { /* private mode */ } },
}
const memberHeaders = (slug) => json({ 'X-Member-Token': memberStore.get(slug) })
const guestPost = (slug, path, body) => fetch(`/api/r/${slug}/loyalty${path}`, {
  method: 'POST', headers: memberHeaders(slug), body: body === undefined ? undefined : JSON.stringify(body),
}).then(j)

export const loyalty = {
  // guest (QR page)
  info: (slug) => fetch(`/api/r/${slug}/loyalty`).then(j),
  join: (slug, body) => guestPost(slug, '/join', body),
  me: (slug) => fetch(`/api/r/${slug}/loyalty/me`, { headers: memberHeaders(slug) })
    .then((res) => (res.status === 404 ? null : j(res))),
  link: (slug, tableCode) => guestPost(slug, '/link', { tableCode }),
  redeem: (slug, rewardId, tableCode) => guestPost(slug, '/redeem', { rewardId, tableCode }),
  cancel: (slug, id) => guestPost(slug, `/redemptions/${id}/cancel`),

  // owner / manager
  program: () => staffGet(`/admin/loyalty/program`),
  saveProgram: (p) => staffPut(`/admin/loyalty/program`, p),
  overview: () => staffGet(`/admin/loyalty/overview`),
  rewards: () => staffGet(`/admin/loyalty/rewards`),
  addReward: (r) => staffPost(`/admin/loyalty/rewards`, r),
  updateReward: (id, r) => staffPut(`/admin/loyalty/rewards/${id}`, r),
  deleteReward: (id) => staffDelete(`/admin/loyalty/rewards/${id}`),
  members: (q = '', page = 1) => staffGet(`/admin/loyalty/members?q=${encodeURIComponent(q)}&page=${page}&pageSize=20`),
  member: (id) => staffGet(`/admin/loyalty/members/${id}`),
  adjust: (id, points, note) => staffPost(`/admin/loyalty/members/${id}/adjust`, { points, note }),

  // reception
  redemptions: (status = '') => staffGet(`/admin/loyalty/redemptions${status ? `?status=${status}` : ''}`),
  approve: (id) => staffPost(`/admin/loyalty/redemptions/${id}/approve`),
  reject: (id, note) => staffPost(`/admin/loyalty/redemptions/${id}/reject`, { note }),
  tableMember: (code) => staffGet(`/admin/loyalty/tables/${encodeURIComponent(code)}`),
  attach: (code, phone, name) => staffPost(`/admin/loyalty/tables/${encodeURIComponent(code)}/member`, { phone, name }),
  detach: (code) => staffDelete(`/admin/loyalty/tables/${encodeURIComponent(code)}/member`),
}

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
  // A table's running orders: only that table's own pass (or staff) may read them.
  tableOrders: async (slug, code) => {
    const token = await passFor(slug, code)
    return fetch(`/api/r/${slug}/tables/${code}/orders`, { headers: bearer(token) }).then(g)
  },
  // Takeaway or delivery from the restaurant's order QR: no table, no pass needed. Returns { order, ticket, token }.
  placeGuestTicket: (slug, body) => fetch(`/api/r/${slug}/tickets/guest`, {
    method: 'POST', headers: json(), body: JSON.stringify(body),
  }).then(g),
  placeOrder: async (slug, body) => {
    const token = await auth.guestToken(slug, body.tableCode)
    return fetch(`/api/r/${slug}/orders`, {
      method: 'POST', headers: json(bearer(token)), body: JSON.stringify(body),
    }).then(g)
  },
  getOrder: async (slugOrId, maybeId) => {
    let slug, id
    if (maybeId !== undefined) {
      slug = slugOrId
      id = maybeId
    } else {
      slug = auth.staffSlug()
      id = slugOrId
    }
    const token = await passFor(slug)
    return fetch(`/api/r/${slug}/orders/${id}`, { headers: json(bearer(token)) }).then(g)
  },
  cancelOrder: async (slug, id, reason) => {
    const token = await passFor(slug)
    return fetch(`/api/r/${slug}/orders/${id}/cancel`, {
      method: 'POST', headers: json(bearer(token)), body: JSON.stringify({ reason }),
    }).then(g)
  },
  reorder: async (slug, id) => {
    const token = await passFor(slug)
    return fetch(`/api/r/${slug}/orders/${id}/reorder`, {
      method: 'POST', headers: json(bearer(token)),
    }).then(g)
  },
  submitFeedback: (slug, body) => fetch(`/api/r/${slug}/feedback`, {
    method: 'POST', headers: json(), body: JSON.stringify(body),
  }).then(j),

  // feedback (owner)
  getFeedback: () => staffGet(`/admin/feedback`),

  // guests: which dishes to show as "most ordered"
  getPopular: (slug) => fetch(`/api/r/${slug}/popular`).then(j),

  // supplier khata
  staffPlan: () => staffGet(`/staff/plan`),
  suppliers: () => staffGet(`/admin/suppliers`),
  addSupplier: (s) => staffPost(`/admin/suppliers`, s),
  updateSupplier: (id, s) => staffPut(`/admin/suppliers/${id}`, s),
  archiveSupplier: (id) => staffDelete(`/admin/suppliers/${id}`),
  supplierLedger: (id) => staffGet(`/admin/suppliers/${id}/ledger`),
  addPurchase: (id, p) => staffPost(`/admin/suppliers/${id}/purchases`, p),
  addSupplierPayment: (id, p) => staffPost(`/admin/suppliers/${id}/payments`, p),
  addSupplierReturn: (id, r) => staffPost(`/admin/suppliers/${id}/returns`, r),
  deleteSupplierEntry: (entryId) => staffDelete(`/admin/suppliers/entries/${entryId}`),

  // expenses
  expenses: ({ from = '', to = '', category = '', branchId = null } = {}) => {
    const p = new URLSearchParams()
    if (from) p.set('from', from); if (to) p.set('to', to); if (category) p.set('category', category); if (branchId) p.set('branchId', branchId)
    return staffGet(`/admin/expenses?${p}`)
  },
  addExpense: (e) => staffPost(`/admin/expenses`, e),
  updateExpense: (id, e) => staffPut(`/admin/expenses/${id}`, e),
  deleteExpense: (id) => staffDelete(`/admin/expenses/${id}`),

  // reports and waste
  businessReport: ({ from, to, branchId = null }) => {
    const p = new URLSearchParams({ from, to })
    if (branchId) p.set('branchId', branchId)
    return staffGet(`/admin/reports/business?${p}`)
  },
  wasteStock: (id, quantity, reason) => staffPost(`/admin/stock/${id}/waste`, { quantity, reason }),

  // ---- staff (slug from session) ----
  getTables: () => staffGet(`/tables`),
  // A waiter/reception takes an order on a table's behalf (staff token → any table).
  // Server stamps it Source=Waiter and assigns the signed-in staff member.
  placeStaffOrder: (body) => staffPost(`/orders`, body),
  staffOrders: () => staffGet(`/staff/orders`),
  approve: (id) => staffPost(`/orders/${id}/approve`),
  cook: (id, etaMinutes) => staffPost(`/orders/${id}/cook`, { etaMinutes }),
  ready: (id) => staffPost(`/orders/${id}/ready`),
  assign: (id, waiterName) => staffPost(`/orders/${id}/assign`, { waiterName }),
  serve: (id) => staffPost(`/orders/${id}/serve`),
  pay: (id) => staffPost(`/orders/${id}/pay`),
  cancel: (id, reason = null) => staffPost(`/orders/${id}/cancel`, { reason }),
  voidItem: (orderId, itemId, qty, reason) => staffPost(`/orders/${orderId}/items/${itemId}/void`, { qty, reason }),
  moveTable: (code, toCode) => staffPost(`/tables/${code}/move`, { toCode }),

  // parcel and delivery: each ticket is its own private table, so nothing can land on the wrong bill
  placeTicket: (t) => staffPost(`/tickets`, t),
  tickets: () => staffGet(`/tickets`),
  updateTicket: (code, t) => staffPut(`/tickets/${code}`, t),

  // delivery handling: the promised time, the rider's link, out for delivery, delivered / collected
  shopPoint: () => staffGet(`/tickets/shop`).then((x) => x?.point || null),
  promiseTicket: (code, minutes) => staffPost(`/tickets/${code}/promise`, { minutes }),
  riderLink: (code, riderName, riderPhone) => staffPost(`/tickets/${code}/rider-link`, { riderName, riderPhone }),
  dispatchTicket: (code) => staffPost(`/tickets/${code}/dispatch`),
  deliveredTicket: (code) => staffPost(`/tickets/${code}/delivered`),
  // the guest following their takeaway / delivery (only the phone that placed it, or staff)
  trackTicket: async (slug, code) => {
    const token = await passFor(slug, code)
    return fetch(`/api/r/${slug}/tickets/${code}/track`, { headers: bearer(token) }).then(g)
  },
  // the rider's phone: no account, only the private key from the link reception sent
  ride: (slug, code, k) => fetch(`/api/r/${slug}/ride/${code}?k=${encodeURIComponent(k)}`).then(g),
  rideLocation: (slug, code, k, lat, lng) => fetch(`/api/r/${slug}/ride/${code}/location?k=${encodeURIComponent(k)}`, {
    method: 'POST', headers: json(), body: JSON.stringify({ lat, lng }),
  }).then(g),
  ridePickup: (slug, code, k) => fetch(`/api/r/${slug}/ride/${code}/pickup?k=${encodeURIComponent(k)}`, { method: 'POST' }).then(g),
  rideDelivered: (slug, code, k) => fetch(`/api/r/${slug}/ride/${code}/delivered?k=${encodeURIComponent(k)}`, { method: 'POST' }).then(g),

  // a guest tapping "call waiter" or "ask for the bill"
  callStaff: async (slug, code, kind) => {
    const token = await auth.guestToken(slug, code)
    return fetch(`/api/r/${slug}/tables/${code}/call`, {
      method: 'POST', headers: json(bearer(token)), body: JSON.stringify({ kind }),
    }).then(g)
  },
  serviceRequests: () => staffGet(`/staff/requests`),
  serviceDone: (id) => staffPost(`/staff/requests/${id}/done`),

  // customer khata
  findCustomers: (q) => staffGet(`/staff/customers/find?q=${encodeURIComponent(q)}`),
  customers: () => staffGet(`/admin/customers`),
  addCustomer: (c) => staffPost(`/admin/customers`, c),
  updateCustomer: (id, c) => staffPut(`/admin/customers/${id}`, c),
  archiveCustomer: (id) => staffDelete(`/admin/customers/${id}`),
  customerLedger: (id) => staffGet(`/admin/customers/${id}/ledger`),
  addCustomerPayment: (id, p) => staffPost(`/admin/customers/${id}/payments`, p),
  deleteCustomerEntry: (entryId) => staffDelete(`/admin/customers/entries/${entryId}`),

  // salaries and attendance
  payroll: (month = '') => staffGet(`/admin/payroll${month ? `?month=${month}` : ''}`),
  addEmployee: (e) => staffPost(`/admin/payroll`, e),
  updateEmployee: (id, e) => staffPut(`/admin/payroll/${id}`, e),
  archiveEmployee: (id) => staffDelete(`/admin/payroll/${id}`),
  employeeEntries: (id, month) => staffGet(`/admin/payroll/${id}/entries?month=${month}`),
  addEmployeeEntry: (id, e) => staffPost(`/admin/payroll/${id}/entries`, e),
  deleteEmployeeEntry: (entryId) => staffDelete(`/admin/payroll/entries/${entryId}`),
  attendance: (month) => staffGet(`/admin/payroll/attendance?month=${month}`),
  setAttendance: (a) => staffPut(`/admin/payroll/attendance`, a),

  // end-of-day cash count
  closing: (date = '', branchId = null) => staffGet(`/admin/closing?date=${date}${branchId ? `&branchId=${branchId}` : ''}`),
  closeDay: (c) => staffPost(`/admin/closing`, c),

  // change every price by a percentage
  bulkPrice: (p) => staffPost(`/admin/menu/bulk-price`, p),

  // billing
  getBill: (code, discount = null, method = null) => {
    const p = new URLSearchParams()
    if (discount != null) p.set('discount', discount)
    if (method) p.set('method', method)
    return staffGet(`/tables/${code}/bill?${p}`)
  },
  settle: (code, method, discount, amountTendered = 0, customerId = null) => staffPost(`/tables/${code}/settle`, { method, discount, amountTendered, customerId }),
  payPart: (code, amount, method, discount = null) => staffPost(`/tables/${code}/pay-part`, { amount, method, discount }),
  invoice: (id) => staffGet(`/invoices/${id}`),

  // reception dashboard (today's takings, pending bills, settled history)
  receptionDashboard: (q = '', page = 1, pageSize = 20) =>
    staffGet(`/reception/dashboard?page=${page}&pageSize=${pageSize}${q ? `&q=${encodeURIComponent(q)}` : ''}`),

  // staff order history (kitchen / waiter / reception)
  ordersHistory: ({ status = '', from = '', to = '', q = '', page = 1, pageSize = 20 } = {}) => {
    const p = new URLSearchParams({ page, pageSize })
    if (status) p.set('status', status); if (from) p.set('from', from); if (to) p.set('to', to); if (q) p.set('q', q)
    return staffGet(`/staff/orders/history?${p}`)
  },

  // menu admin
  adminMenu: () => staffGet(`/admin/menu`),
  addCategory: (name, sortOrder) => staffPost(`/admin/categories`, { name, sortOrder }),
  updateCategory: (id, name, sortOrder) => staffPut(`/admin/categories/${id}`, { name, sortOrder }),
  deleteCategory: (id) => staffDelete(`/admin/categories/${id}`),
  createItem: (item) => staffPost(`/admin/items`, item),
  updateItem: (id, item) => staffPut(`/admin/items/${id}`, item),
  setAvailability: (id, available) => staffPost(`/admin/items/${id}/availability`, { available }),
  deleteItem: (id) => staffDelete(`/admin/items/${id}`),
  uploadItemPhoto: async (id, file) => { const fd = new FormData(); fd.append('file', await shrinkImage(file)); return fetch(`/api/r/${S()}/admin/items/${id}/photo`, { method: 'POST', headers: staffAuth(), body: fd }).then(j) },

  // bulk menu import (returns a preview to review before saving)
  importMenuPhoto: (file) => { const fd = new FormData(); fd.append('file', file); return fetch(`/api/r/${S()}/admin/menu/import/photo`, { method: 'POST', headers: staffAuth(), body: fd }).then(j) },
  importMenuCsv: (csv) => fetch(`/api/r/${S()}/admin/menu/import/csv`, { method: 'POST', headers: { 'Content-Type': 'text/csv', ...staffAuth() }, body: csv }).then(j),
  applyMenuImport: (categories) => staffPost(`/admin/menu/import/apply`, { categories }),

  // dashboard
  dashboard: (days = 7, branchId = null) => staffGet(`/admin/dashboard?days=${days}${branchId ? `&branchId=${branchId}` : ''}`),
  // paginated sales report (filters + search + server-side paging)
  salesReport: ({ from = '', to = '', branchId = null, q = '', method = '', page = 1, pageSize = 20 } = {}) => {
    const p = new URLSearchParams({ page, pageSize })
    if (from) p.set('from', from); if (to) p.set('to', to); if (branchId) p.set('branchId', branchId)
    if (q) p.set('q', q); if (method) p.set('method', method)
    return staffGet(`/admin/reports/sales?${p}`)
  },
  downloadInvoicesCsv: async (from, to, branchId = null, q = '', method = '') => {
    const qs = new URLSearchParams()
    if (from) qs.set('from', from); if (to) qs.set('to', to); if (branchId) qs.set('branchId', branchId)
    if (q) qs.set('q', q); if (method) qs.set('method', method)
    const res = await fetch(`/api/r/${S()}/admin/reports/invoices.csv?${qs}`, { headers: staffAuth() })
    if (!res.ok) throw await parseError(res)
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

  // billing settings (tax/GST, discount, receipt, stock)
  getBillingSettings: () => staffGet(`/admin/billing-settings`),
  setBillingSettings: (s) => staffPut(`/admin/billing-settings`, s),

  // receipt branding (logo + printed header/footer)
  getReceiptSettings: () => staffGet(`/admin/receipt-settings`),
  setReceiptSettings: (s) => staffPut(`/admin/receipt-settings`, s),
  getSubscription: () => staffGet(`/admin/subscription`),
  uploadLogo: async (file) => { const fd = new FormData(); fd.append('file', await shrinkImage(file, { maxSide: 512, keepTransparency: true })); return fetch(`/api/r/${S()}/admin/logo`, { method: 'POST', headers: staffAuth(), body: fd }).then(j) },
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
    throw await parseError(res)   // friendly ApiError — never the raw body
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
  resetOwnerPassword: (id) => fetch(`/api/platform/restaurants/${id}/reset-owner-password`, { method: 'POST', headers: ph() }).then(pj),
}

export const money = (n) => 'Rs ' + (Number(n) || 0).toLocaleString('en-PK')
export const count = (n, one, many = `${one}s`) => `${n} ${n === 1 ? one : many}`

export const STATUS_LABEL = {
  Placed: 'Sent to reception', Approved: 'Approved', Cooking: 'Being cooked',
  Ready: 'Ready to serve', Served: 'Served', Paid: 'Paid', Cancelled: 'Cancelled',
}

// The same steps read differently when the food leaves the building.
export const statusLabel = (o) => {
  if (o.channel === 'Takeaway') {
    if (o.status === 'Ready') return 'Ready for pickup'
    if (o.status === 'Served') return 'Handed over'
  }
  if (o.channel === 'Delivery') {
    if (o.status === 'Ready') return 'Ready for rider'
    if (o.status === 'Served') return 'Out for delivery'
  }
  return STATUS_LABEL[o.status] || o.status
}

// The "Cashier" role is presented as "Reception" everywhere in the UI (the stored
// role value stays "Cashier" for tokens/DB compatibility).
export const ROLE_LABEL = { Cashier: 'Reception' }
export const roleLabel = (r) => ROLE_LABEL[r] || r
