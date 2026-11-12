import { useEffect, useRef, useState } from 'react'
import { useParams, useSearchParams } from 'react-router-dom'
import { api, money } from '../api'
import { ticketLabel } from '../auth'
import { friendlyError } from '../errors'
import MapView, { directionsUrl } from '../MapView.jsx'
import { BikeIcon, CallIcon, CashIcon, CheckIcon, ClockIcon, NavIcon, PinIcon } from '../icons.jsx'
import '../design/delivery.css'

// The rider's phone. No account: reception sends a private link (…/ride/D251112-3?k=…) on WhatsApp.
// It shows where to go, who to call and what to collect; "Picked up" starts sharing the rider's
// position with the customer, "Delivered" stops it.
const clock = (d) => new Date(d).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })
const SEND_EVERY = 10000

export default function Ride() {
  const { slug, code } = useParams()
  const [params] = useSearchParams()
  const k = params.get('k') || ''
  const [r, setR] = useState(null)
  const [err, setErr] = useState('')
  const [busy, setBusy] = useState(false)
  const [sharing, setSharing] = useState('')      // '', 'on', 'off'
  const [now, setNow] = useState(Date.now())
  const lastSent = useRef(0)
  const [me, setMe] = useState(null)

  const load = () => api.ride(slug, code, k).then((x) => { setR(x); setErr('') }).catch((e) => setErr(friendlyError(e)))
  useEffect(() => {
    load()
    const t = setInterval(load, 15000)
    const c = setInterval(() => setNow(Date.now()), 15000)
    return () => { clearInterval(t); clearInterval(c) }
  }, [slug, code, k])

  const onTheWay = r?.stage === 'OnTheWay' && !r?.deliveredAt
  // While the food is on the way, the customer sees this phone's position (every 10 seconds at most).
  useEffect(() => {
    if (!onTheWay || !navigator.geolocation) return
    const id = navigator.geolocation.watchPosition((p) => {
      setSharing('on')
      const at = { lat: p.coords.latitude, lng: p.coords.longitude }
      setMe(at)
      if (Date.now() - lastSent.current < SEND_EVERY) return
      lastSent.current = Date.now()
      api.rideLocation(slug, code, k, at.lat, at.lng).catch(() => {})
    }, () => setSharing('off'), { enableHighAccuracy: true, maximumAge: 5000, timeout: 20000 })
    return () => navigator.geolocation.clearWatch(id)
  }, [onTheWay])

  const act = async (fn) => {
    setBusy(true)
    try { await fn(); await load() } catch (e) { setErr(friendlyError(e)) }
    setBusy(false)
  }

  if (!r) return (
    <div className="rd">
      <div className="rd-card rd-msg">{err ? <><b>Can't open this delivery</b><p>{err}</p></> : <span className="spinner" />}</div>
    </div>
  )

  const done = !!r.deliveredAt || r.stage === 'Delivered' || r.stage === 'Closed'
  const left = r.promisedAt ? Math.round((new Date(r.promisedAt) - now) / 60000) : null
  const points = [r.shop && { ...r.shop, kind: 'shop', label: r.restaurantName }, r.home && { ...r.home, kind: 'home', label: r.customerName || 'Customer' },
    onTheWay && me && { ...me, kind: 'rider', label: 'You' }].filter(Boolean)

  return (
    <div className="rd">
      <header className="rd-top">
        <span className="rd-ico"><BikeIcon size={20} /></span>
        <div>
          <small>{r.restaurantName}</small>
          <b>{ticketLabel(r.tableCode)}</b>
        </div>
        <span className={'rd-stage s-' + r.stage}>{done ? 'Delivered' : onTheWay ? 'On the way' : r.foodReady ? 'Food ready' : 'In kitchen'}</span>
      </header>

      {points.length > 0 && <MapView className="rd-map" height={250} points={points} />}

      <section className="rd-card">
        <div className="rd-who">
          <div>
            <b>{r.customerName || 'Customer'}</b>
            <p><PinIcon size={14} /> {r.address || 'No address given'}</p>
            {r.distanceKm != null && <small>{r.distanceKm.toFixed(1)} km from the restaurant</small>}
          </div>
        </div>
        <div className="rd-go">
          {r.customerPhone && <a className="rd-btn" href={`tel:${r.customerPhone}`}><CallIcon size={17} /> Call</a>}
          <a className="rd-btn dark" href={directionsUrl(r.home, r.address)} target="_blank" rel="noreferrer"><NavIcon size={17} /> Navigate</a>
        </div>
      </section>

      <section className="rd-card rd-money">
        <div><CashIcon size={18} /> <span>Collect from the customer</span></div>
        <b>{r.collect > 0 ? money(r.collect) : 'Nothing, already paid'}</b>
      </section>

      {r.promisedAt && !done && (
        <section className={'rd-card rd-due' + (left < 0 ? ' late' : '')}>
          <ClockIcon size={17} />
          <span>Promised at the door by <b>{clock(r.promisedAt)}</b></span>
          <em>{left >= 0 ? `${left} min left` : `${-left} min late`}</em>
        </section>
      )}

      <section className="rd-card">
        <h3>In the bag</h3>
        <ul className="rd-items">{r.items.map((x) => <li key={x}>{x}</li>)}</ul>
        {r.note && <p className="rd-note">Note: {r.note}</p>}
      </section>

      {err && <p className="rd-err" role="alert">{err}</p>}

      <div className="rd-foot">
        {done ? (
          <div className="rd-done"><CheckIcon size={20} /> Delivered at {clock(r.deliveredAt || Date.now())}. Thank you!</div>
        ) : onTheWay ? (
          <>
            <small className={'rd-share ' + sharing}>{sharing === 'off' ? 'Location is off. Turn it on so the customer can follow you.' : 'The customer can see you on the map.'}</small>
            <button className="rd-cta green" disabled={busy} onClick={() => act(() => api.rideDelivered(slug, code, k))}><CheckIcon size={19} /> Delivered</button>
          </>
        ) : (
          <button className="rd-cta" disabled={busy || !r.foodReady} onClick={() => act(() => api.ridePickup(slug, code, k))}>
            {r.foodReady ? <><BikeIcon size={19} /> Picked up, start delivery</> : 'Waiting for the kitchen'}
          </button>
        )}
      </div>
    </div>
  )
}
