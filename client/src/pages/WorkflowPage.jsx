import CloudscapeSelect from '../components/CloudscapeSelect.jsx'
import CloudscapeTextarea from '../components/CloudscapeTextarea.jsx'
import CloudscapeButton from '../components/CloudscapeButton.jsx'
import { useEffect, useState } from 'react'
import { api } from '../api/client.js'
import { useAuth } from '../auth/AuthContext.jsx'
import WorkflowStepper from '../components/WorkflowStepper.jsx'
import { buildStages, stageName } from '../utils/workflowStages.js'
import BusyOverlay from '../components/BusyOverlay.jsx'

const ACTION_LABEL = {
  Started: '▶ Started', Forward: '➡ Moved forward', Back: '↩ Sent back', Detached: '✂ Detached',
  Passed: '✔ Passed', Approved: '✅ Approved', Revoked: '🚫 Approval revoked',
}

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

  function clearNote(item) { setNotes((n) => ({ ...n, [item.favoriteId]: '' })) }

  function moveForward(item, nextName) {
    run('Moving to the next step…',
      async () => { await api.moveWorkflow(item.favoriteId, item.currentStageIndex + 1, notes[item.favoriteId]); clearNote(item) },
      `Moved to "${nextName}".`)
  }

  function passStep(item, stepIndex) {
    run('Passing the step…',
      async () => { await api.passWorkflowStep(item.favoriteId, stepIndex, notes[item.favoriteId]); clearNote(item) },
      `"${item.steps[stepIndex].name}" passed.`)
  }

  function approveStep(item, stepIndex) {
    run('Approving…',
      async () => { await api.approveWorkflowStep(item.favoriteId, stepIndex, notes[item.favoriteId]); clearNote(item) },
      `You approved "${item.steps[stepIndex].name}".`)
  }

  function revokeApproval(item, stepIndex) {
    if (!confirm(`Revoke the approval of "${item.steps[stepIndex].name}"? If that approval made the template official, it goes back to draft.`)) return
    run('Revoking approval…',
      async () => { await api.revokeWorkflowApproval(item.favoriteId, stepIndex, notes[item.favoriteId]); clearNote(item) },
      'Approval revoked.')
  }

  function sendBack(item, stages) {
    const target = Number(backTarget[item.favoriteId] ?? 0)
    run('Sending back…',
      async () => { await api.moveWorkflow(item.favoriteId, target, notes[item.favoriteId]); clearNote(item) },
      `Sent back to "${stageName(item.steps, stages[target])}".`)
  }

  function detach(item) {
    if (!confirm(`Take "${item.name}" out of its workflow? It returns to simple pending approval.`)) return
    run('Detaching…', () => api.detachWorkflow(item.favoriteId), 'Detached from the workflow.')
  }

    async function toggleHistory(id) {
        // 1. If it's already open or loading, close/cancel it
        if (history[id]) {
            setHistory((h) => {
                const n = { ...h };
                delete n[id];
                return n;
            });
            return;
        }

        // 2. Set to loading state
        setHistory((h) => ({ ...h, [id]: 'loading' }));

        try {
            // 3. Await the API call OUTSIDE the setHistory updater
            const data = await api.getWorkflowHistory(id);

            // 4. Update the state with the result
            setHistory((h) => {
                // Race condition check: If the user closed it while the API was fetching, 
                // h[id] will be undefined. Only update if it is still 'loading'.
                if (h[id] !== 'loading') return h;
                return { ...h, [id]: data };
            });
        } catch (e) {
            setHistory((h) => {
                // Only delete if it's still trying to load this specific ID
                if (h[id] !== 'loading') return h;
                const n = { ...h };
                delete n[id];
                return n;
            });
            setError(e.message);
        }
    }

  function renderCard(item) {
    const stages = buildStages(item.steps)
    const stage = stages[item.currentStageIndex] || []
    const isLastStage = item.currentStageIndex >= stages.length - 1
    const nextName = isLastStage ? null : stageName(item.steps, stages[item.currentStageIndex + 1])
    const approved = (i) => item.approvals.some((a) => a.stepIndex === i)
    const entries = history[item.favoriteId]
    const showControls = item.canAct || item.canGoBack || item.canRevoke
    const pending = stage.filter((i) => !item.passedSteps.includes(i))

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

        <WorkflowStepper steps={item.steps} currentStageIndex={item.currentStageIndex}
                         passedSteps={item.passedSteps} approvals={item.approvals} />

        {item.approvals.length > 0 && (
          <ul className="wf-approvals">
            {item.approvals.map((a) => (
              <li key={a.stepIndex}>✅ “{a.stepName}” approved by @{a.approvedByUsername} · {new Date(a.approvedAtUtc).toLocaleString()}</li>
            ))}
          </ul>
        )}

        <div className="favorite-card-actions">
          <CloudscapeButton className="link-button" onClick={() => setSqlOpenId(sqlOpenId === item.favoriteId ? null : item.favoriteId)}>
            {sqlOpenId === item.favoriteId ? 'Hide SQL' : 'Review SQL'}
          </CloudscapeButton>
          <CloudscapeButton className="link-button" onClick={() => toggleHistory(item.favoriteId)}>
            {entries ? 'Hide history' : 'History'}
          </CloudscapeButton>
          {(isAdmin || item.ownerUsername === username) && (
            <CloudscapeButton className="link-button danger" onClick={() => detach(item)}>Detach from workflow</CloudscapeButton>
          )}
        </div>
        {sqlOpenId === item.favoriteId && <pre className="favorite-sql">{item.sql}</pre>}

        {entries === 'loading' && <p className="muted">Loading history…</p>}
        {Array.isArray(entries) && (
          <ul className="wf-history">
            {entries.map((h) => (
              <li key={h.id}>
                <span className="wf-history-action">{ACTION_LABEL[h.action] || h.action}</span>
                {['Forward', 'Back'].includes(h.action) ? ` ${h.fromStepName} → ${h.toStepName}` : ` “${h.toStepName}”`}
                <span className="muted"> · @{h.movedByUsername} · {new Date(h.movedAtUtc).toLocaleString()}</span>
                {h.note && <div className="wf-history-note">“{h.note}”</div>}
              </li>
            ))}
          </ul>
        )}

        {showControls ? (
          <div className="wf-actions">
            <CloudscapeTextarea rows={2} placeholder="Add a note (optional) - e.g. what you checked, or why it's being sent back"
                      value={notes[item.favoriteId] || ''}
                      onChange={(e) => setNotes((n) => ({ ...n, [item.favoriteId]: e.target.value }))} />

            <div className="wf-actions-group">
              {item.approvableSteps.map((i) => (
                <CloudscapeButton key={`a${i}`} className="wf-approve-button" onClick={() => approveStep(item, i)} disabled={!!busyMessage}>
                  ✅ Approve “{item.steps[i].name}”
                </CloudscapeButton>
              ))}

              {/* Single-step stage: one button moves it on. Parallel stage: each group passes its own step. */}
              {stage.length === 1 && !isLastStage && item.actionableSteps.includes(stage[0]) && (
                <CloudscapeButton onClick={() => moveForward(item, nextName)}
                        disabled={!!busyMessage || (item.steps[stage[0]].requiresApproval && !approved(stage[0]))}
                        title={item.steps[stage[0]].requiresApproval && !approved(stage[0]) ? 'Approve this step first' : undefined}>
                  ➡ Move to “{nextName}”
                </CloudscapeButton>
              )}
              {stage.length > 1 && item.actionableSteps.map((i) => (
                <CloudscapeButton key={`p${i}`} onClick={() => passStep(item, i)}
                        disabled={!!busyMessage || (item.steps[i].requiresApproval && !approved(i))}
                        title={item.steps[i].requiresApproval && !approved(i) ? 'Approve this step first' : undefined}>
                  ✔ Pass “{item.steps[i].name}”
                </CloudscapeButton>
              ))}
            </div>

            {item.canGoBack && (
              <div className="wf-actions-group">
                <span className="wf-back">
                  <CloudscapeSelect value={backTarget[item.favoriteId] ?? 0}
                          onChange={(e) => setBackTarget((b) => ({ ...b, [item.favoriteId]: e.target.value }))}>
                    {stages.slice(0, item.currentStageIndex).map((st, si) => (
                      <option key={si} value={si}>{si + 1}. {stageName(item.steps, st)}</option>
                    ))}
                  </CloudscapeSelect>
                  <CloudscapeButton className="secondary" onClick={() => sendBack(item, stages)} disabled={!!busyMessage}>↩ Send back</CloudscapeButton>
                </span>
              </div>
            )}

            {item.canRevoke && (
              <div className="wf-actions-group">
                {stage.filter(approved).map((i) => (
                  <CloudscapeButton key={`r${i}`} className="secondary wf-revoke-button" onClick={() => revokeApproval(item, i)} disabled={!!busyMessage}>
                    🚫 Revoke approval “{item.steps[i].name}” <span className="muted">(Admin)</span>
                  </CloudscapeButton>
                ))}
              </div>
            )}
          </div>
        ) : (
          <p className="muted wf-waiting">
            Waiting on {pending.length === 0 ? 'the next step' : pending.map((i) => item.steps[i].responsibleGroup
              ? `the “${item.steps[i].responsibleGroup}” group` : 'the template owner').join(' and ')}.
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
                <CloudscapeSelect value={pickedDefinition[t.id] || ''} onChange={(e) => setPickedDefinition((p) => ({ ...p, [t.id]: e.target.value }))}>
                  <option value="">Choose a workflow…</option>
                  {definitions.map((d) => <option key={d.id} value={d.id}>{d.name} ({d.steps.length} steps)</option>)}
                </CloudscapeSelect>
                <CloudscapeButton onClick={() => startWorkflow(t)} disabled={!!busyMessage}>🔀 Start workflow</CloudscapeButton>
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
