namespace Sherlock.MCP.Runtime;

public sealed class AmbiguousTypeNameException : Exception
{
    public AmbiguousTypeNameException(string typeName, IReadOnlyList<string> candidates)
        : base($"Type name '{typeName}' is ambiguous; it matches {candidates.Count} types: {string.Join(", ", candidates)}.")
    {
        TypeName = typeName;
        Candidates = candidates;
    }

    public string TypeName { get; }

    public IReadOnlyList<string> Candidates { get; }
}
