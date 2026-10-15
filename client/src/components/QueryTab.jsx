import { useEffect, useState, useCallback, useMemo, useRef } from 'react'
import { api } from '../api/client.js'
import ResultGrid from './ResultGrid.jsx'
import SaveFavoriteModal from './SaveFavoriteModal.jsx'
import ParameterModal from './ParameterModal.jsx'
import GroupByPanel from './GroupByPanel.jsx'
import ConnectionHealthBanner from './ConnectionHealthBanner.jsx'
import { formatSql } from '../utils/sqlFormatter.js'

const PAGE_SIZE = 100
const PLACEHOLDER_REGEX = /\{\{\s*(\w+)\s*\}\}/g

function extractParamNames(sql) {
  const names = []
  for (const match of sql.matchAll(PLACEHOLDER_REGEX)) {
    if (!names.includes(match[1])) names.push(match[1])
  }
  return names
}

// One independent query editor + results panel. QueryWorkspace renders one of
// these per open tab and keeps inactive tabs mounted (just hidden) so switching
// tabs doesn't lose the SQL, results, or scroll position.
export default function QueryTab({ initialSql, initialConnectionId, showHealthBanner }) {
  const [connections, setConnections] = useState([])
  const [connectionId, setConnectionId] = useState(initialConnectionId || '')
  const [sql, setSql] = useState(initialSql || 'SELECT * FROM your_table')
  const [paramValues, setParamValues] = useState({})
  const [page, setPage] = useState({ columns: [], rows: [] })
  const [skip, setSkip] = useState(0)
  const [hasMore, setHasMore] = useState(false)
  const [totalCount, setTotalCount] = useState(null)
  const [elapsedMs, setElapsedMs] = useState(null)
  const [loading, setLoading] = useState(false)
  const [exporting, setExporting] = useState(false)
  const [error, setError] = useState(null)
  const [showSaveModal, setShowSaveModal] = useState(false)
  const [showParamModal, setShowParamModal] = useState(false)
  const [pendingAction, setPendingAction] = useState(null)
  const [groupByResult, setGroupByResult] = useState(null)
  const [groupByLoading, setGroupByLoading] = useState(false)
  const [groupByError, setGroupByError] = useState(null)
  const [pinning, setPinning] = useState(false)
  const [pinMessage, setPinMessage] = useState(null)
  const textareaRef = useRef(null)
  const autoPromptedRef = useRef(false)

  const requiredParams = useMemo(() => extractParamNames(sql), [sql])
  const hasAllValues = useCallback(
    (values) => requiredParams.every((p) => values?.[p]?.trim()),
    [requiredParams]
  )

  useEffect(() => {
    api.getConnections().then((list) => {
      setConnections(list)
      if (list.length) setConnectionId((prev) => prev || list[0].id)
    }).catch((e) => setError(e.message))
  }, [])

  // If this tab was opened from a favorite/template/history entry, either prompt for
  // parameters (if it needs any) or run it immediately (if it doesn't) - no extra click.
  useEffect(() => {
    if (initialSql && !autoPromptedRef.current) {
      const names = extractParamNames(initialSql)
      if (names.length > 0) {
        setPendingAction('run')
        setShowParamModal(true)
      } else {
        runQuery(0, {})
      }
      autoPromptedRef.current = true
    }
  }, [initialSql]) // eslint-disable-line react-hooks/exhaustive-deps

  const runQuery = useCallback(async (skipValue = 0, valuesOverride = null) => {
    const values = valuesOverride ?? paramValues
    if (!connectionId) {
      setError('Select a connection first.')
      return
    }
    if (!hasAllValues(values)) {
      setPendingAction('run')
      setShowParamModal(true)
      return
    }
    setLoading(true)
    setError(null)
    try {
      const result = await api.runPage({
        connectionId, sql, skip: skipValue, take: PAGE_SIZE, parameters: values,
      })
      setPage({ columns: result.columns, rows: result.rows })
      setSkip(skipValue)
      setHasMore(result.hasMore)
      setElapsedMs(result.elapsedMs)
      setGroupByResult(null)
      if (skipValue === 0) setTotalCount(result.estimatedTotalCount)
    } catch (e) {
      setError(e.message)
    } finally {
      setLoading(false)
    }
  }, [connectionId, sql, paramValues, hasAllValues])

  const exportCsv = useCallback(async (valuesOverride = null) => {
    const values = valuesOverride ?? paramValues
    if (!connectionId) {
      setError('Select a connection first.')
      return
    }
    if (!hasAllValues(values)) {
      setPendingAction('export')
      setShowParamModal(true)
      return
    }
    setExporting(true)
    setError(null)
    try {
      await api.exportCsv(connectionId, sql, values)
    } catch (e) {
      setError(e.message)
    } finally {
      setExporting(false)
    }
  }, [connectionId, sql, paramValues, hasAllValues])

  const handleGroupBy = useCallback(async (column) => {
    if (!column) return
    setGroupByLoading(true)
    setGroupByError(null)
    setPinMessage(null)
    try {
      const result = await api.groupBy({ connectionId, sql, column, parameters: paramValues, top: 100 })
      setGroupByResult(result)
    } catch (e) {
      setGroupByError(e.message)
    } finally {
      setGroupByLoading(false)
    }
  }, [connectionId, sql, paramValues])

  async function handlePinToDashboard(column) {
    setPinning(true)
    setPinMessage(null)
    try {
      const title = window.prompt('Title for this dashboard tile:', `${column} breakdown`)
      if (!title) { setPinning(false); return }
      await api.pinToDashboard({ title, connectionId, sql, parameters: paramValues, groupByColumn: column })
      setPinMessage('Pinned to your dashboard.')
    } catch (e) {
      setPinMessage(`Failed to pin: ${e.message}`)
    } finally {
      setPinning(false)
    }
  }

  async function handleSaveFavorite({ id, name, category, tags, isTemplate, description, sharedWithUsernames, dateFieldFormats }) {
    try {
      await api.saveFavorite({ id, name, sql, connectionId, category, tags, isTemplate, description, sharedWithUsernames, dateFieldFormats })
      setShowSaveModal(false)
    } catch (e) {
      setError(e.message)
    }
  }

  function handleParamsSubmit(values) {
    setParamValues(values)
    setShowParamModal(false)
    const action = pendingAction
    setPendingAction(null)
    if (action === 'export') exportCsv(values)
    else runQuery(0, values)
  }

  function handleFormat() {
    setSql((current) => formatSql(current))
  }

  // Ctrl/Cmd+Enter runs the query, Ctrl/Cmd+S saves as favorite - scoped to
  // this tab's editor so shortcuts don't fire for a background tab.
  function handleEditorKeyDown(e) {
    const isMod = e.ctrlKey || e.metaKey
    if (isMod && e.key === 'Enter') {
      e.preventDefault()
      runQuery(0)
    } else if (isMod && e.key.toLowerCase() === 's') {
      e.preventDefault()
      if (sql.trim()) setShowSaveModal(true)
    } else if (isMod && e.shiftKey && e.key.toLowerCase() === 'f') {
      e.preventDefault()
      handleFormat()
    }
  }

  return (
    <div className="query-page">
      {showHealthBanner && (
        <ConnectionHealthBanner onRefresh={(refresh) => api.getConnectionHealth(refresh)} />
      )}

      <div className="toolbar">
        <select value={connectionId} onChange={(e) => setConnectionId(e.target.value)}>
          {connections.length === 0 && <option value="">No connections configured</option>}
          {connections.map((c) => (
            <option key={c.id} value={c.id}>{c.name} ({c.providerType})</option>
          ))}
        </select>
        {connections.length === 0 && <a className="link-button" href="/connections">Add a connection →</a>}
      </div>

      <textarea
        ref={textareaRef}
        className="sql-editor"
        value={sql}
        onChange={(e) => setSql(e.target.value)}
        onKeyDown={handleEditorKeyDown}
        rows={6}
        placeholder="SELECT * FROM my_table WHERE customer_id = {{customer_id}}"
      />
      <p className="muted shortcut-hint">
        Ctrl+Enter to run · Ctrl+S to save as favorite · Ctrl+Shift+F to format
      </p>

      {requiredParams.length > 0 && (
        <p className="muted param-hint">
          Parameters detected: {requiredParams.join(', ')}
          {' '}
          <button type="button" className="link-button"
                  onClick={() => { setPendingAction('run'); setShowParamModal(true) }}>
            {hasAllValues(paramValues) ? 'edit values' : 'fill in values'}
          </button>
        </p>
      )}

      <div className="toolbar">
        <button onClick={() => runQuery(0)} disabled={loading}>
          {loading ? 'Running…' : 'Run query'}
        </button>
        <button className="secondary" onClick={handleFormat} title="Ctrl+Shift+F">
          🪄 Format
        </button>
        <button className="secondary" onClick={() => exportCsv()} disabled={!page.columns.length || exporting}>
          {exporting ? 'Exporting…' : 'Export CSV (all rows)'}
        </button>
        <button className="secondary" onClick={() => setShowSaveModal(true)} disabled={!sql.trim()}>
          ★ Save as favorite
        </button>
        {elapsedMs !== null && <span className="muted">page fetched in {elapsedMs}ms</span>}
      </div>

      {error && <div className="error-banner">{error}</div>}

      <ResultGrid columns={page.columns} rows={page.rows} />

      {page.columns.length > 0 && (
        <div className="pagination">
          <button disabled={skip === 0 || loading} onClick={() => runQuery(Math.max(0, skip - PAGE_SIZE))}>
            ← Previous
          </button>
          <span>
            Rows {skip + 1}–{skip + page.rows.length}
            {totalCount !== null && totalCount !== undefined ? ` of ~${totalCount.toLocaleString()}` : ''}
          </span>
          <button disabled={!hasMore || loading} onClick={() => runQuery(skip + PAGE_SIZE)}>
            Next →
          </button>
        </div>
      )}

      <GroupByPanel
        columns={page.columns}
        onGroupBy={handleGroupBy}
        result={groupByResult}
        loading={groupByLoading}
        error={groupByError}
        onPin={handlePinToDashboard}
        pinning={pinning}
        connectionId={connectionId}
      />
      {pinMessage && <p className="muted">{pinMessage}</p>}

      {showSaveModal && (
        <SaveFavoriteModal onSave={handleSaveFavorite} onClose={() => setShowSaveModal(false)} />
      )}
      {showParamModal && (
        <ParameterModal
          paramNames={requiredParams}
          initialValues={paramValues}
          onSubmit={handleParamsSubmit}
          onClose={() => { setShowParamModal(false); setPendingAction(null) }}
        />
      )}
    </div>
  )
}
