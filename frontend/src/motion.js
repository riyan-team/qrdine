import { useEffect, useRef } from 'react'

// Motion helpers for the 3D screens. Everything here only moves pixels: transforms and
// opacity, on the GPU, nothing that touches a cart, an order or a bill.

export const reducedMotion = () => !!window.matchMedia?.('(prefers-reduced-motion: reduce)').matches

const clamp = (v, lo = -1, hi = 1) => Math.max(lo, Math.min(hi, v))

/**
 * Follows the pointer (or the phone's tilt, where the browser offers it) and publishes it as
 * --px and --py, each -1 to 1, on the element. CSS decides what to do with them. Eased, and it
 * stops asking for frames as soon as it has caught up, so an idle screen costs nothing.
 */
export function useParallax() {
  const ref = useRef(null)
  useEffect(() => {
    const el = ref.current
    if (!el || reducedMotion()) return undefined
    let raf = 0; let x = 0; let y = 0; let tx = 0; let ty = 0

    const tick = () => {
      x += (tx - x) * 0.07
      y += (ty - y) * 0.07
      el.style.setProperty('--px', x.toFixed(3))
      el.style.setProperty('--py', y.toFixed(3))
      raf = Math.abs(tx - x) + Math.abs(ty - y) > 0.002 ? requestAnimationFrame(tick) : 0
    }
    const kick = () => { if (!raf) raf = requestAnimationFrame(tick) }
    const onMove = (e) => {
      if (e.pointerType === 'touch') return
      tx = (e.clientX / window.innerWidth) * 2 - 1
      ty = (e.clientY / window.innerHeight) * 2 - 1
      kick()
    }
    const onTilt = (e) => {
      if (e.gamma == null || e.beta == null) return
      tx = clamp(e.gamma / 28)
      ty = clamp((e.beta - 45) / 28)
      kick()
    }
    window.addEventListener('pointermove', onMove, { passive: true })
    window.addEventListener('deviceorientation', onTilt, { passive: true })
    return () => {
      cancelAnimationFrame(raf)
      window.removeEventListener('pointermove', onMove)
      window.removeEventListener('deviceorientation', onTilt)
    }
  }, [])
  return ref
}

/**
 * Spread on a card to make it lean towards the finger or cursor. A touch that turns into a
 * scroll fires pointercancel, so the card lets go by itself and never fights the scroll.
 */
export function tilt(max = 9) {
  const lean = (e) => {
    if (reducedMotion()) return
    const el = e.currentTarget
    const r = el.getBoundingClientRect()
    const px = clamp(((e.clientX - r.left) / r.width) * 2 - 1)
    const py = clamp(((e.clientY - r.top) / r.height) * 2 - 1)
    el.style.transition = 'transform .12s ease-out'
    el.style.transform = `perspective(800px) rotateX(${(-py * max).toFixed(2)}deg) rotateY(${(px * max).toFixed(2)}deg) scale3d(1.02,1.02,1.02)`
  }
  const release = (e) => {
    const el = e.currentTarget
    el.style.transition = 'transform .5s cubic-bezier(.2,.9,.25,1.15)'
    el.style.transform = ''
  }
  return {
    onPointerDown: lean,
    onPointerMove: (e) => { if (e.pointerType !== 'touch' || e.buttons) lean(e) },
    onPointerUp: release,
    onPointerLeave: release,
    onPointerCancel: release,
  }
}

/**
 * Sends a little copy of the dish in an arc to the cart bar, spinning in 3D on the way.
 * It's a decoration: the item is already in the cart before this starts.
 */
export function flyToCart(fromEl, targetSelector = '.gm-cartbar') {
  if (!fromEl || reducedMotion()) return
  const target = document.querySelector(targetSelector)
  const from = fromEl.getBoundingClientRect()
  const to = target?.getBoundingClientRect() ?? { left: window.innerWidth / 2, top: window.innerHeight - 70, width: 0, height: 0 }

  const dot = document.createElement('div')
  dot.className = 'gm-fly'
  const size = 46
  dot.style.left = `${from.left + from.width / 2 - size / 2}px`
  dot.style.top = `${from.top + from.height / 2 - size / 2}px`
  dot.style.width = dot.style.height = `${size}px`
  document.body.appendChild(dot)

  const dx = to.left + to.width / 2 - (from.left + from.width / 2)
  const dy = to.top + to.height / 2 - (from.top + from.height / 2)
  const anim = dot.animate([
    { transform: 'translate3d(0,0,0) rotateY(0deg) scale(1)', opacity: 1 },
    { transform: `translate3d(${dx * 0.45}px,${dy * 0.3 - 90}px,120px) rotateY(220deg) rotateX(30deg) scale(1.15)`, opacity: 1, offset: 0.5 },
    { transform: `translate3d(${dx}px,${dy}px,0) rotateY(540deg) scale(.25)`, opacity: 0.2 },
  ], { duration: 620, easing: 'cubic-bezier(.45,.05,.3,1)' })
  anim.onfinish = () => {
    dot.remove()
    const bar = document.querySelector(targetSelector)
    bar?.animate([
      { transform: 'translateX(-50%) scale(1)' },
      { transform: 'translateX(-50%) scale(1.045)' },
      { transform: 'translateX(-50%) scale(1)' },
    ], { duration: 280, easing: 'ease-out' })
  }
  anim.oncancel = () => dot.remove()
}

/** Calls fn with how far (-1 to 1 and beyond) each child sits from the scroller's centre, once a frame while scrolling. */
export function useScrollDepth(fn, axis = 'x') {
  const ref = useRef(null)
  useEffect(() => {
    const el = ref.current
    if (!el) return undefined
    let raf = 0
    const run = () => {
      raf = 0
      const centre = axis === 'x' ? el.scrollLeft + el.clientWidth / 2 : el.scrollTop + el.clientHeight / 2
      Array.from(el.children).forEach((child, i) => {
        const c = axis === 'x' ? child.offsetLeft + child.offsetWidth / 2 : child.offsetTop + child.offsetHeight / 2
        const span = axis === 'x' ? child.offsetWidth : child.offsetHeight
        fn(child, (c - centre) / (span || 1), i)
      })
    }
    const onScroll = () => { if (!raf) raf = requestAnimationFrame(run) }
    el.addEventListener('scroll', onScroll, { passive: true })
    run()
    const again = setTimeout(run, 80)   // children may still be laying out on the first frame
    return () => { el.removeEventListener('scroll', onScroll); cancelAnimationFrame(raf); clearTimeout(again) }
  })
  return ref
}
