import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import DineInLogo from '../DineInLogo.jsx'
import { CONTACT } from '../contact'
import { activate, getEdition } from '../pcEdition'
import { auth } from '../auth'

// The PC edition's licence screen: this PC's code (sent to the seller), the licence key (pasted back),
// and what the current licence says. Online there is no licence, so this page just goes home.
export default function Activate() {
  const nav = useNavigate()
  const [lic, setLic] = useState(undefined)
  const [key, setKey] = useState('')
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')
  const [copied, setCopied] = useState(false)

  useEffect(() => {
    getEdition(true).then((l) => { if (!l) nav('/', { replace: true }); else setLic(l) })
  }, [nav])

  const submit = async (e) => {
    e.preventDefault()
    setErr(''); setBusy(true)
    try { setLic(await activate(key)); setKey('') } catch (x) { setErr(x.message) }
    setBusy(false)
  }
  const copy = async () => {
    try { await navigator.clipboard.writeText(lic.machineCode); setCopied(true); setTimeout(() => setCopied(false), 2000) } catch { /* select it by hand */ }
  }

  if (!lic) return null
  const next = auth.staffToken ? auth.home() : lic.hasRestaurant ? '/login' : '/signup'
  const ok = lic.canWork
  const msg = `Dine In licence for this PC: ${lic.machineCode}`

  return (
    <div className="wrap" style={{ maxWidth: 620, margin: '0 auto', padding: '32px 16px' }}>
      <DineInLogo />
      <div className="h" style={{ marginTop: 18 }}>{ok ? 'Dine In is licensed on this PC' : 'Activate Dine In on this PC'}</div>

      <div className="card" style={{ borderColor: lic.state === 'Active' ? 'var(--green, #2e7d32)' : lic.canRead ? 'var(--amber, #b26a00)' : 'var(--red)', marginBottom: 16 }}>
        <div style={{ fontWeight: 600 }}>{lic.message}</div>
        {lic.restaurant && <div className="muted" style={{ marginTop: 6, fontSize: 14 }}>
          {lic.restaurant}{lic.expires ? ` · until ${lic.expires}` : ' · no end date'}
        </div>}
      </div>

      <div className="card" style={{ marginBottom: 16 }}>
        <div className="muted" style={{ fontSize: 13 }}>This PC's code</div>
        <div className="row" style={{ gap: 10, alignItems: 'center', flexWrap: 'wrap', marginTop: 4 }}>
          <span style={{ fontFamily: 'monospace', fontSize: 26, letterSpacing: '.06em', fontWeight: 700 }}>{lic.machineCode}</span>
          <button className="ghost btn-sm" type="button" onClick={copy}>{copied ? 'Copied' : 'Copy'}</button>
        </div>
        <p className="muted" style={{ fontSize: 14, marginBottom: 0 }}>
          {ok ? 'To renew or move to another PC, send this code to your Dine In seller.'
            : 'Send this code to your Dine In seller. You get a licence key back: paste it below.'}{' '}
          <a href={`${CONTACT.whatsapp}?text=${encodeURIComponent(msg)}`} target="_blank" rel="noreferrer">WhatsApp {CONTACT.phone}</a>
        </p>
      </div>

      <form className="card" onSubmit={submit} style={{ marginBottom: 16 }}>
        <label htmlFor="lic-key" style={{ fontWeight: 600 }}>{ok ? 'New licence key (renewal)' : 'Licence key'}</label>
        <textarea id="lic-key" rows={4} value={key} onChange={(e) => setKey(e.target.value)} placeholder="DINEIN1.…"
          style={{ width: '100%', fontFamily: 'monospace', fontSize: 13, marginTop: 6 }} spellCheck={false} autoComplete="off" />
        {err && <div style={{ color: 'var(--red)', marginTop: 8 }}>{err}</div>}
        <div className="row" style={{ gap: 8, marginTop: 10 }}>
          <button className="primary" disabled={busy || !key.trim()}>{busy ? 'Checking…' : ok ? 'Save new key' : 'Activate'}</button>
          {lic.canRead && <Link className="ghost" to={next} style={{ padding: '8px 14px' }}>{ok ? 'Continue' : 'Open my records'}</Link>}
        </div>
      </form>
    </div>
  )
}
