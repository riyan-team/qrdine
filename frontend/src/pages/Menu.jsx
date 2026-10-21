import { useEffect, useState, useMemo, useRef } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { api, money, STATUS_LABEL } from '../api'
import { useHub } from '../useHub'
import { auth } from '../auth'
import { ui, toast, playBell } from '../ui.jsx'
import { useCart, plusOrCustomize, cartToItems, CartLines, Customizer } from '../ordering.jsx'
import { BellIcon, SearchIcon, TableIcon, PlusIcon } from '../icons.jsx'
import DineInLogo from '../DineInLogo.jsx'
import { useLoyalty, RewardsStrip, RewardsHub, GoogleReviewCard, RateUs, burst } from '../loyalty.jsx'

export default function Menu() {
  const { slug, code } = useParams()
  const navigate = useNavigate()
  const [data, setData] = useState(null)
  const cart = useCart()
  const [note, setNote] = useState('')
  const [orders, setOrders] = useState([])
  const [placing, setPlacing] = useState(false)
  const [customizing, setCustomizing] = useState(null)
  const [query, setQuery] = useState('')
  const [activeFilter, setActiveFilter] = useState('all') // 'all' | 'deals' | 'meals' | categoryId
  const notifiedBellRef = useRef(new Set())
  // Loyalty & reviews (additive: hidden until the restaurant switches rewards on)
  const loy = useLoyalty(slug, code)
  const [hubOpen, setHubOpen] = useState(false)
  const linkedRef = useRef(false)

  useEffect(() => {
    // Remember table code for guest session persistence
    if (slug && code) {
      auth.rememberTable(slug, code)
    }
    // Load menu and table orders
    api.getMenu(slug).then(setData).catch(() => {})
    api.tableOrders(slug, code).then(setOrders).catch(() => {})

    // Load previously notified orders for this session to prevent repeated ring on reload
    try {
      const stored = sessionStorage.getItem(`qrdine.bell.${slug}`)
      if (stored) {
        notifiedBellRef.current = new Set(JSON.parse(stored))
      }
    } catch {}
  }, [slug, code])

  // Real-time updates via SignalR
  useHub(slug, code, (o) => {
    setOrders((prev) => {
      const others = prev.filter((x) => x.id !== o.id)
      if (o.status === 'Paid' || o.status === 'Cancelled') return others
      return [...others, o].sort((a, b) => a.id - b.id)
    })

    if (o.status === 'Ready' && !notifiedBellRef.current.has(o.id)) {
      notifiedBellRef.current.add(o.id)
      try {
        sessionStorage.setItem(`qrdine.bell.${slug}`, JSON.stringify([...notifiedBellRef.current]))
      } catch {}
      playBell()
      toast.success(`Order #${o.id} is ready to serve! 🔔`)
    }

    // The bill was settled: a member sees the points it earned.
    if (o.status === 'Paid' && loy.me) {
      const before = loy.me.points
      setTimeout(async () => {
        const m = await loy.refresh()
        if (m && m.points > before) { burst(); toast.success(`+${(m.points - before).toLocaleString('en-PK')} points earned! ⭐`) }
      }, 1500)
    }
  }, null)

  // A returning member with a running order: make sure this seating earns for them.
  useEffect(() => {
    if (!linkedRef.current && loy.me && orders.length > 0) { linkedRef.current = true; loy.link() }
  }, [loy.me, orders.length])

  const onPlus = (it) => plusOrCustomize(it, cart.addToCart, setCustomizing)

  const place = async () => {
    if (cart.list.length === 0) return
    setPlacing(true)
    try {
      const newOrder = await api.placeOrder(slug, { tableCode: code, items: cartToItems(cart.list), note: note || null })
      cart.clear()
      setNote('')
      toast.success(`Order #${newOrder.id} placed! Kitchen will prepare it shortly.`)
      loy.link()   // the seating exists now: tie its bill to the member (no-op when not a member)
    } catch (e) {
      ui.alert('Could not place order: ' + e.message)
    }
    setPlacing(false)
  }

  // All menu items flattened for search and deals
  const allItems = useMemo(() => {
    if (!data?.menu) return []
    return data.menu.flatMap((cat) => cat.items.map((it) => ({ ...it, categoryName: cat.name, categoryId: cat.id })))
  }, [data?.menu])

  // Active deals
  const deals = useMemo(() => {
    return allItems.filter((it) => it.available && it.dealActive && it.dealPrice != null)
  }, [allItems])

  // Filter menu items based on query & activeFilter
  const filteredCategories = useMemo(() => {
    if (!data?.menu) return []
    const q = query.trim().toLowerCase()

    return data.menu.map((cat) => {
      const items = cat.items.filter((it) => {
        // Search filter
        const matchesQuery = !q ||
          it.name.toLowerCase().includes(q) ||
          (it.description || '').toLowerCase().includes(q) ||
          (it.nameUrdu || '').toLowerCase().includes(q) ||
          (cat.name || '').toLowerCase().includes(q) ||
          (it.dealDescription || '').toLowerCase().includes(q)

        if (!matchesQuery) return false

        // Category/Type filter
        if (activeFilter === 'all') return true
        if (activeFilter === 'deals') return it.dealActive && it.dealPrice != null
        if (activeFilter === 'meals') {
          const isMealCat = /meal|main|platter|karahi|biryani|burger|pizza|combo|bbq/i.test(cat.name)
          const isMealItem = /meal|combo|platter|deal/i.test(it.name)
          return isMealCat || isMealItem
        }
        return cat.id === activeFilter
      })
      return { ...cat, items }
    }).filter((cat) => cat.items.length > 0)
  }, [data?.menu, query, activeFilter])

  if (!data) return <div className="loading"><span className="spinner dark" /> Loading menu…</div>

  const totalResults = filteredCategories.reduce((acc, c) => acc + c.items.length, 0)
  const isDealsActive = activeFilter === 'deals'

  return (
    <div className="wrap" style={{ paddingBottom: 150, maxWidth: 680, width: '100%', boxSizing: 'border-box' }}>
      {/* Modern Header Card */}
      <div className="menu-header-card">
        <div className="menu-brand-row">
          <div className="menu-brand-left">
            <DineInLogo className="menu-brand-logo" />
            <div style={{ minWidth: 0 }}>
              <div className="menu-restaurant-name" title={data.restaurant.name}>{data.restaurant.name}</div>
              <div className="muted" style={{ fontSize: 12.5 }}>Contactless Digital Menu</div>
            </div>
          </div>
          <div className="menu-table-badge">
            <TableIcon size={14} />
            <span>Table {code.replace(/^[Tt]/, '')}</span>
          </div>
        </div>
      </div>

      {/* Loyalty: join, or points and the next reward (opens Rewards & Reviews) */}
      <RewardsStrip loy={loy} onOpen={() => setHubOpen(true)} />

      {/* Fast Menu Search with Clear Button */}
      <div className="menu-search-box">
        <span className="search-icon"><SearchIcon size={18} /></span>
        <input
          placeholder="Search food, deals, meals, drinks…"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
        {query && (
          <button className="clear-btn" onClick={() => setQuery('')} aria-label="Clear search">
            ✕
          </button>
        )}
      </div>

      {/* Filter & Category Pills Bar */}
      <div className="menu-filter-bar">
        <div
          className={`filter-chip${activeFilter === 'all' ? ' active' : ''}`}
          onClick={() => setActiveFilter('all')}
        >
          All
        </div>
        {deals.length > 0 && (
          <div
            className={`filter-chip${activeFilter === 'deals' ? ' active' : ''}`}
            onClick={() => setActiveFilter('deals')}
          >
            🔥 Deals ({deals.length})
          </div>
        )}
        <div
          className={`filter-chip${activeFilter === 'meals' ? ' active' : ''}`}
          onClick={() => setActiveFilter('meals')}
        >
          🍽️ Meals
        </div>
        {data.menu.map((c) => (
          <div
            key={c.id}
            className={`filter-chip${activeFilter === c.id ? ' active' : ''}`}
            onClick={() => setActiveFilter(c.id)}
          >
            {c.name}
          </div>
        ))}
      </div>

      {/* Live Order Tracker (if customer placed orders) */}
      {orders.length > 0 && (
        <LiveTracker orders={orders} slug={slug} code={code} navigate={navigate} />
      )}

      {/* Highlighted Deals & Offers Section (when deals exist and not filtering out) */}
      {deals.length > 0 && (activeFilter === 'all' || isDealsActive) && !query && (
        <div className="deals-banner">
          <div className="deals-title-row">
            <div className="deals-title">
              <span>🔥 Special Deals & Offers</span>
            </div>
            <span className="pill s-Cancelled" style={{ fontSize: 11 }}>Limited Time</span>
          </div>
          <div className="deals-grid">
            {deals.slice(0, 4).map((it) => {
              const pct = it.price > it.dealPrice ? Math.round((1 - it.dealPrice / it.price) * 100) : null
              return (
                <div key={it.id} className="deal-card">
                  <div className="food-thumb" style={{ width: 68, height: 68 }}>
                    {it.imageUrl ? (
                      <img src={it.imageUrl} alt={it.name} />
                    ) : (
                      <span>{it.emoji || '🍽️'}</span>
                    )}
                  </div>
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div className="food-name" style={{ fontSize: 14 }}>{it.name}</div>
                    {it.dealDescription && (
                      <div className="muted" style={{ fontSize: 11.5, margin: '2px 0' }}>{it.dealDescription}</div>
                    )}
                    <div className="row between" style={{ marginTop: 4 }}>
                      <div>
                        <span className="food-old-price">{money(it.price)}</span>
                        <b className="food-deal-price" style={{ fontSize: 14 }}>{money(it.dealPrice)}</b>
                        {pct && <span className="food-badge-discount" style={{ marginLeft: 4 }}>{pct}% OFF</span>}
                      </div>
                      <button className="food-btn-add" onClick={() => onPlus(it)}>
                        <PlusIcon size={12} /> Add
                      </button>
                    </div>
                  </div>
                </div>
              )
            })}
          </div>
        </div>
      )}

      {/* Search Feedback / Empty state */}
      {query && (
        <div className="muted" style={{ fontSize: 13, marginBottom: 12, paddingLeft: 4 }}>
          Found {totalResults} result{totalResults !== 1 ? 's' : ''} for "{query}"
        </div>
      )}

      {/* Menu Categories & Food Items */}
      {filteredCategories.length === 0 ? (
        <div className="empty">
          <div style={{ fontSize: 32, marginBottom: 8 }}>🔍</div>
          <b>No items match your search.</b>
          <div className="muted" style={{ fontSize: 13, marginTop: 4 }}>
            Try a different search term or select "All" to browse the full menu.
          </div>
          <button
            className="ghost"
            style={{ marginTop: 12 }}
            onClick={() => { setQuery(''); setActiveFilter('all') }}
          >
            Reset filters
          </button>
        </div>
      ) : (
        filteredCategories.map((cat) => (
          <div key={cat.id} id={`cat-${cat.id}`} style={{ marginBottom: 20 }}>
            <div className="cat-title" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginTop: 14 }}>
              <span>{cat.name}</span>
              <span className="muted" style={{ fontSize: 12, fontWeight: 500 }}>{cat.items.length} items</span>
            </div>
            <div>
              {cat.items.map((it) => (
                <FoodItemCard key={it.id} it={it} onPlus={onPlus} />
              ))}
            </div>
          </div>
        ))
      )}

      {/* Guest Experience Feedback Card (+ the loyalty bonus for members) and Google reviews */}
      <RateUs slug={slug} code={code} loy={loy} />
      <GoogleReviewCard info={loy.info} />

      <RewardsHub loy={loy} slug={slug} code={code} open={hubOpen} onClose={() => setHubOpen(false)} />

      {/* Sticky Bottom Cart Sheet */}
      {cart.list.length > 0 && (
        <div className="cart">
          <div className="wrap" style={{ padding: 0 }}>
            <div className="row between" style={{ marginBottom: 6 }}>
              <b>Your Cart</b>
              <span className="muted" style={{ fontSize: 13 }}>Table {code.replace(/^[Tt]/, '')}</span>
            </div>
            <CartLines list={cart.list} changeQty={cart.changeQty} />
            <input
              placeholder="Special instructions for kitchen (e.g. no onion, extra spicy)"
              value={note}
              onChange={(e) => setNote(e.target.value)}
              style={{ width: '100%', marginBottom: 10, fontSize: 13.5 }}
            />
            <div className="row between">
              <div>
                <div className="muted" style={{ fontSize: 12.5 }}>{cart.count} item{cart.count !== 1 ? 's' : ''}</div>
                <div className="big" style={{ fontSize: 22, color: 'var(--accent)' }}>{money(cart.total)}</div>
              </div>
              <button
                className="green"
                disabled={placing}
                onClick={place}
                style={{ fontSize: 16, padding: '12px 20px', borderRadius: 14 }}
              >
                {placing ? 'Sending order…' : 'Place Order →'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Customizer Modal for variants and modifiers */}
      {customizing && (
        <Customizer
          item={customizing}
          onClose={() => setCustomizing(null)}
          onAdd={(line) => {
            cart.addToCart(line)
            setCustomizing(null)
          }}
        />
      )}
    </div>
  )
}

// ---- Modern Food Item Card Component ----
function FoodItemCard({ it, onPlus }) {
  const hasVariants = (it.variants?.length || 0) > 0
  const isCustomizable = hasVariants || (it.modifierGroups?.length || 0) > 0
  const minPrice = hasVariants ? Math.min(...it.variants.map((v) => v.price)) : it.price
  const isDeal = it.dealActive && it.dealPrice != null && !hasVariants
  const pct = isDeal && it.price > it.dealPrice ? Math.round((1 - it.dealPrice / it.price) * 100) : null

  return (
    <div className="food-card">
      <div className="food-thumb">
        {it.imageUrl ? (
          <img src={it.imageUrl} alt={it.name} loading="lazy" />
        ) : (
          <span>{it.emoji || '🍽️'}</span>
        )}
      </div>
      <div className="food-details">
        <div>
          <div className="food-name-row">
            <div className="food-name">
              {it.name}
              {it.nameUrdu && <span className="food-urdu"> ({it.nameUrdu})</span>}
            </div>
          </div>
          {it.description && <div className="food-desc">{it.description}</div>}
        </div>

        <div className="food-footer">
          <div>
            {isDeal ? (
              <div>
                <span className="food-old-price">{money(it.price)}</span>
                <span className="food-deal-price">{money(it.dealPrice)}</span>
                {pct && <span className="food-badge-discount">{pct}% OFF</span>}
              </div>
            ) : (
              <div className="food-price">
                {hasVariants && <span className="muted" style={{ fontSize: 12, fontWeight: 400 }}>from </span>}
                {money(minPrice)}
              </div>
            )}
            {isCustomizable && !isDeal && (
              <div className="muted" style={{ fontSize: 11 }}>Customizable</div>
            )}
          </div>

          <div>
            {it.available ? (
              <button className="food-btn-add" onClick={() => onPlus(it)}>
                <PlusIcon size={13} />
                <span>{isCustomizable ? 'Choose' : 'Add'}</span>
              </button>
            ) : (
              <span className="pill s-Served" style={{ fontSize: 11.5 }}>Out of stock</span>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}

// ---- Live Customer Order Tracker ----
function LiveTracker({ orders, slug, code, navigate }) {
  return (
    <div className="card" style={{ marginBottom: 14, borderColor: 'var(--accent-tint)' }}>
      <div className="row between" style={{ marginBottom: 8 }}>
        <b style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
          <span>📋 Your Orders</span>
        </b>
        <span className="muted" style={{ fontSize: 12 }}>Updates in real-time</span>
      </div>
      {orders.map((o) => (
        <TrackerRow key={o.id} o={o} slug={slug} navigate={navigate} />
      ))}
    </div>
  )
}

function TrackerRow({ o, slug, navigate }) {
  const [now, setNow] = useState(Date.now())

  useEffect(() => {
    if (o.status === 'Cooking') {
      const t = setInterval(() => setNow(Date.now()), 1000)
      return () => clearInterval(t)
    }
  }, [o.status])

  let countdown = null
  let isDue = false
  if (o.status === 'Cooking' && o.acceptedAt) {
    const done = new Date(o.acceptedAt).getTime() + o.etaMinutes * 60000
    const leftSec = Math.round((done - now) / 1000)
    if (leftSec > 0) {
      countdown = `${String(Math.floor(leftSec / 60)).padStart(2, '0')}:${String(leftSec % 60).padStart(2, '0')}`
    } else {
      isDue = true
      countdown = 'Finishing up'
    }
  }

  return (
    <div
      className="tracker"
      style={{ cursor: 'pointer', padding: '8px 0 8px 12px', borderLeftColor: o.status === 'Ready' ? 'var(--accent2)' : 'var(--accent)' }}
      onClick={() => navigate(`/r/${slug}/order/${o.id}`)}
      title="Tap to view full order details"
    >
      <div className="row between">
        <b style={{ fontSize: 14.5 }}>Order #{o.id}</b>
        <span className={'pill s-' + o.status}>
          {STATUS_LABEL[o.status] || o.status}
        </span>
      </div>
      <div className="muted" style={{ fontSize: 12.5, margin: '3px 0' }}>
        {o.items.map((i) => `${i.qty}× ${i.name}${i.variantName ? ` (${i.variantName})` : ''}`).join(', ')}
      </div>

      {o.status === 'Cooking' && (
        <div className="row between" style={{ marginTop: 4 }}>
          <span className="muted" style={{ fontSize: 12 }}>Kitchen estimate:</span>
          <span className="countdown" style={{ fontSize: 17, color: isDue ? 'var(--accent2)' : 'var(--amber)' }}>
            ⏱ {countdown}
          </span>
        </div>
      )}
      {o.status === 'Ready' && (
        <div style={{ color: 'var(--accent2)', fontWeight: 700, fontSize: 13, marginTop: 4, display: 'flex', alignItems: 'center', gap: 5 }}>
          <BellIcon size={15} /> Food is ready to serve! Tap to view
        </div>
      )}
      {o.status === 'Served' && (
        <div className="muted" style={{ fontSize: 12, marginTop: 2 }}>Served. Enjoy your meal!</div>
      )}
      <div className="muted" style={{ fontSize: 11.5, marginTop: 2, textAlign: 'right', color: 'var(--accent)' }}>
        View details & receipt →
      </div>
    </div>
  )
}

