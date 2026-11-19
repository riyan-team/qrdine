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

// ---- Hybrid: this PC runs the counter and keeps the restaurant's online copy in step ----
// state: 'off' (never connected), 'connecting' (downloading the restaurant), 'ready' (the counter runs here).

export async function pcSyncStatus() {
  try {
    const res = await window.fetch('/api/pc/sync', { headers: { Accept: 'application/json' } })
    if (!res.ok || !(res.headers.get('content-type') || '').includes('json')) return null
    return await res.json()
  } catch { return null }
}

async function pcPost(path, body, token) {
  const headers = { 'Content-Type': 'application/json', Accept: 'application/json' }
  if (token) headers.Authorization = `Bearer ${token}`
  let res
  try { res = await window.fetch(path, { method: 'POST', headers, body: JSON.stringify(body ?? {}) }) }
  catch { throw new Error('Dine In on this PC is not answering. Is it still running?') }
  const out = await res.json().catch(() => null)
  if (!res.ok) throw new Error(out?.detail || out?.title || 'That did not work. Please try again.')
  return out
}

export const connectPc = (server, key, token) => pcPost('/api/pc/sync/connect', { server, key }, token)
export const syncNow = (token) => pcPost('/api/pc/sync/now', undefined, token)
