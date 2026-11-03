import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client.js'

export default function HistoryPage() {
  const [entries, setEntries] = useState([])
  const [error, setError] = useState(null)
  const [loading, setLoading] = useState(true)
  const navigate = useNavigate()

  async function load() {
    setLoading(true)
    try {
      setEntries(await api.getHistory(100, 0))
    } catch (e) {
      setError(e.message)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [])

  function runAgain(entry) {
    const params = new URLSearchParams({
      sql: entry.sql,
      connectionId: entry.connectionId,
      t: String(Date.now()),
    })
    navigate(`/query?${params.toString()}`)
  }

  async function handleClear() {
    if (!confirm('Clear your entire query history? This cannot be undone.')) return
    await api.clearHistory()
    load()
  }

  return (
    <div className="history-page">
      <div className="dashboard-header">
        <h2>Query history</h2>
        {entries.length > 0 && (
          <button className="secondary danger" onClick={handleClear}>Clear history</button>
        )}
      </div>
      <p className="muted">Every query you've run, most recent first. Distinct from Favorites — this is automatic.</p>

      {error && <div className="error-banner">{error}</div>}

      {loading ? (
        <p className="muted">Loading…</p>
      ) : entries.length === 0 ? (
        <p className="muted">No queries run yet.</p>
      ) : (
        <ul className="favorite-list">
          {entries.map((entry) => (
            <li key={entry.id} className={entry.success ? 'favorite-card' : 'favorite-card history-failed'}>
              <div className="favorite-card-header">
                <span className="muted">{new Date(entry.executedAtUtc).toLocaleString()}</span>
                <span className="badge">{entry.connectionName}</span>
              </div>
              <pre className="favorite-sql">{entry.sql}</pre>
              <p className="muted history-meta">
                {entry.success
                  ? <>✓ {entry.rowCount?.toLocaleString() ?? '?'} row(s) · {entry.durationMs}ms</>
                  : <span className="history-error">✗ {entry.error}</span>}
              </p>
              <div className="favorite-card-actions">
                <button onClick={() => runAgain(entry)}>Run again</button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
