using System.Globalization;
using System.Reflection;
using Sherlock.MCP.Runtime.Contracts.MemberAnalysis;

namespace Sherlock.MCP.Runtime.ApiDiff;

internal static class ApiSurfaceReader
{
    private const BindingFlags DeclaredMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static IReadOnlyDictionary<string, ApiTypeSurface> Read(
        IEnumerable<Type> types, string? namespacePrefix, ICollection<string> warnings, CancellationToken cancellationToken)
    {
        var surfaces = new Dictionary<string, ApiTypeSurface>(StringComparer.Ordinal);
        var declaredIdentities = new Dictionary<Type, Dictionary<string, ApiInheritedMember>>();
        foreach (var type in types)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!MatchesNamespace(type, namespacePrefix)) continue;
            var visibility = TypeVisibility(type);
            if (visibility == ApiVisibility.None) continue;

            var surface = ReadType(type, visibility, declaredIdentities);
            if (surface.Members == null)
                warnings.Add($"Members of {surface.DisplayName} could not be read (unresolved dependency); member changes were not compared.");
            surfaces[surface.Key] = surface;
        }
        return surfaces;
    }

    internal static int TypeVisibility(Type type)
    {
        if (IsCompilerGenerated(type.Name)) return ApiVisibility.None;
        if (!type.IsNested) return type.IsPublic ? ApiVisibility.Public : ApiVisibility.None;

        var outer = TypeVisibility(type.DeclaringType!);
        var own = type.IsNestedPublic ? ApiVisibility.Public
            : (type.IsNestedFamily || type.IsNestedFamORAssem) && IsExternallyExtensible(type.DeclaringType!) ? ApiVisibility.Protected
            : ApiVisibility.None;
        return Math.Min(outer, own);
    }

    internal static string IdentityName(Type type)
    {
        if (type.IsByRef) return IdentityName(type.GetElementType()!) + "&";
        if (type.IsPointer) return IdentityName(type.GetElementType()!) + "*";
        if (type.IsArray)
        {
            var rank = type.GetArrayRank();
            return IdentityName(type.GetElementType()!) + (rank == 1 ? "[]" : "[" + new string(',', rank - 1) + "]");
        }
        if (type.IsGenericParameter)
            return (type.DeclaringMethod != null ? "!!" : "!") + type.GenericParameterPosition.ToString(CultureInfo.InvariantCulture);
        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            var definition = type.GetGenericTypeDefinition();
            return $"{definition.FullName ?? definition.Name}<{string.Join(",", type.GetGenericArguments().Select(IdentityName))}>";
        }
        return type.FullName ?? type.Name;
    }

    private static bool MatchesNamespace(Type type, string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return true;
        var ns = type.Namespace ?? string.Empty;
        return ns.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            || ns.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCompilerGenerated(string name) => name.Contains('<') || name.Contains('>');

    private static ApiTypeSurface ReadType(Type type, int visibility, Dictionary<Type, Dictionary<string, ApiInheritedMember>> declaredIdentities)
    {
        var kind = Safe(() => KindOf(type), "class");
        var isClass = kind == "class";
        var isStatic = isClass && type.IsAbstract && type.IsSealed;
        var isSealed = isClass && type.IsSealed && !isStatic;
        var isAbstract = isClass && type.IsAbstract && !isStatic;
        var isExtensible = isClass && !isSealed && !isStatic && Safe(() => HasAccessibleConstructor(type), false);
        var members = Safe<IReadOnlyDictionary<string, ApiMemberSurface>?>(() => ReadMembers(type, hidesProtected: !isExtensible), null);
        var displayName = TypeNameFormatter.FriendlyFullName(type);

        return new ApiTypeSurface(
            Key: type.FullName ?? type.Name,
            DisplayName: displayName,
            Kind: kind,
            Visibility: visibility,
            IsSealed: isSealed,
            IsAbstract: isAbstract,
            IsStatic: isStatic,
            IsExtensible: isExtensible,
            BaseTypes: Safe<IReadOnlyList<string>?>(() => BaseChain(type), null),
            Interfaces: Safe<IReadOnlyList<string>?>(() => Interfaces(type), null),
            Constraints: Safe<IReadOnlyDictionary<string, string>>(() => Constraints(type), new Dictionary<string, string>()),
            Signature: $"{ApiVisibility.Name(visibility)} {TypeModifiers(isStatic, isSealed, isAbstract)}{kind} {displayName}",
            Members: members,
            InheritedMembers: Safe<IReadOnlyDictionary<string, ApiInheritedMember>?>(() => InheritedMembers(type, declaredIdentities), null));
    }

    private static string KindOf(Type type)
    {
        if (type.IsEnum) return "enum";
        if (type.IsInterface) return "interface";
        if (type.IsValueType) return "struct";
        if (type.BaseType?.FullName == "System.MulticastDelegate") return "delegate";
        return "class";
    }

    private static string TypeModifiers(bool isStatic, bool isSealed, bool isAbstract) =>
        isStatic ? "static " : isSealed ? "sealed " : isAbstract ? "abstract " : string.Empty;

    private static bool IsExternallyExtensible(Type type) =>
        type.IsClass && !type.IsSealed && Safe(() => HasAccessibleConstructor(type), false);

    private static bool HasAccessibleConstructor(Type type) =>
        type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Any(c => c.IsPublic || c.IsFamily || c.IsFamilyOrAssembly);

    private static List<string> BaseChain(Type type) => BaseTypes(type).Select(IdentityName).ToList();

    private static IEnumerable<Type> BaseTypes(Type type)
    {
        for (var current = type.BaseType; current != null && current.FullName != "System.Object"; current = current.BaseType)
            yield return current;
    }

    private static Dictionary<string, ApiInheritedMember> InheritedMembers(
        Type type, Dictionary<Type, Dictionary<string, ApiInheritedMember>> declaredIdentities)
    {
        var inherited = new Dictionary<string, ApiInheritedMember>(StringComparer.Ordinal);
        var sources = type.IsInterface ? type.GetInterfaces() : BaseTypes(type);
        foreach (var source in sources)
            foreach (var (identity, member) in DeclaredIdentities(source, declaredIdentities))
                inherited.TryAdd(identity, member);
        return inherited;
    }

    private static Dictionary<string, ApiInheritedMember> DeclaredIdentities(
        Type type, Dictionary<Type, Dictionary<string, ApiInheritedMember>> declaredIdentities)
    {
        if (declaredIdentities.TryGetValue(type, out var known)) return known;
        var identities = new Dictionary<string, ApiInheritedMember>(StringComparer.Ordinal);
        foreach (var member in type.GetMembers(DeclaredMembers))
            if (IdentityOf(member) is { Member.Visibility: > ApiVisibility.None } entry)
                identities.TryAdd(entry.Identity, entry.Member);
        declaredIdentities[type] = identities;
        return identities;
    }

    private static (string Identity, ApiInheritedMember Member)? IdentityOf(MemberInfo member)
    {
        switch (member)
        {
            case MethodInfo method when !MemberAnalysisService.IsAccessorMethod(method) && !IsCompilerGenerated(method.Name):
                var methodVisibility = MemberVisibility(method, hidesProtected: false);
                return (MethodIdentity(method), new(methodVisibility, IdentityName(method.ReturnType), method.IsStatic, ApiVisibility.None, ApiVisibility.None));
            case PropertyInfo property:
                var getter = MemberVisibility(property.GetGetMethod(true), hidesProtected: false);
                var setter = MemberVisibility(property.GetSetMethod(true), hidesProtected: false);
                var isStatic = (property.GetGetMethod(true) ?? property.GetSetMethod(true))?.IsStatic ?? false;
                return (PropertyIdentity(property), new(Math.Max(getter, setter), IdentityName(property.PropertyType), isStatic, getter, setter));
            case FieldInfo field when !field.IsSpecialName && !IsCompilerGenerated(field.Name):
                return (FieldIdentity(field), new(FieldVisibility(field, hidesProtected: false), IdentityName(field.FieldType), field.IsStatic, ApiVisibility.None, ApiVisibility.None));
            case EventInfo eventInfo:
                var adder = eventInfo.GetAddMethod(true);
                var handler = eventInfo.EventHandlerType is { } handlerType ? IdentityName(handlerType) : null;
                return (EventIdentity(eventInfo), new(MemberVisibility(adder, hidesProtected: false), handler, adder?.IsStatic ?? false, ApiVisibility.None, ApiVisibility.None));
            default:
                return null;
        }
    }

    private static string MethodIdentity(MethodInfo method)
    {
        var arity = method.IsGenericMethodDefinition ? method.GetGenericArguments().Length : 0;
        var conversionSuffix = method.Name is "op_Implicit" or "op_Explicit" ? $"->{IdentityName(method.ReturnType)}" : string.Empty;
        return $"method:{method.Name}`{arity}({ParameterIdentity(method.GetParameters())}){conversionSuffix}";
    }

    private static string PropertyIdentity(PropertyInfo property) =>
        $"property:{property.Name}[{ParameterIdentity(property.GetIndexParameters())}]";

    private static string FieldIdentity(FieldInfo field) => $"field:{field.Name}";

    private static string EventIdentity(EventInfo eventInfo) => $"event:{eventInfo.Name}";

    private static string[] Interfaces(Type type) =>
        type.GetInterfaces().Where(i => i.IsVisible).Select(IdentityName).Distinct().Order(StringComparer.Ordinal).ToArray();

    private static Dictionary<string, string> Constraints(Type type)
    {
        var constraints = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!type.IsGenericTypeDefinition) return constraints;
        foreach (var parameter in type.GetGenericArguments().Where(parameter => parameter.DeclaringType == type))
            foreach (var (key, display) in ConstraintTokens(parameter))
                constraints[$"!{parameter.GenericParameterPosition}:{key}"] = $"{parameter.Name} : {display}";
        return constraints;
    }

    private static IEnumerable<(string Key, string Display)> ConstraintTokens(Type parameter)
    {
        var attributes = parameter.GenericParameterAttributes;
        if ((attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0) yield return ("class", "class");
        if ((attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0) yield return ("struct", "struct");
        if ((attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0
            && (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) == 0) yield return ("new()", "new()");
        foreach (var constraint in parameter.GetGenericParameterConstraints())
            if (constraint.FullName != "System.ValueType")
                yield return (IdentityName(constraint), TypeNameFormatter.FriendlyFullName(constraint));
    }

    private static Dictionary<string, ApiMemberSurface> ReadMembers(Type type, bool hidesProtected)
    {
        var members = new Dictionary<string, ApiMemberSurface>(StringComparer.Ordinal);
        foreach (var member in type.GetMembers(DeclaredMembers))
        {
            var surface = member switch
            {
                ConstructorInfo constructor => ReadConstructor(constructor, hidesProtected),
                MethodInfo method => ReadMethod(method, hidesProtected),
                PropertyInfo property => ReadProperty(property, hidesProtected),
                FieldInfo field => ReadField(field, hidesProtected),
                EventInfo eventInfo => ReadEvent(eventInfo, hidesProtected),
                _ => null
            };
            if (surface != null) members[surface.Identity] = surface;
        }
        return members;
    }

    private static int MemberVisibility(MethodBase? method, bool hidesProtected) => method switch
    {
        null => ApiVisibility.None,
        { IsPublic: true } => ApiVisibility.Public,
        { IsFamily: true } or { IsFamilyOrAssembly: true } => hidesProtected ? ApiVisibility.None : ApiVisibility.Protected,
        _ => ApiVisibility.None
    };

    private static int FieldVisibility(FieldInfo field, bool hidesProtected) => field switch
    {
        { IsPublic: true } => ApiVisibility.Public,
        { IsFamily: true } or { IsFamilyOrAssembly: true } => hidesProtected ? ApiVisibility.None : ApiVisibility.Protected,
        _ => ApiVisibility.None
    };

    private static bool IsOverride(MethodInfo? method) =>
        method is { IsVirtual: true } && (method.Attributes & MethodAttributes.NewSlot) == 0 && method.DeclaringType?.IsInterface != true;

    private static ApiMemberSurface? ReadConstructor(ConstructorInfo constructor, bool hidesProtected)
    {
        if (constructor.IsStatic) return null;
        var visibility = MemberVisibility(constructor, hidesProtected);
        if (visibility == ApiVisibility.None) return null;

        var details = MemberAnalysisService.BuildConstructorDetails(constructor);
        return new ApiMemberSurface(
            Identity: $"ctor:({ParameterIdentity(constructor.GetParameters())})",
            Kind: "constructor",
            Name: ".ctor",
            Signature: details.Signature,
            Visibility: visibility,
            ValueType: null,
            ValueTypeDisplay: null,
            IsStatic: false,
            IsVirtual: false,
            IsAbstract: false,
            IsReadOnly: false,
            IsConst: false,
            ConstantValue: null,
            GetterVisibility: ApiVisibility.None,
            SetterVisibility: ApiVisibility.None,
            IsInitOnly: false,
            Parameters: Parameters(details.Parameters));
    }

    private static ApiMemberSurface? ReadMethod(MethodInfo method, bool hidesProtected)
    {
        if (MemberAnalysisService.IsAccessorMethod(method) || IsCompilerGenerated(method.Name) || IsOverride(method)) return null;
        var visibility = MemberVisibility(method, hidesProtected);
        if (visibility == ApiVisibility.None) return null;

        var details = MemberAnalysisService.BuildMethodDetails(method);
        return new ApiMemberSurface(
            Identity: MethodIdentity(method),
            Kind: "method",
            Name: method.Name,
            Signature: details.Signature,
            Visibility: visibility,
            ValueType: IdentityName(method.ReturnType),
            ValueTypeDisplay: details.ReturnTypeName,
            IsStatic: method.IsStatic,
            IsVirtual: details.IsVirtual,
            IsAbstract: method.IsAbstract,
            IsReadOnly: false,
            IsConst: false,
            ConstantValue: null,
            GetterVisibility: ApiVisibility.None,
            SetterVisibility: ApiVisibility.None,
            IsInitOnly: false,
            Parameters: Parameters(details.Parameters));
    }

    private static ApiMemberSurface? ReadProperty(PropertyInfo property, bool hidesProtected)
    {
        var getter = property.GetGetMethod(true);
        var setter = property.GetSetMethod(true);
        if (IsOverride(getter ?? setter)) return null;
        var getterVisibility = MemberVisibility(getter, hidesProtected);
        var setterVisibility = MemberVisibility(setter, hidesProtected);
        var visibility = Math.Max(getterVisibility, setterVisibility);
        if (visibility == ApiVisibility.None) return null;

        var details = MemberAnalysisService.BuildPropertyDetails(property);
        var primary = getter ?? setter!;
        return new ApiMemberSurface(
            Identity: PropertyIdentity(property),
            Kind: "property",
            Name: property.Name,
            Signature: details.Signature,
            Visibility: visibility,
            ValueType: IdentityName(property.PropertyType),
            ValueTypeDisplay: details.TypeName,
            IsStatic: primary.IsStatic,
            IsVirtual: details.IsVirtual,
            IsAbstract: details.IsAbstract,
            IsReadOnly: false,
            IsConst: false,
            ConstantValue: null,
            GetterVisibility: getterVisibility,
            SetterVisibility: setterVisibility,
            IsInitOnly: setter != null && Safe(() => IsInitAccessor(setter), false),
            Parameters: Parameters(details.IndexerParameters));
    }

    private static ApiMemberSurface? ReadField(FieldInfo field, bool hidesProtected)
    {
        if (field.IsSpecialName || IsCompilerGenerated(field.Name)) return null;
        var visibility = FieldVisibility(field, hidesProtected);
        if (visibility == ApiVisibility.None) return null;

        var details = MemberAnalysisService.BuildFieldDetails(field);
        return new ApiMemberSurface(
            Identity: FieldIdentity(field),
            Kind: "field",
            Name: field.Name,
            Signature: details.Signature,
            Visibility: visibility,
            ValueType: IdentityName(field.FieldType),
            ValueTypeDisplay: details.TypeName,
            IsStatic: field.IsStatic,
            IsVirtual: false,
            IsAbstract: false,
            IsReadOnly: details.IsReadOnly,
            IsConst: details.IsConst,
            ConstantValue: details.ConstantValue is null ? null : Convert.ToString(details.ConstantValue, CultureInfo.InvariantCulture),
            GetterVisibility: ApiVisibility.None,
            SetterVisibility: ApiVisibility.None,
            IsInitOnly: false,
            Parameters: []);
    }

    private static ApiMemberSurface? ReadEvent(EventInfo eventInfo, bool hidesProtected)
    {
        var adder = eventInfo.GetAddMethod(true);
        if (IsOverride(adder)) return null;
        var visibility = MemberVisibility(adder, hidesProtected);
        if (visibility == ApiVisibility.None) return null;

        var details = MemberAnalysisService.BuildEventDetails(eventInfo);
        return new ApiMemberSurface(
            Identity: EventIdentity(eventInfo),
            Kind: "event",
            Name: eventInfo.Name,
            Signature: details.Signature,
            Visibility: visibility,
            ValueType: eventInfo.EventHandlerType is { } handler ? IdentityName(handler) : null,
            ValueTypeDisplay: details.EventHandlerTypeName,
            IsStatic: adder!.IsStatic,
            IsVirtual: details.IsVirtual,
            IsAbstract: details.IsAbstract,
            IsReadOnly: false,
            IsConst: false,
            ConstantValue: null,
            GetterVisibility: ApiVisibility.None,
            SetterVisibility: ApiVisibility.None,
            IsInitOnly: false,
            Parameters: []);
    }

    private static bool IsInitAccessor(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");

    private static string ParameterIdentity(ParameterInfo[] parameters) =>
        string.Join(",", parameters.Select(p => IdentityName(p.ParameterType)));

    private static ApiParameter[] Parameters(IEnumerable<ParameterDetails> parameters) =>
        parameters.Select(p => new ApiParameter(p.Name, Modifier(p), p.IsParams, p.IsOptional, p.IsOptional ? p.DefaultValue : null)).ToArray();

    private static string Modifier(ParameterDetails parameter) =>
        parameter.IsOut ? "out" : parameter.IsRef ? (parameter.IsIn ? "in" : "ref") : string.Empty;

    private static T Safe<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or TypeLoadException or BadImageFormatException)
        {
            return fallback;
        }
    }
}
