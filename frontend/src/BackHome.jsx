import { Link } from 'react-router-dom'
import { ArrowLeftIcon } from './icons.jsx'

// "Back to home" on the sign-in, sign-up and operator sign-in screens (they have no top bar),
// so a visitor who tapped Get started / Sign in can return to the landing page.
export default function BackHome({ to = '/', label = 'Back to home' }) {
  return (
    <Link to={to} className="back-home">
      <ArrowLeftIcon size={15} /> {label}
    </Link>
  )
}
