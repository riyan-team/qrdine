import { useEffect, useState } from 'react'
import { QRCodeSVG } from 'qrcode.react'
import { Link } from 'react-router-dom'
import { api } from '../api'
import { auth } from '../auth'

// Owner view: generates a printable QR per table. In real life you print these
// and stick one on each table. Scanning opens that table's menu directly.
export default function Owner() {
  const [tables, setTables] = useState([])
  const slug = auth.staffSlug()
  useEffect(() => { api.getTables().then(setTables).catch(() => {}) }, [])

  const origin = window.location.origin
  const urlFor = (code) => `${origin}/r/${slug}/t/${code}`
  const groupByBranch = (list) => {
    const m = {}
    for (const t of list) (m[t.branchName || ''] ??= []).push(t)
    return Object.entries(m)
  }

  return (
    <div className="wrap">
      <div className="h">Table QR codes</div>
      <p className="muted noprint">
        Print these and place one on each table. A customer scans it and the menu for that exact
        table opens — no app to install. Click a card to open it here for testing.
        <br />Then open <b>Reception</b>, <b>Kitchen</b> and <b>Waiter</b> tabs to watch orders flow live.
      </p>
      <div className="row noprint" style={{ marginBottom: 12 }}>
        <button className="primary" onClick={() => window.print()}>🖨️ Print all QR codes</button>
      </div>

      {groupByBranch(tables).map(([branch, list]) => (
        <div key={branch}>
          {tables.some((t) => t.branchName && t.branchName !== branch) || branch !== '' ? (
            <div className="cat-title">🏢 {branch || 'Main Branch'}</div>
          ) : null}
          <div className="grid cols">
            {list.map((t) => (
              <div className="card qrcard" key={t.code}>
                <div className="qbox"><QRCodeSVG value={urlFor(t.code)} size={150} level="M" /></div>
                <div className="big" style={{ marginTop: 10 }}>{t.name}</div>
                <div className="muted" style={{ fontSize: 12, wordBreak: 'break-all' }}>{urlFor(t.code)}</div>
                <Link to={`/r/${slug}/t/${t.code}`} className="noprint">
                  <button className="primary" style={{ marginTop: 10, width: '100%' }}>Open this table's menu →</button>
                </Link>
              </div>
            ))}
          </div>
        </div>
      ))}
      {tables.length === 0 && <div className="empty">Loading tables… is the API running on :5099?</div>}
    </div>
  )
}
