import CloudscapeInput from '../components/CloudscapeInput.jsx'
import CloudscapeButton from '../components/CloudscapeButton.jsx'
import Checkbox from '@cloudscape-design/components/checkbox'
import { useEffect, useState, useMemo } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client.js'
import BusyOverlay from '../components/BusyOverlay.jsx'
import { useAuth } from '../auth/AuthContext.jsx'
import CategorySidebar from '../components/CategorySidebar.jsx'
import AssignFavoriteModal from '../components/AssignFavoriteModal.jsx'

export default function FavoritesPage() {
  const { isAdmin } = useAuth()
  const [allFavorites, setAllFavorites] = useState([])
  const [allTags, setAllTags] = useState([])
  const [search, setSearch] = useState('')
  const [category, setCategory] = useState('')
  const [error, setError] = useState(null)
  const [assigningFavorite, setAssigningFavorite] = useState(null)
  const [assignedToMe, setAssignedToMe] = useState([])
  const [assignedByMe, setAssignedByMe] = useState([])
  const [completeMessage, setCompleteMessage] = useState(null)
  const [completingId, setCompletingId] = useState(null)
  const [selectedIds, setSelectedIds] = useState([])
  const [bulkCategory, setBulkCategory] = useState('')
  const [bulkTag, setBulkTag] = useState('')
  const [bulkMessage, setBulkMessage] = useState(null)
  const navigate = useNavigate()

  async function load() {
    try {
      // Load everything once, then filter in the browser - this keeps the sidebar
      // counts accurate regardless of the active filter, and the favorites list
      // is small enough that client-side filtering is instant.
      const favs = await api.getFavorites('', '')
      setAllFavorites(favs)
    } catch (e) {
      setError(e.message)
    }
  }

  async function loadAssignments() {
    try {
      const [toMe, byMe] = await Promise.all([api.getAssignedToMe(), api.getAssignedByMe()])
      setAssignedToMe(toMe)
      setAssignedByMe(byMe)
    } catch {
      // Assignments panel is a convenience feature - a failed fetch shouldn't block the favorites list.
    }
  }

  async function loadTags() {
    try {
      setAllTags(await api.getTags())
    } catch {
      // Tag management is an admin convenience - a failed fetch shouldn't block the page.
    }
  }

  useEffect(() => { load(); loadAssignments(); if (isAdmin) loadTags() }, []) // eslint-disable-line react-hooks/exhaustive-deps

  async function handleRenameCategory(oldName, newName) {
    try {
      await api.renameCategory(oldName, newName)
      if (category === oldName) setCategory(newName)
      load()
    } catch (e) {
      setError(e.message)
    }
  }

  async function handleRenameTag(oldName) {
    const next = window.prompt(`Rename tag "${oldName}" to:`, oldName)
    if (!next || !next.trim() || next.trim() === oldName) return
    try {
      await api.renameTag(oldName, next.trim())
      load()
      loadTags()
    } catch (e) {
      setError(e.message)
    }
  }

  const categories = useMemo(() => {
    const set = new Set(allFavorites.map((f) => f.category))
    return [...set].sort((a, b) => a.localeCompare(b))
  }, [allFavorites])

  const counts = useMemo(() => {
    const result = {}
    for (const fav of allFavorites) {
      result[fav.category] = (result[fav.category] ?? 0) + 1
    }
    return result
  }, [allFavorites])

  const visible = useMemo(() => {
    const needle = search.trim().toLowerCase()
    return allFavorites
      .filter((f) => !category || f.category === category)
      .filter((f) => {
        if (!needle) return true
        return f.name.toLowerCase().includes(needle)
          || f.sql.toLowerCase().includes(needle)
          || f.tags.some((t) => t.toLowerCase().includes(needle))
      })
      .sort((a, b) => new Date(b.lastUsedAtUtc || b.createdAtUtc) - new Date(a.lastUsedAtUtc || a.createdAtUtc))
  }, [allFavorites, search, category])

  async function handleUse(fav) {
    await api.markFavoriteUsed(fav.id).catch(() => {})
    // t=<unique> makes every navigation distinct, so QueryWorkspace opens a new
    // tab even if you run the same favorite twice in a row.
    const params = new URLSearchParams({ sql: fav.sql, connectionId: fav.connectionId, t: String(Date.now()) })
    navigate(`/query?${params.toString()}`)
  }

  function handleCreateAlert(fav) {
    navigate('/alerts', { state: { prefillName: fav.name, prefillSql: fav.sql, prefillConnectionId: fav.connectionId } })
  }

  function toggleSelected(id) {
    setSelectedIds((prev) => (prev.includes(id) ? prev.filter((i) => i !== id) : [...prev, id]))
  }

  function toggleSelectAllVisible() {
    const visibleIds = visible.map((f) => f.id)
    const allSelected = visibleIds.every((id) => selectedIds.includes(id))
    setSelectedIds(allSelected ? selectedIds.filter((id) => !visibleIds.includes(id)) : [...new Set([...selectedIds, ...visibleIds])])
  }

  function clearSelection() {
    setSelectedIds([])
    setBulkCategory('')
    setBulkTag('')
    setBulkMessage(null)
  }

  async function handleBulkMoveCategory() {
    if (!bulkCategory.trim()) return
    try {
      const { updated } = await api.bulkMoveCategory(selectedIds, bulkCategory.trim())
      setBulkMessage(`Moved ${updated} favorite(s) to "${bulkCategory.trim()}".`)
      setBulkCategory('')
      load()
    } catch (e) {
      setError(e.message)
    }
  }

  async function handleBulkAddTag() {
    if (!bulkTag.trim()) return
    try {
      const { updated } = await api.bulkAddTag(selectedIds, bulkTag.trim())
      setBulkMessage(`Added tag "${bulkTag.trim()}" to ${updated} favorite(s).`)
      setBulkTag('')
      load()
    } catch (e) {
      setError(e.message)
    }
  }

  async function handleBulkDelete() {
    if (!confirm(`Delete ${selectedIds.length} selected favorite(s)? This cannot be undone.`)) return
    try {
      const { deleted } = await api.bulkDeleteFavorites(selectedIds)
      setBulkMessage(`Deleted ${deleted} favorite(s).`)
      setSelectedIds([])
      load()
    } catch (e) {
      setError(e.message)
    }
  }

  async function handleDelete(id) {
    if (!confirm('Delete this favorite?')) return
    try {
      await api.deleteFavorite(id)
      load()
    } catch (e) {
      setError(e.message)
    }
  }

  async function handleAssign(payload) {
    try {
      await api.createAssignment(payload)
      setAssigningFavorite(null)
      loadAssignments()
    } catch (e) {
      setError(e.message)
    }
  }

  async function handleCompleteAssignment(id) {
    if (completingId) return // already running - ignore extra clicks
    setCompletingId(id)
    try {
      await api.completeAssignment(id)
      setCompleteMessage('Marked done — the person who assigned it has been notified.')
      setTimeout(() => setCompleteMessage(null), 4000)
      await loadAssignments()
    } catch (e) {
      setError(e.message)
    } finally {
      setCompletingId(null)
    }
  }

  function handleRunAssignment(assignment) {
    const params = new URLSearchParams({
      sql: assignment.sql, connectionId: assignment.connectionId, t: String(Date.now()),
    })
    navigate(`/query?${params.toString()}`)
  }

  async function handleCancelAssignment(id) {
    if (!confirm('Cancel this assignment?')) return
    await api.deleteAssignment(id)
    loadAssignments()
  }

  return (
    <div className="favorites-layout">
      {completingId && <BusyOverlay message="Marking as done…" />}
      <CategorySidebar
        categories={categories}
        counts={counts}
        selected={category}
        onSelect={setCategory}
        totalCount={allFavorites.length}
        onRename={isAdmin ? handleRenameCategory : null}
      />

      <div className="favorites-page">
        {isAdmin && allTags.length > 0 && (
          <div className="tag-manager">
            <span className="muted">Manage tags:</span>
            {allTags.map((t) => (
              <CloudscapeButton key={t} type="button" className="tag tag-suggestion" onClick={() => handleRenameTag(t)} title={`Rename "${t}"`}>
                {t} ✎
              </CloudscapeButton>
            ))}
          </div>
        )}
        {(assignedToMe.length > 0 || assignedByMe.length > 0) && (
          <div className="assignments-panel">
            {completeMessage && <p className="test-ok">{completeMessage}</p>}
            {assignedToMe.length > 0 && (
              <div>
                <h4>Assigned to you</h4>
                <ul className="assignment-list">
                  {assignedToMe.map((a) => (
                    <li key={a.id} className="assignment-item assignment-item-detailed">
                      <div className="assignment-item-main">
                        <span>
                          <strong>{a.favoriteName}</strong> — from @{a.assignedByUsername}
                          {a.note ? `: ${a.note}` : ''}
                          {' · due '}{new Date(a.nextDueAtUtc).toLocaleDateString()}
                        </span>
                        <pre className="favorite-sql assignment-sql">{a.sql}</pre>
                      </div>
                      <div className="assignment-item-actions">
                        <CloudscapeButton className="secondary" onClick={() => handleRunAssignment(a)}>Run</CloudscapeButton>
                        <CloudscapeButton className="link-button" onClick={() => handleCompleteAssignment(a.id)} disabled={completingId !== null}>
                          {completingId === a.id ? '⏳ marking done…' : 'mark done'}
                        </CloudscapeButton>
                      </div>
                    </li>
                  ))}
                </ul>
              </div>
            )}
            {assignedByMe.length > 0 && (
              <div>
                <h4>You assigned</h4>
                <ul className="assignment-list">
                  {assignedByMe.map((a) => (
                    <li key={a.id} className="assignment-item assignment-item-detailed">
                      <div className="assignment-item-main">
                        <span>
                          <strong>{a.favoriteName}</strong> — to @{a.assignedToUsername}
                          {' · next due '}{new Date(a.nextDueAtUtc).toLocaleDateString()}
                          {a.repeatIntervalDays > 0 ? ` · repeats every ${a.repeatIntervalDays}d` : ' · one-time'}
                        </span>
                        <span className="muted">
                          {a.completionHistory && a.completionHistory.length > 0
                            ? `Completed ${a.completionHistory.length}x — last: ${new Date(a.lastCompletedAtUtc).toLocaleString()}`
                            : 'Not completed yet'}
                        </span>
                      </div>
                      <CloudscapeButton className="link-button danger" onClick={() => handleCancelAssignment(a.id)}>cancel</CloudscapeButton>
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </div>
        )}

        <div className="toolbar">
          <CloudscapeInput
            placeholder="Search name, SQL, or tag…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          {category && (
            <span className="active-filter">
              {category}
              <CloudscapeButton type="button" onClick={() => setCategory('')} aria-label="Clear category filter">×</CloudscapeButton>
            </span>
          )}
        </div>

        {selectedIds.length > 0 && (
          <div className="bulk-actions-bar">
            <span>{selectedIds.length} selected</span>
            <div className="bulk-actions-controls">
              <CloudscapeInput placeholder="Move to category…" value={bulkCategory} onChange={(e) => setBulkCategory(e.target.value)} list="existing-categories-bulk" />
              <datalist id="existing-categories-bulk">
                {categories.map((c) => <option key={c} value={c} />)}
              </datalist>
              <CloudscapeButton className="secondary" onClick={handleBulkMoveCategory} disabled={!bulkCategory.trim()}>Move</CloudscapeButton>
            </div>
            <div className="bulk-actions-controls">
              <CloudscapeInput placeholder="Add tag…" value={bulkTag} onChange={(e) => setBulkTag(e.target.value)} />
              <CloudscapeButton className="secondary" onClick={handleBulkAddTag} disabled={!bulkTag.trim()}>Add tag</CloudscapeButton>
            </div>
            <CloudscapeButton className="secondary danger" onClick={handleBulkDelete}>Delete selected</CloudscapeButton>
            <CloudscapeButton className="link-button" onClick={clearSelection}>clear selection</CloudscapeButton>
          </div>
        )}
        {bulkMessage && <p className="test-ok">{bulkMessage}</p>}

        {error && <div className="error-banner">{error}</div>}

        {visible.length > 0 && (
          <Checkbox
            className="select-all-row"
            checked={visible.every((f) => selectedIds.includes(f.id))}
            onChange={toggleSelectAllVisible}
          >
            Select all shown
          </Checkbox>
        )}

        {visible.length === 0 ? (
          <p className="muted">
            {allFavorites.length === 0
              ? 'No favorites saved yet. Save a query from the Run Query page.'
              : 'No favorites match this filter.'}
          </p>
        ) : (
          <ul className="favorite-list">
            {visible.map((fav) => (
              <li key={fav.id} className={selectedIds.includes(fav.id) ? 'favorite-card favorite-card-selected' : 'favorite-card'}>
                <div className="favorite-card-header">
                  <Checkbox
                    className="favorite-select-checkbox"
                    checked={selectedIds.includes(fav.id)}
                    onChange={() => toggleSelected(fav.id)}
                    ariaLabel={`Select ${fav.name}`}
                  />
                  <strong>{fav.name}</strong>
                  <CloudscapeButton
                    type="button"
                    className="badge badge-button"
                    onClick={() => setCategory(fav.category)}
                    title={`Show all in ${fav.category}`}
                  >
                    {fav.category}
                  </CloudscapeButton>
                </div>
                <pre className="favorite-sql">{fav.sql}</pre>
                {fav.tags.length > 0 && (
                  <div className="tag-list">
                    {fav.tags.map((t) => <span key={t} className="tag">{t}</span>)}
                  </div>
                )}
                <div className="favorite-card-actions">
                  <CloudscapeButton onClick={() => handleUse(fav)}>Run again</CloudscapeButton>
                  <CloudscapeButton className="secondary" onClick={() => setAssigningFavorite(fav)}>@ Assign</CloudscapeButton>
                  <CloudscapeButton className="secondary" onClick={() => handleCreateAlert(fav)}>🔔 Create alert</CloudscapeButton>
                  <CloudscapeButton className="secondary danger" onClick={() => handleDelete(fav.id)}>Delete</CloudscapeButton>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>

      {assigningFavorite && (
        <AssignFavoriteModal
          favorite={assigningFavorite}
          onAssign={handleAssign}
          onClose={() => setAssigningFavorite(null)}
        />
      )}
    </div>
  )
}
