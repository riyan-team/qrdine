// The "Dine In" brand wordmark, hand-rebuilt as vector art so it stays crisp at any
// size and inherits its colour from CSS `currentColor` (white on the maroon side panel,
// ink on light). The "D" is three concentric arcs; the "I" of "In" is a fork.
export default function DineInLogo({ className, style }) {
  return (
    <svg viewBox="0 0 430 190" className={className} style={style}
      xmlns="http://www.w3.org/2000/svg" role="img" aria-label="Dine In">
      {/* D — three concentric arcs (bulging left) */}
      <g fill="none" stroke="currentColor" strokeWidth="7" strokeLinecap="round">
        <path d="M78 30 A 65 65 0 0 0 78 160" />
        <path d="M78 50 A 47 47 0 0 0 78 140" />
        <path d="M78 70 A 29 29 0 0 0 78 120" />
      </g>

      {/* ine */}
      <text x="112" y="132" fill="currentColor"
        fontFamily="Georgia, 'Times New Roman', serif" fontSize="98" fontStyle="italic">ine</text>

      {/* I → fork */}
      <g stroke="currentColor" strokeLinecap="round" fill="none">
        <g strokeWidth="4">
          <line x1="286" y1="46" x2="286" y2="88" />
          <line x1="294" y1="46" x2="294" y2="88" />
          <line x1="302" y1="46" x2="302" y2="88" />
          <line x1="310" y1="46" x2="310" y2="88" />
        </g>
        <line x1="286" y1="88" x2="310" y2="88" strokeWidth="4" />
        <line x1="298" y1="88" x2="298" y2="150" strokeWidth="7" />
      </g>

      {/* n */}
      <text x="322" y="132" fill="currentColor"
        fontFamily="Georgia, 'Times New Roman', serif" fontSize="98" fontStyle="italic">n</text>
    </svg>
  )
}
