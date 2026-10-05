namespace Sherlock.MCP.Runtime.ProjectEvaluation;

public interface IProjectEvaluator
{
    public Task<ProjectEvaluationResult> EvaluateAsync(
        string projectPath,
        IReadOnlyDictionary<string, string> globalProperties,
        IReadOnlyList<string> properties,
        IReadOnlyList<string> items,
        CancellationToken cancellationToken = default);
}
