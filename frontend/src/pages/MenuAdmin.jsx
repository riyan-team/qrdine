import { useEffect, useState } from 'react'
import { api, money } from '../api'

// Owner/manager menu management: categories + items, with variants and modifiers.
export default function MenuAdmin() {
  const [data, setData] = useState(null)
  const [editing, setEditing] = useState(null)   // item being edited, or 'new'
  const [err, setErr] = useState('')

  const load = () => api.adminMenu().then(setData).catch((e) => setErr(e.message))
  useEffect(() => { load() }, [])

  const cats = data?.menu ?? []

  const addCategory = async () => {
    const name = window.prompt('New category name:')
    if (!name) return
    await api.addCategory(name.trim(), cats.length + 1); load()
  }
  const renameCategory = async (c) => {
    const name = window.prompt('Rename category:', c.name)
    if (!name || name === c.name) return
    await api.updateCategory(c.id, name.trim(), cats.indexOf(c) + 1); load()
  }
  const deleteCategory = async (c) => {
    if (!window.confirm(`Delete category "${c.name}"?`)) return
    try { await api.deleteCategory(c.id); load() } catch (e) { alert(e.message) }
  }
  const toggle = async (it) => { await api.setAvailability(it.id, !it.available); load() }
  const removeItem = async (it) => { if (window.confirm(`Delete "${it.name}"?`)) { await api.deleteItem(it.id); load() } }

  if (!data) return <div className="empty">{err || 'Loading menu…'}</div>

  return (
    <div className="wrap">
      <div className="row between">
        <div className="h" style={{ margin: 0 }}>Menu management</div>
        <div className="row" style={{ gap: 8 }}>
          <button onClick={addCategory}>+ Category</button>
          <button className="primary" onClick={() => setEditing('new')} disabled={cats.length === 0}>+ Item</button>
        </div>
      </div>
      <p className="muted">Add or edit your menu — prices, photos (emoji), availability, sizes and add-ons. Changes are live for customers instantly.</p>

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
                  <div className="name">{it.name} {!it.available && <span className="pill s-Served">out of stock</span>}</div>
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
})

function ItemEditor({ item, categories, onClose, onSaved }) {
  const [f, setF] = useState(() => item ? {
    categoryId: findCategoryId(categories, item.id) ?? categories[0].id,
    name: item.name, nameUrdu: item.nameUrdu || '', description: item.description || '',
    price: item.price, emoji: item.emoji, available: item.available, prepMinutes: item.prepMinutes,
    taxRate: item.taxRate ?? '', furtherTaxRate: item.furtherTaxRate ?? 0, pctCode: item.pctCode || '',
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
    catch (e) { alert(e.message) }
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
