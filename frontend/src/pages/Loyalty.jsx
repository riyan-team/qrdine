import { useEffect, useMemo, useState } from 'react'
import { loyalty, money } from '../api'
import { toast, ui } from '../ui.jsx'
import { friendlyError } from '../errors'
import { auth } from '../auth'
import { StarIcon, UserIcon, TrendIcon, CheckIcon, SearchIcon, PlusIcon, EditIcon, TrashIcon, RefreshIcon, ClockIcon, CloseIcon } from '../icons.jsx'
import { CountUp } from '../loyalty.jsx'

// Owner / manager: the loyalty programme, rewards, members, reward requests and review links.
// Everything the guest sees on the table's QR page is set here.

const TABS = [
  { id: 'overview', label: 'Overview' },
  { id: 'program', label: 'Program & reviews' },
  { id: 'rewards', label: 'Rewards' },
  { id: 'members', label: 'Members' },
  { id: 'requests', label: 'Reward requests' },
]
const fail = (e) => toast.error(friendlyError(e))
const fmtDate = (d) => (d ? new Date(d).toLocaleString('en-PK', { day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit' }) : '—')

export default function Loyalty() {
  const [tab, setTab] = useState('overview')
  const [program, setProgram] = useState(null)
  const [pending, setPending] = useState(0)
  const loadProgram = () => loyalty.program().then(setProgram).catch(fail)
  useEffect(() => { loadProgram() }, [])
  // the requests badge stays fresh while the page is open
  useEffect(() => {
    if (!auth.can('orders.pay')) return
    const tick = () => loyalty.redemptions('Pending').then((l) => setPending(l.length)).catch(() => {})
    tick()
    const id = setInterval(tick, 15000)
    return () => clearInterval(id)
  }, [])

  const toggle = async () => {
    if (!program) return
    try {
      const p = await loyalty.saveProgram({ ...program, enabled: !program.enabled })
      setProgram(p)
      toast.success(p.enabled ? 'Rewards are live on your QR menu 🎉' : 'Rewards are switched off.')
    } catch (e) { fail(e) }
  }

  return (
    <div className="wrap">
      <div className="row between lx-admin-head" style={{ flexWrap: 'wrap', gap: 10 }}>
        <div>
          <div className="h" style={{ marginBottom: 2 }}>Loyalty &amp; reviews</div>
          <p className="muted" style={{ margin: 0 }}>Points on every bill, rewards guests can claim, and your Google reviews — all on the table QR.</p>
        </div>
        {program && (
          <button className={'lx-live ' + (program.enabled ? 'on' : '')} onClick={toggle}>
            <span className="lx-live-dot" /> {program.enabled ? 'Live on QR menu' : 'Switched off — turn on'}
          </button>
        )}
      </div>

      <div className="settings-nav" style={{ marginTop: 14 }}>
        {TABS.map((t) => (
          <button key={t.id} className={tab === t.id ? 'on' : ''} onClick={() => setTab(t.id)}>
            {t.label}{t.id === 'requests' && pending > 0 && <span className="lx-count">{pending}</span>}
          </button>
        ))}
      </div>

      <div className="settings-panel fade-up" key={tab}>
        {tab === 'overview' && <Overview program={program} goTo={setTab} />}
        {tab === 'program' && program && <ProgramForm program={program} onSaved={setProgram} />}
        {tab === 'rewards' && <Rewards />}
        {tab === 'members' && <Members />}
        {tab === 'requests' && <Requests onCount={setPending} />}
      </div>
    </div>
  )
}

// ───────────────────────── overview ─────────────────────────

function Overview({ program, goTo }) {
  const [o, setO] = useState(null)
  const [rewards, setRewards] = useState(null)
  useEffect(() => {
    loyalty.overview().then(setO).catch(fail)
    loyalty.rewards().then(setRewards).catch(() => setRewards([]))
  }, [program?.enabled])
  if (!o) return <div className="loading"><span className="spinner dark" /> Loading…</div>

  const steps = [
    { done: !!program?.enabled, label: 'Switch rewards on', tab: 'program' },
    { done: (rewards?.length ?? 0) > 0, label: 'Add at least one reward', tab: 'rewards' },
    { done: !!program?.googleReviewUrl, label: 'Add your Google review link', tab: 'program' },
  ]
  const tiles = [
    { Icon: UserIcon, num: o.members, label: 'Members', sub: `${o.newMembers30d} new in 30 days` },
    { Icon: TrendIcon, num: o.activeMembers30d, label: 'Came back (30 days)', sub: 'members with a visit' },
    { Icon: StarIcon, num: o.pointsIssued, label: 'Points given', sub: `${o.pointsOutstanding.toLocaleString('en-PK')} still to spend` },
    { Icon: CheckIcon, num: o.approvedRedemptions, label: 'Rewards enjoyed', sub: `${o.pointsRedeemed.toLocaleString('en-PK')} points used` },
    { Icon: ClockIcon, num: o.pendingRedemptions, label: 'Waiting requests', sub: 'approve at reception', go: 'requests' },
    { Icon: StarIcon, num: o.reviews.count ? o.reviews.average : 0, label: 'Guest rating', sub: `${o.reviews.count} rating${o.reviews.count === 1 ? '' : 's'} on the QR`, decimal: true },
  ]
  return (
    <div className="stagger">
      {steps.some((s) => !s.done) && (
        <div className="card lx-setup">
          <b>Get set up</b>
          <div className="lx-steps">
            {steps.map((s) => (
              <button key={s.label} className={'lx-step' + (s.done ? ' done' : '')} onClick={() => goTo(s.tab)}>
                <span className="lx-step-tick">{s.done ? <CheckIcon size={14} /> : ''}</span> {s.label}
              </button>
            ))}
          </div>
        </div>
      )}
      <div className="grid g3">
        {tiles.map((t) => (
          <div key={t.label} className={'card stat lx-tile' + (t.go ? ' clickable' : '')} onClick={t.go ? () => goTo(t.go) : undefined}>
            <span className="chip"><t.Icon size={20} /></span>
            <div className="num">{t.decimal ? (t.num ? t.num.toFixed(1) : '—') : <CountUp value={t.num} />}</div>
            <div className="lbl">{t.label}</div>
            <div className="muted" style={{ fontSize: 12.5 }}>{t.sub}</div>
          </div>
        ))}
      </div>
      <div className="card" style={{ marginTop: 16 }}>
        <b>Top members</b>
        {o.topMembers.length === 0 ? <div className="empty" style={{ padding: 20 }}>No members yet. Guests join from the table QR.</div> : (
          <div className="table-scroll">
            <table className="data-table">
              <thead><tr><th>Name</th><th>Phone</th><th className="num">Points</th><th className="num">Visits</th><th className="num">Spent</th></tr></thead>
              <tbody>
                {o.topMembers.map((m) => (
                  <tr key={m.id}><td>{m.name}</td><td>{m.phone}</td><td className="num">{m.points.toLocaleString('en-PK')}</td><td className="num">{m.visits}</td><td className="num">{money(m.lifetimeSpend)}</td></tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  )
}

// ───────────────────────── programme & reviews ─────────────────────────

function Toggle({ label, checked, onChange }) {
  return (
    <label className="switch-row">
      <span className={'switch' + (checked ? ' on' : '')} onClick={(e) => { e.preventDefault(); onChange(!checked) }}><span className="knob" /></span>
      <span>{label}</span>
      <input type="checkbox" checked={!!checked} onChange={(e) => onChange(e.target.checked)} style={{ display: 'none' }} />
    </label>
  )
}

function ProgramForm({ program, onSaved }) {
  const [f, setF] = useState(program)
  const [busy, setBusy] = useState(false)
  const set = (k) => (e) => setF({ ...f, [k]: e?.target ? e.target.value : e })
  const num = (v) => (v === '' || v == null ? 0 : Number(v))
  const earn = num(f.earnPoints), per = num(f.perAmount)
  const example = per > 0 ? Math.floor(2000 / per) * earn : 0

  const save = async () => {
    setBusy(true)
    try {
      const p = await loyalty.saveProgram({
        ...f, earnPoints: earn, perAmount: per, minBillAmount: num(f.minBillAmount),
        welcomeBonus: num(f.welcomeBonus), feedbackBonus: num(f.feedbackBonus),
      })
      onSaved(p); setF(p)
      toast.success('Saved. Guests see the changes on their next scan.')
    } catch (e) { fail(e) }
    setBusy(false)
  }

  return (
    <div className="lx-program">
      <div className="set-grid">
        <div className="card">
          <b>Programme</b>
          <div style={{ margin: '12px 0' }}><Toggle label="Show rewards on the table QR menu" checked={f.enabled} onChange={set('enabled')} /></div>
          <label className="field"><span className="field-label">Programme name</span>
            <input value={f.name} maxLength={60} onChange={set('name')} placeholder="e.g. Karachi Grill Rewards" /></label>
          <label className="field"><span className="field-label">Tagline (shown to guests)</span>
            <input value={f.tagline} maxLength={160} onChange={set('tagline')} placeholder="Earn points on every visit and treat yourself." /></label>
        </div>

        <div className="card">
          <b>Earning</b>
          <div className="lx-rule">
            <input type="number" min="0" value={f.earnPoints} onChange={set('earnPoints')} aria-label="Points earned" />
            <span>point{earn === 1 ? '' : 's'} for every</span>
            <span className="lx-rs">Rs</span>
            <input type="number" min="1" value={f.perAmount} onChange={set('perAmount')} aria-label="Amount per point" />
            <span>spent</span>
          </div>
          <div className="lx-example">A {money(2000)} bill earns <b>{example.toLocaleString('en-PK')} points</b>.</div>
          <div className="grid g2" style={{ gap: 12, marginTop: 12 }}>
            <label className="field"><span className="field-label">Minimum bill to earn (Rs)</span>
              <input type="number" min="0" value={f.minBillAmount} onChange={set('minBillAmount')} /></label>
            <label className="field"><span className="field-label">Welcome bonus (points)</span>
              <input type="number" min="0" value={f.welcomeBonus} onChange={set('welcomeBonus')} /></label>
            <label className="field"><span className="field-label">Rating bonus (points, once a day)</span>
              <input type="number" min="0" value={f.feedbackBonus} onChange={set('feedbackBonus')} /></label>
          </div>
          <div className="field-hint">Points are added when the bill is settled at reception, for the member linked to that table.</div>
        </div>

        <div className="card">
          <b>Google reviews</b>
          <p className="muted" style={{ fontSize: 13, margin: '6px 0 12px' }}>
            Guests see a <b>Review us on Google</b> button after their meal. In Google Business Profile, open <i>Ask for reviews</i> and copy the link
            (or use <code>https://search.google.com/local/writereview?placeid=YOUR_PLACE_ID</code>).
          </p>
          <label className="field"><span className="field-label">Google review link</span>
            <div className="row" style={{ gap: 6 }}>
              <input style={{ flex: 1 }} value={f.googleReviewUrl} onChange={set('googleReviewUrl')} placeholder="https://g.page/r/…/review" />
              {f.googleReviewUrl && <a className="lx-btn lx-btn-ghost" href={f.googleReviewUrl} target="_blank" rel="noopener noreferrer">Test</a>}
            </div>
          </label>
          <label className="field"><span className="field-label">Google Maps page (to read reviews)</span>
            <div className="row" style={{ gap: 6 }}>
              <input style={{ flex: 1 }} value={f.googleMapsUrl} onChange={set('googleMapsUrl')} placeholder="https://maps.app.goo.gl/…" />
              {f.googleMapsUrl && <a className="lx-btn lx-btn-ghost" href={f.googleMapsUrl} target="_blank" rel="noopener noreferrer">Test</a>}
            </div>
          </label>
          <div className="field-hint">Google's rules: ask every guest (not only happy ones) and never give points for a Google review — this button follows both.</div>
        </div>
      </div>

      <aside className="lx-preview">
        <div className="lx-preview-label">What guests see</div>
        <div className="lx-phone">
          <div className="lx-strip static">
            <span className="lx-strip-ico">🎁</span>
            <span className="lx-strip-txt"><b>Join {f.name || 'Rewards'}</b><span>{num(f.welcomeBonus) > 0 ? `Get ${num(f.welcomeBonus)} pts free — earn on every bill` : 'Earn points on every bill'}</span></span>
            <span className="lx-strip-go">›</span>
          </div>
          <div className="lx-hero lx-hero-join" style={{ marginTop: 10 }}>
            <div className="lx-hero-kicker">{f.name || 'Rewards'}</div>
            <div className="lx-hero-title">{f.tagline || 'Earn points on every visit.'}</div>
            <div className="lx-perks">
              <span><StarIcon size={13} /> {earn} pt{earn === 1 ? '' : 's'} per {money(per || 0)}</span>
              {num(f.welcomeBonus) > 0 && <span>🎁 {num(f.welcomeBonus)} pts welcome</span>}
            </div>
          </div>
          {!f.enabled && <div className="lx-preview-off">Switched off — guests don't see this yet</div>}
        </div>
        <button className="primary" disabled={busy} onClick={save} style={{ width: '100%', marginTop: 14 }}>{busy ? 'Saving…' : 'Save changes'}</button>
      </aside>
    </div>
  )
}

// ───────────────────────── rewards ─────────────────────────

const EMOJIS = ['🎁', '🥤', '☕', '🍰', '🍨', '🍟', '🍕', '🍔', '🍗', '🥘', '🍛', '💸', '🎉', '⭐']
const blank = { title: '', description: '', emoji: '🎁', pointsCost: 100, active: true, sortOrder: 0 }
const SAMPLES = [
  { title: 'Free soft drink', description: 'Any regular soft drink', emoji: '🥤', pointsCost: 100, active: true, sortOrder: 1 },
  { title: 'Free dessert', description: 'Chef’s dessert of the day', emoji: '🍰', pointsCost: 250, active: true, sortOrder: 2 },
  { title: 'Rs 500 off your bill', description: 'On bills over Rs 2,500', emoji: '💸', pointsCost: 500, active: true, sortOrder: 3 },
]

function Rewards() {
  const [list, setList] = useState(null)
  const [edit, setEdit] = useState(null)   // a reward being added/edited
  const load = () => loyalty.rewards().then(setList).catch(fail)
  useEffect(() => { load() }, [])

  const save = async () => {
    const body = { ...edit, pointsCost: Number(edit.pointsCost) || 0, sortOrder: Number(edit.sortOrder) || 0 }
    try {
      if (edit.id) await loyalty.updateReward(edit.id, body)
      else await loyalty.addReward(body)
      setEdit(null); load(); toast.success('Reward saved.')
    } catch (e) { fail(e) }
  }
  const remove = async (r) => {
    if (!(await ui.confirm({ title: 'Delete reward?', message: `“${r.title}” will disappear from the QR menu. Requests already waiting can still be approved.`, confirmText: 'Delete', danger: true }))) return
    try { await loyalty.deleteReward(r.id); load() } catch (e) { fail(e) }
  }
  const toggle = async (r) => {
    try { await loyalty.updateReward(r.id, { ...r, active: !r.active }); load() } catch (e) { fail(e) }
  }
  const addSamples = async () => {
    try { for (const s of SAMPLES) await loyalty.addReward(s); load(); toast.success('Sample rewards added — edit them to suit you.') } catch (e) { fail(e) }
  }

  if (!list) return <div className="loading"><span className="spinner dark" /> Loading…</div>
  return (
    <div>
      <div className="row between" style={{ marginBottom: 12, flexWrap: 'wrap', gap: 8 }}>
        <span className="muted" style={{ fontSize: 13.5 }}>Guests claim these with their points; reception confirms each one.</span>
        <div className="row" style={{ gap: 8 }}>
          {list.length === 0 && <button onClick={addSamples}>Add sample rewards</button>}
          <button className="primary" onClick={() => setEdit({ ...blank, sortOrder: list.length + 1 })}><PlusIcon size={15} /> New reward</button>
        </div>
      </div>

      {list.length === 0 ? (
        <div className="card empty">No rewards yet. Add one, or start with the samples.</div>
      ) : (
        <div className="lx-reward-admin stagger">
          {list.map((r) => (
            <div key={r.id} className={'card lx-ra' + (r.active ? '' : ' off')}>
              <span className="lx-emoji big-emoji">{r.emoji}</span>
              <div style={{ minWidth: 0, flex: 1 }}>
                <b>{r.title}</b>
                {r.description && <div className="muted" style={{ fontSize: 13 }}>{r.description}</div>}
                <div className="lx-cost" style={{ marginTop: 4 }}>{r.pointsCost.toLocaleString('en-PK')} pts</div>
              </div>
              <Toggle label="" checked={r.active} onChange={() => toggle(r)} />
              <button className="icon-btn" onClick={() => setEdit(r)} aria-label="Edit"><EditIcon size={16} /></button>
              <button className="icon-btn danger" onClick={() => remove(r)} aria-label="Delete"><TrashIcon size={16} /></button>
            </div>
          ))}
        </div>
      )}

      {edit && (
        <div className="overlay" onClick={(e) => { if (e.target.className === 'overlay') setEdit(null) }}>
          <div className="modal fade-up">
            <div className="row between"><b style={{ fontSize: 17 }}>{edit.id ? 'Edit reward' : 'New reward'}</b>
              <button className="icon-btn" onClick={() => setEdit(null)} aria-label="Close"><CloseIcon size={17} /></button></div>
            <div className="lx-emojis">
              {EMOJIS.map((e) => <button key={e} className={edit.emoji === e ? 'on' : ''} onClick={() => setEdit({ ...edit, emoji: e })}>{e}</button>)}
            </div>
            <label className="field"><span className="field-label">Name</span>
              <input value={edit.title} maxLength={60} onChange={(e) => setEdit({ ...edit, title: e.target.value })} placeholder="e.g. Free dessert" autoFocus /></label>
            <label className="field"><span className="field-label">Details (optional)</span>
              <input value={edit.description} maxLength={200} onChange={(e) => setEdit({ ...edit, description: e.target.value })} placeholder="e.g. Chef’s dessert of the day" /></label>
            <div className="grid g2" style={{ gap: 12 }}>
              <label className="field"><span className="field-label">Points needed</span>
                <input type="number" min="1" value={edit.pointsCost} onChange={(e) => setEdit({ ...edit, pointsCost: e.target.value })} /></label>
              <label className="field"><span className="field-label">Order in the list</span>
                <input type="number" value={edit.sortOrder} onChange={(e) => setEdit({ ...edit, sortOrder: e.target.value })} /></label>
            </div>
            <Toggle label="Guests can claim it" checked={edit.active} onChange={(v) => setEdit({ ...edit, active: v })} />
            <div className="row" style={{ justifyContent: 'flex-end', gap: 8, marginTop: 16 }}>
              <button onClick={() => setEdit(null)}>Cancel</button>
              <button className="primary" onClick={save}>Save reward</button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

// ───────────────────────── members ─────────────────────────

function Members() {
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [data, setData] = useState(null)
  const [open, setOpen] = useState(null)
  useEffect(() => {
    const t = setTimeout(() => loyalty.members(q, page).then(setData).catch(fail), 250)
    return () => clearTimeout(t)
  }, [q, page])
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1

  return (
    <div>
      <div className="search-box lx-search">
        <SearchIcon size={16} />
        <input placeholder="Search by name or phone" value={q} onChange={(e) => { setQ(e.target.value); setPage(1) }} />
      </div>
      <div className="card" style={{ marginTop: 12 }}>
        <div className="table-scroll">
          <table className="data-table">
            <thead><tr><th>Name</th><th>Phone</th><th className="num">Points</th><th className="num">Visits</th><th className="num">Spent</th><th>Last visit</th></tr></thead>
            <tbody>
              {!data ? <tr><td colSpan={6} className="empty-row">Loading…</td></tr>
                : data.items.length === 0 ? <tr><td colSpan={6} className="empty-row">{q ? 'No member matches that search.' : 'No members yet.'}</td></tr>
                : data.items.map((m) => (
                  <tr key={m.id} className="lx-row" onClick={() => setOpen(m.id)}>
                    <td><b>{m.name}</b></td><td>{m.phone}</td><td className="num">{m.points.toLocaleString('en-PK')}</td>
                    <td className="num">{m.visits}</td><td className="num">{money(m.lifetimeSpend)}</td><td>{fmtDate(m.lastVisitAt || m.joinedAt)}</td>
                  </tr>
                ))}
            </tbody>
          </table>
        </div>
        {data && data.total > data.pageSize && (
          <div className="pager">
            <button className="btn-sm" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
            <span className="muted">Page {page} of {pages} · {data.total} members</span>
            <button className="btn-sm" disabled={page >= pages} onClick={() => setPage(page + 1)}>Next</button>
          </div>
        )}
      </div>
      {open && <MemberModal id={open} onClose={() => setOpen(null)} onChanged={() => loyalty.members(q, page).then(setData).catch(() => {})} />}
    </div>
  )
}

function MemberModal({ id, onClose, onChanged }) {
  const [d, setD] = useState(null)
  const [points, setPoints] = useState('')
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  useEffect(() => { loyalty.member(id).then(setD).catch(fail) }, [id])
  const adjust = async () => {
    setBusy(true)
    try {
      const res = await loyalty.adjust(id, Number(points), note)
      setD(res); setPoints(''); setNote(''); onChanged(); toast.success('Points updated.')
    } catch (e) { fail(e) }
    setBusy(false)
  }
  return (
    <div className="overlay" onClick={(e) => { if (e.target.className === 'overlay') onClose() }}>
      <div className="modal fade-up" style={{ maxWidth: 520 }}>
        {!d ? <div className="loading"><span className="spinner dark" /> Loading…</div> : (
          <>
            <div className="row between">
              <div><b style={{ fontSize: 18 }}>{d.member.name}</b><div className="muted" style={{ fontSize: 13 }}>{d.member.phone} · joined {fmtDate(d.member.joinedAt)}</div></div>
              <button className="icon-btn" onClick={onClose} aria-label="Close"><CloseIcon size={17} /></button>
            </div>
            <div className="lx-mstats">
              <div><b><CountUp value={d.member.points} /></b><span>points</span></div>
              <div><b>{d.member.visits}</b><span>visits</span></div>
              <div><b>{money(d.member.lifetimeSpend)}</b><span>spent</span></div>
              <div><b>{d.member.lifetimePoints.toLocaleString('en-PK')}</b><span>earned</span></div>
            </div>
            <div className="set-block" style={{ marginTop: 12 }}>
              <b style={{ fontSize: 13.5 }}>Adjust points</b>
              <div className="row" style={{ gap: 6, marginTop: 8, flexWrap: 'wrap' }}>
                <input type="number" style={{ width: 110 }} value={points} onChange={(e) => setPoints(e.target.value)} placeholder="+50 or -20" />
                <input style={{ flex: 1, minWidth: 160 }} value={note} maxLength={200} onChange={(e) => setNote(e.target.value)} placeholder="Reason (e.g. birthday gift)" />
                <button className="primary" disabled={busy || !Number(points) || !note.trim()} onClick={adjust}>Apply</button>
              </div>
            </div>
            <div className="lx-sub" style={{ marginTop: 14 }}>History</div>
            <div className="lx-history admin">
              {d.history.length === 0 ? <div className="muted" style={{ fontSize: 13 }}>No points yet.</div> : d.history.map((t) => (
                <div className="lx-hist" key={t.id}>
                  <span>{t.type}{t.billAmount > 0 ? ` · ${money(t.billAmount)}` : ''}{t.note ? ` · ${t.note}` : ''}<small>{fmtDate(t.createdAt)}</small></span>
                  <b className={t.points >= 0 ? 'plus' : 'minus'}>{t.points >= 0 ? '+' : ''}{t.points.toLocaleString('en-PK')}</b>
                </div>
              ))}
            </div>
          </>
        )}
      </div>
    </div>
  )
}

// ───────────────────────── reward requests ─────────────────────────

/** Reward requests with approve / reject. Also used on Reception (compact). */
export function Requests({ onCount, compact }) {
  const [status, setStatus] = useState('Pending')
  const [list, setList] = useState(null)
  const [busy, setBusy] = useState(null)
  const load = () => loyalty.redemptions(status === 'All' ? '' : status).then((l) => {
    setList(l)
    if (status === 'Pending') onCount?.(l.length)
  }).catch((e) => {
    // on Reception the strip simply stays hidden (loyalty off, or a role without payments)
    if (compact) setList([]); else fail(e)
  })
  useEffect(() => {
    load()
    if (status !== 'Pending') return
    const id = setInterval(load, 12000)
    return () => clearInterval(id)
  }, [status])

  const decide = async (r, ok) => {
    let note = null
    if (!ok) {
      note = await ui.prompt({ title: 'Reject request', message: `Reject ${r.rewardTitle} for ${r.memberName}? Their points stay with them.`, placeholder: 'Reason (optional)', confirmText: 'Reject' })
      if (note === null) return
    }
    setBusy(r.id)
    try {
      if (ok) { await loyalty.approve(r.id); toast.success(`${r.rewardEmoji} ${r.rewardTitle} approved for ${r.memberName}.`) }
      else { await loyalty.reject(r.id, note); toast.show('Request rejected.') }
      load()
    } catch (e) { fail(e) }
    setBusy(null)
  }

  const shown = compact ? (list || []).filter((r) => r.status === 'Pending') : list
  if (compact && (!shown || shown.length === 0)) return null
  return (
    <div className={compact ? 'lx-recept fade-up' : ''}>
      {compact ? <div className="cat-title"><StarIcon size={15} /> Reward requests ({shown.length})</div> : (
        <div className="row between" style={{ marginBottom: 12 }}>
          <div className="seg">
            {['Pending', 'Approved', 'Rejected', 'All'].map((s) => <button key={s} className={status === s ? 'on' : ''} onClick={() => setStatus(s)}>{s}</button>)}
          </div>
          <button className="ghost" onClick={load} aria-label="Refresh"><RefreshIcon size={16} /></button>
        </div>
      )}
      {!shown ? <div className="loading"><span className="spinner dark" /> Loading…</div>
        : shown.length === 0 ? <div className="card empty">{status === 'Pending' ? 'No rewards waiting. New requests appear here by themselves.' : 'Nothing here yet.'}</div>
        : (
          <div className="grid lx-req-grid stagger">
            {shown.map((r) => (
              <div key={r.id} className={'card lx-req ' + r.status.toLowerCase()}>
                <div className="row between" style={{ alignItems: 'flex-start' }}>
                  <div className="row" style={{ gap: 10, alignItems: 'flex-start', minWidth: 0 }}>
                    <span className="lx-emoji">{r.rewardEmoji}</span>
                    <div style={{ minWidth: 0 }}>
                      <b>{r.rewardTitle}</b>
                      <div className="muted" style={{ fontSize: 13 }}>{r.memberName} · {r.memberPhone}</div>
                      <div className="muted" style={{ fontSize: 12 }}>{r.tableCode ? `Table ${r.tableCode} · ` : ''}{fmtDate(r.requestedAt)}</div>
                    </div>
                  </div>
                  <div className="lx-req-code">{r.code}</div>
                </div>
                <div className="row between" style={{ marginTop: 10 }}>
                  <span className="lx-cost">{r.pointsCost.toLocaleString('en-PK')} pts</span>
                  {r.status === 'Pending' ? (
                    <div className="row" style={{ gap: 6 }}>
                      <button className="btn-sm" disabled={busy === r.id} onClick={() => decide(r, false)}>Reject</button>
                      <button className="green btn-sm" disabled={busy === r.id} onClick={() => decide(r, true)}><CheckIcon size={14} /> Approve</button>
                    </div>
                  ) : <span className={'lx-badge ' + r.status.toLowerCase()}>{r.status}{r.decidedBy ? ` · ${r.decidedBy}` : ''}</span>}
                </div>
                {r.note && <div className="muted" style={{ fontSize: 12.5, marginTop: 6 }}>“{r.note}”</div>}
              </div>
            ))}
          </div>
        )}
    </div>
  )
}

// ───────────────────────── reception: the running bill's member ─────────────────────────

/** In the bill window: who this bill earns points for, attach by phone, and their waiting rewards. */
export function BillMember({ code, total, onChange }) {
  const [d, setD] = useState(null)
  const [phone, setPhone] = useState('')
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)
  const load = () => loyalty.tableMember(code).then((x) => { setD(x); onChange?.(x) }).catch(() => setD(null))
  useEffect(() => { load() }, [code])
  const est = useMemo(() => {
    if (!d?.enabled || !d.perAmount || total < d.minBillAmount) return 0
    return Math.floor(total / d.perAmount) * d.earnPoints
  }, [d, total])
  if (!d?.enabled) return null

  const attach = async () => {
    setBusy(true)
    try { const x = await loyalty.attach(code, phone, name); setD(x); onChange?.(x); setPhone(''); setName(''); toast.success('Member added to this bill.') }
    catch (e) { fail(e) }
    setBusy(false)
  }
  const detach = async () => { try { await loyalty.detach(code); load() } catch (e) { fail(e) } }
  const decide = async (r, ok) => {
    try { if (ok) await loyalty.approve(r.id); else await loyalty.reject(r.id, null); load(); toast.success(ok ? 'Reward approved.' : 'Request rejected.') }
    catch (e) { fail(e) }
  }

  return (
    <div className="lx-billbox fade-up">
      {d.member ? (
        <>
          <div className="row between">
            <div className="row" style={{ gap: 8 }}>
              <span className="lx-strip-ico small"><StarIcon size={14} /></span>
              <div><b>{d.member.name}</b> <span className="muted" style={{ fontSize: 12.5 }}>{d.member.phone}</span>
                <div className="muted" style={{ fontSize: 12.5 }}>{d.member.points.toLocaleString('en-PK')} pts · this bill earns about <b style={{ color: 'var(--accent2)' }}>+{est.toLocaleString('en-PK')}</b></div></div>
            </div>
            <button className="ghost btn-sm" onClick={detach}>Remove</button>
          </div>
          {d.pending.map((r) => (
            <div key={r.id} className="lx-billreq">
              <span>{r.rewardEmoji} {r.rewardTitle} · <b>{r.code}</b> · {r.pointsCost} pts</span>
              <span className="row" style={{ gap: 4 }}>
                <button className="btn-sm" onClick={() => decide(r, false)}>Reject</button>
                <button className="green btn-sm" onClick={() => decide(r, true)}>Approve</button>
              </span>
            </div>
          ))}
        </>
      ) : (
        <>
          <div className="muted" style={{ fontSize: 13, marginBottom: 6 }}><StarIcon size={13} /> Loyalty: add the guest's number so this bill earns points</div>
          <div className="row" style={{ gap: 6, flexWrap: 'wrap' }}>
            <input style={{ flex: '1 1 130px' }} value={phone} inputMode="tel" onChange={(e) => setPhone(e.target.value)} placeholder="0300 1234567" />
            <input style={{ flex: '1 1 110px' }} value={name} onChange={(e) => setName(e.target.value)} placeholder="Name (new member)" />
            <button className="btn-sm" disabled={busy || phone.replace(/\D/g, '').length < 10} onClick={attach}>Add</button>
          </div>
        </>
      )}
    </div>
  )
}
