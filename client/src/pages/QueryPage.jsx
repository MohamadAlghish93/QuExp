import { useEffect, useState, useCallback, useMemo, useRef } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api } from '../api/client.js'
import ResultGrid from '../components/ResultGrid.jsx'
import SaveFavoriteModal from '../components/SaveFavoriteModal.jsx'
import ParameterModal from '../components/ParameterModal.jsx'
import GroupByPanel from '../components/GroupByPanel.jsx'
import ConnectionHealthBanner from '../components/ConnectionHealthBanner.jsx'

const PAGE_SIZE = 100
const PLACEHOLDER_REGEX = /\{\{\s*(\w+)\s*\}\}/g

function extractParamNames(sql) {
  const names = []
  for (const match of sql.matchAll(PLACEHOLDER_REGEX)) {
    if (!names.includes(match[1])) names.push(match[1])
  }
  return names
}

export default function QueryPage() {
  const [searchParams] = useSearchParams()
  const [connections, setConnections] = useState([])
  const [connectionId, setConnectionId] = useState('')
  const [sql, setSql] = useState('SELECT * FROM your_table where field={{name_field}}')
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
  // What to do once the parameter modal is submitted: 'run' or 'export'.
  const [pendingAction, setPendingAction] = useState(null)
  const [groupByResult, setGroupByResult] = useState(null)
  const [groupByLoading, setGroupByLoading] = useState(false)
  const [groupByError, setGroupByError] = useState(null)
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

  // Support arriving here from the Favorites page with a query pre-filled.
  // If that query needs parameters, prompt for them immediately.
  useEffect(() => {
    const favSql = searchParams.get('sql')
    const favConn = searchParams.get('connectionId')
    if (favSql) setSql(favSql)
    if (favConn) setConnectionId(favConn)

    if (favSql && !autoPromptedRef.current) {
      const names = extractParamNames(favSql)
      if (names.length > 0) {
        setPendingAction('run')
        setShowParamModal(true)
        autoPromptedRef.current = true
      }
    }
  }, [searchParams])

  // Parameter values are passed in explicitly rather than read from state, because
  // right after the modal submits, the state update hasn't been applied yet - reading
  // paramValues here would see stale values and re-open the modal a second time.
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
      setGroupByResult(null) // stale once a new query runs
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
    try {
      const result = await api.groupBy({
        connectionId, sql, column, parameters: paramValues, top: 100,
      })
      setGroupByResult(result)
    } catch (e) {
      setGroupByError(e.message)
    } finally {
      setGroupByLoading(false)
    }
  }, [connectionId, sql, paramValues])

  async function handleSaveFavorite({ name, category, tags }) {
    try {
      await api.saveFavorite({ name, sql, connectionId, category, tags })
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
    // Pass values straight through so the action doesn't depend on the state update landing first.
    if (action === 'export') exportCsv(values)
    else runQuery(0, values)
  }

  return (
    <div className="query-page">
      <ConnectionHealthBanner onRefresh={(refresh) => api.getConnectionHealth(refresh)} />

      <div className="toolbar">
        <select value={connectionId} onChange={(e) => setConnectionId(e.target.value)}>
          {connections.length === 0 && <option value="">No connections configured</option>}
          {connections.map((c) => (
            <option key={c.id} value={c.id}>{c.name} ({c.providerType})</option>
          ))}
        </select>
        {connections.length === 0 && (
          <a className="link-button" href="/connections">Add a connection →</a>
        )}
      </div>

      <textarea
        className="sql-editor"
        value={sql}
        onChange={(e) => setSql(e.target.value)}
        rows={6}
        placeholder="SELECT * FROM my_table WHERE customer_id = {{customer_id}}"
      />
      {requiredParams.length > 0 && (
        <p className="muted param-hint">
          Parameters detected: {requiredParams.join(', ')}
          {' '}
          <button
            type="button"
            className="link-button"
            onClick={() => { setPendingAction('run'); setShowParamModal(true) }}
          >
            {hasAllValues(paramValues) ? 'edit values' : 'fill in values'}
          </button>
        </p>
      )}

      <div className="toolbar">
        <button onClick={() => runQuery(0)} disabled={loading}>
          {loading ? 'Running…' : 'Run query'}
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
      />

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
