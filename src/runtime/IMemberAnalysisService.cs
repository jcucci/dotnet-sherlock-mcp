using System.Reflection;

using Sherlock.MCP.Runtime.Contracts.MemberAnalysis;

namespace Sherlock.MCP.Runtime;

public interface IMemberAnalysisService
{
    public MemberInfo[] GetAllMembers(string assemblyPath, string typeName, MemberFilterOptions? options = null);
    public MethodDetails[] GetMethods(string assemblyPath, string typeName, MemberFilterOptions? options = null);
    public PropertyDetails[] GetProperties(string assemblyPath, string typeName, MemberFilterOptions? options = null);
    public FieldDetails[] GetFields(string assemblyPath, string typeName, MemberFilterOptions? options = null);
    public EventDetails[] GetEvents(string assemblyPath, string typeName, MemberFilterOptions? options = null);
    public ConstructorDetails[] GetConstructors(string assemblyPath, string typeName, MemberFilterOptions? options = null);
}

