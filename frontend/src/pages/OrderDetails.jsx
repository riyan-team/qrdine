import { useEffect, useState, useRef } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { api, money, STATUS_LABEL } from '../api'
import { useHub } from '../useHub'
import { auth } from '../auth'
import { ui, toast, playBell } from '../ui.jsx'
import { friendlyError } from '../errors'
import { CheckIcon, BellIcon, RefreshIcon, TableIcon } from '../icons.jsx'

const LIFECYCLE_STEPS = ['Placed', 'Approved', 'Cooking', 'Ready', 'Served', 'Paid']

export default function OrderDetails() {
  const { slug, code, id } = useParams()
  const navigate = useNavigate()
  const [order, setOrder] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [cancelling, setCancelling] = useState(false)
  const [reordering, setReordering] = useState(false)
  const [showCancel, setShowCancel] = useState(false)
  const [cancelReason, setCancelReason] = useState('')
  const bellPlayed = useRef(false)
  const [now, setNow] = useState(Date.now())

  useEffect(() => {
    loadOrder()
  }, [slug, id])

  const orderRef = useRef(order)
  orderRef.current = order

  // Resolve table code from URL param, loaded order, or remembered session
  const activeTableCode = code || order?.tableCode || auth.getTable(slug)

  // Live updates via SignalR
  useHub(slug, activeTableCode, (o) => {
    if (o.id === Number(id)) {
      setOrder(o)
      if (o.status === 'Ready' && !bellPlayed.current) {
        bellPlayed.current = true
        playBell()
        toast.success(`Order #${o.id} is ready! 🔔`)
      }
    }
  }, null)

  async function loadOrder() {
    try {
      const data = await api.getOrder(slug, Number(id))
      setOrder(data)
      if (data?.tableCode) {
        auth.rememberTable(slug, data.tableCode)
      }
      setError('')
    } catch (e) {
      setError(friendlyError(e))
    } finally {
      setLoading(false)
    }
  }

  // Second-by-second timer update while cooking
  useEffect(() => {
    if (!order || order.status !== 'Cooking') return
    const t = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(t)
  }, [order?.status])

  const handleCancel = async () => {
    if (cancelling) return
    setCancelling(true)
    try {
      const updated = await api.cancelOrder(slug, Number(id), cancelReason.trim() || 'Cancelled by customer')
      setOrder(updated)
      setShowCancel(false)
      setCancelReason('')
      toast.success('Order cancelled successfully.')
    } catch (e) {
      toast.error(friendlyError(e))
    } finally {
      setCancelling(false)
    }
  }

  const handleReorder = async () => {
    if (reordering) return
    setReordering(true)
    try {
      const newOrder = await api.reorder(slug, Number(id))
      toast.success(`New order #${newOrder.id} placed successfully!`)
      navigate(`/r/${slug}/order/${newOrder.id}`)
    } catch (e) {
      toast.error(friendlyError(e))
    } finally {
      setReordering(false)
    }
  }

  const goBackToMenu = () => {
    const tbl = activeTableCode || order?.tableCode || ''
    if (tbl) {
      navigate(`/r/${slug}/t/${tbl}`)
    } else {
      navigate(`/r/${slug}`)
    }
  }

  if (loading) return <div className="loading"><span className="spinner dark" /> Loading order details…</div>
  if (error) return (
    <div className="wrap" style={{ maxWidth: 540, textAlign: 'center', paddingTop: 40 }}>
      <div className="empty">{error}</div>
      <button className="primary" onClick={goBackToMenu}>← Return to Menu</button>
    </div>
  )
  if (!order) return <div className="empty">Order not found.</div>

  const isCancelled = order.status === 'Cancelled'
  const canCancel = ['Placed', 'Approved', 'Cooking'].includes(order.status)
  const canReorder = ['Served', 'Paid', 'Cancelled'].includes(order.status)

  // Countdown timer calculations
  let countdown = null
  let isDue = false
  if (order.status === 'Cooking' && order.acceptedAt) {
    const done = new Date(order.acceptedAt).getTime() + order.etaMinutes * 60000
    const leftSec = Math.round((done - now) / 1000)
    if (leftSec > 0) {
      countdown = `${String(Math.floor(leftSec / 60)).padStart(2, '0')}:${String(leftSec % 60).padStart(2, '0')}`
    } else {
      isDue = true
      countdown = 'Finishing up'
    }
  }

  // Stepper active index
  const currentStepIdx = LIFECYCLE_STEPS.indexOf(order.status)

  return (
    <div className="wrap" style={{ paddingBottom: 100, maxWidth: 640 }}>
      {/* Back Button & Header */}
      <div className="row between" style={{ marginBottom: 12 }}>
        <button className="ghost" onClick={goBackToMenu} style={{ padding: '8px 12px', fontSize: 13.5 }}>
          ← Back to Menu
        </button>
        <div className="menu-table-badge">
          <TableIcon size={14} />
          <span>{order.tableName || `Table ${order.tableCode}`}</span>
        </div>
      </div>

      {/* Main Order Card */}
      <div className="card" style={{ marginBottom: 14 }}>
        <div className="row between">
          <div>
            <div className="big" style={{ fontSize: 24 }}>Order #{order.id}</div>
            <div className="muted" style={{ fontSize: 13 }}>
              {new Date(order.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })} · {new Date(order.createdAt).toLocaleDateString()}
            </div>
          </div>
          <span className={'pill s-' + order.status} style={{ fontSize: 13, padding: '5px 12px' }}>
            {STATUS_LABEL[order.status] || order.status}
          </span>
        </div>

        {/* Status Stepper Progress Bar */}
        {!isCancelled ? (
          <div className="stepper-container" style={{ margin: '16px 0 4px', padding: '12px 10px' }}>
            <div className="stepper-track">
              {LIFECYCLE_STEPS.map((step, idx) => {
                const isCompleted = idx < currentStepIdx
                const isCurrent = idx === currentStepIdx
                return (
                  <div
                    key={step}
                    className={`stepper-step${isCompleted ? ' completed' : ''}${isCurrent ? ' active' : ''}`}
                  >
                    <div className="stepper-circle">
                      {isCompleted ? '✓' : idx + 1}
                    </div>
                    <span className="stepper-label">{step}</span>
                  </div>
                )
              })}
            </div>
          </div>
        ) : (
          <div className="alert-banner" style={{ background: '#fdf0f2', border: '1px solid #f8c8d1', color: 'var(--red)', marginTop: 14 }}>
            <div>
              <b>This order was cancelled.</b>
              {order.customerNote && (
                <div style={{ fontSize: 13, marginTop: 4 }}>Note: {order.customerNote}</div>
              )}
            </div>
          </div>
        )}
      </div>

      {/* Real-time Cooking Timer Card */}
      {order.status === 'Cooking' && (
        <div className="cooking-timer-card">
          <div className="row" style={{ justifyContent: 'center', gap: 8, color: 'var(--amber)' }}>
            <span className="timer-pulse-icon" style={{ fontSize: 20 }}>⏱</span>
            <b style={{ fontSize: 15 }}>Kitchen is preparing your food</b>
          </div>
          <div className="timer-digits">
            {countdown}
          </div>
          <div className="muted" style={{ fontSize: 13 }}>
            {isDue ? 'Due any moment now — putting the finishing touches' : `Promised in about ${order.etaMinutes} minutes`}
          </div>
        </div>
      )}

      {/* Order Ready Notification Card */}
      {order.status === 'Ready' && (
        <div className="card" style={{ marginBottom: 14, textAlign: 'center', borderColor: 'var(--accent2)', background: 'var(--green-tint)' }}>
          <div style={{ fontSize: 36, color: 'var(--accent2)', marginBottom: 4 }}><CheckIcon size={38} /></div>
          <div style={{ fontWeight: 800, fontSize: 20, color: 'var(--accent2)' }}>Order is Ready to Serve! 🔔</div>
          <div className="muted" style={{ fontSize: 13.5, marginTop: 4 }}>
            Our staff is bringing your food to your table right now.
          </div>
        </div>
      )}

      {/* Served & Paid state banner */}
      {order.status === 'Served' && (
        <div className="card" style={{ marginBottom: 14, textAlign: 'center' }}>
          <b style={{ fontSize: 16 }}>Food Served. Enjoy your meal!</b>
          <div className="muted" style={{ fontSize: 13, marginTop: 2 }}>You can ask reception for the bill when you're done.</div>
        </div>
      )}

      {order.status === 'Paid' && (
        <div className="card" style={{ marginBottom: 14, textAlign: 'center', background: '#f6fbf8', borderColor: '#c3e8d2' }}>
          <div className="paid-badge" style={{ margin: '0 auto 6px' }}><CheckIcon size={16} /> Bill Settled</div>
          <div className="muted" style={{ fontSize: 13 }}>Thank you for dining with us!</div>
        </div>
      )}

      {/* Order Items Breakdown */}
      <div className="card" style={{ marginBottom: 14 }}>
        <div className="cat-title" style={{ marginTop: 4, marginBottom: 10 }}>Order Items ({order.items.length})</div>
        {order.items.map((i, n) => (
          <div key={n} className="row between" style={{ padding: '8px 0', borderBottom: n < order.items.length - 1 ? '1px solid var(--line)' : 'none' }}>
            <div style={{ flex: 1, minWidth: 0, paddingRight: 12 }}>
              <div style={{ fontWeight: 700, fontSize: 14.5 }}>
                {i.qty}× {i.name}
                {i.variantName && <span className="muted" style={{ fontWeight: 400 }}> ({i.variantName})</span>}
              </div>
              {i.modifiers?.length > 0 && (
                <div className="muted" style={{ fontSize: 12 }}>· {i.modifiers.join(', ')}</div>
              )}
              {i.note && (
                <div className="muted" style={{ fontSize: 12, fontStyle: 'italic' }}>Note: {i.note}</div>
              )}
            </div>
            <div style={{ fontWeight: 700, fontSize: 14.5 }}>
              {money(i.price * i.qty)}
            </div>
          </div>
        ))}
      </div>

      {/* Order Total */}
      <div className="card" style={{ marginBottom: 14 }}>
        <div className="row between" style={{ fontSize: 14, padding: '3px 0' }}>
          <span className="muted">Subtotal</span>
          <span>{money(order.total)}</span>
        </div>
        <div className="row between" style={{ fontSize: 17, fontWeight: 800, marginTop: 6, paddingTop: 6, borderTop: '1px dashed var(--line)' }}>
          <span>Total</span>
          <span style={{ color: 'var(--accent)' }}>{money(order.total)}</span>
        </div>
      </div>

      {/* Customer Note if any */}
      {order.customerNote && (
        <div className="card" style={{ marginBottom: 14 }}>
          <div className="muted" style={{ fontSize: 12.5, fontWeight: 600, textTransform: 'uppercase' }}>Instructions:</div>
          <div style={{ fontSize: 13.5, marginTop: 2 }}>{order.customerNote}</div>
        </div>
      )}

      {/* Action Buttons: Cancel Order & Reorder */}
      <div className="row" style={{ gap: 10, flexWrap: 'wrap' }}>
        {canCancel && (
          <button
            className="red"
            onClick={() => setShowCancel(true)}
            style={{ flex: 1, padding: '12px', fontSize: 15 }}
          >
            Cancel Order
          </button>
        )}
        {canReorder && (
          <button
            className="green"
            onClick={handleReorder}
            disabled={reordering}
            style={{ flex: 1, padding: '12px', fontSize: 15, display: 'inline-flex', alignItems: 'center', justifyContent: 'center', gap: 6 }}
          >
            <RefreshIcon size={16} />
            <span>{reordering ? 'Placing Order…' : 'Order Again'}</span>
          </button>
        )}
      </div>

      {/* Cancel Confirmation Dialog */}
      {showCancel && (
        <div className="overlay" onClick={(e) => { if (e.target.className === 'overlay') setShowCancel(false) }}>
          <div className="modal">
            <div className="big" style={{ fontSize: 20, marginBottom: 6 }}>Cancel Order #{order.id}?</div>
            <div className="muted" style={{ fontSize: 13.5, marginBottom: 12 }}>
              Are you sure you want to cancel this order? The kitchen will be notified immediately.
            </div>
            <textarea
              placeholder="Reason for cancellation (optional)…"
              value={cancelReason}
              onChange={(e) => setCancelReason(e.target.value)}
              style={{ width: '100%', minHeight: 70, marginBottom: 12, fontSize: 13.5 }}
            />
            <div className="row" style={{ gap: 8, justifyContent: 'flex-end' }}>
              <button onClick={() => { setShowCancel(false); setCancelReason('') }}>
                No, Keep It
              </button>
              <button className="red" disabled={cancelling} onClick={handleCancel}>
                {cancelling ? 'Cancelling…' : 'Yes, Cancel Order'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
