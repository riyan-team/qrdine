import { useEffect, useRef } from 'react'
import * as signalr from '@microsoft/signalr'

// Subscribe to live order changes for a restaurant.
// group: 'staff' (all staff screens) or a table code (a single customer's phone).
export function useHub(slug, group, onOrderChanged) {
  const cb = useRef(onOrderChanged)
  cb.current = onOrderChanged

  useEffect(() => {
    if (!slug || !group) return
    const conn = new signalr.HubConnectionBuilder()
      .withUrl('/hub/orders').withAutomaticReconnect().build()

    conn.on('orderChanged', (o) => cb.current && cb.current(o))

    const join = async () => {
      if (group === 'staff') await conn.invoke('JoinStaff', slug)
      else await conn.invoke('JoinTable', slug, group)
    }
    conn.start().then(join).catch(() => {})
    conn.onreconnected(join)

    return () => { conn.stop() }
  }, [slug, group])
}
