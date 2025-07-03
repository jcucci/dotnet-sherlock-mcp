using System.Reflection;
using System.Runtime.Loader;
using TypeAnalysisInfo = Sherlock.MCP.Runtime.Contracts.TypeAnalysis.TypeInfo;
using TypeAnalysisHierarchy = Sherlock.MCP.Runtime.Contracts.TypeAnalysis.TypeHierarchy;
using TypeAnalysisGenericTypeInfo = Sherlock.MCP.Runtime.Contracts.TypeAnalysis.GenericTypeInfo;
using TypeAnalysisAttributeInfo = Sherlock.MCP.Runtime.Contracts.TypeAnalysis.AttributeInfo;
using TypeAnalysisGenericParameterInfo = Sherlock.MCP.Runtime.Contracts.TypeAnalysis.GenericParameterInfo;
using TypeAnalysisTypeKind = Sherlock.MCP.Runtime.Contracts.TypeAnalysis.TypeKind;
using TypeAnalysisAccessibilityLevel = Sherlock.MCP.Runtime.Contracts.TypeAnalysis.AccessibilityLevel;
using TypeAnalysisGenericVariance = Sherlock.MCP.Runtime.Contracts.TypeAnalysis.GenericVariance;

namespace Sherlock.MCP.Runtime;


public interface ITypeAnalysisService
{
    /// <summary>
    /// Loads an assembly from the specified file path safely
    /// </summary>
    /// <param name="assemblyPath">Path to the assembly file</param>
    /// <returns>The loaded assembly or null if loading failed</returns>
    Assembly? LoadAssembly(string assemblyPath);

    /// <summary>
    /// Gets comprehensive information about a type
    /// </summary>
    /// <param name="type">The type to analyze</param>
    /// <returns>Detailed type information</returns>
    TypeAnalysisInfo GetTypeInfo(Type type);

    /// <summary>
    /// Gets comprehensive information about a type by name from a loaded assembly
    /// </summary>
    /// <param name="assemblyPath">Path to the assembly containing the type</param>
    /// <param name="typeName">Full name of the type</param>
    /// <returns>Detailed type information or null if type not found</returns>
    TypeAnalysisInfo? GetTypeInfo(string assemblyPath, string typeName);

    /// <summary>
    /// Gets the inheritance hierarchy for a type
    /// </summary>
    /// <param name="type">The type to analyze</param>
    /// <returns>Type hierarchy information</returns>
    TypeAnalysisHierarchy GetTypeHierarchy(Type type);

    /// <summary>
    /// Gets generic type information including parameters and constraints
    /// </summary>
    /// <param name="type">The generic type to analyze</param>
    /// <returns>Generic type information</returns>
    TypeAnalysisGenericTypeInfo GetGenericTypeInfo(Type type);

    /// <summary>
    /// Gets all attributes applied to a type
    /// </summary>
    /// <param name="type">The type to analyze</param>
    /// <returns>Array of attribute information</returns>
    TypeAnalysisAttributeInfo[] GetTypeAttributes(Type type);

    /// <summary>
    /// Gets all nested types within a parent type
    /// </summary>
    /// <param name="parentType">The parent type to analyze</param>
    /// <returns>Array of nested type information</returns>
    TypeAnalysisInfo[] GetNestedTypes(Type parentType);

    /// <summary>
    /// Gets all types from an assembly
    /// </summary>
    /// <param name="assemblyPath">Path to the assembly</param>
    /// <returns>Array of all types in the assembly</returns>
    TypeAnalysisInfo[] GetTypesFromAssembly(string assemblyPath);
}

public class TypeAnalysisService : ITypeAnalysisService
{
    private readonly Dictionary<string, Assembly> _loadedAssemblies = new();

    public Assembly? LoadAssembly(string assemblyPath)
    {
        try
        {
            if (_loadedAssemblies.TryGetValue(assemblyPath, out var cachedAssembly))
            {
                return cachedAssembly;
            }

            if (!File.Exists(assemblyPath))
            {
                return null;
            }

            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
            _loadedAssemblies[assemblyPath] = assembly;
            return assembly;
        }
        catch (Exception)
        {
            // Handle loading errors gracefully
            return null;
        }
    }

    public TypeAnalysisInfo GetTypeInfo(Type type)
    {
        var kind = GetTypeKind(type);
        var accessibility = GetAccessibilityLevel(type);
        var attributes = GetTypeAttributes(type);
        var genericParameters = type.IsGenericType ? GetGenericParameters(type) : Array.Empty<TypeAnalysisGenericParameterInfo>();
        var nestedTypes = GetNestedTypes(type);

        return new TypeAnalysisInfo(
            FullName: type.FullName ?? type.Name,
            Name: type.Name,
            Namespace: type.Namespace,
            Kind: kind,
            Accessibility: accessibility,
            IsAbstract: type.IsAbstract,
            IsSealed: type.IsSealed,
            IsStatic: type.IsAbstract && type.IsSealed && !type.IsInterface,
            IsGeneric: type.IsGenericType,
            IsNested: type.IsNested,
            AssemblyName: type.Assembly.GetName().Name,
            BaseType: type.BaseType?.FullName,
            Interfaces: type.GetInterfaces().Select(i => i.FullName ?? i.Name).ToArray(),
            Attributes: attributes,
            GenericParameters: genericParameters,
            NestedTypes: nestedTypes
        );
    }

    public TypeAnalysisInfo? GetTypeInfo(string assemblyPath, string typeName)
    {
        var assembly = LoadAssembly(assemblyPath);
        if (assembly == null)
        {
            return null;
        }

        try
        {
            var type = assembly.GetType(typeName);
            return type != null ? GetTypeInfo(type) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public TypeAnalysisHierarchy GetTypeHierarchy(Type type)
    {
        var inheritanceChain = new List<string>();
        var baseTypes = new List<TypeAnalysisInfo>();
        
        var current = type.BaseType;
        while (current != null)
        {
            inheritanceChain.Add(current.FullName ?? current.Name);
            baseTypes.Add(GetTypeInfo(current));
            current = current.BaseType;
        }

        var allInterfaces = type.GetInterfaces()
            .Select(i => i.FullName ?? i.Name)
            .ToArray();

        // Note: Getting derived types requires scanning all loaded assemblies
        // This is expensive and might not be practical in all scenarios
        var derivedTypes = Array.Empty<TypeAnalysisInfo>();

        return new TypeAnalysisHierarchy(
            TypeName: type.FullName ?? type.Name,
            InheritanceChain: inheritanceChain.ToArray(),
            AllInterfaces: allInterfaces,
            BaseTypes: baseTypes.ToArray(),
            DerivedTypes: derivedTypes
        );
    }

    public TypeAnalysisGenericTypeInfo GetGenericTypeInfo(Type type)
    {
        if (!type.IsGenericType)
        {
            return new TypeAnalysisGenericTypeInfo(
                TypeName: type.FullName ?? type.Name,
                IsGenericTypeDefinition: false,
                IsConstructedGenericType: false,
                GenericParameters: Array.Empty<TypeAnalysisGenericParameterInfo>(),
                GenericArguments: Array.Empty<string>(),
                ParameterVariances: Array.Empty<TypeAnalysisGenericVariance>()
            );
        }

        var genericParameters = GetGenericParameters(type);
        var genericArguments = type.IsGenericTypeDefinition 
            ? Array.Empty<string>()
            : type.GetGenericArguments().Select(t => t.FullName ?? t.Name).ToArray();

        var variances = type.IsGenericTypeDefinition
            ? type.GetGenericArguments()
                .Select(GetGenericVariance)
                .ToArray()
            : Array.Empty<TypeAnalysisGenericVariance>();

        return new TypeAnalysisGenericTypeInfo(
            TypeName: type.FullName ?? type.Name,
            IsGenericTypeDefinition: type.IsGenericTypeDefinition,
            IsConstructedGenericType: type.IsConstructedGenericType,
            GenericParameters: genericParameters,
            GenericArguments: genericArguments,
            ParameterVariances: variances
        );
    }

    public TypeAnalysisAttributeInfo[] GetTypeAttributes(Type type)
    {
        try
        {
            return type.GetCustomAttributesData()
                .Select(ConvertAttributeData)
                .ToArray();
        }
        catch (Exception)
        {
            return Array.Empty<TypeAnalysisAttributeInfo>();
        }
    }

    public TypeAnalysisInfo[] GetNestedTypes(Type parentType)
    {
        try
        {
            return parentType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Select(GetTypeInfo)
                .ToArray();
        }
        catch (Exception)
        {
            return Array.Empty<TypeAnalysisInfo>();
        }
    }

    public TypeAnalysisInfo[] GetTypesFromAssembly(string assemblyPath)
    {
        var assembly = LoadAssembly(assemblyPath);
        if (assembly == null)
        {
            return Array.Empty<TypeAnalysisInfo>();
        }

        try
        {
            return assembly.GetTypes()
                .Where(t => t.IsPublic || t.IsNestedPublic)
                .Select(GetTypeInfo)
                .ToArray();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Return only the successfully loaded types
            return ex.Types
                .Where(t => t != null && (t.IsPublic || t.IsNestedPublic))
                .Select(t => GetTypeInfo(t!))
                .ToArray();
        }
        catch (Exception)
        {
            return Array.Empty<TypeAnalysisInfo>();
        }
    }

    #region Private Helper Methods

    private TypeAnalysisTypeKind GetTypeKind(Type type)
    {
        if (type.IsEnum) return TypeAnalysisTypeKind.Enum;
        if (type.IsInterface) return TypeAnalysisTypeKind.Interface;
        if (type.IsValueType) return TypeAnalysisTypeKind.Struct;
        if (type.IsArray) return TypeAnalysisTypeKind.Array;
        if (type.IsPointer) return TypeAnalysisTypeKind.Pointer;
        if (type.IsByRef) return TypeAnalysisTypeKind.ByRef;
        if (type.IsGenericParameter) return TypeAnalysisTypeKind.GenericParameter;
        if (typeof(Delegate).IsAssignableFrom(type)) return TypeAnalysisTypeKind.Delegate;
        if (type.IsClass) return TypeAnalysisTypeKind.Class;
        
        return TypeAnalysisTypeKind.Unknown;
    }

    private TypeAnalysisAccessibilityLevel GetAccessibilityLevel(Type type)
    {
        if (type.IsPublic || type.IsNestedPublic) return TypeAnalysisAccessibilityLevel.Public;
        if (type.IsNestedPrivate) return TypeAnalysisAccessibilityLevel.Private;
        if (type.IsNestedFamily) return TypeAnalysisAccessibilityLevel.Protected;
        if (type.IsNestedAssembly) return TypeAnalysisAccessibilityLevel.Internal;
        if (type.IsNestedFamORAssem) return TypeAnalysisAccessibilityLevel.ProtectedInternal;
        if (type.IsNestedFamANDAssem) return TypeAnalysisAccessibilityLevel.PrivateProtected;
        if (!type.IsVisible) return TypeAnalysisAccessibilityLevel.Internal;
        
        return TypeAnalysisAccessibilityLevel.Unknown;
    }

    private TypeAnalysisGenericParameterInfo[] GetGenericParameters(Type type)
    {
        if (!type.IsGenericType)
        {
            return Array.Empty<TypeAnalysisGenericParameterInfo>();
        }

        return type.GetGenericArguments()
            .Where(t => t.IsGenericParameter)
            .Select(CreateGenericParameterInfo)
            .ToArray();
    }

    private TypeAnalysisGenericParameterInfo CreateGenericParameterInfo(Type genericParameter)
    {
        var constraints = genericParameter.GetGenericParameterConstraints()
            .Select(t => t.FullName ?? t.Name)
            .ToArray();

        var attrs = genericParameter.GenericParameterAttributes;

        return new TypeAnalysisGenericParameterInfo(
            Name: genericParameter.Name,
            Position: genericParameter.GenericParameterPosition,
            Constraints: attrs,
            TypeConstraints: constraints,
            HasReferenceTypeConstraint: (attrs & GenericParameterAttributes.ReferenceTypeConstraint) != 0,
            HasValueTypeConstraint: (attrs & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0,
            HasDefaultConstructorConstraint: (attrs & GenericParameterAttributes.DefaultConstructorConstraint) != 0
        );
    }

    private TypeAnalysisGenericVariance GetGenericVariance(Type genericParameter)
    {
        if (!genericParameter.IsGenericParameter)
        {
            return TypeAnalysisGenericVariance.None;
        }

        var attrs = genericParameter.GenericParameterAttributes;
        if ((attrs & GenericParameterAttributes.Covariant) != 0)
        {
            return TypeAnalysisGenericVariance.Covariant;
        }
        if ((attrs & GenericParameterAttributes.Contravariant) != 0)
        {
            return TypeAnalysisGenericVariance.Contravariant;
        }

        return TypeAnalysisGenericVariance.None;
    }

    private TypeAnalysisAttributeInfo ConvertAttributeData(CustomAttributeData attributeData)
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

        return new TypeAnalysisAttributeInfo(
            AttributeType: attributeType.FullName ?? attributeType.Name,
            ConstructorArguments: constructorArgs,
            NamedArguments: namedArgs,
            AllowMultiple: attributeUsage?.AllowMultiple ?? false,
            ValidOn: attributeUsage?.ValidOn ?? AttributeTargets.All
        );
    }

    #endregion
}