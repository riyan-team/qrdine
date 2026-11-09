import { useEffect, useRef } from 'react'
import * as signalr from '@microsoft/signalr'
import { auth } from './auth'

// Subscribe to live order changes for a restaurant.
// group: 'staff' (all staff screens) or a table code (a single customer's phone).
// The server only lets a connection in with a sign-in: staff use theirs, a guest uses the table pass
// (renewed when it runs out, so reconnecting after hours still works).
export function useHub(slug, group, onOrderChanged, onTimerTick, onServiceRequest) {
  const cb = useRef(onOrderChanged)
  const tickCb = useRef(onTimerTick)
  cb.current = onOrderChanged
  tickCb.current = onTimerTick
  const reqCb = useRef(onServiceRequest)
  reqCb.current = onServiceRequest

  useEffect(() => {
    if (!slug || !group) return
    const pass = async () => {
      if (group === 'staff') return auth.staffToken || ''
      if (auth.staffToken && auth.staffSlug() === slug) return auth.staffToken
      return auth.guestToken(slug, group)
    }
    const conn = new signalr.HubConnectionBuilder()
      .withUrl('/hub/orders', { accessTokenFactory: pass }).withAutomaticReconnect().build()

    conn.on('orderChanged', (o) => cb.current && cb.current(o))
    conn.on('timerTick', (serverTime) => tickCb.current && tickCb.current(serverTime))
    conn.on('serviceRequest', (r) => reqCb.current && reqCb.current(r))

    const join = async () => {
      if (group === 'staff') await conn.invoke('JoinStaff', slug)
      else await conn.invoke('JoinTable', slug, group)
    }
    conn.start().then(join).catch(() => {})
    conn.onreconnected(() => join().catch(() => {}))
    // The server closes a connection when its pass runs out; start again with a fresh one.
    let stopped = false
    conn.onclose(() => {
      if (stopped) return
      setTimeout(() => { if (!stopped) conn.start().then(join).catch(() => {}) }, 3000)
    })

    return () => { stopped = true; conn.stop() }
  }, [slug, group])
}
