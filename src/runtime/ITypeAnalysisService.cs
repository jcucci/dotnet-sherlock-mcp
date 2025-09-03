using System.Reflection;
using TypeAnalysisInfo = Sherlock.MCP.Runtime.Contracts.TypeAnalysis.TypeInfo;
using TypeAnalysisHierarchy = Sherlock.MCP.Runtime.Contracts.TypeAnalysis.TypeHierarchy;
using TypeAnalysisGenericTypeInfo = Sherlock.MCP.Runtime.Contracts.TypeAnalysis.GenericTypeInfo;
using TypeAnalysisAttributeInfo = Sherlock.MCP.Runtime.Contracts.TypeAnalysis.AttributeInfo;

namespace Sherlock.MCP.Runtime;

public interface ITypeAnalysisService
{
    Assembly? LoadAssembly(string assemblyPath);
    TypeAnalysisInfo GetTypeInfo(Type type);
    TypeAnalysisInfo? GetTypeInfo(string assemblyPath, string typeName);
    TypeAnalysisHierarchy GetTypeHierarchy(Type type);
    TypeAnalysisGenericTypeInfo GetGenericTypeInfo(Type type);
    TypeAnalysisAttributeInfo[] GetTypeAttributes(Type type);
    TypeAnalysisInfo[] GetNestedTypes(Type parentType);
    TypeAnalysisInfo[] GetTypesFromAssembly(string assemblyPath);
}

