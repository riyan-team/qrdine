import { useEffect, useRef } from 'react'
import './design/map.css'

// One small map for every delivery screen: the guest pinning their door, the guest following the rider,
// the rider's route and the delivery board. OpenStreetMap tiles, no key. Leaflet only loads when a map is shown.
//   points: [{ lat, lng, kind: 'shop' | 'home' | 'rider', label }]
//   pick:   { lat, lng, onMove, kind?, label? } a draggable pin (tap the map to move it too); kind defaults to 'home'
//   area:   { lat, lng, km } the delivery area, drawn as a circle
//   center: where to look when there is nothing to show yet ([lat, lng], zoomed out)
let loading = null
const loadLeaflet = () => (loading ||= Promise.all([import('leaflet'), import('leaflet/dist/leaflet.css')]).then(([m]) => m.default || m))

const GLYPH = {
  shop: '<svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor" stroke-width="2.1" stroke-linecap="round" stroke-linejoin="round"><path d="M4 10h16l-1.2-5H5.2Z"/><path d="M5 10v9h14v-9"/><path d="M10 19v-5h4v5"/></svg>',
  home: '<svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor" stroke-width="2.1" stroke-linecap="round" stroke-linejoin="round"><path d="M3 11 12 4l9 7"/><path d="M5 10v10h14V10"/></svg>',
  rider: '<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2.1" stroke-linecap="round" stroke-linejoin="round"><circle cx="5.5" cy="17" r="3"/><circle cx="18.5" cy="17" r="3"/><path d="M5.5 17 9 9h5l4.5 8"/><path d="M12 9 10 5H7"/></svg>',
}

export default function MapView({ points = [], pick = null, area = null, center = null, height = 220, className = '', zoom = 15, route = true }) {
  const el = useRef(null)
  const st = useRef({ L: null, map: null, layer: null, fitted: '' })
  const pickRef = useRef(pick)
  pickRef.current = pick

  const draw = () => {
    const { L, map } = st.current
    if (!L || !map) return
    st.current.layer?.remove()
    const layer = L.layerGroup().addTo(map)
    st.current.layer = layer
    const all = [...points.filter((p) => p && Number.isFinite(p.lat) && Number.isFinite(p.lng))]
    const p = pickRef.current
    if (p && Number.isFinite(p.lat)) all.push({ lat: p.lat, lng: p.lng, kind: p.kind || 'home', label: p.label ?? 'Your door', drag: true })
    if (area && Number.isFinite(area.lat) && area.km > 0)
      L.circle([area.lat, area.lng], { radius: area.km * 1000, color: '#17794f', weight: 2, opacity: 0.6, fillColor: '#17794f', fillOpacity: 0.07, interactive: false }).addTo(layer)
    const icon = (kind) => L.divIcon({ className: '', html: `<div class="mv-pin mv-${kind}">${GLYPH[kind] || ''}</div>`, iconSize: [36, 44], iconAnchor: [18, 42] })
    const riderIcon = () => L.divIcon({ className: '', html: `<div class="mv-dot"><i></i>${GLYPH.rider}</div>`, iconSize: [40, 40], iconAnchor: [20, 20] })
    cancelAnimationFrame(st.current.raf)
    const shown = st.current.riders || (st.current.riders = {})
    const glides = []
    for (const q of all) {
      const rider = q.kind === 'rider'
      const from = rider ? shown[q.label || ''] : null
      const m = L.marker(from ? [from.lat, from.lng] : [q.lat, q.lng], { icon: rider ? riderIcon() : icon(q.kind), draggable: !!q.drag, keyboard: false, zIndexOffset: rider ? 500 : 0 })
      if (q.label) m.bindTooltip(q.label, { direction: 'top', offset: [0, -40], className: 'mv-tip' })
      if (q.drag) m.on('dragend', () => { const ll = m.getLatLng(); pickRef.current?.onMove?.({ lat: ll.lat, lng: ll.lng }) })
      m.addTo(layer)
      if (rider) {
        if (!from) shown[q.label || ''] = { lat: q.lat, lng: q.lng }
        else glides.push({ m, key: q.label || '', a: from, b: q })
      }
    }
    // the way the food travels: shop → rider → door
    const order = ['shop', 'rider', 'home']
    const line = order.map((k) => all.find((q) => q.kind === k)).filter(Boolean)
    const path = route && line.length > 1 ? L.polyline(line.map((q) => [q.lat, q.lng]), { color: '#a81d48', weight: 4, opacity: 0.75, dashArray: '2 9', lineCap: 'round' }).addTo(layer) : null
    // a rider moves smoothly from where the map last showed them, instead of jumping
    const still = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
    if (glides.length) {
      const t0 = performance.now(), dur = still ? 0 : 3200
      const step = (t) => {
        const k = dur ? Math.min(1, (t - t0) / dur) : 1
        const e = k < 0.5 ? 2 * k * k : 1 - (-2 * k + 2) ** 2 / 2
        for (const g of glides) {
          const at = { lat: g.a.lat + (g.b.lat - g.a.lat) * e, lng: g.a.lng + (g.b.lng - g.a.lng) * e }
          g.m.setLatLng([at.lat, at.lng]); shown[g.key] = at
        }
        if (path) path.setLatLngs(line.map((q) => (q.kind === 'rider' && glides[0] ? [shown[glides[0].key].lat, shown[glides[0].key].lng] : [q.lat, q.lng])))
        if (k < 1) st.current.raf = requestAnimationFrame(step)
      }
      st.current.raf = requestAnimationFrame(step)
    }
    // reframe only when what's on the map changes (not every time the rider moves a little)
    const key = all.map((q) => q.kind).join(',') + (p ? `|${p.lat?.toFixed(2)},${p.lng?.toFixed(2)}` : '') + (area ? `|${area.km}` : '')
    if (!all.length && center && !st.current.fitted) { st.current.fitted = 'center'; map.setView(center, 5, { animate: false }) }
    if (key !== st.current.fitted && all.length) {
      st.current.fitted = key
      if (all.length === 1 && area?.km > 0) map.fitBounds(L.latLng(area.lat, area.lng).toBounds(area.km * 2000), { padding: [16, 16], animate: false })
      else if (all.length === 1) map.setView([all[0].lat, all[0].lng], zoom, { animate: false })
      else map.fitBounds(L.latLngBounds(all.map((q) => [q.lat, q.lng])), { padding: [42, 42], maxZoom: 16, animate: false })
    }
  }

  useEffect(() => {
    let dead = false
    loadLeaflet().then((L) => {
      if (dead || st.current.map || !el.current) return
      const map = L.map(el.current, { zoomControl: false, attributionControl: true, scrollWheelZoom: false, tap: true })
      L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', { maxZoom: 19, attribution: '© OpenStreetMap' }).addTo(map)
      map.attributionControl.setPrefix(false)
      map.on('click', (e) => pickRef.current?.onMove?.({ lat: e.latlng.lat, lng: e.latlng.lng }))
      st.current = { ...st.current, L, map }
      draw()
      setTimeout(() => map.invalidateSize(), 120)
    })
    return () => { dead = true; cancelAnimationFrame(st.current.raf); st.current.map?.remove(); st.current = { L: null, map: null, layer: null, fitted: '' } }
  }, [])

  useEffect(() => { draw() }, [JSON.stringify(points), pick?.lat, pick?.lng, area?.lat, area?.lng, area?.km])

  return <div ref={el} className={'mv ' + className} style={{ height }} />
}

/** Google Maps turn-by-turn to a pin (or to the typed address when there is no pin). */
export const directionsUrl = (to, address = '') =>
  to ? `https://www.google.com/maps/dir/?api=1&destination=${to.lat},${to.lng}&travelmode=driving`
    : `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(address)}`

/** The phone's own location, as a promise. */
export const myLocation = () => new Promise((resolve, reject) => {
  if (!navigator.geolocation) return reject(new Error('This phone cannot share its location.'))
  navigator.geolocation.getCurrentPosition((p) => resolve({ lat: p.coords.latitude, lng: p.coords.longitude }),
    () => reject(new Error('Location is turned off. Allow it, or move the pin on the map.')), { enableHighAccuracy: true, timeout: 12000, maximumAge: 30000 })
})

/** Straight-line km between two points. */
export const kmBetween = (a, b) => {
  if (!a || !b) return null
  const r = (d) => (d * Math.PI) / 180
  const h = Math.sin(r(b.lat - a.lat) / 2) ** 2 + Math.cos(r(a.lat)) * Math.cos(r(b.lat)) * Math.sin(r(b.lng - a.lng) / 2) ** 2
  return 6371 * 2 * Math.atan2(Math.sqrt(h), Math.sqrt(1 - h))
}
