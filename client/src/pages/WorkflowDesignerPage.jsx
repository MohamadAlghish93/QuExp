import CloudscapeInput from '../components/CloudscapeInput.jsx'
import CloudscapeButton from '../components/CloudscapeButton.jsx'
import Checkbox from '@cloudscape-design/components/checkbox'
import Icon from '@cloudscape-design/components/icon'
import { useEffect, useState } from 'react'
import { api } from '../api/client.js'
import BusyOverlay from '../components/BusyOverlay.jsx'
import WorkflowStepper from '../components/WorkflowStepper.jsx'

const NEW_WORKFLOW = () => ({
  id: null,
  name: '',
  steps: [
    { name: 'Draft', responsibleGroup: '', makesOfficial: false, requiresApproval: false, parallelWithPrevious: false },
    { name: 'Review', responsibleGroup: '', makesOfficial: false, requiresApproval: false, parallelWithPrevious: false },
    { name: 'Approval', responsibleGroup: '', makesOfficial: true, requiresApproval: true, parallelWithPrevious: false },
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
    setDraft((d) => ({ ...d, steps: [...d.steps, { name: '', responsibleGroup: '', makesOfficial: false, requiresApproval: false, parallelWithPrevious: false }] }))
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
                  <CloudscapeButton className={d.id === draft.id ? 'link-button active-link' : 'link-button'}
                          onClick={() => setDraft({ id: d.id, name: d.name, steps: d.steps })}>
                    {d.name} <span className="muted">({d.steps.length})</span>
                  </CloudscapeButton>
                </li>
              ))}
            </ul>
          )}
          <CloudscapeButton className="secondary" iconName="add-plus" onClick={() => setDraft(NEW_WORKFLOW())}>New design</CloudscapeButton>
        </div>

        <div className="pdf-designer-main">
          <div className="pdf-designer-panel">
            <h4 className="pdf-panel-title">{draft.id ? 'Edit workflow' : 'New workflow'}</h4>
            <CloudscapeInput className="wf-name-input" placeholder="Workflow name, e.g. Two-stage approval" value={draft.name}
                   onChange={(e) => setDraft((d) => ({ ...d, name: e.target.value }))} />

            <ol className="wf-design-steps">
              {draft.steps.map((step, i) => (
                <li key={i} className="wf-design-step">
                  <span className="wf-design-number">{i + 1}</span>
                  <CloudscapeInput placeholder="Step name" value={step.name} onChange={(e) => updateStep(i, { name: e.target.value })} />
                  <CloudscapeInput placeholder="Responsible group (empty = owner)" value={step.responsibleGroup}
                         list="wf-groups" onChange={(e) => updateStep(i, { responsibleGroup: e.target.value })} />
                  <Checkbox
                    className="checkbox-label"
                    checked={step.makesOfficial}
                    onChange={({ detail }) => updateStep(i, { makesOfficial: detail.checked })}
                    nativeInputAttributes={{ title: 'This step makes the template official' }}
                  >
                    <><Icon name="flag" /> Official</>
                  </Checkbox>
                  <Checkbox
                    className="checkbox-label"
                    checked={!!step.requiresApproval}
                    onChange={({ detail }) => updateStep(i, { requiresApproval: detail.checked })}
                    nativeInputAttributes={{ title: 'Shows an Approve button on this step. It must be approved before it can be passed, and only an Admin can revoke the approval.' }}
                  >
                    <><Icon name="status-pending" /> Needs approval</>
                  </Checkbox>
                  <Checkbox
                    className="checkbox-label"
                    disabled={i === 0}
                    checked={i > 0 && !!step.parallelWithPrevious}
                    onChange={({ detail }) => updateStep(i, { parallelWithPrevious: detail.checked })}
                    nativeInputAttributes={{
                      title: i === 0
                        ? 'The first step has nothing above it to run alongside'
                        : 'Runs at the same time as the step above; both must be passed before moving on',
                    }}
                  >
                    In parallel with step above
                  </Checkbox>
                  <span className="wf-design-controls">
                    <CloudscapeButton variant="icon" iconName="arrow-up" onClick={() => moveStep(i, -1)} disabled={i === 0} aria-label="Move step up" />
                    <CloudscapeButton variant="icon" iconName="arrow-down" onClick={() => moveStep(i, 1)} disabled={i === draft.steps.length - 1} aria-label="Move step down" />
                    <CloudscapeButton variant="icon" iconName="close" onClick={() => removeStep(i)} disabled={draft.steps.length <= 2} aria-label="Remove step" />
                  </span>
                </li>
              ))}
            </ol>
            <datalist id="wf-groups">{groups.map((g) => <option key={g} value={g} />)}</datalist>

            <CloudscapeButton className="secondary" iconName="add-plus" onClick={addStep}>Add step</CloudscapeButton>
            {officialCount === 0 && <p className="field-hint hint-new">Flag at least one step as official, or the template can never be published.</p>}
            <p className="field-hint hint-existing">
              <strong>Needs approval</strong> adds an Approve button to the step (it must be approved before it can be passed). If that step is also
              <strong> Official</strong>, the approval is what publishes the template - and only an Admin can revoke it.
              <strong> In parallel</strong> steps run at the same time: each group passes its own, and the template moves on once all are passed.
            </p>
            {officialCount > 1 && <p className="field-hint hint-existing">{officialCount} steps are flagged official - the template goes live at the first one it reaches.</p>}
            {groups.length === 0 && <p className="field-hint hint-new">No user groups exist yet. Assign users to groups on the Users page so steps can be handed to them.</p>}

            <div className="pdf-designer-panel wf-preview">
              <h4 className="pdf-panel-title">Preview</h4>
              <WorkflowStepper steps={draft.steps.map((st) => ({ ...st, name: st.name || '…' }))} />
            </div>

            <div className="toolbar" style={{ marginTop: 12 }}>
              <CloudscapeButton variant="primary" onClick={save} disabled={!!busyMessage}>Save workflow</CloudscapeButton>
              {draft.id && <CloudscapeButton className="secondary danger" iconName="remove" onClick={remove} disabled={!!busyMessage}>Delete</CloudscapeButton>}
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}
