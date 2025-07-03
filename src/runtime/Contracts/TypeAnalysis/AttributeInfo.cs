using System.Reflection;

namespace Sherlock.MCP.Runtime.Contracts.TypeAnalysis;

public record AttributeInfo(
    string AttributeType,
    object?[] ConstructorArguments,
    Dictionary<string, object?> NamedArguments,
    bool AllowMultiple,
    AttributeTargets ValidOn
);