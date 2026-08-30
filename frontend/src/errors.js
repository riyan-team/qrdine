// Centralised API-error handling. Every failed request is turned into an ApiError
// carrying a friendly, human message (never raw JSON, a stack trace, or an internal
// property name), the HTTP status, any per-field validation messages, and an optional
// support reference. The UI shows err.message and, for forms, maps err.fieldErrors
// onto the offending inputs.

export class ApiError extends Error {
  constructor(message, { status = 0, fieldErrors = null, reference = null } = {}) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.fieldErrors = fieldErrors   // { fieldName: "message" } | null
    this.reference = reference       // support id for 500s | null
  }
}

// Generic, friendly fallbacks per HTTP status — used only when the server didn't
// send its own friendly `detail`.
const BY_STATUS = {
  400: 'Please check the details and try again.',
  401: 'Please sign in and try again.',
  403: "You don't have permission to do that.",
  404: "We couldn't find what you were looking for.",
  409: 'That conflicts with something that already exists.',
  422: 'Please check the details and try again.',
  429: 'Too many attempts. Please wait a moment and try again.',
  500: "Something went wrong on our end. Please try again.",
  502: 'The service is temporarily unavailable. Please try again in a moment.',
  503: 'The service is temporarily unavailable. Please try again in a moment.',
  504: 'The service took too long to respond. Please try again.',
}

// Turn a ProblemDetails/ValidationProblem `errors` map into { field: firstMessage }.
function normalizeFieldErrors(errors) {
  if (!errors || typeof errors !== 'object') return null
  const out = {}
  for (const [key, val] of Object.entries(errors)) {
    const msg = Array.isArray(val) ? val[0] : (typeof val === 'string' ? val : null)
    if (msg) out[key] = msg
  }
  return Object.keys(out).length ? out : null
}

// Build an ApiError from a fetch Response (call only when !res.ok).
export async function parseError(res) {
  const status = res.status
  let body = null
  try {
    const text = await res.text()
    body = text ? JSON.parse(text) : null
  } catch { /* non-JSON body (e.g. an HTML error page) — ignore, fall back by status */ }

  const fieldErrors = body ? normalizeFieldErrors(body.errors) : null
  // Prefer the server's own friendly `detail` (our DomainException + ValidationFilter
  // messages are all human-readable). Only a plain string is trusted as user-facing.
  const detail = typeof body?.detail === 'string' && body.detail.trim() ? body.detail.trim() : null
  const message = detail || BY_STATUS[status] || 'Something went wrong. Please try again.'
  const reference = typeof body?.reference === 'string' ? body.reference : null
  return new ApiError(message, { status, fieldErrors, reference })
}

// For a thrown error of unknown origin (e.g. a network failure), produce a friendly line.
export function friendlyError(err) {
  if (err instanceof ApiError) return err.message
  // TypeError from fetch() usually means the network/server is unreachable.
  if (err instanceof TypeError) return "We couldn't reach the server. Check your connection and try again."
  return (err && err.message) ? err.message : 'Something went wrong. Please try again.'
}
