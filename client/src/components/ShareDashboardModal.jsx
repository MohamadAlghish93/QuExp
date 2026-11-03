import CloudscapeButton from './CloudscapeButton.jsx'
import { useState } from 'react'
import { api } from '../api/client.js'
import { useAuth } from '../auth/AuthContext.jsx'
import UserAutocomplete from './UserAutocomplete.jsx'
import Alert from '@cloudscape-design/components/alert'
import Modal from '@cloudscape-design/components/modal'

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
    <Modal
      visible
      size="medium"
      header={`Share "${item.title}"`}
      onDismiss={onClose}
      closeAriaLabel="Close sharing dialog"
    >
      <form className="cloudscape-modal-content" onSubmit={handleShare}>
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
                  <CloudscapeButton type="button" variant="icon" iconName="close" onClick={() => handleUnshare(u)} aria-label={`Unshare with ${u}`} />
                </span>
              ))}
            </div>
          </div>
        )}

        {error && <Alert type="error">{error}</Alert>}

        <div className="modal-actions">
          <CloudscapeButton type="button" className="secondary" onClick={onClose}>Close</CloudscapeButton>
          <CloudscapeButton type="submit" disabled={!target || saving}>{saving ? 'Sharing…' : 'Share'}</CloudscapeButton>
        </div>
      </form>
    </Modal>
  )
}
