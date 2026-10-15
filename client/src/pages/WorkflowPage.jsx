import { useEffect, useState } from 'react'
import { api } from '../api/client.js'
import { useAuth } from '../auth/AuthContext.jsx'
import WorkflowStepper from '../components/WorkflowStepper.jsx'
import BusyOverlay from '../components/BusyOverlay.jsx'

const ACTION_LABEL = { Started: '▶ Started', Forward: '➡ Moved forward', Back: '↩ Sent back', Detached: '✂ Detached' }

// Your workflow inbox: templates waiting on you, your own templates in flight, and a place to
// start a saved workflow on one of your templates. Moving, history and notes all live here.
export default function WorkflowPage() {
  const { username, isAdmin } = useAuth()
  const [inbox, setInbox] = useState({ waitingForMe: [], other: [] })
  const [definitions, setDefinitions] = useState([])
  const [startable, setStartable] = useState([])
  const [pickedDefinition, setPickedDefinition] = useState({}) // { [favoriteId]: definitionId }
  const [notes, setNotes] = useState({})                      // { [favoriteId]: text }
  const [backTarget, setBackTarget] = useState({})            // { [favoriteId]: step index }
  const [history, setHistory] = useState({})                  // { [favoriteId]: entries[] | 'loading' }
  const [sqlOpenId, setSqlOpenId] = useState(null)
  const [busyMessage, setBusyMessage] = useState(null)
  const [error, setError] = useState(null)
  const [message, setMessage] = useState(null)
  const [loading, setLoading] = useState(true)

  async function load() {
    try {
      const [box, templates] = await Promise.all([api.getWorkflowInbox(), api.getTemplates('', '')])
      setInbox(box)
      setStartable(templates.filter((t) => (isAdmin || t.ownerUsername === username) && !(t.workflowSteps?.length)))
      // Definitions are Editor/Admin-only; template owners always are, but don't fail the whole page if not.
      api.getWorkflowDefinitions().then(setDefinitions).catch(() => setDefinitions([]))
      setError(null)
    } catch (e) {
      setError(e.message)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, []) // eslint-disable-line react-hooks/exhaustive-deps

  // One wrapper for every action: shows the busy overlay, blocks double-clicks, reports success/failure.
  async function run(busyText, action, successText) {
    if (busyMessage) return
    setBusyMessage(busyText)
    setError(null)
    setMessage(null)
    try {
      await action()
      if (successText) {
        setMessage(successText)
        setTimeout(() => setMessage(null), 4000)
      }
      await load()
    } catch (e) {
      setError(e.message)
    } finally {
      setBusyMessage(null)
    }
  }

  function startWorkflow(t) {
    const definitionId = pickedDefinition[t.id]
    if (!definitionId) { setError('Pick a workflow first.'); return }
    run('Starting workflow…', () => api.startWorkflow(t.id, definitionId, null), `"${t.name}" entered the workflow as a draft.`)
  }

  function moveForward(item) {
    const next = item.steps[item.currentStepIndex + 1]
    run('Moving to the next step…',
      async () => { await api.moveWorkflow(item.favoriteId, item.currentStepIndex + 1, notes[item.favoriteId]); setNotes((n) => ({ ...n, [item.favoriteId]: '' })) },
      `Moved to "${next.name}".`)
  }

  function sendBack(item) {
    const target = backTarget[item.favoriteId] ?? 0
    run('Sending back…',
      async () => { await api.moveWorkflow(item.favoriteId, Number(target), notes[item.favoriteId]); setNotes((n) => ({ ...n, [item.favoriteId]: '' })) },
      `Sent back to "${item.steps[Number(target)].name}".`)
  }

  function detach(item) {
    if (!confirm(`Take "${item.name}" out of its workflow? It returns to simple pending approval.`)) return
    run('Detaching…', () => api.detachWorkflow(item.favoriteId), 'Detached from the workflow.')
  }

  async function toggleHistory(id) {
      if (history[id]) { setHistory((h) => { const n = { ...h }; delete n[id]; return n }); return }

      setHistory((h) => ({ ...h, [id]: 'loading' }))

      try {
          const data = await api.getWorkflowHistory(id);
          setHistory((h) => ({ ...h, [id]: data }));
      } catch (e) {
          setHistory((h) => {
              const n = { ...h };
              delete n[id];
              return n;
          });
          setError(e.message);
      }

    //try {
    //  setHistory((h) => ({ ...h, [id]: await api.getWorkflowHistory(id) }))
    //} catch (e) {
    //  setHistory((h) => { const n = { ...h }; delete n[id]; return n })
    //  setError(e.message)
    //}
  }

  function renderCard(item) {
    const last = item.steps.length - 1
    const entries = history[item.favoriteId]
    return (
      <li key={item.favoriteId} className="favorite-card wf-card">
        <div className="favorite-card-header">
          <strong>{item.name}</strong>
          <span className="template-badges">
            <span className={item.isOfficial ? 'badge' : 'badge badge-pending'}>{item.isOfficial ? '✓ Official' : '✎ Draft'}</span>
            <span className="badge badge-muted">{item.workflowName}</span>
          </span>
        </div>
        <p className="muted">{item.description || 'No description provided.'} · by @{item.ownerUsername}</p>

        <WorkflowStepper steps={item.steps} currentIndex={item.currentStepIndex} />

        <div className="favorite-card-actions">
          <button className="link-button" onClick={() => setSqlOpenId(sqlOpenId === item.favoriteId ? null : item.favoriteId)}>
            {sqlOpenId === item.favoriteId ? 'Hide SQL' : 'Review SQL'}
          </button>
          <button className="link-button" onClick={() => toggleHistory(item.favoriteId)}>
            {entries ? 'Hide history' : 'History'}
          </button>
          {(isAdmin || item.ownerUsername === username) && (
            <button className="link-button danger" onClick={() => detach(item)}>Detach from workflow</button>
          )}
        </div>
        {sqlOpenId === item.favoriteId && <pre className="favorite-sql">{item.sql}</pre>}

        {entries === 'loading' && <p className="muted">Loading history…</p>}
        {Array.isArray(entries) && (
          <ul className="wf-history">
            {entries.map((h) => (
              <li key={h.id}>
                <span className="wf-history-action">{ACTION_LABEL[h.action] || h.action}</span>
                {h.action !== 'Started' && h.action !== 'Detached' ? ` ${h.fromStepName} → ${h.toStepName}` : ` at ${h.toStepName}`}
                <span className="muted"> · @{h.movedByUsername} · {new Date(h.movedAtUtc).toLocaleString()}</span>
                {h.note && <div className="wf-history-note">“{h.note}”</div>}
              </li>
            ))}
          </ul>
        )}

        {item.canAct ? (
          <div className="wf-actions">
            <textarea rows={2} placeholder="Add a note (optional) - e.g. what you checked, or why it's being sent back"
                      value={notes[item.favoriteId] || ''}
                      onChange={(e) => setNotes((n) => ({ ...n, [item.favoriteId]: e.target.value }))} />
            <div className="wf-actions-row">
              {item.currentStepIndex < last && (
                <button onClick={() => moveForward(item)} disabled={!!busyMessage}>
                  ➡ Move to “{item.steps[item.currentStepIndex + 1].name}”
                </button>
              )}
              {item.currentStepIndex > 0 && (
                <span className="wf-back">
                  <select value={backTarget[item.favoriteId] ?? 0}
                          onChange={(e) => setBackTarget((b) => ({ ...b, [item.favoriteId]: e.target.value }))}>
                    {item.steps.slice(0, item.currentStepIndex).map((s, i) => <option key={i} value={i}>{i + 1}. {s.name}</option>)}
                  </select>
                  <button className="secondary" onClick={() => sendBack(item)} disabled={!!busyMessage}>↩ Send back</button>
                </span>
              )}
            </div>
          </div>
        ) : (
          <p className="muted wf-waiting">
            Waiting on {item.steps[item.currentStepIndex].responsibleGroup
              ? `the “${item.steps[item.currentStepIndex].responsibleGroup}” group` : 'the template owner'}.
          </p>
        )}
      </li>
    )
  }

  if (loading) return <p className="muted">Loading…</p>

  return (
    <div className="favorites-page">
      {busyMessage && <BusyOverlay message={busyMessage} />}
      <h2>Workflow</h2>
      <p className="muted">
        A template in a workflow stays a draft - hidden from everyone else - until it reaches a step that makes it official.
        It moves forward one step at a time, or back to any earlier step for rework.
      </p>
      {error && <div className="error-banner">{error}</div>}
      {message && <p className="test-ok">{message}</p>}

      <h3>Waiting for you ({inbox.waitingForMe.length})</h3>
      {inbox.waitingForMe.length === 0
        ? <p className="muted">Nothing needs your attention right now.</p>
        : <ul className="favorite-list">{inbox.waitingForMe.map(renderCard)}</ul>}

      <h3 style={{ marginTop: 24 }}>Other templates in a workflow ({inbox.other.length})</h3>
      {inbox.other.length === 0
        ? <p className="muted">None.</p>
        : <ul className="favorite-list">{inbox.other.map(renderCard)}</ul>}

      <h3 style={{ marginTop: 24 }}>Start a workflow</h3>
      {startable.length === 0 ? (
        <p className="muted">All your templates are already in a workflow (or you don't have any yet).</p>
      ) : definitions.length === 0 ? (
        <p className="muted">No saved workflows yet - an Editor or Admin can create one under Workflow Designs.</p>
      ) : (
        <ul className="favorite-list">
          {startable.map((t) => (
            <li key={t.id} className="favorite-card">
              <div className="favorite-card-header">
                <strong>{t.name}</strong>
                <span className="muted">{t.approvalStatus === 'Approved' ? 'currently official' : 'not yet official'}</span>
              </div>
              <div className="toolbar">
                <select value={pickedDefinition[t.id] || ''} onChange={(e) => setPickedDefinition((p) => ({ ...p, [t.id]: e.target.value }))}>
                  <option value="">Choose a workflow…</option>
                  {definitions.map((d) => <option key={d.id} value={d.id}>{d.name} ({d.steps.length} steps)</option>)}
                </select>
                <button onClick={() => startWorkflow(t)} disabled={!!busyMessage}>🔀 Start workflow</button>
              </div>
              {t.approvalStatus === 'Approved' && (
                <p className="field-hint hint-new">Starting a workflow puts this template back to draft until it reaches an official step again.</p>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
