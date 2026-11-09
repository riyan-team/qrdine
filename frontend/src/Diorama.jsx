import { FoodArt, foodTone } from './foodArt.jsx'

// A small restaurant built out of CSS: a phone showing the menu, the QR card it was scanned
// from, a kitchen ticket arriving and a bill printing. It's real 3D (preserve-3d, no WebGL, so
// it stays smooth on a cheap phone) and it leans with --px / --py from useParallax().
// The whole thing loops through one order: choose, send, cook, bill.

const TILES = [
  ['Biryani', 'biryani'], ['Karahi', 'karahi'], ['BBQ', 'bbq'], ['Naan', 'naan'], ['Desserts', 'dessert'], ['Drinks', 'drink'],
]

/** The phone's screen on its own, so sign-up can show the owner's own name on it. */
export function PhoneScreen({ name = 'Your restaurant', table = 'Table 4', looping = true }) {
  return (
    <div className={'dio-screen' + (looping ? ' looping' : '')}>
      <div className="dio-ui dio-ui-1">
        <div className="dio-ui-head">
          <span className="dio-ui-name">{name}</span>
          <span className="dio-ui-table">{table}</span>
        </div>
        <div className="dio-ui-search" />
        <div className="dio-ui-tiles">
          {TILES.map(([label, kind]) => {
            const [a, b] = foodTone(kind)
            return (
              <div key={kind} className="dio-tile" style={{ background: `linear-gradient(150deg, ${a}, ${b})` }}>
                <FoodArt kind={kind} />
                <span>{label}</span>
              </div>
            )
          })}
        </div>
      </div>
      {looping && (
        <>
          <div className="dio-ui dio-ui-2">
            <div className="dio-ui-head"><span className="dio-ui-name">Biryani</span></div>
            {[['Chicken Biryani', 'Rs 450'], ['Beef Biryani', 'Rs 520'], ['Sindhi Biryani', 'Rs 480']].map(([dish, price], i) => (
              <div className="dio-row" key={dish}>
                <div className="dio-row-pic" style={{ background: `linear-gradient(150deg, ${foodTone('biryani')[0]}, ${foodTone('biryani')[1]})` }}><FoodArt kind="biryani" /></div>
                <div className="dio-row-txt"><b>{dish}</b><span>{price}</span></div>
                <span className={'dio-add' + (i === 0 ? ' on' : '')}>{i === 0 ? '1' : '+'}</span>
              </div>
            ))}
            <div className="dio-bar"><span>1 item</span><b>Rs 450</b></div>
          </div>
          <div className="dio-ui dio-ui-3">
            <div className="dio-tick"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M20 6 9 17l-5-5" /></svg></div>
            <b>Order sent</b>
            <span>The kitchen has it</span>
          </div>
        </>
      )}
    </div>
  )
}

/** A phone with real thickness. */
export function Phone3D({ children, className = '' }) {
  return (
    <div className={`dio-phone ${className}`}>
      <i className="dio-side l" /><i className="dio-side r" /><i className="dio-side t" /><i className="dio-side b" /><i className="dio-back" />
      <div className="dio-face"><div className="dio-notch" />{children}</div>
    </div>
  )
}

function FakeQr() {
  // Decorative only, so it doesn't scan to anything by accident.
  const cells = [1,1,1,0,1,0,1,1,1, 1,0,1,0,0,1,1,0,1, 1,1,1,1,0,1,1,1,1, 0,0,1,0,1,1,0,1,0, 1,1,0,1,0,0,1,0,1, 0,1,1,0,1,1,0,0,1, 1,1,1,0,0,1,1,0,1, 1,0,1,1,1,0,0,1,0, 1,1,1,0,1,1,1,0,1]
  return (
    <svg viewBox="0 0 9 9" className="dio-qr-code" aria-hidden="true" shapeRendering="crispEdges">
      {cells.map((on, i) => (on ? <rect key={i} x={i % 9} y={Math.floor(i / 9)} width="1" height="1" /> : null))}
    </svg>
  )
}

export default function Diorama({ name, className = '' }) {
  return (
    <div className={`dio ${className}`} aria-hidden="true">
      <div className="dio-stage">
        <div className="dio-table" />
        <div className="dio-shadow" />

        <div className="dio-qr">
          <i className="dio-qr-back" />
          <div className="dio-qr-face">
            <small>Scan to order</small>
            <FakeQr />
            <b>Table 4</b>
          </div>
        </div>

        <Phone3D><PhoneScreen name={name} /></Phone3D>

        <div className="dio-ticket">
          <div className="dio-ticket-top"><b>Kitchen</b><span>Table 4</span></div>
          <div>1 × Chicken Biryani</div>
          <div className="dio-ticket-eta"><i />Ready in 15 min</div>
        </div>

        <div className="dio-receipt">
          <div className="dio-receipt-paper">
            <b>Bill</b>
            <div><span>Chicken Biryani</span><span>450</span></div>
            <div><span>Raita</span><span>80</span></div>
            <div className="sum"><span>Total</span><span>Rs 530</span></div>
          </div>
        </div>
      </div>
    </div>
  )
}
