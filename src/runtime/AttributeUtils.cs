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
            .Select(arg => ProjectValue(arg.Value))
            .ToArray();

        var namedArgs = attributeData.NamedArguments
            .ToDictionary(
                arg => arg.MemberName,
                arg => ProjectValue(arg.TypedValue.Value)
            );

        var attributeType = attributeData.AttributeType;
        var (allowMultiple, validOn) = ReadAttributeUsage(attributeType);

        return new AttributeInfo(
            AttributeType: attributeType.FullName ?? attributeType.Name,
            ConstructorArguments: constructorArgs,
            NamedArguments: namedArgs,
            AllowMultiple: allowMultiple,
            ValidOn: validOn
        );
    }

    private static object? ProjectValue(object? raw) => raw switch
    {
        null => null,
        Type t => new TypeRef(t.FullName ?? t.Name, t.Assembly.GetName().Name),
        IReadOnlyCollection<CustomAttributeTypedArgument> elems
            => elems.Select(e => ProjectValue(e.Value)).ToArray(),
        _ => raw
    };

    private static (bool AllowMultiple, AttributeTargets ValidOn) ReadAttributeUsage(Type attributeType)
    {
        try
        {
            var usage = attributeType.GetCustomAttributesData()
                .FirstOrDefault(a => a.AttributeType.FullName == "System.AttributeUsageAttribute");
            if (usage == null) return (false, AttributeTargets.All);

            var validOn = AttributeTargets.All;
            if (usage.ConstructorArguments.Count > 0)
            {
                var value = usage.ConstructorArguments[0].Value;
                if (value is AttributeTargets targets)
                {
                    validOn = targets;
                }
                else if (value is byte or sbyte or short or ushort or int or uint or long or ulong)
                {
                    validOn = (AttributeTargets)System.Convert.ToInt64(value);
                }
            }

            var allowMultiple = false;
            foreach (var named in usage.NamedArguments)
            {
                if (named.MemberName == "AllowMultiple" && named.TypedValue.Value is bool b)
                {
                    allowMultiple = b;
                    break;
                }
            }

            return (allowMultiple, validOn);
        }
        catch
        {
            return (false, AttributeTargets.All);
        }
    }
}
