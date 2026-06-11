namespace Sherlock.MCP.Runtime.Contracts.Il;

public record MethodCallInfo(
    string Target,
    string Kind,
    string SourceMethod);

public record FieldAccessInfo(
    string Target,
    string Access,
    string SourceMethod);
