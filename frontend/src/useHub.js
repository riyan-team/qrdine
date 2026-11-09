import { useEffect, useRef } from 'react'
import * as signalr from '@microsoft/signalr'

// Subscribe to live order changes for a restaurant.
// group: 'staff' (all staff screens) or a table code (a single customer's phone).
export function useHub(slug, group, onOrderChanged, onTimerTick, onServiceRequest) {
  const cb = useRef(onOrderChanged)
  const tickCb = useRef(onTimerTick)
  cb.current = onOrderChanged
  tickCb.current = onTimerTick
  const reqCb = useRef(onServiceRequest)
  reqCb.current = onServiceRequest

  useEffect(() => {
    if (!slug || !group) return
    const conn = new signalr.HubConnectionBuilder()
      .withUrl('/hub/orders').withAutomaticReconnect().build()

    conn.on('orderChanged', (o) => cb.current && cb.current(o))
    conn.on('timerTick', (serverTime) => tickCb.current && tickCb.current(serverTime))
    conn.on('serviceRequest', (r) => reqCb.current && reqCb.current(r))

    const join = async () => {
      if (group === 'staff') await conn.invoke('JoinStaff', slug)
      else await conn.invoke('JoinTable', slug, group)
    }
    conn.start().then(join).catch(() => {})
    conn.onreconnected(join)

    return () => { conn.stop() }
  }, [slug, group])
}
