import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import DineInLogo from '../DineInLogo.jsx'
import Diorama, { Phone3D, PhoneScreen } from '../Diorama.jsx'
import { CONTACT } from '../contact'
import { getPlans, money } from '../api'
import { tilt, useParallax } from '../motion'
import { BookIcon, ChatIcon, CheckIcon, PhoneIcon } from '../icons.jsx'

// What a plan includes, in the words an owner would use.
const PLAN_LINES = [
  [null, 'QR menu and table QR codes'],
  ['ordering', 'Guests and staff place orders'],
  ['kitchen', 'Kitchen screen with timers'],
  ['waiter', 'Waiter screen on any phone'],
  ['billing', 'Bills, split payments, parcels, deliveries, customer khata, day closing'],
  ['inventory', 'Stock, recipes and supplier khata'],
  ['reports', 'Reports, expenses, salaries and attendance'],
]

const EVERYTHING = [
  'Menu in English and Urdu', 'Sizes, add-ons and deals', 'Read your menu from a photo', 'Change all prices by a percentage',
  'Guests call a waiter from the table', 'Shift a table, join two bills', 'Remove an item, with a reason on record', 'Parcel and delivery tickets',
  'Saved customers by mobile number', 'Rider name and delivery charge', 'Split a bill any way', 'Cash, card, JazzCash, Easypaisa',
  'Separate tax rate for card and cash', 'Discounts only for staff you allow', 'Customer khata with limits', 'Supplier khata with statements',
  'Purchases add to stock', 'Recipes take stock off by themselves', 'Waste recorded with a reason', 'Expenses by category',
  'Salaries, advances, attendance', 'Night-time cash count', 'Sales by dish, hour, table, cashier and waiter', 'Excel and print on every report',
  'Rewards and points for regulars', 'Several branches', 'Each staff member sees only their screens', 'Receipts on your thermal printer',
]

const FAQ = [
  ['Do guests need to install anything?', 'No. They point the phone camera at the QR code on the table and the menu opens in the browser.'],
  ['Do I need new hardware?', "No. It runs on whatever phone, tablet or computer you have. Bills and kitchen slips print on a normal thermal receipt printer connected to that device."],
  ['Can a mistake put an order on the wrong table?', 'Each QR code belongs to one table, and the server works out every price itself. Staff can shift a table or join two bills, and each change stays on record.'],
  ['What happens if the internet goes down?', "Dine In runs online, so it needs a connection. A phone hotspot is enough to keep going. Your data is backed up every day."],
  ['Does it work with FBR?', "It's built to send invoices to FBR's POS system. Talk to us and we'll connect it with your own FBR registration."],
  ['How do I get my menu in?', 'Type it, paste it from a spreadsheet, or photograph your printed menu and let it read the dishes and prices for you to check.'],
]

function useReveal() {
  const root = useRef(null)
  useEffect(() => {
    const els = [...root.current.querySelectorAll('[data-rise]')]
    if (!('IntersectionObserver' in window) || window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) return undefined
    els.forEach((el) => el.classList.add('ln-pre'))
    const io = new IntersectionObserver((entries) => {
      entries.forEach((e) => { if (e.isIntersecting) { e.target.classList.add('ln-in'); io.unobserve(e.target) } })
    }, { threshold: 0.14, rootMargin: '0px 0px -6% 0px' })
    els.forEach((el) => io.observe(el))
    return () => io.disconnect()
  }, [])
  return root
}

// ---- pictures of the product, drawn with the same parts the product is made of ----
function FloorPicture() {
  const tiles = [
    ['Table 1', 'Free', '', 'free'], ['Table 2', 'New order', 'Rs 1,250', 'new'], ['Table 3', 'In kitchen', 'Rs 2,480', 'cooking'], ['Table 4', 'Food ready', 'Rs 980', 'ready'],
    ['Table 5', 'Eating', 'Rs 3,120', 'served'], ['Table 6', 'Free', '', 'free'], ['Parcel 3', 'In kitchen', 'Rs 640', 'cooking'], ['Delivery 2', 'With rider', 'Rs 1,870', 'served'],
  ]
  return (
    <div className="ln-device" {...tilt(5)}>
      <div className="ln-device-bar"><i /><i /><i /><span>Reception</span></div>
      <div className="ln-floor">
        {tiles.map(([name, state, amount, kind]) => (
          <div key={name} className={`ln-tile is-${kind}`}><b>{name}</b><small>{state}</small>{amount && <span>{amount}</span>}</div>
        ))}
      </div>
    </div>
  )
}

function KitchenPicture() {
  return (
    <div className="ln-tickets" {...tilt(6)}>
      <div className="ln-ticket t1"><b>Table 3</b><span className="ln-time">12 min</span><p>2 × Chicken Karahi (half)</p><p>4 × Naan</p></div>
      <div className="ln-ticket t2"><b>Parcel 3</b><span className="ln-time">6 min</span><p>1 × Chicken Biryani</p><p>1 × Raita</p></div>
      <div className="ln-ticket t3"><b>Table 5</b><span className="ln-time ok">Ready</span><p>3 × Seekh Kabab</p></div>
    </div>
  )
}

function KhataPicture() {
  const rows = [['2 Nov', 'Bill 1187, chicken 40 kg', 'Rs 26,000', '', 'Rs 26,000'], ['5 Nov', 'Paid by cash', '', 'Rs 15,000', 'Rs 11,000'], ['8 Nov', 'Bill 1204, chicken 30 kg', 'Rs 19,500', '', 'Rs 30,500']]
  return (
    <div className="ln-device" {...tilt(5)}>
      <div className="ln-device-bar"><i /><i /><i /><span>Ali Poultry</span></div>
      <div className="ln-khata">
        <div className="ln-owe"><small>You owe</small><b>Rs 30,500</b></div>
        <table>
          <thead><tr><th>Date</th><th>Detail</th><th>Bought</th><th>Paid</th><th>Balance</th></tr></thead>
          <tbody>{rows.map((r) => <tr key={r[0]}>{r.map((c, i) => <td key={i}>{c}</td>)}</tr>)}</tbody>
        </table>
      </div>
    </div>
  )
}

function ReportPicture() {
  const bars = [['Chicken Karahi', 100, 'Rs 84,200'], ['Chicken Biryani', 72, 'Rs 60,600'], ['Seekh Kabab', 51, 'Rs 43,100'], ['Mutton Handi', 34, 'Rs 28,700']]
  return (
    <div className="ln-device" {...tilt(5)}>
      <div className="ln-device-bar"><i /><i /><i /><span>Reports</span></div>
      <div className="ln-report">
        <div className="ln-figs">
          <div className="lead"><small>Sales</small><b>Rs 4,18,500</b></div>
          <div><small>Expenses</small><b>Rs 96,200</b></div>
          <div><small>Estimated profit</small><b>Rs 1,12,300</b></div>
        </div>
        <div className="ln-bars">
          {bars.map(([name, width, value]) => (
            <div key={name}><span>{name}</span><i><em style={{ width: `${width}%` }} /></i><b>{value}</b></div>
          ))}
        </div>
      </div>
    </div>
  )
}

function Row({ flip, kicker, title, children, picture }) {
  return (
    <section className={`ln-row${flip ? ' flip' : ''}`}>
      <div className="ln-row-text" data-rise>
        <span className="ln-kicker">{kicker}</span>
        <h2>{title}</h2>
        {children}
      </div>
      <div className="ln-row-pic" data-rise>{picture}</div>
    </section>
  )
}

export default function Landing() {
  const root = useReveal()
  const hero = useParallax()
  const [plans, setPlans] = useState(null)
  useEffect(() => { getPlans().then(setPlans).catch(() => setPlans([])) }, [])

  return (
    <div className="ln" ref={root}>
      <header className="ln-nav">
        <Link to="/" className="ln-logo" aria-label="Dine In home"><DineInLogo /></Link>
        <nav>
          <a href="#how">How it works</a>
          <a href="#features">What's included</a>
          <a href="#pricing">Pricing</a>
          <a href="#contact">Contact</a>
        </nav>
        <Link to="/login" className="ln-signin">Sign in</Link>
      </header>

      <section className="ln-hero" ref={hero} id="top">
        <div className="ln-hero-light" />
        <div className="ln-hero-grid">
          <div className="ln-hero-text">
            <h1>Your whole restaurant, <em>one screen.</em></h1>
            <p>Guests order from the table, the kitchen sees it at once, and bills, khata, expenses and reports keep themselves up to date.</p>
            <div className="ln-cta">
              <Link to="/signup" className="ln-btn primary">Start free for 14 days</Link>
              <a href={CONTACT.whatsapp} target="_blank" rel="noreferrer" className="ln-btn quiet"><ChatIcon size={17} /> Ask on WhatsApp</a>
            </div>
            <small>No card needed. Set up in a few minutes. Made for restaurants in Pakistan.</small>
          </div>
          <Diorama name="Karachi Grill" />
        </div>
      </section>

      <section className="ln-journey" id="how">
        <h2 data-rise>One order, from table to till</h2>
        <ol>
          {[
            ['Guest scans', 'The menu for their table opens on their phone. They choose and send.'],
            ['Reception approves', 'It appears on your floor with a chime. One tap sends it to the kitchen.'],
            ['Kitchen cooks', 'The ticket shows on the kitchen screen with a timer. The waiter hears when it\'s ready.'],
            ['Bill settles', 'Split it, take cash, card or JazzCash, or put it on khata. Sales and stock update themselves.'],
          ].map(([title, text], i) => (
            <li key={title} data-rise style={{ transitionDelay: `${i * 90}ms` }} {...tilt(7)}>
              <span>{i + 1}</span><b>{title}</b><p>{text}</p>
            </li>
          ))}
        </ol>
      </section>

      <div className="ln-rows">
        <Row kicker="For your guests" title="A menu people enjoy opening"
          picture={<div className="ln-phone" {...tilt(8)}><Phone3D><PhoneScreen name="Karachi Grill" /></Phone3D></div>}>
          <p>It opens on your categories, like the pages of a good menu. One tap goes in, a swipe moves to the next, and the phone's back button walks back out.</p>
          <p>Today's deals and the most ordered dishes come first, and a suggestion appears before they send the order. It's quick to choose, and easy to add one more thing.</p>
        </Row>

        <Row flip kicker="At the counter" title="Every table at a glance"
          picture={<FloorPicture />}>
          <p>See who's waiting, what's cooking and what's ready, with the running bill on each table. Take a parcel or delivery from the same screen, with the customer found by their mobile number.</p>
          <p>Shift a table, join two bills, remove an item with a reason, send the address to your rider on WhatsApp. Guests can call a waiter or ask for the bill from their phone.</p>
        </Row>

        <Row kicker="In the kitchen" title="Fewer shouted orders"
          picture={<KitchenPicture />}>
          <p>Tickets arrive on a screen as soon as they're approved, oldest first, each with its own timer. Tap when it's ready and the waiter's phone chimes.</p>
          <p>Prefer paper? Kitchen slips print on your thermal printer too.</p>
        </Row>

        <Row flip kicker="The khata" title="The khata you already keep, kept for you"
          picture={<KhataPicture />}>
          <p>Every supplier bill and payment, with the balance worked out on each line. Bills add to your stock. Send a statement on WhatsApp in one tap.</p>
          <p>Offices and regulars get a customer khata too, with a limit you set, and a reminder when it's time to pay.</p>
        </Row>

        <Row kicker="The numbers" title="Know what you actually made"
          picture={<ReportPicture />}>
          <p>Sales by dish, hour, table, cashier and waiter. Expenses by category. Salaries and advances. Cancelled orders and discounts, and who gave them. Every report prints or opens in Excel.</p>
          <p>At night, count the drawer and Dine In tells you if it's short.</p>
        </Row>
      </div>

      <section className="ln-all" id="features">
        <h2 data-rise>What's included</h2>
        <ul data-rise>
          {EVERYTHING.map((f) => <li key={f}><CheckIcon size={15} /> {f}</li>)}
        </ul>
      </section>

      <section className="ln-pricing" id="pricing">
        <h2 data-rise>Pricing</h2>
        <p className="ln-pricing-sub" data-rise>Every plan starts with 14 days free. Pay monthly, and a one-time setup when you decide to stay.</p>
        {plans?.length > 0 ? (
          <div className="ln-plans">
            {plans.map((p, i) => {
              const top = i === plans.length - 1
              return (
                <div key={p.id} className={`ln-plan${top ? ' top' : ''}`} data-rise style={{ transitionDelay: `${i * 70}ms` }}>
                  {top && <span className="ln-plan-tag">Everything</span>}
                  <h3>{p.name}</h3>
                  <div className="ln-price">{money(p.pricePkr)}<small>/month</small></div>
                  {p.setupFeePkr > 0 && <div className="ln-setup">{money(p.setupFeePkr)} one-time setup</div>}
                  <ul>{PLAN_LINES.filter(([k]) => !k || p[k]).map(([k, label]) => <li key={label}><CheckIcon size={14} /> {label}</li>)}</ul>
                  <Link to="/signup" className={`ln-btn ${top ? 'primary' : 'outline'}`}>Start free</Link>
                </div>
              )
            })}
          </div>
        ) : plans && <p className="ln-pricing-sub">Call us on {CONTACT.phone} for today's prices.</p>}
      </section>

      <section className="ln-faq">
        <h2 data-rise>Questions owners ask</h2>
        <div data-rise>
          {FAQ.map(([q, a]) => <details key={q}><summary>{q}</summary><p>{a}</p></details>)}
        </div>
      </section>

      <section className="ln-contact" id="contact">
        <div data-rise>
          <h2>See it with your own menu</h2>
          <p>Send us your menu and we'll help you set it up. Or start on your own, it only takes a few minutes.</p>
          <div className="ln-cta">
            <Link to="/signup" className="ln-btn primary">Start free for 14 days</Link>
            <a href={CONTACT.whatsapp} target="_blank" rel="noreferrer" className="ln-btn quiet"><ChatIcon size={17} /> WhatsApp</a>
            <a href={`tel:${CONTACT.tel}`} className="ln-btn quiet"><PhoneIcon size={17} /> {CONTACT.phone}</a>
            <a href={`mailto:${CONTACT.email}`} className="ln-btn quiet"><BookIcon size={17} /> {CONTACT.email}</a>
          </div>
        </div>
      </section>

      <footer className="ln-foot">
        <DineInLogo />
        <span>© {new Date().getFullYear()} Dine In</span>
        <Link to="/login">Sign in</Link>
      </footer>
    </div>
  )
}
