import { useState } from 'react'
import { api } from '../api/client.js'
import { useAuth } from '../auth/AuthContext.jsx'
import UserAutocomplete from './UserAutocomplete.jsx'

export default function ShareDashboardModal({ item, onShared, onClose }) {
  const { username: myUsername } = useAuth()
  const [target, setTarget] = useState('')
  const [error, setError] = useState(null)
  const [saving, setSaving] = useState(false)

  async function handleShare(e) {
    e.preventDefault()
    if (!target) return
    setSaving(true)
    setError(null)
    try {
      await api.shareDashboardItem(item.id, target)
      onShared()
    } catch (e) {
      setError(e.message)
    } finally {
      setSaving(false)
    }
  }

  async function handleUnshare(username) {
    try {
      await api.unshareDashboardItem(item.id, username)
      onShared()
    } catch (e) {
      setError(e.message)
    }
  }

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <form className="modal" onClick={(e) => e.stopPropagation()} onSubmit={handleShare}>
        <h3>Share "{item.title}"</h3>
        <p className="muted">This tile will also appear on their dashboard, kept live the same way it is on yours.</p>

        <label>
          Share with
          <UserAutocomplete value={target} onChange={setTarget} excludeUsernames={[myUsername]} placeholder="Type a username…" />
        </label>

        {item.sharedWithUsernames?.length > 0 && (
          <div>
            <p className="field-hint hint-existing">Already shared with:</p>
            <div className="tag-list">
              {item.sharedWithUsernames.map((u) => (
                <span key={u} className="tag tag-removable">
                  @{u}
                  <button type="button" onClick={() => handleUnshare(u)} aria-label={`Unshare with ${u}`}>×</button>
                </span>
              ))}
            </div>
          </div>
        )}

        {error && <div className="error-banner">{error}</div>}

        <div className="modal-actions">
          <button type="button" className="secondary" onClick={onClose}>Close</button>
          <button type="submit" disabled={!target || saving}>{saving ? 'Sharing…' : 'Share'}</button>
        </div>
      </form>
    </div>
  )
}
