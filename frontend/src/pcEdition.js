// The restaurant PC edition (DineIn.exe on the counter PC). Online, /api/licence doesn't exist, so the
// app's own page comes back instead of JSON and this says "online": nothing below changes anything there.

let edition = null   // Promise of the licence view, or null when online

export function getEdition(fresh = false) {
  if (!edition || fresh) {
    edition = window.fetch('/api/licence', { headers: { Accept: 'application/json' } })
      .then(async (res) => {
        if (!res.ok || !(res.headers.get('content-type') || '').includes('json')) return null
        const body = await res.json()
        return body?.edition === 'pc' ? body : null
      })
      .catch(() => null)
  }
  return edition
}

export async function activate(key) {
  const res = await window.fetch('/api/licence', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify({ key }),
  })
  const body = await res.json().catch(() => null)
  if (!res.ok) throw new Error(body?.detail || 'The licence key was not accepted.')
  edition = Promise.resolve(body)
  return body
}

// What each state means to the person at the counter.
export const licenceNeedsKey = (l) => !!l && !l.canRead     // never activated, not genuine, another PC
export const licenceWarns = (l) => !!l && ['EndingSoon', 'Grace', 'Expired', 'ClockBack'].includes(l.state)
