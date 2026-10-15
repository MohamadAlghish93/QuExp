import { useEffect, useMemo, useState } from 'react'
import { api } from '../api/client.js'
import { useAuth } from '../auth/AuthContext.jsx'
import ResultGrid from '../components/ResultGrid.jsx'
import ParameterModal from '../components/ParameterModal.jsx'
import SaveFavoriteModal from '../components/SaveFavoriteModal.jsx'
import { useNavigate } from 'react-router-dom'

const PLACEHOLDER_REGEX = /\{\{\s*(\w+)\s*\}\}/g
function extractParamNames(sql) {
  const names = []
  for (const match of sql.matchAll(PLACEHOLDER_REGEX)) {
    if (!names.includes(match[1])) names.push(match[1])
  }
  return names
}

// A curated library of parameterized, well-described queries so people who
// don't write SQL can still find and run something useful. Running a template
// executes it right here and shows only the RESULT - the SQL itself is never
// put in front of the user unless they deliberately opt in via the SQL icon.
export default function TemplatesPage() {
  const { canEditTemplates, isAdmin, username } = useAuth()
  const navigate = useNavigate()
  const [pendingTemplates, setPendingTemplates] = useState([])
  const [templates, setTemplates] = useState([])
  const [recordCounts, setRecordCounts] = useState([]) // side panel: [{ favoriteId, name, count, error }]
  const [search, setSearch] = useState('')
  const [error, setError] = useState(null)
  const [sqlViewTemplate, setSqlViewTemplate] = useState(null) // template whose SQL is shown in the popup
  const [runningTemplate, setRunningTemplate] = useState(null) // template currently awaiting parameters
  const [resultsById, setResultsById] = useState({}) // { [templateId]: { columns, rows, error, loading, parameters } }
  const [editingTemplate, setEditingTemplate] = useState(null)
  const [exportingId, setExportingId] = useState(null)
  const [pdfLayoutsById, setPdfLayoutsById] = useState({}) // { [templateId]: PdfLayoutResponse[] }
  const [generatingPdfFor, setGeneratingPdfFor] = useState(null)

  async function load() {
    try {
      setTemplates(await api.getTemplates(search, ''))
      if (isAdmin) setPendingTemplates(await api.getPendingTemplates())
    } catch (e) {
      setError(e.message)
    }
  }

  // Side panel: name + record count for every template the caller can see. Loaded once
  // separately from the main list, since counts are slower (each runs a real COUNT(*)).
  async function loadRecordCounts() {
    try {
      setRecordCounts(await api.getTemplateRecordCounts())
    } catch {
      // The side panel is a convenience feature - a failed fetch shouldn't block the page.
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
    setGeneratingPdfFor(`${template.id}`)
    setError(null)
    try {
      const layouts = await ensurePdfLayouts(template.id)
      if (layouts.length === 0) {
        if (confirm(`No PDF layout defined for "${template.name}" yet. Design one now?`))
          navigate(`/pdf-layouts?favoriteId=${template.id}`)
        return
      }
      // Uses the first defined layout for this template. Manage/reorder layouts from the designer.
      await api.generatePdf(layouts[0].id, `${template.name}`, row)
    } catch (e) {
      setError(e.message)
    } finally {
      setGeneratingPdfFor(null)
    }
  }

  useEffect(() => { load() }, [search]) // eslint-disable-line react-hooks/exhaustive-deps

  async function handleApprove(id) {
    try {
      await api.approveTemplate(id)
      load()
    } catch (e) {
      setError(e.message)
    }
  }

  async function handleReject(id) {
    try {
      await api.rejectTemplate(id)
      load()
    } catch (e) {
      setError(e.message)
    }
  }

  async function executeTemplate(template, parameters) {
    setResultsById((prev) => ({ ...prev, [template.id]: { loading: true } }))
    try {
      const result = await api.runPage({
        connectionId: template.connectionId, sql: template.sql, skip: 0, take: 100, parameters,
      })
      setResultsById((prev) => ({
        ...prev,
        // Keep the parameters used for this run, so Export CSV (which re-runs the full,
        // unpaged query) uses the same values instead of asking again.
        [template.id]: { columns: result.columns, rows: result.rows, elapsedMs: result.elapsedMs, parameters },
      }))
    } catch (e) {
      setResultsById((prev) => ({ ...prev, [template.id]: { error: e.message } }))
    }
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
    const paramNames = extractParamNames(template.sql)
    if (paramNames.length > 0) {
      setRunningTemplate(template) // needs values first - opens the parameter form, still no SQL shown
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
    try {
      await api.hideTemplate(id)
      load()
    } catch (e) {
      setError(e.message)
    }
  }

  async function handleSaveEdit(payload) {
    try {
      // Keep the original SQL and connection - this form only edits name/category/
      // tags/description/sharing, not the query itself.
      await api.saveFavorite({
        ...payload,
        id: editingTemplate.id,
        sql: editingTemplate.sql,
        connectionId: editingTemplate.connectionId,
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

  return (
    <div className="favorites-layout">
      <aside className="category-sidebar">
        <h4>Templates &amp; record counts</h4>
        {recordCounts.length === 0 ? (
          <p className="muted">No templates visible to you yet.</p>
        ) : (
          <ul>
            {recordCounts.map((rc) => (
              <li key={rc.favoriteId}>
                <button className="category-item" onClick={() => setSearch(rc.name)} title={rc.error || ''}>
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
      <div className="dashboard-header">
        <h2>Query templates</h2>
        {canEditTemplates && (
          <span className="muted">
            Save a query as a template from the Query page's "Save as favorite" form.
          </span>
        )}
      </div>
      <p className="muted">
        Ready-to-run queries curated by your team — no SQL knowledge needed. Click Run and see the
        result; the underlying query stays out of the way unless you choose to look at it.
      </p>

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

      <div className="toolbar">
        <input
          placeholder="Search templates…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
      </div>

      {error && <div className="error-banner">{error}</div>}

      {templates.length === 0 ? (
        <p className="muted">
          No templates yet. {canEditTemplates ? 'Create one from the Query page.' : 'Ask an Editor or Admin to add some.'}
        </p>
      ) : (
        <div className="template-grid">
          {templates.map((t) => {
            const result = resultsById[t.id]
            const isShared = t.sharedWithUsernames && t.sharedWithUsernames.length > 0
            return (
              <div key={t.id} className={result?.columns || result?.error ? 'template-card template-card-expanded' : 'template-card'}>
                {canEditTemplates && (
                  <button className="template-delete-icon" onClick={() => handleDelete(t.id)} title="Remove template" aria-label={`Remove ${t.name}`}>
                    🗑
                  </button>
                )}

                <div className="favorite-card-header">
                  <strong>{t.name}</strong>
                  <div className="template-badges">
                    {t.workflowSteps?.length > 0 && (
                      <span className={t.workflowSteps[t.workflowStepIndex]?.makesOfficial ? 'badge' : 'badge badge-pending'}
                            title={`Workflow: ${t.workflowName}`}>
                        🔀 {t.workflowSteps[t.workflowStepIndex]?.name}
                      </span>
                    )}
                    {t.approvalStatus === 'Pending' && !(t.workflowSteps?.length > 0) && <span className="badge badge-pending">⏳ Pending</span>}
                    {t.approvalStatus === 'Rejected' && <span className="badge badge-rejected">✕ Rejected</span>}
                    <span className="badge">{t.category}</span>
                  </div>
                </div>

                <p className="template-description">{t.description || 'No description provided.'}</p>

                <div className="template-meta-row">
                  {t.tags.length > 0 && (
                    <div className="tag-list">{t.tags.map((tag) => <span key={tag} className="tag">{tag}</span>)}</div>
                  )}
                  <span className="visibility-chip" title={isShared ? `Shared with: ${t.sharedWithUsernames.join(', ')}` : 'Visible to everyone'}>
                    {isShared ? `🔒 ${t.sharedWithUsernames.length} people` : '🌐 Everyone'}
                  </span>
                </div>

                <div className="template-actions">
                  <button onClick={() => handleRunClick(t)} disabled={result?.loading}>
                    {result?.loading ? 'Running…' : '▶ Run'}
                  </button>
                  <button className="icon-text-button" onClick={() => setSqlViewTemplate(t)} title="View the underlying SQL">
                    🔍 SQL
                  </button>
                  {canEditTemplates && (
                    <button className="icon-text-button" onClick={() => setEditingTemplate(t)} title="Edit this template">
                      ✎ Edit
                    </button>
                  )}
                  {t.ownerUsername !== username && (
                    <button className="icon-text-button" onClick={() => handleHide(t.id)} title="Hide this from your Templates list only">
                      🙈 Hide
                    </button>
                  )}
                </div>

                {result?.error && <div className="error-banner">{result.error}</div>}
                {result?.columns && (
                  <div className="template-result">
                    <div className="template-result-header">
                      <span className="muted">{result.rows.length} row(s) · {result.elapsedMs}ms</span>
                      <div className="template-result-header-actions">
                        <button className="link-button" onClick={() => navigate(`/pdf-layouts?favoriteId=${t.id}`)}>
                          design PDF layout
                        </button>
                        <button className="link-button" onClick={() => handleExport(t)} disabled={exportingId === t.id}>
                          {exportingId === t.id ? 'exporting…' : 'export CSV (all rows)'}
                        </button>
                        <button className="link-button" onClick={() => closeResult(t.id)}>close</button>
                      </div>
                    </div>
                    <ResultGrid
                      columns={result.columns}
                      rows={result.rows}
                      dateFieldFormats={t.dateFieldFormats}
                      rowActions={(row) => (
                        <button
                          className="icon-text-button row-pdf-button"
                          onClick={() => handleGeneratePdf(t, row)}
                          disabled={generatingPdfFor === t.id}
                          title="Generate a PDF for this row"
                        >
                          📄 PDF
                        </button>
                      )}
                    />
                  </div>
                )}
              </div>
            )
          })}
        </div>
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
          }}
          onSave={handleSaveEdit}
          onClose={() => setEditingTemplate(null)}
        />
      )}

      {/* SQL is shown as a deliberate, separate popup - never inline with the result,
          keeping "run and see the result" and "look at the query" as two distinct actions. */}
      {sqlViewTemplate && (
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
