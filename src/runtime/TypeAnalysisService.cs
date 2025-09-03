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

public class TypeAnalysisService : ITypeAnalysisService
{
    private readonly Dictionary<string, Assembly> _loadedAssemblies = [];

    public Assembly? LoadAssembly(string assemblyPath)
    {
        try
        {
            if (_loadedAssemblies.TryGetValue(assemblyPath, out var cachedAssembly))
                return cachedAssembly;

            if (!File.Exists(assemblyPath))
                return null;

            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
            _loadedAssemblies[assemblyPath] = assembly;

            return assembly;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public TypeAnalysisInfo GetTypeInfo(Type type)
    {
        return new TypeAnalysisInfo(
            FullName: type.FullName ?? type.Name,
            Name: type.Name,
            Namespace: type.Namespace,
            Kind: GetTypeKind(type),
            Accessibility: GetAccessibilityLevel(type),
            IsAbstract: type.IsAbstract,
            IsSealed: type.IsSealed,
            IsStatic: type.IsAbstract && type.IsSealed && !type.IsInterface,
            IsGeneric: type.IsGenericType,
            IsNested: type.IsNested,
            AssemblyName: type.Assembly.GetName().Name,
            BaseType: type.BaseType?.FullName,
            Interfaces: [.. type.GetInterfaces().Select(i => i.FullName ?? i.Name)],
            Attributes: GetTypeAttributes(type),
            GenericParameters: type.IsGenericType ? GetGenericParameters(type) : [],
            NestedTypes: GetNestedTypes(type)
        );
    }

    public TypeAnalysisInfo? GetTypeInfo(string assemblyPath, string typeName)
    {
        var assembly = LoadAssembly(assemblyPath);
        if (assembly == null)
            return null;

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
                GenericParameters: [],
                GenericArguments: [],
                ParameterVariances: []
            );
        }

        var genericParameters = GetGenericParameters(type);
        var genericArguments = type.IsGenericTypeDefinition
            ? []
            : type.GetGenericArguments().Select(t => t.FullName ?? t.Name).ToArray();

        var variances = type.IsGenericTypeDefinition
            ? type.GetGenericArguments().Select(GetGenericVariance).ToArray()
            : [];

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
            return type.GetCustomAttributesData().Select(AttributeUtils.Convert).ToArray();
        }
        catch (Exception)
        {
            return [];
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
            return [];

        try
        {
            return assembly.GetTypes()
                .Where(t => t.IsPublic || t.IsNestedPublic)
                .Select(GetTypeInfo)
                .ToArray();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types
                .Where(t => t != null && (t.IsPublic || t.IsNestedPublic))
                .Select(t => GetTypeInfo(t!))
                .ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static TypeAnalysisTypeKind GetTypeKind(Type type)
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

    private static TypeAnalysisAccessibilityLevel GetAccessibilityLevel(Type type)
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
            return [];

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
            return TypeAnalysisGenericVariance.None;

        var attrs = genericParameter.GenericParameterAttributes;
        if ((attrs & GenericParameterAttributes.Covariant) != 0)
            return TypeAnalysisGenericVariance.Covariant;

        if ((attrs & GenericParameterAttributes.Contravariant) != 0)
            return TypeAnalysisGenericVariance.Contravariant;

        return TypeAnalysisGenericVariance.None;
    }
}
