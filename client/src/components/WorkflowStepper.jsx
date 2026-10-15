// Horizontal progress view of a workflow: every step as a pill, the current one highlighted,
// steps already passed ticked, and the step(s) that make the template official flagged.
// Under each pill, the group responsible for it.
export default function WorkflowStepper({ steps, currentIndex }) {
  return (
    <ol className="wf-stepper">
      {steps.map((step, i) => {
        const state = i < currentIndex ? 'done' : i === currentIndex ? 'current' : 'todo'
        return (
          <li key={i} className={`wf-step wf-step-${state}`}>
            <span className="wf-step-pill" title={step.makesOfficial ? 'Reaching this step makes the template official' : undefined}>
              {state === 'done' ? '✓' : i + 1} {step.name}{step.makesOfficial ? ' 🏁' : ''}
            </span>
            <span className="wf-step-group">{step.responsibleGroup || 'owner'}</span>
          </li>
        )
      })}
    </ol>
  )
}
