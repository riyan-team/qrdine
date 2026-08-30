import { useEffect, useState } from 'react'
import { api } from '../api'
import { auth } from '../auth'
import { ui } from '../ui.jsx'

const ROLES = ['Owner', 'Manager', 'Cashier', 'Kitchen', 'Waiter']

const PERM_LABELS = {
  'orders.create': 'Take orders (waiter/reception)',
  'orders.approve': 'Approve orders', 'orders.cook': 'Kitchen: cook & ready',
  'orders.serve': 'Waiter: assign & serve', 'orders.pay': 'Billing & payment',
  'orders.cancel': 'Cancel orders', 'menu.manage': 'Manage menu',
  'inventory.manage': 'Manage inventory', 'settings.manage': 'Manage tables & staff',
  'reports.view': 'View reports & dashboard', 'invoice.sync': 'Sync invoices with FBR',
}

// Which plan feature each permission belongs to (unlisted = always available).
// Permissions for modules not in the plan are hidden from the editor.
const PERM_FEATURE = {
  'orders.create': 'ordering',
  'orders.approve': 'ordering', 'orders.pay': 'ordering', 'orders.cancel': 'ordering',
  'orders.cook': 'kitchen', 'orders.serve': 'waiter',
  'inventory.manage': 'inventory', 'reports.view': 'reports', 'invoice.sync': 'fbr',
}
const permInPlan = (perm) => { const f = PERM_FEATURE[perm]; return !f || auth.hasFeature(f) }

// Owner/manager settings: manage tables, staff and (owner-only) permissions.
export default function Settings() {
  const isOwner = auth.role() === 'Owner'
  return (
    <div className="wrap">
      <div className="h">Settings</div>
      <AccountSettings />
      <ServiceSettings />
      <FbrSettings />
      <Branches />
      <div className="grid g2">
        <Tables />
        <Staff />
      </div>
      {isOwner && <Permissions />}
    </div>
  )
}

// The signed-in user changes their own password.
function AccountSettings() {
  const [cur, setCur] = useState('')
  const [nw, setNw] = useState('')
  const [msg, setMsg] = useState('')
  const [err, setErr] = useState('')
  const [busy, setBusy] = useState(false)
  const staff = auth.getStaff()
  const save = async () => {
    setErr(''); setMsg(''); setBusy(true)
    try { await api.changePassword(cur, nw); setMsg('Password updated.'); setCur(''); setNw(''); setTimeout(() => setMsg(''), 2000) }
    catch (e) { setErr(e.message) }
    setBusy(false)
  }
  return (
    <div className="card" style={{ marginBottom: 16 }}>
      <div className="row between"><b>Your account</b><span className="muted" style={{ fontSize: 13 }}>{staff?.displayName} · {staff?.role}</span></div>
      <p className="muted" style={{ fontSize: 13, margin: '4px 0 10px' }}>Change your own password.</p>
      <div className="row" style={{ gap: 8, flexWrap: 'wrap' }}>
        <input type="password" placeholder="Current password" value={cur} onChange={(e) => setCur(e.target.value)} style={{ flex: 1, minWidth: 160 }} />
        <input type="password" placeholder="New password (6+)" value={nw} onChange={(e) => setNw(e.target.value)} style={{ flex: 1, minWidth: 160 }} />
        <button className="primary" disabled={busy || !cur || nw.length < 6} onClick={save}>{msg || 'Update password'}</button>
      </div>
      {err && <div style={{ color: 'var(--red)', fontSize: 13, marginTop: 6 }}>{err}</div>}
    </div>
  )
}

// How waiters get attached to tables. Manual (default) vs automatic least-busy.
function ServiceSettings() {
  const [s, setS] = useState(null)
  const [msg, setMsg] = useState('')
  useEffect(() => { api.getServiceSettings().then(setS).catch(() => {}) }, [])
  if (!s) return null
  const save = async (val) => {
    setS({ waiterAutoAssign: val })
    try { await api.setServiceSettings({ waiterAutoAssign: val }); setMsg('Saved'); setTimeout(() => setMsg(''), 1200) }
    catch (e) { ui.alert(e.message) }
  }
  return (
    <div className="card" style={{ marginBottom: 16 }}>
      <div className="row between"><b>Waiter assignment</b>{msg && <span className="muted" style={{ fontSize: 13 }}>{msg}</span>}</div>
      <p className="muted" style={{ fontSize: 13, margin: '4px 0 10px' }}>How a waiter gets attached to a table’s orders.</p>
      <label className="row" style={{ gap: 8, marginBottom: 8, cursor: 'pointer', alignItems: 'flex-start' }}>
        <input type="radio" name="wa" checked={!s.waiterAutoAssign} onChange={() => save(false)} style={{ marginTop: 3 }} />
        <span><b>Manual</b> — waiters take their own tables (or a manager assigns). <span className="muted">Default. Best if you run fixed sections.</span></span>
      </label>
      <label className="row" style={{ gap: 8, cursor: 'pointer', alignItems: 'flex-start' }}>
        <input type="radio" name="wa" checked={s.waiterAutoAssign} onChange={() => save(true)} style={{ marginTop: 3 }} />
        <span><b>Automatic</b> — each approved order goes to the least-busy waiter. <span className="muted">Needs staff with the Waiter role.</span></span>
      </label>
    </div>
  )
}

// Restaurant-level FBR settings. The enable switch only works when the platform
// operator has turned FBR on for this account (featureFbr).
function FbrSettings() {
  const [s, setS] = useState(null)
  const [msg, setMsg] = useState('')
  const [err, setErr] = useState('')
  const load = () => api.getFbrSettings().then(setS).catch((e) => setErr(e.message))
  useEffect(() => { load() }, [])
  if (!s) return null

  const set = (k, v) => setS((x) => ({ ...x, [k]: v }))
  const save = async () => {
    setErr(''); setMsg('')
    try {
      await api.setFbrSettings({ fbrEnabled: s.fbrEnabled, businessName: s.businessName, ntn: s.ntn, strn: s.strn })
      setMsg('Saved.'); setTimeout(() => setMsg(''), 1500)
    } catch (e) { setErr(e.message) }
  }

  return (
    <div className="card" style={{ marginBottom: 16 }}>
      <div className="row between">
        <b>FBR fiscal integration</b>
        {!s.featureFbr && <span className="pill s-Placed">Not in your plan</span>}
      </div>
      <p className="muted" style={{ fontSize: 13, margin: '4px 0 10px' }}>
        {s.featureFbr
          ? 'File each settled invoice with FBR. Turn it on, set your seller identity, then configure each branch’s POS (mode + POSID) below.'
          : 'FBR filing isn’t enabled for your account. Ask the operator to enable it for your restaurant.'}
      </p>
      <label className="row" style={{ gap: 8, marginBottom: 10 }}>
        <input type="checkbox" disabled={!s.featureFbr} checked={s.fbrEnabled} onChange={(e) => set('fbrEnabled', e.target.checked)} />
        <span>Sync invoices with FBR</span>
      </label>
      <div className="row" style={{ gap: 8, flexWrap: 'wrap' }}>
        <input placeholder="Registered business name" value={s.businessName || ''} onChange={(e) => set('businessName', e.target.value)} style={{ flex: 2, minWidth: 180 }} />
        <input placeholder="NTN" value={s.ntn || ''} onChange={(e) => set('ntn', e.target.value)} style={{ flex: 1, minWidth: 110 }} />
        <input placeholder="STRN" value={s.strn || ''} onChange={(e) => set('strn', e.target.value)} style={{ flex: 1, minWidth: 110 }} />
        <button className="primary" disabled={!s.featureFbr} onClick={save}>{msg || 'Save'}</button>
      </div>
      {err && <div style={{ color: 'var(--red)', fontSize: 13, marginTop: 6 }}>{err}</div>}
    </div>
  )
}

function Branches() {
  const [rows, setRows] = useState([])
  const [f, setF] = useState({ name: '', address: '' })
  const [err, setErr] = useState('')
  const load = () => api.listBranches().then(setRows).catch((e) => setErr(e.message))
  useEffect(() => { load() }, [])

  const add = async () => {
    setErr('')
    try { await api.addBranch({ name: f.name.trim(), address: f.address.trim(), isActive: true }); setF({ name: '', address: '' }); load() }
    catch (e) { setErr(e.message) }
  }
  const rename = async (b) => { const name = await ui.prompt({ title: 'Rename branch', defaultValue: b.name, confirmText: 'Rename', required: true }); if (name && name !== b.name) { await api.updateBranch(b.id, { ...b, name }); load() } }
  const del = async (b) => { if (await ui.confirm({ title: 'Delete branch', message: `Delete branch "${b.name}"?`, confirmText: 'Delete', danger: true })) { try { await api.deleteBranch(b.id); load() } catch (e) { ui.alert(e.message) } } }
  const [fbrFor, setFbrFor] = useState(null)

  return (
    <div className="card" style={{ marginBottom: 16 }}>
      <b>Branches</b>
      <p className="muted" style={{ fontSize: 13, margin: '4px 0 10px' }}>Each location has its own tables, QR codes and its own FBR POS registration. Sales report per branch.</p>
      {rows.map((b) => (
        <div key={b.id} style={{ borderBottom: '1px solid var(--line)' }}>
          <div className="row between" style={{ padding: '7px 0' }}>
            <span><b>{b.name}</b> <span className="muted">· {b.address || 'no address'} · {b.tables} table(s)
              {b.fbrPosId ? ` · FBR ${b.fbrMode} POS ${b.fbrPosId}` : ''}</span></span>
            <div className="row" style={{ gap: 6 }}>
              <button className="ghost" onClick={() => setFbrFor(fbrFor === b.id ? null : b.id)}>FBR POS</button>
              <button className="ghost" onClick={() => rename(b)}>Rename</button>
              <button className="ghost" onClick={() => del(b)}>🗑</button>
            </div>
          </div>
          {fbrFor === b.id && <BranchFbr b={b} onSaved={() => { setFbrFor(null); load() }} />}
        </div>
      ))}
      <div className="row" style={{ gap: 8, marginTop: 10 }}>
        <input placeholder="Branch name" value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} style={{ flex: 1 }} />
        <input placeholder="Address" value={f.address} onChange={(e) => setF({ ...f, address: e.target.value })} style={{ flex: 1 }} />
        <button className="primary" disabled={!f.name.trim()} onClick={add}>Add branch</button>
      </div>
      {err && <div style={{ color: 'var(--red)', fontSize: 13, marginTop: 6 }}>{err}</div>}
    </div>
  )
}

// Per-branch FBR POS registration: Local (IMS component) vs Cloud gateway, the
// branch's default POSID/token, and (cloud) the static IP registered with FBR.
function BranchFbr({ b, onSaved }) {
  const [f, setF] = useState({
    name: b.name, address: b.address, isActive: b.isActive,
    fbrMode: b.fbrMode || 'Local', fbrSandbox: b.fbrSandbox ?? true,
    fbrPosId: b.fbrPosId || '', fbrToken: b.fbrToken || '',
    fbrStaticIp: b.fbrStaticIp || '', fbrEndpointOverride: b.fbrEndpointOverride || '',
  })
  const [err, setErr] = useState('')
  const set = (k, v) => setF((x) => ({ ...x, [k]: v }))
  const save = async () => {
    setErr('')
    try {
      await api.updateBranch(b.id, {
        ...f, fbrPosId: f.fbrPosId ? Number(f.fbrPosId) : null,
        fbrToken: f.fbrToken || null, fbrEndpointOverride: f.fbrEndpointOverride || null,
      })
      onSaved()
    } catch (e) { setErr(e.message) }
  }

  return (
    <div style={{ background: 'var(--surface2)', borderRadius: 8, margin: '4px 0 8px', padding: 12 }}>
      <div className="row" style={{ gap: 8, flexWrap: 'wrap', marginBottom: 8 }}>
        <label className="row" style={{ gap: 6 }}>
          <span className="muted" style={{ fontSize: 13 }}>Mode</span>
          <select value={f.fbrMode} onChange={(e) => set('fbrMode', e.target.value)}>
            <option value="Local">Local (IMS on POS PC)</option>
            <option value="Cloud">Cloud (FBR gateway)</option>
          </select>
        </label>
        <label className="row" style={{ gap: 6 }}>
          <input type="checkbox" checked={f.fbrSandbox} onChange={(e) => set('fbrSandbox', e.target.checked)} />
          <span style={{ fontSize: 13 }}>Sandbox (test)</span>
        </label>
      </div>
      <div className="row" style={{ gap: 8, flexWrap: 'wrap', marginBottom: 8 }}>
        <input placeholder="POSID (from FBR)" value={f.fbrPosId} onChange={(e) => set('fbrPosId', e.target.value)} style={{ width: 150 }} />
        {f.fbrMode === 'Cloud' && <input placeholder="Bearer token" value={f.fbrToken} onChange={(e) => set('fbrToken', e.target.value)} style={{ flex: 1, minWidth: 160 }} />}
        {f.fbrMode === 'Cloud' && <input placeholder="Registered static IP" value={f.fbrStaticIp} onChange={(e) => set('fbrStaticIp', e.target.value)} style={{ width: 150 }} />}
      </div>
      <div className="muted" style={{ fontSize: 12, marginBottom: 8 }}>
        {f.fbrMode === 'Local'
          ? 'Local mode posts to the FBR IMS component on this POS PC — no IP whitelisting. Owner/manager can sync.'
          : 'Cloud mode posts to FBR’s gateway with the token above; the static IP must be the one registered with FBR.'}
      </div>
      <div className="row" style={{ gap: 8 }}>
        <button className="primary" onClick={save}>Save FBR settings</button>
      </div>
      {err && <div style={{ color: 'var(--red)', fontSize: 13, marginTop: 6 }}>{err}</div>}
    </div>
  )
}

function Permissions() {
  const [matrix, setMatrix] = useState(null)
  const [draft, setDraft] = useState({})   // { role: Set(perms) }
  const [savedRole, setSavedRole] = useState('')
  const roles = ['Manager', 'Cashier', 'Kitchen', 'Waiter']

  const load = () => api.getPermissions().then((m) => {
    setMatrix(m)
    const d = {}; roles.forEach((r) => { d[r] = new Set(m.roles[r] || []) }); setDraft(d)
  }).catch(() => {})
  useEffect(() => { load() }, [])
  if (!matrix) return null

  const toggle = (role, perm) => setDraft((d) => {
    const s = new Set(d[role]); s.has(perm) ? s.delete(perm) : s.add(perm)
    return { ...d, [role]: s }
  })
  const save = async (role) => { await api.setPermissions(role, [...draft[role]]); setSavedRole(role); setTimeout(() => setSavedRole(''), 1500) }

  return (
    <div className="card" style={{ marginTop: 16 }}>
      <b>Role permissions</b>
      <p className="muted" style={{ fontSize: 13, margin: '4px 0 12px' }}>Choose what each role can do. Owner always has full access. Staff see changes next time they sign in.</p>
      <div style={{ overflowX: 'auto' }}>
        <table className="tbl" style={{ minWidth: 560 }}>
          <thead>
            <tr><th>Permission</th>{roles.map((r) => <th key={r} style={{ textAlign: 'center' }}>{r}</th>)}</tr>
          </thead>
          <tbody>
            {matrix.all.filter(permInPlan).map((perm) => (
              <tr key={perm}>
                <td>{PERM_LABELS[perm] || perm}</td>
                {roles.map((r) => (
                  <td key={r} style={{ textAlign: 'center' }}>
                    <input type="checkbox" checked={draft[r]?.has(perm) || false} onChange={() => toggle(r, perm)} />
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="row" style={{ gap: 8, marginTop: 12, flexWrap: 'wrap' }}>
        {roles.map((r) => (
          <button key={r} className="primary" onClick={() => save(r)}>
            {savedRole === r ? '✓ Saved' : `Save ${r}`}
          </button>
        ))}
      </div>
    </div>
  )
}

function Tables() {
  const [rows, setRows] = useState([])
  const [branches, setBranches] = useState([])
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [branchId, setBranchId] = useState('')
  const [err, setErr] = useState('')
  const load = () => {
    api.listTables().then(setRows).catch((e) => setErr(e.message))
    api.listBranches().then((b) => { setBranches(b); if (!branchId && b[0]) setBranchId(b[0].id) }).catch(() => {})
  }
  useEffect(() => { load() }, [])

  const add = async () => {
    setErr('')
    try { await api.addTable(code.trim(), name.trim(), Number(branchId)); setCode(''); setName(''); load() }
    catch (e) { setErr(e.message) }
  }
  const rename = async (t) => {
    const name = await ui.prompt({ title: `Rename ${t.code}`, defaultValue: t.name, confirmText: 'Rename', required: true })
    if (name && name !== t.name) { await api.updateTable(t.id, t.code, name, t.branchId); load() }
  }
  const moveBranch = async (t, bid) => { await api.updateTable(t.id, t.code, t.name, Number(bid)); load() }
  const del = async (t) => {
    if (!await ui.confirm({ title: 'Delete table', message: `Delete ${t.code}?`, confirmText: 'Delete', danger: true })) return
    try { await api.deleteTable(t.id); load() } catch (e) { ui.alert(e.message) }
  }

  return (
    <div className="card">
      <b>Tables</b>
      <div style={{ margin: '10px 0' }}>
        {rows.map((t) => (
          <div key={t.id} className="row between" style={{ padding: '7px 0', borderBottom: '1px solid var(--line)' }}>
            <span><b>{t.code}</b> <span className="muted">· {t.name}</span></span>
            <div className="row" style={{ gap: 6 }}>
              {branches.length > 1 && (
                <select value={t.branchId} onChange={(e) => moveBranch(t, e.target.value)} style={{ padding: '4px 6px', fontSize: 12 }}>
                  {branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
                </select>
              )}
              <button className="ghost" onClick={() => rename(t)}>Rename</button>
              <button className="ghost" onClick={() => del(t)}>🗑</button>
            </div>
          </div>
        ))}
        {rows.length === 0 && <div className="muted" style={{ fontSize: 13 }}>No tables yet.</div>}
      </div>
      <div className="row" style={{ gap: 8, flexWrap: 'wrap' }}>
        <input placeholder="Code (T7)" value={code} onChange={(e) => setCode(e.target.value)} style={{ width: 90 }} />
        <input placeholder="Name" value={name} onChange={(e) => setName(e.target.value)} style={{ flex: 1, minWidth: 100 }} />
        {branches.length > 1 && (
          <select value={branchId} onChange={(e) => setBranchId(e.target.value)}>
            {branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
          </select>
        )}
        <button className="primary" disabled={!code.trim() || !name.trim()} onClick={add}>Add</button>
      </div>
      {err && <div style={{ color: 'var(--red)', fontSize: 13, marginTop: 6 }}>{err}</div>}
    </div>
  )
}

function Staff() {
  const [rows, setRows] = useState([])
  const [f, setF] = useState({ username: '', displayName: '', role: 'Cashier', password: '', fbrPosId: '', fbrToken: '' })
  const [err, setErr] = useState('')
  const [posFor, setPosFor] = useState(null)
  const load = () => api.listStaff().then(setRows).catch((e) => setErr(e.message))
  useEffect(() => { load() }, [])
  const set = (k, v) => setF((s) => ({ ...s, [k]: v }))

  const add = async () => {
    setErr('')
    try {
      await api.addStaff({
        username: f.username.trim(), displayName: f.displayName.trim(), role: f.role, password: f.password,
        fbrPosId: f.fbrPosId ? Number(f.fbrPosId) : null, fbrToken: f.fbrToken || null,
      })
      setF({ username: '', displayName: '', role: 'Cashier', password: '', fbrPosId: '', fbrToken: '' }); load()
    } catch (e) { setErr(e.message) }
  }
  const changeRole = async (s, role) => { try { await api.updateStaff(s.id, { displayName: s.displayName, role, fbrPosId: s.fbrPosId }); load() } catch (e) { ui.alert(e.message) } }
  const resetPw = async (s) => {
    const pw = await ui.prompt({ title: `New password for ${s.username}`, placeholder: 'New password', confirmText: 'Update', required: true })
    if (pw) { await api.resetStaffPassword(s.id, pw); ui.alert('Password updated.') }
  }
  const del = async (s) => {
    if (!await ui.confirm({ title: 'Remove staff', message: `Remove ${s.username}?`, confirmText: 'Remove', danger: true })) return
    try { await api.deleteStaff(s.id); load() } catch (e) { ui.alert(e.message) }
  }

  return (
    <div className="card">
      <b>Staff</b>
      <div style={{ margin: '10px 0' }}>
        {rows.map((s) => (
          <div key={s.id} style={{ borderBottom: '1px solid var(--line)' }}>
            <div className="row between" style={{ padding: '7px 0' }}>
              <span><b>{s.displayName}</b> <span className="muted">· {s.username}
                {s.fbrPosId ? ` · POS ${s.fbrPosId}` : ''}{s.hasFbrToken ? ' 🔑' : ''}</span></span>
              <div className="row" style={{ gap: 6 }}>
                <select value={s.role} onChange={(e) => changeRole(s, e.target.value)} style={{ padding: '5px 8px' }}>
                  {ROLES.map((r) => <option key={r}>{r}</option>)}
                </select>
                <button className="ghost" title="FBR POS credentials" onClick={() => setPosFor(posFor === s.id ? null : s.id)}>🧾</button>
                <button className="ghost" onClick={() => resetPw(s)}>🔑</button>
                <button className="ghost" onClick={() => del(s)}>🗑</button>
              </div>
            </div>
            {posFor === s.id && <StaffFbr s={s} onSaved={() => { setPosFor(null); load() }} />}
          </div>
        ))}
        {rows.length === 0 && <div className="muted" style={{ fontSize: 13 }}>No staff yet.</div>}
      </div>
      <div className="cat-title" style={{ fontSize: 13, margin: '6px 0' }}>Add staff</div>
      <div className="row" style={{ gap: 8, marginBottom: 6 }}>
        <input placeholder="Username" value={f.username} onChange={(e) => set('username', e.target.value)} style={{ flex: 1 }} />
        <input placeholder="Display name" value={f.displayName} onChange={(e) => set('displayName', e.target.value)} style={{ flex: 1 }} />
      </div>
      <div className="row" style={{ gap: 8, marginBottom: 6 }}>
        <select value={f.role} onChange={(e) => set('role', e.target.value)}>{ROLES.map((r) => <option key={r}>{r}</option>)}</select>
        <input placeholder="Password" type="password" value={f.password} onChange={(e) => set('password', e.target.value)} style={{ flex: 1 }} />
      </div>
      <div className="row" style={{ gap: 8 }}>
        <input placeholder="FBR POSID (optional)" value={f.fbrPosId} onChange={(e) => set('fbrPosId', e.target.value)} style={{ width: 150 }} />
        <input placeholder="FBR token (cloud, optional)" value={f.fbrToken} onChange={(e) => set('fbrToken', e.target.value)} style={{ flex: 1 }} />
        <button className="primary" disabled={!f.username.trim() || !f.displayName.trim() || !f.password} onClick={add}>Add</button>
      </div>
      {err && <div style={{ color: 'var(--red)', fontSize: 13, marginTop: 6 }}>{err}</div>}
    </div>
  )
}

// Edit an existing operator's FBR POSID / token (cloud sync files under these).
function StaffFbr({ s, onSaved }) {
  const [posId, setPosId] = useState(s.fbrPosId || '')
  const [token, setToken] = useState('')
  const [err, setErr] = useState('')
  const save = async () => {
    setErr('')
    try {
      // token: leave blank to keep unchanged; send explicit value to set/clear.
      await api.updateStaff(s.id, { displayName: s.displayName, role: s.role, fbrPosId: posId ? Number(posId) : null, ...(token !== '' ? { fbrToken: token } : {}) })
      onSaved()
    } catch (e) { setErr(e.message) }
  }
  return (
    <div style={{ background: 'var(--surface2)', borderRadius: 8, margin: '4px 0 8px', padding: 12 }}>
      <div className="muted" style={{ fontSize: 12, marginBottom: 8 }}>This operator’s FBR POS registration. Cloud filings use these; blank falls back to the branch defaults.</div>
      <div className="row" style={{ gap: 8, flexWrap: 'wrap' }}>
        <input placeholder="POSID" value={posId} onChange={(e) => setPosId(e.target.value)} style={{ width: 140 }} />
        <input placeholder={s.hasFbrToken ? 'Token set — type to change' : 'Bearer token (cloud)'} value={token} onChange={(e) => setToken(e.target.value)} style={{ flex: 1, minWidth: 160 }} />
        <button className="primary" onClick={save}>Save</button>
      </div>
      {err && <div style={{ color: 'var(--red)', fontSize: 13, marginTop: 6 }}>{err}</div>}
    </div>
  )
}
