import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import DineInLogo from '../DineInLogo.jsx'
import { connectPc, getEdition, pcSyncStatus } from '../pcEdition'

const ONLINE = 'https://qrdine.mystay.live'

// PC edition, hybrid: bring a restaurant that already runs online onto this PC. The owner pairs the PC online
// (Settings > Restaurant PC) and pastes the key here; this PC downloads everything and the counter runs here from then on.
export default function Connect() {
  const nav = useNavigate()
  const [lic, setLic] = useState(undefined)
  const [st, setSt] = useState(undefined)
  const [server, setServer] = useState(ONLINE)
  const [key, setKey] = useState('')
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')

  useEffect(() => {
    getEdition(true).then((l) => { if (!l) nav('/', { replace: true }); else setLic(l) })
    pcSyncStatus().then((s) => setSt(s || { state: 'off' }))
  }, [nav])

  // While the download runs, show how far it has got.
  useEffect(() => {
    if (st?.state !== 'connecting') return undefined
    const t = setInterval(() => pcSyncStatus().then((s) => { if (s) setSt(s) }), 1500)
    return () => clearInterval(t)
  }, [st?.state])

  const submit = async (e) => {
    e.preventDefault()
    setErr(''); setBusy(true)
    try { setSt(await connectPc(server, key)); setKey('') } catch (x) { setErr(x.message) }
    setBusy(false)
  }
  const signIn = async () => { await getEdition(true); nav('/login', { replace: true }) }

  if (!lic || !st) return null
  const box = { maxWidth: 620, margin: '0 auto', padding: '32px 16px' }

  if (st.state === 'connecting') return (
    <div className="wrap" style={box}>
      <DineInLogo />
      <div className="h" style={{ marginTop: 18 }}>Bringing {st.restaurant || 'your restaurant'} onto this PC</div>
      <div className="card" style={{ marginBottom: 16 }} role="status" aria-live="polite">
        <div style={{ fontWeight: 600 }}>{st.progress || 'Downloading…'}</div>
        <p className="muted" style={{ fontSize: 14, marginBottom: 0 }}>
          The menu, tables, staff, customers, khata and past bills are coming down from {st.server}. Keep this PC on and online;
          a big restaurant can take a few minutes.
        </p>
        {st.lastError && <p style={{ color: 'var(--amber, #b26a00)', fontSize: 14, marginBottom: 0 }}>{st.lastError} Trying again…</p>}
      </div>
    </div>
  )

  if (st.state === 'ready') return (
    <div className="wrap" style={box}>
      <DineInLogo />
      <div className="h" style={{ marginTop: 18 }}>{st.restaurant || 'Your restaurant'} runs on this PC now</div>
      <div className="card" style={{ borderColor: 'var(--green, #2e7d32)', marginBottom: 16 }}>
        <p style={{ marginTop: 0 }}>Orders, bills, payments and stock are made on this PC from now on, with or without internet.
          The online copy at {st.server} follows within seconds whenever the internet is on, so you can still see everything from your phone.</p>
        <p className="muted" style={{ fontSize: 14 }}>Staff sign in with their usual mobile number and password. Print the table QR codes again
          from the QR codes page on this PC: guests now order over the restaurant&apos;s Wi-Fi.</p>
        <button className="primary" type="button" onClick={signIn}>Sign in</button>
      </div>
    </div>
  )

  if (lic.hasRestaurant) return (
    <div className="wrap" style={box}>
      <DineInLogo />
      <div className="h" style={{ marginTop: 18 }}>This PC already has its restaurant</div>
      <div className="card">
        <p style={{ marginTop: 0 }}>Connecting to an online restaurant works on a PC where Dine In has no restaurant yet, so nothing here is mixed up or lost.</p>
        <Link to="/login">Back to sign in</Link>
      </div>
    </div>
  )

  return (
    <div className="wrap" style={box}>
      <DineInLogo />
      <div className="h" style={{ marginTop: 18 }}>Connect this PC to your online restaurant</div>
      <div className="card" style={{ marginBottom: 16 }}>
        <ol style={{ margin: 0, paddingLeft: 20, lineHeight: 1.6 }}>
          <li>Online, sign in as the owner and open <b>Settings &gt; Restaurant PC</b>. Press <b>Pair a PC</b> and copy the key.</li>
          <li>Paste it below. This PC downloads the whole restaurant.</li>
          <li>After that the counter runs on this PC, even with no internet, and the online copy keeps up whenever it can.</li>
        </ol>
      </div>
      <form className="card" onSubmit={submit} style={{ marginBottom: 16 }}>
        <label className="fld" htmlFor="pc-server"><span>Online address</span>
          <input id="pc-server" value={server} onChange={(e) => setServer(e.target.value)} inputMode="url" autoComplete="url" spellCheck={false} maxLength={200} />
        </label>
        <label className="fld" htmlFor="pc-key" style={{ marginTop: 10 }}><span>PC key</span>
          <input id="pc-key" value={key} onChange={(e) => setKey(e.target.value)} placeholder="dk_…" autoComplete="off" spellCheck={false}
            style={{ fontFamily: 'monospace' }} maxLength={200} aria-invalid={!!err} aria-describedby={err ? 'pc-err' : undefined} />
        </label>
        {err && <div id="pc-err" role="alert" style={{ color: 'var(--red)', marginTop: 8 }}>{err}</div>}
        <div className="row" style={{ gap: 8, marginTop: 12, flexWrap: 'wrap' }}>
          <button className="primary" disabled={busy || !key.trim() || !server.trim()}>{busy ? 'Checking…' : 'Connect and download'}</button>
          <Link className="ghost" to="/signup" style={{ padding: '8px 14px' }}>Start a new restaurant instead</Link>
        </div>
      </form>
    </div>
  )
}
