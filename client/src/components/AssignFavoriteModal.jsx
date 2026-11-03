import CloudscapeSelect from './CloudscapeSelect.jsx'
import CloudscapeInput from './CloudscapeInput.jsx'
import CloudscapeButton from './CloudscapeButton.jsx'
import { useState } from 'react'
import { useAuth } from '../auth/AuthContext.jsx'
import UserAutocomplete from './UserAutocomplete.jsx'
import Alert from '@cloudscape-design/components/alert'
import Modal from '@cloudscape-design/components/modal'

// "@mention" a teammate on a saved query - the assign-a-favorite feature.
export default function AssignFavoriteModal({ favorite, onAssign, onClose }) {
  const { username: myUsername } = useAuth()
  const [assignee, setAssignee] = useState('')
  const [note, setNote] = useState('')
  const [repeatIntervalDays, setRepeatIntervalDays] = useState(7)
  const [error, setError] = useState(null)

  function handleSubmit(e) {
    e.preventDefault()
    if (!assignee) {
      setError('Pick a teammate to assign this to.')
      return
    }
    onAssign({ favoriteId: favorite.id, assignedToUsername: assignee, note, repeatIntervalDays: Number(repeatIntervalDays) || 0 })
  }

  return (
    <Modal
      visible
      size="medium"
      header={`Assign "${favorite.name}"`}
      onDismiss={onClose}
      closeAriaLabel="Close assignment dialog"
    >
      <form className="cloudscape-modal-content" onSubmit={handleSubmit}>
        <p className="muted">They'll get a notification now, and again each time it's due.</p>

        <label>
          Assign to
          <UserAutocomplete value={assignee} onChange={setAssignee} excludeUsernames={[myUsername]} placeholder="Type a username…" />
        </label>

        <label>
          Repeat every
          <CloudscapeSelect value={repeatIntervalDays} onChange={(e) => setRepeatIntervalDays(e.target.value)}>
            <option value={0}>Once (no repeat)</option>
            <option value={1}>Day</option>
            <option value={7}>Week</option>
            <option value={30}>Month</option>
          </CloudscapeSelect>
        </label>

        <label>
          Note (optional)
          <CloudscapeInput value={note} onChange={(e) => setNote(e.target.value)} placeholder="Check for anything unusual" />
        </label>

        {error && <Alert type="error">{error}</Alert>}

        <div className="modal-actions">
          <CloudscapeButton type="button" className="secondary" onClick={onClose}>Cancel</CloudscapeButton>
          <CloudscapeButton type="submit" disabled={!assignee}>Assign</CloudscapeButton>
        </div>
      </form>
    </Modal>
  )
}
