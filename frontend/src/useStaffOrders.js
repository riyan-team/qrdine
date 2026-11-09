import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from './api'
import { auth } from './auth'
import { useHub } from './useHub'
import { playBell, toast } from './ui.jsx'

// Shared by Reception / Kitchen / Waiter: load open orders + keep them live. Live updates can be
// missed when the internet drops, so the list is also re-read every 30 seconds and whenever the
// screen comes back into view. A busy floor must never show yesterday's picture.
// Until the first read comes back the list is this exact array, so a screen can tell "not loaded yet"
// from "nothing open" and won't announce everything already running as if it had just arrived.
export const NOT_LOADED = []

// Reads until it works, so one dropped request can't leave a screen looking empty.
// A refusal such as "no permission" won't change by asking again, so that stops it.
export function loadUntilOk(read, onData) {
  let stopped = false
  let timer
  const go = () => read().then((d) => { if (!stopped) onData(d) }).catch((e) => {
    if (!stopped && !(e?.status >= 400 && e.status < 500)) timer = setTimeout(go, 4000)
  })
  go()
  return () => { stopped = true; clearTimeout(timer) }
}

export function useStaffOrders(onServiceRequest) {
  const [orders, setOrders] = useState(NOT_LOADED)

  useEffect(() => {
    const refresh = () => api.staffOrders().then(setOrders).catch(() => {})
    const stop = loadUntilOk(() => api.staffOrders(), setOrders)
    const timer = setInterval(refresh, 30000)
    const onVisible = () => { if (document.visibilityState === 'visible') refresh() }
    const onNet = (e) => { if (!e.detail.down) refresh() }
    document.addEventListener('visibilitychange', onVisible)
    window.addEventListener('dinein:net', onNet)
    return () => {
      stop()
      clearInterval(timer)
      document.removeEventListener('visibilitychange', onVisible)
      window.removeEventListener('dinein:net', onNet)
    }
  }, [])

  useHub(auth.staffSlug(), 'staff', (o) => {
    setOrders((prev) => {
      const others = prev.filter((x) => x.id !== o.id)
      if (o.status === 'Paid' || o.status === 'Cancelled') return others
      return [...others, o].sort((a, b) => a.id - b.id)
    })
  }, null, onServiceRequest)

  return orders
}

// Guests calling for a waiter or the bill. Pass onCall to useStaffOrders so they share one connection.
export function useCalls() {
  const [calls, setCalls] = useState([])
  const known = useRef(new Set())

  useEffect(() => {
    const refresh = () => api.serviceRequests().then((rows) => {
      known.current = new Set(rows.map((r) => r.id))
      setCalls(rows)
    }).catch(() => {})
    refresh()
    const timer = setInterval(refresh, 30000)
    return () => clearInterval(timer)
  }, [])

  const onCall = useCallback((r) => {
    if (!r.done && !known.current.has(r.id)) {
      known.current.add(r.id)
      playBell()
      toast.info(r.kind === 'Bill' ? `${r.tableName} is asking for the bill` : `${r.tableName} is calling a waiter`)
    }
    setCalls((prev) => {
      const others = prev.filter((x) => x.id !== r.id)
      return r.done ? others : [...others, r]
    })
  }, [])

  const done = useCallback((id) => {
    setCalls((prev) => prev.filter((x) => x.id !== id))
    return api.serviceDone(id).catch(() => {})
  }, [])

  return { calls, onCall, done }
}
