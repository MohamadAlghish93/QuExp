import { useEffect, useState } from 'react'
import { api } from '../api/client.js'
import BusyOverlay from '../components/BusyOverlay.jsx'

const NEW_WORKFLOW = () => ({
  id: null,
  name: '',
  steps: [
    { name: 'Draft', responsibleGroup: '', makesOfficial: false },
    { name: 'Review', responsibleGroup: '', makesOfficial: false },
    { name: 'Approved', responsibleGroup: '', makesOfficial: true },
  ],
})

// Design reusable workflows: an ordered list of steps, each handled by a user group, with one or more
// steps flagged as "makes the template official". One flagged step = single sign-off; flag a later
// step after an earlier non-official review for a two-stage approval. Saved designs are reusable.
export default function WorkflowDesignerPage() {
  const [definitions, setDefinitions] = useState([])
  const [groups, setGroups] = useState([])
  const [draft, setDraft] = useState(NEW_WORKFLOW())
  const [busyMessage, setBusyMessage] = useState(null)
  const [message, setMessage] = useState(null)
  const [error, setError] = useState(null)
  const [loading, setLoading] = useState(true)

  async function load() {
    try {
      const [defs, grp] = await Promise.all([api.getWorkflowDefinitions(), api.getWorkflowGroups()])
      setDefinitions(defs)
      setGroups(grp)
      setError(null)
    } catch (e) {
      setError(e.message)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [])

  function updateStep(index, changes) {
    setDraft((d) => ({ ...d, steps: d.steps.map((s, i) => (i === index ? { ...s, ...changes } : s)) }))
  }

  function moveStep(index, delta) {
    setDraft((d) => {
      const target = index + delta
      if (target < 0 || target >= d.steps.length) return d
      const steps = [...d.steps]
      ;[steps[index], steps[target]] = [steps[target], steps[index]]
      return { ...d, steps }
    })
  }

  function removeStep(index) {
    setDraft((d) => ({ ...d, steps: d.steps.filter((_, i) => i !== index) }))
  }

  function addStep() {
    setDraft((d) => ({ ...d, steps: [...d.steps, { name: '', responsibleGroup: '', makesOfficial: false }] }))
  }

  async function run(busyText, action, successText) {
    if (busyMessage) return
    setBusyMessage(busyText)
    setError(null)
    setMessage(null)
    try {
      await action()
      if (successText) { setMessage(successText); setTimeout(() => setMessage(null), 4000) }
      await load()
    } catch (e) {
      setError(e.message)
    } finally {
      setBusyMessage(null)
    }
  }

  function save() {
    run('Saving workflow…', async () => {
      const saved = await api.saveWorkflowDefinition({ id: draft.id, name: draft.name, steps: draft.steps })
      setDraft({ id: saved.id, name: saved.name, steps: saved.steps })
    }, 'Workflow saved.')
  }

  function remove() {
    if (!draft.id || !confirm('Delete this workflow design? Templates already using it keep working - they hold their own copy of the steps.')) return
    run('Deleting…', async () => { await api.deleteWorkflowDefinition(draft.id); setDraft(NEW_WORKFLOW()) }, 'Workflow deleted.')
  }

  const officialCount = draft.steps.filter((s) => s.makesOfficial).length

  if (loading) return <p className="muted">Loading…</p>

  return (
    <div className="favorites-page">
      {busyMessage && <BusyOverlay message={busyMessage} />}
      <h2>Workflow designs</h2>
      <p className="muted">
        Define how a template travels between groups before it becomes official. Each step is handled by a
        user group (leave it empty to make the template's owner responsible). Saved designs can be reused on any template.
      </p>
      {error && <div className="error-banner">{error}</div>}
      {message && <p className="test-ok">{message}</p>}

      <div className="pdf-designer-layout">
        <div className="pdf-designer-sidebar">
          <h4>Saved designs</h4>
          {definitions.length === 0 ? <p className="muted">None yet.</p> : (
            <ul className="pdf-layout-list">
              {definitions.map((d) => (
                <li key={d.id}>
                  <button className={d.id === draft.id ? 'link-button active-link' : 'link-button'}
                          onClick={() => setDraft({ id: d.id, name: d.name, steps: d.steps })}>
                    {d.name} <span className="muted">({d.steps.length})</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
          <button className="secondary" onClick={() => setDraft(NEW_WORKFLOW())}>+ New design</button>
        </div>

        <div className="pdf-designer-main">
          <div className="pdf-designer-panel">
            <h4 className="pdf-panel-title">{draft.id ? 'Edit workflow' : 'New workflow'}</h4>
            <input className="wf-name-input" placeholder="Workflow name, e.g. Two-stage approval" value={draft.name}
                   onChange={(e) => setDraft((d) => ({ ...d, name: e.target.value }))} />

            <ol className="wf-design-steps">
              {draft.steps.map((step, i) => (
                <li key={i} className="wf-design-step">
                  <span className="wf-design-number">{i + 1}</span>
                  <input placeholder="Step name" value={step.name} onChange={(e) => updateStep(i, { name: e.target.value })} />
                  <input placeholder="Responsible group (empty = owner)" value={step.responsibleGroup}
                         list="wf-groups" onChange={(e) => updateStep(i, { responsibleGroup: e.target.value })} />
                  <label className="checkbox-label" title="Reaching this step makes the template official">
                    <input type="checkbox" checked={step.makesOfficial} onChange={(e) => updateStep(i, { makesOfficial: e.target.checked })} />
                    Official 🏁
                  </label>
                  <span className="wf-design-controls">
                    <button className="icon-button" onClick={() => moveStep(i, -1)} disabled={i === 0} aria-label="Move step up">↑</button>
                    <button className="icon-button" onClick={() => moveStep(i, 1)} disabled={i === draft.steps.length - 1} aria-label="Move step down">↓</button>
                    <button className="icon-button" onClick={() => removeStep(i)} disabled={draft.steps.length <= 2} aria-label="Remove step">×</button>
                  </span>
                </li>
              ))}
            </ol>
            <datalist id="wf-groups">{groups.map((g) => <option key={g} value={g} />)}</datalist>

            <button className="secondary" onClick={addStep}>+ Add step</button>
            {officialCount === 0 && <p className="field-hint hint-new">Flag at least one step as official, or the template can never be published.</p>}
            {officialCount > 1 && <p className="field-hint hint-existing">{officialCount} steps are flagged official - the template goes live at the first one it reaches.</p>}
            {groups.length === 0 && <p className="field-hint hint-new">No user groups exist yet. Assign users to groups on the Users page so steps can be handed to them.</p>}

            <div className="pdf-designer-panel wf-preview">
              <h4 className="pdf-panel-title">Preview</h4>
              <div className="wf-stepper">
                {draft.steps.map((s, i) => (
                  <span key={i} className="wf-step wf-step-todo">
                    <span className="wf-step-pill">{i + 1} {s.name || '…'}{s.makesOfficial ? ' 🏁' : ''}</span>
                    <span className="wf-step-group">{s.responsibleGroup || 'owner'}</span>
                  </span>
                ))}
              </div>
            </div>

            <div className="toolbar" style={{ marginTop: 12 }}>
              <button onClick={save} disabled={!!busyMessage}>💾 Save workflow</button>
              {draft.id && <button className="secondary danger" onClick={remove} disabled={!!busyMessage}>🗑 Delete</button>}
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}
