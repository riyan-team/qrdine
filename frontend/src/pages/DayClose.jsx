import { useEffect, useState } from 'react'
import { api, count, money } from '../api'
import { toast } from '../ui.jsx'
import { friendlyError } from '../errors'
import { Figure, Loading, PageHead, Problem, addDays, fmtDate, printPage, todayPk, useData } from '../backoffice.jsx'
import { CheckIcon, PrintIcon } from '../icons.jsx'

// The last thing done each night: count the drawer against what the books say should be in it.
export default function DayClose() {
  const [date, setDate] = useState(todayPk())
  const { data, error, loading, reload, setData } = useData(() => api.closing(date), [date])
  const [opening, setOpening] = useState('')
  const [counted, setCounted] = useState('')
  const [takenOut, setTakenOut] = useState('')
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (!data) return
    setOpening(String(data.closed?.openingCash ?? data.openingCash ?? 0))
    setCounted(data.closed ? String(data.closed.counted) : '')
    setTakenOut(data.closed ? String(data.closed.takenOut) : '')
    setNote(data.closed?.note || '')
  }, [data])

  const openingNum = Number(opening) || 0
  const cashIn = (data?.cashSales ?? 0) + (data?.khataReceipts ?? 0)
  const cashOut = (data?.cashExpenses ?? 0) + (data?.supplierPayments ?? 0)
  const expected = openingNum + cashIn - cashOut
  const hasCount = counted !== ''
  const diff = (Number(counted) || 0) - expected

  const save = async () => {
    setBusy(true)
    try {
      const res = await api.closeDay({ date, openingCash: openingNum, countedCash: Number(counted) || 0, takenOut: Number(takenOut) || 0, note: note.trim() || null, branchId: null })
      setData(res)
      toast.success(`${fmtDate(date)} is closed`)
    } catch (e) { toast.error(friendlyError(e)) }
    setBusy(false)
  }

  const print = () => {
    const rows = [
      { label: 'Opening cash', amount: openingNum }, { label: 'Cash sales', amount: data.cashSales },
      { label: 'Cash received on khata', amount: data.khataReceipts }, { label: 'Cash expenses', amount: -data.cashExpenses },
      { label: 'Cash paid to suppliers', amount: -data.supplierPayments },
    ]
    printPage({
      title: `Day closing: ${fmtDate(date)}`, subtitle: `${count(data.bills, 'bill')}, sales ${money(data.sales)}`,
      sections: [
        { columns: [{ key: 'label', label: 'Cash' }, { key: 'amount', label: 'Amount', num: true, print: (r) => money(r.amount) }], rows, total: { amount: money(expected) } },
        { title: 'Count', columns: [{ key: 'label', label: '' }, { key: 'amount', label: 'Amount', num: true, print: (r) => money(r.amount) }],
          rows: [{ label: 'Counted in the drawer', amount: Number(counted) || 0 }, { label: 'Difference', amount: diff }, { label: 'Taken out', amount: Number(takenOut) || 0 }] },
        { title: 'All sales by how they were paid', columns: [{ key: 'name', label: 'Paid by' }, { key: 'count', label: 'Payments', num: true }, { key: 'amount', label: 'Amount', num: true, print: (r) => money(r.amount) }], rows: data.methods },
      ],
    })
  }

  return (
    <div className="bo-wrap">
      <PageHead title="Day closing" sub="Count the cash at the end of the night. Anything short or over is written down, and tomorrow opens with what's left in the drawer." />
      <div className="bo-toolbar">
        <button className="btn-sm" onClick={() => setDate(addDays(date, -1))}>Previous day</button>
        <input type="date" max={todayPk()} value={date} onChange={(e) => e.target.value && setDate(e.target.value)} />
        <button className="btn-sm" disabled={date >= todayPk()} onClick={() => setDate(addDays(date, 1))}>Next day</button>
      </div>
      <Problem onRetry={reload}>{error}</Problem>
      {loading && !data && <Loading />}
      {data && <>
        <div className="bo-figs">
          <Figure tone="lead" label="Total sales" value={money(data.sales)} hint={count(data.bills, 'bill')} />
          <Figure label="Cash sales" value={money(data.cashSales)} />
          <Figure label="Card and wallets" value={money(data.otherSales)} />
          <Figure label="On khata" value={money(data.khataSales)} hint="Not in the drawer" />
        </div>
        <div className="bo-cols">
          <div className="bo-card">
            <div className="bo-card-head"><h2>What should be in the drawer</h2></div>
            <div className="dc-sum">
              <div><span>Opening cash</span>
                <input type="number" min="0" inputMode="numeric" style={{ width: 120, textAlign: 'right', padding: '5px 8px' }} value={opening} onChange={(e) => setOpening(e.target.value)} />
              </div>
              <div><span>Cash sales</span><span>{money(data.cashSales)}</span></div>
              <div><span>Cash received on khata</span><span>{money(data.khataReceipts)}</span></div>
              <div><span>Cash expenses</span><span>{data.cashExpenses > 0 ? '− ' : ''}{money(data.cashExpenses)}</span></div>
              <div><span>Cash paid to suppliers</span><span>{data.supplierPayments > 0 ? '− ' : ''}{money(data.supplierPayments)}</span></div>
              <div className="is-total"><span>Should be</span><span>{money(expected)}</span></div>
            </div>
          </div>
          <div className="bo-card">
            <div className="bo-card-head"><h2>What you counted</h2></div>
            <div className="bo-form">
              <div className="field"><label className="field-label">Cash in the drawer (Rs)</label><input autoFocus={!data.closed} type="number" min="0" inputMode="numeric" value={counted} onChange={(e) => setCounted(e.target.value)} /></div>
              <div className="field"><label className="field-label">Taken out by owner (Rs)</label><input type="number" min="0" inputMode="numeric" placeholder="0" value={takenOut} onChange={(e) => setTakenOut(e.target.value)} /></div>
              <div className="field is-full"><label className="field-label">Note (optional)</label><input value={note} onChange={(e) => setNote(e.target.value)} /></div>
            </div>
            {hasCount && (
              <div className={`dc-diff${Math.abs(diff) < 1 ? '' : diff < 0 ? ' is-short' : ' is-over'}`}>
                {Math.abs(diff) < 1 ? 'The drawer matches exactly.' : diff < 0 ? `Short by ${money(-diff)}` : `Over by ${money(diff)}`}
              </div>
            )}
            {data.closed && <div className="bo-small" style={{ marginBottom: 10 }}><CheckIcon size={14} /> Closed by {data.closed.closedBy} at {new Date(data.closed.closedAt).toLocaleTimeString('en-PK', { timeStyle: 'short' })}. Saving again replaces this count.</div>}
            <div className="row" style={{ gap: 8 }}>
              <button onClick={print}><PrintIcon size={15} /> Print</button>
              <button className="primary" style={{ marginLeft: 'auto' }} disabled={busy || !hasCount} onClick={save}>{busy ? 'Saving…' : data.closed ? 'Save new count' : 'Close the day'}</button>
            </div>
          </div>
        </div>
      </>}
    </div>
  )
}
