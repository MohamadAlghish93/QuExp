import { useState } from 'react'
import { useAuth } from '../auth/AuthContext.jsx'
import UserAutocomplete from './UserAutocomplete.jsx'

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
    <div className="modal-backdrop" onClick={onClose}>
      <form className="modal" onClick={(e) => e.stopPropagation()} onSubmit={handleSubmit}>
        <h3>Assign "{favorite.name}"</h3>
        <p className="muted">They'll get a notification now, and again each time it's due.</p>

        <label>
          Assign to
          <UserAutocomplete value={assignee} onChange={setAssignee} excludeUsernames={[myUsername]} placeholder="Type a username…" />
        </label>

        <label>
          Repeat every
          <select value={repeatIntervalDays} onChange={(e) => setRepeatIntervalDays(e.target.value)}>
            <option value={0}>Once (no repeat)</option>
            <option value={1}>Day</option>
            <option value={7}>Week</option>
            <option value={30}>Month</option>
          </select>
        </label>

        <label>
          Note (optional)
          <input value={note} onChange={(e) => setNote(e.target.value)} placeholder="Check for anything unusual" />
        </label>

        {error && <div className="error-banner">{error}</div>}

        <div className="modal-actions">
          <button type="button" className="secondary" onClick={onClose}>Cancel</button>
          <button type="submit" disabled={!assignee}>Assign</button>
        </div>
      </form>
    </div>
  )
}
