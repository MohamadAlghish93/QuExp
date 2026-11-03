import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client.js'
import { useAuth } from '../auth/AuthContext.jsx'
import ResultGrid from '../components/ResultGrid.jsx'
import ParameterModal from '../components/ParameterModal.jsx'
import SaveFavoriteModal from '../components/SaveFavoriteModal.jsx'
import { currentStageName, isCurrentStageOfficial } from '../utils/workflowStages.js'

const PAGE_SIZE = 100
const PLACEHOLDER_REGEX = /\{\{\s*(\w+)\s*\}\}/g

function extractParamNames(sql) {
  const names = []
  for (const match of sql.matchAll(PLACEHOLDER_REGEX)) {
    if (!names.includes(match[1])) names.push(match[1])
  }
  return names
}

// "customer_id" / "customerId" -> "Customer id" - so a report can say what it will ask for in plain words.
function humanize(name) {
  const spaced = name.replace(/_/g, ' ').replace(/([a-z0-9])([A-Z])/g, '$1 $2').trim().toLowerCase()
  return spaced.charAt(0).toUpperCase() + spaced.slice(1)
}

function describeRange(r) {
  if (r.rows.length === 0) return 'No records on this page'
  const from = r.skip + 1
  const to = r.skip + r.rows.length
  return r.total ? `Showing ${from}–${to} of about ${Number(r.total).toLocaleString()} records` : `Showing ${from}–${to}`
}

// Ready-made reports for people who don't write SQL: find one, press Run, read the result. The SQL behind
// a report is only offered to people who manage templates - everyone else just sees results.
export default function TemplatesPage() {
  const { canEditTemplates, isAdmin, username } = useAuth()
  const navigate = useNavigate()
  const [pendingTemplates, setPendingTemplates] = useState([])
  const [templates, setTemplates] = useState([])
  const [loaded, setLoaded] = useState(false)
  const [recordCounts, setRecordCounts] = useState([]) // side panel: [{ favoriteId, name, count, error }]
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('') // what's actually sent to the server (debounced from searchInput)
  const [category, setCategory] = useState('')
  const [error, setError] = useState(null)
  const [sqlViewTemplate, setSqlViewTemplate] = useState(null)
  const [runningTemplate, setRunningTemplate] = useState(null) // template awaiting parameter values
  const [resultsById, setResultsById] = useState({}) // { [id]: { columns, rows, parameters, skip, hasMore, total, loading, error } }
  const [editingTemplate, setEditingTemplate] = useState(null)
  const [exportingId, setExportingId] = useState(null)
  const [pdfLayoutsById, setPdfLayoutsById] = useState({})
  const [generatingPdfFor, setGeneratingPdfFor] = useState(null)

  async function load() {
    try {
      setTemplates(await api.getTemplates(search, ''))
      if (isAdmin) setPendingTemplates(await api.getPendingTemplates())
    } catch (e) {
      setError(e.message)
    } finally {
      setLoaded(true)
    }
  }

  useEffect(() => { load() }, [search]) // eslint-disable-line react-hooks/exhaustive-deps

  // Wait for a pause in typing before asking the server, instead of one request per keystroke.
  useEffect(() => {
    const timer = setTimeout(() => setSearch(searchInput.trim()), 300)
    return () => clearTimeout(timer)
  }, [searchInput])

  function searchFor(text) {
    setSearchInput(text)
    setSearch(text.trim())
  }

  // Side panel: name + record count for every template the caller can see. Loaded separately from the main
  // list, since counts are slower (each runs a real COUNT(*)).
  async function loadRecordCounts() {
    try {
      setRecordCounts(await api.getTemplateRecordCounts())
    } catch {
      // The side panel is a convenience - a failed fetch shouldn't block the page.
    }
  }

  useEffect(() => { loadRecordCounts() }, [])

  async function ensurePdfLayouts(templateId) {
    if (pdfLayoutsById[templateId]) return pdfLayoutsById[templateId]
    try {
      const layouts = await api.getPdfLayouts(templateId)
      setPdfLayoutsById((prev) => ({ ...prev, [templateId]: layouts }))
      return layouts
    } catch {
      return []
    }
  }

  async function handleGeneratePdf(template, row) {
    setGeneratingPdfFor(template.id)
    setError(null)
    try {
      const layouts = await ensurePdfLayouts(template.id)
      if (layouts.length === 0) {
        if (!canEditTemplates) {
          alert('There is no PDF layout for this report yet. Ask someone who manages templates to set one up.')
        } else if (confirm(`No PDF layout defined for "${template.name}" yet. Design one now?`)) {
          navigate(`/pdf-layouts?favoriteId=${template.id}`)
        }
        return
      }
      // Uses the first defined layout for this template. Manage layouts from the designer.
      await api.generatePdf(layouts[0].id, template.name, row)
    } catch (e) {
      setError(e.message)
    } finally {
      setGeneratingPdfFor(null)
    }
  }

  async function handleApprove(id) {
    try { await api.approveTemplate(id); load() } catch (e) { setError(e.message) }
  }

  async function handleReject(id) {
    try { await api.rejectTemplate(id); load() } catch (e) { setError(e.message) }
  }

  // Runs one page of a report. The previous page stays on screen while the next one loads, so paging
  // doesn't flash the table away.
  async function executeTemplate(template, parameters, skip = 0) {
    setResultsById((prev) => ({ ...prev, [template.id]: { ...prev[template.id], loading: true, error: null } }))
    try {
      const result = await api.runPage({
        connectionId: template.connectionId, sql: template.sql, skip, take: PAGE_SIZE, parameters,
      })
      setResultsById((prev) => ({
        ...prev,
        [template.id]: {
          columns: result.columns, rows: result.rows, elapsedMs: result.elapsedMs,
          // Kept so paging and Download-all re-run with the same values, without asking again.
          parameters, skip, hasMore: result.hasMore,
          total: skip === 0 ? result.estimatedTotalCount : prev[template.id]?.total,
        },
      }))
    } catch (e) {
      setResultsById((prev) => ({ ...prev, [template.id]: { ...prev[template.id], loading: false, error: e.message } }))
    }
  }

  function goToPage(template, result, direction) {
    executeTemplate(template, result.parameters, Math.max(0, result.skip + direction * PAGE_SIZE))
  }

  async function handleExport(template) {
    const result = resultsById[template.id]
    setExportingId(template.id)
    try {
      await api.exportCsv(template.connectionId, template.sql, result?.parameters || {})
    } catch (e) {
      setResultsById((prev) => ({ ...prev, [template.id]: { ...prev[template.id], error: e.message } }))
    } finally {
      setExportingId(null)
    }
  }

  function handleRunClick(template) {
    if (extractParamNames(template.sql).length > 0) {
      setRunningTemplate(template) // needs values first - opens the form, still no SQL shown
    } else {
      executeTemplate(template, {})
    }
  }

  function handleParamsSubmit(values) {
    const template = runningTemplate
    setRunningTemplate(null)
    executeTemplate(template, values)
  }

  function closeResult(id) {
    setResultsById((prev) => { const next = { ...prev }; delete next[id]; return next })
  }

  async function handleDelete(id) {
    if (!confirm('Remove this template from the library?')) return
    await api.deleteFavorite(id)
    closeResult(id)
    load()
  }

  async function handleHide(id) {
    try { await api.hideTemplate(id); load() } catch (e) { setError(e.message) }
  }

  async function handleSaveEdit(payload) {
    try {
      // Keep the original SQL and connection - this form only edits name/category/tags/description/sharing.
      await api.saveFavorite({
        ...payload, id: editingTemplate.id, sql: editingTemplate.sql, connectionId: editingTemplate.connectionId,
      })
      setEditingTemplate(null)
      load()
    } catch (e) {
      setError(e.message)
    }
  }

  const runningTemplateParams = useMemo(
    () => (runningTemplate ? extractParamNames(runningTemplate.sql) : []),
    [runningTemplate]
  )

  const categories = useMemo(() => {
    const counts = {}
    for (const t of templates) counts[t.category] = (counts[t.category] || 0) + 1
    return Object.entries(counts).sort(([a], [b]) => a.localeCompare(b))
  }, [templates])

  const visibleTemplates = useMemo(
    () => templates.filter((t) => !category || t.category === category),
    [templates, category]
  )

  function clearFilters() {
    searchFor('')
    setCategory('')
  }

  function renderResult(t, result) {
    const page = Math.floor(result.skip / PAGE_SIZE) + 1
    return (
      <div className="template-result">
        <div className="template-result-header">
          <strong>📊 Results</strong>
          <div className="template-result-header-actions">
            {canEditTemplates && (
              <button className="link-button" onClick={() => navigate(`/pdf-layouts?favoriteId=${t.id}`)}>design PDF layout</button>
            )}
            <button className="link-button" onClick={() => handleExport(t)} disabled={exportingId === t.id}>
              {exportingId === t.id ? 'Preparing file…' : '⬇ Download everything (CSV)'}
            </button>
            <button className="link-button" onClick={() => closeResult(t.id)}>✕ Close</button>
          </div>
        </div>

        <p className="muted template-result-summary">
          {describeRange(result)}
          {canEditTemplates && result.elapsedMs !== undefined ? ` · ${result.elapsedMs} ms` : ''}
          {result.loading && <span className="template-updating"><span className="spinner spinner-small" /> Updating…</span>}
        </p>

        {result.rows.length === 0 && result.skip === 0 ? (
          <div className="template-empty-result">
            <strong>No records found</strong>
            <span className="muted">This report didn't return anything{Object.keys(result.parameters || {}).length ? ' for the values you entered' : ''}.</span>
          </div>
        ) : (
          <ResultGrid
            columns={result.columns}
            rows={result.rows}
            dateFieldFormats={t.dateFieldFormats}
            rowActions={(row) => (
              <button
                className="icon-text-button row-pdf-button"
                onClick={() => handleGeneratePdf(t, row)}
                disabled={generatingPdfFor === t.id}
                title="Make a PDF of this record"
              >
                📄 PDF
              </button>
            )}
          />
        )}

        {(result.skip > 0 || result.hasMore) && (
          <div className="template-pager">
            <button className="secondary" onClick={() => goToPage(t, result, -1)} disabled={result.skip === 0 || result.loading}>← Previous</button>
            <span className="muted">Page {page}</span>
            <button className="secondary" onClick={() => goToPage(t, result, 1)} disabled={!result.hasMore || result.loading}>Next →</button>
          </div>
        )}
      </div>
    )
  }

  function renderCard(t) {
    const result = resultsById[t.id]
    const isShared = t.sharedWithUsernames && t.sharedWithUsernames.length > 0
    const asks = extractParamNames(t.sql).map(humanize)
    const hasResult = !!result?.columns
    const failedFirstRun = !!result?.error && !hasResult

    return (
      <div key={t.id} className={result?.columns || result?.error ? 'template-card template-card-expanded' : 'template-card'}>
        {canEditTemplates && (
          <button className="template-delete-icon" onClick={() => handleDelete(t.id)} title="Remove template" aria-label={`Remove ${t.name}`}>🗑</button>
        )}

        <div className="template-card-top">
          <h3 className="template-name">{t.name}</h3>
          <div className="template-badges">
            {t.workflowSteps?.length > 0 && (
              <span className={isCurrentStageOfficial(t) ? 'badge' : 'badge badge-pending'} title={`Workflow: ${t.workflowName}`}>
                🔀 {currentStageName(t)}
              </span>
            )}
            {t.approvalStatus === 'Pending' && !(t.workflowSteps?.length > 0) && <span className="badge badge-pending">⏳ Pending</span>}
            {t.approvalStatus === 'Rejected' && <span className="badge badge-rejected">✕ Rejected</span>}
            <span className="badge">{t.category}</span>
          </div>
        </div>

        <p className="template-description">
          {t.description || (canEditTemplates ? 'No description yet - add one with Edit so people know what this report shows.' : 'No description provided.')}
        </p>

        {t.workflowApprovals?.length > 0 && (
          <p className="template-approved-by" title="Who approved this template, and when">
            ✅ Approved by{' '}
            {t.workflowApprovals.map((a) =>
              `@${a.approvedByUsername} (${new Date(a.approvedAtUtc).toLocaleDateString()}${t.workflowApprovals.length > 1 ? `, ${a.stepName}` : ''})`
            ).join(' · ')}
          </p>
        )}

        {asks.length > 0 && (
          <p className="template-needs">✍ You'll be asked for: <strong>{asks.join(', ')}</strong></p>
        )}

        {canEditTemplates && (
          <div className="template-meta-row">
            {t.tags.length > 0 && <div className="tag-list">{t.tags.map((tag) => <span key={tag} className="tag">{tag}</span>)}</div>}
            <span className="visibility-chip" title={isShared ? `Shared with: ${t.sharedWithUsernames.join(', ')}` : 'Visible to everyone'}>
              {isShared ? `🔒 ${t.sharedWithUsernames.length} people` : '🌐 Everyone'}
            </span>
          </div>
        )}

        <button className="template-run-button" onClick={() => handleRunClick(t)} disabled={result?.loading}>
          {result?.loading && !hasResult
            ? <><span className="spinner spinner-small spinner-on-dark" /> Running…</>
            : hasResult ? '↻ Run again' : '▶ Run report'}
        </button>

        {(canEditTemplates || t.ownerUsername !== username) && (
          <div className="template-actions template-secondary-actions">
            {canEditTemplates && (
              <button className="icon-text-button" onClick={() => setSqlViewTemplate(t)} title="See the query behind this report">🔍 View query</button>
            )}
            {canEditTemplates && (
              <button className="icon-text-button" onClick={() => setEditingTemplate(t)} title="Edit this template">✎ Edit</button>
            )}
            {t.ownerUsername !== username && (
              <button className="icon-text-button" onClick={() => handleHide(t.id)} title="Hide this from your Templates list only">🙈 Hide from my list</button>
            )}
          </div>
        )}

        {failedFirstRun && (
          <div className="template-error-box">
            <strong>We couldn't run this report.</strong>
            <span>{result.error}</span>
            <button className="secondary" onClick={() => handleRunClick(t)}>Try again</button>
          </div>
        )}
        {result?.error && hasResult && <div className="error-banner">{result.error}</div>}

        {hasResult && renderResult(t, result)}
      </div>
    )
  }

  const filtering = !!search || !!category

  return (
    <div className="favorites-layout">
      <aside className="category-sidebar">
        <h4>Reports &amp; how many records</h4>
        {recordCounts.length === 0 ? (
          <p className="muted">No reports available to you yet.</p>
        ) : (
          <ul>
            {recordCounts.map((rc) => (
              <li key={rc.favoriteId}>
                <button className="category-item" onClick={() => searchFor(rc.name)} title={rc.error || `Find “${rc.name}”`}>
                  <span title={rc.name}>{rc.name}</span>
                  <span className="count-badge">
                    {rc.error ? '—' : rc.count === null || rc.count === undefined ? '…' : rc.count.toLocaleString()}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </aside>

      <div className="favorites-page">
        <div className="template-hero">
          <h2>Templates</h2>
          <p className="muted">
            Ready-made reports. Find the one you need, press <strong>Run report</strong>, and read the results - no technical knowledge needed.
            {canEditTemplates && ' You can save any query as a template from the Query page.'}
          </p>
        </div>

        {isAdmin && pendingTemplates.length > 0 && (
          <div className="pending-review-panel">
            <h4>⏳ Awaiting your approval ({pendingTemplates.length})</h4>
            <ul className="favorite-list">
              {pendingTemplates.map((t) => (
                <li key={t.id} className="favorite-card">
                  <div className="favorite-card-header">
                    <strong>{t.name}</strong>
                    <span className="muted">by @{t.ownerUsername}</span>
                  </div>
                  <p className="muted">{t.description || 'No description provided.'}</p>
                  <div className="favorite-card-actions">
                    <button onClick={() => handleApprove(t.id)}>Approve</button>
                    <button className="secondary danger" onClick={() => handleReject(t.id)}>Reject</button>
                  </div>
                </li>
              ))}
            </ul>
          </div>
        )}

        <div className="tpl-search">
          <span className="tpl-search-icon" aria-hidden="true">🔍</span>
          <input
            type="search"
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            placeholder="Find a report - try a name or a topic…"
            aria-label="Find a report"
          />
          {searchInput && <button className="link-button" onClick={() => searchFor('')}>clear</button>}
        </div>

        {categories.length > 1 && (
          <div className="tpl-chips" role="group" aria-label="Filter by topic">
            <button className={category === '' ? 'tpl-chip active' : 'tpl-chip'} onClick={() => setCategory('')}>All ({templates.length})</button>
            {categories.map(([name, count]) => (
              <button key={name} className={category === name ? 'tpl-chip active' : 'tpl-chip'} onClick={() => setCategory(category === name ? '' : name)}>
                {name} ({count})
              </button>
            ))}
          </div>
        )}

        {error && <div className="error-banner">{error}</div>}

        {!loaded ? (
          <p className="muted"><span className="spinner spinner-small" /> Loading reports…</p>
        ) : visibleTemplates.length === 0 ? (
          <div className="template-empty">
            <strong>{filtering ? 'No reports match what you typed' : 'No reports here yet'}</strong>
            <span className="muted">
              {filtering
                ? 'Try a different word, or clear the search to see everything.'
                : canEditTemplates ? 'Save a query as a template from the Query page and it will appear here.' : 'Ask an Editor or Admin to add some.'}
            </span>
            {filtering && <button className="secondary" onClick={clearFilters}>Show all reports</button>}
          </div>
        ) : (
          <>
            <p className="muted tpl-count">{visibleTemplates.length} report{visibleTemplates.length === 1 ? '' : 's'}{filtering ? ' found' : ''}</p>
            <div className="template-grid">{visibleTemplates.map(renderCard)}</div>
          </>
        )}

        {runningTemplate && (
          <ParameterModal
            paramNames={runningTemplateParams}
            initialValues={{}}
            onSubmit={handleParamsSubmit}
            onClose={() => setRunningTemplate(null)}
          />
        )}

        {editingTemplate && (
          <SaveFavoriteModal
            initialValues={{
              id: editingTemplate.id,
              name: editingTemplate.name,
              category: editingTemplate.category,
              tags: editingTemplate.tags,
              isTemplate: true,
              description: editingTemplate.description,
              sharedWithUsernames: editingTemplate.sharedWithUsernames,
              // Without this, saving an edit would silently drop the template's date formats.
              dateFieldFormats: editingTemplate.dateFieldFormats,
            }}
            onSave={handleSaveEdit}
            onClose={() => setEditingTemplate(null)}
          />
        )}

        {/* The query behind a report - only offered to people who manage templates. */}
        {canEditTemplates && sqlViewTemplate && (
          <div className="modal-backdrop" onClick={() => setSqlViewTemplate(null)}>
            <div className="modal modal-wide sql-view-modal" onClick={(e) => e.stopPropagation()}>
              <h3>{sqlViewTemplate.name}</h3>
              <pre className="favorite-sql sql-view-block">{sqlViewTemplate.sql}</pre>
              <div className="modal-actions">
                <button type="button" onClick={() => setSqlViewTemplate(null)}>Close</button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
