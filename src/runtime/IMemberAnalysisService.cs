using System.Reflection;
using Sherlock.MCP.Runtime.Contracts.MemberAnalysis;

namespace Sherlock.MCP.Runtime;

public interface IMemberAnalysisService
{
    MemberInfo[] GetAllMembers(string assemblyPath, string typeName, MemberFilterOptions? options = null);
    MethodDetails[] GetMethods(string assemblyPath, string typeName, MemberFilterOptions? options = null);
    PropertyDetails[] GetProperties(string assemblyPath, string typeName, MemberFilterOptions? options = null);
    FieldDetails[] GetFields(string assemblyPath, string typeName, MemberFilterOptions? options = null);
    EventDetails[] GetEvents(string assemblyPath, string typeName, MemberFilterOptions? options = null);
    ConstructorDetails[] GetConstructors(string assemblyPath, string typeName, MemberFilterOptions? options = null);
}

