import { useEffect, useState } from 'react'
import { api, money } from '../api'
import { auth } from '../auth'
import { RefreshIcon } from '../icons.jsx'

// Owner/manager view of FBR filing: what's synced, what's pending/failed (with the
// exact FBR error), and a MANUAL retry (never automatic). Retry is idempotent — an
// already-filed invoice is refused server-side, so nothing gets double-submitted.
export default function FbrInvoices() {
  const [tab, setTab] = useState('pending')
  const [synced, setSynced] = useState([])
  const [pending, setPending] = useState([])
  const [busy, setBusy] = useState(0)
  const [err, setErr] = useState('')
  const canSync = auth.can('invoice.sync')

  const load = () => {
    api.fbrPending().then(setPending).catch((e) => setErr(e.message))
    api.fbrSynced().then(setSynced).catch(() => {})
  }
  useEffect(() => { load() }, [])

  const retry = async (id) => {
    setErr(''); setBusy(id)
    try { await api.fbrRetry(id); load() }
    catch (e) { setErr(e.message) }
    setBusy(0)
  }

  const rows = tab === 'pending' ? pending : synced

  return (
    <div className="wrap">
      <div className="h">FBR fiscal invoices</div>
      <p className="muted">Invoices filed with FBR appear under Synced. Anything FBR hasn't accepted stays in Pending with the reason. Fix the cause, then retry.</p>

      <div className="row" style={{ gap: 8, marginBottom: 12 }}>
        <button className={tab === 'pending' ? 'primary' : 'ghost'} onClick={() => setTab('pending')}>
          Pending / failed ({pending.length})
        </button>
        <button className={tab === 'synced' ? 'primary' : 'ghost'} onClick={() => setTab('synced')}>
          Synced ({synced.length})
        </button>
        <button className="ghost" style={{ marginLeft: 'auto' }} onClick={load}><RefreshIcon size={15} /> Refresh</button>
      </div>

      {err && <div className="card" style={{ borderColor: 'var(--red)', color: 'var(--red)', marginBottom: 10 }}>{err}</div>}

      <div className="card" style={{ padding: 0, overflowX: 'auto' }}>
        <table className="tbl" style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <Th>Invoice</Th><Th>Table</Th><Th>Total</Th><Th>Status</Th>
              <Th>{tab === 'synced' ? 'FBR number' : 'Reason / attempts'}</Th>
              {tab === 'pending' && canSync && <Th>Action</Th>}
            </tr>
          </thead>
          <tbody>
            {rows.map((i) => (
              <tr key={i.id} style={{ borderTop: '1px solid var(--line)' }}>
                <Td><b>{i.number}</b><div className="muted" style={{ fontSize: 12 }}>{i.usin}</div></Td>
                <Td>{i.tableName}</Td>
                <Td>{money(i.total)}<div className="muted" style={{ fontSize: 12 }}>{i.paymentMethod}</div></Td>
                <Td><span className={'pill ' + statusClass(i.fiscalStatus)}>{i.fiscalStatus}</span></Td>
                <Td>
                  {tab === 'synced'
                    ? <span style={{ fontFamily: 'monospace' }}>{i.fiscalNumber}</span>
                    : <>
                        <div style={{ color: 'var(--red)', fontSize: 13, maxWidth: 420 }}>{i.fiscalError || '—'}</div>
                        <div className="muted" style={{ fontSize: 12 }}>
                          {i.fiscalAttempts} {i.fiscalAttempts === 1 ? 'attempt' : 'attempts'}{i.fiscalLastAttemptAt ? ` · last ${new Date(i.fiscalLastAttemptAt).toLocaleString()}` : ''}
                        </div>
                      </>}
                </Td>
                {tab === 'pending' && canSync && (
                  <Td><button className="primary" disabled={busy === i.id} onClick={() => retry(i.id)}>
                    {busy === i.id ? 'Filing…' : <><RefreshIcon size={15} /> Retry</>}
                  </button></Td>
                )}
              </tr>
            ))}
            {rows.length === 0 && (
              <tr><td colSpan={6} className="muted" style={{ padding: 16, textAlign: 'center' }}>
                {tab === 'pending' ? 'Nothing pending. Every invoice is filed.' : 'No synced invoices yet.'}
              </td></tr>
            )}
          </tbody>
        </table>
      </div>
      {!canSync && tab === 'pending' && (
        <p className="muted" style={{ fontSize: 13, marginTop: 8 }}>You can view pending invoices, but retrying needs the “sync invoice” permission.</p>
      )}
    </div>
  )
}

function statusClass(s) {
  if (s === 'Registered') return 's-Served'
  if (s === 'Failed') return 's-Cancelled'
  return 's-Placed'
}
const Th = ({ children }) => <th style={{ textAlign: 'left', padding: '10px 12px', fontSize: 12, textTransform: 'uppercase', letterSpacing: '.04em', color: 'var(--muted)' }}>{children}</th>
const Td = ({ children, ...p }) => <td style={{ padding: '10px 12px', verticalAlign: 'top', fontSize: 14 }} {...p}>{children}</td>
