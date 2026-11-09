// Shrinks a picture in the browser before it is uploaded, so a 6 MB phone photo becomes
// roughly 150 KB: menus open faster on mobile data and the database stays small.
// If the browser can't read the file (e.g. HEIC on Windows) the original is returned and
// the server's own checks give the friendly message. GIFs are left alone (they may animate).
export async function shrinkImage(file, { maxSide = 1000, quality = 0.8, keepTransparency = false } = {}) {
  if (!file?.type?.startsWith('image/') || file.type === 'image/gif') return file
  const url = URL.createObjectURL(file)
  try {
    // An <img> rather than createImageBitmap: it reliably applies the phone's rotation flag,
    // which would otherwise be lost and leave photos sideways.
    const img = new Image()
    img.src = url
    await img.decode()

    const scale = Math.min(1, maxSide / Math.max(img.naturalWidth, img.naturalHeight))
    const canvas = document.createElement('canvas')
    canvas.width = Math.round(img.naturalWidth * scale)
    canvas.height = Math.round(img.naturalHeight * scale)
    const ctx = canvas.getContext('2d')

    const transparent = keepTransparency && file.type !== 'image/jpeg'
    if (!transparent) {
      // JPEG has no transparency: without this, clear areas of a PNG turn black.
      ctx.fillStyle = '#fff'
      ctx.fillRect(0, 0, canvas.width, canvas.height)
    }
    ctx.drawImage(img, 0, 0, canvas.width, canvas.height)

    const type = transparent ? 'image/png' : 'image/jpeg'
    const blob = await new Promise((done) => canvas.toBlob(done, type, quality))
    if (!blob || blob.size >= file.size) return file   // already small: keep the original
    const name = file.name.replace(/\.[^.]+$/, '') + (transparent ? '.png' : '.jpg')
    return new File([blob], name, { type })
  } catch {
    return file
  } finally {
    URL.revokeObjectURL(url)
  }
}
