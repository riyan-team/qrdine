import DineInLogo from './DineInLogo.jsx'
import { CheckIcon } from './icons.jsx'

// The branded maroon panel shared by the sign-in and sign-up screens: the Dine In
// logo, tagline, a short quote and a few value points. Keeps both auth pages
// visually consistent and on-brand.
const POINTS = [
  'QR self-ordering — no app for guests',
  'Live kitchen timing & fast billing',
  'FBR-ready fiscal invoices',
]

export default function AuthBrand() {
  return (
    <aside className="auth-side">
      <span className="auth-glow g1" /><span className="auth-glow g2" />
      <div className="auth-side-inner">
        <DineInLogo className="auth-logo" />
        <div className="auth-side-tag">QR ordering, kitchen timing &amp; billing — all in one.</div>
        <blockquote className="auth-quote">
          “Great restaurants run on rhythm. Dine In keeps every order, ticket and bill in time.”
        </blockquote>
        <ul className="auth-points">
          {POINTS.map((p) => (
            <li key={p}><span className="auth-pt-ico"><CheckIcon size={14} /></span>{p}</li>
          ))}
        </ul>
      </div>
    </aside>
  )
}
