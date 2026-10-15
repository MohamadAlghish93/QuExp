import { useEffect, useMemo, useState } from 'react'
import { api } from '../api/client.js'

// Every error that reached a user (reported by their browser) plus every unhandled server
// exception (with stack trace), in one place for admins.
export default function ErrorLogPage() {
  const [entries, setEntries] = useState([])
  const [source, setSource] = useState('')
  const [search, setSearch] = useState('')
  const [expandedId, setExpandedId] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)

  async function load() {
    setLoading(true)
    try {
      setEntries(await api.getErrorLog(source))
      setError(null)
    } catch (e) {
      setError(e.message)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [source]) // eslint-disable-line react-hooks/exhaustive-deps

  const visible = useMemo(() => {
    const needle = search.trim().toLowerCase()
    if (!needle) return entries
    return entries.filter((e) =>
      e.message.toLowerCase().includes(needle) ||
      e.username.toLowerCase().includes(needle) ||
      (e.path || '').toLowerCase().includes(needle))
  }, [entries, search])

  async function handleClear() {
    if (!confirm('Delete the entire error log? This cannot be undone.')) return
    await api.clearErrorLog()
    load()
  }

  return (
    <div className="favorites-page">
      <div className="dashboard-header">
        <h2>Error log</h2>
        <div className="toolbar" style={{ margin: 0 }}>
          <button className="secondary" onClick={load} disabled={loading}>{loading ? 'Loading…' : '↻ Refresh'}</button>
          {entries.length > 0 && <button className="secondary danger" onClick={handleClear}>Clear log</button>}
        </div>
      </div>
      <p className="muted">
        Errors users saw in the app (<strong>Client</strong>) and unexpected server failures with
        full stack traces (<strong>Server</strong>). Routine sign-in mistakes and expired sessions aren't included.
      </p>

      {error && <div className="error-banner">{error}</div>}

      <div className="toolbar">
        <select value={source} onChange={(e) => setSource(e.target.value)}>
          <option value="">All sources</option>
          <option value="Client">Client (what users saw)</option>
          <option value="Server">Server (unhandled exceptions)</option>
        </select>
        <input placeholder="Search message, user, or path…" value={search} onChange={(e) => setSearch(e.target.value)} />
        <span className="muted">{visible.length} shown</span>
      </div>

      {!loading && visible.length === 0 ? (
        <p className="muted">No errors logged. 🎉</p>
      ) : (
        <ul className="favorite-list">
          {visible.map((e) => (
            <li key={e.id} className={e.source === 'Server' ? 'favorite-card history-failed' : 'favorite-card'}>
              <div className="favorite-card-header">
                <span>
                  <span className={e.source === 'Server' ? 'badge badge-rejected' : 'badge'}>{e.source}</span>
                  {e.statusCode ? <span className="badge badge-muted" style={{ marginLeft: 6 }}>{e.statusCode}</span> : null}
                </span>
                <span className="muted">{new Date(e.createdAtUtc).toLocaleString()} · @{e.username}</span>
              </div>
              <p className="error-log-message">{e.message}</p>
              {e.path && <p className="muted connection-detail">{e.method} {e.path}</p>}
              {e.details && (
                <>
                  <button className="link-button" onClick={() => setExpandedId(expandedId === e.id ? null : e.id)}>
                    {expandedId === e.id ? 'hide stack trace' : 'show stack trace'}
                  </button>
                  {expandedId === e.id && <pre className="favorite-sql error-log-stack">{e.details}</pre>}
                </>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
