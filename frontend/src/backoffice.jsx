import { useCallback, useEffect, useRef, useState } from 'react'
import { CloseIcon, DownloadIcon, PrintIcon } from './icons.jsx'

// What the back-office screens share: page headings, figures, dialogs, dates in Pakistan time,
// and getting a table out of the app as a spreadsheet, a printed page or a WhatsApp message.

// ---- dates: the restaurant's day is the Pakistan calendar day, wherever the browser is ----
export const todayPk = () => new Date(Date.now() + 5 * 3600 * 1000).toISOString().slice(0, 10)
export const monthPk = () => todayPk().slice(0, 7)
export const addDays = (iso, n) => {
  const d = new Date(`${iso}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() + n)
  return d.toISOString().slice(0, 10)
}
export const monthStart = (iso) => `${iso.slice(0, 7)}-01`
export const monthEnd = (iso) => {
  const d = new Date(`${monthStart(iso)}T00:00:00Z`)
  d.setUTCMonth(d.getUTCMonth() + 1)
  d.setUTCDate(0)
  return d.toISOString().slice(0, 10)
}
export const weekStart = (iso) => {
  const d = new Date(`${iso}T00:00:00Z`)
  return addDays(iso, -((d.getUTCDay() + 6) % 7))   // Monday
}
export const fmtDate = (iso) => {
  if (!iso) return ''
  return new Date(`${iso.slice(0, 10)}T00:00:00Z`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' })
}
export const fmtMonth = (key) =>
  new Date(`${key}-01T00:00:00Z`).toLocaleDateString('en-GB', { month: 'long', year: 'numeric', timeZone: 'UTC' })

// ---- loading ----
export function useData(loader, deps = []) {
  const [data, setData] = useState(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const reload = useCallback(() => {
    setLoading(true)
    return Promise.resolve(loader())
      .then((d) => { setData(d); setError('') })
      .catch((e) => setError(e?.message || 'Could not load this. Please try again.'))
      .finally(() => setLoading(false))
  }, deps)
  useEffect(() => { reload() }, [reload])
  return { data, error, loading, reload, setData }
}

// ---- layout pieces ----
export function PageHead({ title, sub, children }) {
  return (
    <header className="bo-head">
      <div className="bo-head-text">
        <h1 className="h">{title}</h1>
        {sub && <p>{sub}</p>}
      </div>
      {children && <div className="bo-head-actions noprint">{children}</div>}
    </header>
  )
}

export function Figure({ label, value, hint, tone }) {
  return (
    <div className={`bo-fig${tone ? ` is-${tone}` : ''}`}>
      <div className="bo-fig-label">{label}</div>
      <div className="bo-fig-value">{value}</div>
      {hint && <div className="bo-fig-hint">{hint}</div>}
    </div>
  )
}

export function Empty({ title, children, action }) {
  return (
    <div className="bo-empty">
      <b>{title}</b>
      {children && <p>{children}</p>}
      {action}
    </div>
  )
}

export function Loading() {
  return <div className="bo-loading" aria-label="Loading"><span /><span /><span /></div>
}

export function Problem({ children, onRetry }) {
  if (!children) return null
  return (
    <div className="form-error" role="alert">
      {children} {onRetry && <button className="ghost btn-sm" onClick={onRetry}>Try again</button>}
    </div>
  )
}

const openDialogs = []

export function Modal({ title, sub, onClose, children, footer, wide }) {
  const close = useRef(onClose)
  close.current = onClose
  useEffect(() => {
    const me = {}
    openDialogs.push(me)
    const onKey = (e) => { if (e.key === 'Escape' && openDialogs[openDialogs.length - 1] === me) close.current?.() }
    window.addEventListener('keydown', onKey)
    return () => { window.removeEventListener('keydown', onKey); openDialogs.splice(openDialogs.indexOf(me), 1) }
  }, [])
  return (
    <div className="overlay bo-overlay" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose?.() }}>
      <div className={`modal bo-modal${wide ? ' is-wide' : ''}`} role="dialog" aria-modal="true" aria-label={title}>
        <div className="bo-modal-head">
          <div>
            <div className="big">{title}</div>
            {sub && <div className="bo-modal-sub">{sub}</div>}
          </div>
          <button className="ghost bo-x" onClick={onClose} aria-label="Close"><CloseIcon size={18} /></button>
        </div>
        <div className="bo-modal-body">{children}</div>
        {footer && <div className="bo-modal-foot">{footer}</div>}
      </div>
    </div>
  )
}

export function Tabs({ value, onChange, options }) {
  return (
    <div className="seg bo-tabs noprint" role="tablist">
      {options.map((o) => (
        <button key={o.value} role="tab" aria-selected={value === o.value} className={value === o.value ? 'on' : ''} onClick={() => onChange(o.value)}>
          {o.label}
        </button>
      ))}
    </div>
  )
}

// Money that changes hands has a method. These are what Pakistani counters actually take.
export const PAY_METHODS = ['Cash', 'Card', 'JazzCash', 'Easypaisa', 'Bank transfer']

// ---- getting a table out of the app ----
export const toCsv = (columns, rows) => {
  const esc = (v) => {
    const s = v === null || v === undefined ? '' : String(v)
    return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s
  }
  return [columns.map((c) => esc(c.label)).join(','), ...rows.map((r) => columns.map((c) => esc(c.csv ? c.csv(r) : r[c.key])).join(','))].join('\r\n')
}

export function downloadCsv(name, columns, rows) {
  // The leading BOM makes Excel read Urdu names correctly.
  const url = URL.createObjectURL(new Blob(['﻿' + toCsv(columns, rows)], { type: 'text/csv;charset=utf-8' }))
  const a = document.createElement('a')
  a.href = url
  a.download = `${name}.csv`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

const escHtml = (v) => String(v ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]))

// Printed in its own window on A4: the app's own print rules are for 80mm till rolls.
export function printPage({ title, subtitle, sections }) {
  const win = window.open('', '_blank', 'width=900,height=700')
  if (!win) return false
  const table = (s) => `
    ${s.title ? `<h2>${escHtml(s.title)}</h2>` : ''}
    ${s.note ? `<p class="note">${escHtml(s.note)}</p>` : ''}
    <table><thead><tr>${s.columns.map((c) => `<th class="${c.num ? 'num' : ''}">${escHtml(c.label)}</th>`).join('')}</tr></thead>
    <tbody>${s.rows.map((r) => `<tr>${s.columns.map((c) => `<td class="${c.num ? 'num' : ''}">${escHtml(c.print ? c.print(r) : c.render ? c.render(r) : r[c.key])}</td>`).join('')}</tr>`).join('')}</tbody>
    ${s.total ? `<tfoot><tr>${s.columns.map((c, i) => `<td class="${c.num ? 'num' : ''}">${escHtml(i === 0 ? 'Total' : s.total[c.key] ?? '')}</td>`).join('')}</tr></tfoot>` : ''}
    </table>`
  win.document.write(`<!doctype html><html><head><meta charset="utf-8"><title>${escHtml(title)}</title><style>
    @page { size: A4; margin: 14mm; }
    body { font: 12.5px/1.5 "Segoe UI", Arial, sans-serif; color: #1f1714; margin: 0; }
    h1 { font: 600 21px Georgia, serif; margin: 0; }
    h2 { font: 600 14px Georgia, serif; margin: 20px 0 6px; }
    .sub { color: #6e6158; margin: 2px 0 14px; }
    .note { color: #6e6158; margin: 0 0 6px; font-size: 11.5px; }
    table { width: 100%; border-collapse: collapse; }
    th { text-align: left; font-size: 11px; color: #6e6158; border-bottom: 1.5px solid #1f1714; padding: 5px 6px; }
    td { padding: 5px 6px; border-bottom: 1px solid #e8dfd2; vertical-align: top; }
    tfoot td { font-weight: 700; border-top: 1.5px solid #1f1714; border-bottom: 0; }
    .num { text-align: right; font-variant-numeric: tabular-nums; }
    tr { break-inside: avoid; }
  </style></head><body><h1>${escHtml(title)}</h1><div class="sub">${escHtml(subtitle || '')}</div>${sections.map(table).join('')}</body></html>`)
  win.document.close()
  win.focus()
  setTimeout(() => win.print(), 250)
  return true
}

// Pakistani numbers go in as 03xx and wa.me wants 923xx.
export const waLink = (phone, text) => {
  const digits = String(phone || '').replace(/\D/g, '')
  const number = digits.startsWith('0') ? `92${digits.slice(1)}` : digits
  return `https://wa.me/${number}?text=${encodeURIComponent(text)}`
}

export function ExportButtons({ onCsv, onPrint }) {
  return (
    <span className="row noprint" style={{ gap: 6 }}>
      {onCsv && <button className="ghost btn-sm" onClick={onCsv}><DownloadIcon size={15} /> Excel</button>}
      {onPrint && <button className="ghost btn-sm" onClick={onPrint}><PrintIcon size={15} /> Print</button>}
    </span>
  )
}

// A plain table that scrolls sideways on a phone rather than squeezing every column.
export function DataTable({ columns, rows, rowKey, empty = 'Nothing here yet.', onRow, total }) {
  return (
    <div className="table-scroll bo-table">
      <table className="data-table">
        <thead>
          <tr>{columns.map((c) => <th key={c.key || c.label} className={c.num ? 'num' : ''}>{c.label}</th>)}</tr>
        </thead>
        <tbody>
          {rows.map((r, i) => (
            <tr key={rowKey ? rowKey(r) : i} className={onRow ? 'is-link' : ''} onClick={onRow ? () => onRow(r) : undefined}>
              {columns.map((c) => <td key={c.key || c.label} className={c.num ? 'num' : ''}>{c.render ? c.render(r) : r[c.key]}</td>)}
            </tr>
          ))}
          {rows.length === 0 && <tr><td colSpan={columns.length} className="empty-row">{empty}</td></tr>}
        </tbody>
        {total && rows.length > 0 && (
          <tfoot>
            <tr>{columns.map((c, i) => <td key={c.key || c.label} className={c.num ? 'num' : ''}>{i === 0 ? 'Total' : total[c.key] ?? ''}</td>)}</tr>
          </tfoot>
        )}
      </table>
    </div>
  )
}
