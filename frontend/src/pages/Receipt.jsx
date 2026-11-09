import { QRCodeSVG } from 'qrcode.react'
import { money } from '../api'

// Renders a bill/invoice in receipt form. Works for both a live preview (no
// number/date) and a settled invoice. Only this element prints (see styles.css).
// Layout is a fixed 3-column grid (Qty · Item · Amount) so amounts always line up
// on the right edge and long item names wrap cleanly under their own column.
export default function Receipt({ bill, invoice }) {
  const d = invoice || bill
  if (!d) return null
  const isInvoice = !!invoice
  // Tax label: the snapshot on a settled invoice, or the current name on a preview.
  const taxLabel = d.taxLabel || d.taxName || 'Tax'
  // Show cash received + change for cash payments (or whenever change was given).
  const showCash = (d.amountTendered > 0) && (d.changeGiven > 0 || /cash/i.test(d.paymentMethod || ''))

  return (
    <div className="receipt">
      <div className="r-h">
        {d.logoUrl && <img className="r-logo" src={d.logoUrl} alt="" />}
        <div className="r-name">{d.restaurantName}</div>
        {d.receiptHeader && <div className="r-sub">{d.receiptHeader}</div>}
        <div className="r-doc">{isInvoice ? 'TAX INVOICE' : 'BILL, NOT PAID'}</div>
      </div>

      {/* Meta: table, invoice number & date — a label/value grid so it stays aligned. */}
      <div className="r-meta">
        <div className="r-meta-row"><span>{!d.channel || d.channel === 'Dine' ? 'Table' : 'Order'}</span><span>{d.tableName}</span></div>
        {d.customerName && <div className="r-meta-row"><span>Customer</span><span>{d.customerName}</span></div>}
        {d.customerPhone && <div className="r-meta-row"><span>Phone</span><span>{d.customerPhone}</span></div>}
        {d.deliveryAddress && <div className="r-meta-row"><span>Deliver to</span><span>{d.deliveryAddress}</span></div>}
        {d.riderName && <div className="r-meta-row"><span>Rider</span><span>{d.riderName}</span></div>}
        {isInvoice && <>
          <div className="r-meta-row"><span>Invoice</span><span>{d.number}</span></div>
          <div className="r-meta-row"><span>Date</span><span>{new Date(d.issuedAt).toLocaleString('en-PK')}</span></div>
        </>}
      </div>

      <table className="r-items">
        <colgroup>
          <col className="c-qty" /><col className="c-item" /><col className="c-amt" />
        </colgroup>
        <thead>
          <tr><th className="r-qty">Qty</th><th className="r-item">Item</th><th className="r-amt">Amount</th></tr>
        </thead>
        <tbody>
          {d.lines.map((l, i) => (
            <tr key={i}>
              <td className="r-qty">{l.qty}</td>
              <td className="r-item">
                <span className="r-item-name">{l.name}</span>
                {l.qty > 1 && l.unitPrice > 0 &&
                  <span className="r-item-rate">@ {money(l.unitPrice)}</span>}
              </td>
              <td className="r-amt">{money(l.lineTotal)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <div className="r-tot">
        <div className="r-line"><span>Subtotal</span><span>{money(d.subtotal)}</span></div>
        {d.discountAmount > 0 &&
          <div className="r-line"><span>Discount</span><span>− {money(d.discountAmount)}</span></div>}
        {d.serviceChargePercent > 0 &&
          <div className="r-line"><span>Service {d.serviceChargePercent}%</span><span>{money(d.serviceChargeAmount)}</span></div>}
        {d.taxAmount > 0 &&
          <div className="r-line"><span>{taxLabel}{d.taxPercent > 0 ? ` ${d.taxPercent}%` : ''}</span><span>{money(d.taxAmount)}</span></div>}
        <div className="r-line r-grand"><span>Total</span><span>{money(d.total)}</span></div>
        {isInvoice && <div className="r-line r-pay"><span>Paid via</span><span>{d.paymentMethod}</span></div>}
        {isInvoice && showCash && <>
          <div className="r-line r-pay"><span>Cash received</span><span>{money(d.amountTendered)}</span></div>
          <div className="r-line r-pay"><span>Change</span><span>{money(d.changeGiven)}</span></div>
        </>}
      </div>

      {/* Fiscal block. The receipt ALWAYS prints; this only reflects the FBR state. */}
      {isInvoice && d.fiscalNumber && (
        <div className="r-fbr">
          <div style={{ fontSize: 12, fontWeight: 800, letterSpacing: '.04em' }}>INTEGRATED WITH FBR</div>
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
        <div className="r-fbr">
          <div style={{ fontSize: 11, fontWeight: 700, color: '#a00' }}>FBR FILING PENDING</div>
          <div style={{ fontSize: 10, color: '#666', marginTop: 2 }}>
            This sale is valid. It will be filed with FBR shortly.
          </div>
        </div>
      )}

      <div className="r-foot">
        {isInvoice
          ? (d.receiptFooter || 'Thank you, please come again!')
          : 'Bill preview (not yet paid)'}
      </div>

      {/* Branded strip — always printed on every slip. */}
      <div className="r-brand">Powered by Dine In · 0309 8480389</div>
    </div>
  )
}
