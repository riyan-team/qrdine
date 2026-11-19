import { useEffect, useState } from 'react'
import { api, money } from '../api'
import { auth } from '../auth'
import { toast } from '../ui.jsx'
import { friendlyError } from '../errors'
import { RefreshIcon } from '../icons.jsx'

// The tax office (FBR / PRA) view: bills still to go (waiting, or refused with the office's reason) and bills filed.
// Sending: one bill, the picked ones, or every waiting one (25 a press). A bill already filed is refused by the
// server, and a bill being sent by someone else at the same moment is left alone, so nothing is filed twice.
export default function FbrInvoices() {
  const [tab, setTab] = useState('pending')
  const [synced, setSynced] = useState([])
  const [pending, setPending] = useState([])
  const [picked, setPicked] = useState(() => new Set())
  const [busy, setBusy] = useState('')   // '' | 'all' | 'picked' | <id>
  const [err, setErr] = useState('')
  const [last, setLast] = useState(null)
  const canSync = auth.can('invoice.sync')

  const load = () => {
    api.fbrPending().then((rows) => {
      setPending(rows)
      setPicked((p) => new Set([...p].filter((id) => rows.some((r) => r.id === id))))
    }).catch((e) => setErr(friendlyError(e)))
    api.fbrSynced().then(setSynced).catch(() => {})
  }
  useEffect(() => { load() }, [])

  const sendOne = async (id) => {
    setErr(''); setBusy(id)
    try {
      const r = await api.fbrRetry(id)
      if (r.fiscalStatus === 'Registered') toast.success(`${r.number} filed: ${r.fiscalNumber}`)
      else toast.error(`${r.number} not filed: ${r.fiscalError || 'no answer'}`)
      load()
    } catch (e) { setErr(friendlyError(e)) }
    setBusy('')
  }
  const sendMany = async (ids, which) => {
    setErr(''); setBusy(which)
    try {
      const r = await api.fbrRetryMany(ids)
      setLast(r)
      const parts = [`${r.sent} filed`]
      if (r.refused) parts.push(`${r.refused} refused`)
      if (r.stillWaiting) parts.push(`${r.stillWaiting} couldn't reach the office`)
      if (r.skipped) parts.push(`${r.skipped} skipped`)
      const text = parts.join(', ') + (r.left ? `. ${r.left} still to go.` : '. All done.')
      if (r.refused || r.stillWaiting) toast.error(text); else toast.success(text)
      if (which === 'picked') setPicked(new Set())
      load()
    } catch (e) { setErr(friendlyError(e)) }
    setBusy('')
  }

  const rows = tab === 'pending' ? pending : synced
  const allPicked = pending.length > 0 && pending.every((r) => picked.has(r.id))
  const togglePick = (id) => setPicked((p) => { const n = new Set(p); n.has(id) ? n.delete(id) : n.add(id); return n })
  const toggleAll = () => setPicked(allPicked ? new Set() : new Set(pending.map((r) => r.id)))
  const sending = tab === 'pending' && canSync
  const cols = 6 + (sending ? 2 : 0)

  return (
    <div className="wrap">
      <div className="h">Tax office invoices</div>
      <p className="muted">
        Bills filed with FBR or PRA are under Filed, with the office's number. Bills still to go wait under Waiting:
        ones that couldn't reach the office, and ones it refused, with its reason. Fix the reason, then send again.
      </p>

      <div className="row" style={{ gap: 8, marginBottom: 12, flexWrap: 'wrap' }}>
        <button className={tab === 'pending' ? 'primary' : 'ghost'} onClick={() => setTab('pending')}>
          Waiting / refused ({pending.length})
        </button>
        <button className={tab === 'synced' ? 'primary' : 'ghost'} onClick={() => setTab('synced')}>
          Filed ({synced.length})
        </button>
        <button className="ghost" style={{ marginLeft: 'auto' }} onClick={load}><RefreshIcon size={15} /> Refresh</button>
      </div>

      {sending && pending.length > 0 && (
        <div className="row" style={{ gap: 8, marginBottom: 12, flexWrap: 'wrap', alignItems: 'center' }}>
          <button className="primary" disabled={!!busy} onClick={() => sendMany(null, 'all')}>
            {busy === 'all' ? 'Sending…' : `Send all waiting${pending.length > 25 ? ' (25 at a time)' : ''}`}
          </button>
          <button className="ghost" disabled={!!busy || picked.size === 0} onClick={() => sendMany([...picked], 'picked')}>
            {busy === 'picked' ? 'Sending…' : `Send picked (${picked.size})`}
          </button>
          {last?.stopped && <span style={{ color: 'var(--red)', fontSize: 13 }}>{last.stopped}</span>}
        </div>
      )}

      {err && <div className="card" style={{ borderColor: 'var(--red)', color: 'var(--red)', marginBottom: 10 }}>{err}</div>}

      <div className="card" style={{ padding: 0, overflowX: 'auto' }}>
        <table className="tbl" style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              {sending && (
                <Th><input type="checkbox" aria-label="Pick all" checked={allPicked} disabled={pending.length === 0} onChange={toggleAll} /></Th>
              )}
              <Th>Bill</Th><Th>Table</Th><Th>Total</Th><Th>Office</Th><Th>Status</Th>
              <Th>{tab === 'synced' ? 'Office number' : 'Reason / tries'}</Th>
              {sending && <Th />}
            </tr>
          </thead>
          <tbody>
            {rows.map((i) => (
              <tr key={i.id} style={{ borderTop: '1px solid var(--line)' }}>
                {sending && (
                  <Td><input type="checkbox" aria-label={`Pick ${i.number}`} checked={picked.has(i.id)} onChange={() => togglePick(i.id)} /></Td>
                )}
                <Td><b>{i.number}</b><div className="muted" style={{ fontSize: 12 }}>{new Date(i.issuedAt).toLocaleString()}</div></Td>
                <Td>{i.tableName}</Td>
                <Td>{money(i.total)}<div className="muted" style={{ fontSize: 12 }}>{i.paymentMethod}{i.tax ? ` · tax ${money(i.tax)}` : ''}</div></Td>
                <Td>{i.office}{i.environment && <div><span className={'pill ' + envClass(i.environment)} style={{ fontSize: 11 }}>{i.environment}</span></div>}</Td>
                <Td><span className={'pill ' + statusClass(i.fiscalStatus)}>{i.beingSent ? 'Sending…' : statusText(i)}</span></Td>
                <Td>
                  {tab === 'synced'
                    ? <><span style={{ fontFamily: 'monospace', wordBreak: 'break-all' }}>{i.fiscalNumber}</span>
                        {i.syncedByPosId && <div className="muted" style={{ fontSize: 12 }}>POSID {i.syncedByPosId}</div>}</>
                    : <>
                        <div style={{ color: i.fiscalStatus === 'Failed' ? 'var(--red)' : 'inherit', fontSize: 13, maxWidth: 420 }}>{i.fiscalError || '—'}</div>
                        <div className="muted" style={{ fontSize: 12 }}>
                          {i.fiscalAttempts} {i.fiscalAttempts === 1 ? 'try' : 'tries'}{i.fiscalLastAttemptAt ? ` · last ${new Date(i.fiscalLastAttemptAt).toLocaleString()}` : ''}
                        </div>
                      </>}
                </Td>
                {sending && (
                  <Td><button className="primary btn-sm" disabled={!!busy || i.beingSent} onClick={() => sendOne(i.id)}>
                    {busy === i.id ? 'Sending…' : 'Send now'}
                  </button></Td>
                )}
              </tr>
            ))}
            {rows.length === 0 && (
              <tr><td colSpan={cols} className="muted" style={{ padding: 16, textAlign: 'center' }}>
                {tab === 'pending' ? 'Nothing waiting. Every bill is filed.' : 'No filed bills yet.'}
              </td></tr>
            )}
          </tbody>
        </table>
      </div>
      {!canSync && tab === 'pending' && (
        <p className="muted" style={{ fontSize: 13, marginTop: 8 }}>You can see waiting bills, but sending them needs the “sync invoice” permission.</p>
      )}
    </div>
  )
}

function statusText(i) {
  if (i.fiscalStatus === 'Registered') return 'Filed'
  if (i.fiscalStatus === 'Failed') return 'Refused'
  return 'Waiting'
}
function statusClass(s) {
  if (s === 'Registered') return 's-Ready'
  if (s === 'Failed') return 's-Cancelled'
  return 's-Placed'
}
const envClass = (e) => (e === 'Live' ? 's-Cooking' : e === 'Test' ? 's-Approved' : 's-Placed')
const Th = ({ children }) => <th style={{ textAlign: 'left', padding: '10px 12px', fontSize: 12, textTransform: 'uppercase', letterSpacing: '.04em', color: 'var(--muted)' }}>{children}</th>
const Td = ({ children, ...p }) => <td style={{ padding: '10px 12px', verticalAlign: 'top', fontSize: 14 }} {...p}>{children}</td>
