import { useEffect, useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api } from '../api/client.js'
import ParameterModal from '../components/ParameterModal.jsx'
import PdfPagePreview from '../components/PdfPagePreview.jsx'

const PAGE_POINTS = { A4: { width: 595, height: 842 }, Letter: { width: 612, height: 792 } }
const DISPLAY_HEIGHT_PX = 420
const PLACEHOLDER_REGEX = /\{\{\s*(\w+)\s*\}\}/g

function extractParamNames(sql) {
  const names = []
  for (const match of sql.matchAll(PLACEHOLDER_REGEX)) {
    if (!names.includes(match[1])) names.push(match[1])
  }
  return names
}

// Visual field-placement designer for per-row PDF generation. Pick a template,
// pull its columns from a sample row, then drag fields onto a page-sized canvas
// to define exactly where each one prints. "Render as table" fields become a
// bulleted list with a PDF bookmark, for columns holding multiple values.
export default function PdfLayoutDesignerPage() {
  const [searchParams] = useSearchParams()
  const [templates, setTemplates] = useState([])
  const [templateId, setTemplateId] = useState(searchParams.get('favoriteId') || '')
  const [sampleColumns, setSampleColumns] = useState([])
  const [sampleRow, setSampleRow] = useState(null)
  const [paramNames, setParamNames] = useState([])
  const [showParamModal, setShowParamModal] = useState(false)
  const [sampleError, setSampleError] = useState(null)

  const [layouts, setLayouts] = useState([])
  const [layoutId, setLayoutId] = useState(null)
  const [layoutName, setLayoutName] = useState('')
  const [pageSize, setPageSize] = useState('A4')
  const [fields, setFields] = useState([])
  const [selectedFieldIndex, setSelectedFieldIndex] = useState(null)

  const [saving, setSaving] = useState(false)
  const [previewing, setPreviewing] = useState(false)
  const [message, setMessage] = useState(null)
  const [error, setError] = useState(null)
  const [hasBackgroundPdf, setHasBackgroundPdf] = useState(false)
  const [backgroundBlob, setBackgroundBlob] = useState(null)
  const [uploadingBackground, setUploadingBackground] = useState(false)

  const canvasRef = useRef(null)
  const dragState = useRef(null)
  const backgroundFileInputRef = useRef(null)

  const dims = PAGE_POINTS[pageSize]
  const scale = DISPLAY_HEIGHT_PX / dims.height
  const canvasWidthPx = dims.width * scale

  useEffect(() => {
    api.getTemplates('', '').then(setTemplates).catch((e) => setError(e.message))
  }, [])

  useEffect(() => {
    if (!templateId) return
    loadSample()
    loadLayouts()
    resetLayoutForm()
  }, [templateId]) // eslint-disable-line react-hooks/exhaustive-deps

  const selectedTemplate = templates.find((t) => t.id === templateId)

  async function loadSample(parameters = {}) {
    if (!selectedTemplate) return
    const names = extractParamNames(selectedTemplate.sql)
    if (names.length > 0 && Object.keys(parameters).length === 0) {
      setParamNames(names)
      setShowParamModal(true)
      return
    }
    setSampleError(null)
    try {
      const result = await api.runPage({
        connectionId: selectedTemplate.connectionId, sql: selectedTemplate.sql, skip: 0, take: 1, parameters,
      })
      setSampleColumns(result.columns)
      setSampleRow(result.rows[0] || null)
    } catch (e) {
      setSampleError(e.message)
    }
  }

  async function loadLayouts() {
    try {
      setLayouts(await api.getPdfLayouts(templateId))
    } catch (e) {
      setError(e.message)
    }
  }

  function resetLayoutForm() {
    setLayoutId(null)
    setLayoutName('')
    setPageSize('A4')
    setFields([])
    setSelectedFieldIndex(null)
    setHasBackgroundPdf(false)
    loadBackgroundPreview(null)
  }

  function loadLayoutIntoForm(layout) {
    setLayoutId(layout.id)
    setLayoutName(layout.name)
    setPageSize(layout.pageSize)
    setFields(layout.fields)
    setSelectedFieldIndex(null)
    setHasBackgroundPdf(layout.hasBackgroundPdf)
    loadBackgroundPreview(layout.hasBackgroundPdf ? layout.id : null)
  }

  // Fetched as an authenticated blob, then rendered to a <canvas> by PdfPagePreview -
  // not a live <embed> viewer, whose chrome overlapped the fields and swallowed scroll.
  async function loadBackgroundPreview(layoutIdToLoad) {
    setBackgroundBlob(null)
    if (!layoutIdToLoad) return
    try {
      const token = localStorage.getItem('qe_token')
      const res = await fetch(api.pdfBackgroundUrl(layoutIdToLoad), {
        headers: token ? { Authorization: `Bearer ${token}` } : {},
      })
      if (!res.ok) return
      setBackgroundBlob(await res.blob())
    } catch {
      // Preview is a nice-to-have - a failed fetch just means no background shown.
    }
  }

  async function handleBackgroundFileSelected(e) {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (!file || !layoutId) return
    setUploadingBackground(true)
    setError(null)
    try {
      await api.uploadPdfBackground(layoutId, file)
      setHasBackgroundPdf(true)
      loadBackgroundPreview(layoutId)
      setMessage('Background uploaded.')
    } catch (e) {
      setError(e.message)
    } finally {
      setUploadingBackground(false)
    }
  }

  async function handleRemoveBackground() {
    if (!layoutId || !confirm('Remove the background PDF? Generated PDFs will use a blank page again.')) return
    try {
      await api.deletePdfBackground(layoutId)
      setHasBackgroundPdf(false)
      loadBackgroundPreview(null)
    } catch (e) {
      setError(e.message)
    }
  }

  function addField(columnName) {
    setFields((prev) => [...prev, { columnName, label: columnName, x: 40, y: 40 + prev.length * 24, fontSize: 11, isTable: false }])
    setSelectedFieldIndex(fields.length)
  }

  function updateField(index, changes) {
    setFields((prev) => prev.map((f, i) => (i === index ? { ...f, ...changes } : f)))
  }

  function removeField(index) {
    setFields((prev) => prev.filter((_, i) => i !== index))
    setSelectedFieldIndex(null)
  }

  function handleFieldMouseDown(e, index) {
    e.preventDefault()
    setSelectedFieldIndex(index)
    dragState.current = { index, startX: e.clientX, startY: e.clientY, origX: fields[index].x, origY: fields[index].y }
    window.addEventListener('mousemove', handleDragMove)
    window.addEventListener('mouseup', handleDragEnd)
  }

  function handleDragMove(e) {
    const state = dragState.current
    if (!state) return
    const dxPoints = (e.clientX - state.startX) / scale
    const dyPoints = (e.clientY - state.startY) / scale
    updateField(state.index, {
      x: Math.max(0, Math.round(state.origX + dxPoints)),
      y: Math.max(0, Math.round(state.origY + dyPoints)),
    })
  }

  function handleDragEnd() {
    dragState.current = null
    window.removeEventListener('mousemove', handleDragMove)
    window.removeEventListener('mouseup', handleDragEnd)
  }

  async function handleSaveLayout() {
    if (!layoutName.trim()) { setError('Name the layout first.'); return }
    if (fields.length === 0) { setError('Add at least one field.'); return }
    setSaving(true)
    setError(null)
    setMessage(null)
    try {
      const saved = await api.savePdfLayout({ id: layoutId, name: layoutName.trim(), favoriteId: templateId, pageSize, fields })
      setLayoutId(saved.id)
      setMessage('Layout saved.')
      loadLayouts()
      // A brand-new layout has no background yet - uploading one needs a saved id to attach to.
      if (!hasBackgroundPdf) setHasBackgroundPdf(saved.hasBackgroundPdf)
    } catch (e) {
      setError(e.message)
    } finally {
      setSaving(false)
    }
  }

  async function handleDeleteLayout() {
    if (!layoutId || !confirm('Delete this layout?')) return
    await api.deletePdfLayout(layoutId)
    resetLayoutForm()
    loadLayouts()
  }

  async function handlePreview() {
    if (!layoutId) { setError('Save the layout first, then you can preview it.'); return }
    if (!sampleRow) { setError('No sample row available to preview with.'); return }
    setPreviewing(true)
    setError(null)
    try {
      await api.generatePdf(layoutId, layoutName, sampleRow)
    } catch (e) {
      setError(e.message)
    } finally {
      setPreviewing(false)
    }
  }

  const selectedField = selectedFieldIndex !== null ? fields[selectedFieldIndex] : null
  const usedColumns = useMemo(() => new Set(fields.map((f) => f.columnName)), [fields])

  return (
    <div className="favorites-page">
      <h2>PDF layout designer</h2>
      <p className="muted">
        Pick a template, then drag its columns onto the page to define where each one prints
        when someone generates a PDF for a single result row. Check "render as table" for a
        column that holds multiple values - it'll print as a bulleted list with a bookmark.
      </p>

      {error && <div className="error-banner">{error}</div>}

      <div className="toolbar">
        <select value={templateId} onChange={(e) => setTemplateId(e.target.value)}>
          <option value="">Select a template…</option>
          {templates.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
        </select>
      </div>

      {templateId && (
        <div className="pdf-designer-layout">
          <div className="pdf-designer-sidebar">
            <h4>Saved layouts</h4>
            {layouts.length === 0 ? (
              <p className="muted">None yet for this template.</p>
            ) : (
              <ul className="pdf-layout-list">
                {layouts.map((l) => (
                  <li key={l.id} className="pdf-layout-list-item">
                    <button className={l.id === layoutId ? 'link-button active-link' : 'link-button'} onClick={() => loadLayoutIntoForm(l)}>
                      {l.name}
                    </button>
                    {l.hasBackgroundPdf && <span title="Has a custom background PDF">🖼️</span>}
                  </li>
                ))}
              </ul>
            )}
            <button className="secondary" onClick={resetLayoutForm}>+ New layout</button>

            <h4 style={{ marginTop: 18 }}>Columns</h4>
            <p className="muted pdf-sidebar-hint">Click a column to drop it onto the page.</p>
            {sampleError && <div className="error-banner">{sampleError}</div>}
            {sampleColumns.length === 0 && !sampleError && <p className="muted">Loading columns…</p>}
            <div className="pdf-column-chips">
              {sampleColumns.map((c) => (
                <button key={c} type="button" className="tag tag-suggestion" disabled={usedColumns.has(c)} onClick={() => addField(c)}>
                  {usedColumns.has(c) ? '✓' : '+'} {c}
                </button>
              ))}
            </div>
          </div>

          <div className="pdf-designer-main">
            <div className="pdf-designer-panel">
              <h4 className="pdf-panel-title">Layout settings</h4>
              <div className="toolbar">
                <input placeholder="Layout name" value={layoutName} onChange={(e) => setLayoutName(e.target.value)} />
                <select value={pageSize} onChange={(e) => setPageSize(e.target.value)}>
                  <option value="A4">A4</option>
                  <option value="Letter">Letter</option>
                </select>
                <button onClick={handleSaveLayout} disabled={saving}>{saving ? 'Saving…' : '💾 Save layout'}</button>
                <button className="secondary" onClick={handlePreview} disabled={previewing}>
                  {previewing ? 'Generating…' : '👁 Preview PDF'}
                </button>
                {layoutId && <button className="secondary danger" onClick={handleDeleteLayout}>🗑 Delete</button>}
              </div>
            </div>

            {layoutId && (
              <div className="pdf-designer-panel">
              <h4 className="pdf-panel-title">Background PDF <span className="muted">(optional)</span></h4>
              <div className="toolbar pdf-background-toolbar">
                <input ref={backgroundFileInputRef} type="file" accept="application/pdf" style={{ display: 'none' }}
                       onChange={handleBackgroundFileSelected} />
                <button className="secondary" onClick={() => backgroundFileInputRef.current?.click()} disabled={uploadingBackground}>
                  {uploadingBackground ? 'Uploading…' : hasBackgroundPdf ? '📤 Replace background PDF' : '📤 Upload background PDF'}
                </button>
                {hasBackgroundPdf && (
                  <>
                    <span className="muted">Using your uploaded PDF's first page as the background.</span>
                    <button className="link-button danger" onClick={handleRemoveBackground}>remove</button>
                  </>
                )}
              </div>
              </div>
            )}
            {message && <p className="test-ok">{message}</p>}

            <div className="pdf-designer-panel pdf-canvas-wrapper">
              <h4 className="pdf-panel-title">Page preview</h4>
              <div
                ref={canvasRef}
                className="pdf-canvas"
                style={{ width: canvasWidthPx, height: DISPLAY_HEIGHT_PX }}
              >
                {backgroundBlob && (
                  <PdfPagePreview blob={backgroundBlob} width={canvasWidthPx} height={DISPLAY_HEIGHT_PX} className="pdf-canvas-background" />
                )}
                {fields.map((field, i) => (
                  <div
                    key={i}
                    className={`pdf-field-box${i === selectedFieldIndex ? ' selected' : ''}${field.isTable ? ' is-table' : ''}`}
                    style={{ left: field.x * scale, top: field.y * scale }}
                    onMouseDown={(e) => handleFieldMouseDown(e, i)}
                  >
                    {field.label || field.columnName}{field.isTable ? ' ☰' : ''}
                  </div>
                ))}
              </div>
              <p className="muted pdf-canvas-hint">
                {fields.length === 0
                  ? 'Click a column on the left to add it here, then drag it into place.'
                  : 'Drag a field to reposition it. Click to select and edit its properties.'}
              </p>
            </div>

            {selectedField && (
              <div className="pdf-designer-panel pdf-field-properties">
                <div className="pdf-field-properties-header">
                  <h4 className="pdf-panel-title">Field: {selectedField.columnName}</h4>
                  <button className="icon-button" onClick={() => setSelectedFieldIndex(null)} aria-label="Close field properties">×</button>
                </div>
                <div className="form-row">
                  <label className="grow">
                    Label
                    <input value={selectedField.label} onChange={(e) => updateField(selectedFieldIndex, { label: e.target.value })} />
                  </label>
                  <label className="port-field">
                    Font size
                    <input type="number" value={selectedField.fontSize}
                           onChange={(e) => updateField(selectedFieldIndex, { fontSize: Number(e.target.value) || 11 })} />
                  </label>
                </div>
                <div className="form-row">
                  <label className="port-field">
                    X
                    <input type="number" value={Math.round(selectedField.x)}
                           onChange={(e) => updateField(selectedFieldIndex, { x: Number(e.target.value) || 0 })} />
                  </label>
                  <label className="port-field">
                    Y
                    <input type="number" value={Math.round(selectedField.y)}
                           onChange={(e) => updateField(selectedFieldIndex, { y: Number(e.target.value) || 0 })} />
                  </label>
                  <label className="checkbox-label" style={{ alignSelf: 'center' }}>
                    <input type="checkbox" checked={selectedField.isTable}
                           onChange={(e) => updateField(selectedFieldIndex, { isTable: e.target.checked })} />
                    Render as table (for multi-value columns)
                  </label>
                </div>
                <button className="secondary danger" onClick={() => removeField(selectedFieldIndex)}>Remove field</button>
              </div>
            )}
          </div>
        </div>
      )}

      {showParamModal && (
        <ParameterModal
          paramNames={paramNames}
          initialValues={{}}
          onSubmit={(values) => { setShowParamModal(false); loadSample(values) }}
          onClose={() => setShowParamModal(false)}
        />
      )}
    </div>
  )
}
