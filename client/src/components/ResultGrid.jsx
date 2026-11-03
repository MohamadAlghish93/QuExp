import CloudscapeButton from './CloudscapeButton.jsx'
import CloudscapeInput from './CloudscapeInput.jsx'
import Box from '@cloudscape-design/components/box'
import Table from '@cloudscape-design/components/table'
import { useEffect, useState, useMemo } from 'react'
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

  if (!columns.length) {
    return <p className="muted">Run a query to see results here.</p>
  }

  function displayValue(col, raw) {
    const fmt = dateFormatByColumn[col]
    if (!fmt || raw === null || raw === undefined) return raw
    return formatDateField(raw, fmt.sourceFormat, fmt.displayFormat)
  }

  const searching = terms.length > 0
  const columnDefinitions = [
    ...(rowActions ? [{ id: 'actions', header: '', cell: (row) => rowActions(row), width: 96, minWidth: 96 }] : []),
    ...columns.map((col) => ({
      id: col,
      header: (
        <span title={dateFormatByColumn[col] ? `${col} (formatted as date)` : col}>
          {col}{dateFormatByColumn[col] ? ' 📅' : ''}
        </span>
      ),
      cell: (row) => formatCell(displayValue(col, row[col])),
      width: widths[col] ?? DEFAULT_WIDTH,
      minWidth: MIN_WIDTH,
    })),
  ]
  const actionColumnCount = rowActions ? 1 : 0

  return (
    <div className="grid-with-search">
      {rows.length > 0 && (
        <div className="grid-search">
          <span className="grid-search-icon" aria-hidden="true">🔍</span>
          <CloudscapeInput
            type="search"
            className="grid-search-input"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Search these rows - type a name, number, date…"
            aria-label="Search the rows in this table"
            title="Searches the rows shown on this page. Several words narrow it down."
          />
          {searching && <CloudscapeButton type="button" className="link-button" onClick={() => setQuery('')}>clear</CloudscapeButton>}
          <span className="muted grid-search-count">
            {searching ? `${visibleRows.length} of ${rows.length} rows match` : `${rows.length} row${rows.length === 1 ? '' : 's'}`}
          </span>
        </div>
      )}

      <div className="grid-wrapper">
        <Table
          variant="embedded"
          contentDensity="compact"
          columnDefinitions={columnDefinitions}
          items={visibleRows}
          resizableColumns
          onColumnWidthsChange={({ detail }) => {
            setWidths((current) => {
              const next = { ...current }
              detail.widths.slice(actionColumnCount).forEach((width, index) => {
                if (columns[index]) next[columns[index]] = width
              })
              return next
            })
          }}
          ariaLabels={{
            tableLabel: 'Query results',
            resizerTooltipText: 'Resize column',
            resizerRoleDescription: 'Column resizer',
          }}
          empty={
            <Box textAlign="center">
              {rows.length === 0 ? 'No rows in this page.' : (
                <>
                  Nothing on this page matches “{query.trim()}”.{' '}
                  <CloudscapeButton type="button" className="link-button" onClick={() => setQuery('')}>
                    Clear the search
                  </CloudscapeButton>
                </>
              )}
            </Box>
          }
        />
      </div>
    </div>
  )
}

function formatCell(value) {
  if (value === null || value === undefined) return <span className="null-cell">NULL</span>
  return String(value)
}
