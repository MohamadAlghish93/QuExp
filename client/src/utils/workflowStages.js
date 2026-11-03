// Mirrors the server's WorkflowStages: a step flagged parallelWithPrevious joins the stage of the
// step above it, anything else starts a new stage. A template is always in exactly one stage.
export function buildStages(steps) {
  const stages = []
  steps.forEach((step, i) => {
    if (i === 0 || !step.parallelWithPrevious) stages.push([])
    stages[stages.length - 1].push(i)
  })
  return stages
}

export function stageName(steps, stage) {
  return stage.map((i) => steps[i].name).join(' + ')
}

// Name of the stage a template is currently in, e.g. "Review" or "Legal + Finance".
export function currentStageName(template) {
  const steps = template.workflowSteps || []
  if (steps.length === 0) return ''
  const stages = buildStages(steps)
  const stage = stages[Math.min(template.workflowStepIndex ?? 0, stages.length - 1)]
  return stageName(steps, stage)
}

// Is the template's current stage one that makes it official (and, where approval is required, approved)?
export function isCurrentStageOfficial(template) {
  const steps = template.workflowSteps || []
  if (steps.length === 0) return false
  const stages = buildStages(steps)
  const stage = stages[Math.min(template.workflowStepIndex ?? 0, stages.length - 1)]
  const approvals = template.workflowApprovals || []
  const makers = stage.filter((i) => steps[i].makesOfficial)
  return makers.length > 0 && makers.every((i) => !steps[i].requiresApproval || approvals.some((a) => a.stepIndex === i))
}
