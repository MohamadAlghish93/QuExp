import { useEffect, useRef, useState, useCallback, useMemo } from 'react'
import { formatDateField } from '../utils/dateFieldFormat.js'

const DEFAULT_WIDTH = 160
const MIN_WIDTH = 60

// dateFieldFormats: optional array of { columnName, sourceFormat, displayFormat } -
// matching columns get their raw value reformatted for display (e.g. a raw
// "20260809220911" column shown as "2026/08/09 22:09:11"). The underlying row
// data handed to callers (export, PDF generation, etc.) is never touched - only
// what's drawn in this table.
//
// Search: the box above the table filters the rows you can see. Type one or several words - a row
// is kept when EVERY word appears somewhere in it (any column, matched against what's displayed, so a
// reformatted date is searched the way it reads). It searches the rows on the current page only; the
// rows handed in are never changed, so row actions (PDF, export) always get the original row.
export default function ResultGrid({ columns, rows, rowActions, dateFieldFormats }) {
  const [widths, setWidths] = useState({})
  const [query, setQuery] = useState('')
  const resizeState = useRef(null) // { column, startX, startWidth }

  const dateFormatByColumn = useMemo(() => {
    const map = {}
    for (const f of dateFieldFormats || []) map[f.columnName] = f
    return map
  }, [dateFieldFormats])

  // A different set of columns means a different query - start with a clean search box.
  const columnsKey = columns.join('\u0001')
  useEffect(() => { setQuery('') }, [columnsKey])

  const terms = useMemo(() => query.trim().toLowerCase().split(/\s+/).filter(Boolean), [query])

  const visibleRows = useMemo(() => {
    if (terms.length === 0) return rows
    return rows.filter((row) => {
      const haystack = columns
        .map((col) => {
          const raw = row[col]
          if (raw === null || raw === undefined) return ''
          const fmt = dateFormatByColumn[col]
          return String(fmt ? formatDateField(raw, fmt.sourceFormat, fmt.displayFormat) : raw)
        })
        .join('\u0001')
        .toLowerCase()
      return terms.every((term) => haystack.includes(term))
    })
  }, [rows, columns, terms, dateFormatByColumn])

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

  const searching = terms.length > 0

  return (
    <div className="grid-with-search">
      {rows.length > 0 && (
        <div className="grid-search">
          <span className="grid-search-icon" aria-hidden="true">🔍</span>
          <input
            type="search"
            className="grid-search-input"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Search these rows - type a name, number, date…"
            aria-label="Search the rows in this table"
            title="Searches the rows shown on this page. Several words narrow it down."
          />
          {searching && <button type="button" className="link-button" onClick={() => setQuery('')}>clear</button>}
          <span className="muted grid-search-count">
            {searching ? `${visibleRows.length} of ${rows.length} rows match` : `${rows.length} row${rows.length === 1 ? '' : 's'}`}
          </span>
        </div>
      )}

      <div className="grid-wrapper">
        <table className="result-grid" style={{ tableLayout: 'fixed' }}>
          <colgroup>
            {rowActions && <col style={{ width: '96px' }} />}
            {columns.map((col) => (
              <col key={col} style={{ width: `${getWidth(col)}px` }} />
            ))}
          </colgroup>
          <thead>
            <tr>
              {rowActions && <th className="row-actions-header"></th>}
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
            </tr>
          </thead>
          <tbody>
            {visibleRows.map((row, i) => (
              <tr key={i}>
                {rowActions && <td className="row-actions-cell">{rowActions(row)}</td>}
                {columns.map((col) => {
                  const display = displayValue(col, row[col])
                  return <td key={col} title={formatTitle(row[col])}>{formatCell(display)}</td>
                })}
              </tr>
            ))}
          </tbody>
        </table>
        {rows.length === 0 && <p className="muted">No rows in this page.</p>}
        {rows.length > 0 && visibleRows.length === 0 && (
          <p className="muted grid-no-match">
            Nothing on this page matches “{query.trim()}”. <button type="button" className="link-button" onClick={() => setQuery('')}>Clear the search</button>
          </p>
        )}
      </div>
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
