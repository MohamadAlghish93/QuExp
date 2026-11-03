using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Application.Workflows;

/// <summary>
/// Groups a workflow's steps into stages. A step flagged ParallelWithPrevious joins the stage of the
/// step above it; any other step starts a new stage. A template is always "in" exactly one stage, and
/// every step of that stage is active at once. Workflows with no parallel steps produce one step per
/// stage, so they behave exactly as plain sequential workflows always did.
/// </summary>
public static class WorkflowStages
{
    public static List<List<int>> Build(IReadOnlyList<WorkflowStep> steps)
    {
        var stages = new List<List<int>>();
        for (var i = 0; i < steps.Count; i++)
        {
            if (i == 0 || !steps[i].ParallelWithPrevious) stages.Add(new List<int>());
            stages[^1].Add(i);
        }
        return stages;
    }

    public static int StageOf(IReadOnlyList<List<int>> stages, int stepIndex)
    {
        for (var i = 0; i < stages.Count; i++)
            if (stages[i].Contains(stepIndex)) return i;
        return -1;
    }

    public static string Name(IReadOnlyList<WorkflowStep> steps, IEnumerable<int> stage) =>
        string.Join(" + ", stage.Select(i => steps[i].Name));
}
