import CloudscapeSelect from './CloudscapeSelect.jsx'
import CloudscapeButton from './CloudscapeButton.jsx'
import CloudscapeInput from './CloudscapeInput.jsx'
import { useEffect, useState } from 'react'
import Table from '@cloudscape-design/components/table'
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
          <CloudscapeSelect value={column} onChange={(e) => setColumn(e.target.value)}>
            <option value="">Select a column…</option>
            {columns.map((c) => <option key={c} value={c}>{c}</option>)}
          </CloudscapeSelect>
        </label>
        <CloudscapeButton onClick={() => onGroupBy(column)} disabled={!column || loading}>
          {loading ? 'Counting…' : 'Count rows per value'}
        </CloudscapeButton>
        {result && onPin && (
          <CloudscapeButton className="secondary" onClick={() => onPin(result.column)} disabled={pinning}>
            {pinning ? 'Pinning…' : '📌 Pin to dashboard'}
          </CloudscapeButton>
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
          <Table
            variant="embedded"
            items={result.groups}
            columnDefinitions={[
              {
                id: 'value',
                header: result.column,
                cell: (group) => group.value === null ? <span className="null-cell">NULL</span> : String(group.value),
              },
              {
                id: 'meaning',
                header: 'Meaning',
                cell: (group) => (
                  <CloudscapeInput
                    className="legend-input"
                    value={legend[String(group.value)] || ''}
                    onChange={(e) => handleMeaningChange(group.value, e.target.value)}
                    placeholder="e.g. Pending"
                  />
                ),
              },
              { id: 'count', header: 'Count', cell: (group) => group.count.toLocaleString() },
            ]}
          />
        </div>
      )}
    </div>
  )
}
