import { useEffect, useState } from 'react'
import { getLegend, setLegendEntry } from '../utils/valueLegend.js'

// Runs GROUP BY + COUNT(*) server-side across the WHOLE result set, not just the
// page currently loaded in the grid - otherwise counts on a million-row query
// would only reflect the visible 100 rows and be misleading.
export default function GroupByPanel({ columns, onGroupBy, result, loading, error, onPin, pinning, connectionId }) {
  const [column, setColumn] = useState('')
  const [legend, setLegend] = useState({})

  // Reload the saved legend for this column whenever a new group-by result comes in.
  useEffect(() => {
    if (result && connectionId) setLegend(getLegend(connectionId, result.column))
  }, [result, connectionId])

  function handleMeaningChange(value, meaning) {
    setLegendEntry(connectionId, result.column, value, meaning)
    setLegend((prev) => {
      const next = { ...prev }
      const key = String(value)
      if (meaning.trim()) next[key] = meaning
      else delete next[key]
      return next
    })
  }

  if (!columns.length) return null

  return (
    <div className="groupby-panel">
      <div className="toolbar">
        <label className="inline-label">
          Group by column
          <select value={column} onChange={(e) => setColumn(e.target.value)}>
            <option value="">Select a column…</option>
            {columns.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </label>
        <button onClick={() => onGroupBy(column)} disabled={!column || loading}>
          {loading ? 'Counting…' : 'Count rows per value'}
        </button>
        {result && onPin && (
          <button className="secondary" onClick={() => onPin(result.column)} disabled={pinning}>
            {pinning ? 'Pinning…' : '📌 Pin to dashboard'}
          </button>
        )}
      </div>

      {error && <div className="error-banner">{error}</div>}

      {result && (
        <div className="groupby-result">
          <p className="muted">
            {result.groups.length} distinct value{result.groups.length === 1 ? '' : 's'} in
            {' '}<strong>{result.column}</strong> across the full result set
            {result.truncated && ' (top values only — list was truncated)'}
            {' '}· {result.elapsedMs}ms
          </p>
          <p className="muted groupby-legend-hint">
            If this column holds coded values (e.g. status 1, 2, 3), type what each one means
            in the "Meaning" column below — saved in your browser for next time.
          </p>
          <table className="result-grid groupby-table">
            <thead>
              <tr>
                <th>{result.column}</th>
                <th>Meaning</th>
                <th>Count</th>
              </tr>
            </thead>
            <tbody>
              {result.groups.map((g, i) => (
                <tr key={i}>
                  <td>{g.value === null ? <span className="null-cell">NULL</span> : g.value}</td>
                  <td>
                    <input
                      className="legend-input"
                      value={legend[String(g.value)] || ''}
                      onChange={(e) => handleMeaningChange(g.value, e.target.value)}
                      placeholder="e.g. Pending"
                    />
                  </td>
                  <td>{g.count.toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
