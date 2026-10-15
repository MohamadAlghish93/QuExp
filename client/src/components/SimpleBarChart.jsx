import { getLegend } from '../utils/valueLegend.js'

// Deliberately not a charting library - just horizontal bars sized by percentage
// of the largest value. Keeps the dashboard dependency-free and fast to render
// for the small top-10 group-by results it's built for.
//
// connectionId/column let this look up the same "meaning" labels (1 -> "Pending")
// the user already defined in the query page's GroupByPanel - previously the
// dashboard only ever showed the raw coded value, even after a meaning was saved.
export default function SimpleBarChart({ groups, connectionId, column }) {
  if (!groups || groups.length === 0) return <p className="muted">No data.</p>

  const max = Math.max(...groups.map((g) => g.count), 1)
  const legend = connectionId && column ? getLegend(connectionId, column) : {}

  return (
    <div className="bar-chart">
      {groups.map((g, i) => {
        const meaning = legend[String(g.value)]
        return (
          <div key={i} className="bar-row">
            <span className="bar-label" title={meaning ? `${g.value} = ${meaning}` : (g.value ?? 'NULL')}>
              {meaning || (g.value ?? <em>NULL</em>)}
            </span>
            <div className="bar-track">
              <div className="bar-fill" style={{ width: `${(g.count / max) * 100}%` }} />
            </div>
            <span className="bar-count">{g.count.toLocaleString()}</span>
          </div>
        )
      })}
    </div>
  )
}
