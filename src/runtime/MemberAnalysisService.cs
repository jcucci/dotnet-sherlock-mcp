using System.Reflection;
using System.Text;
using Sherlock.MCP.Runtime.Contracts.Common;
using Sherlock.MCP.Runtime.Contracts.MemberAnalysis;
using Sherlock.MCP.Runtime.Inspection;

namespace Sherlock.MCP.Runtime;

public class MemberAnalysisService : IMemberAnalysisService
{
    private readonly IInspectionContextProvider _contexts;

    public MemberAnalysisService() : this(new SharedInspectionContextProvider(new RuntimeOptions()))
    {
    }

    public MemberAnalysisService(IInspectionContextProvider contexts) => _contexts = contexts;

    public MemberInfo[] GetAllMembers(string assemblyPath, string typeName, MemberFilterOptions? options = null)
    {
        using var lease = _contexts.Acquire(assemblyPath);
        var type = LoadTypeFromAssembly(lease.Assembly, typeName, options);
        var bindingFlags = GetBindingFlags(options);

        return type.GetMembers(bindingFlags);
    }

    public MethodDetails[] GetMethods(string assemblyPath, string typeName, MemberFilterOptions? options = null)
        => GetMethodsPage(assemblyPath, typeName, options, options?.Skip ?? 0, options?.Take ?? int.MaxValue).Items;

    public PropertyDetails[] GetProperties(string assemblyPath, string typeName, MemberFilterOptions? options = null)
        => GetPropertiesPage(assemblyPath, typeName, options, options?.Skip ?? 0, options?.Take ?? int.MaxValue).Items;

    public FieldDetails[] GetFields(string assemblyPath, string typeName, MemberFilterOptions? options = null)
        => GetFieldsPage(assemblyPath, typeName, options, options?.Skip ?? 0, options?.Take ?? int.MaxValue).Items;

    public EventDetails[] GetEvents(string assemblyPath, string typeName, MemberFilterOptions? options = null)
        => GetEventsPage(assemblyPath, typeName, options, options?.Skip ?? 0, options?.Take ?? int.MaxValue).Items;

    public ConstructorDetails[] GetConstructors(string assemblyPath, string typeName, MemberFilterOptions? options = null)
        => GetConstructorsPage(assemblyPath, typeName, options, options?.Skip ?? 0, options?.Take ?? int.MaxValue).Items;

    public PagedResult<MethodDetails> GetMethodsPage(string assemblyPath, string typeName, MemberFilterOptions? options, int offset, int pageSize)
    {
        using var lease = _contexts.Acquire(assemblyPath);
        var type = LoadTypeFromAssembly(lease.Assembly, typeName, options);
        var methods = type.GetMethods(GetBindingFlags(options))
            .Where(m => !IsAccessorMethod(m));

        var filtered = FilterAndSortMembers(methods, options);
        return BuildPage(filtered, offset, pageSize, BuildMethodDetails);
    }

    public PagedResult<PropertyDetails> GetPropertiesPage(string assemblyPath, string typeName, MemberFilterOptions? options, int offset, int pageSize)
    {
        using var lease = _contexts.Acquire(assemblyPath);
        var type = LoadTypeFromAssembly(lease.Assembly, typeName, options);
        var filtered = FilterAndSortMembers(type.GetProperties(GetBindingFlags(options)), options);
        return BuildPage(filtered, offset, pageSize, BuildPropertyDetails);
    }

    public PagedResult<FieldDetails> GetFieldsPage(string assemblyPath, string typeName, MemberFilterOptions? options, int offset, int pageSize)
    {
        using var lease = _contexts.Acquire(assemblyPath);
        var type = LoadTypeFromAssembly(lease.Assembly, typeName, options);
        var filtered = FilterAndSortMembers(type.GetFields(GetBindingFlags(options)), options);
        return BuildPage(filtered, offset, pageSize, BuildFieldDetails);
    }

    public PagedResult<EventDetails> GetEventsPage(string assemblyPath, string typeName, MemberFilterOptions? options, int offset, int pageSize)
    {
        using var lease = _contexts.Acquire(assemblyPath);
        var type = LoadTypeFromAssembly(lease.Assembly, typeName, options);
        var filtered = FilterAndSortMembers(type.GetEvents(GetBindingFlags(options)), options);
        return BuildPage(filtered, offset, pageSize, BuildEventDetails);
    }

    public PagedResult<ConstructorDetails> GetConstructorsPage(string assemblyPath, string typeName, MemberFilterOptions? options, int offset, int pageSize)
    {
        using var lease = _contexts.Acquire(assemblyPath);
        var type = LoadTypeFromAssembly(lease.Assembly, typeName, options);
        var all = type.GetConstructors(GetBindingFlags(options))
            .Select(BuildConstructorDetails);

        // Constructors sort and filter by full signature, so details are built before paging; counts stay tiny.
        var filtered = FilterAndSortDetails(all, options, c => c.Signature, c => c.CustomAttributes).ToArray();
        var items = filtered.Skip(Math.Max(0, offset)).Take(Math.Max(0, pageSize)).ToArray();
        return new PagedResult<ConstructorDetails>(filtered.Length, items);
    }

    private static PagedResult<TDetails> BuildPage<TMember, TDetails>(IReadOnlyList<TMember> filtered, int offset, int pageSize, Func<TMember, TDetails> build)
        where TMember : MemberInfo
    {
        var items = filtered
            .Skip(Math.Max(0, offset))
            .Take(Math.Max(0, pageSize))
            .Select(build)
            .ToArray();
        return new PagedResult<TDetails>(filtered.Count, items);
    }

    private static bool IsAccessorMethod(MethodInfo method) =>
        method.IsSpecialName && (method.Name.StartsWith("get_", StringComparison.Ordinal) || method.Name.StartsWith("set_", StringComparison.Ordinal) ||
            method.Name.StartsWith("add_", StringComparison.Ordinal) || method.Name.StartsWith("remove_", StringComparison.Ordinal));

    private static MethodDetails BuildMethodDetails(MethodInfo method)
    {
        var parameters = GetParameterDetails(method.GetParameters());
        var genericParams = method.IsGenericMethodDefinition ?
            method.GetGenericArguments().Select(t => t.Name).ToArray() :
            Array.Empty<string>();
        var isOperator = IsOperatorMethod(method);
        var isExtensionMethod = IsExtensionMethod(method);
        var signature = BuildMethodSignature(method, parameters, genericParams);
        var customAttributes = AttributeUtils.FromMember(method);

        return new MethodDetails(
            Name: method.Name,
            ReturnTypeName: GetFriendlyTypeName(method.ReturnType),
            Parameters: parameters,
            GenericTypeParameters: genericParams,
            Attributes: method.Attributes,
            AccessModifier: GetAccessModifier(method),
            IsStatic: method.IsStatic,
            IsVirtual: method.IsVirtual && !method.IsFinal,
            IsAbstract: method.IsAbstract,
            IsSealed: method.IsFinal && method.IsVirtual,
            IsOverride: IsOverrideMethod(method),
            IsOperator: isOperator,
            IsExtensionMethod: isExtensionMethod,
            CustomAttributes: customAttributes,
            Signature: signature
        );
    }

    private static PropertyDetails BuildPropertyDetails(PropertyInfo property)
    {
        var indexerParams = GetParameterDetails(property.GetIndexParameters());
        var isIndexer = indexerParams.Length > 0;
        var getMethod = property.GetGetMethod(true);
        var setMethod = property.GetSetMethod(true);
        var getterAccessModifier = getMethod != null ? GetAccessModifier(getMethod) : null;
        var setterAccessModifier = setMethod != null ? GetAccessModifier(setMethod) : null;
        var primaryMethod = getMethod ?? setMethod;
        var signature = BuildPropertySignature(property, indexerParams);
        var customAttributes = AttributeUtils.FromMember(property);

        return new PropertyDetails(
            Name: property.Name,
            TypeName: GetFriendlyTypeName(property.PropertyType),
            Attributes: property.Attributes,
            AccessModifier: primaryMethod != null ? GetAccessModifier(primaryMethod) : "private",
            IsStatic: primaryMethod?.IsStatic ?? false,
            IsVirtual: primaryMethod?.IsVirtual == true && !primaryMethod.IsFinal,
            IsAbstract: primaryMethod?.IsAbstract ?? false,
            IsSealed: primaryMethod?.IsFinal == true && primaryMethod?.IsVirtual == true,
            IsOverride: primaryMethod != null && IsOverrideMethod(primaryMethod),
            CanRead: property.CanRead,
            CanWrite: property.CanWrite,
            IsIndexer: isIndexer,
            IndexerParameters: indexerParams,
            GetterAccessModifier: getterAccessModifier,
            SetterAccessModifier: setterAccessModifier,
            CustomAttributes: customAttributes,
            Signature: signature
        );
    }

    private static FieldDetails BuildFieldDetails(FieldInfo field)
    {
        object? constantValue = null;
        if (field.IsLiteral && !field.IsInitOnly)
        {
            try
            {
                constantValue = field.GetRawConstantValue();
            }
            catch
            {
            }
        }

        var signature = BuildFieldSignature(field);
        var customAttributes = AttributeUtils.FromMember(field);

        return new FieldDetails(
            Name: field.Name,
            TypeName: GetFriendlyTypeName(field.FieldType),
            Attributes: field.Attributes,
            AccessModifier: GetAccessModifier(field),
            IsStatic: field.IsStatic,
            IsReadOnly: field.IsInitOnly,
            IsConst: field.IsLiteral && !field.IsInitOnly,
            IsVolatile: IsVolatileField(field),
            IsInitOnly: field.IsInitOnly,
            ConstantValue: constantValue,
            CustomAttributes: customAttributes,
            Signature: signature
        );
    }

    private static EventDetails BuildEventDetails(EventInfo eventInfo)
    {
        var addMethod = eventInfo.GetAddMethod(true);
        var removeMethod = eventInfo.GetRemoveMethod(true);
        var addMethodAccessModifier = addMethod != null ? GetAccessModifier(addMethod) : null;
        var removeMethodAccessModifier = removeMethod != null ? GetAccessModifier(removeMethod) : null;
        var primaryMethod = addMethod ?? removeMethod;
        var signature = BuildEventSignature(eventInfo);
        var customAttributes = AttributeUtils.FromMember(eventInfo);

        return new EventDetails(
            Name: eventInfo.Name,
            EventHandlerTypeName: GetFriendlyTypeName(eventInfo.EventHandlerType!),
            Attributes: eventInfo.Attributes,
            AccessModifier: primaryMethod != null ? GetAccessModifier(primaryMethod) : "private",
            IsStatic: primaryMethod?.IsStatic ?? false,
            IsVirtual: primaryMethod?.IsVirtual == true && !primaryMethod.IsFinal,
            IsAbstract: primaryMethod?.IsAbstract ?? false,
            IsSealed: primaryMethod?.IsFinal == true && primaryMethod?.IsVirtual == true,
            IsOverride: primaryMethod != null && IsOverrideMethod(primaryMethod),
            AddMethodAccessModifier: addMethodAccessModifier,
            RemoveMethodAccessModifier: removeMethodAccessModifier,
            CustomAttributes: customAttributes,
            Signature: signature
        );
    }

    private static ConstructorDetails BuildConstructorDetails(ConstructorInfo constructor)
    {
        var parameters = GetParameterDetails(constructor.GetParameters());
        var signature = BuildConstructorSignature(constructor, parameters);
        var customAttributes = AttributeUtils.FromMember(constructor);

        return new ConstructorDetails(
            Parameters: parameters,
            Attributes: constructor.Attributes,
            AccessModifier: GetAccessModifier(constructor),
            IsStatic: constructor.IsStatic,
            CustomAttributes: customAttributes,
            Signature: signature
        );
    }

    private static Type LoadTypeFromAssembly(Assembly assembly, string typeName, MemberFilterOptions? options)
    {
        var caseSensitive = options?.CaseSensitive ?? true;

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var type = assembly.GetType(typeName);
        if (type == null)
        {
            var allTypes = assembly.GetTypes();
            type = allTypes.FirstOrDefault(t => string.Equals(t.FullName, typeName, comparison) || string.Equals(t.Name, typeName, comparison));
            if (type == null && typeName.Contains('.'))
            {
                // Normalize nested types: System.Outer.Inner => System.Outer+Inner
                var candidate = typeName.Replace('.', '+');
                type = allTypes.FirstOrDefault(t => string.Equals(t.FullName, candidate, comparison));
                if (type == null)
                {
                    // Alternate: compare with '+' in runtime full name replaced to '.'
                    type = allTypes.FirstOrDefault(t => string.Equals((t.FullName ?? t.Name).Replace('+', '.'), typeName, comparison));
                }
            }
        }

        return type ?? throw new ArgumentException($"Type '{typeName}' not found in assembly '{assembly.FullName}'");
    }

    private static BindingFlags GetBindingFlags(MemberFilterOptions? options)
    {
        options ??= new MemberFilterOptions();
        var flags = (BindingFlags)0;

        if (options.IncludePublic) flags |= BindingFlags.Public;
        if (options.IncludeNonPublic) flags |= BindingFlags.NonPublic;
        if (options.IncludeStatic) flags |= BindingFlags.Static;
        if (options.IncludeInstance) flags |= BindingFlags.Instance;
        if (options.IncludeDeclaredOnly) flags |= BindingFlags.DeclaredOnly;

        return flags;
    }

    private static ParameterDetails[] GetParameterDetails(ParameterInfo[] parameters)
    {
        return parameters.Select(p => new ParameterDetails(
            Name: p.Name ?? "param",
            TypeName: GetFriendlyTypeName(p.ParameterType),
            DefaultValue: p.HasDefaultValue ? FormatDefaultLiteral(SafeGetRawDefaultValue(p)) : null,
            IsOptional: p.IsOptional,
            IsOut: p.IsOut,
            IsRef: p.ParameterType.IsByRef && !p.IsOut,
            IsIn: p.IsIn,
            IsParams: p.CustomAttributes.Any(a => a.AttributeType.FullName == "System.ParamArrayAttribute"),
            Attributes: p.Attributes,
            CustomAttributes: AttributeUtils.FromParameter(p)
        )).ToArray();
    }

    private static object? SafeGetRawDefaultValue(ParameterInfo p)
    {
        try { return p.RawDefaultValue; }
        catch { return null; }
    }

    private static string FormatDefaultLiteral(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        string s => $"\"{EscapeCSharpString(s)}\"",
        char c => $"'{EscapeCSharpChar(c)}'",
        Enum e => $"{e.GetType().Name}.{e}",
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null"
    };

    private static string EscapeCSharpString(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value) sb.Append(EscapeCSharpChar(c, isCharLiteral: false));
        return sb.ToString();
    }

    private static string EscapeCSharpChar(char c) => EscapeCSharpChar(c, isCharLiteral: true);

    private static string EscapeCSharpChar(char c, bool isCharLiteral) => c switch
    {
        '\\' => "\\\\",
        '\0' => "\\0",
        '\a' => "\\a",
        '\b' => "\\b",
        '\f' => "\\f",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        '\v' => "\\v",
        '"' when !isCharLiteral => "\\\"",
        '\'' when isCharLiteral => "\\'",
        _ => c.ToString()
    };

    private static List<T> FilterAndSortMembers<T>(IEnumerable<T> source, MemberFilterOptions? options) where T : MemberInfo
    {
        options ??= new MemberFilterOptions();
        var comparison = options.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var query = source;

        if (!string.IsNullOrEmpty(options.NameContains))
        {
            query = query.Where(m => m.Name.Contains(options.NameContains!, comparison));
        }
        if (!string.IsNullOrEmpty(options.HasAttributeContains))
        {
            query = query.Where(m => HasAttributeMatch(m, options.HasAttributeContains!, comparison));
        }

        var descending = options.SortOrder.Equals("desc", StringComparison.OrdinalIgnoreCase);
        var ordered = options.SortBy.ToLowerInvariant() switch
        {
            "access" => descending
                ? query.OrderByDescending(AccessModifierOf).ThenByDescending(m => m.Name)
                : query.OrderBy(AccessModifierOf).ThenBy(m => m.Name),
            _ => descending
                ? query.OrderByDescending(m => m.Name)
                : query.OrderBy(m => m.Name)
        };

        return ordered.ToList();
    }

    private static IEnumerable<T> FilterAndSortDetails<T>(IEnumerable<T> source, MemberFilterOptions? options, Func<T, string> nameSelector, Func<T, Sherlock.MCP.Runtime.Contracts.TypeAnalysis.AttributeInfo[]> attrSelector)
    {
        options ??= new MemberFilterOptions();
        var comparison = options.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var query = source;

        if (!string.IsNullOrEmpty(options.NameContains))
        {
            query = query.Where(x => nameSelector(x).Contains(options.NameContains!, comparison));
        }
        if (!string.IsNullOrEmpty(options.HasAttributeContains))
        {
            query = query.Where(x => attrSelector(x).Any(a => (a.AttributeType?.IndexOf(options.HasAttributeContains!, comparison) ?? -1) >= 0));
        }

        return options.SortOrder.Equals("desc", StringComparison.OrdinalIgnoreCase)
            ? query.OrderByDescending(nameSelector)
            : query.OrderBy(nameSelector);
    }

    private static bool HasAttributeMatch(MemberInfo member, string filter, StringComparison comparison)
    {
        try
        {
            return member.GetCustomAttributesData()
                .Any(a => (a.AttributeType.FullName ?? a.AttributeType.Name).Contains(filter, comparison));
        }
        catch
        {
            return false;
        }
    }

    private static string AccessModifierOf(MemberInfo member) => member switch
    {
        PropertyInfo property => (property.GetGetMethod(true) ?? property.GetSetMethod(true)) is { } accessor ? GetAccessModifier(accessor) : "private",
        EventInfo eventInfo => (eventInfo.GetAddMethod(true) ?? eventInfo.GetRemoveMethod(true)) is { } accessor ? GetAccessModifier(accessor) : "private",
        _ => GetAccessModifier(member)
    };

    private static string GetFriendlyTypeName(Type type) => TypeNameFormatter.FriendlyName(type);

    private static string GetAccessModifier(MemberInfo member)
    {
        return member switch
        {
            MethodBase method when method.IsPublic => "public",
            MethodBase method when method.IsPrivate => "private",
            MethodBase method when method.IsFamily => "protected",
            MethodBase method when method.IsAssembly => "internal",
            MethodBase method when method.IsFamilyOrAssembly => "protected internal",
            MethodBase method when method.IsFamilyAndAssembly => "private protected",
            FieldInfo field when field.IsPublic => "public",
            FieldInfo field when field.IsPrivate => "private",
            FieldInfo field when field.IsFamily => "protected",
            FieldInfo field when field.IsAssembly => "internal",
            FieldInfo field when field.IsFamilyOrAssembly => "protected internal",
            FieldInfo field when field.IsFamilyAndAssembly => "private protected",
            _ => "private"
        };
    }
    private static bool IsOperatorMethod(MethodInfo method)
    {
        return method.IsSpecialName && method.IsStatic &&
               (method.Name.StartsWith("op_", StringComparison.Ordinal) || method.Name == "True" || method.Name == "False");
    }
    private static bool IsExtensionMethod(MethodInfo method)
    {
        return method.IsStatic &&
               method.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.ExtensionAttribute");
    }
    private static bool IsOverrideMethod(MethodBase method)
    {
        if (method is not MethodInfo methodInfo) return false;
        if (!methodInfo.IsVirtual) return false;
        return (methodInfo.Attributes & MethodAttributes.NewSlot) == 0;
    }
    private static bool IsVolatileField(FieldInfo field)
    {
        var requiredModifiers = field.GetRequiredCustomModifiers();
        return requiredModifiers.Any(m => m.FullName == "System.Runtime.CompilerServices.IsVolatile");
    }
    private static bool IsInterfaceMember(MemberInfo member) => member.DeclaringType?.IsInterface == true;

    private static string BuildMethodSignature(MethodInfo method, ParameterDetails[] parameters, string[] genericParams)
    {
        var sb = new StringBuilder();
        var onInterface = IsInterfaceMember(method);
        var suppressPublic = onInterface && method.IsPublic;
        var suppressImplicitAbstractVirtual = onInterface && !method.IsStatic;
        if (!suppressPublic)
        {
            sb.Append(GetAccessModifier(method));
            sb.Append(' ');
        }
        if (method.IsStatic) sb.Append("static ");
        if (method.IsAbstract && !suppressImplicitAbstractVirtual) sb.Append("abstract ");
        else if (method.IsVirtual && !method.IsFinal && !IsOverrideMethod(method) && !suppressImplicitAbstractVirtual) sb.Append("virtual ");
        else if (IsOverrideMethod(method)) sb.Append("override ");
        else if (method.IsFinal && method.IsVirtual) sb.Append("sealed ");
        sb.Append(GetFriendlyTypeName(method.ReturnType));
        sb.Append(' ');
        sb.Append(method.Name);
        if (genericParams.Length > 0)
        {
            sb.Append('<');
            sb.Append(string.Join(", ", genericParams));
            sb.Append('>');
        }
        sb.Append('(');
        sb.Append(string.Join(", ", parameters.Select(FormatParameter)));
        sb.Append(')');
        return sb.ToString();
    }

    private static string BuildPropertySignature(PropertyInfo property, ParameterDetails[] indexerParams)
    {
        var sb = new StringBuilder();
        var onInterface = IsInterfaceMember(property);
        var getMethod = property.GetGetMethod(true);
        var setMethod = property.GetSetMethod(true);
        var primaryMethod = getMethod ?? setMethod;
        if (primaryMethod != null)
        {
            var suppressPublic = onInterface && primaryMethod.IsPublic;
            var suppressImplicitAbstractVirtual = onInterface && !primaryMethod.IsStatic;
            if (!suppressPublic)
            {
                sb.Append(GetAccessModifier(primaryMethod));
                sb.Append(' ');
            }
            if (primaryMethod.IsStatic) sb.Append("static ");
            if (primaryMethod.IsAbstract && !suppressImplicitAbstractVirtual) sb.Append("abstract ");
            else if (primaryMethod.IsVirtual && !primaryMethod.IsFinal && !IsOverrideMethod(primaryMethod) && !suppressImplicitAbstractVirtual) sb.Append("virtual ");
            else if (IsOverrideMethod(primaryMethod)) sb.Append("override ");
            else if (primaryMethod.IsFinal && primaryMethod.IsVirtual) sb.Append("sealed ");
        }
        sb.Append(GetFriendlyTypeName(property.PropertyType));
        sb.Append(' ');
        if (indexerParams.Length > 0)
        {
            sb.Append("this[");
            sb.Append(string.Join(", ", indexerParams.Select(FormatParameter)));
            sb.Append(']');
        }
        else
        {
            sb.Append(property.Name);
        }
        sb.Append(" { ");
        if (property.CanRead) sb.Append("get; ");
        if (property.CanWrite) sb.Append("set; ");
        sb.Append('}');
        return sb.ToString();
    }
    private static string BuildFieldSignature(FieldInfo field)
    {
        var sb = new StringBuilder();
        sb.Append(GetAccessModifier(field));
        sb.Append(' ');
        if (field.IsStatic) sb.Append("static ");
        if (field.IsLiteral && !field.IsInitOnly) sb.Append("const ");
        else if (field.IsInitOnly) sb.Append("readonly ");
        if (IsVolatileField(field)) sb.Append("volatile ");
        sb.Append(GetFriendlyTypeName(field.FieldType));
        sb.Append(' ');
        sb.Append(field.Name);
        return sb.ToString();
    }

    private static string BuildEventSignature(EventInfo eventInfo)
    {
        var sb = new StringBuilder();
        var onInterface = IsInterfaceMember(eventInfo);
        var addMethod = eventInfo.GetAddMethod(true);
        var primaryMethod = addMethod;
        if (primaryMethod != null)
        {
            var suppressPublic = onInterface && primaryMethod.IsPublic;
            var suppressImplicitAbstractVirtual = onInterface && !primaryMethod.IsStatic;
            if (!suppressPublic)
            {
                sb.Append(GetAccessModifier(primaryMethod));
                sb.Append(' ');
            }
            if (primaryMethod.IsStatic) sb.Append("static ");
            if (primaryMethod.IsAbstract && !suppressImplicitAbstractVirtual) sb.Append("abstract ");
            else if (primaryMethod.IsVirtual && !primaryMethod.IsFinal && !IsOverrideMethod(primaryMethod) && !suppressImplicitAbstractVirtual) sb.Append("virtual ");
            else if (IsOverrideMethod(primaryMethod)) sb.Append("override ");
            else if (primaryMethod.IsFinal && primaryMethod.IsVirtual) sb.Append("sealed ");
        }
        sb.Append("event ");
        sb.Append(GetFriendlyTypeName(eventInfo.EventHandlerType!));
        sb.Append(' ');
        sb.Append(eventInfo.Name);
        return sb.ToString();
    }

    private static string BuildConstructorSignature(ConstructorInfo constructor, ParameterDetails[] parameters)
    {
        var sb = new StringBuilder();
        sb.Append(GetAccessModifier(constructor));
        sb.Append(' ');
        if (constructor.IsStatic) sb.Append("static ");
        var typeName = constructor.DeclaringType?.Name ?? "Constructor";
        sb.Append(typeName);
        sb.Append('(');
        sb.Append(string.Join(", ", parameters.Select(FormatParameter)));
        sb.Append(')');
        return sb.ToString();
    }

    private static string FormatParameter(ParameterDetails param)
    {
        var sb = new StringBuilder();
        if (param.IsOut) sb.Append("out ");
        else if (param.IsRef) sb.Append("ref ");
        else if (param.IsIn) sb.Append("in ");
        if (param.IsParams) sb.Append("params ");
        sb.Append(param.TypeName);
        sb.Append(' ');
        sb.Append(param.Name);
        if (param.IsOptional && param.DefaultValue != null)
        {
            sb.Append(" = ");
            sb.Append(param.DefaultValue);
        }
        return sb.ToString();
    }
}
