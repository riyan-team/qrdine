import { useState } from 'react'
import { api, money } from '../api'
import { auth } from '../auth'
import { ui, toast } from '../ui.jsx'
import { friendlyError } from '../errors'
import {
  DataTable, Empty, Figure, Loading, Modal, PAY_METHODS, PageHead, Problem, fmtDate, printPage, todayPk, useData, waLink,
} from '../backoffice.jsx'
import { ChatIcon, EditIcon, PlusIcon, PrintIcon, TrashIcon } from '../icons.jsx'

// Offices and regulars who eat now and pay at the end of the month. A bill reaches this page by
// choosing "Khata" when settling at Reception; money coming back is recorded here.
export default function Customers() {
  const { data, error, loading, reload } = useData(() => api.customers(), [])
  const [q, setQ] = useState('')
  const [form, setForm] = useState(null)
  const [openId, setOpenId] = useState(null)

  const rows = (data || []).filter((c) => !q.trim() || c.name.toLowerCase().includes(q.trim().toLowerCase()) || c.phone.includes(q.trim()))
  const owed = (data || []).reduce((s, c) => s + Math.max(0, c.balance), 0)
  const owing = (data || []).filter((c) => c.balance > 0).length

  return (
    <div className="bo-wrap">
      <PageHead title="Customer khata" sub="Who owes you, and how much. Bills arrive here when you choose Khata while settling at Reception.">
        <button className="primary" onClick={() => setForm({})}><PlusIcon size={16} /> Add customer</button>
      </PageHead>
      <Problem onRetry={reload}>{error}</Problem>
      {loading && !data && <Loading />}
      {data && <>
        <div className="bo-figs">
          <Figure tone="lead" label="Customers owe you" value={money(owed)} hint={`${owing} ${owing === 1 ? 'customer' : 'customers'}`} />
          <Figure label="Customers on file" value={data.length} />
        </div>
        <div className="bo-card">
          {data.length === 0 ? (
            <Empty title="No customers yet" action={<button className="primary" onClick={() => setForm({})}>Add a customer</button>}>
              Delivery customers are saved here automatically by their phone number.
            </Empty>
          ) : <>
            <div className="bo-toolbar"><input className="grow" placeholder="Search by name or mobile" value={q} onChange={(e) => setQ(e.target.value)} /></div>
            <DataTable rowKey={(c) => c.id} rows={rows} onRow={(c) => setOpenId(c.id)} columns={[
              { key: 'name', label: 'Customer', render: (c) => <b>{c.name}</b> },
              { key: 'phone', label: 'Mobile' },
              { key: 'lastEntry', label: 'Last entry', render: (c) => (c.lastEntry ? fmtDate(c.lastEntry) : '-') },
              { key: 'creditLimit', label: 'Limit', num: true, render: (c) => (c.creditLimit > 0 ? money(c.creditLimit) : '-') },
              { key: 'balance', label: 'Owes', num: true, render: (c) => (c.balance > 0 ? <span className="bo-owe">{money(c.balance)}</span> : <span className="bo-clear">Clear</span>) },
            ]} />
          </>}
        </div>
      </>}
      {form && <CustomerForm customer={form} onClose={() => setForm(null)} onSaved={() => { setForm(null); reload() }} />}
      {openId && <CustomerLedger id={openId} onClose={() => { setOpenId(null); reload() }} onEdit={(c) => { setOpenId(null); setForm(c) }} />}
    </div>
  )
}

function CustomerForm({ customer, onClose, onSaved }) {
  const isNew = !customer.id
  const [f, setF] = useState({
    name: customer.name || '', phone: customer.phone || '', address: customer.address || '', notes: customer.notes || '',
    openingBalance: customer.openingBalance || '', creditLimit: customer.creditLimit || '',
  })
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')
  const set = (k) => (e) => setF({ ...f, [k]: e.target.value })
  const save = async () => {
    setBusy(true); setErr('')
    const body = { ...f, openingBalance: Number(f.openingBalance) || 0, creditLimit: Number(f.creditLimit) || 0 }
    try {
      if (isNew) await api.addCustomer(body); else await api.updateCustomer(customer.id, body)
      onSaved()
    } catch (e) { setErr(friendlyError(e)); setBusy(false) }
  }
  return (
    <Modal title={isNew ? 'Add customer' : `Edit ${customer.name}`} onClose={onClose} footer={<>
      <button onClick={onClose}>Cancel</button>
      <button className="primary push" disabled={busy || !f.name.trim()} onClick={save}>{busy ? 'Saving…' : 'Save'}</button>
    </>}>
      <div className="bo-form">
        <div className="field"><label className="field-label">Name</label><input autoFocus value={f.name} onChange={set('name')} /></div>
        <div className="field"><label className="field-label">Mobile</label><input inputMode="tel" value={f.phone} onChange={set('phone')} /></div>
        <div className="field is-full"><label className="field-label">Address</label><input value={f.address} onChange={set('address')} /></div>
        <div className="field"><label className="field-label">Already owes (Rs)</label><input type="number" value={f.openingBalance} onChange={set('openingBalance')} /></div>
        <div className="field"><label className="field-label">Khata limit (Rs)</label><input type="number" min="0" placeholder="No limit" value={f.creditLimit} onChange={set('creditLimit')} /></div>
        <div className="field is-full"><label className="field-label">Notes</label><input value={f.notes} onChange={set('notes')} /></div>
      </div>
      {err && <div className="form-error" style={{ marginTop: 10 }}>{err}</div>}
    </Modal>
  )
}

function CustomerLedger({ id, onClose, onEdit }) {
  const { data, error, reload } = useData(() => api.customerLedger(id), [id])
  const [paying, setPaying] = useState(false)
  const c = data?.customer
  const rows = data ? [...data.rows].reverse() : []
  const place = auth.getStaff()?.restaurantName || ''

  const columns = [
    { key: 'date', label: 'Date', render: (r) => fmtDate(r.date) },
    { key: 'detail', label: 'Detail', render: (r) => (r.type === 'Sale' ? `Bill ${r.reference}${r.note ? `, ${r.note}` : ''}` : `Received${r.method ? ` by ${r.method}` : ''}${r.reference ? `, ${r.reference}` : ''}${r.note ? `, ${r.note}` : ''}`) },
    { key: 'charged', label: 'Bill', num: true, render: (r) => (r.charged ? money(r.charged) : '') },
    { key: 'paid', label: 'Received', num: true, render: (r) => (r.paid ? money(r.paid) : '') },
    { key: 'balance', label: 'Owes', num: true, render: (r) => money(r.balance) },
  ]
  const printStatement = () => printPage({
    title: `${c.name}: account statement`, subtitle: `${place}. Balance ${money(c.balance)} on ${fmtDate(todayPk())}`,
    sections: [{ columns, rows: data.rows, note: c.openingBalance ? `Opening balance ${money(c.openingBalance)}` : '' }],
  })
  const reminder = c ? `Assalam o Alaikum ${c.name}. Your balance at ${place} is ${money(c.balance)}. Thank you.` : ''

  const removeEntry = async (r) => {
    if (!(await ui.confirm({ title: 'Remove this payment?', message: `${money(r.paid)} on ${fmtDate(r.date)}. What they owe goes back up.`, confirmText: 'Remove', danger: true }))) return
    try { await api.deleteCustomerEntry(r.id); reload() } catch (e) { toast.error(friendlyError(e)) }
  }
  const archive = async () => {
    if (!(await ui.confirm({ title: `Remove ${c.name}?`, message: 'They come off your list. Past bills stay in your reports.', confirmText: 'Remove', danger: true }))) return
    try { await api.archiveCustomer(id); onClose() } catch (e) { toast.error(friendlyError(e)) }
  }

  return (
    <Modal wide title={c?.name || 'Customer'} sub={c ? [c.phone, c.address].filter(Boolean).join(' · ') : ''} onClose={onClose} footer={c && <>
      <button onClick={printStatement}><PrintIcon size={15} /> Statement</button>
      {c.phone && <a href={waLink(c.phone, reminder)} target="_blank" rel="noreferrer"><button><ChatIcon size={15} /> Reminder</button></a>}
      <button onClick={() => onEdit(c)}><EditIcon size={15} /> Edit</button>
      {c.balance <= 0 && <button className="ghost" onClick={archive}><TrashIcon size={15} /> Remove</button>}
      <button className="primary push" onClick={() => setPaying(true)}><PlusIcon size={15} /> Receive payment</button>
    </>}>
      <Problem onRetry={reload}>{error}</Problem>
      {!data && !error && <Loading />}
      {c && <>
        <div className="bo-figs">
          <Figure tone={c.balance > 0 ? 'bad' : 'good'} label="Owes now" value={c.balance > 0 ? money(c.balance) : 'Clear'} />
          {c.creditLimit > 0 && <Figure label="Khata limit" value={money(c.creditLimit)} hint={c.balance >= c.creditLimit ? 'Limit reached' : `${money(c.creditLimit - c.balance)} left`} />}
        </div>
        <DataTable rowKey={(r) => r.id} rows={rows} empty="No bills or payments yet." columns={[...columns, {
          key: 'x', label: '', render: (r) => (r.type === 'Payment'
            ? <button className="ghost btn-sm" aria-label="Remove this payment" onClick={() => removeEntry(r)}><TrashIcon size={14} /></button> : null),
        }]} />
      </>}
      {paying && <PaymentForm customer={c} onClose={() => setPaying(false)} onSaved={() => { setPaying(false); reload() }} />}
    </Modal>
  )
}

function PaymentForm({ customer, onClose, onSaved }) {
  const [f, setF] = useState({ amount: customer.balance > 0 ? customer.balance : '', method: 'Cash', date: todayPk(), reference: '', note: '' })
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')
  const set = (k) => (e) => setF({ ...f, [k]: e.target.value })
  const save = async () => {
    setBusy(true); setErr('')
    try { await api.addCustomerPayment(customer.id, { ...f, amount: Number(f.amount) }); toast.success('Payment recorded'); onSaved() }
    catch (e) { setErr(friendlyError(e)); setBusy(false) }
  }
  return (
    <Modal title={`Payment from ${customer.name}`} sub={`Owes ${money(customer.balance)}`} onClose={onClose} footer={<>
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
