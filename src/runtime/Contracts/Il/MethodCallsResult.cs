namespace Sherlock.MCP.Runtime.Contracts.Il;

public record MethodCallsResult(
    string DeclaringTypeFullName,
    string MethodName,
    int MatchedOverloads,
    bool AnyBodyless,
    MethodCallInfo[] Calls,
    FieldAccessInfo[] FieldAccesses);

public record InboundCallHit(
    string AssemblyPath,
    string CallerTypeFullName,
    string CallerMethod,
    string ReferenceKind,
    string TargetMember);

public record IlAnalysisOptions(
    bool CaseSensitive = false,
    bool IncludeNonPublic = false);
