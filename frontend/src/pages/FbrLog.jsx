import { Fragment, useEffect, useState } from 'react'
import { api } from '../api'
import { toast } from '../ui.jsx'
import { friendlyError } from '../errors'

// The tax office log: every try at sending a bill to FBR or PRA, newest first. Each line says how it started
// (when settled, automatic, a Send press, a test bill), who pressed, how it ended, the office's HTTP answer and the
// reason. "Details" shows the address it went to, the exact JSON sent and the answer that came back.
// The token is never shown: it travels in a header and isn't kept.
const HOW = {
  Settle: 'When settled', Auto: 'Automatic', Manual: 'Send now', Picked: 'Send picked', All: 'Send all', Test: 'Test bill',
}
const RESULT = { Filed: 'Filed', Refused: 'Refused', Waiting: 'No answer', Error: 'Error' }
const resultClass = (o) => (o === 'Filed' ? 's-Ready' : o === 'Waiting' ? 's-Placed' : 's-Cancelled')
const envClass = (e) => (e === 'Live' ? 's-Cooking' : e === 'Test' ? 's-Approved' : 's-Placed')

export default function FbrLog({ invoice, onClearInvoice }) {
  const [rows, setRows] = useState([])
  const [more, setMore] = useState(false)
  const [day, setDay] = useState(null)
  const [problems, setProblems] = useState(false)
  const [q, setQ] = useState('')
  const [search, setSearch] = useState('')
  const [open, setOpen] = useState(null)       // id of the line shown in full
  const [detail, setDetail] = useState({})     // id -> full line
  const [err, setErr] = useState('')
  const [loading, setLoading] = useState(false)
  const narrow = useNarrow()

  const filters = { invoiceId: invoice?.id, problems, q: search }
  const load = async (before) => {
    setErr(''); setLoading(true)
    try {
      const page = await api.fbrLog({ ...filters, before, take: 100 })
      setRows((old) => (before ? [...old, ...page.rows] : page.rows))
      setMore(page.more)
      setDay(page.last24h)
    } catch (e) { setErr(friendlyError(e)) }
    setLoading(false)
  }
  useEffect(() => { load() }, [invoice?.id, problems, search])

  const toggle = async (id) => {
    if (open === id) { setOpen(null); return }
    setOpen(id)
    if (!detail[id]) {
      try {
        const d = await api.fbrLogLine(id)
        setDetail((m) => ({ ...m, [id]: d }))
      } catch (e) { toast.error(friendlyError(e)); setOpen(null) }
    }
  }

  return (
    <div>
      {day && (
        <div className="row" style={{ gap: 8, marginBottom: 10, flexWrap: 'wrap', fontSize: 13 }}>
          <span className="muted">Last 24 hours:</span>
          <span className="pill s-Ready">{day.filed} filed</span>
          <span className={'pill ' + (day.refused ? 's-Cancelled' : 's-Placed')}>{day.refused} refused</span>
          <span className="pill s-Placed">{day.waiting} no answer</span>
          {day.errors > 0 && <span className="pill s-Cancelled">{day.errors} error{day.errors === 1 ? '' : 's'}</span>}
        </div>
      )}

      <div className="row" style={{ gap: 8, marginBottom: 12, flexWrap: 'wrap', alignItems: 'center' }}>
        <form onSubmit={(e) => { e.preventDefault(); setSearch(q.trim()) }} className="row" style={{ gap: 6, flex: '1 1 220px' }}>
          <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Bill or office number" aria-label="Search the log"
            style={{ flex: 1, minWidth: 0 }} />
          <button className="ghost" type="submit">Search</button>
        </form>
        <label className="row" style={{ gap: 6, alignItems: 'center', fontSize: 14 }}>
          <input type="checkbox" checked={problems} onChange={(e) => setProblems(e.target.checked)} /> Problems only
        </label>
        {invoice && (
          <span className="pill s-Approved" style={{ display: 'inline-flex', gap: 6, alignItems: 'center' }}>
            Bill {invoice.number}
            <button className="ghost btn-sm" aria-label="Show every bill" onClick={onClearInvoice} style={{ padding: '0 6px' }}>×</button>
          </span>
        )}
        {search && (
          <span className="pill s-Approved" style={{ display: 'inline-flex', gap: 6, alignItems: 'center' }}>
            “{search}”
            <button className="ghost btn-sm" aria-label="Clear the search" onClick={() => { setQ(''); setSearch('') }} style={{ padding: '0 6px' }}>×</button>
          </span>
        )}
      </div>

      {err && <div className="card" style={{ borderColor: 'var(--red)', color: 'var(--red)', marginBottom: 10 }}>{err}</div>}

      {narrow ? (
        <div style={{ display: 'grid', gap: 10 }}>
          {rows.map((r) => (
            <div key={r.id} className="card" style={{ padding: 12 }}>
              <div className="row" style={{ justifyContent: 'space-between', gap: 8, alignItems: 'flex-start', flexWrap: 'nowrap' }}>
                <div style={{ minWidth: 0 }}>
                  <b style={{ wordBreak: 'break-all' }}>{r.billNumber}</b>
                  <div className="muted" style={{ fontSize: 12 }}>{new Date(r.at).toLocaleString()} · try {r.attempt}</div>
                </div>
                <span className={'pill ' + resultClass(r.outcome)} style={{ flexShrink: 0 }}>{RESULT[r.outcome] || r.outcome}</span>
              </div>
              <div className="muted" style={{ fontSize: 12, marginTop: 6 }}>
                {r.office} {r.environment} · {HOW[r.trigger] || r.trigger}{r.by ? ` · ${r.by}` : r.trigger === 'Auto' ? ' · by itself' : ''} · {answer(r)}
              </div>
              <div style={{ marginTop: 6 }}><NumberOrReason r={r} /></div>
              <button className="ghost btn-sm" style={{ marginTop: 8 }} onClick={() => toggle(r.id)} aria-expanded={open === r.id}>
                {open === r.id ? 'Hide' : 'Details'}
              </button>
              {open === r.id && (detail[r.id] ? <Detail d={detail[r.id]} /> : <div className="muted" style={{ paddingTop: 8 }}>Loading…</div>)}
            </div>
          ))}
          {rows.length === 0 && !loading && (
            <div className="card muted" style={{ padding: 16, textAlign: 'center' }}>
              {problems || search || invoice ? 'Nothing matches.' : 'Nothing sent yet. Each try at sending a bill shows here.'}
            </div>
          )}
        </div>
      ) : (
      <div className="card" style={{ padding: 0, overflowX: 'auto' }}>
        <table className="tbl" style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr><Th>When</Th><Th>Bill</Th><Th>Office</Th><Th>How</Th><Th>Result</Th><Th>Number / reason</Th><Th /></tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <Fragment key={r.id}>
                <tr style={{ borderTop: '1px solid var(--line)' }}>
                  <Td>{new Date(r.at).toLocaleString()}<div className="muted" style={{ fontSize: 12 }}>try {r.attempt}</div></Td>
                  <Td><b>{r.billNumber}</b>{r.posId && <div className="muted" style={{ fontSize: 12 }}>POSID {r.posId}</div>}</Td>
                  <Td>{r.office}<div><span className={'pill ' + envClass(r.environment)} style={{ fontSize: 11 }}>{r.environment}</span></div></Td>
                  <Td>{HOW[r.trigger] || r.trigger}<div className="muted" style={{ fontSize: 12 }}>{r.by || (r.trigger === 'Auto' ? 'by itself' : '')}</div></Td>
                  <Td>
                    <span className={'pill ' + resultClass(r.outcome)}>{RESULT[r.outcome] || r.outcome}</span>
                    <div className="muted" style={{ fontSize: 12 }}>{answer(r)}</div>
                  </Td>
                  <Td><NumberOrReason r={r} /></Td>
                  <Td><button className="ghost btn-sm" onClick={() => toggle(r.id)} aria-expanded={open === r.id}>
                    {open === r.id ? 'Hide' : 'Details'}
                  </button></Td>
                </tr>
                {open === r.id && (
                  <tr>
                    <td colSpan={7} style={{ padding: '0 12px 14px', background: 'var(--surface2)' }}>
                      {detail[r.id] ? <Detail d={detail[r.id]} /> : <div className="muted" style={{ padding: 10 }}>Loading…</div>}
                    </td>
                  </tr>
                )}
              </Fragment>
            ))}
            {rows.length === 0 && !loading && (
              <tr><td colSpan={7} className="muted" style={{ padding: 16, textAlign: 'center' }}>
                {problems || search || invoice ? 'Nothing matches.' : 'Nothing sent yet. Each try at sending a bill shows here.'}
              </td></tr>
            )}
          </tbody>
        </table>
      </div>
      )}
      {more && (
        <div style={{ textAlign: 'center', marginTop: 10 }}>
          <button className="ghost" disabled={loading} onClick={() => load(rows[rows.length - 1]?.id)}>{loading ? 'Loading…' : 'Show older'}</button>
        </div>
      )}
      <p className="muted" style={{ fontSize: 13, marginTop: 8 }}>
        Lines are kept for 60 days. The token is never shown or kept here. On a restaurant PC, the PC keeps its own log.
      </p>
    </div>
  )
}

// The office's HTTP answer and how long it took, e.g. "HTTP 200 · 210 ms".
function answer(r) {
  const head = r.httpStatus ? `HTTP ${r.httpStatus}` : r.environment === 'Practice' ? 'not sent' : 'no HTTP answer'
  const took = r.durationMs > 0 ? ` · ${r.durationMs < 1000 ? r.durationMs + ' ms' : (r.durationMs / 1000).toFixed(1) + ' s'}` : ''
  return head + took
}

function NumberOrReason({ r }) {
  return r.fiscalNumber
    ? <span style={{ fontFamily: 'monospace', wordBreak: 'break-all', fontSize: 13 }}>{r.fiscalNumber}</span>
    : <div style={{ color: r.outcome === 'Waiting' ? 'inherit' : 'var(--red)', fontSize: 13, maxWidth: 380 }}>{r.error || '—'}</div>
}

// True on a phone-width screen, where the log shows as cards instead of a wide table.
function useNarrow(px = 700) {
  const q = `(max-width: ${px}px)`
  const [narrow, setNarrow] = useState(() => typeof window !== 'undefined' && !!window.matchMedia?.(q).matches)
  useEffect(() => {
    const m = window.matchMedia?.(q)
    if (!m) return undefined
    const f = () => setNarrow(m.matches)
    m.addEventListener('change', f)
    return () => m.removeEventListener('change', f)
  }, [q])
  return narrow
}

function Detail({ d }) {
  return (
    <div style={{ display: 'grid', gap: 10, paddingTop: 10 }}>
      <div style={{ fontSize: 13 }}>
        <span className="muted">Sent to: </span>
        <span style={{ fontFamily: 'monospace', wordBreak: 'break-all' }}>{d.endpoint || '—'}</span>
      </div>
      {d.row.error && <div style={{ fontSize: 13 }}><span className="muted">Reason: </span>{d.row.error}</div>}
      <Block title="What was sent (JSON)" text={d.requestBody} empty="Nothing was sent: the bill stopped before it left (see the reason)." />
      <Block title={`The office's answer${d.row.httpStatus ? ` (HTTP ${d.row.httpStatus})` : ''}`} text={d.responseBody}
        empty={d.row.environment === 'Practice' ? 'Practice: nothing was sent, so there is no answer.' : 'No answer came back.'} />
    </div>
  )
}

function Block({ title, text, empty }) {
  const pretty = (() => {
    if (!text) return ''
    try { return JSON.stringify(JSON.parse(text), null, 2) } catch { return text }
  })()
  const copy = async () => {
    try { await navigator.clipboard.writeText(pretty); toast.success('Copied') }
    catch { toast.error('Copying isn’t allowed here. Select the text and copy it.') }
  }
  return (
    <div>
      <div className="row" style={{ justifyContent: 'space-between', alignItems: 'center', marginBottom: 4 }}>
        <b style={{ fontSize: 13 }}>{title}</b>
        {pretty && <button className="ghost btn-sm" onClick={copy}>Copy</button>}
      </div>
      {pretty
        ? <pre style={{ margin: 0, padding: 10, maxHeight: 320, overflow: 'auto', fontSize: 12, lineHeight: 1.45,
            background: 'var(--surface)', border: '1px solid var(--line)', borderRadius: 8, whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>{pretty}</pre>
        : <div className="muted" style={{ fontSize: 13 }}>{empty}</div>}
    </div>
  )
}

const Th = ({ children }) => <th style={{ textAlign: 'left', padding: '10px 12px', fontSize: 12, textTransform: 'uppercase', letterSpacing: '.04em', color: 'var(--muted)' }}>{children}</th>
const Td = ({ children, ...p }) => <td style={{ padding: '10px 12px', verticalAlign: 'top', fontSize: 14 }} {...p}>{children}</td>
