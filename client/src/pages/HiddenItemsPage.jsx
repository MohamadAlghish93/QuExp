import { useEffect, useState } from 'react'
import { api } from '../api/client.js'

// Everything the current user has hidden from their own view, with one-click restore.
// Hiding only ever affected this user, so nothing here is lost - just out of the way.
export default function HiddenItemsPage() {
  const [templates, setTemplates] = useState([])
  const [tiles, setTiles] = useState([])
  const [busyId, setBusyId] = useState(null)
  const [error, setError] = useState(null)
  const [loading, setLoading] = useState(true)

  async function load() {
    try {
      const [t, d] = await Promise.all([api.getHiddenTemplates(), api.getHiddenDashboardItems()])
      setTemplates(t)
      setTiles(d)
    } catch (e) {
      setError(e.message)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [])

  async function restore(id, action) {
    setBusyId(id)
    setError(null)
    try { await action(id); await load() }
    catch (e) { setError(e.message) }
    finally { setBusyId(null) }
  }

  if (loading) return <p className="muted">Loading…</p>

  return (
    <div className="favorites-page">
      <h2>Hidden items</h2>
      <p className="muted">
        Things you've hidden from your own view. Restoring one puts it straight back - hiding
        never removed it for anyone else, or from the person who shared it with you.
      </p>
      {error && <div className="error-banner">{error}</div>}

      <h3>Templates</h3>
      {templates.length === 0 ? <p className="muted">No hidden templates.</p> : (
        <ul className="favorite-list">
          {templates.map((t) => (
            <li key={t.id} className="favorite-card">
              <div className="favorite-card-header">
                <strong>{t.name}</strong>
                <span className="muted">by @{t.ownerUsername}</span>
              </div>
              <p className="muted">{t.description || 'No description provided.'}</p>
              <div className="favorite-card-actions">
                <button onClick={() => restore(t.id, api.unhideTemplate)} disabled={busyId === t.id}>
                  {busyId === t.id ? 'Restoring…' : '↩ Unhide'}
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}

      <h3 style={{ marginTop: 24 }}>Dashboard tiles</h3>
      {tiles.length === 0 ? <p className="muted">No hidden dashboard tiles.</p> : (
        <ul className="favorite-list">
          {tiles.map((t) => (
            <li key={t.id} className="favorite-card">
              <div className="favorite-card-header">
                <strong>{t.title}</strong>
                <span className="muted">shared by @{t.ownerUsername}</span>
              </div>
              <p className="muted">Grouped by {t.groupByColumn}</p>
              <div className="favorite-card-actions">
                <button onClick={() => restore(t.id, api.unhideDashboardItem)} disabled={busyId === t.id}>
                  {busyId === t.id ? 'Restoring…' : '↩ Unhide'}
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
