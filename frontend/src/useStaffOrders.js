import { useEffect, useState } from 'react'
import { api } from './api'
import { auth } from './auth'
import { useHub } from './useHub'

// Shared by Reception / Kitchen / Waiter: load open orders + keep them live.
export function useStaffOrders() {
  const [orders, setOrders] = useState([])

  useEffect(() => { api.staffOrders().then(setOrders).catch(() => {}) }, [])

  useHub(auth.staffSlug(), 'staff', (o) => {
    setOrders((prev) => {
      const others = prev.filter((x) => x.id !== o.id)
      if (o.status === 'Paid' || o.status === 'Cancelled') return others
      return [...others, o].sort((a, b) => a.id - b.id)
    })
  }, null)

  return orders
}
