import { buildStages } from '../utils/workflowStages.js'
import Icon from '@cloudscape-design/components/icon'

// Progress view of a workflow. Each STAGE is a column; steps that run in parallel are stacked inside
// it. Finished stages are ticked, the current one is highlighted, and within the current stage each
// parallel step shows whether it has been passed yet. A step that needs approval shows who approved it.
export default function WorkflowStepper({ steps, currentStageIndex = -1, passedSteps = [], approvals = [] }) {
  const stages = buildStages(steps)
  return (
    <ol className="wf-stepper">
      {stages.map((stage, si) => {
        const state = si < currentStageIndex ? 'done' : si === currentStageIndex ? 'current' : 'todo'
        return (
          <li key={si} className={`wf-stage wf-stage-${state}${stage.length > 1 ? ' wf-stage-parallel' : ''}`}>
            {stage.length > 1 && <span className="wf-stage-label">in parallel</span>}
            {stage.map((i) => {
              const step = steps[i]
              const approval = approvals.find((a) => a.stepIndex === i)
              const passed = state === 'done' || (state === 'current' && passedSteps.includes(i))
              return (
                <div key={i} className={`wf-step wf-step-${passed ? 'done' : state}`}>
                  <span className="wf-step-pill"
                        title={step.makesOfficial ? 'This step makes the template official' : undefined}>
                    {passed ? <Icon name="status-positive" /> : i + 1} {step.name}
                    {step.makesOfficial && <> <Icon name="flag" /></>}
                    {step.requiresApproval && <> <Icon name={approval ? 'status-positive' : 'status-pending'} /></>}
                  </span>
                  <span className="wf-step-group">
                    {step.responsibleGroup || 'owner'}
                    {approval ? ` · approved by @${approval.approvedByUsername}` : ''}
                  </span>
                </div>
              )
            })}
          </li>
        )
      })}
    </ol>
  )
}
