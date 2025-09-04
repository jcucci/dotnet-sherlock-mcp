using System.Reflection;
using Sherlock.MCP.Runtime.Contracts.TypeAnalysis;

namespace Sherlock.MCP.Runtime;

public static class AttributeUtils
{
    public static AttributeInfo[] FromMember(MemberInfo member)
    {
        try
        {
            return member.GetCustomAttributesData()
                .Select(Convert)
                .ToArray();
        }
        catch
        {
            return Array.Empty<AttributeInfo>();
        }
    }

    public static AttributeInfo[] FromParameter(ParameterInfo parameter)
    {
        try
        {
            return parameter.GetCustomAttributesData()
                .Select(Convert)
                .ToArray();
        }
        catch
        {
            return Array.Empty<AttributeInfo>();
        }
    }

    public static AttributeInfo Convert(CustomAttributeData attributeData)
    {
        var constructorArgs = attributeData.ConstructorArguments
            .Select(arg => arg.Value)
            .ToArray();

        var namedArgs = attributeData.NamedArguments
            .ToDictionary(
                arg => arg.MemberName,
                arg => arg.TypedValue.Value
            );

        var attributeType = attributeData.AttributeType;
        var attributeUsage = attributeType.GetCustomAttribute<AttributeUsageAttribute>();

        return new AttributeInfo(
            AttributeType: attributeType.FullName ?? attributeType.Name,
            ConstructorArguments: constructorArgs,
            NamedArguments: namedArgs,
            AllowMultiple: attributeUsage?.AllowMultiple ?? false,
            ValidOn: attributeUsage?.ValidOn ?? AttributeTargets.All
        );
    }
}
