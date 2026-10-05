namespace Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;

public record ResolvedPackageReferences(PackageReference[] Packages, ProjectEvaluationInfo Evaluation);
