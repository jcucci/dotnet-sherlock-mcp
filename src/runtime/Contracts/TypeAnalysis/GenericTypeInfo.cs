namespace Sherlock.MCP.Runtime.Contracts.TypeAnalysis;

public record GenericTypeInfo(
    string TypeName,
    bool IsGenericTypeDefinition,
    bool IsConstructedGenericType,
    GenericParameterInfo[] GenericParameters,
    string[] GenericArguments,
    GenericVariance[] ParameterVariances
);
