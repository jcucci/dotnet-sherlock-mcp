using System.Reflection;
using TypeAnalysisInfo = Sherlock.MCP.Runtime.Contracts.TypeAnalysis.TypeInfo;
using TypeAnalysisHierarchy = Sherlock.MCP.Runtime.Contracts.TypeAnalysis.TypeHierarchy;
using TypeAnalysisGenericTypeInfo = Sherlock.MCP.Runtime.Contracts.TypeAnalysis.GenericTypeInfo;
using TypeAnalysisAttributeInfo = Sherlock.MCP.Runtime.Contracts.TypeAnalysis.AttributeInfo;

namespace Sherlock.MCP.Runtime;

public interface ITypeAnalysisService
{
    public Assembly? LoadAssembly(string assemblyPath);
    public TypeAnalysisInfo GetTypeInfo(Type type);
    public TypeAnalysisInfo? GetTypeInfo(string assemblyPath, string typeName);
    public TypeAnalysisHierarchy GetTypeHierarchy(Type type);
    public TypeAnalysisHierarchy? GetTypeHierarchy(string assemblyPath, string typeName);
    public TypeAnalysisGenericTypeInfo GetGenericTypeInfo(Type type);
    public TypeAnalysisGenericTypeInfo? GetGenericTypeInfo(string assemblyPath, string typeName);
    public TypeAnalysisAttributeInfo[] GetTypeAttributes(Type type);
    public (string TypeFullName, TypeAnalysisAttributeInfo[] Attributes)? GetTypeAttributes(string assemblyPath, string typeName);
    public TypeAnalysisInfo[] GetNestedTypes(Type parentType);
    public (string TypeFullName, TypeAnalysisInfo[] NestedTypes)? GetNestedTypes(string assemblyPath, string typeName);
    public TypeAnalysisInfo[] GetTypesFromAssembly(string assemblyPath);
}

