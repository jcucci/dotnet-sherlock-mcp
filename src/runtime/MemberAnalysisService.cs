using System.Reflection;
using System.Text;
using Sherlock.MCP.Runtime.Contracts.MemberAnalysis;
using Sherlock.MCP.Runtime.Inspection;

namespace Sherlock.MCP.Runtime;

public class MemberAnalysisService : IMemberAnalysisService
{

    public MemberInfo[] GetAllMembers(string assemblyPath, string typeName, MemberFilterOptions? options = null)
    {
        using var ctx = InspectionContextFactory.Create(assemblyPath);
        var type = LoadTypeFromAssembly(ctx.Assembly, typeName, options);
        var bindingFlags = GetBindingFlags(options);

        return type.GetMembers(bindingFlags);
    }

    public MethodDetails[] GetMethods(string assemblyPath, string typeName, MemberFilterOptions? options = null)
    {
        using var ctx = InspectionContextFactory.Create(assemblyPath);
        var type = LoadTypeFromAssembly(ctx.Assembly, typeName, options);
        var bindingFlags = GetBindingFlags(options);
        var methods = type.GetMethods(bindingFlags);

        var methodDetails = new List<MethodDetails>();
        foreach (var method in methods)
        {
            if (method.IsSpecialName && (method.Name.StartsWith("get_") || method.Name.StartsWith("set_") || 
                method.Name.StartsWith("add_") || method.Name.StartsWith("remove_")))
            {
                continue;
            }

            var parameters = GetParameterDetails(method.GetParameters());
            var genericParams = method.IsGenericMethodDefinition ? 
                method.GetGenericArguments().Select(t => t.Name).ToArray() : 
                Array.Empty<string>();
            var isOperator = IsOperatorMethod(method);
            var isExtensionMethod = IsExtensionMethod(method);
            var signature = BuildMethodSignature(method, parameters, genericParams);
            var customAttributes = AttributeUtils.FromMember(method);

            methodDetails.Add(new MethodDetails(
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
            ));
        }

        return ApplyMemberFilters(methodDetails, options, m => m.Name, m => m.CustomAttributes).ToArray();
    }

    public PropertyDetails[] GetProperties(string assemblyPath, string typeName, MemberFilterOptions? options = null)
    {
        using var ctx = InspectionContextFactory.Create(assemblyPath);
        var type = LoadTypeFromAssembly(ctx.Assembly, typeName, options);
        var bindingFlags = GetBindingFlags(options);
        var properties = type.GetProperties(bindingFlags);

        var propertyDetails = new List<PropertyDetails>();
        foreach (var property in properties)
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

            propertyDetails.Add(new PropertyDetails(
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
            ));
        }

        return ApplyMemberFilters(propertyDetails, options, p => p.Name, p => p.CustomAttributes).ToArray();
    }

    public FieldDetails[] GetFields(string assemblyPath, string typeName, MemberFilterOptions? options = null)
    {
        using var ctx = InspectionContextFactory.Create(assemblyPath);
        var type = LoadTypeFromAssembly(ctx.Assembly, typeName, options);
        var bindingFlags = GetBindingFlags(options);
        var fields = type.GetFields(bindingFlags);

        var fieldDetails = new List<FieldDetails>();
        foreach (var field in fields)
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

            fieldDetails.Add(new FieldDetails(
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
            ));
        }

        return ApplyMemberFilters(fieldDetails, options, f => f.Name, f => f.CustomAttributes).ToArray();
    }

    public EventDetails[] GetEvents(string assemblyPath, string typeName, MemberFilterOptions? options = null)
    {
        using var ctx = InspectionContextFactory.Create(assemblyPath);
        var type = LoadTypeFromAssembly(ctx.Assembly, typeName, options);
        var bindingFlags = GetBindingFlags(options);
        var events = type.GetEvents(bindingFlags);

        var eventDetails = new List<EventDetails>();
        foreach (var eventInfo in events)
        {
            var addMethod = eventInfo.GetAddMethod(true);
            var removeMethod = eventInfo.GetRemoveMethod(true);
            var addMethodAccessModifier = addMethod != null ? GetAccessModifier(addMethod) : null;
            var removeMethodAccessModifier = removeMethod != null ? GetAccessModifier(removeMethod) : null;
            var primaryMethod = addMethod ?? removeMethod;
            var signature = BuildEventSignature(eventInfo);
            var customAttributes = AttributeUtils.FromMember(eventInfo);

            eventDetails.Add(new EventDetails(
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
            ));
        }

        return ApplyMemberFilters(eventDetails, options, e => e.Name, e => e.CustomAttributes).ToArray();
    }

    public ConstructorDetails[] GetConstructors(string assemblyPath, string typeName, MemberFilterOptions? options = null)
    {
        using var ctx = InspectionContextFactory.Create(assemblyPath);
        var type = LoadTypeFromAssembly(ctx.Assembly, typeName, options);
        var bindingFlags = GetBindingFlags(options);
        var constructors = type.GetConstructors(bindingFlags);

        var constructorDetails = new List<ConstructorDetails>();
        foreach (var constructor in constructors)
        {
            var parameters = GetParameterDetails(constructor.GetParameters());
            var signature = BuildConstructorSignature(constructor, parameters);
            var customAttributes = AttributeUtils.FromMember(constructor);

            constructorDetails.Add(new ConstructorDetails(
                Parameters: parameters,
                Attributes: constructor.Attributes,
                AccessModifier: GetAccessModifier(constructor),
                IsStatic: constructor.IsStatic,
                CustomAttributes: customAttributes,
                Signature: signature
            ));
        }

        return ApplyMemberFilters(constructorDetails, options, c => c.Signature, c => c.CustomAttributes).ToArray();
    }

    private static Type LoadTypeFromAssembly(Assembly assembly, string typeName, MemberFilterOptions? options)
    {
        var caseSensitive = options?.CaseSensitive ?? true;

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var type = assembly.GetType(typeName, false, !caseSensitive);
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
            DefaultValue: p.HasDefaultValue ? p.DefaultValue?.ToString() : null,
            IsOptional: p.IsOptional,
            IsOut: p.IsOut,
            IsRef: p.ParameterType.IsByRef && !p.IsOut,
            IsIn: p.IsIn,
            IsParams: p.GetCustomAttribute<ParamArrayAttribute>() != null,
            Attributes: p.Attributes,
            CustomAttributes: AttributeUtils.FromParameter(p)
        )).ToArray();
    }

    private static IEnumerable<T> ApplyMemberFilters<T>(IEnumerable<T> source, MemberFilterOptions? options, Func<T, string> nameSelector, Func<T, Sherlock.MCP.Runtime.Contracts.TypeAnalysis.AttributeInfo[]> attrSelector)
    {
        options ??= new MemberFilterOptions();
        var comparison = options.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var query = source;

        if (!string.IsNullOrEmpty(options.NameContains))
        {
            query = query.Where(x => nameSelector(x).IndexOf(options.NameContains!, comparison) >= 0);
        }
        if (!string.IsNullOrEmpty(options.HasAttributeContains))
        {
            query = query.Where(x => attrSelector(x).Any(a => (a.AttributeType?.IndexOf(options.HasAttributeContains!, comparison) ?? -1) >= 0));
        }

        query = options.SortBy.ToLowerInvariant() switch
        {
            _ => (options.SortOrder.Equals("desc", StringComparison.OrdinalIgnoreCase)
                ? query.OrderByDescending(x => nameSelector(x))
                : query.OrderBy(x => nameSelector(x)))
        };

        if (options.Skip.HasValue) query = query.Skip(Math.Max(0, options.Skip.Value));
        if (options.Take.HasValue) query = query.Take(Math.Max(0, options.Take.Value));

        return query;
    }

    private static string GetFriendlyTypeName(Type type)
    {
        if (type.IsByRef)
        {
            return GetFriendlyTypeName(type.GetElementType()!) + "&";
        }
        if (type.IsPointer)
        {
            return GetFriendlyTypeName(type.GetElementType()!) + "*";
        }
        if (type.IsArray)
        {
            var elementType = GetFriendlyTypeName(type.GetElementType()!);
            var rank = type.GetArrayRank();
            var brackets = rank == 1 ? "[]" : "[" + new string(',', rank - 1) + "]";
            return elementType + brackets;
        }
        if (type.IsGenericType)
        {
            var genericTypeDef = type.GetGenericTypeDefinition();
            var friendlyName = genericTypeDef.Name;
            var backtickIndex = friendlyName.IndexOf('`');
            if (backtickIndex > 0)
            {
                friendlyName = friendlyName.Substring(0, backtickIndex);
            }
            var genericArgs = type.GetGenericArguments().Select(GetFriendlyTypeName);
            return $"{friendlyName}<{string.Join(", ", genericArgs)}>";
        }
        var builtInTypes = new Dictionary<Type, string>
        {
            { typeof(bool), "bool" },
            { typeof(byte), "byte" },
            { typeof(sbyte), "sbyte" },
            { typeof(char), "char" },
            { typeof(decimal), "decimal" },
            { typeof(double), "double" },
            { typeof(float), "float" },
            { typeof(int), "int" },
            { typeof(uint), "uint" },
            { typeof(long), "long" },
            { typeof(ulong), "ulong" },
            { typeof(short), "short" },
            { typeof(ushort), "ushort" },
            { typeof(object), "object" },
            { typeof(string), "string" },
            { typeof(void), "void" }
        };
        return builtInTypes.TryGetValue(type, out var builtInName) ? builtInName : type.Name;
    }
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
               (method.Name.StartsWith("op_") || method.Name == "True" || method.Name == "False");
    }
    private static bool IsExtensionMethod(MethodInfo method)
    {
        return method.IsStatic && 
               method.GetCustomAttribute<System.Runtime.CompilerServices.ExtensionAttribute>() != null;
    }
    private static bool IsOverrideMethod(MethodBase method)
    {
        if (method is MethodInfo methodInfo)
        {
            return methodInfo.GetBaseDefinition() != methodInfo;
        }
        return false;
    }
    private static bool IsVolatileField(FieldInfo field)
    {
        var requiredModifiers = field.GetRequiredCustomModifiers();
        return requiredModifiers.Any(m => m == typeof(System.Runtime.CompilerServices.IsVolatile));
    }
    private static string BuildMethodSignature(MethodInfo method, ParameterDetails[] parameters, string[] genericParams)
    {
        var sb = new StringBuilder();
        sb.Append(GetAccessModifier(method));
        sb.Append(' ');
        if (method.IsStatic) sb.Append("static ");
        if (method.IsAbstract) sb.Append("abstract ");
        else if (method.IsVirtual && !method.IsFinal && !IsOverrideMethod(method)) sb.Append("virtual ");
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
        var getMethod = property.GetGetMethod(true);
        var setMethod = property.GetSetMethod(true);
        var primaryMethod = getMethod ?? setMethod;
        if (primaryMethod != null)
        {
            sb.Append(GetAccessModifier(primaryMethod));
            sb.Append(' ');
            if (primaryMethod.IsStatic) sb.Append("static ");
            if (primaryMethod.IsAbstract) sb.Append("abstract ");
            else if (primaryMethod.IsVirtual && !primaryMethod.IsFinal && !IsOverrideMethod(primaryMethod)) sb.Append("virtual ");
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
        var addMethod = eventInfo.GetAddMethod(true);
        var primaryMethod = addMethod;
        if (primaryMethod != null)
        {
            sb.Append(GetAccessModifier(primaryMethod));
            sb.Append(' ');
            if (primaryMethod.IsStatic) sb.Append("static ");
            if (primaryMethod.IsAbstract) sb.Append("abstract ");
            else if (primaryMethod.IsVirtual && !primaryMethod.IsFinal && !IsOverrideMethod(primaryMethod)) sb.Append("virtual ");
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
