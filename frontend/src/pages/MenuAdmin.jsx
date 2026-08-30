import { useEffect, useState } from 'react'
import { api, money } from '../api'
import { ui, toast } from '../ui.jsx'
import { friendlyError } from '../errors'

// Owner/manager menu management: categories + items, with variants and modifiers.
export default function MenuAdmin() {
  const [data, setData] = useState(null)
  const [editing, setEditing] = useState(null)   // item being edited, or 'new'
  const [importing, setImporting] = useState(false)
  const [err, setErr] = useState('')

  const load = () => api.adminMenu().then(setData).catch((e) => setErr(e.message))
  useEffect(() => { load() }, [])

  const cats = data?.menu ?? []

  const addCategory = async () => {
    const name = await ui.prompt({ title: 'New category', placeholder: 'e.g. Starters', confirmText: 'Add', required: true })
    if (!name) return
    try { await api.addCategory(name, cats.length + 1); load() } catch (e) { toast.error(friendlyError(e)) }
  }
  const renameCategory = async (c) => {
    const name = await ui.prompt({ title: 'Rename category', defaultValue: c.name, confirmText: 'Rename', required: true })
    if (!name || name === c.name) return
    try { await api.updateCategory(c.id, name, cats.indexOf(c) + 1); load() } catch (e) { toast.error(friendlyError(e)) }
  }
  const deleteCategory = async (c) => {
    if (!await ui.confirm({ title: 'Delete category', message: `Delete category "${c.name}"?`, confirmText: 'Delete', danger: true })) return
    try { await api.deleteCategory(c.id); load() } catch (e) { toast.error(friendlyError(e)) }
  }
  const toggle = async (it) => {
    try { await api.setAvailability(it.id, !it.available); load() } catch (e) { toast.error(friendlyError(e)) }
  }
  const removeItem = async (it) => {
    if (!await ui.confirm({ title: 'Delete item', message: `Delete "${it.name}"?`, confirmText: 'Delete', danger: true })) return
    try { await api.deleteItem(it.id); load() } catch (e) { toast.error(friendlyError(e)) }
  }

  if (!data) return <div className="empty">{err || 'Loading menu…'}</div>

  return (
    <div className="wrap">
      <div className="row between">
        <div className="h" style={{ margin: 0 }}>Menu management</div>
        <div className="row" style={{ gap: 8, flexWrap: 'wrap' }}>
          <button onClick={() => setImporting(true)}>⬆ Import menu</button>
          <button onClick={addCategory}>+ Category</button>
          <button className="primary" onClick={() => setEditing('new')} disabled={cats.length === 0}>+ Item</button>
        </div>
      </div>
      <p className="muted">Add or edit your menu — prices, photos (emoji), availability, sizes and add-ons. Changes are live for customers instantly.</p>

      {importing && <MenuImportModal onClose={() => setImporting(false)} onImported={() => { setImporting(false); load() }} />}

      {cats.length === 0 && <div className="empty">Add a category first, then add items to it.</div>}

      {cats.map((c) => (
        <div key={c.id} style={{ marginBottom: 18 }}>
          <div className="row between" style={{ marginBottom: 6 }}>
            <div className="cat-title" style={{ margin: 0 }}>{c.name}</div>
            <div className="row" style={{ gap: 6 }}>
              <button className="ghost" onClick={() => renameCategory(c)}>Rename</button>
              <button className="ghost" onClick={() => deleteCategory(c)}>Delete</button>
            </div>
          </div>
          <div className="card">
            {c.items.length === 0 && <div className="muted" style={{ fontSize: 13 }}>No items yet.</div>}
            {c.items.map((it) => (
              <div className="item" key={it.id}>
                <div className="emoji">{it.imageUrl
                  ? <img src={it.imageUrl} alt="" style={{ width: '100%', height: '100%', objectFit: 'cover', borderRadius: 14 }} />
                  : it.emoji}</div>
                <div style={{ flex: 1 }}>
                  <div className="name">{it.name} {!it.available && <span className="pill s-Served">out of stock</span>}
                    {it.dealPrice != null && <span className="pill s-Cancelled">🏷 deal {money(it.dealPrice)}{it.dealActive ? '' : ' (scheduled)'}</span>}</div>
                  <div className="muted" style={{ fontSize: 13 }}>
                    {it.variants?.length ? it.variants.map((v) => `${v.name} ${money(v.price)}`).join(' · ') : money(it.price)}
                    {it.modifierGroups?.length ? ` · ${it.modifierGroups.length} option group(s)` : ''}
                  </div>
                </div>
                <div className="row" style={{ gap: 6 }}>
                  <button className="ghost" onClick={() => toggle(it)}>{it.available ? 'Mark out' : 'Mark in'}</button>
                  <button onClick={() => setEditing(it)}>Edit</button>
                  <button className="ghost" onClick={() => removeItem(it)}>🗑</button>
                </div>
              </div>
            ))}
          </div>
        </div>
      ))}

      {editing && (
        <ItemEditor
          item={editing === 'new' ? null : editing}
          categories={cats}
          onClose={() => setEditing(null)}
          onSaved={() => { setEditing(null); load() }}
        />
      )}
    </div>
  )
}

const blankItem = (categoryId) => ({
  categoryId, name: '', nameUrdu: '', description: '', price: 0, emoji: '🍽️',
  available: true, prepMinutes: 15, taxRate: '', furtherTaxRate: 0, pctCode: '',
  variants: [], modifierGroups: [],
  dealPrice: '', dealDescription: '', dealDays: [], dealStart: '', dealEnd: '', dealHideWhenOff: false,
})

const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']
const minToHHMM = (m) => (m == null ? '' : `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`)
const hhmmToMin = (s) => { if (!s) return null; const [h, m] = s.split(':').map(Number); return h * 60 + (m || 0) }

function ItemEditor({ item, categories, onClose, onSaved }) {
  const [f, setF] = useState(() => item ? {
    categoryId: findCategoryId(categories, item.id) ?? categories[0].id,
    name: item.name, nameUrdu: item.nameUrdu || '', description: item.description || '',
    price: item.price, emoji: item.emoji, available: item.available, prepMinutes: item.prepMinutes,
    taxRate: item.taxRate ?? '', furtherTaxRate: item.furtherTaxRate ?? 0, pctCode: item.pctCode || '',
    dealPrice: item.dealPrice ?? '', dealDescription: item.dealDescription || '',
    dealDays: item.dealDays ? item.dealDays.split(',').filter(Boolean).map(Number) : [],
    dealStart: minToHHMM(item.dealStartMinutes), dealEnd: minToHHMM(item.dealEndMinutes),
    dealHideWhenOff: !!item.dealHideWhenOff,
    variants: (item.variants || []).map((v) => ({ name: v.name, price: v.price })),
    modifierGroups: (item.modifierGroups || []).map((g) => ({
      name: g.name, minSelect: g.minSelect, maxSelect: g.maxSelect,
      options: g.options.map((o) => ({ name: o.name, price: o.price })),
    })),
  } : blankItem(categories[0].id))
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')
  const [photo, setPhoto] = useState(item?.imageUrl || null)
  const [uploading, setUploading] = useState(false)
  const set = (k, v) => setF((s) => ({ ...s, [k]: v }))

  const uploadPhoto = async (e) => {
    const file = e.target.files?.[0]
    if (!file || !item) return
    setUploading(true)
    try { const r = await api.uploadItemPhoto(item.id, file); setPhoto(r.imageUrl) }
    catch (e) { ui.alert(e.message) }
    setUploading(false)
  }

  // variant helpers
  const addVariant = () => set('variants', [...f.variants, { name: '', price: 0 }])
  const setVariant = (i, k, v) => set('variants', f.variants.map((x, n) => n === i ? { ...x, [k]: v } : x))
  const delVariant = (i) => set('variants', f.variants.filter((_, n) => n !== i))
  // group helpers
  const addGroup = () => set('modifierGroups', [...f.modifierGroups, { name: '', minSelect: 0, maxSelect: 1, options: [] }])
  const setGroup = (gi, k, v) => set('modifierGroups', f.modifierGroups.map((g, n) => n === gi ? { ...g, [k]: v } : g))
  const delGroup = (gi) => set('modifierGroups', f.modifierGroups.filter((_, n) => n !== gi))
  const addOption = (gi) => setGroup(gi, 'options', [...f.modifierGroups[gi].options, { name: '', price: 0 }])
  const setOption = (gi, oi, k, v) => setGroup(gi, 'options', f.modifierGroups[gi].options.map((o, n) => n === oi ? { ...o, [k]: v } : o))
  const delOption = (gi, oi) => setGroup(gi, 'options', f.modifierGroups[gi].options.filter((_, n) => n !== oi))

  const save = async () => {
    setBusy(true); setErr('')
    const payload = {
      categoryId: Number(f.categoryId), name: f.name.trim(), nameUrdu: f.nameUrdu.trim(),
      description: f.description.trim(), price: Number(f.price), emoji: f.emoji || '🍽️',
      available: f.available, prepMinutes: Number(f.prepMinutes),
      taxRate: f.taxRate === '' || f.taxRate === null ? null : Number(f.taxRate),
      furtherTaxRate: Number(f.furtherTaxRate) || 0, pctCode: (f.pctCode || '').trim(),
      dealPrice: f.dealPrice === '' || f.dealPrice === null ? null : Number(f.dealPrice),
      dealDescription: (f.dealDescription || '').trim(),
      dealDays: [...f.dealDays].sort((a, b) => a - b).join(','),
      dealStartMinutes: hhmmToMin(f.dealStart), dealEndMinutes: hhmmToMin(f.dealEnd),
      dealHideWhenOff: !!f.dealHideWhenOff,
      variants: f.variants.map((v, i) => ({ name: v.name.trim(), price: Number(v.price), sortOrder: i + 1 })),
      modifierGroups: f.modifierGroups.map((g, gi) => ({
        name: g.name.trim(), minSelect: Number(g.minSelect), maxSelect: Number(g.maxSelect), sortOrder: gi + 1,
        options: g.options.map((o, oi) => ({ name: o.name.trim(), price: Number(o.price), sortOrder: oi + 1 })),
      })),
    }
    try {
      if (item) await api.updateItem(item.id, payload); else await api.createItem(payload)
      onSaved()
    } catch (e) { setErr(e.message) }
    setBusy(false)
  }

  const inp = { width: '100%', marginBottom: 8 }
  return (
    <div className="overlay" onClick={(e) => { if (e.target.className === 'overlay') onClose() }}>
      <div className="modal" style={{ maxWidth: 520 }}>
        <div className="row between"><div className="big">{item ? 'Edit item' : 'New item'}</div><button className="ghost" onClick={onClose}>✕</button></div>

        {item ? (
          <div className="row" style={{ gap: 12, margin: '10px 0' }}>
            <div className="emoji" style={{ width: 64, height: 64 }}>
              {photo ? <img src={photo} alt="" style={{ width: '100%', height: '100%', objectFit: 'cover', borderRadius: 14 }} /> : (item.emoji || '🍽️')}
            </div>
            <label className="ghost" style={{ cursor: 'pointer', border: '1px solid var(--line)', padding: '8px 12px', borderRadius: 10 }}>
              {uploading ? 'Uploading…' : '📷 Upload photo'}
              <input type="file" accept="image/*" style={{ display: 'none' }} onChange={uploadPhoto} disabled={uploading} />
            </label>
          </div>
        ) : (
          <div className="muted" style={{ fontSize: 12, margin: '8px 0' }}>💡 Save the item first, then re-open it to add a photo.</div>
        )}

        <label className="muted" style={{ fontSize: 12 }}>Category</label>
        <select value={f.categoryId} onChange={(e) => set('categoryId', e.target.value)} style={inp}>
          {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </select>

        <div className="row" style={{ gap: 8 }}>
          <input placeholder="Name (English)" value={f.name} onChange={(e) => set('name', e.target.value)} style={{ ...inp, flex: 2 }} />
          <input placeholder="اردو نام" value={f.nameUrdu} onChange={(e) => set('nameUrdu', e.target.value)} style={{ ...inp, flex: 1 }} />
        </div>
        <input placeholder="Description" value={f.description} onChange={(e) => set('description', e.target.value)} style={inp} />
        <div className="row" style={{ gap: 8 }}>
          <input placeholder="Emoji" value={f.emoji} onChange={(e) => set('emoji', e.target.value)} style={{ ...inp, width: 70 }} />
          <input type="number" placeholder="Base price" value={f.price} onChange={(e) => set('price', e.target.value)} style={inp} />
          <input type="number" placeholder="Prep min" value={f.prepMinutes} onChange={(e) => set('prepMinutes', e.target.value)} style={{ ...inp, width: 90 }} />
        </div>
        <label className="muted" style={{ fontSize: 12 }}>Tax (for FBR — leave rate blank to use the restaurant default)</label>
        <div className="row" style={{ gap: 8 }}>
          <input type="number" placeholder="Tax %" value={f.taxRate} onChange={(e) => set('taxRate', e.target.value)} style={{ ...inp, flex: 1 }} />
          <input type="number" placeholder="Further tax %" value={f.furtherTaxRate} onChange={(e) => set('furtherTaxRate', e.target.value)} style={{ ...inp, flex: 1 }} />
          <input placeholder="PCT / HS code" value={f.pctCode} onChange={(e) => set('pctCode', e.target.value)} style={{ ...inp, flex: 1 }} />
        </div>
        <label className="row" style={{ gap: 8, marginBottom: 8, cursor: 'pointer' }}>
          <input type="checkbox" checked={f.available} onChange={(e) => set('available', e.target.checked)} /> Available
        </label>
        {f.variants.length > 0 && <div className="muted" style={{ fontSize: 12 }}>If sizes are set, the base price is ignored.</div>}

        {/* Deal / offer */}
        <div className="cat-title" style={{ fontSize: 14 }}>Deal / offer <span className="muted" style={{ fontWeight: 400, fontSize: 12 }}>(optional — shows an “OFF” price)</span></div>
        <div className="row" style={{ gap: 8 }}>
          <input type="number" placeholder="Deal price (below base)" value={f.dealPrice} onChange={(e) => set('dealPrice', e.target.value)} style={{ ...inp, flex: 1 }} />
          <input placeholder="What’s included (combo)" value={f.dealDescription} onChange={(e) => set('dealDescription', e.target.value)} style={{ ...inp, flex: 2 }} />
        </div>
        {f.dealPrice !== '' && Number(f.dealPrice) >= 0 && (
          <div style={{ background: 'var(--surface2)', borderRadius: 10, padding: 10, marginBottom: 8 }}>
            <label className="muted" style={{ fontSize: 12 }}>Active days (none = every day)</label>
            <div className="row" style={{ gap: 4, flexWrap: 'wrap', margin: '4px 0 8px' }}>
              {DAYS.map((d, idx) => (
                <button key={idx} className={f.dealDays.includes(idx) ? 'primary' : 'ghost'} style={{ padding: '4px 9px' }}
                  onClick={() => set('dealDays', f.dealDays.includes(idx) ? f.dealDays.filter((x) => x !== idx) : [...f.dealDays, idx])}>{d}</button>
              ))}
            </div>
            <div className="row" style={{ gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
              <span className="muted" style={{ fontSize: 12 }}>Time</span>
              <input type="time" value={f.dealStart} onChange={(e) => set('dealStart', e.target.value)} />
              <span className="muted">to</span>
              <input type="time" value={f.dealEnd} onChange={(e) => set('dealEnd', e.target.value)} />
              <span className="muted" style={{ fontSize: 11 }}>(blank = all day)</span>
            </div>
            <label className="row" style={{ gap: 8, marginTop: 8, cursor: 'pointer' }}>
              <input type="checkbox" checked={f.dealHideWhenOff} onChange={(e) => set('dealHideWhenOff', e.target.checked)} />
              <span style={{ fontSize: 13 }}>Hide this item when the deal isn’t active</span>
            </label>
          </div>
        )}

        {/* Variants */}
        <div className="cat-title" style={{ fontSize: 14 }}>Sizes / variants</div>
        {f.variants.map((v, i) => (
          <div key={i} className="row" style={{ gap: 8, marginBottom: 6 }}>
            <input placeholder="e.g. Half" value={v.name} onChange={(e) => setVariant(i, 'name', e.target.value)} style={{ flex: 2 }} />
            <input type="number" placeholder="Price" value={v.price} onChange={(e) => setVariant(i, 'price', e.target.value)} style={{ flex: 1 }} />
            <button className="ghost" onClick={() => delVariant(i)}>✕</button>
          </div>
        ))}
        <button className="ghost" onClick={addVariant} style={{ marginBottom: 10 }}>+ size</button>

        {/* Modifier groups */}
        <div className="cat-title" style={{ fontSize: 14 }}>Option groups (spice, add-ons)</div>
        {f.modifierGroups.map((g, gi) => (
          <div key={gi} className="card" style={{ padding: 10, marginBottom: 8 }}>
            <div className="row" style={{ gap: 8, marginBottom: 6 }}>
              <input placeholder="Group name" value={g.name} onChange={(e) => setGroup(gi, 'name', e.target.value)} style={{ flex: 2 }} />
              <input type="number" title="min" value={g.minSelect} onChange={(e) => setGroup(gi, 'minSelect', e.target.value)} style={{ width: 56 }} />
              <input type="number" title="max" value={g.maxSelect} onChange={(e) => setGroup(gi, 'maxSelect', e.target.value)} style={{ width: 56 }} />
              <button className="ghost" onClick={() => delGroup(gi)}>✕</button>
            </div>
            {g.options.map((o, oi) => (
              <div key={oi} className="row" style={{ gap: 8, marginBottom: 4, paddingLeft: 12 }}>
                <input placeholder="Option" value={o.name} onChange={(e) => setOption(gi, oi, 'name', e.target.value)} style={{ flex: 2 }} />
                <input type="number" placeholder="+price" value={o.price} onChange={(e) => setOption(gi, oi, 'price', e.target.value)} style={{ flex: 1 }} />
                <button className="ghost" onClick={() => delOption(gi, oi)}>✕</button>
              </div>
            ))}
            <button className="ghost" onClick={() => addOption(gi)} style={{ marginLeft: 12 }}>+ option</button>
          </div>
        ))}
        <button className="ghost" onClick={addGroup} style={{ marginBottom: 10 }}>+ option group</button>

        {err && <div style={{ color: 'var(--red)', fontSize: 14, marginBottom: 8 }}>{err}</div>}
        <div className="row" style={{ gap: 8 }}>
          <button onClick={onClose}>Cancel</button>
          <button className="green" style={{ marginLeft: 'auto' }} disabled={busy || !f.name.trim()} onClick={save}>
            {busy ? 'Saving…' : 'Save item'}
          </button>
        </div>
      </div>
    </div>
  )
}

// The admin menu carries category→items; find which category an item id sits in.
function findCategoryId(categories, itemId) {
  for (const c of categories) if (c.items.some((i) => i.id === itemId)) return c.id
  return null
}

// Bulk import: read a menu from a spreadsheet (CSV) or a photo, review, then add.
function MenuImportModal({ onClose, onImported }) {
  const [mode, setMode] = useState('csv')
  const [preview, setPreview] = useState(null)
  const [csvText, setCsvText] = useState('')
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')

  const runCsv = async () => {
    setErr(''); setBusy(true)
    try { setPreview(await api.importMenuCsv(csvText)) } catch (e) { setErr(e.message) }
    setBusy(false)
  }
  const onCsvFile = async (e) => { const f = e.target.files?.[0]; if (f) setCsvText(await f.text()) }
  const onPhoto = async (e) => {
    const f = e.target.files?.[0]; if (!f) return
    setErr(''); setBusy(true)
    try { setPreview(await api.importMenuPhoto(f)) } catch (e) { setErr(e.message) }
    setBusy(false)
  }
  const downloadTemplate = () => {
    const csv = 'Category,Item,Price,DealPrice\nStarters,Chicken Corn Soup,250,\nBiryani,Chicken Biryani,350,\nDeals,Family Deal,1500,1200\nDrinks,Soft Drink,80,\n'
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv' }))
    const a = document.createElement('a'); a.href = url; a.download = 'menu-template.csv'; a.click(); URL.revokeObjectURL(url)
  }
  const setItem = (ci, ii, k, v) => setPreview((p) => ({ ...p, categories: p.categories.map((c, x) => x !== ci ? c : { ...c, items: c.items.map((it, y) => y !== ii ? it : { ...it, [k]: v }) }) }))
  const removeItem = (ci, ii) => setPreview((p) => ({ ...p, categories: p.categories.map((c, x) => x !== ci ? c : { ...c, items: c.items.filter((_, y) => y !== ii) }).filter((c) => c.items.length) }))
  const setCat = (ci, name) => setPreview((p) => ({ ...p, categories: p.categories.map((c, x) => x !== ci ? c : { ...c, name }) }))
  const total = preview ? preview.categories.reduce((s, c) => s + c.items.length, 0) : 0

  const apply = async () => {
    setBusy(true); setErr('')
    try {
      const categories = preview.categories.map((c) => ({ name: c.name, items: c.items.map((i) => ({ name: i.name, price: Number(i.price) || 0 })) }))
      const r = await api.applyMenuImport(categories)
      await ui.alert(`Added ${r.created} item(s) to your menu.`)
      onImported()
    } catch (e) { setErr(e.message); setBusy(false) }
  }

  return (
    <div className="overlay" onMouseDown={(e) => { if (e.target.classList.contains('overlay')) onClose() }}>
      <div className="modal" style={{ maxWidth: 560 }}>
        <div className="row between"><div className="big">Import menu</div><button className="ghost" onClick={onClose}>✕</button></div>

        {!preview && (
          <>
            <div className="row" style={{ gap: 8, margin: '10px 0' }}>
              <button className={mode === 'csv' ? 'primary' : ''} onClick={() => { setMode('csv'); setErr('') }}>📄 Spreadsheet</button>
              <button className={mode === 'photo' ? 'primary' : ''} onClick={() => { setMode('photo'); setErr('') }}>📷 Photo</button>
            </div>
            {mode === 'csv' ? (
              <>
                <p className="muted" style={{ fontSize: 13 }}>Columns: <b>Category, Item, Price</b> (optional 4th column: <b>deal price</b> for offers). Paste below or upload a .csv. <button className="ghost" onClick={downloadTemplate}>Download template</button></p>
                <textarea value={csvText} onChange={(e) => setCsvText(e.target.value)} placeholder={'Category,Item,Price\nBiryani,Chicken Biryani,350'} style={{ width: '100%', minHeight: 120, marginBottom: 8 }} />
                <div className="row" style={{ gap: 8 }}>
                  <input type="file" accept=".csv,text/csv" onChange={onCsvFile} />
                  <button className="primary" style={{ marginLeft: 'auto' }} disabled={busy || !csvText.trim()} onClick={runCsv}>{busy ? 'Reading…' : 'Preview →'}</button>
                </div>
              </>
            ) : (
              <>
                <p className="muted" style={{ fontSize: 13 }}>Upload a clear photo of your printed menu — we’ll read the items and prices for you to review.</p>
                <input type="file" accept="image/*" onChange={onPhoto} />
                {busy && <div className="muted" style={{ marginTop: 8 }}>Reading the menu photo…</div>}
              </>
            )}
            {err && <div style={{ color: 'var(--red)', fontSize: 14, marginTop: 8 }}>{err}</div>}
          </>
        )}

        {preview && (
          <>
            <p className="muted" style={{ fontSize: 13, margin: '8px 0' }}>Review &amp; fix prices, then add. <b>{total}</b> item(s) across {preview.categories.length} categories.</p>
            <div style={{ maxHeight: '50vh', overflowY: 'auto' }}>
              {preview.categories.map((c, ci) => (
                <div key={ci} style={{ marginBottom: 12 }}>
                  <input value={c.name} onChange={(e) => setCat(ci, e.target.value)} style={{ fontWeight: 700, width: '100%', marginBottom: 4 }} />
                  {c.items.map((it, ii) => (
                    <div key={ii} className="row" style={{ gap: 6, marginBottom: 4 }}>
                      <input value={it.name} onChange={(e) => setItem(ci, ii, 'name', e.target.value)} style={{ flex: 1 }} />
                      <input type="number" value={it.price} onChange={(e) => setItem(ci, ii, 'price', e.target.value)} style={{ width: 90 }} />
                      <button className="ghost" onClick={() => removeItem(ci, ii)}>🗑</button>
                    </div>
                  ))}
                </div>
              ))}
            </div>
            {err && <div style={{ color: 'var(--red)', fontSize: 14, marginTop: 8 }}>{err}</div>}
            <div className="row" style={{ gap: 8, marginTop: 10 }}>
              <button onClick={() => setPreview(null)}>← Back</button>
              <button className="green" style={{ marginLeft: 'auto' }} disabled={busy || total === 0} onClick={apply}>{busy ? 'Adding…' : `Add ${total} item(s)`}</button>
            </div>
          </>
        )}
      </div>
    </div>
  )
}
