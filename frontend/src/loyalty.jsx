import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { api, loyalty, memberStore, money } from './api'
import { toast, ui } from './ui.jsx'
import { StarIcon, CloseIcon, CheckIcon, PhoneIcon, UserIcon } from './icons.jsx'

// Guest side of Loyalty & Reviews (the table's QR page). Everything here is additive: when the
// restaurant hasn't switched rewards on, only the Google review card can show (if links are set).

const reduceMotion = () => typeof window !== 'undefined' && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
const pts = (n) => `${Number(n || 0).toLocaleString('en-PK')} pts`
const RATED_KEY = (slug) => `qrdine.rated.${slug}`

/** Programme info + this phone's member, with join / link / refresh. */
export function useLoyalty(slug, code) {
  const [info, setInfo] = useState(null)
  const [me, setMe] = useState(null)
  const meRef = useRef(null)
  meRef.current = me

  const refresh = useCallback(async () => {
    if (!memberStore.get(slug)) { setMe(null); return null }
    try {
      const m = await loyalty.me(slug)
      if (!m) memberStore.clear(slug)   // the member was removed: forget it on this phone
      setMe(m)
      return m
    } catch { return meRef.current }
  }, [slug])

  useEffect(() => {
    loyalty.info(slug).then(setInfo).catch(() => setInfo(null))
    refresh()
  }, [slug, refresh])

  const join = async (name, phone) => {
    const m = await loyalty.join(slug, { name, phone, tableCode: code })
    memberStore.set(slug, m.token)
    setMe(m)
    return m
  }
  // Tie this table's running bill to the member (after an order, so the seating exists).
  const link = useCallback(() => {
    if (memberStore.get(slug)) loyalty.link(slug, code).catch(() => {})
  }, [slug, code])
  const forget = () => { memberStore.clear(slug); setMe(null) }

  return { info, me, setMe, refresh, join, link, forget, enabled: !!info?.enabled }
}

/** Counts from the last shown value to the new one (no animation for reduced motion). */
export function CountUp({ value, duration = 900 }) {
  const [shown, setShown] = useState(value || 0)
  const from = useRef(value || 0)
  useEffect(() => {
    const start = from.current, end = value || 0
    if (start === end || reduceMotion()) { setShown(end); from.current = end; return }
    let raf, t0
    const step = (t) => {
      t0 = t0 ?? t
      const k = Math.min(1, (t - t0) / duration)
      const eased = 1 - Math.pow(1 - k, 3)
      setShown(Math.round(start + (end - start) * eased))
      if (k < 1) raf = requestAnimationFrame(step)
      else from.current = end
    }
    raf = requestAnimationFrame(step)
    // frames pause in background tabs: make sure the number still lands on the right value
    const done = setTimeout(() => { cancelAnimationFrame(raf); setShown(end); from.current = end }, duration + 150)
    return () => { cancelAnimationFrame(raf); clearTimeout(done) }
  }, [value, duration])
  return <>{shown.toLocaleString('en-PK')}</>
}

/** A ring that fills to `pct` (0–1) with a smooth sweep. */
function Ring({ pct, size = 112, stroke = 9, children }) {
  const r = (size - stroke) / 2
  const c = 2 * Math.PI * r
  const [p, setP] = useState(0)
  useEffect(() => {
    const target = Math.max(0, Math.min(1, pct || 0))
    const t = setTimeout(() => setP(target), 30)   // after first paint, so the sweep animates
    return () => clearTimeout(t)
  }, [pct])
  return (
    <div className="lx-ring" style={{ width: size, height: size }}>
      <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} aria-hidden="true">
        <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke="rgba(255,255,255,.22)" strokeWidth={stroke} />
        <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke="#fff" strokeWidth={stroke} strokeLinecap="round"
          strokeDasharray={c} strokeDashoffset={c * (1 - p)} transform={`rotate(-90 ${size / 2} ${size / 2})`}
          style={{ transition: 'stroke-dashoffset 1.1s cubic-bezier(.2,.8,.2,1)' }} />
      </svg>
      <div className="lx-ring-mid">{children}</div>
    </div>
  )
}

/** A short burst of confetti from the middle of the screen (skipped for reduced motion). */
export function burst() {
  if (reduceMotion() || typeof document === 'undefined') return
  const host = document.createElement('div')
  host.className = 'lx-confetti'
  const colors = ['#a81d48', '#e0a500', '#17915a', '#3667cc', '#f08bab', '#ffffff']
  for (let i = 0; i < 42; i++) {
    const s = document.createElement('i')
    const angle = Math.random() * Math.PI * 2
    const dist = 120 + Math.random() * 220
    s.style.setProperty('--x', `${Math.cos(angle) * dist}px`)
    s.style.setProperty('--y', `${Math.sin(angle) * dist - 120}px`)
    s.style.setProperty('--r', `${Math.random() * 720 - 360}deg`)
    s.style.background = colors[i % colors.length]
    s.style.animationDelay = `${Math.random() * 0.12}s`
    host.appendChild(s)
  }
  document.body.appendChild(host)
  setTimeout(() => host.remove(), 1700)
}

/** The next reward this balance is working towards, and how close it is. */
function nextGoal(rewards, available) {
  const list = (rewards || []).filter((r) => r.active !== false).sort((a, b) => a.pointsCost - b.pointsCost)
  const next = list.find((r) => r.pointsCost > available)
  const ready = list.filter((r) => r.pointsCost <= available)
  if (!next) return { next: null, pct: list.length ? 1 : 0, ready }
  const prev = [...list].reverse().find((r) => r.pointsCost <= available)?.pointsCost ?? 0
  return { next, pct: (available - prev) / Math.max(1, next.pointsCost - prev), toGo: next.pointsCost - available, ready }
}

// ───────────────────────── page pieces ─────────────────────────

/** A strip under the menu header: join, or the balance and the next reward. Opens the hub. */
export function RewardsStrip({ loy, onOpen }) {
  const { info, me, enabled } = loy
  const [bump, setBump] = useState(false)
  const lastPts = useRef(me?.points)
  useEffect(() => {
    if (me && lastPts.current != null && me.points !== lastPts.current) { setBump(true); setTimeout(() => setBump(false), 700) }
    lastPts.current = me?.points
  }, [me?.points])
  if (!enabled) return null
  const goal = me ? nextGoal(info.rewards, me.availablePoints) : null
  return (
    <button className={'lx-strip' + (bump ? ' lx-bump' : '')} onClick={onOpen}>
      <span className="lx-strip-ico" aria-hidden="true"><StarIcon size={18} /></span>
      {me ? (
        <span className="lx-strip-txt">
          <b><CountUp value={me.points} /> points</b>
          <span>{goal?.next ? `${goal.toGo.toLocaleString('en-PK')} more for ${goal.next.emoji} ${goal.next.title}` : goal?.ready?.length ? 'You can claim a reward now!' : info.name}</span>
        </span>
      ) : (
        <span className="lx-strip-txt">
          <b>Join {info.name}</b>
          <span>{info.welcomeBonus > 0 ? `Get ${pts(info.welcomeBonus)} free, and earn on every bill` : 'Earn points on every bill'}</span>
        </span>
      )}
      <span className="lx-strip-go" aria-hidden="true">›</span>
    </button>
  )
}

/** "Loved it? Review us on Google" — shown to every guest (never only to happy ones, never paid with points). */
export function GoogleReviewCard({ info }) {
  if (!info?.googleReviewUrl && !info?.googleMapsUrl) return null
  return (
    <div className="card lx-google fade-up">
      <div className="lx-google-head">
        <span className="lx-g" aria-hidden="true">G</span>
        <div>
          <b>Enjoyed your visit?</b>
          <div className="muted" style={{ fontSize: 13 }}>A Google review helps other diners find us.</div>
        </div>
      </div>
      <div className="row" style={{ gap: 8, flexWrap: 'wrap', marginTop: 10 }}>
        {info.googleReviewUrl && (
          <a className="lx-btn lx-btn-solid" href={info.googleReviewUrl} target="_blank" rel="noopener noreferrer">
            <StarIcon size={15} /> Review us on Google
          </a>
        )}
        {info.googleMapsUrl && (
          <a className="lx-btn lx-btn-ghost" href={info.googleMapsUrl} target="_blank" rel="noopener noreferrer">See our Google reviews</a>
        )}
      </div>
    </div>
  )
}

// ───────────────────────── the hub (bottom sheet) ─────────────────────────

/** Rewards & Reviews in one place: join, balance, rewards, codes, history, ratings and Google links. */
export function RewardsHub({ loy, slug, code, open, onClose, onRated }) {
  const [tab, setTab] = useState('rewards')
  const { info, me } = loy

  // While a reward waits for staff, keep the code screen up to date.
  const waiting = me?.redemptions?.some((r) => r.status === 'Pending')
  useEffect(() => {
    if (!open || !waiting) return
    const id = setInterval(async () => {
      const before = me?.redemptions?.filter((r) => r.status === 'Pending').map((r) => r.id) || []
      const m = await loy.refresh()
      const approved = m?.redemptions?.find((r) => before.includes(r.id) && r.status === 'Approved')
      if (approved) { burst(); toast.success(`${approved.rewardTitle} approved. Enjoy!`) }
    }, 6000)
    return () => clearInterval(id)
  }, [open, waiting])

  useEffect(() => { if (open) loy.refresh() }, [open])
  useEffect(() => {
    if (!open) return
    const onKey = (e) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open])

  if (!open || !info) return null
  return (
    <div className="lx-overlay" onClick={(e) => { if (e.target === e.currentTarget) onClose() }}>
      <div className="lx-sheet" role="dialog" aria-modal="true" aria-label={`${info.name} and reviews`}>
        <div className="lx-grab" aria-hidden="true" />
        <div className="row between" style={{ marginBottom: 8 }}>
          <div className="seg">
            {info.enabled && <button className={tab === 'rewards' ? 'on' : ''} onClick={() => setTab('rewards')}>{info.name}</button>}
            <button className={tab === 'reviews' || !info.enabled ? 'on' : ''} onClick={() => setTab('reviews')}>Reviews</button>
          </div>
          <button className="icon-btn" onClick={onClose} aria-label="Close"><CloseIcon size={18} /></button>
        </div>
        {info.enabled && tab === 'rewards'
          ? (me ? <MemberView loy={loy} slug={slug} code={code} /> : <JoinView loy={loy} />)
          : <ReviewsView info={info} slug={slug} code={code} loy={loy} onRated={onRated} />}
      </div>
    </div>
  )
}

function JoinView({ loy }) {
  const { info } = loy
  const [name, setName] = useState('')
  const [phone, setPhone] = useState('')
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')
  const submit = async (e) => {
    e.preventDefault()
    setErr('')
    if (phone.replace(/\D/g, '').length < 10) { setErr('Enter your mobile number, e.g. 0300 1234567.'); return }
    setBusy(true)
    try {
      const m = await loy.join(name.trim(), phone)
      if (m.justJoined) {
        burst()
        toast.success(m.welcomeBonusAwarded > 0 ? `Welcome! ${pts(m.welcomeBonusAwarded)} added` : 'Welcome to rewards!')
      } else toast.success(`Welcome back, ${m.name}!`)
    } catch (ex) { setErr(ex.message || 'Could not join right now.') }
    setBusy(false)
  }
  const sample = info.perAmount > 0 ? Math.round(info.perAmount * 20) : 2000
  return (
    <div className="stagger">
      <div className="lx-hero lx-hero-join">
        <div className="lx-hero-kicker">{info.name}</div>
        <div className="lx-hero-title">{info.tagline || 'Earn points on every visit.'}</div>
        <div className="lx-perks">
          <span><StarIcon size={14} /> {info.earnPoints} pt{info.earnPoints === 1 ? '' : 's'} per {money(info.perAmount)}</span>
          {info.welcomeBonus > 0 && <span>{pts(info.welcomeBonus)} welcome gift</span>}
          {info.feedbackBonus > 0 && <span>{pts(info.feedbackBonus)} for rating us</span>}
        </div>
        <div className="lx-hero-note">A {money(sample)} bill earns {Math.floor(sample / info.perAmount) * info.earnPoints} points.</div>
      </div>

      <form onSubmit={submit} className="lx-join">
        <label className="field">
          <span className="field-label">Your name</span>
          <div className="lx-input"><UserIcon size={16} /><input value={name} maxLength={60} onChange={(e) => setName(e.target.value)} placeholder="e.g. Ayesha" autoComplete="name" /></div>
        </label>
        <label className="field">
          <span className="field-label">Mobile number</span>
          <div className="lx-input"><PhoneIcon size={16} /><input value={phone} inputMode="tel" maxLength={20} onChange={(e) => setPhone(e.target.value)} placeholder="0300 1234567" autoComplete="tel" /></div>
        </label>
        {err && <div className="field-error">{err}</div>}
        <button className="primary lx-cta" disabled={busy}>{busy ? 'Joining…' : info.welcomeBonus > 0 ? `Join & get ${pts(info.welcomeBonus)}` : 'Join rewards'}</button>
        <div className="muted" style={{ fontSize: 12, textAlign: 'center', marginTop: 8 }}>
          Already a member? Enter the same number to see your points. Rewards are confirmed by our staff.
        </div>
      </form>

      {info.rewards?.length > 0 && (
        <>
          <div className="lx-sub">Rewards you can unlock</div>
          <div className="lx-rewards">
            {info.rewards.map((r) => (
              <div className="lx-reward locked" key={r.id}>
                <span className="lx-emoji">{r.emoji}</span>
                <span className="lx-reward-txt"><b>{r.title}</b>{r.description && <span>{r.description}</span>}</span>
                <span className="lx-cost">{pts(r.pointsCost)}</span>
              </div>
            ))}
          </div>
        </>
      )}
    </div>
  )
}

function MemberView({ loy, slug, code }) {
  const { info, me } = loy
  const [busyId, setBusyId] = useState(null)
  const goal = useMemo(() => nextGoal(info.rewards, me.availablePoints), [info.rewards, me.availablePoints])
  const pending = me.redemptions.filter((r) => r.status === 'Pending')
  const recentDone = me.redemptions.filter((r) => r.status !== 'Pending').slice(0, 3)

  const redeem = async (r) => {
    const ok = await ui.confirm({ title: r.title, message: `Use ${pts(r.pointsCost)} for this reward? You'll get a code to show our staff. Points come off when they confirm it.`, confirmText: 'Get my code' })
    if (!ok) return
    setBusyId(r.id)
    try {
      await loyalty.redeem(slug, r.id, code)
      await loy.refresh()
      toast.success('Your code is ready. Show it to our staff.')
      document.querySelector('.lx-ticket')?.scrollIntoView({ behavior: 'smooth', block: 'center' })
    } catch (ex) { toast.error(ex.message || 'Could not request this reward.') }
    setBusyId(null)
  }
  const cancel = async (red) => {
    if (!(await ui.confirm({ message: 'Cancel this reward request? Your points stay with you.' }))) return
    try { await loyalty.cancel(slug, red.id); await loy.refresh() } catch (ex) { toast.error(ex.message) }
  }

  return (
    <div className="stagger">
      <div className="lx-hero">
        <div className="lx-hero-row">
          <Ring pct={goal.pct}>
            <div className="lx-ring-num"><CountUp value={me.points} /></div>
            <div className="lx-ring-lbl">points</div>
          </Ring>
          <div style={{ minWidth: 0 }}>
            <div className="lx-hero-kicker">{info.name}</div>
            <div className="lx-hero-name">Hi, {me.name || 'there'}</div>
            <div className="lx-hero-phone">{me.phoneMasked}</div>
            <div className="lx-hero-goal">
              {goal.next ? <>{goal.toGo.toLocaleString('en-PK')} more for <b>{goal.next.emoji} {goal.next.title}</b></>
                : goal.ready.length ? <b>You can claim a reward now!</b> : 'Keep earning on every bill.'}
            </div>
          </div>
        </div>
        <div className="lx-stats">
          <span><b>{me.visits}</b> visit{me.visits === 1 ? '' : 's'}</span>
          <span><b>{me.lifetimePoints.toLocaleString('en-PK')}</b> earned</span>
          {me.availablePoints !== me.points && <span><b>{me.availablePoints.toLocaleString('en-PK')}</b> free to use</span>}
        </div>
      </div>

      {pending.map((red) => (
        <div className="lx-ticket" key={red.id}>
          <div className="lx-ticket-top">
            <span className="lx-emoji">{red.rewardEmoji}</span>
            <div style={{ minWidth: 0 }}>
              <b>{red.rewardTitle}</b>
              <div className="lx-wait"><span className="lx-dot" /> Show this code to our staff</div>
            </div>
          </div>
          <div className="lx-code" aria-label={`Code ${red.code.split('').join(' ')}`}>{red.code}</div>
          <div className="row between" style={{ fontSize: 12.5 }}>
            <span className="muted">{pts(red.pointsCost)} · waiting for staff</span>
            <button className="ghost btn-sm" onClick={() => cancel(red)}>Cancel</button>
          </div>
        </div>
      ))}

      <div className="lx-sub">Rewards</div>
      {info.rewards?.length ? (
        <div className="lx-rewards">
          {info.rewards.map((r) => {
            const can = me.availablePoints >= r.pointsCost
            const pct = Math.min(1, me.availablePoints / Math.max(1, r.pointsCost))
            return (
              <div className={'lx-reward' + (can ? ' ready' : ' locked')} key={r.id}>
                <span className="lx-emoji">{r.emoji}</span>
                <span className="lx-reward-txt">
                  <b>{r.title}</b>
                  {r.description && <span>{r.description}</span>}
                  {!can && <span className="lx-bar"><i style={{ width: `${pct * 100}%` }} /></span>}
                </span>
                {can ? (
                  <button className="primary btn-sm lx-redeem" disabled={busyId === r.id} onClick={() => redeem(r)}>
                    {busyId === r.id ? '…' : `Redeem · ${r.pointsCost.toLocaleString('en-PK')}`}
                  </button>
                ) : <span className="lx-cost">{pts(r.pointsCost)}</span>}
              </div>
            )
          })}
        </div>
      ) : <div className="muted" style={{ fontSize: 13 }}>Rewards are coming soon. Your points are safe.</div>}

      {recentDone.length > 0 && (
        <>
          <div className="lx-sub">Your rewards</div>
          {recentDone.map((r) => (
            <div className="lx-hist" key={r.id}>
              <span>{r.rewardEmoji} {r.rewardTitle}</span>
              <span className={'lx-badge ' + r.status.toLowerCase()}>{r.status === 'Approved' ? 'Enjoyed' : r.status}</span>
            </div>
          ))}
        </>
      )}

      {me.history?.length > 0 && (
        <>
          <div className="lx-sub">Points history</div>
          <div className="lx-history">
            {me.history.map((t) => (
              <div className="lx-hist" key={t.id}>
                <span>
                  {TXN_LABEL[t.type] || t.type}
                  {t.type === 'Earn' && t.billAmount > 0 ? ` · ${money(t.billAmount)} bill` : ''}
                  {t.note && (t.type === 'Redeem' || t.type === 'Adjust') ? ` · ${t.note}` : ''}
                  <small>{new Date(t.createdAt).toLocaleDateString('en-PK', { day: 'numeric', month: 'short' })}</small>
                </span>
                <b className={t.points >= 0 ? 'plus' : 'minus'}>{t.points >= 0 ? '+' : ''}{t.points.toLocaleString('en-PK')}</b>
              </div>
            ))}
          </div>
        </>
      )}

      <button className="ghost lx-forget" onClick={async () => { if (await ui.confirm({ message: 'Use a different number on this phone? Your points stay on your number.' })) loy.forget() }}>
        Not you? Use another number
      </button>
    </div>
  )
}

const TXN_LABEL = { Welcome: 'Welcome gift', Earn: 'Visit', Feedback: 'Rating bonus', Redeem: 'Reward', Adjust: 'Adjustment' }

function ReviewsView({ info, slug, code, loy, onRated }) {
  const r = info.reviews || { average: 0, count: 0 }
  return (
    <div className="stagger">
      <div className="lx-score">
        <div className="lx-score-num">{r.count ? r.average.toFixed(1) : '—'}</div>
        <div>
          <div className="lx-stars" aria-label={`${r.average} out of 5`}>
            {[1, 2, 3, 4, 5].map((n) => (
              <span key={n} className={n <= Math.round(r.average) ? 'on' : ''}><StarIcon size={18} /></span>
            ))}
          </div>
          <div className="muted" style={{ fontSize: 13 }}>{r.count ? `From ${r.count.toLocaleString('en-PK')} guest rating${r.count === 1 ? '' : 's'} here` : 'Be the first to rate us'}</div>
        </div>
      </div>
      <RateUs slug={slug} code={code} loy={loy} onRated={onRated} />
      <GoogleReviewCard info={info} />
    </div>
  )
}

/** Star rating with an optional comment; a member earns the feedback bonus (once a day). */
export function RateUs({ slug, code, loy, onRated, compact }) {
  const [rating, setRating] = useState(0)
  const [hover, setHover] = useState(0)
  const [comment, setComment] = useState('')
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState(() => { try { return sessionStorage.getItem(RATED_KEY(slug)) === '1' } catch { return false } })
  const bonus = loy?.enabled && loy?.info?.feedbackBonus > 0 ? loy.info.feedbackBonus : 0
  // the page card and the Reviews tab are the same rating: once one is sent, both say thanks
  useEffect(() => {
    const onRatedAnywhere = () => setDone(true)
    window.addEventListener('qrdine:rated', onRatedAnywhere)
    return () => window.removeEventListener('qrdine:rated', onRatedAnywhere)
  }, [])

  const submit = async () => {
    if (!rating) return
    setBusy(true)
    try {
      const res = await api.submitFeedback(slug, { rating, comment: comment.trim(), tableCode: code, memberToken: memberStore.get(slug) || null })
      try { sessionStorage.setItem(RATED_KEY(slug), '1') } catch { /* private mode */ }
      setDone(true)
      window.dispatchEvent(new Event('qrdine:rated'))
      onRated?.()
      if (res?.bonus > 0) { toast.success(`Thanks! ${pts(res.bonus)} added to your rewards ⭐`); loy?.refresh() }
    } catch { toast.error('Could not send your rating. Please try again.') }
    setBusy(false)
  }

  if (done) return (
    <div className={'card lx-rated' + (compact ? '' : ' fade-up')}>
      <span className="ok-check"><CheckIcon size={22} /></span>
      <div><b>Thanks for your rating!</b><div className="muted" style={{ fontSize: 13 }}>We hope to see you again soon.</div></div>
    </div>
  )
  return (
    <div className="card lx-rate">
      <div className="row between">
        <b>Rate your visit</b>
        {bonus > 0 && <span className="lx-chip">+{bonus} pts for members</span>}
      </div>
      <div className="lx-rate-stars" onMouseLeave={() => setHover(0)}>
        {[1, 2, 3, 4, 5].map((n) => (
          <button key={n} type="button" aria-label={`${n} star${n === 1 ? '' : 's'}`}
            className={(hover || rating) >= n ? 'on' : ''} onClick={() => setRating(n)} onMouseEnter={() => setHover(n)}>
            <StarIcon size={32} />
          </button>
        ))}
      </div>
      {rating > 0 && (
        <div className="fade-up">
          <textarea placeholder="Tell us about your visit (optional)" value={comment} maxLength={500}
            onChange={(e) => setComment(e.target.value)} style={{ width: '100%', minHeight: 64, marginBottom: 8 }} />
          <button className="primary" disabled={busy} onClick={submit} style={{ width: '100%' }}>{busy ? 'Sending…' : 'Send rating'}</button>
        </div>
      )}
    </div>
  )
}

