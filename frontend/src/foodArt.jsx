// Hand-drawn food illustrations for dishes and categories that don't have a photo yet, so a
// brand new menu already looks considered. Flat shapes, one warm palette, viewBox 0 0 100 100.
// A restaurant's real photos always win over these.

const ART = {
  biryani: (
    <>
      <ellipse cx="50" cy="70" rx="38" ry="12" fill="#3c1e14" />
      <path d="M14 60c0-6 16-11 36-11s36 5 36 11c0 11-16 21-36 21S14 71 14 60Z" fill="#7b3a1f" />
      <ellipse cx="50" cy="56" rx="32" ry="11" fill="#f0c56a" />
      <path d="M26 54c6-9 14-16 24-16s18 7 24 16c-6 4-15 6-24 6s-18-2-24-6Z" fill="#e9a93c" />
      <circle cx="42" cy="47" r="2.4" fill="#fff5dc" /><circle cx="52" cy="43" r="2.2" fill="#fff5dc" /><circle cx="60" cy="50" r="2.4" fill="#fff5dc" /><circle cx="47" cy="54" r="2" fill="#fff5dc" />
      <path d="M57 38c3-3 7-3 9 0-1 4-6 6-9 3-1-1-1-2 0-3Z" fill="#4e8a3a" />
      <circle cx="38" cy="52" r="3" fill="#a8341d" />
    </>
  ),
  rice: (
    <>
      <path d="M16 54h68c0 18-15 30-34 30S16 72 16 54Z" fill="#e6dccb" />
      <path d="M16 54h68c-1 4-2 7-4 10H20c-2-3-3-6-4-10Z" fill="#b53a2d" />
      <path d="M22 54c2-14 14-24 28-24s26 10 28 24H22Z" fill="#fbf3df" />
      <g fill="#ecdcb4"><circle cx="36" cy="44" r="2" /><circle cx="46" cy="38" r="2" /><circle cx="56" cy="42" r="2" /><circle cx="64" cy="48" r="2" /><circle cx="44" cy="48" r="2" /></g>
    </>
  ),
  karahi: (
    <>
      <path d="M8 44h10M82 44h10" stroke="#2c1c17" strokeWidth="5" strokeLinecap="round" />
      <path d="M16 44h68c0 22-16 36-34 36S16 66 16 44Z" fill="#332220" />
      <ellipse cx="50" cy="44" rx="34" ry="11" fill="#c04a1d" />
      <path d="M26 44c5-5 12-7 24-7s19 2 24 7c-5 4-14 6-24 6s-19-2-24-6Z" fill="#dd6a26" />
      <circle cx="38" cy="43" r="4" fill="#8e2f14" /><circle cx="56" cy="41" r="4.4" fill="#8e2f14" /><circle cx="64" cy="46" r="3" fill="#8e2f14" />
      <path d="M44 39l6 8M52 47l6-6" stroke="#58a047" strokeWidth="2.4" strokeLinecap="round" />
    </>
  ),
  curry: (
    <>
      <path d="M18 50h64c0 20-14 32-32 32S18 70 18 50Z" fill="#f1e7d6" />
      <ellipse cx="50" cy="50" rx="32" ry="10.5" fill="#d9851f" />
      <path d="M30 50c6-3 12-5 20-5s14 2 20 5c-6 3-13 5-20 5s-14-2-20-5Z" fill="#e8a433" />
      <circle cx="42" cy="50" r="3.6" fill="#9c4c13" /><circle cx="56" cy="49" r="4" fill="#9c4c13" />
      <path d="M49 44c3-2 6-1 7 1" stroke="#4e8a3a" strokeWidth="2.4" strokeLinecap="round" fill="none" />
    </>
  ),
  nihari: (
    <>
      <path d="M16 48h68c0 22-15 34-34 34S16 70 16 48Z" fill="#40241c" />
      <ellipse cx="50" cy="48" rx="34" ry="11" fill="#7e2d14" />
      <path d="M28 48c7-3 13-5 22-5s15 2 22 5c-7 3-14 5-22 5s-15-2-22-5Z" fill="#a63e17" />
      <ellipse cx="50" cy="47" rx="9" ry="5" fill="#5e2110" />
      <path d="M40 44l4 5M54 43l5 6M47 41l2 5" stroke="#f1d27a" strokeWidth="2" strokeLinecap="round" />
      <path d="M62 50l6-3" stroke="#6da655" strokeWidth="2.4" strokeLinecap="round" />
      <path d="M36 28c-3 4 3 5 0 9M50 25c-3 4 3 5 0 9M64 28c-3 4 3 5 0 9" stroke="#caa58f" strokeWidth="2.2" strokeLinecap="round" fill="none" />
    </>
  ),
  bbq: (
    <>
      <path d="M14 82 84 16" stroke="#8b8177" strokeWidth="3.4" strokeLinecap="round" />
      <path d="M84 16l5-5" stroke="#5d544c" strokeWidth="5" strokeLinecap="round" />
      <g>
        <ellipse cx="33" cy="64" rx="11" ry="8.5" transform="rotate(-43 33 64)" fill="#7c2f14" />
        <ellipse cx="49" cy="49" rx="11" ry="8.5" transform="rotate(-43 49 49)" fill="#a2431b" />
        <ellipse cx="65" cy="34" rx="11" ry="8.5" transform="rotate(-43 65 34)" fill="#7c2f14" />
      </g>
      <path d="M28 62l9 6M44 47l9 6M60 32l9 6" stroke="#401a0b" strokeWidth="2" strokeLinecap="round" />
      <circle cx="41" cy="57" r="3" fill="#6da655" /><circle cx="57" cy="42" r="3" fill="#e0a22f" />
    </>
  ),
  burger: (
    <>
      <path d="M18 46c0-14 14-22 32-22s32 8 32 22H18Z" fill="#dd9a3e" />
      <g fill="#f8e6bf"><ellipse cx="36" cy="33" rx="2.6" ry="1.5" /><ellipse cx="50" cy="29" rx="2.6" ry="1.5" /><ellipse cx="64" cy="34" rx="2.6" ry="1.5" /></g>
      <path d="M15 50c6 5 11 5 17 1 6 4 12 4 18 0 6 4 12 4 18 0 6 4 11 4 17-1v3H15Z" fill="#6da655" />
      <rect x="16" y="54" width="68" height="10" rx="5" fill="#5a2a16" />
      <path d="M18 54h64l-10 7-10-4-12 6-12-6-12 4Z" fill="#f2c13c" />
      <path d="M18 67h64c0 8-7 12-14 12H32c-7 0-14-4-14-12Z" fill="#c98231" />
    </>
  ),
  naan: (
    <>
      <path d="M50 14c20 4 34 22 32 42-2 19-18 30-34 28S16 68 18 48c2-18 16-37 32-34Z" fill="#e7bd7c" />
      <path d="M50 22c15 3 26 18 24 33-2 14-14 22-26 21S26 64 28 49c2-14 11-30 22-27Z" fill="#f2d39b" />
      <g fill="#b67a35"><circle cx="42" cy="38" r="3" /><circle cx="58" cy="44" r="3.4" /><circle cx="46" cy="58" r="3.6" /><circle cx="62" cy="62" r="2.4" /><circle cx="36" cy="52" r="2" /></g>
    </>
  ),
  salad: (
    <>
      <path d="M16 52h68c0 18-15 30-34 30S16 70 16 52Z" fill="#f5efe3" />
      <path d="M22 54c0-12 8-20 16-20 2-6 8-9 14-7 6-2 13 1 15 7 7 1 12 9 11 20H22Z" fill="#6da655" />
      <circle cx="42" cy="44" r="5" fill="#d0432d" /><circle cx="60" cy="46" r="5" fill="#d0432d" />
      <path d="M50 36c3-3 6-3 8-1" stroke="#3f7a2f" strokeWidth="2.4" strokeLinecap="round" fill="none" />
      <ellipse cx="50" cy="54" rx="13" ry="3" fill="#faf3e6" />
    </>
  ),
  dessert: (
    <>
      <path d="M24 44h52l-7 38H31Z" fill="#f4e6cf" />
      <path d="M24 44h52l-2 12H26Z" fill="#a81d48" />
      <path d="M22 44c0-12 12-20 28-20s28 8 28 20c-4 3-7 3-10 0-3 3-7 3-10 0-3 3-5 3-8 0-3 3-6 3-9 0-3 3-6 3-9 0-3 3-7 3-10 0Z" fill="#fff5e0" />
      <circle cx="50" cy="22" r="5" fill="#c53b4a" />
      <path d="M50 17c1-4 3-6 6-7" stroke="#5a8b3e" strokeWidth="2" strokeLinecap="round" fill="none" />
    </>
  ),
  drink: (
    <>
      <path d="M30 24h40l-5 58a4 4 0 0 1-4 4H39a4 4 0 0 1-4-4Z" fill="#fbe9d0" />
      <path d="M32 40h36l-4 42a4 4 0 0 1-4 4H40a4 4 0 0 1-4-4Z" fill="#e0862d" />
      <path d="M32 40c6-3 12 3 18 0s12-3 18 0v5H32Z" fill="#f1a744" />
      <rect x="39" y="50" width="9" height="9" rx="2" fill="#fff" opacity=".5" />
      <rect x="53" y="60" width="9" height="9" rx="2" fill="#fff" opacity=".42" />
      <path d="M58 26 66 8" stroke="#a81d48" strokeWidth="4" strokeLinecap="round" />
    </>
  ),
  soup: (
    <>
      <path d="M16 48h68c0 22-15 34-34 34S16 70 16 48Z" fill="#a81d48" />
      <ellipse cx="50" cy="48" rx="34" ry="11" fill="#f0e1c2" />
      <ellipse cx="50" cy="48" rx="26" ry="7.5" fill="#d99a2b" />
      <circle cx="42" cy="48" r="2" fill="#6da655" /><circle cx="56" cy="46" r="2" fill="#6da655" /><circle cx="50" cy="50" r="1.6" fill="#a8341d" />
      <path d="M38 30c-3 4 3 5 0 9M50 27c-3 4 3 5 0 9M62 30c-3 4 3 5 0 9" stroke="#b9a18f" strokeWidth="2.2" strokeLinecap="round" fill="none" />
    </>
  ),
  deal: (
    <>
      <path d="M50 12 60 24l15-3 3 15 12 9-8 13 3 15-15 4-6 14-14-6-14 6-6-14-15-4 3-15-8-13 12-9 3-15 15 3Z" fill="#a81d48" />
      <path d="M38 62 62 38" stroke="#fff" strokeWidth="4.4" strokeLinecap="round" />
      <circle cx="39" cy="40" r="5.5" fill="none" stroke="#fff" strokeWidth="3.6" />
      <circle cx="61" cy="60" r="5.5" fill="none" stroke="#fff" strokeWidth="3.6" />
    </>
  ),
  plate: (
    <>
      <circle cx="50" cy="50" r="36" fill="#f6efe2" />
      <circle cx="50" cy="50" r="26" fill="#fffaf0" stroke="#e6d9c2" strokeWidth="2" />
      <path d="M36 54c3-11 10-16 17-16 7 0 11 6 11 13 0 6-6 11-14 11-7 0-15-2-14-8Z" fill="#c9672b" />
      <path d="M46 42c3 2 7 4 12 3" stroke="#6da655" strokeWidth="2.6" strokeLinecap="round" fill="none" />
    </>
  ),
}

// Colour behind the art, per food family. [light, deep]
const TONES = {
  biryani: ['#fdeed0', '#f4cf8c'], rice: ['#f9f1e2', '#ecd9b7'], karahi: ['#fbe3d3', '#efb892'],
  curry: ['#fdefd0', '#f3cc83'], nihari: ['#f6ddcf', '#dfa78b'], bbq: ['#f8ddd0', '#e9a98a'],
  burger: ['#fcecd2', '#f2c98d'], naan: ['#fbf1dd', '#efd9ad'], salad: ['#e7f2db', '#bfdca0'],
  dessert: ['#fbe4ea', '#f1b7c6'], drink: ['#fdeada', '#f6c08e'], soup: ['#fae5e3', '#eeb6b2'],
  deal: ['#fbe0e8', '#efa9bd'], plate: ['#f6efe3', '#e7d9c1'],
}

/** Picks an illustration from a dish or category name. English and common Roman Urdu. */
export function foodKind(name = '') {
  const n = name.toLowerCase()
  if (/deal|combo|offer/.test(n)) return 'deal'
  if (/biryani|biriyani/.test(n)) return 'biryani'
  if (/karahi|kadahi|handi/.test(n)) return 'karahi'
  if (/nihari|haleem|paya/.test(n)) return 'nihari'
  if (/bbq|b\.b\.q|tikka|kebab|kabab|seekh|boti|grill|malai|chargha/.test(n)) return 'bbq'
  if (/burger|pizza|sandwich|shawarma|roll|fries|fast food|zinger|wrap/.test(n)) return 'burger'
  if (/naan|nan\b|roti|paratha|bread|tandoor/.test(n)) return 'naan'
  if (/raita|salad/.test(n)) return 'salad'
  if (/dessert|sweet|kheer|gulab|ice cream|halwa|zarda|custard|cake|shahi tukra|falooda/.test(n)) return 'dessert'
  if (/beverage|drink|tea|chai|coffee|juice|lassi|soda|cola|pepsi|coke|water|shake|sprite|fanta|lemonade|margarita/.test(n)) return 'drink'
  if (/soup|starter|appeti/.test(n)) return 'soup'
  if (/pulao|pilau|rice|chawal/.test(n)) return 'rice'
  if (/salan|curry|korma|qorma|daal|dal\b|sabzi|chana|mutton|chicken|beef|main/.test(n)) return 'curry'
  return 'plate'
}

export function FoodArt({ kind = 'plate', className, style }) {
  return (
    <svg viewBox="0 0 100 100" className={className} style={style} aria-hidden="true">
      {ART[kind] || ART.plate}
    </svg>
  )
}

export const foodTone = (kind) => TONES[kind] || TONES.plate

/** A dish's picture: the restaurant's photo if there is one, otherwise the illustration. */
export function DishPicture({ item, categoryName, className = '' }) {
  const kind = foodKind(item.name) === 'plate' ? foodKind(categoryName) : foodKind(item.name)
  const [a, b] = foodTone(kind)
  if (item.imageUrl) {
    return <div className={`dish-pic ${className}`}><img src={item.imageUrl} alt="" loading="lazy" /></div>
  }
  return (
    <div className={`dish-pic is-art ${className}`} style={{ background: `linear-gradient(150deg, ${a}, ${b})` }}>
      <FoodArt kind={kind} />
    </div>
  )
}
