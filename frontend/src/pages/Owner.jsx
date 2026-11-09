import { useRef } from 'react'
import { QRCodeSVG } from 'qrcode.react'
import { Link } from 'react-router-dom'
import { api } from '../api'
import { auth } from '../auth'
import { Empty, Loading, PageHead, Problem, useData } from '../backoffice.jsx'
import { BankIcon, PrintIcon } from '../icons.jsx'

const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]))

// One QR per table. Each opens the menu for that exact table, so an order can't land on the wrong one.
export default function Owner() {
  const { data: tables, error, loading, reload } = useData(() => api.getTables(), [])
  const sheet = useRef(null)
  const slug = auth.staffSlug()
  const place = auth.getStaff()?.restaurantName || ''
  const urlFor = (code) => `${window.location.origin}/r/${slug}/t/${code}`

  const groups = {}
  for (const t of tables || []) (groups[t.branchName || ''] ??= []).push(t)
  const entries = Object.entries(groups)

  // Printed in its own window on A4, six to a page, ready to cut and stand on the tables.
  const print = () => {
    const cards = [...sheet.current.querySelectorAll('[data-qr]')].map((el) => `
      <div class="card">
        <div class="place">${esc(place)}</div>
        <div class="scan">Scan to see the menu and order</div>
        ${el.querySelector('svg').outerHTML}
        <div class="table">${esc(el.dataset.qr)}</div>
      </div>`).join('')
    const win = window.open('', '_blank', 'width=900,height=700')
    if (!win) return
    win.document.write(`<!doctype html><html><head><meta charset="utf-8"><title>Table QR codes</title><style>
      @page { size: A4; margin: 10mm; }
      body { margin: 0; font-family: Georgia, 'Times New Roman', serif; color: #1f1714; }
      .sheet { display: grid; grid-template-columns: 1fr 1fr; gap: 6mm; }
      .card { border: 1px dashed #b9ab9a; border-radius: 4mm; padding: 7mm 5mm; text-align: center; break-inside: avoid; height: 78mm; box-sizing: border-box; }
      .place { font-size: 17pt; font-weight: 600; }
      .scan { font: 10pt 'Segoe UI', Arial, sans-serif; color: #6e6158; margin: 1mm 0 3mm; }
      svg { width: 44mm; height: 44mm; display: block; margin: 0 auto; }
      .table { font-size: 15pt; font-weight: 600; margin-top: 3mm; }
    </style></head><body><div class="sheet">${cards}</div></body></html>`)
    win.document.close()
    win.focus()
    setTimeout(() => win.print(), 250)
  }

  return (
    <div className="bo-wrap">
      <PageHead title="Table QR codes" sub="Print these and put one on each table. A guest scans it and that table's menu opens, with nothing to install.">
        {tables?.length > 0 && <button className="primary" onClick={print}><PrintIcon size={16} /> Print all</button>}
      </PageHead>
      <Problem onRetry={reload}>{error}</Problem>
      {loading && !tables && <Loading />}
      {tables?.length === 0 && (
        <div className="bo-card"><Empty title="No tables yet" action={<Link to="/admin/settings"><button className="primary">Add tables in Settings</button></Link>}>Add your tables and their QR codes appear here.</Empty></div>
      )}
      <div ref={sheet}>
        {entries.map(([branch, list]) => (
          <div key={branch} style={{ marginBottom: 18 }}>
            {entries.length > 1 && <div className="fl-section"><h2><BankIcon size={15} /> {branch || 'Main branch'}</h2></div>}
            <div className="qr-grid">
              {list.map((t) => (
                <div className="qr-card" key={t.code} data-qr={t.name}>
                  <QRCodeSVG value={urlFor(t.code)} size={148} level="M" />
                  <b>{t.name}</b>
                  <Link to={`/r/${slug}/t/${t.code}`}><button className="ghost btn-sm">Open this table's menu</button></Link>
                </div>
              ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}
