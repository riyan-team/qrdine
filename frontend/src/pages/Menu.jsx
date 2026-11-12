import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { api, money, STATUS_LABEL, statusLabel } from '../api'
import { useHub } from '../useHub'
import { loadUntilOk } from '../useStaffOrders'
import { auth, isTicketCode, ticketLabel } from '../auth'
import { ui, toast, playBell } from '../ui.jsx'
import { useCart, plusOrCustomize, cartToItems, needsCustomizing } from '../ordering.jsx'
import { ArrowLeftIcon, BagIcon, BellIcon, BikeIcon, CheckIcon, CloseIcon, ClockIcon, LocateIcon, PinIcon, SearchIcon, TruckIcon } from '../icons.jsx'
import MapView, { kmBetween, myLocation } from '../MapView.jsx'
import { DishPicture, FoodArt, foodKind, foodTone } from '../foodArt.jsx'
import { flyToCart, reducedMotion, tilt, useParallax, useScrollDepth } from '../motion'
import { useLoyalty, RewardsStrip, RewardsHub, GoogleReviewCard, RateUs, burst } from '../loyalty.jsx'

// The guest's menu. Opens on category cards, a tap goes into that category, a swipe moves to
// the next one, and the phone's own back button walks back out. Screens live in the URL
// (?c=category, ?i=dish, ?cart=1, ?find=1) for that reason.
//
// Everything about an order is unchanged from before: useCart, plusOrCustomize, cartToItems and
// api.placeOrder are the same calls, and the server still works out every price itself.
//
// Three ways in:
//   /r/:slug/t/T4          a table QR: dine in, call a waiter, ask for the bill
//   /r/:slug/go            the restaurant's order QR: takeaway or delivery, no table (checkout asks name/phone/address)
//   /r/:slug/t/P251112-3   that takeaway/delivery order afterwards, followed on the phone that placed it

const DEALS = 'deals'

const fromPrice = (it) => (it.variants?.length ? Math.min(...it.variants.map((v) => v.price)) : it.price)
const isDeal = (it) => it.dealActive && it.dealPrice != null && !it.variants?.length

// The guest's name, number and address, remembered on their own phone for the next order.
const detailsKey = (slug) => `qrdine.guest.${slug}`
const loadDetails = (slug) => { try { return JSON.parse(localStorage.getItem(detailsKey(slug)) || 'null') || {} } catch { return {} } }
const saveDetails = (slug, d) => { try { localStorage.setItem(detailsKey(slug), JSON.stringify(d)) } catch {} }
const digits = (s) => String(s || '').replace(/\D/g, '')

/** Keeps something on screen long enough to animate out. */
function usePresence(open, ms = 320) {
  const [state, setState] = useState(open ? 'in' : 'gone')
  useEffect(() => {
    if (open) { setState('in'); return undefined }
    if (state === 'gone') return undefined
    setState('out')
    const t = setTimeout(() => setState('gone'), reducedMotion() ? 0 : ms)
    return () => clearTimeout(t)
  }, [open])
  return state
}

export default function Menu() {
  const { slug, code: tableParam } = useParams()
  const pickup = !tableParam                 // the restaurant's takeaway/delivery QR
  const code = tableParam || ''
  const ticket = isTicketCode(code)          // a takeaway/delivery order this phone placed
  const mode = pickup ? 'pickup' : ticket ? 'ticket' : 'table'
  const navigate = useNavigate()
  const location = useLocation()
  const [params] = useSearchParams()
  const scene = useParallax()

  const [data, setData] = useState(null)
  const [unavailable, setUnavailable] = useState(false)   // restaurant paused or address not found
  const cart = useCart(`qrdine.cart.${slug}.${code || 'go'}`)
  const [note, setNote] = useState('')
  const [orders, setOrders] = useState([])
  const [placing, setPlacing] = useState(false)
  const [placed, setPlaced] = useState(location.state?.placed || null)
  const [details, setDetails] = useState(() => loadDetails(slug))
  const [myTickets, setMyTickets] = useState([])
  const [popularIds, setPopularIds] = useState([])
  const notifiedBellRef = useRef(new Set())
  // Loyalty & reviews (additive: hidden until the restaurant switches rewards on)
  const loy = useLoyalty(slug, code)
  const [hubOpen, setHubOpen] = useState(false)
  const linkedRef = useRef(false)

  useEffect(() => {
    // Remember table code for guest session persistence
    if (slug && code) auth.rememberTable(slug, code)
    // Load menu and table orders
    // On a weak signal the first try can fail, so it keeps trying rather than leaving a guest looking at a blank menu.
    const stop = loadUntilOk(() => api.getMenu(slug).catch((e) => {
      if (e?.status === 403 || e?.status === 404) setUnavailable(true)
      throw e
    }), setData)
    if (code) api.tableOrders(slug, code).then(setOrders).catch(() => {})
    else { setOrders([]); setMyTickets(auth.myTickets(slug)) }
    api.getPopular(slug).then(setPopularIds).catch(() => {})

    // Load previously notified orders for this session to prevent repeated ring on reload
    try {
      const stored = sessionStorage.getItem(`qrdine.bell.${slug}`)
      if (stored) notifiedBellRef.current = new Set(JSON.parse(stored))
    } catch {}
    return stop
  }, [slug, code])

  // Real-time updates via SignalR
  useHub(slug, code || null, (o) => {
    setOrders((prev) => {
      const others = prev.filter((x) => x.id !== o.id)
      if (o.status === 'Paid' || o.status === 'Cancelled') return others
      if (o.tableCode && o.tableCode.toUpperCase() !== String(code).toUpperCase()) return others   // shifted to another table
      return [...others, o].sort((a, b) => a.id - b.id)
    })

    if (o.status === 'Ready' && !notifiedBellRef.current.has(o.id)) {
      notifiedBellRef.current.add(o.id)
      try {
        sessionStorage.setItem(`qrdine.bell.${slug}`, JSON.stringify([...notifiedBellRef.current]))
      } catch {}
      playBell()
      toast.success(o.channel === 'Takeaway' ? `Order #${o.id} is ready. Come and collect it.`
        : o.channel === 'Delivery' ? `Order #${o.id} is packed. The rider brings it next.`
        : `Order #${o.id} is ready and on its way to you.`)
    }

    // The bill was settled: a member sees the points it earned.
    if (o.status === 'Paid' && loy.me) {
      const before = loy.me.points
      setTimeout(async () => {
        const m = await loy.refresh()
        if (m && m.points > before) { burst(); toast.success(`+${(m.points - before).toLocaleString('en-PK')} points earned!`) }
      }, 1500)
    }
  }, null)

  // Moving from the order QR to the new ticket's page: start at the top, where its live status is.
  useEffect(() => { document.querySelector('.gm-view.gm-home')?.scrollTo?.(0, 0) }, [code])

  // A returning member with a running order: make sure this seating earns for them.
  useEffect(() => {
    if (!linkedRef.current && loy.me && orders.length > 0) { linkedRef.current = true; loy.link() }
  }, [loy.me, orders.length])

  // ---- screens, held in the URL so the back button works ----
  const open = (changes) => {
    const next = new URLSearchParams(params)
    Object.entries(changes).forEach(([k, v]) => { if (v == null) next.delete(k); else next.set(k, v) })
    navigate({ search: next.toString() ? `?${next}` : '' }, { state: { inApp: true } })
  }
  const close = (...keys) => {
    if (location.state?.inApp) { navigate(-1); return }
    const next = new URLSearchParams(params)
    keys.forEach((k) => next.delete(k))
    navigate({ search: next.toString() ? `?${next}` : '' }, { replace: true })
  }
  // Read the address as it is now: this runs after a swipe settles, when a sheet may have just opened.
  const swipedTo = (id) => {
    const next = new URLSearchParams(window.location.search)
    if (next.get('c') === String(id)) return
    next.set('c', id)
    navigate({ search: `?${next}` }, { replace: true, state: window.history.state?.usr })
  }

  const allItems = useMemo(() => {
    if (!data?.menu) return []
    return data.menu.flatMap((cat) => cat.items.map((it) => ({ ...it, categoryName: cat.name, categoryId: cat.id })))
  }, [data?.menu])

  const deals = useMemo(() => allItems.filter((it) => it.available && it.dealActive && it.dealPrice != null), [allItems])
  const popular = useMemo(() => popularIds.map((id) => allItems.find((it) => it.id === id)).filter((it) => it?.available), [popularIds, allItems])
  const categories = useMemo(() => (data?.menu || []).filter((c) => c.items.length > 0), [data?.menu])
  const panels = useMemo(() => (deals.length ? [{ id: DEALS, name: 'Deals', items: deals }, ...categories] : categories), [deals, categories])

  // Guests on a menu-only plan can look but there's nothing behind an order button for them.
  // The order QR also needs the restaurant to have takeaway or delivery switched on.
  const pk = data?.restaurant?.pickup
  const pickupOpen = !!(pk?.takeaway || pk?.delivery)
  const canOrder = data?.restaurant?.features?.ordering !== false && (!pickup || pickupOpen)

  const catParam = params.get('c')
  const itemParam = params.get('i')
  const itemNow = itemParam ? allItems.find((it) => String(it.id) === itemParam) : null
  const itemRef = useRef(null)
  if (itemNow) itemRef.current = itemNow
  const cartOpen = !!params.get('cart') && cart.list.length > 0
  const findOpen = !!params.get('find')

  const pushedBack = !!itemNow || cartOpen || hubOpen

  const onPlus = (it, e) => {
    const el = e?.currentTarget
    plusOrCustomize(it, (line) => { cart.addToCart(line); flyToCart(el) }, (toCustomize) => open({ i: toCustomize.id }))
  }

  const place = async (how) => {
    if (cart.list.length === 0) return
    setPlacing(true)
    if (pickup) {
      try {
        const res = await api.placeGuestTicket(slug, {
          channel: how.channel, customerName: how.name.trim(), customerPhone: how.phone.trim(),
          address: how.channel === 'Delivery' ? how.address.trim() : null, items: cartToItems(cart.list), note: note || null,
          lat: how.channel === 'Delivery' ? how.lat ?? null : null, lng: how.channel === 'Delivery' ? how.lng ?? null : null,
        })
        auth.keepTicket(slug, res.ticket, res.token)
        saveDetails(slug, { name: how.name.trim(), phone: how.phone.trim(), address: how.address?.trim() || details.address || '', channel: how.channel,
          lat: how.lat ?? details.lat ?? null, lng: how.lng ?? details.lng ?? null })
        cart.clear()
        setNote('')
        const shown = { ...res.order, ticketName: res.ticket.tableName }
        setPlaced(shown)
        // From here on it is that ticket's own page: live status, add more, rewards.
        navigate(`/r/${slug}/t/${res.ticket.tableCode}`, { replace: true, state: { placed: shown } })
      } catch (e) {
        ui.alert('Could not place order: ' + e.message)
      }
      setPlacing(false)
      return
    }
    try {
      const newOrder = await api.placeOrder(slug, { tableCode: code, items: cartToItems(cart.list), note: note || null })
      cart.clear()
      setNote('')
      setPlaced(newOrder)
      close('cart')
      loy.link()   // the seating exists now: tie its bill to the member (no-op when not a member)
    } catch (e) {
      ui.alert('Could not place order: ' + e.message)
    }
    setPlacing(false)
  }

  if (unavailable) return (
    <div className="gm-closed">
      <div className="gm-closed-card">
        <h1>This menu isn't available right now</h1>
        <p>Please ask a member of staff to take your order.</p>
      </div>
    </div>
  )
  if (!data) return <MenuSkeleton />

  const r = data.restaurant
  if (pickup && !pickupOpen && data.restaurant?.features?.ordering !== false) return (
    <div className="gm-closed">
      <div className="gm-closed-card">
        <h1>{r.name} isn't taking online orders right now</h1>
        <p>Takeaway and delivery orders are closed for the moment. Please call the restaurant, or try again later.</p>
      </div>
    </div>
  )
  const tableNo = code.replace(/^[Tt]/, '')
  const where = mode === 'table' ? `Table ${tableNo}` : mode === 'ticket' ? ticketLabel(code)
    : pk?.takeaway && pk?.delivery ? 'Order online' : pk?.delivery ? 'Delivery' : 'Takeaway'

  return (
    <div className="gm" ref={scene}>
      <div className={'gm-stage' + (pushedBack ? ' pushed' : '')}>
        <HomeView r={r} where={where} mode={mode} pk={pk} myTickets={myTickets} slug={slug} code={code} loy={loy} orders={orders} navigate={navigate}
          deals={deals} popular={popular} categories={categories} covered={!!catParam} canOrder={canOrder}
          onCategory={(id) => open({ c: id })} onFind={() => open({ find: 1 })} onItem={(it) => open({ i: it.id })}
          onPlus={onPlus} cart={cart} onHub={() => setHubOpen(true)} />

        <CategoryView show={!!catParam} panels={panels} activeId={catParam} cart={cart} canOrder={canOrder}
          orders={orders} onSwipe={swipedTo} onBack={() => close('c')} onFind={() => open({ find: 1 })}
          onItem={(it) => open({ i: it.id })} onPlus={onPlus} onTrack={(o) => navigate(`/r/${slug}/order/${o.id}`)} />

        <FindView show={findOpen} items={allItems} categories={categories} cart={cart} canOrder={canOrder}
          onClose={() => close('find')} onItem={(it) => open({ i: it.id })} onPlus={onPlus}
          onCategory={(id) => { const next = new URLSearchParams(params); next.delete('find'); next.set('c', id); navigate({ search: `?${next}` }, { replace: true, state: { inApp: true } }) }} />
      </div>

      {canOrder && cart.list.length > 0 && (
        <button className="gm-cartbar" onClick={() => open({ cart: 1 })}>
          <span className="gm-cartbar-n">{cart.count}</span>
          <span className="gm-cartbar-label">View your order</span>
          <b>{money(cart.total)}</b>
        </button>
      )}

      <ItemSheet item={itemRef.current} open={!!itemNow} canOrder={canOrder} onClose={() => close('i')}
        onAdd={(line, el) => { cart.addToCart(line); close('i'); setTimeout(() => flyToCart(el), 60) }} />

      <CartSheet open={cartOpen} cart={cart} note={note} setNote={setNote} placing={placing} place={place} where={where}
        mode={mode} pk={pk} details={details} setDetails={setDetails}
        allItems={allItems} onClose={() => close('cart')} onPlus={onPlus} onItem={(it) => open({ i: it.id })} />

      {placed && <PlacedOverlay order={placed} where={placed.ticketName ? (placed.channel === 'Delivery' ? 'delivery' : 'takeaway') : where}
        mode={placed.ticketName ? 'ticket' : mode} onDone={() => { setPlaced(null); if (location.state?.placed) navigate(location.pathname + location.search, { replace: true }) }} />}

      <RewardsHub loy={loy} slug={slug} code={code} open={hubOpen} onClose={() => setHubOpen(false)} />
    </div>
  )
}

// Two taps a guest needs while sitting at the table. Each goes straight to the staff screens.
function TableHelp({ slug, code, hasOrders }) {
  const [sent, setSent] = useState({})
  const timers = useRef([])
  useEffect(() => () => timers.current.forEach(clearTimeout), [])
  const call = async (kind) => {
    if (sent[kind]) return
    setSent((s) => ({ ...s, [kind]: true }))
    try {
      await api.callStaff(slug, code, kind)
      toast.success(kind === 'Bill' ? "We've told them you'd like the bill." : "We've called a waiter for you.")
      timers.current.push(setTimeout(() => setSent((s) => ({ ...s, [kind]: false })), 60000))
    } catch (e) {
      setSent((s) => ({ ...s, [kind]: false }))
      toast.error(e.message || 'That did not go through. Please try again.')
    }
  }
  return (
    <div className="gm-help">
      <button disabled={sent.Waiter} onClick={() => call('Waiter')}><BellIcon size={16} /> {sent.Waiter ? 'Waiter called' : 'Call a waiter'}</button>
      {hasOrders && <button disabled={sent.Bill} onClick={() => call('Bill')}><ClockIcon size={16} /> {sent.Bill ? 'Bill requested' : 'Ask for the bill'}</button>}
    </div>
  )
}

// ---- Opening screen ----
function HomeView({ r, where, mode, pk, myTickets, slug, code, loy, orders, navigate, deals, popular, categories, covered, canOrder, onCategory, onFind, onItem, onPlus, cart, onHub }) {
  const hour = (new Date().getUTCHours() + 5) % 24
  const greet = hour < 12 ? 'Good morning' : hour < 17 ? 'Good afternoon' : 'Good evening'
  const seen = useRef(false)
  useEffect(() => { seen.current = true }, [])
  const [handedOver, setHandedOver] = useState('')   // 'Delivered' / 'Collected' once the tracker says so

  return (
    <div className={'gm-view gm-home' + (covered ? ' covered' : '')}>
      <header className="gm-hero">
        <div className="gm-hero-float" aria-hidden="true">
          <FoodArt kind="biryani" className="f1" /><FoodArt kind="bbq" className="f2" /><FoodArt kind="drink" className="f3" />
        </div>
        <div className="gm-coin" aria-hidden="true">
          <div className="gm-coin-spin">
            <div className="gm-coin-face">{r.logoUrl ? <img src={r.logoUrl} alt="" /> : <span>{r.name.charAt(0)}</span>}</div>
            <div className="gm-coin-face back"><span>{r.name.charAt(0)}</span></div>
          </div>
        </div>
        <div className="gm-hero-txt">
          <h1>{r.name}</h1>
          <p>{greet}. {mode === 'pickup' ? 'Takeaway or delivery?' : mode === 'ticket' ? 'Your order is below. Forgot something? Add it.' : 'What are you in the mood for?'}</p>
        </div>
        <span className="gm-table">{where}</span>
      </header>

      <div className="gm-body">
        <button className="gm-search" onClick={onFind}><SearchIcon size={18} /> Search the menu</button>

        {orders.length > 0 && <LiveOrders orders={orders} handedOver={handedOver} onOpen={(o) => navigate(`/r/${slug}/order/${o.id}`)} />}

        {canOrder && mode === 'table' && <TableHelp slug={slug} code={code} hasOrders={orders.length > 0} />}

        {mode === 'pickup' && <PickupWays pk={pk} />}
        {mode === 'pickup' && myTickets.length > 0 && (
          <div className="gm-mytickets">
            {myTickets.slice(0, 3).map((t) => (
              <button key={t.code} onClick={() => navigate(`/r/${slug}/t/${t.code}`)}>
                {t.channel === 'Delivery' ? <TruckIcon size={16} /> : <BagIcon size={16} />}
                <span>Your {t.channel === 'Delivery' ? 'delivery' : 'takeaway'} order <b>{ticketLabel(t.code).split(' ')[1]}</b></span>
                <em>Follow it</em>
              </button>
            ))}
          </div>
        )}
        {mode === 'ticket' && <TrackCard slug={slug} code={code} onHandedOver={setHandedOver} />}
        {mode === 'ticket' && (
          <div className="gm-help">
            <button onClick={() => navigate(`/r/${slug}/go`)}><BagIcon size={16} /> Start a new order</button>
          </div>
        )}

        <RewardsStrip loy={loy} onOpen={onHub} />

        {deals.length > 0 && <DealCarousel deals={deals} canOrder={canOrder} onItem={onItem} onPlus={onPlus} />}

        {popular.length > 0 && (
          <section className="gm-sec">
            <h2>Most ordered here</h2>
            <div className="gm-popular">
              {popular.map((it) => (
                <article key={it.id} className="gm-pop" onClick={() => onItem(it)}>
                  <DishPicture item={it} categoryName={it.categoryName} className="gm-pop-pic" />
                  <b>{it.name}</b>
                  <div className="gm-pop-foot">
                    <span>{money(isDeal(it) ? it.dealPrice : fromPrice(it))}</span>
                    {canOrder && <AddControl it={it} cart={cart} onPlus={onPlus} small />}
                  </div>
                </article>
              ))}
            </div>
          </section>
        )}

        <section className="gm-sec">
          <h2>The menu</h2>
          <div className="gm-cats">
            {deals.length > 0 && <CategoryCard name="Deals" kind="deal" count={deals.length} onClick={() => onCategory(DEALS)} i={0} />}
            {categories.map((c, i) => {
              const photo = c.items.find((it) => it.imageUrl)?.imageUrl
              const from = Math.min(...c.items.map(fromPrice))
              return (
                <CategoryCard key={c.id} name={c.name} kind={foodKind(c.name)} photo={photo} count={c.items.length} from={from}
                  i={i + (deals.length ? 1 : 0)} onClick={() => onCategory(c.id)} />
              )
            })}
          </div>
        </section>

        <RateUs slug={slug} code={code} loy={loy} />
        <GoogleReviewCard info={loy.info} />
        <div className="gm-endspace" />
      </div>
    </div>
  )
}

function CategoryCard({ name, kind, photo, count, from, i, onClick }) {
  const [a, b] = foodTone(kind)
  return (
    <button className={'gm-cat' + (photo ? ' has-photo' : '')} style={{ '--i': i, background: photo ? undefined : `linear-gradient(155deg, ${a}, ${b})` }} onClick={onClick} {...tilt(10)}>
      {photo ? <img src={photo} alt="" loading="lazy" /> : <FoodArt kind={kind} className="gm-cat-art" />}
      <span className="gm-cat-txt">
        <b>{name}</b>
        <small>{count} {count === 1 ? 'item' : 'items'}{from != null ? ` · from ${money(from)}` : ''}</small>
      </span>
    </button>
  )
}

// Today's deals, as a row of cards that turn towards you as they reach the middle.
function DealCarousel({ deals, canOrder, onItem, onPlus }) {
  const ref = useScrollDepth((child, depth) => {
    if (reducedMotion()) return
    const d = Math.max(-1.4, Math.min(1.4, depth))
    child.firstElementChild.style.transform = `rotateY(${(d * -26).toFixed(1)}deg) scale(${(1 - Math.abs(d) * 0.1).toFixed(3)}) translateZ(${(-Math.abs(d) * 30).toFixed(0)}px)`
  })
  return (
    <section className="gm-sec">
      <h2>Today's deals</h2>
      <div className="gm-deals" ref={ref}>
        {deals.map((it) => {
          const pct = it.price > it.dealPrice ? Math.round((1 - it.dealPrice / it.price) * 100) : null
          return (
            <div key={it.id} className="gm-deal-slot">
            <article className="gm-deal" onClick={() => onItem(it)}>
              <DishPicture item={it} categoryName={it.categoryName} className="gm-deal-pic" />
              {pct > 0 && <span className="gm-off">{pct}% off</span>}
              <div className="gm-deal-txt">
                <b>{it.name}</b>
                {it.dealDescription && <small>{it.dealDescription}</small>}
                <div className="gm-deal-foot">
                  <span className="gm-price"><s>{money(it.price)}</s> {money(it.dealPrice)}</span>
                  {canOrder && <button className="gm-add-pill" onClick={(e) => { e.stopPropagation(); onPlus(it, e) }}>Add</button>}
                </div>
              </div>
            </article>
            </div>
          )
        })}
      </div>
    </section>
  )
}

// ---- Inside a category: swipe sideways for the next one ----
function CategoryView({ show, panels, activeId, cart, canOrder, orders, onSwipe, onBack, onFind, onItem, onPlus, onTrack }) {
  const presence = usePresence(show, 380)
  const pinnedId = useRef(activeId)
  if (show && activeId) pinnedId.current = activeId
  if (presence === 'gone') return null
  return (
    <div className={`gm-view gm-catview ${presence}`}>
      <CategoryPager panels={panels} activeId={pinnedId.current} cart={cart} canOrder={canOrder} orders={orders}
        onSwipe={onSwipe} onBack={onBack} onFind={onFind} onItem={onItem} onPlus={onPlus} onTrack={onTrack} />
    </div>
  )
}

function CategoryPager({ panels, activeId, cart, canOrder, orders, onSwipe, onBack, onFind, onItem, onPlus, onTrack }) {
  const [current, setCurrent] = useState(activeId)
  const chipsRef = useRef(null)
  const settled = useRef(false)
  const programmatic = useRef(false)

  const pager = useScrollDepth((child, depth) => {
    if (reducedMotion()) return
    const d = Math.max(-1.1, Math.min(1.1, depth))
    const panel = child.firstElementChild
    panel.style.transform = `rotateY(${(d * -24).toFixed(2)}deg) scale(${(1 - Math.abs(d) * 0.07).toFixed(3)})`
    panel.style.opacity = (1 - Math.min(Math.abs(d) * 0.55, 0.65)).toFixed(3)
  })

  // Open on the category that was tapped, with no sliding across the ones before it.
  useLayoutEffect(() => {
    const idx = Math.max(0, panels.findIndex((p) => String(p.id) === String(activeId)))
    const el = pager.current
    if (el) el.scrollLeft = idx * el.clientWidth
    settled.current = true
  }, [])

  // Which panel are we on? Keep the chips and the URL in step with the swipe.
  useEffect(() => {
    const el = pager.current
    if (!el) return undefined
    let t = 0
    const onScroll = () => {
      clearTimeout(t)
      t = setTimeout(() => {
        const idx = Math.round(el.scrollLeft / el.clientWidth)
        const id = panels[idx]?.id
        if (id == null) return
        setCurrent((cur) => {
          if (String(cur) === String(id)) return cur
          return id
        })
        programmatic.current = false
        onSwipe(id)
      }, 90)
    }
    el.addEventListener('scroll', onScroll, { passive: true })
    return () => { el.removeEventListener('scroll', onScroll); clearTimeout(t) }
  }, [panels])

  useEffect(() => {
    const chip = chipsRef.current?.querySelector('.on')
    chip?.scrollIntoView({ behavior: settled.current ? 'smooth' : 'auto', inline: 'center', block: 'nearest' })
  }, [current])

  const jump = (id) => {
    const idx = panels.findIndex((p) => String(p.id) === String(id))
    programmatic.current = true
    pager.current?.scrollTo({ left: idx * pager.current.clientWidth, behavior: reducedMotion() ? 'auto' : 'smooth' })
    setCurrent(id)
  }

  const active = panels.find((p) => String(p.id) === String(current)) || panels[0]
  const running = orders[orders.length - 1]

  return (
    <>
      <div className="gm-bar">
        <button className="gm-icon" onClick={onBack} aria-label="Back to all categories"><ArrowLeftIcon size={20} /></button>
        <h1 key={active?.id}>{active?.name}</h1>
        {running && (
          <button className={`gm-icon gm-bell s-${running.status}`} onClick={() => onTrack(running)} aria-label={`Order ${running.id}: ${STATUS_LABEL[running.status]}`}>
            <BellIcon size={19} />
          </button>
        )}
        <button className="gm-icon" onClick={onFind} aria-label="Search the menu"><SearchIcon size={19} /></button>
      </div>
      <nav className="gm-chips" ref={chipsRef}>
        {panels.map((p) => (
          <button key={p.id} className={String(p.id) === String(current) ? 'on' : ''} onClick={() => jump(p.id)}>{p.name}</button>
        ))}
      </nav>
      <div className="gm-pager" ref={pager}>
        {panels.map((p) => (
          <div key={p.id} className="gm-page">
            <section className="gm-panel">
              {p.items.map((it, i) => (
                <DishRow key={it.id} it={it} i={i} categoryName={it.categoryName || p.name} cart={cart} canOrder={canOrder} onItem={onItem} onPlus={onPlus} />
              ))}
              <div className="gm-endspace" />
            </section>
          </div>
        ))}
      </div>
    </>
  )
}

// ---- One dish ----
function DishRow({ it, i = 0, categoryName, cart, canOrder, onItem, onPlus }) {
  const deal = isDeal(it)
  const pct = deal && it.price > it.dealPrice ? Math.round((1 - it.dealPrice / it.price) * 100) : null
  return (
    <article className={'gm-dish' + (it.available ? '' : ' off')} style={{ '--i': Math.min(i, 8) }} onClick={() => it.available && onItem(it)}>
      <div className="gm-dish-txt">
        <h3>{it.name}</h3>
        {it.nameUrdu && <div className="gm-urdu" dir="rtl" lang="ur">{it.nameUrdu}</div>}
        {(it.dealActive && it.dealDescription) ? <p>{it.dealDescription}</p> : it.description ? <p>{it.description}</p> : null}
        <div className="gm-dish-price">
          {deal
            ? <span className="gm-price"><s>{money(it.price)}</s> {money(it.dealPrice)}{pct > 0 && <em>{pct}% off</em>}</span>
            : <span className="gm-price plain">{it.variants?.length > 0 && <small>from </small>}{money(fromPrice(it))}</span>}
          {it.prepMinutes > 0 && <span className="gm-mins"><ClockIcon size={12} /> {it.prepMinutes} min</span>}
        </div>
      </div>
      <div className="gm-dish-side">
        <DishPicture item={it} categoryName={categoryName} className="gm-dish-pic" />
        {canOrder && (it.available
          ? <AddControl it={it} cart={cart} onPlus={onPlus} />
          : <span className="gm-soldout">Not available</span>)}
      </div>
    </article>
  )
}

/** The + on a dish. A plain dish that's in the order becomes − 1 +, right there. */
function AddControl({ it, cart, onPlus, small }) {
  const lines = cart.list.filter((x) => x.item.id === it.id)
  const qty = lines.reduce((s, x) => s + x.qty, 0)
  const stop = (fn) => (e) => { e.stopPropagation(); fn(e) }

  if (qty > 0 && !needsCustomizing(it)) {
    return (
      <div className={'gm-step' + (small ? ' small' : '')} onClick={(e) => e.stopPropagation()}>
        <button onClick={stop(() => cart.changeQty(`${it.id}`, -1))} aria-label={`One less ${it.name}`}>−</button>
        <b key={qty} className="gm-step-n">{qty}</b>
        <button onClick={stop((e) => onPlus(it, e))} aria-label={`One more ${it.name}`}>+</button>
      </div>
    )
  }
  return (
    <button className={'gm-plus' + (small ? ' small' : '') + (qty > 0 ? ' has' : '')} onClick={stop((e) => onPlus(it, e))}
      aria-label={needsCustomizing(it) ? `Choose options for ${it.name}` : `Add ${it.name}`}>
      {qty > 0 ? qty : '+'}
    </button>
  )
}

// ---- Bottom sheets. Pull the handle down to close, or tap outside. ----
function Sheet({ open, onClose, children, label, className = '' }) {
  const presence = usePresence(open, 340)
  const panel = useRef(null)
  const drag = useRef(null)

  useEffect(() => {
    if (!open) return undefined
    const onKey = (e) => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open])

  if (presence === 'gone') return null

  const down = (e) => {
    drag.current = { y: e.clientY, t: Date.now(), dy: 0 }
    e.currentTarget.setPointerCapture?.(e.pointerId)
    if (panel.current) panel.current.style.transition = 'none'
  }
  const move = (e) => {
    if (!drag.current) return
    const dy = Math.max(0, e.clientY - drag.current.y)
    drag.current.dy = dy
    if (panel.current) panel.current.style.transform = `translateY(${dy}px)`
  }
  const up = () => {
    if (!drag.current) return
    const { dy, t } = drag.current
    drag.current = null
    const fast = dy > 40 && dy / (Date.now() - t) > 0.6
    if (panel.current) {
      panel.current.style.transition = ''
      panel.current.style.transform = ''
    }
    if (dy > 110 || fast) onClose()
  }

  return (
    <div className={`gm-sheetwrap ${presence}`} role="dialog" aria-modal="true" aria-label={label}>
      <div className="gm-scrim" onClick={onClose} />
      <div className={`gm-sheet ${className}`} ref={panel}>
        <div className="gm-grab" onPointerDown={down} onPointerMove={move} onPointerUp={up} onPointerCancel={up}><i /></div>
        {children}
      </div>
    </div>
  )
}

function ItemSheet({ item, open, canOrder, onClose, onAdd }) {
  const [variantId, setVariantId] = useState(null)
  const [selected, setSelected] = useState({})   // { groupId: number[] }
  const [qty, setQty] = useState(1)
  const [tried, setTried] = useState(false)

  useEffect(() => {
    if (!open || !item) return
    setVariantId(item.variants?.[0]?.id ?? null)
    setSelected({})
    setQty(1)
    setTried(false)
  }, [open, item?.id])

  if (!item) return null

  const toggle = (group, optId) => setSelected((s) => {
    const cur = s[group.id] || []
    let next
    if (group.maxSelect === 1) next = cur.includes(optId) && group.minSelect === 0 ? [] : [optId]
    else if (cur.includes(optId)) next = cur.filter((x) => x !== optId)
    else if (cur.length >= group.maxSelect) next = cur
    else next = [...cur, optId]
    return { ...s, [group.id]: next }
  })

  // The same rule the server charges by: a size's price, or else the deal price, or else the
  // normal price, plus the options. The price shown here is only ever a preview of that.
  const base = item.variants?.length ? (item.variants.find((v) => v.id === variantId)?.price ?? 0)
    : (item.dealActive && item.dealPrice != null) ? item.dealPrice : item.price
  const allOpts = (item.modifierGroups || []).flatMap((g) => g.options)
  const chosenIds = Object.values(selected).flat()
  const chosen = allOpts.filter((o) => chosenIds.includes(o.id))
  const unit = base + chosen.reduce((s, o) => s + o.price, 0)
  const groupMissing = (g) => (selected[g.id]?.length || 0) < g.minSelect
  const valid = (!item.variants?.length || variantId != null) && (item.modifierGroups || []).every((g) => !groupMissing(g))

  const add = (e) => {
    if (!valid) { setTried(true); return }
    const sig = needsCustomizing(item)
      ? `${item.id}|${variantId ?? ''}|${[...chosenIds].sort((a, b) => a - b).join(',')}`
      : `${item.id}`
    onAdd({
      sig, item, name: item.name,
      variantId, variantName: item.variants?.find((v) => v.id === variantId)?.name ?? null,
      modifierOptionIds: chosenIds, modifierNames: chosen.map((o) => o.name),
      unitPrice: unit, qty,
    }, e.currentTarget)
  }

  return (
    <Sheet open={open} onClose={onClose} label={item.name} className="gm-itemsheet">
      <div className="gm-item-scroll">
        <DishPicture item={item} categoryName={item.categoryName} className="gm-item-pic" />
        <button className="gm-sheet-x" onClick={onClose} aria-label="Close"><CloseIcon size={18} /></button>
        <div className="gm-item-body">
          <h2>{item.name}</h2>
          {item.nameUrdu && <div className="gm-urdu big" dir="rtl" lang="ur">{item.nameUrdu}</div>}
          {item.description && <p>{item.description}</p>}
          {item.dealActive && item.dealDescription && <p className="gm-item-deal">{item.dealDescription}</p>}
          {item.prepMinutes > 0 && <span className="gm-mins"><ClockIcon size={13} /> About {item.prepMinutes} minutes</span>}

          {item.variants?.length > 0 && (
            <div className="gm-group">
              <h3>Choose a size</h3>
              {item.variants.map((v) => (
                <button key={v.id} className={'gm-opt' + (variantId === v.id ? ' on' : '')} onClick={() => setVariantId(v.id)}>
                  <i className="radio" /><span>{v.name}</span><b>{money(v.price)}</b>
                </button>
              ))}
            </div>
          )}

          {(item.modifierGroups || []).map((g) => (
            <div key={g.id} className={'gm-group' + (tried && groupMissing(g) ? ' missing' : '')}>
              <h3>{g.name} <small>{g.minSelect > 0 ? 'Please choose' : 'Optional'}{g.maxSelect > 1 ? `, up to ${g.maxSelect}` : ''}</small></h3>
              {g.options.map((o) => {
                const on = (selected[g.id] || []).includes(o.id)
                return (
                  <button key={o.id} className={'gm-opt' + (on ? ' on' : '')} onClick={() => toggle(g, o.id)}>
                    <i className={g.maxSelect === 1 ? 'radio' : 'box'}>{on && g.maxSelect !== 1 && <CheckIcon size={13} />}</i>
                    <span>{o.name}</span>
                    {o.price > 0 && <b>+ {money(o.price)}</b>}
                  </button>
                )
              })}
            </div>
          ))}
        </div>
      </div>
      {canOrder && item.available && (
        <div className="gm-item-foot">
          <div className="gm-step big">
            <button onClick={() => setQty((q) => Math.max(1, q - 1))} aria-label="One less">−</button>
            <b>{qty}</b>
            <button onClick={() => setQty((q) => Math.min(50, q + 1))} aria-label="One more">+</button>
          </div>
          <button className="gm-cta" onClick={add}>
            <span>Add to order</span><b>{money(unit * qty)}</b>
          </button>
        </div>
      )}
    </Sheet>
  )
}

function CartSheet({ open, cart, note, setNote, placing, place, where, mode, pk, details, setDetails, allItems, onClose, onPlus, onItem }) {
  const pickup = mode === 'pickup'
  const ways = [pk?.takeaway && 'Takeaway', pk?.delivery && 'Delivery'].filter(Boolean)
  const channel = ways.includes(details.channel) ? details.channel : ways[0]
  const delivery = pickup && channel === 'Delivery'
  const fee = delivery ? Number(pk?.deliveryFee) || 0 : 0
  const [err, setErr] = useState('')
  const set = (k) => (e) => { setErr(''); setDetails((d) => ({ ...d, [k]: e.target.value })) }
  // The door on a map: the rider drives to the pin, not to a guess from the address.
  const shop = pk?.shopLat != null && pk?.shopLng != null ? { lat: pk.shopLat, lng: pk.shopLng } : null
  const pin = details.lat != null && details.lng != null ? { lat: details.lat, lng: details.lng } : null
  const radius = Number(pk?.radiusKm) || 0
  const km = kmBetween(shop, pin)
  const tooFar = radius > 0 && km != null && km > radius
  const [locating, setLocating] = useState(false)
  const movePin = (p) => { setErr(''); setDetails((d) => ({ ...d, lat: +p.lat.toFixed(6), lng: +p.lng.toFixed(6) })) }
  const locate = async () => {
    setLocating(true)
    try { movePin(await myLocation()) } catch (e) { setErr(e.message) }
    setLocating(false)
  }
  const send = () => {
    if (!pickup) return place()
    const how = { channel, name: details.name || '', phone: details.phone || '', address: details.address || '', lat: pin?.lat, lng: pin?.lng }
    if (!how.name.trim()) return setErr('Please enter your name.')
    if (digits(how.phone).length < 10) return setErr('Please enter your mobile number, so the restaurant can call you.')
    if (delivery && how.address.trim().length < 6) return setErr('Please enter the delivery address.')
    if (delivery && tooFar) return setErr(`That pin is ${km.toFixed(1)} km away. Delivery is within ${radius} km.`)
    return place(how)
  }

  // A drink, dessert or bread they haven't got yet: the natural last-second add.
  const extras = useMemo(() => {
    const inCart = new Set(cart.list.map((x) => x.item.id))
    const score = (it) => {
      const k = foodKind(it.categoryName)
      return k === 'drink' ? 3 : k === 'naan' ? 2 : k === 'dessert' ? 1 : k === 'salad' ? 1 : 0
    }
    return allItems.filter((it) => it.available && !inCart.has(it.id) && score(it) > 0)
      .sort((a, b) => score(b) - score(a)).slice(0, 8)
  }, [cart.list.length, allItems])

  return (
    <Sheet open={open} onClose={onClose} label="Your order" className="gm-cartsheet">
      <div className="gm-cart-head">
        <h2>Your order</h2>
        <span className="gm-table light">{pickup ? channel : where}</span>
        <button className="gm-sheet-x inline" onClick={onClose} aria-label="Close"><CloseIcon size={18} /></button>
      </div>
      <div className="gm-cart-scroll">
        <div className="gm-lines">
          {cart.list.map((x) => (
            <div className="gm-line" key={x.sig}>
              <div className="gm-line-txt">
                <b>{x.name}{x.variantName ? ` (${x.variantName})` : ''}</b>
                {x.modifierNames.length > 0 && <small>{x.modifierNames.join(', ')}</small>}
                <span>{money(x.unitPrice * x.qty)}</span>
              </div>
              <div className="gm-step">
                <button onClick={() => cart.changeQty(x.sig, -1)} aria-label={`One less ${x.name}`}>−</button>
                <b key={x.qty} className="gm-step-n">{x.qty}</b>
                <button onClick={() => cart.changeQty(x.sig, +1)} aria-label={`One more ${x.name}`}>+</button>
              </div>
            </div>
          ))}
        </div>

        {extras.length > 0 && (
          <div className="gm-extras">
            <h3>Goes well with this</h3>
            <div className="gm-extra-row">
              {extras.map((it) => (
                <button key={it.id} className="gm-extra" onClick={(e) => (needsCustomizing(it) ? onItem(it) : onPlus(it, e))}>
                  <DishPicture item={it} categoryName={it.categoryName} className="gm-extra-pic" />
                  <span>{it.name}</span>
                  <b>+ {money(isDeal(it) ? it.dealPrice : fromPrice(it))}</b>
                </button>
              ))}
            </div>
          </div>
        )}

        <label className="gm-note">
          <span>Anything for the kitchen?</span>
          <input value={note} onChange={(e) => setNote(e.target.value)} maxLength={200} placeholder="No onion, extra spicy…" />
        </label>

        {pickup && (
          <div className="gm-checkout">
            {ways.length > 1 && (
              <div className="gm-ways" role="radiogroup" aria-label="Takeaway or delivery">
                {ways.map((w) => (
                  <button key={w} role="radio" aria-checked={channel === w} className={channel === w ? 'on' : ''}
                    onClick={() => { setErr(''); setDetails((d) => ({ ...d, channel: w })) }}>
                    {w === 'Delivery' ? <TruckIcon size={20} /> : <BagIcon size={20} />}
                    <b>{w}</b>
                    <small>{w === 'Delivery' ? (Number(pk.deliveryFee) > 0 ? `${money(pk.deliveryFee)} delivery` : 'Free delivery') : 'Collect it yourself'}</small>
                  </button>
                ))}
              </div>
            )}
            {delivery && pk?.deliveryNote && <p className="gm-ways-note"><TruckIcon size={14} /> {pk.deliveryNote}</p>}
            <label className="gm-field"><span>Your name</span>
              <input value={details.name || ''} onChange={set('name')} maxLength={60} autoComplete="name" placeholder="Name" /></label>
            <label className="gm-field"><span>Mobile number</span>
              <input value={details.phone || ''} onChange={set('phone')} maxLength={16} inputMode="tel" autoComplete="tel" placeholder="03xx xxxxxxx" /></label>
            {delivery && (
              <label className="gm-field"><span>Delivery address</span>
                <textarea value={details.address || ''} onChange={set('address')} maxLength={300} rows={2} autoComplete="street-address" placeholder="House, street, area, landmark" /></label>
            )}
            {delivery && (
              <div className="gm-pin">
                <div className="gm-pin-head">
                  <span><PinIcon size={16} /> {pin ? 'Your door on the map' : 'Pin your door on the map'}</span>
                  <button type="button" onClick={locate} disabled={locating}>
                    {locating ? <span className="spinner" /> : <LocateIcon size={15} />} {pin ? 'Locate me again' : 'Use my location'}
                  </button>
                </div>
                {(shop || pin) && (
                  <MapView className="gm-pin-map" height={190} points={shop ? [{ ...shop, kind: 'shop', label: 'Restaurant' }] : []}
                    area={shop && radius > 0 ? { ...shop, km: radius } : null}
                    pick={pin ? { ...pin, onMove: movePin } : { lat: NaN, lng: NaN, onMove: movePin }} />
                )}
                <small className={tooFar ? 'bad' : ''}>
                  {pin
                    ? (km != null ? `${km.toFixed(1)} km from the restaurant. ` : '') + (tooFar ? `Outside the ${radius} km delivery area.` : 'Drag the pin if it is not exactly on your door.')
                    : shop ? 'Tap the map at your door, or use your location. The rider follows this pin.' : 'Share your location so the rider can find you.'}
                </small>
              </div>
            )}
            {err && <p className="gm-field-err" role="alert">{err}</p>}
          </div>
        )}
      </div>
      <div className="gm-cart-foot">
        {fee > 0 && <div className="gm-subline"><span>Food {money(cart.total)}</span><span>Delivery {money(fee)}</span></div>}
        <div className="gm-total"><span>Total</span><b>{money(cart.total + fee)}</b></div>
        <small className="gm-total-note">{pickup
          ? (delivery ? 'Pay the rider on delivery: cash, JazzCash or Easypaisa.' : 'Pay when you collect your order.')
          : mode === 'ticket' ? 'This is added to the same order.' : 'You pay at the end. Any tax or service charge shows on your bill.'}</small>
        <button className="gm-cta send" disabled={placing} onClick={send}>
          {placing ? <><span className="spinner" /> Sending to the restaurant</>
            : pickup ? (delivery ? 'Place delivery order' : 'Place takeaway order') : mode === 'ticket' ? 'Add to my order' : 'Place order'}
        </button>
      </div>
    </Sheet>
  )
}

// ---- the customer following a takeaway or delivery ----
const TRACK_TITLE = {
  Received: () => 'Waiting for the restaurant to confirm',
  Confirmed: () => 'Confirmed. The kitchen starts it next',
  Cooking: () => 'Being cooked now',
  Ready: (d) => d ? 'Packed. The rider picks it up next' : 'Ready. Come and collect it',
  OnTheWay: (d, t) => (t.riderName ? `${t.riderName} is on the way` : 'On the way to you'),
  Delivered: () => 'Delivered. Enjoy your meal',
  Collected: () => 'Collected. Enjoy your meal',
  Closed: () => 'This order is closed',
}
const clock = (d) => new Date(d).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })
const minsFrom = (d, now) => Math.round((new Date(d) - now) / 60000)

function TrackCard({ slug, code, onHandedOver }) {
  const [t, setT] = useState(null)
  const [now, setNow] = useState(Date.now())
  useEffect(() => {
    let dead = false, timer
    const tick = async () => {
      let x = null
      if (document.visibilityState !== 'hidden') {
        try { x = await api.trackTicket(slug, code); if (!dead) setT(x) } catch { /* the order page still shows the basics */ }
      }
      // quicker while the rider is on the road, so the dot keeps moving
      if (!dead) timer = setTimeout(tick, x?.stage === 'OnTheWay' && x?.rider ? 4000 : 8000)
    }
    tick()
    const clockTimer = setInterval(() => setNow(Date.now()), 15000)
    return () => { dead = true; clearTimeout(timer); clearInterval(clockTimer) }
  }, [slug, code])
  useEffect(() => { onHandedOver?.(t && ['Delivered', 'Collected'].includes(t.stage) ? t.stage : '') }, [t?.stage])
  if (!t) return null
  const d = t.channel === 'Delivery'
  const done = ['Delivered', 'Collected', 'Closed'].includes(t.stage)
  const steps = d ? [['Received', 'Placed'], ['Cooking', 'Cooking'], ['OnTheWay', 'On the way'], ['Delivered', 'Delivered']]
    : [['Received', 'Placed'], ['Cooking', 'Cooking'], ['Ready', 'Ready'], ['Collected', 'Collected']]
  const rank = { Received: 0, Confirmed: 0, Cooking: 1, Ready: d ? 1 : 2, OnTheWay: 2, Delivered: 4, Collected: 4, Closed: 4 }[t.stage] ?? 0
  const left = t.etaAt ? minsFrom(t.etaAt, now) : null
  const late = t.promisedAt && t.deliveredAt ? minsFrom(t.deliveredAt, new Date(t.promisedAt)) : null
  const points = [t.shop && { ...t.shop, kind: 'shop', label: 'Restaurant' }, t.home && { ...t.home, kind: 'home', label: 'You' },
    t.rider && { ...t.rider, kind: 'rider', label: t.riderName || 'Rider' }].filter(Boolean)
  return (
    <section className={'gm-track s-' + t.stage} aria-live="polite">
      <div className="gm-track-top">
        <span className="gm-track-ico">{d ? (t.stage === 'OnTheWay' ? <BikeIcon size={20} /> : <TruckIcon size={20} />) : <BagIcon size={20} />}</span>
        <div>
          <b>{TRACK_TITLE[t.stage]?.(d, t) || t.stage}</b>
          <small>{ticketLabel(code)}{t.distanceKm != null && d ? ` · ${t.distanceKm.toFixed(1)} km away` : ''}</small>
        </div>
      </div>
      {!done && t.etaAt && (
        <div className="gm-track-eta">
          <span>{d ? 'Arrives by' : 'Ready at'}</span>
          <b>{clock(t.etaAt)}</b>
          <em>{left > 0 ? `in ${left} min` : d ? 'any minute now' : 'now'}</em>
        </div>
      )}
      {!done && !t.etaAt && <p className="gm-track-wait"><ClockIcon size={14} /> The restaurant tells you the time when they confirm.</p>}
      {done && t.deliveredAt && (
        <p className="gm-track-done"><CheckIcon size={15} /> {d ? 'Delivered' : 'Collected'} at {clock(t.deliveredAt)}
          {late != null && (late <= 0 ? <em className="ok">{late < 0 ? `${-late} min early` : 'on time'}</em> : <em>{late} min after the promised time</em>)}</p>
      )}
      <ol className="gm-track-steps">
        {steps.map(([k, label], i) => <li key={k} className={i < rank ? 'done' : i === rank ? 'now' : ''}><i />{label}</li>)}
      </ol>
      {d && points.length > 0 && !done && <MapView className="gm-track-map" height={200} points={points} />}
      {t.promisedAt && !done && <small className="gm-track-promise">Promised by {clock(t.promisedAt)}</small>}
    </section>
  )
}

function PlacedOverlay({ order, where, mode, onDone }) {
  useEffect(() => {
    const t = setTimeout(onDone, 3600)
    return () => clearTimeout(t)
  }, [])
  return (
    <div className="gm-placed" onClick={onDone} role="status">
      <div className="gm-ticket">
        <div className="gm-ticket-tick"><CheckIcon size={30} /></div>
        <h2>Order sent</h2>
        {mode === 'ticket'
          ? <p>Your {where} order #{order.id} has reached the restaurant. They confirm it next.</p>
          : <p>Order #{order.id} for {where} has reached the restaurant.</p>}
        <small>{mode === 'ticket' ? 'Keep this page open to follow it.' : 'You can follow it on the menu screen.'}</small>
      </div>
    </div>
  )
}

// The order QR's two ways, said up front so nobody fills a cart for a delivery that isn't offered.
function PickupWays({ pk }) {
  return (
    <div className="gm-pickways">
      {pk?.takeaway && <div><BagIcon size={18} /><b>Takeaway</b><small>Order now, collect it ready</small></div>}
      {pk?.delivery && <div><TruckIcon size={18} /><b>Delivery</b>
        <small>{Number(pk.deliveryFee) > 0 ? `${money(pk.deliveryFee)} · ` : 'Free · '}{pk.deliveryNote || 'To your door'}</small></div>}
    </div>
  )
}

// ---- Live orders ----
const FLOW = ['Placed', 'Approved', 'Cooking', 'Ready', 'Served']

function LiveOrders({ orders, handedOver, onOpen }) {
  return (
    <section className="gm-live">
      {orders.map((o) => <LiveOrder key={o.id} o={o} handedOver={handedOver} onOpen={onOpen} />)}
    </section>
  )
}

function LiveOrder({ o, handedOver, onOpen }) {
  const [now, setNow] = useState(Date.now())
  useEffect(() => {
    if (o.status !== 'Cooking') return undefined
    const t = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(t)
  }, [o.status])

  let countdown = null
  if (o.status === 'Cooking' && o.acceptedAt) {
    const left = Math.round((new Date(o.acceptedAt).getTime() + o.etaMinutes * 60000 - now) / 1000)
    countdown = left > 0 ? `${String(Math.floor(left / 60)).padStart(2, '0')}:${String(left % 60).padStart(2, '0')}` : 'Finishing up'
  }
  const stage = Math.max(0, FLOW.indexOf(o.status))
  return (
    <button className={`gm-liveorder s-${o.status}`} onClick={() => onOpen(o)}>
      <div className="gm-live-top">
        <b>{handedOver && o.status === 'Served' ? handedOver : statusLabel(o)}</b>
        {countdown ? <span className="gm-count">{countdown}</span> : <span className="gm-live-id">Order #{o.id}</span>}
      </div>
      <div className="gm-live-bar">{FLOW.slice(0, 4).map((s, i) => <i key={s} className={i <= stage ? 'on' : ''} />)}</div>
      <small>{o.items.map((i) => `${i.qty}× ${i.name}${i.variantName ? ` (${i.variantName})` : ''}`).join(', ')}</small>
    </button>
  )
}

// ---- Search ----
function FindView({ show, items, categories, cart, canOrder, onClose, onItem, onPlus, onCategory }) {
  const presence = usePresence(show, 280)
  const [q, setQ] = useState('')
  const input = useRef(null)
  useEffect(() => {
    if (!show) return undefined
    setQ('')
    const t = setTimeout(() => input.current?.focus(), 260)
    return () => clearTimeout(t)
  }, [show])

  const results = useMemo(() => {
    const term = q.trim().toLowerCase()
    if (!term) return []
    return items.filter((it) =>
      it.name.toLowerCase().includes(term) || (it.nameUrdu || '').includes(q.trim()) ||
      (it.description || '').toLowerCase().includes(term) || (it.categoryName || '').toLowerCase().includes(term) ||
      (it.dealDescription || '').toLowerCase().includes(term))
  }, [q, items])

  if (presence === 'gone') return null
  return (
    <div className={`gm-view gm-find ${presence}`}>
      <div className="gm-bar">
        <button className="gm-icon" onClick={onClose} aria-label="Back"><ArrowLeftIcon size={20} /></button>
        <input ref={input} value={q} onChange={(e) => setQ(e.target.value)} placeholder="Biryani, chai, karahi…" enterKeyHint="search" />
        {q && <button className="gm-icon" onClick={() => { setQ(''); input.current?.focus() }} aria-label="Clear"><CloseIcon size={18} /></button>}
      </div>
      <div className="gm-find-body">
        {!q.trim() && (
          <div className="gm-find-cats">
            {categories.map((c) => <button key={c.id} onClick={() => onCategory(c.id)}>{c.name}</button>)}
          </div>
        )}
        {q.trim() && results.length === 0 && (
          <div className="gm-empty"><b>Nothing called "{q.trim()}"</b><span>Try another word, or ask your waiter.</span></div>
        )}
        {results.map((it, i) => (
          <DishRow key={it.id} it={it} i={i} categoryName={it.categoryName} cart={cart} canOrder={canOrder} onItem={onItem} onPlus={onPlus} />
        ))}
        <div className="gm-endspace" />
      </div>
    </div>
  )
}

// A guest on poor signal should know the page is still working, not stuck.
function MenuSkeleton() {
  const [slow, setSlow] = useState(false)
  useEffect(() => {
    const t = setTimeout(() => setSlow(true), 6000)
    return () => clearTimeout(t)
  }, [])
  return (
    <div className="gm"><div className="gm-stage"><div className="gm-view gm-home">
      <header className="gm-hero"><div className="gm-hero-txt"><i className="gm-sk w50" /><i className="gm-sk w70" /></div></header>
      <div className="gm-body">
        <i className="gm-sk gm-sk-search" />
        {slow && <p className="gm-slow" role="status">Taking longer than usual. Still trying…</p>}
        <div className="gm-cats">{[0, 1, 2, 3, 4, 5].map((i) => <i key={i} className="gm-sk gm-sk-cat" style={{ '--i': i }} />)}</div>
      </div>
    </div></div></div>
  )
}
