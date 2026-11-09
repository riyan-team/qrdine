import { useState } from 'react'
import { api, money } from '../api'
import { ui, toast } from '../ui.jsx'
import { friendlyError } from '../errors'
import {
  DataTable, Empty, ExportButtons, Figure, Loading, Modal, PAY_METHODS, PageHead, Problem, addDays, downloadCsv, fmtDate, monthEnd, monthStart,
  printPage, todayPk, useData,
} from '../backoffice.jsx'
import { EditIcon, PlusIcon, TrashIcon } from '../icons.jsx'

const PERIODS = [
  { key: 'today', label: 'Today', range: (t) => [t, t] },
  { key: 'yesterday', label: 'Yesterday', range: (t) => [addDays(t, -1), addDays(t, -1)] },
  { key: 'month', label: 'This month', range: (t) => [monthStart(t), monthEnd(t)] },
  { key: 'last', label: 'Last month', range: (t) => { const end = addDays(monthStart(t), -1); return [monthStart(end), end] } },
]

// Every rupee that leaves the shop that isn't a supplier bill: rent, gas, bijli, staff meals.
export default function Expenses() {
  const [period, setPeriod] = useState('month')
  const [category, setCategory] = useState('')
  const [editing, setEditing] = useState(null)   // null, or an expense (empty for new)
  const today = todayPk()
  const [from, to] = PERIODS.find((p) => p.key === period).range(today)
  const { data, error, loading, reload } = useData(() => api.expenses({ from, to, category }), [from, to, category])

  const columns = [
    { key: 'date', label: 'Date', render: (r) => fmtDate(r.date), csv: (r) => r.date, print: (r) => fmtDate(r.date) },
    { key: 'category', label: 'Category' },
    { key: 'paidTo', label: 'Paid to' },
    { key: 'note', label: 'Note' },
    { key: 'paymentMethod', label: 'Paid by' },
    { key: 'amount', label: 'Amount', num: true, render: (r) => money(r.amount), csv: (r) => r.amount, print: (r) => money(r.amount) },
  ]
  const rows = data?.rows || []
  const remove = async (r) => {
    if (!(await ui.confirm({ title: 'Delete this expense?', message: `${r.category}, ${money(r.amount)} on ${fmtDate(r.date)}.`, confirmText: 'Delete', danger: true }))) return
    try { await api.deleteExpense(r.id); reload() } catch (e) { toast.error(friendlyError(e)) }
  }

  return (
    <div className="bo-wrap">
      <PageHead title="Expenses" sub="Rent, gas, bijli and everything else that isn't a supplier bill. It all counts towards your profit and the day's cash.">
        <button className="primary" onClick={() => setEditing({})}><PlusIcon size={16} /> Add expense</button>
      </PageHead>

      <div className="bo-toolbar">
        <div className="bo-chips">
          {PERIODS.map((p) => <button key={p.key} className={`bo-chip${period === p.key ? ' on' : ''}`} onClick={() => setPeriod(p.key)}>{p.label}</button>)}
        </div>
      </div>

      <Problem onRetry={reload}>{error}</Problem>
      {loading && !data && <Loading />}
      {data && <>
        <div className="bo-figs">
          <Figure tone="lead" label={category ? category : 'Total spent'} value={money(data.total)} hint={from === to ? fmtDate(from) : `${fmtDate(from)} to ${fmtDate(to)}`} />
          {data.byCategory.slice(0, 3).map((c) => <Figure key={c.category} label={c.category} value={money(c.amount)} hint={`${c.count} ${c.count === 1 ? 'entry' : 'entries'}`} />)}
        </div>

        {data.byCategory.length > 1 && (
          <div className="bo-chips" style={{ marginBottom: 12 }}>
            <button className={`bo-chip${category === '' ? ' on' : ''}`} onClick={() => setCategory('')}>All</button>
            {data.byCategory.map((c) => <button key={c.category} className={`bo-chip${category === c.category ? ' on' : ''}`} onClick={() => setCategory(c.category)}>{c.category}</button>)}
          </div>
        )}

        <div className="bo-card">
          {rows.length === 0 ? (
            <Empty title="No expenses in this period" action={<button className="primary" onClick={() => setEditing({})}>Add the first one</button>}>
              Note down what you spend as you spend it and your profit stays accurate.
            </Empty>
          ) : <>
            <div className="bo-card-head">
              <h2>{rows.length} {rows.length === 1 ? 'entry' : 'entries'}</h2>
              <ExportButtons
                onCsv={() => downloadCsv(`expenses_${from}_${to}`, columns, rows)}
                onPrint={() => printPage({ title: 'Expenses', subtitle: `${fmtDate(from)} to ${fmtDate(to)}`, sections: [{ columns, rows, total: { amount: money(data.total) } }] })} />
            </div>
            <DataTable rowKey={(r) => r.id} rows={rows} total={{ amount: money(data.total) }} columns={[...columns, {
              key: 'x', label: '', render: (r) => (
                <span className="row noprint" style={{ gap: 2, justifyContent: 'flex-end' }}>
                  <button className="ghost btn-sm" aria-label="Edit" onClick={() => setEditing(r)}><EditIcon size={15} /></button>
                  <button className="ghost btn-sm" aria-label="Delete" onClick={() => remove(r)}><TrashIcon size={15} /></button>
                </span>
              ),
            }]} />
          </>}
        </div>

        {editing && <ExpenseForm expense={editing} categories={data.categories} onClose={() => setEditing(null)} onSaved={() => { setEditing(null); reload() }} />}
      </>}
    </div>
  )
}

function ExpenseForm({ expense, categories, onClose, onSaved }) {
  const isNew = !expense.id
  const [f, setF] = useState({
    date: expense.date?.slice(0, 10) || todayPk(), category: expense.category || categories[0] || 'Other',
    amount: expense.amount || '', paymentMethod: expense.paymentMethod || 'Cash', paidTo: expense.paidTo || '', note: expense.note || '',
  })
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')
  const set = (k) => (e) => setF({ ...f, [k]: e.target.value })
  const save = async () => {
    setBusy(true); setErr('')
    try {
      const body = { ...f, amount: Number(f.amount), branchId: expense.branchId ?? null }
      if (isNew) await api.addExpense(body); else await api.updateExpense(expense.id, body)
      onSaved()
    } catch (e) { setErr(friendlyError(e)); setBusy(false) }
  }
  return (
    <Modal title={isNew ? 'Add expense' : 'Edit expense'} onClose={onClose} footer={<>
      <button onClick={onClose}>Cancel</button>
      <button className="primary push" disabled={busy || !(Number(f.amount) > 0)} onClick={save}>{busy ? 'Saving…' : 'Save'}</button>
    </>}>
      <div className="bo-chips" style={{ marginBottom: 12 }}>
        {categories.map((c) => <button key={c} className={`bo-chip${f.category === c ? ' on' : ''}`} onClick={() => setF({ ...f, category: c })}>{c}</button>)}
      </div>
      <div className="bo-form">
        <div className="field"><label className="field-label">Amount (Rs)</label><input autoFocus type="number" min="0" inputMode="decimal" value={f.amount} onChange={set('amount')} /></div>
        <div className="field"><label className="field-label">Date</label><input type="date" max={todayPk()} value={f.date} onChange={set('date')} /></div>
        <div className="field"><label className="field-label">Paid by</label>
          <select value={f.paymentMethod} onChange={set('paymentMethod')}>{PAY_METHODS.map((m) => <option key={m}>{m}</option>)}</select>
        </div>
        <div className="field"><label className="field-label">Paid to (optional)</label><input value={f.paidTo} onChange={set('paidTo')} /></div>
        <div className="field is-full"><label className="field-label">Note (optional)</label><input value={f.note} onChange={set('note')} /></div>
      </div>
      {err && <div className="form-error" style={{ marginTop: 10 }}>{err}</div>}
    </Modal>
  )
}
