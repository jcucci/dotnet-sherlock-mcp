namespace Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;

public record ProjectEvaluationInfo(string Mode, string? Reason)
{
    public const string MsBuildMode = "msbuild";
    public const string XmlMode = "xml";
    public const string DisabledReason = "disabled";

    public static ProjectEvaluationInfo MsBuild { get; } = new(MsBuildMode, null);

    public static ProjectEvaluationInfo Xml(string reason) => new(XmlMode, reason);
}
