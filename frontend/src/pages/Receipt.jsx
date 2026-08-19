import { QRCodeSVG } from 'qrcode.react'
import { money } from '../api'

// Renders a bill/invoice in receipt form. Works for both a live preview (no
// number/date) and a settled invoice. Only this element prints (see styles.css).
export default function Receipt({ bill, invoice }) {
  const d = invoice || bill
  if (!d) return null
  const isInvoice = !!invoice

  return (
    <div className="receipt">
      <div className="r-h">
        <div style={{ fontWeight: 800, fontSize: 18 }}>{d.restaurantName}</div>
        <div style={{ fontSize: 13 }}>{d.tableName}</div>
        {isInvoice && (
          <div style={{ fontSize: 12, marginTop: 4 }}>
            {d.number} · {new Date(d.issuedAt).toLocaleString('en-PK')}
          </div>
        )}
      </div>

      <table>
        <tbody>
          {d.lines.map((l, i) => (
            <tr key={i}>
              <td style={{ padding: '3px 0' }}>{l.qty}× {l.name}</td>
              <td style={{ textAlign: 'right' }}>{money(l.lineTotal)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <div className="r-tot">
        <div className="r-line"><span>Subtotal</span><span>{money(d.subtotal)}</span></div>
        {d.serviceChargePercent > 0 &&
          <div className="r-line"><span>Service {d.serviceChargePercent}%</span><span>{money(d.serviceChargeAmount)}</span></div>}
        {d.taxPercent > 0 &&
          <div className="r-line"><span>GST {d.taxPercent}%</span><span>{money(d.taxAmount)}</span></div>}
        {isInvoice && d.discountAmount > 0 &&
          <div className="r-line"><span>Discount</span><span>− {money(d.discountAmount)}</span></div>}
        <div className="r-line r-grand"><span>Total</span><span>{money(d.total)}</span></div>
        {isInvoice && <div className="r-line" style={{ fontSize: 13 }}><span>Paid via</span><span>{d.paymentMethod}</span></div>}
      </div>

      {/* Fiscal block. The receipt ALWAYS prints; this only reflects the FBR state. */}
      {isInvoice && d.fiscalNumber && (
        <div style={{ textAlign: 'center', marginTop: 12, paddingTop: 10, borderTop: '1px dashed #999' }}>
          <div style={{ fontSize: 12, fontWeight: 800, letterSpacing: '.04em' }}>✓ INTEGRATED WITH FBR</div>
          <div style={{ background: '#fff', display: 'inline-block', padding: 4, marginTop: 4 }}>
            <QRCodeSVG value={d.fiscalQrPayload || d.fiscalNumber} size={92} level="M" />
          </div>
          <div style={{ fontSize: 11, marginTop: 6 }}>
            {(d.fiscalProvider === 'Simulated' || d.fiscalProvider === 'Sandbox') ? 'FBR (test)' : 'FBR'} Fiscal Invoice
          </div>
          <div style={{ fontSize: 12, fontWeight: 700, letterSpacing: '.02em' }}>{d.fiscalNumber}</div>
          <div style={{ fontSize: 10, color: '#666' }}>Verify with the FBR Tax Asaan app</div>
        </div>
      )}
      {isInvoice && !d.fiscalNumber && (d.fiscalStatus === 'Pending' || d.fiscalStatus === 'Failed') && (
        <div style={{ textAlign: 'center', marginTop: 12, paddingTop: 10, borderTop: '1px dashed #999' }}>
          <div style={{ fontSize: 11, fontWeight: 700, color: '#a00' }}>FBR FILING PENDING</div>
          <div style={{ fontSize: 10, color: '#666', marginTop: 2 }}>
            This sale is valid. It will be filed with FBR shortly.
          </div>
        </div>
      )}

      <div className="r-foot">
        {isInvoice ? 'Thank you — please come again!' : 'Bill preview (not yet paid)'}
      </div>
    </div>
  )
}
