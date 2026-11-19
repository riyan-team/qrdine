// The same phone rule the server uses (Application/Common/Phone.cs): digits, with spaces, dashes,
// brackets or a leading + allowed, and 7 to 15 digits in all. Fits "0300 1234567", "+92 300 1234567"
// and "021-1234567".
export const PHONE_MESSAGE = 'Enter a phone number with 7 to 15 digits.'

export function isPhone(raw) {
  const s = String(raw ?? '').trim()
  if (!s || !/^[0-9+\-() ]+$/.test(s)) return false
  const digits = s.replace(/\D/g, '').length
  return digits >= 7 && digits <= 15
}

// For optional phone boxes: blank is fine, anything typed must be a real number.
export const phoneProblem = (raw) => (String(raw ?? '').trim() === '' || isPhone(raw) ? '' : PHONE_MESSAGE)

// Staff sign in with a mobile number or a short name without spaces (StaffUsername on the server).
export function usernameProblem(raw) {
  const u = String(raw ?? '').trim()
  if (u.length < 3) return 'Username must be at least 3 characters.'
  if (u.length > 60) return 'Username must be 60 characters or fewer.'
  if (/^[a-zA-Z0-9_.-]+$/.test(u) || isPhone(u)) return ''
  return 'Use a mobile number, or letters, numbers, dots, dashes and underscores (no spaces).'
}
