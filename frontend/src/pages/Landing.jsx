import { useEffect } from 'react'
import { Link } from 'react-router-dom'
import DineInLogo from '../DineInLogo.jsx'
import {
  QrIcon, KitchenIcon, UserIcon, CashIcon, BankIcon, ChartIcon,
  BoxIcon, TableIcon, CheckIcon, PhoneIcon, ReceiptIcon,
} from '../icons.jsx'

// Public marketing homepage — the app's front door. Everything a prospective
// restaurant needs on one page: what it does, the features, how it works, who
// we are, and how to reach us. The two CTAs go to sign-up and sign-in.
const FEATURES = [
  { Icon: QrIcon, title: 'QR self-ordering', text: 'Guests scan the table QR and order from their own phone — no app, no waiting. Orders land at reception instantly.' },
  { Icon: KitchenIcon, title: 'Kitchen display & timing', text: 'Live tickets on a kitchen screen with a per-dish cook countdown, so nothing is late and nothing is forgotten.' },
  { Icon: UserIcon, title: 'Waiter ordering', text: 'Staff take orders tableside on the same live menu. Add more to a running tab any time — it all bills together.' },
  { Icon: CashIcon, title: 'Billing & split payments', text: 'One-tap bills, split evenly or by amount, cash / card / JazzCash / Easypaisa — settled and printed in seconds.' },
  { Icon: BankIcon, title: 'FBR fiscal integration', text: 'File every settled invoice with FBR, print the fiscal QR, and stay compliant — built for Pakistan.' },
  { Icon: ChartIcon, title: 'Dashboard & reports', text: 'Today’s sales, tax breakdown, top items and customer ratings at a glance. Export a CSV for your tax return.' },
  { Icon: BoxIcon, title: 'Inventory & recipes', text: 'Track stock, tie ingredients to dishes, and get low-stock alerts before you run out mid-service.' },
  { Icon: TableIcon, title: 'Multi-branch', text: 'Each location gets its own tables, QR codes and POS registration — with sales reported per branch.' },
]

const STEPS = [
  { n: '1', title: 'Create your restaurant', text: 'Sign up in a minute, add your menu (or import it from a photo), and print your table QR codes.' },
  { n: '2', title: 'Take orders your way', text: 'Guests scan and order, or your waiters take orders tableside. The kitchen sees every ticket live.' },
  { n: '3', title: 'Bill, file & grow', text: 'Settle bills fast, file with FBR automatically, and watch your numbers on the dashboard.' },
]

export default function Landing() {
  // Reveal-on-scroll. We add the hidden class from JS so that, without JS, the
  // content stays fully visible. Honour prefers-reduced-motion.
  useEffect(() => {
    const reduce = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
    const els = [...document.querySelectorAll('.lp-feature, .lp-step, .lp-stat-card, .lp-section-head, .lp-about-copy, .lp-contact-card')]
    if (reduce || !('IntersectionObserver' in window)) return
    els.forEach((el, i) => { el.classList.add('lp-reveal'); el.style.transitionDelay = `${(i % 4) * 70}ms` })
    const io = new IntersectionObserver((entries) => {
      entries.forEach((e) => { if (e.isIntersecting) { e.target.classList.add('rv-in'); io.unobserve(e.target) } })
    }, { threshold: 0.12, rootMargin: '0px 0px -8% 0px' })
    els.forEach((el) => io.observe(el))
    return () => io.disconnect()
  }, [])

  return (
    <div className="landing">
      {/* ---- top nav ---- */}
      <header className="lp-nav">
        <div className="lp-wrap lp-nav-inner">
          <a href="#top" className="lp-brand" aria-label="Dine In home"><DineInLogo /></a>
          <nav className="lp-links">
            <a href="#features">Features</a>
            <a href="#how">How it works</a>
            <a href="#about">About</a>
            <a href="#contact">Contact</a>
          </nav>
          <div className="lp-nav-cta">
            <Link to="/login" className="lp-btn ghost"><UserIcon size={16} /> Sign in</Link>
            <Link to="/signup" className="lp-btn solid">Get started</Link>
          </div>
        </div>
      </header>

      {/* ---- hero ---- */}
      <section className="lp-hero" id="top">
        <div className="lp-blob b1" /><div className="lp-blob b2" />
        <div className="lp-wrap lp-hero-grid">
          <div className="lp-hero-copy">
            <span className="lp-eyebrow">QR ordering · kitchen timing · billing</span>
            <h1 className="lp-h1">The all-in-one POS for modern restaurants.</h1>
            <p className="lp-sub">
              Dine In brings self-ordering, kitchen timing, billing and FBR filing together in one
              beautiful system — so your team moves faster and your guests are happier.
            </p>
            <div className="lp-hero-cta">
              <Link to="/signup" className="lp-btn solid lg">Create your restaurant →</Link>
              <Link to="/login" className="lp-btn ghost lg"><UserIcon size={17} /> Sign in</Link>
            </div>
            <div className="lp-trust">
              <span><CheckIcon size={15} /> No app for guests</span>
              <span><CheckIcon size={15} /> FBR-ready</span>
              <span><CheckIcon size={15} /> Set up in minutes</span>
            </div>
          </div>

          {/* on-brand animated receipt + phone mock */}
          <div className="lp-hero-art" aria-hidden="true">
            <div className="lp-phone float-slow">
              <div className="lp-phone-notch" />
              <div className="lp-phone-screen">
                <div className="lp-menu-head"><DineInLogo /></div>
                <div className="lp-menu-row"><span>Chicken Biryani</span><span>Rs 450</span></div>
                <div className="lp-menu-row"><span>Seekh Kebab</span><span>Rs 380</span></div>
                <div className="lp-menu-row"><span>Chapli Kebab</span><span>Rs 420</span></div>
                <div className="lp-menu-cta">Add to order</div>
              </div>
            </div>
            <div className="lp-receipt float-fast">
              <div className="lp-r-logo"><DineInLogo /></div>
              <div className="lp-r-name">Karachi Biryani House</div>
              <div className="lp-r-line"><span>2× Chicken Biryani</span><span>Rs 900</span></div>
              <div className="lp-r-line"><span>1× Seekh Kebab</span><span>Rs 380</span></div>
              <div className="lp-r-line grand"><span>Total</span><span>Rs 1,280</span></div>
              <div className="lp-r-foot">Integrated with FBR</div>
            </div>
          </div>
        </div>
      </section>

      {/* ---- features ---- */}
      <section className="lp-section" id="features">
        <div className="lp-wrap">
          <div className="lp-section-head">
            <span className="lp-kicker">Everything you need</span>
            <h2 className="lp-h2">One system, front to back of house</h2>
            <p className="lp-lead">From the guest’s first scan to the fiscal invoice — every step of service, built in.</p>
          </div>
          <div className="lp-features">
            {FEATURES.map((f) => (
              <div className="lp-feature" key={f.title}>
                <div className="lp-feature-ico"><f.Icon size={22} /></div>
                <h3>{f.title}</h3>
                <p>{f.text}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* ---- how it works ---- */}
      <section className="lp-section alt" id="how">
        <div className="lp-wrap">
          <div className="lp-section-head">
            <span className="lp-kicker">How it works</span>
            <h2 className="lp-h2">Up and running in three steps</h2>
          </div>
          <div className="lp-steps">
            {STEPS.map((s) => (
              <div className="lp-step" key={s.n}>
                <div className="lp-step-n">{s.n}</div>
                <h3>{s.title}</h3>
                <p>{s.text}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* ---- about ---- */}
      <section className="lp-section" id="about">
        <div className="lp-wrap lp-about">
          <div className="lp-about-copy">
            <span className="lp-kicker">About Dine In</span>
            <h2 className="lp-h2">Built for restaurants in Pakistan</h2>
            <p className="lp-lead">
              We started Dine In to give restaurants a single, modern system that just works —
              no tangle of apps, no expensive hardware. QR ordering, a live kitchen, fast billing
              and FBR filing, all in one place, priced for local kitchens.
            </p>
            <p className="lp-lead">
              Whether you run one café or a chain of branches, Dine In scales with you — with
              per-branch reporting, configurable staff roles, and tools your team actually enjoys using.
            </p>
            <div className="lp-stats">
              <div><b>All-in-one</b><span>ordering → billing</span></div>
              <div><b>FBR</b><span>fiscal-ready</span></div>
              <div><b>Multi-branch</b><span>one dashboard</span></div>
            </div>
          </div>
          <div className="lp-about-art">
            <div className="lp-stat-card"><div className="lp-stat-ico"><ReceiptIcon size={20} /></div><b>Faster tables</b><span>Orders reach the kitchen the moment a guest taps “order”.</span></div>
            <div className="lp-stat-card"><div className="lp-stat-ico"><ChartIcon size={20} /></div><b>Clearer numbers</b><span>Live sales, tax and top-sellers — no spreadsheets.</span></div>
            <div className="lp-stat-card"><div className="lp-stat-ico"><CheckIcon size={20} /></div><b>Less stress</b><span>One tool your whole team learns in a day.</span></div>
          </div>
        </div>
      </section>

      {/* ---- CTA band ---- */}
      <section className="lp-cta-band">
        <div className="lp-wrap lp-cta-inner">
          <div>
            <h2 className="lp-h2" style={{ color: '#fff', margin: 0 }}>Ready to modernise your restaurant?</h2>
            <p style={{ opacity: .9, margin: '8px 0 0' }}>Create your restaurant and print your first QR codes today.</p>
          </div>
          <div className="lp-hero-cta">
            <Link to="/signup" className="lp-btn white lg">Get started free →</Link>
            <Link to="/login" className="lp-btn ghost-light lg">Sign in</Link>
          </div>
        </div>
      </section>

      {/* ---- contact ---- */}
      <section className="lp-section" id="contact">
        <div className="lp-wrap lp-contact">
          <div>
            <span className="lp-kicker">Contact</span>
            <h2 className="lp-h2">Talk to us</h2>
            <p className="lp-lead">Questions, a demo, or help getting set up — we’re happy to help.</p>
            <div className="lp-contact-rows">
              <a className="lp-contact-row" href="mailto:hello@dinein.pk"><span className="lp-c-ico"><ReceiptIcon size={18} /></span><div><b>Email</b><span>hello@dinein.pk</span></div></a>
              <a className="lp-contact-row" href="tel:+920000000000"><span className="lp-c-ico"><PhoneIcon size={18} /></span><div><b>Phone</b><span>+92 000 0000000</span></div></a>
              <div className="lp-contact-row"><span className="lp-c-ico"><TableIcon size={18} /></span><div><b>Where</b><span>Lahore, Pakistan</span></div></div>
            </div>
          </div>
          <div className="lp-contact-card">
            <h3 style={{ marginTop: 0 }}>Start today</h3>
            <p className="lp-lead" style={{ marginTop: 4 }}>Set up your restaurant in a few minutes — no card required.</p>
            <Link to="/signup" className="lp-btn solid lg" style={{ width: '100%', justifyContent: 'center' }}>Create your restaurant</Link>
            <div className="lp-contact-signin">
              Already have an account? <Link to="/login">Sign in →</Link>
            </div>
          </div>
        </div>
      </section>

      {/* ---- footer ---- */}
      <footer className="lp-footer">
        <div className="lp-wrap lp-footer-inner">
          <div className="lp-foot-brand"><DineInLogo /><span>QR ordering, kitchen timing &amp; billing — all in one.</span></div>
          <div className="lp-foot-links">
            <a href="#features">Features</a>
            <a href="#how">How it works</a>
            <a href="#about">About</a>
            <a href="#contact">Contact</a>
            <Link to="/login">Sign in</Link>
            <Link to="/signup">Get started</Link>
          </div>
          <div className="lp-foot-copy">© {new Date().getFullYear()} Dine In. All rights reserved.</div>
        </div>
      </footer>
    </div>
  )
}
