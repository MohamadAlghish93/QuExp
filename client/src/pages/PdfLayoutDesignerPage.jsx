import CloudscapeSelect from '../components/CloudscapeSelect.jsx'
import CloudscapeInput from '../components/CloudscapeInput.jsx'
import CloudscapeTextarea from '../components/CloudscapeTextarea.jsx'
import CloudscapeButton from '../components/CloudscapeButton.jsx'
import Checkbox from '@cloudscape-design/components/checkbox'
import { useEffect, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api } from '../api/client.js'
import ParameterModal from '../components/ParameterModal.jsx'
import PdfPagePreview from '../components/PdfPagePreview.jsx'

const PAGE_POINTS = { A4: { width: 595, height: 842 }, Letter: { width: 612, height: 792 } }
const ZOOMS = [{ label: 'Fit to width', value: 1 }, { label: '80%', value: 0.8 }, { label: '60%', value: 0.6 }]
const PLACEHOLDER_REGEX = /\{\{\s*(\w+)\s*\}\}/g
// Same Arabic ranges the server checks, so the preview aligns a title exactly where the PDF will.
const ARABIC_REGEX = /[\u0600-\u06FF\u0750-\u077F\uFB50-\uFDFF\uFE70-\uFEFF]/

function extractParamNames(sql) {
  const names = []
  for (const match of sql.matchAll(PLACEHOLDER_REGEX)) if (!names.includes(match[1])) names.push(match[1])
  return names
}

// Same rule the server uses when printing: a {{placeholder}} named like a parent column takes that
// column's value; any other placeholder takes the value of the chosen parent column.
function subQueryParams(sql, row, parentKeyColumn) {
  const find = (name) => Object.keys(row || {}).find((k) => k.toLowerCase() === String(name).toLowerCase())
  const params = {}
  const missing = []
  for (const name of extractParamNames(sql)) {
    const key = find(name) ?? (parentKeyColumn ? find(parentKeyColumn) : undefined)
    if (key === undefined || row[key] === null || row[key] === undefined) missing.push(name)
    else params[name] = String(row[key])
  }
  return { params, missing }
}

const uid = () => Math.random().toString(36).slice(2)
const newTable = () => ({ _uid: uid(), title: '', connectionId: null, sql: '', parentKeyColumn: '', x: 40, y: 300, width: 515, fontSize: 10, columns: [] })
const withUid = (tables) => (tables || []).map((t) => ({ ...t, _uid: uid() }))
const stripUid = (tables) => tables.map(({ _uid, ...rest }) => rest) // eslint-disable-line no-unused-vars

// Designs how one result row prints as a PDF. Fields are dragged onto the page; a table block prints the
// rows of a related query (one parent row -> many child rows) with headers typed here.
export default function PdfLayoutDesignerPage() {
  const [searchParams] = useSearchParams()
  const [templates, setTemplates] = useState([])
  const [connections, setConnections] = useState([])
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
  const [tables, setTables] = useState([])
  const [selection, setSelection] = useState(null) // { kind: 'field' | 'table', index }
  const [tableSamples, setTableSamples] = useState({}) // by table _uid: { columns, rows, loading, error }

  const [saving, setSaving] = useState(false)
  const [previewing, setPreviewing] = useState(false)
  const [message, setMessage] = useState(null)
  const [error, setError] = useState(null)
  const [hasBackgroundPdf, setHasBackgroundPdf] = useState(false)
  const [backgroundBlob, setBackgroundBlob] = useState(null)
  const [uploadingBackground, setUploadingBackground] = useState(false)

  const [zoom, setZoom] = useState(1)
  const [pageAreaWidth, setPageAreaWidth] = useState(520)
  const pageAreaRef = useRef(null)
  const dragState = useRef(null)
  const scaleRef = useRef(1)
  const backgroundFileInputRef = useRef(null)

  // The page is sized from the space it's given, so there's never a sideways scrollbar.
  const dims = PAGE_POINTS[pageSize]
  const scale = Math.min(pageAreaWidth / dims.width, 1.5) * zoom
  scaleRef.current = scale
  const canvasW = Math.floor(dims.width * scale)
  const canvasH = Math.floor(dims.height * scale)

  useEffect(() => {
    const el = pageAreaRef.current
    if (!el || typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(([entry]) => setPageAreaWidth(Math.max(240, Math.floor(entry.contentRect.width))))
    observer.observe(el)
    return () => observer.disconnect()
  }, [templateId])

  useEffect(() => {
    api.getTemplates('', '').then(setTemplates).catch((e) => setError(e.message))
    api.getConnections().then(setConnections).catch(() => {})
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
    try { setLayouts(await api.getPdfLayouts(templateId)) } catch (e) { setError(e.message) }
  }

  function resetLayoutForm() {
    setLayoutId(null); setLayoutName(''); setPageSize('A4'); setFields([]); setTables([])
    setSelection(null); setHasBackgroundPdf(false); loadBackgroundPreview(null); setMessage(null)
  }

  function loadLayoutIntoForm(layout) {
    setLayoutId(layout.id); setLayoutName(layout.name); setPageSize(layout.pageSize)
    setFields(layout.fields); setTables(withUid(layout.tables)); setSelection(null); setMessage(null)
    setHasBackgroundPdf(layout.hasBackgroundPdf)
    loadBackgroundPreview(layout.hasBackgroundPdf ? layout.id : null)
  }

  // Fetched as an authenticated blob, then drawn to a <canvas> by PdfPagePreview - a live <embed> viewer
  // put its own toolbar over the fields and swallowed page scrolling.
  async function loadBackgroundPreview(layoutIdToLoad) {
    setBackgroundBlob(null)
    if (!layoutIdToLoad) return
    try {
      const token = localStorage.getItem('qe_token')
      const res = await fetch(api.pdfBackgroundUrl(layoutIdToLoad), { headers: token ? { Authorization: `Bearer ${token}` } : {} })
      if (res.ok) setBackgroundBlob(await res.blob())
    } catch { /* preview is a nice-to-have */ }
  }

  async function handleBackgroundFileSelected(e) {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (!file || !layoutId) return
    setUploadingBackground(true); setError(null)
    try {
      await api.uploadPdfBackground(layoutId, file)
      setHasBackgroundPdf(true); loadBackgroundPreview(layoutId); setMessage('Background uploaded.')
    } catch (err) { setError(err.message) } finally { setUploadingBackground(false) }
  }

  async function handleRemoveBackground() {
    if (!layoutId || !confirm('Remove the background PDF? Generated PDFs will use a blank page again.')) return
    try { await api.deletePdfBackground(layoutId); setHasBackgroundPdf(false); loadBackgroundPreview(null) }
    catch (err) { setError(err.message) }
  }

  // ---- fields
  const updateField = (i, changes) => setFields((prev) => prev.map((f, idx) => (idx === i ? { ...f, ...changes } : f)))

  function addField(columnName) {
    setFields((prev) => [...prev, { columnName, label: columnName, x: 40, y: 40 + prev.length * 24, fontSize: 11, isTable: false }])
    setSelection({ kind: 'field', index: fields.length })
  }

  function removeField(i) { setFields((prev) => prev.filter((_, idx) => idx !== i)); setSelection(null) }

  // ---- tables
  const updateTable = (i, changes) => setTables((prev) => prev.map((t, idx) => (idx === i ? { ...t, ...changes } : t)))

  function addTable() {
    setTables((prev) => [...prev, { ...newTable(), y: 300 + prev.length * 40 }])
    setSelection({ kind: 'table', index: tables.length })
  }

  function removeTable(i) {
    setTables((prev) => prev.filter((_, idx) => idx !== i)); setSelection(null)
  }

  const updateColumn = (ti, ci, changes) =>
    setTables((prev) => prev.map((t, idx) => idx !== ti ? t : { ...t, columns: t.columns.map((c, k) => (k === ci ? { ...c, ...changes } : c)) }))

  function moveColumn(ti, ci, delta) {
    setTables((prev) => prev.map((t, idx) => {
      if (idx !== ti) return t
      const target = ci + delta
      if (target < 0 || target >= t.columns.length) return t
      const columns = [...t.columns]
      ;[columns[ci], columns[target]] = [columns[target], columns[ci]]
      return { ...t, columns }
    }))
  }

  const removeColumn = (ti, ci) =>
    setTables((prev) => prev.map((t, idx) => idx !== ti ? t : { ...t, columns: t.columns.filter((_, k) => k !== ci) }))

  const addColumn = (ti, columnName) =>
    setTables((prev) => prev.map((t, idx) => idx !== ti ? t : { ...t, columns: [...t.columns, { columnName, header: columnName, width: null }] }))

  // Runs the sub-query once against the sample parent row, to learn its columns and show a few real values.
  async function loadTableColumns(ti) {
    const table = tables[ti]
    setError(null)
    if (!table.sql.trim()) { setError('Write the sub-query first.'); return }
    if (!sampleRow) { setError('Run the main query first - there is no sample row to fill the sub-query with.'); return }
    const { params, missing } = subQueryParams(table.sql, sampleRow, table.parentKeyColumn)
    if (missing.length > 0) {
      setError(`No sample value for ${missing.map((n) => `{{${n}}}`).join(', ')}. Choose which column of the main query supplies it.`)
      return
    }
    setTableSamples((s) => ({ ...s, [table._uid]: { loading: true } }))
    try {
      const result = await api.runPage({
        connectionId: table.connectionId || selectedTemplate.connectionId, sql: table.sql, skip: 0, take: 3, parameters: params,
      })
      setTableSamples((s) => ({ ...s, [table._uid]: { columns: result.columns, rows: result.rows } }))
      // First time: print everything, with each column's own name as its header, ready to retype.
      if (table.columns.length === 0)
        updateTable(ti, { columns: result.columns.map((c) => ({ columnName: c, header: c, width: null })) })
    } catch (e) {
      setTableSamples((s) => ({ ...s, [table._uid]: { error: e.message } }))
    }
  }

  // ---- dragging (fields and table blocks share one handler)
  function startDrag(e, kind, index) {
    e.preventDefault()
    e.stopPropagation()
    setSelection({ kind, index })
    const item = kind === 'field' ? fields[index] : tables[index]
    dragState.current = { kind, index, startX: e.clientX, startY: e.clientY, origX: item.x, origY: item.y }
    window.addEventListener('mousemove', handleDragMove)
    window.addEventListener('mouseup', handleDragEnd)
  }

  function handleDragMove(e) {
    const s = dragState.current
    if (!s) return
    const x = Math.max(0, Math.round(s.origX + (e.clientX - s.startX) / scaleRef.current))
    const y = Math.max(0, Math.round(s.origY + (e.clientY - s.startY) / scaleRef.current))
    if (s.kind === 'field') updateField(s.index, { x, y })
    else updateTable(s.index, { x, y })
  }

  function handleDragEnd() {
    dragState.current = null
    window.removeEventListener('mousemove', handleDragMove)
    window.removeEventListener('mouseup', handleDragEnd)
  }

  // ---- save / preview / delete
  async function handleSaveLayout() {
    if (!layoutName.trim()) { setError('Name the layout first.'); return }
    if (fields.length === 0 && tables.length === 0) { setError('Add at least one field or table.'); return }
    setSaving(true); setError(null); setMessage(null)
    try {
      const saved = await api.savePdfLayout({
        id: layoutId, name: layoutName.trim(), favoriteId: templateId, pageSize, fields, tables: stripUid(tables),
      })
      setLayoutId(saved.id)
      setMessage('Layout saved.')
      loadLayouts()
      if (!hasBackgroundPdf) setHasBackgroundPdf(saved.hasBackgroundPdf)
    } catch (e) { setError(e.message) } finally { setSaving(false) }
  }

  async function handleDeleteLayout() {
    if (!layoutId || !confirm('Delete this layout?')) return
    await api.deletePdfLayout(layoutId)
    resetLayoutForm(); loadLayouts()
  }

  async function handlePreview() {
    if (!layoutId) { setError('Save the layout first, then you can preview it.'); return }
    if (!sampleRow) { setError('No sample row available to preview with.'); return }
    setPreviewing(true); setError(null)
    try { await api.generatePdf(layoutId, layoutName, sampleRow) }
    catch (e) { setError(e.message) } finally { setPreviewing(false) }
  }

  const selectedField = selection?.kind === 'field' ? fields[selection.index] : null
  const selectedTable = selection?.kind === 'table' ? tables[selection.index] : null
  const usedColumns = new Set(fields.map((f) => f.columnName))

  function renderTableBox(t, i) {
    const sample = tableSamples[t._uid]
    const cols = t.columns.length > 0 ? t.columns : (sample?.columns || []).map((c) => ({ columnName: c, header: c, width: null }))
    const fontPx = Math.max(7, t.fontSize * scale)
    const flex = (c) => (c.width > 0 ? c.width : 1)
    return (
      <div key={t._uid}
           className={`pdfd-table-box${selection?.kind === 'table' && selection.index === i ? ' selected' : ''}`}
           style={{ left: t.x * scale, top: t.y * scale, width: t.width * scale, fontSize: fontPx }}
           onMouseDown={(e) => startDrag(e, 'table', i)}>
        {t.title && <div className="pdfd-table-title" style={{ textAlign: ARABIC_REGEX.test(t.title) ? 'right' : 'left' }}>{t.title}</div>}
        <div className="pdfd-table-line pdfd-table-head">
          {cols.length === 0 ? <span>Table (add columns)</span> : cols.map((c, k) => <span key={k} style={{ flex: flex(c) }}>{c.header || c.columnName}</span>)}
        </div>
        {[0, 1, 2].map((r) => (
          <div key={r} className="pdfd-table-line">
            {cols.map((c, k) => (
              <span key={k} style={{ flex: flex(c) }}>{sample?.rows?.[r] ? String(sample.rows[r][c.columnName] ?? '') : '…'}</span>
            ))}
          </div>
        ))}
      </div>
    )
  }

  return (
    <div className="pdfd-page">
      <h2>PDF layout designer</h2>
      <p className="muted">
        Choose a template, then drag its columns onto the page to place them. Add a <strong>table</strong> to print the rows of a
        related query (one main row → many related rows, e.g. an order and its lines).
      </p>

      {error && <div className="error-banner">{error}</div>}

      <div className="toolbar">
        <CloudscapeSelect value={templateId} onChange={(e) => setTemplateId(e.target.value)}>
          <option value="">Select a template…</option>
          {templates.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
        </CloudscapeSelect>
      </div>

      {templateId && (
        <div className="pdfd-layout">
          {/* ---------------- left: layouts, columns, tables ---------------- */}
          <aside className="pdfd-side">
            <section className="pdfd-card">
              <h4 className="pdfd-title">Saved layouts</h4>
              {layouts.length === 0 ? <p className="muted pdfd-hint">None yet for this template.</p> : (
                <ul className="pdfd-list">
                  {layouts.map((l) => (
                    <li key={l.id}>
                      <CloudscapeButton className={`pdfd-list-button${l.id === layoutId ? ' active' : ''}`} onClick={() => loadLayoutIntoForm(l)}>
                        <span className="pdfd-ellipsis">{l.name}</span>{l.hasBackgroundPdf && <span title="Has a background PDF">🖼️</span>}
                      </CloudscapeButton>
                    </li>
                  ))}
                </ul>
              )}
              <CloudscapeButton className="secondary pdfd-full" onClick={resetLayoutForm}>+ New layout</CloudscapeButton>
            </section>

            <section className="pdfd-card">
              <h4 className="pdfd-title">Columns</h4>
              <p className="muted pdfd-hint">Click a column to drop it on the page.</p>
              {sampleError && <div className="error-banner">{sampleError}</div>}
              {sampleColumns.length === 0 && !sampleError && <p className="muted pdfd-hint">Loading columns…</p>}
              <div className="pdfd-chips">
                {sampleColumns.map((c) => (
                  <CloudscapeButton key={c} type="button" className="tag tag-suggestion pdfd-chip" disabled={usedColumns.has(c)}
                          onClick={() => addField(c)} title={c}>
                    {usedColumns.has(c) ? '✓' : '+'} {c}
                  </CloudscapeButton>
                ))}
              </div>
            </section>

            <section className="pdfd-card">
              <h4 className="pdfd-title">Tables</h4>
              <p className="muted pdfd-hint">Print related rows from another query.</p>
              <ul className="pdfd-list">
                {tables.map((t, i) => (
                  <li key={t._uid}>
                    <CloudscapeButton className={`pdfd-list-button${selection?.kind === 'table' && selection.index === i ? ' active' : ''}`}
                            onClick={() => setSelection({ kind: 'table', index: i })}>
                      <span className="pdfd-ellipsis">▦ {t.title || `Table ${i + 1}`}</span>
                    </CloudscapeButton>
                  </li>
                ))}
              </ul>
              <CloudscapeButton className="secondary pdfd-full" onClick={addTable}>+ Add table</CloudscapeButton>
            </section>
          </aside>

          {/* ---------------- centre: settings + the page ---------------- */}
          <main className="pdfd-main">
            <section className="pdfd-card">
              <h4 className="pdfd-title">Layout</h4>
              <div className="pdfd-settings">
                <CloudscapeInput className="pdfd-name" placeholder="Layout name" value={layoutName} onChange={(e) => setLayoutName(e.target.value)} />
                <CloudscapeSelect value={pageSize} onChange={(e) => setPageSize(e.target.value)} title="Page size">
                  <option value="A4">A4</option>
                  <option value="Letter">Letter</option>
                </CloudscapeSelect>
                <CloudscapeSelect value={zoom} onChange={(e) => setZoom(Number(e.target.value))} title="Zoom">
                  {ZOOMS.map((z) => <option key={z.value} value={z.value}>{z.label}</option>)}
                </CloudscapeSelect>
              </div>
              <div className="pdfd-actions">
                <CloudscapeButton onClick={handleSaveLayout} disabled={saving}>{saving ? 'Saving…' : '💾 Save'}</CloudscapeButton>
                <CloudscapeButton className="secondary" onClick={handlePreview} disabled={previewing}>{previewing ? 'Generating…' : '👁 Preview PDF'}</CloudscapeButton>
                {layoutId && <CloudscapeButton className="secondary danger" onClick={handleDeleteLayout}>🗑 Delete</CloudscapeButton>}
              </div>
              {message && <p className="test-ok pdfd-message">{message}</p>}

              {layoutId && (
                <div className="pdfd-background">
                  <input ref={backgroundFileInputRef} type="file" accept="application/pdf" style={{ display: 'none' }} onChange={handleBackgroundFileSelected} />
                  <CloudscapeButton className="secondary" onClick={() => backgroundFileInputRef.current?.click()} disabled={uploadingBackground}>
                    {uploadingBackground ? 'Uploading…' : hasBackgroundPdf ? '📤 Replace background PDF' : '📤 Upload background PDF'}
                  </CloudscapeButton>
                  {hasBackgroundPdf && (
                    <>
                      <span className="muted">Using the first page as the background.</span>
                      <CloudscapeButton className="link-button danger" onClick={handleRemoveBackground}>remove</CloudscapeButton>
                    </>
                  )}
                </div>
              )}
            </section>

            <section className="pdfd-card">
              <h4 className="pdfd-title">Page</h4>
              <div className="pdfd-page-area" ref={pageAreaRef}>
                <div className="pdfd-canvas" style={{ width: canvasW, height: canvasH }} onMouseDown={() => setSelection(null)}>
                  {backgroundBlob && <PdfPagePreview blob={backgroundBlob} width={canvasW} height={canvasH} className="pdfd-canvas-background" />}
                  {tables.map(renderTableBox)}
                  {fields.map((field, i) => (
                    <div key={i}
                         className={`pdfd-field-box${selection?.kind === 'field' && selection.index === i ? ' selected' : ''}${field.isTable ? ' is-list' : ''}`}
                         style={{ left: field.x * scale, top: field.y * scale }}
                         onMouseDown={(e) => startDrag(e, 'field', i)}>
                      {field.label || field.columnName}{field.isTable ? ' ☰' : ''}
                    </div>
                  ))}
                </div>
              </div>
              <p className="muted pdfd-hint">
                {fields.length + tables.length === 0
                  ? 'Click a column on the left to add it here, then drag it into place.'
                  : 'Drag anything to move it. Click it to edit its settings on the right.'}
              </p>
            </section>
          </main>

          {/* ---------------- right: inspector ---------------- */}
          <aside className="pdfd-inspector">
            {!selectedField && !selectedTable && (
              <section className="pdfd-card">
                <h4 className="pdfd-title">Properties</h4>
                <p className="muted pdfd-hint">Select a field or table on the page to edit it here.</p>
              </section>
            )}

            {selectedField && (
              <section className="pdfd-card">
                <div className="pdfd-title-row">
                  <h4 className="pdfd-title pdfd-ellipsis" title={selectedField.columnName}>Field · {selectedField.columnName}</h4>
                  <CloudscapeButton variant="icon" iconName="close" onClick={() => setSelection(null)} aria-label="Close" />
                </div>
                <label className="pdfd-label">Label
                  <CloudscapeInput value={selectedField.label || ''} onChange={(e) => updateField(selection.index, { label: e.target.value })} />
                </label>
                <div className="pdfd-grid-3">
                  <label className="pdfd-label">X<CloudscapeInput type="number" value={Math.round(selectedField.x)} onChange={(e) => updateField(selection.index, { x: Number(e.target.value) || 0 })} /></label>
                  <label className="pdfd-label">Y<CloudscapeInput type="number" value={Math.round(selectedField.y)} onChange={(e) => updateField(selection.index, { y: Number(e.target.value) || 0 })} /></label>
                  <label className="pdfd-label">Size<CloudscapeInput type="number" value={selectedField.fontSize} onChange={(e) => updateField(selection.index, { fontSize: Number(e.target.value) || 11 })} /></label>
                </div>
                <Checkbox
                  className="pdfd-check"
                  checked={selectedField.isTable}
                  onChange={({ detail }) => updateField(selection.index, { isTable: detail.checked })}
                >
                  Print as a bulleted list (column holds several values)
                </Checkbox>
                <CloudscapeButton className="secondary danger pdfd-full" onClick={() => removeField(selection.index)}>Remove field</CloudscapeButton>
              </section>
            )}

            {selectedTable && (() => {
              const ti = selection.index
              const sample = tableSamples[selectedTable._uid]
              const available = (sample?.columns || []).filter((c) => !selectedTable.columns.some((x) => x.columnName === c))
              return (
                <section className="pdfd-card">
                  <div className="pdfd-title-row">
                    <h4 className="pdfd-title pdfd-ellipsis">Table · {selectedTable.title || `Table ${ti + 1}`}</h4>
                    <CloudscapeButton variant="icon" iconName="close" onClick={() => setSelection(null)} aria-label="Close" />
                  </div>

                  <label className="pdfd-label">Title (optional)
                    <CloudscapeInput value={selectedTable.title} onChange={(e) => updateTable(ti, { title: e.target.value })} placeholder="e.g. Order lines" />
                  </label>

                  <label className="pdfd-label">Runs on
                    <CloudscapeSelect value={selectedTable.connectionId || ''} onChange={(e) => updateTable(ti, { connectionId: e.target.value || null })}>
                      <option value="">Same connection as the template</option>
                      {connections.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
                    </CloudscapeSelect>
                  </label>

                  <label className="pdfd-label">Sub-query
                    <CloudscapeTextarea rows={5} className="pdfd-sql" value={selectedTable.sql}
                              onChange={(e) => updateTable(ti, { sql: e.target.value })}
                              placeholder={'SELECT item, qty, price\nFROM order_lines\nWHERE order_id = {{id}}'} />
                  </label>
                  <p className="muted pdfd-hint">Put <code>{'{{name}}'}</code> where the main row's value goes. A placeholder named like a main column uses that column; otherwise it uses the column below.</p>

                  <label className="pdfd-label">Main-row column to link on
                    <CloudscapeSelect value={selectedTable.parentKeyColumn} onChange={(e) => updateTable(ti, { parentKeyColumn: e.target.value })}>
                      <option value="">Select a column…</option>
                      {sampleColumns.map((c) => <option key={c} value={c}>{c}</option>)}
                    </CloudscapeSelect>
                  </label>

                  <CloudscapeButton className="secondary pdfd-full" onClick={() => loadTableColumns(ti)} disabled={sample?.loading}>
                    {sample?.loading ? 'Loading…' : '↻ Load columns from sub-query'}
                  </CloudscapeButton>
                  {sample?.error && <div className="error-banner">{sample.error}</div>}

                  <h5 className="pdfd-subtitle">Columns &amp; headers</h5>
                  {selectedTable.columns.length === 0 ? (
                    <p className="muted pdfd-hint">Load the columns, then type the header you want printed above each one. Leave this empty to print every column under its own name.</p>
                  ) : (
                    <div className="pdfd-columns">
                      <div className="pdfd-columns-head"><span>Column</span><span>Header</span><span>Width</span><span /></div>
                      {selectedTable.columns.map((c, ci) => (
                        <div key={ci} className="pdfd-columns-row">
                          <span className="pdfd-ellipsis" title={c.columnName}>{c.columnName}</span>
                          <CloudscapeInput value={c.header} onChange={(e) => updateColumn(ti, ci, { header: e.target.value })} placeholder="Header" />
                          <CloudscapeInput type="number" value={c.width ?? ''} placeholder="auto" onChange={(e) => updateColumn(ti, ci, { width: e.target.value === '' ? null : Number(e.target.value) })} />
                          <span className="pdfd-column-buttons">
                            <CloudscapeButton variant="icon" iconName="arrow-up" onClick={() => moveColumn(ti, ci, -1)} disabled={ci === 0} aria-label="Move up" />
                            <CloudscapeButton variant="icon" iconName="arrow-down" onClick={() => moveColumn(ti, ci, 1)} disabled={ci === selectedTable.columns.length - 1} aria-label="Move down" />
                            <CloudscapeButton variant="icon" iconName="close" onClick={() => removeColumn(ti, ci)} aria-label="Remove column" />
                          </span>
                        </div>
                      ))}
                    </div>
                  )}
                  {available.length > 0 && (
                    <div className="pdfd-chips">
                      {available.map((c) => (
                        <CloudscapeButton key={c} type="button" className="tag tag-suggestion pdfd-chip" onClick={() => addColumn(ti, c)}>+ {c}</CloudscapeButton>
                      ))}
                    </div>
                  )}

                  <h5 className="pdfd-subtitle">Position</h5>
                  <div className="pdfd-grid-4">
                    <label className="pdfd-label">X<CloudscapeInput type="number" value={Math.round(selectedTable.x)} onChange={(e) => updateTable(ti, { x: Number(e.target.value) || 0 })} /></label>
                    <label className="pdfd-label">Y<CloudscapeInput type="number" value={Math.round(selectedTable.y)} onChange={(e) => updateTable(ti, { y: Number(e.target.value) || 0 })} /></label>
                    <label className="pdfd-label">Width<CloudscapeInput type="number" value={Math.round(selectedTable.width)} onChange={(e) => updateTable(ti, { width: Number(e.target.value) || 0 })} /></label>
                    <label className="pdfd-label">Size<CloudscapeInput type="number" value={selectedTable.fontSize} onChange={(e) => updateTable(ti, { fontSize: Number(e.target.value) || 10 })} /></label>
                  </div>
                  <p className="muted pdfd-hint">A long table continues on extra pages, with the header repeated.</p>

                  <CloudscapeButton className="secondary danger pdfd-full" onClick={() => removeTable(ti)}>Remove table</CloudscapeButton>
                </section>
              )
            })()}
          </aside>
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
