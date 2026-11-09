import { useEffect, useState } from 'react'
import { api, money } from '../api'
import { auth } from '../auth'
import { ui, toast } from '../ui.jsx'
import { friendlyError } from '../errors'
import {
  DataTable, Empty, Figure, Loading, Modal, PAY_METHODS, PageHead, Problem, fmtDate, printPage, todayPk, useData, waLink,
} from '../backoffice.jsx'
import { ChatIcon, CloseIcon, EditIcon, PlusIcon, PrintIcon, TrashIcon } from '../icons.jsx'

// The supplier khata: every bill, payment and return, with a running balance, so "how much do we
// owe the chicken wala" has one answer. What's owed is always worked out from the entries.
export default function Suppliers() {
  const { data, error, loading, reload } = useData(() => api.suppliers(), [])
  const [q, setQ] = useState('')
  const [form, setForm] = useState(null)
  const [openId, setOpenId] = useState(null)

  const rows = (data || []).filter((s) => !q.trim() || s.name.toLowerCase().includes(q.trim().toLowerCase()) || s.phone.includes(q.trim()))
  const owed = (data || []).reduce((sum, s) => sum + Math.max(0, s.balance), 0)

  return (
    <div className="bo-wrap">
      <PageHead title="Supplier khata" sub="What you've bought, what you've paid and what you still owe each supplier. Bills add to your stock automatically.">
        <button className="primary" onClick={() => setForm({})}><PlusIcon size={16} /> Add supplier</button>
      </PageHead>
      <Problem onRetry={reload}>{error}</Problem>
      {loading && !data && <Loading />}
      {data && <>
        <div className="bo-figs">
          <Figure tone="lead" label="You owe" value={money(owed)} hint={`${data.filter((s) => s.balance > 0).length} of ${data.length} suppliers`} />
        </div>
        <div className="bo-card">
          {data.length === 0 ? (
            <Empty title="No suppliers yet" action={<button className="primary" onClick={() => setForm({})}>Add your first supplier</button>}>
              Add the people you buy from: chicken, vegetables, gas, drinks.
            </Empty>
          ) : <>
            <div className="bo-toolbar"><input className="grow" placeholder="Search suppliers" value={q} onChange={(e) => setQ(e.target.value)} /></div>
            <DataTable rowKey={(s) => s.id} rows={rows} onRow={(s) => setOpenId(s.id)} columns={[
              { key: 'name', label: 'Supplier', render: (s) => <b>{s.name}</b> },
              { key: 'phone', label: 'Mobile' },
              { key: 'lastEntry', label: 'Last entry', render: (s) => (s.lastEntry ? fmtDate(s.lastEntry) : '-') },
              { key: 'balance', label: 'You owe', num: true, render: (s) => (s.balance > 0 ? <span className="bo-owe">{money(s.balance)}</span> : <span className="bo-clear">Clear</span>) },
            ]} />
          </>}
        </div>
      </>}
      {form && <SupplierForm supplier={form} onClose={() => setForm(null)} onSaved={() => { setForm(null); reload() }} />}
      {openId && <SupplierLedger id={openId} onClose={() => { setOpenId(null); reload() }} onEdit={(s) => { setOpenId(null); setForm(s) }} />}
    </div>
  )
}

function SupplierForm({ supplier, onClose, onSaved }) {
  const isNew = !supplier.id
  const [f, setF] = useState({ name: supplier.name || '', phone: supplier.phone || '', notes: supplier.notes || '', openingBalance: supplier.openingBalance || '' })
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')
  const set = (k) => (e) => setF({ ...f, [k]: e.target.value })
  const save = async () => {
    setBusy(true); setErr('')
    const body = { ...f, openingBalance: Number(f.openingBalance) || 0 }
    try {
      if (isNew) await api.addSupplier(body); else await api.updateSupplier(supplier.id, body)
      onSaved()
    } catch (e) { setErr(friendlyError(e)); setBusy(false) }
  }
  return (
    <Modal title={isNew ? 'Add supplier' : `Edit ${supplier.name}`} onClose={onClose} footer={<>
      <button onClick={onClose}>Cancel</button>
      <button className="primary push" disabled={busy || !f.name.trim()} onClick={save}>{busy ? 'Saving…' : 'Save'}</button>
    </>}>
      <div className="bo-form">
        <div className="field is-full"><label className="field-label">Name</label><input autoFocus value={f.name} onChange={set('name')} /></div>
        <div className="field"><label className="field-label">Mobile</label><input inputMode="tel" value={f.phone} onChange={set('phone')} /></div>
        <div className="field"><label className="field-label">You already owe (Rs)</label><input type="number" value={f.openingBalance} onChange={set('openingBalance')} /></div>
        <div className="field is-full"><label className="field-label">Notes</label><input value={f.notes} onChange={set('notes')} /></div>
      </div>
      {err && <div className="form-error" style={{ marginTop: 10 }}>{err}</div>}
    </Modal>
  )
}

const ENTRY_LABEL = { Purchase: 'Bill', Payment: 'Paid', Return: 'Returned' }

function SupplierLedger({ id, onClose, onEdit }) {
  const { data, error, reload } = useData(() => api.supplierLedger(id), [id])
  const [action, setAction] = useState(null)   // 'bill' | 'payment' | 'return'
  const s = data?.supplier
  const rows = data ? [...data.rows].reverse() : []
  const place = auth.getStaff()?.restaurantName || ''

  const detail = (r) => [
    ENTRY_LABEL[r.type] || r.type, r.reference, r.method,
    r.lines?.length ? r.lines.map((l) => `${l.qty} ${l.name}`).join(', ') : '', r.note,
  ].filter(Boolean).join(' · ')
  const columns = [
    { key: 'date', label: 'Date', render: (r) => fmtDate(r.date) },
    { key: 'detail', label: 'Detail', render: detail },
    { key: 'bought', label: 'Bought', num: true, render: (r) => (r.bought ? money(r.bought) : '') },
    { key: 'paid', label: 'Paid', num: true, render: (r) => (r.paid ? money(r.paid) : '') },
    { key: 'balance', label: 'You owe', num: true, render: (r) => money(r.balance) },
  ]
  const printStatement = () => printPage({
    title: `${s.name}: account statement`, subtitle: `${place}. Balance ${money(s.balance)} on ${fmtDate(todayPk())}`,
    sections: [{ columns, rows: data.rows, note: s.openingBalance ? `Opening balance ${money(s.openingBalance)}` : '' }],
  })
  const removeEntry = async (r) => {
    if (!(await ui.confirm({ title: 'Remove this entry?', message: `${detail(r)} on ${fmtDate(r.date)}. The balance and any stock it added change back.`, confirmText: 'Remove', danger: true }))) return
    try { await api.deleteSupplierEntry(r.id); reload() } catch (e) { toast.error(friendlyError(e)) }
  }
  const archive = async () => {
    if (!(await ui.confirm({ title: `Remove ${s.name}?`, message: 'They come off your list. Past bills stay in your reports.', confirmText: 'Remove', danger: true }))) return
    try { await api.archiveSupplier(id); onClose() } catch (e) { toast.error(friendlyError(e)) }
  }

  return (
    <Modal wide title={s?.name || 'Supplier'} sub={s?.phone} onClose={onClose} footer={s && <>
      <button onClick={printStatement}><PrintIcon size={15} /> Statement</button>
      {s.phone && <a href={waLink(s.phone, `Assalam o Alaikum ${s.name}. Our account with you stands at ${money(s.balance)}. ${place}.`)} target="_blank" rel="noreferrer"><button><ChatIcon size={15} /> WhatsApp</button></a>}
      <button onClick={() => onEdit(s)}><EditIcon size={15} /> Edit</button>
      {s.balance <= 0 && <button className="ghost" onClick={archive}><TrashIcon size={15} /> Remove</button>}
      <span className="push row" style={{ gap: 8, flexWrap: 'wrap' }}>
        <button onClick={() => setAction('return')}>Return</button>
        <button onClick={() => setAction('payment')}>Pay</button>
        <button className="primary" onClick={() => setAction('bill')}><PlusIcon size={15} /> Add bill</button>
      </span>
    </>}>
      <Problem onRetry={reload}>{error}</Problem>
      {!data && !error && <Loading />}
      {s && <>
        <div className="bo-figs"><Figure tone={s.balance > 0 ? 'bad' : 'good'} label="You owe now" value={s.balance > 0 ? money(s.balance) : 'Clear'} /></div>
        <DataTable rowKey={(r) => r.id} rows={rows} empty="No bills or payments yet." columns={[...columns, {
          key: 'x', label: '', render: (r) => <button className="ghost btn-sm" aria-label="Remove this entry" onClick={() => removeEntry(r)}><TrashIcon size={14} /></button>,
        }]} />
      </>}
      {(action === 'bill' || action === 'return') && <BillForm kind={action} supplier={s} onClose={() => setAction(null)} onSaved={() => { setAction(null); reload() }} />}
      {action === 'payment' && <PayForm supplier={s} onClose={() => setAction(null)} onSaved={() => { setAction(null); reload() }} />}
    </Modal>
  )
}

const blankLine = () => ({ stockItemId: '', name: '', qty: '', unitCost: '' })

// Tying a line to a stock item adds it to inventory and keeps the cost price current; a plain
// amount is there for bills nobody has time to itemise.
function BillForm({ kind, supplier, onClose, onSaved }) {
  const isReturn = kind === 'return'
  const canStock = auth.can('inventory.manage')
  const [stock, setStock] = useState([])
  const [f, setF] = useState({ date: todayPk(), reference: '', note: '', amount: '', paidNow: '', paidMethod: 'Cash' })
  const [lines, setLines] = useState([])
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')
  useEffect(() => { if (canStock) api.listStock().then(setStock).catch(() => {}) }, [canStock])

  const set = (k) => (e) => setF({ ...f, [k]: e.target.value })
  const setLine = (i, k, v) => setLines(lines.map((l, n) => (n === i ? { ...l, [k]: v } : l)))
  const lineTotal = lines.reduce((s, l) => s + (Number(l.qty) || 0) * (Number(l.unitCost) || 0), 0)
  const total = lines.length ? lineTotal : Number(f.amount) || 0

  const save = async () => {
    setBusy(true); setErr('')
    const body = {
      date: f.date, reference: f.reference, note: f.note,
      amount: lines.length ? lineTotal : Number(f.amount) || 0,
      lines: lines.filter((l) => Number(l.qty) > 0).map((l) => {
        const item = stock.find((s) => String(s.id) === String(l.stockItemId))
        return { stockItemId: item ? item.id : null, name: item ? item.name : l.name, qty: Number(l.qty), unitCost: Number(l.unitCost) || 0 }
      }),
    }
    try {
      if (isReturn) await api.addSupplierReturn(supplier.id, body)
      else await api.addPurchase(supplier.id, { ...body, paidNow: Number(f.paidNow) || 0, paidMethod: f.paidMethod })
      onSaved()
    } catch (e) { setErr(friendlyError(e)); setBusy(false) }
  }

  return (
    <Modal wide title={isReturn ? `Return to ${supplier.name}` : `Bill from ${supplier.name}`} onClose={onClose} footer={<>
      <span className="ts-total">{money(total)}</span>
      <button className="push" onClick={onClose}>Cancel</button>
      <button className="primary" disabled={busy || total <= 0} onClick={save}>{busy ? 'Saving…' : 'Save'}</button>
    </>}>
      <div className="bo-form">
        <div className="field"><label className="field-label">Date</label><input type="date" max={todayPk()} value={f.date} onChange={set('date')} /></div>
        <div className="field"><label className="field-label">Bill number (optional)</label><input value={f.reference} onChange={set('reference')} /></div>
      </div>

      <div className="bl-row">
        <span className="bl-label">{isReturn ? 'What went back' : 'What came in'}</span>
        {lines.map((l, i) => (
          <div className="row" key={i} style={{ gap: 6, marginBottom: 6, flexWrap: 'wrap' }}>
            {canStock && stock.length > 0 ? (
              <select style={{ flex: '2 1 150px' }} value={l.stockItemId} onChange={(e) => setLine(i, 'stockItemId', e.target.value)}>
                <option value="">Not a stock item</option>
                {stock.map((s) => <option key={s.id} value={s.id}>{s.name} ({s.unit})</option>)}
              </select>
            ) : null}
            {!l.stockItemId && <input style={{ flex: '2 1 130px' }} placeholder="Item" value={l.name} onChange={(e) => setLine(i, 'name', e.target.value)} />}
            <input style={{ flex: '1 1 70px' }} type="number" min="0" inputMode="decimal" placeholder="Qty" value={l.qty} onChange={(e) => setLine(i, 'qty', e.target.value)} />
            <input style={{ flex: '1 1 90px' }} type="number" min="0" inputMode="decimal" placeholder="Rate (Rs)" value={l.unitCost} onChange={(e) => setLine(i, 'unitCost', e.target.value)} />
            <button className="ghost" aria-label="Remove line" onClick={() => setLines(lines.filter((_, n) => n !== i))}><CloseIcon size={15} /></button>
          </div>
        ))}
        <button className="btn-sm" onClick={() => setLines([...lines, blankLine()])}><PlusIcon size={14} /> Add item</button>
      </div>

      {lines.length === 0 && (
        <div className="bl-row">
          <label className="bl-label">Or just the total (Rs)</label>
          <input type="number" min="0" inputMode="decimal" style={{ width: '100%' }} value={f.amount} onChange={set('amount')} />
        </div>
      )}

      {!isReturn && (
        <div className="bo-form bl-row">
          <div className="field"><label className="field-label">Paid now (Rs)</label><input type="number" min="0" inputMode="decimal" placeholder="0, all on khata" value={f.paidNow} onChange={set('paidNow')} /></div>
          <div className="field"><label className="field-label">Paid by</label>
            <select value={f.paidMethod} onChange={set('paidMethod')}>{PAY_METHODS.map((m) => <option key={m}>{m}</option>)}</select>
          </div>
        </div>
      )}
      <div className="bl-row"><input style={{ width: '100%' }} placeholder="Note (optional)" value={f.note} onChange={set('note')} /></div>
      {err && <div className="form-error" style={{ marginTop: 10 }}>{err}</div>}
    </Modal>
  )
}

function PayForm({ supplier, onClose, onSaved }) {
  const [f, setF] = useState({ amount: supplier.balance > 0 ? supplier.balance : '', method: 'Cash', date: todayPk(), reference: '', note: '' })
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')
  const set = (k) => (e) => setF({ ...f, [k]: e.target.value })
  const save = async () => {
    setBusy(true); setErr('')
    try { await api.addSupplierPayment(supplier.id, { ...f, amount: Number(f.amount) }); toast.success('Payment recorded'); onSaved() }
    catch (e) { setErr(friendlyError(e)); setBusy(false) }
  }
  return (
    <Modal title={`Pay ${supplier.name}`} sub={`You owe ${money(supplier.balance)}`} onClose={onClose} footer={<>
      <button onClick={onClose}>Cancel</button>
      <button className="primary push" disabled={busy || !(Number(f.amount) > 0)} onClick={save}>{busy ? 'Saving…' : 'Record payment'}</button>
    </>}>
      <div className="bo-form">
        <div className="field"><label className="field-label">Amount (Rs)</label><input autoFocus type="number" min="0" inputMode="decimal" value={f.amount} onChange={set('amount')} /></div>
        <div className="field"><label className="field-label">Date</label><input type="date" max={todayPk()} value={f.date} onChange={set('date')} /></div>
        <div className="field is-full"><label className="field-label">Paid by</label>
          <select value={f.method} onChange={set('method')}>{PAY_METHODS.map((m) => <option key={m}>{m}</option>)}</select>
        </div>
        <div className="field is-full"><label className="field-label">Note (optional)</label><input value={f.note} onChange={set('note')} /></div>
      </div>
      {err && <div className="form-error" style={{ marginTop: 10 }}>{err}</div>}
    </Modal>
  )
}
