using System.Reflection;

namespace Sherlock.MCP.Runtime.Contracts.TypeAnalysis;

public record GenericParameterInfo(
    string Name,
    int Position,
    GenericParameterAttributes Constraints,
    string[] TypeConstraints,
    bool HasReferenceTypeConstraint,
    bool HasValueTypeConstraint,
    bool HasDefaultConstructorConstraint
);
