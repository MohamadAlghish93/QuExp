import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client.js'
import BusyOverlay from '../components/BusyOverlay.jsx'

// Dedicated screen for reviewing every assignment - both what's assigned to you
// and what you've assigned to others - with a proper cancel action on each side.
// (The small panel on the Favorites page still shows a quick summary; this is
// the full list.)
export default function AssignmentsPage() {
  const [assignedToMe, setAssignedToMe] = useState([])
  const [assignedByMe, setAssignedByMe] = useState([])
  const [error, setError] = useState(null)
  const [message, setMessage] = useState(null)
  const [busyId, setBusyId] = useState(null) // id of the assignment with an action in flight
  const navigate = useNavigate()

  async function load() {
    try {
      const [toMe, byMe] = await Promise.all([api.getAssignedToMe(), api.getAssignedByMe()])
      setAssignedToMe(toMe)
      setAssignedByMe(byMe)
    } catch (e) {
      setError(e.message)
    }
  }

  useEffect(() => { load() }, [])

  function handleRun(a) {
    const params = new URLSearchParams({ sql: a.sql, connectionId: a.connectionId, t: String(Date.now()) })
    navigate(`/query?${params.toString()}`)
  }

  async function handleComplete(id) {
    if (busyId) return // an action is already running - ignore extra clicks
    setBusyId(id)
    setError(null)
    try {
      await api.completeAssignment(id)
      setMessage('Marked done — the person who assigned it has been notified.')
      setTimeout(() => setMessage(null), 4000)
      await load()
    } catch (e) {
      setError(e.message)
    } finally {
      setBusyId(null)
    }
  }

  async function handleCancel(id) {
    if (busyId || !confirm('Cancel this assignment?')) return
    setBusyId(id)
    try {
      await api.deleteAssignment(id)
      await load()
    } catch (e) {
      setError(e.message)
    } finally {
      setBusyId(null)
    }
  }

  return (
    <div className="favorites-page">
      {busyId && <BusyOverlay message="Saving your change…" />}
      <h2>Assignments</h2>
      <p className="muted">Everything assigned to you, and everything you've assigned to others.</p>

      {error && <div className="error-banner">{error}</div>}
      {message && <p className="test-ok">{message}</p>}

      <h3>Assigned to you</h3>
      {assignedToMe.length === 0 ? (
        <p className="muted">Nothing assigned to you right now.</p>
      ) : (
        <ul className="favorite-list">
          {assignedToMe.map((a) => (
            <li key={a.id} className="favorite-card">
              <div className="favorite-card-header">
                <strong>{a.favoriteName}</strong>
                <span className="muted">due {new Date(a.nextDueAtUtc).toLocaleDateString()}</span>
              </div>
              <p className="muted">
                From @{a.assignedByUsername}{a.note ? ` — ${a.note}` : ''}
                {a.repeatIntervalDays > 0 ? ` · repeats every ${a.repeatIntervalDays}d` : ' · one-time'}
              </p>
              <pre className="favorite-sql assignment-sql">{a.sql}</pre>
              <div className="favorite-card-actions">
                <button onClick={() => handleRun(a)}>Run</button>
                <button className="secondary" onClick={() => handleComplete(a.id)} disabled={busyId !== null}>
                  {busyId === a.id ? '⏳ Marking done…' : 'Mark done'}
                </button>
                <button className="secondary danger" onClick={() => handleCancel(a.id)} disabled={busyId !== null}>
                  {busyId === a.id ? 'Working…' : 'Cancel'}
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}

      <h3 style={{ marginTop: 24 }}>You assigned</h3>
      {assignedByMe.length === 0 ? (
        <p className="muted">You haven't assigned anything yet — use "@ Assign" on a favorite.</p>
      ) : (
        <ul className="favorite-list">
          {assignedByMe.map((a) => (
            <li key={a.id} className="favorite-card">
              <div className="favorite-card-header">
                <strong>{a.favoriteName}</strong>
                <span className="muted">to @{a.assignedToUsername}</span>
              </div>
              <p className="muted">
                Next due {new Date(a.nextDueAtUtc).toLocaleDateString()}
                {a.repeatIntervalDays > 0 ? ` · repeats every ${a.repeatIntervalDays}d` : ' · one-time'}
                {' · '}
                {a.completionHistory && a.completionHistory.length > 0
                  ? `completed ${a.completionHistory.length}x, last ${new Date(a.lastCompletedAtUtc).toLocaleString()}`
                  : 'not completed yet'}
              </p>
              <div className="favorite-card-actions">
                <button className="secondary danger" onClick={() => handleCancel(a.id)} disabled={busyId !== null}>
                  {busyId === a.id ? 'Working…' : 'Cancel'}
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
