import { useMemo, useState } from 'react'
import { api, money } from '../api'
import { ui, toast } from '../ui.jsx'
import { friendlyError } from '../errors'
import {
  DataTable, Empty, Figure, Loading, Modal, PAY_METHODS, PageHead, Problem, Tabs, fmtDate, fmtMonth, monthPk, printPage, todayPk, useData, waLink,
} from '../backoffice.jsx'
import { ChatIcon, EditIcon, PlusIcon, PrintIcon, TrashIcon } from '../icons.jsx'

const shiftMonth = (key, n) => {
  const d = new Date(`${key}-01T00:00:00Z`)
  d.setUTCMonth(d.getUTCMonth() + n)
  return d.toISOString().slice(0, 7)
}

const ENTRY_TYPES = [
  { key: 'Advance', label: 'Advance', hint: 'Taken before pay day. Comes off this month.' },
  { key: 'Salary', label: 'Salary', hint: 'Paying the month.' },
  { key: 'Bonus', label: 'Bonus', hint: 'Extra on top, Eid or good work.' },
  { key: 'Deduction', label: 'Deduction', hint: 'Comes off pay. No cash changes hands.' },
]

// What each person is due this month, what they've already had, and who came in. Anyone can be on
// the payroll without having a login, so kitchen helpers and cleaners belong here too.
export default function Payroll() {
  const [month, setMonth] = useState(monthPk())
  const [tab, setTab] = useState('pay')
  const { data, error, loading, reload } = useData(() => api.payroll(month), [month])
  const [form, setForm] = useState(null)
  const [openRow, setOpenRow] = useState(null)
  const person = openRow && data?.rows.find((r) => r.id === openRow)

  return (
    <div className="bo-wrap">
      <PageHead title="Staff & salaries" sub="Salaries, advances and attendance. Money you pay out here shows in your expenses and the day's cash on its own.">
        <button className="primary" onClick={() => setForm({})}><PlusIcon size={16} /> Add person</button>
      </PageHead>

      <div className="bo-toolbar">
        <div className="row" style={{ gap: 4 }}>
          <button className="btn-sm" aria-label="Previous month" onClick={() => setMonth(shiftMonth(month, -1))}>‹</button>
          <b style={{ minWidth: 142, textAlign: 'center', fontFamily: 'var(--display)', fontSize: 17 }}>{fmtMonth(month)}</b>
          <button className="btn-sm" aria-label="Next month" disabled={month >= monthPk()} onClick={() => setMonth(shiftMonth(month, 1))}>›</button>
        </div>
        <Tabs value={tab} onChange={setTab} options={[{ value: 'pay', label: 'Pay' }, { value: 'attendance', label: 'Attendance' }]} />
      </div>

      <Problem onRetry={reload}>{error}</Problem>
      {loading && !data && <Loading />}
      {data && tab === 'pay' && <>
        <div className="bo-figs">
          <Figure tone="lead" label="Still to pay" value={money(data.totalDue)} />
          <Figure label="Monthly salaries" value={money(data.totalSalary)} />
          <Figure label="Paid so far" value={money(data.totalPaid)} hint="Salary and advances" />
        </div>
        <div className="bo-card">
          {data.rows.length === 0 ? (
            <Empty title="Nobody on the payroll yet" action={<button className="primary" onClick={() => setForm({})}>Add your first person</button>}>
              Add everyone you pay, whether or not they sign in to Dine In.
            </Empty>
          ) : (
            <DataTable rowKey={(r) => r.id} rows={data.rows} onRow={(r) => setOpenRow(r.id)} columns={[
              { key: 'name', label: 'Name', render: (r) => <><b>{r.name}</b>{r.designation && <div className="bo-small">{r.designation}</div>}</> },
              { key: 'monthlySalary', label: 'Salary', num: true, render: (r) => money(r.monthlySalary) },
              { key: 'advances', label: 'Advance', num: true, render: (r) => (r.advances ? money(r.advances) : '-') },
              { key: 'bonuses', label: 'Bonus', num: true, render: (r) => (r.bonuses ? money(r.bonuses) : '-') },
              { key: 'deductions', label: 'Deducted', num: true, render: (r) => (r.deductions ? money(r.deductions) : '-') },
              { key: 'paid', label: 'Paid', num: true, render: (r) => (r.paid ? money(r.paid) : '-') },
              { key: 'due', label: 'Still due', num: true, render: (r) => (r.due > 0 ? <span className="bo-owe">{money(r.due)}</span> : <span className="bo-clear">{r.due < 0 ? `Overpaid ${money(-r.due)}` : 'Paid'}</span>) },
            ]} />
          )}
        </div>
      </>}
      {data && tab === 'attendance' && <Attendance month={month} people={data.rows} onChanged={reload} />}

      {form && <PersonForm person={form} onClose={() => setForm(null)} onSaved={() => { setForm(null); reload() }} />}
      {person && <PersonSheet person={person} month={month} onClose={() => setOpenRow(null)} onChanged={reload}
        onEdit={() => { setOpenRow(null); setForm(person) }} />}
    </div>
  )
}

function PersonForm({ person, onClose, onSaved }) {
  const isNew = !person.id
  const [f, setF] = useState({
    name: person.name || '', phone: person.phone || '', designation: person.designation || '',
    monthlySalary: person.monthlySalary || '', joinedOn: person.joinedOn || '', notes: person.notes || '',
  })
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')
  const set = (k) => (e) => setF({ ...f, [k]: e.target.value })
  const save = async () => {
    setBusy(true); setErr('')
    const body = { ...f, monthlySalary: Number(f.monthlySalary) || 0, joinedOn: f.joinedOn || null }
    try {
      if (isNew) await api.addEmployee(body); else await api.updateEmployee(person.id, body)
      onSaved()
    } catch (e) { setErr(friendlyError(e)); setBusy(false) }
  }
  return (
    <Modal title={isNew ? 'Add person' : `Edit ${person.name}`} onClose={onClose} footer={<>
      <button onClick={onClose}>Cancel</button>
      <button className="primary push" disabled={busy || !f.name.trim()} onClick={save}>{busy ? 'Saving…' : 'Save'}</button>
    </>}>
      <div className="bo-form">
        <div className="field"><label className="field-label">Name</label><input autoFocus value={f.name} onChange={set('name')} /></div>
        <div className="field"><label className="field-label">Job</label><input placeholder="Cook, waiter, helper" value={f.designation} onChange={set('designation')} /></div>
        <div className="field"><label className="field-label">Monthly salary (Rs)</label><input type="number" min="0" inputMode="numeric" value={f.monthlySalary} onChange={set('monthlySalary')} /></div>
        <div className="field"><label className="field-label">Mobile</label><input inputMode="tel" value={f.phone} onChange={set('phone')} /></div>
        <div className="field"><label className="field-label">Joined on</label><input type="date" value={f.joinedOn} onChange={set('joinedOn')} /></div>
        <div className="field"><label className="field-label">Notes</label><input value={f.notes} onChange={set('notes')} /></div>
      </div>
      {err && <div className="form-error" style={{ marginTop: 10 }}>{err}</div>}
    </Modal>
  )
}

function PersonSheet({ person, month, onClose, onChanged, onEdit }) {
  const { data: entries, error, reload } = useData(() => api.employeeEntries(person.id, month), [person.id, month])
  const [adding, setAdding] = useState(null)
  const refresh = () => { reload(); onChanged() }

  const remove = async (e) => {
    if (!(await ui.confirm({ title: 'Remove this entry?', message: `${e.type}, ${money(e.amount)} on ${fmtDate(e.date)}. Its expense is removed too.`, confirmText: 'Remove', danger: true }))) return
    try { await api.deleteEmployeeEntry(e.id); refresh() } catch (err) { toast.error(friendlyError(err)) }
  }
  const archive = async () => {
    if (!(await ui.confirm({ title: `Remove ${person.name}?`, message: 'They come off the payroll. Past payments stay in your expenses.', confirmText: 'Remove', danger: true }))) return
    try { await api.archiveEmployee(person.id); onChanged(); onClose() } catch (err) { toast.error(friendlyError(err)) }
  }
  const slip = () => printPage({
    title: `Salary slip: ${person.name}`, subtitle: `${fmtMonth(month)}${person.designation ? `. ${person.designation}` : ''}`,
    sections: [{
      columns: [{ key: 'label', label: 'Item' }, { key: 'amount', label: 'Amount', num: true, print: (r) => money(r.amount) }],
      rows: [
        { label: 'Monthly salary', amount: person.monthlySalary }, { label: 'Bonus', amount: person.bonuses },
        { label: 'Deductions', amount: -person.deductions }, { label: 'Advances taken', amount: -person.advances }, { label: 'Paid', amount: -person.paid },
      ],
      total: { amount: money(person.due) },
      note: `Present ${person.present}, absent ${person.absent}, leave ${person.leave}, half days ${person.halfDays}`,
    }],
  })
  const message = `${person.name}, your pay for ${fmtMonth(month)}: salary ${money(person.monthlySalary)}, advances ${money(person.advances)}, paid ${money(person.paid)}, still due ${money(person.due)}.`

  return (
    <Modal wide title={person.name} sub={`${fmtMonth(month)}${person.designation ? ` · ${person.designation}` : ''}`} onClose={onClose} footer={<>
      <button onClick={slip}><PrintIcon size={15} /> Salary slip</button>
      {person.phone && <a href={waLink(person.phone, message)} target="_blank" rel="noreferrer"><button><ChatIcon size={15} /> WhatsApp</button></a>}
      <button onClick={onEdit}><EditIcon size={15} /> Edit</button>
      <button className="ghost" onClick={archive}><TrashIcon size={15} /> Remove</button>
    </>}>
      <div className="bo-figs">
        <Figure tone={person.due > 0 ? 'bad' : 'good'} label="Still due" value={person.due > 0 ? money(person.due) : 'Paid'} />
        <Figure label="Salary" value={money(person.monthlySalary)} />
        <Figure label="Days present" value={person.present} hint={`${person.absent} absent, ${person.leave} leave`} />
      </div>
      <div className="bo-chips" style={{ marginBottom: 12 }}>
        {ENTRY_TYPES.map((t) => <button key={t.key} className="bo-chip" onClick={() => setAdding(t)}><PlusIcon size={13} /> {t.label}</button>)}
      </div>
      <Problem onRetry={reload}>{error}</Problem>
      <DataTable rowKey={(e) => e.id} rows={entries || []} empty="Nothing recorded for this month yet." columns={[
        { key: 'date', label: 'Date', render: (e) => fmtDate(e.date) },
        { key: 'type', label: 'Type' },
        { key: 'method', label: 'Paid by', render: (e) => (e.type === 'Deduction' ? '-' : e.method) },
        { key: 'note', label: 'Note' },
        { key: 'amount', label: 'Amount', num: true, render: (e) => money(e.amount) },
        { key: 'x', label: '', render: (e) => <button className="ghost btn-sm" aria-label="Remove this entry" onClick={() => remove(e)}><TrashIcon size={14} /></button> },
      ]} />
      {adding && <EntryForm type={adding} person={person} month={month} onClose={() => setAdding(null)} onSaved={() => { setAdding(null); refresh() }} />}
    </Modal>
  )
}

function EntryForm({ type, person, month, onClose, onSaved }) {
  const dateDefault = month === monthPk() ? todayPk() : `${month}-28`
  const [f, setF] = useState({ amount: type.key === 'Salary' && person.due > 0 ? person.due : '', date: dateDefault, method: 'Cash', note: '' })
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')
  const set = (k) => (e) => setF({ ...f, [k]: e.target.value })
  const save = async () => {
    setBusy(true); setErr('')
    try { await api.addEmployeeEntry(person.id, { type: type.key, amount: Number(f.amount), month, date: f.date, method: f.method, note: f.note }); onSaved() }
    catch (e) { setErr(friendlyError(e)); setBusy(false) }
  }
  return (
    <Modal title={`${type.label}: ${person.name}`} sub={type.hint} onClose={onClose} footer={<>
      <button onClick={onClose}>Cancel</button>
      <button className="primary push" disabled={busy || !(Number(f.amount) > 0)} onClick={save}>{busy ? 'Saving…' : 'Save'}</button>
    </>}>
      <div className="bo-form">
        <div className="field"><label className="field-label">Amount (Rs)</label><input autoFocus type="number" min="0" inputMode="numeric" value={f.amount} onChange={set('amount')} /></div>
        <div className="field"><label className="field-label">Date</label><input type="date" max={todayPk()} value={f.date} onChange={set('date')} /></div>
        {type.key !== 'Deduction' && (
          <div className="field is-full"><label className="field-label">Paid by</label>
            <select value={f.method} onChange={set('method')}>{PAY_METHODS.map((m) => <option key={m}>{m}</option>)}</select>
          </div>
        )}
        <div className="field is-full"><label className="field-label">Note (optional)</label><input value={f.note} onChange={set('note')} /></div>
      </div>
      {err && <div className="form-error" style={{ marginTop: 10 }}>{err}</div>}
    </Modal>
  )
}

const CYCLE = ['Present', 'Absent', 'Leave', 'HalfDay', '']
const LETTER = { Present: 'P', Absent: 'A', Leave: 'L', HalfDay: 'H' }

// Tap a square to go round: present, absent, leave, half day, blank.
function Attendance({ month, people, onChanged }) {
  const { data, error, reload, setData } = useData(() => api.attendance(month), [month])
  const days = useMemo(() => {
    const [y, m] = month.split('-').map(Number)
    const count = new Date(Date.UTC(y, m, 0)).getUTCDate()
    return Array.from({ length: count }, (_, i) => `${month}-${String(i + 1).padStart(2, '0')}`)
  }, [month])
  const marks = useMemo(() => new Map((data || []).map((a) => [`${a.employeeId}|${a.date}`, a.status])), [data])
  const today = todayPk()

  const tap = async (employeeId, date) => {
    const current = marks.get(`${employeeId}|${date}`) || ''
    const next = CYCLE[(CYCLE.indexOf(current) + 1) % CYCLE.length]
    setData((rows) => [...(rows || []).filter((a) => !(a.employeeId === employeeId && a.date === date)), ...(next ? [{ employeeId, date, status: next }] : [])])
    try { await api.setAttendance({ employeeId, date, status: next || null }); onChanged() }
    catch (e) { toast.error(friendlyError(e)); reload() }
  }

  return (
    <div className="bo-card">
      <Problem onRetry={reload}>{error}</Problem>
      {people.length === 0 ? <Empty title="Add people first">Attendance appears here once someone is on the payroll.</Empty> : <>
        <div className="at-scroll">
          <table className="at-grid">
            <thead><tr><th>Name</th>{days.map((d) => <th key={d}>{Number(d.slice(8))}</th>)}</tr></thead>
            <tbody>
              {people.map((p) => (
                <tr key={p.id}>
                  <td>{p.name}</td>
                  {days.map((d) => {
                    const status = marks.get(`${p.id}|${d}`) || ''
                    return (
                      <td key={d}>
                        <button className={`at-cell${status ? ` is-${status}` : ''}`} disabled={d > today}
                          aria-label={`${p.name}, ${fmtDate(d)}: ${status || 'not marked'}`} onClick={() => tap(p.id, d)}>{LETTER[status] || ''}</button>
                      </td>
                    )
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div className="at-key">
          <span><i style={{ background: 'var(--green-tint)' }} />P present</span>
          <span><i style={{ background: '#f9dfe3' }} />A absent</span>
          <span><i style={{ background: 'var(--saffron-tint)' }} />L leave</span>
          <span><i style={{ background: '#e4e8f6' }} />H half day</span>
          <span>Tap a square to change it.</span>
        </div>
      </>}
    </div>
  )
}
