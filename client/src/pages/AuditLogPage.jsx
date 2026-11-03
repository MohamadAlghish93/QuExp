import { useEffect, useState } from 'react'
import { api } from '../api/client.js'

export default function AuditLogPage() {
  const [entries, setEntries] = useState([])
  const [error, setError] = useState(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    api.getAuditLog(200, 0)
      .then(setEntries)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false))
  }, [])

  return (
    <div className="favorites-page">
      <h2>Audit log</h2>
      <p className="muted">Admin-level actions - who did what, and when.</p>

      {error && <div className="error-banner">{error}</div>}

      {loading ? (
        <p className="muted">Loading…</p>
      ) : entries.length === 0 ? (
        <p className="muted">No admin actions logged yet.</p>
      ) : (
        <table className="result-grid audit-log-table">
          <thead>
            <tr>
              <th>When</th>
              <th>Who</th>
              <th>Action</th>
              <th>Details</th>
            </tr>
          </thead>
          <tbody>
            {entries.map((e) => (
              <tr key={e.id}>
                <td>{new Date(e.createdAtUtc).toLocaleString()}</td>
                <td>@{e.actorUsername}</td>
                <td><span className="badge">{e.action}</span></td>
                <td>{e.targetDescription}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
