import { useRef, useState, useCallback, useMemo } from 'react'
import { formatDateField } from '../utils/dateFieldFormat.js'

const DEFAULT_WIDTH = 160
const MIN_WIDTH = 60

// dateFieldFormats: optional array of { columnName, sourceFormat, displayFormat } -
// matching columns get their raw value reformatted for display (e.g. a raw
// "20260809220911" column shown as "2026/08/09 22:09:11"). The underlying row
// data handed to callers (export, PDF generation, etc.) is never touched - only
// what's drawn in this table.
export default function ResultGrid({ columns, rows, rowActions, dateFieldFormats }) {
  const [widths, setWidths] = useState({})
  const resizeState = useRef(null) // { column, startX, startWidth }

  const dateFormatByColumn = useMemo(() => {
    const map = {}
    for (const f of dateFieldFormats || []) map[f.columnName] = f
    return map
  }, [dateFieldFormats])

  const getWidth = (col) => widths[col] ?? DEFAULT_WIDTH

  const onMouseDown = useCallback((col, e) => {
    e.preventDefault()
    resizeState.current = { column: col, startX: e.clientX, startWidth: getWidth(col) }
    window.addEventListener('mousemove', onMouseMove)
    window.addEventListener('mouseup', onMouseUp)
  }, [widths]) // eslint-disable-line react-hooks/exhaustive-deps

  const onMouseMove = useCallback((e) => {
    const state = resizeState.current
    if (!state) return
    const delta = e.clientX - state.startX
    const newWidth = Math.max(MIN_WIDTH, state.startWidth + delta)
    setWidths((w) => ({ ...w, [state.column]: newWidth }))
  }, [])

  const onMouseUp = useCallback(() => {
    resizeState.current = null
    window.removeEventListener('mousemove', onMouseMove)
    window.removeEventListener('mouseup', onMouseUp)
  }, [onMouseMove])

  if (!columns.length) {
    return <p className="muted">Run a query to see results here.</p>
  }

  function displayValue(col, raw) {
    const fmt = dateFormatByColumn[col]
    if (!fmt || raw === null || raw === undefined) return raw
    return formatDateField(raw, fmt.sourceFormat, fmt.displayFormat)
  }

  return (
    <div className="grid-wrapper">
      <table className="result-grid" style={{ tableLayout: 'fixed' }}>
        <colgroup>
          {columns.map((col) => (
            <col key={col} style={{ width: `${getWidth(col)}px` }} />
          ))}
        </colgroup>
        <thead>
          <tr>
            {columns.map((col) => (
              <th key={col}>
                <div className="th-content">
                  <span className="th-label" title={dateFormatByColumn[col] ? `${col} (formatted as date)` : col}>
                    {col}{dateFormatByColumn[col] ? ' 📅' : ''}
                  </span>
                  <span
                    className="col-resize-handle"
                    onMouseDown={(e) => onMouseDown(col, e)}
                    role="separator"
                    aria-orientation="vertical"
                    aria-label={`Resize ${col} column`}
                  />
                </div>
              </th>
            ))}
            {rowActions && <th className="row-actions-header"></th>}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => (
            <tr key={i}>
              {columns.map((col) => {
                const display = displayValue(col, row[col])
                return <td key={col} title={formatTitle(row[col])}>{formatCell(display)}</td>
              })}
              {rowActions && <td className="row-actions-cell">{rowActions(row)}</td>}
            </tr>
          ))}
        </tbody>
      </table>
      {rows.length === 0 && <p className="muted">No rows in this page.</p>}
    </div>
  )
}

function formatCell(value) {
  if (value === null || value === undefined) return <span className="null-cell">NULL</span>
  return String(value)
}

function formatTitle(value) {
  return value === null || value === undefined ? 'NULL' : String(value)
}
