using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.MemberAnalysis;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Shared;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class MemberAnalysisTools
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    [McpServerTool]
    [Description("Gets methods from a type with filtering and pagination. Large types may have 100+ methods - use nameContains filter or maxItems=25 for efficiency. Prefer over GetAllTypeMembers when only methods needed.")]
    public static string GetTypeMethods(
        IMemberAnalysisService memberAnalysisService,
        ToolMiddleware middleware,
        RuntimeOptions runtimeOptions,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name to analyze. Prefer full name (e.g., 'System.String'); simple names are also accepted")] string typeName,
        [Description("Include public members (default: true)")] bool includePublic = true,
        [Description("Include non-public members (default: false)")] bool includeNonPublic = false,
        [Description("Include static members (default: true)")] bool includeStatic = true,
        [Description("Include instance members (default: true)")] bool includeInstance = true,
        [Description("Case sensitive type/member matching (default: false)")] bool caseSensitive = false,
        [Description("Filter by name contains (optional)")] string? nameContains = null,
        [Description("Filter by attribute type contains (optional)")] string? hasAttributeContains = null,
        [Description("Items to skip (paging)")] int? skip = null,
        [Description("Items to take (paging)")] int? take = null,
        [Description("Sort by: name|access (default: name)")] string sortBy = "name",
        [Description("Sort order: asc|desc (default: asc)")] string sortOrder = "asc",
        [Description("Maximum items to return (overrides take)")] int? maxItems = null,
        [Description("Continuation token for paging")] string? continuationToken = null,
        [Description("Bypass cache for this request")] bool noCache = false)
    {
        try
        {
            if (!File.Exists(assemblyPath))
                return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");

            var cacheKey = CacheKeyHelper.Build(
                "member.methods",
                assemblyPath, typeName, includePublic, includeNonPublic, includeStatic, includeInstance,
                caseSensitive, nameContains, hasAttributeContains, sortBy, sortOrder, maxItems, continuationToken, skip, take);

            return middleware.Execute(cacheKey, () =>
            {
                var options = new MemberFilterOptions
                {
                    IncludePublic = includePublic,
                    IncludeNonPublic = includeNonPublic,
                    IncludeStatic = includeStatic,
                    IncludeInstance = includeInstance,
                    CaseSensitive = caseSensitive,
                    NameContains = nameContains,
                    HasAttributeContains = hasAttributeContains,
                    // paging controlled below
                    SortBy = sortBy,
                    SortOrder = sortOrder
                };

                // Resolve type
                var assembly = Assembly.LoadFrom(assemblyPath);
                var type = assembly.GetType(typeName)
                    ?? assembly.GetExportedTypes()
                        .FirstOrDefault(t => string.Equals(t.FullName, typeName, StringComparison.Ordinal)
                                           || string.Equals(t.Name, typeName, StringComparison.Ordinal));
                if (type?.FullName == null)
                    return JsonHelpers.Error("TypeNotFound", $"Type '{typeName}' not found in assembly");

                // Get full filtered set (no paging yet) to compute total and page safely
                var all = memberAnalysisService.GetMethods(assemblyPath, type.FullName, options);

                var defaultPageSize = runtimeOptions.GetMaxItemsForTool("GetTypeMethods");
                var pageSize = Math.Max(1, maxItems ?? take ?? defaultPageSize);
                var offset = 0;
                string salt = TokenHelper.MakeSalt(cacheKey);
                if (!string.IsNullOrWhiteSpace(continuationToken))
                {
                    if (!TokenHelper.TryParse(continuationToken!, out offset, out var parsedSalt) || parsedSalt != salt)
                    {
                        return JsonHelpers.Error("InvalidContinuationToken", "The continuation token is invalid or expired.");
                    }
                }
                else if (skip.HasValue && skip.Value > 0)
                {
                    offset = skip.Value;
                }

                var pageItems = all.Skip(offset).Take(pageSize).ToArray();
                string? nextToken = null;
                var nextOffset = offset + pageItems.Length;
                if (nextOffset < all.Length)
                {
                    nextToken = TokenHelper.Make(nextOffset, salt);
                }

                var methods = pageItems.Select(m => new
                {
                    name = m.Name,
                    signature = m.Signature,
                    returnType = m.ReturnTypeName,
                    accessModifier = m.AccessModifier,
                    isStatic = m.IsStatic,
                    isVirtual = m.IsVirtual,
                    isAbstract = m.IsAbstract,
                    isSealed = m.IsSealed,
                    isOverride = m.IsOverride,
                    isOperator = m.IsOperator,
                    isExtensionMethod = m.IsExtensionMethod,
                    genericTypeParameters = m.GenericTypeParameters,
                    attributes = m.CustomAttributes,
                    parameters = m.Parameters.Select(p => new
                    {
                        name = p.Name,
                        typeName = p.TypeName,
                        defaultValue = p.DefaultValue,
                        isOptional = p.IsOptional,
                        isOut = p.IsOut,
                        isRef = p.IsRef,
                        isIn = p.IsIn,
                        isParams = p.IsParams,
                        attributes = p.CustomAttributes
                    }).ToArray()
                }).ToArray();

                var methodsJson = JsonSerializer.Serialize(methods, SerializerOptions);
                var result = new
                {
                    typeName,
                    assemblyPath,
                    total = all.Length,
                    count = pageItems.Length,
                    nextToken,
                    pagination = PaginationMetadata.Create(all.Length, pageItems.Length, nextToken, methodsJson.Length),
                    methods
                };

                return JsonHelpers.Envelope("member.methods", result);
            }, noCache);
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to analyze methods: {ex.Message}");
        }
    }



    [McpServerTool]
    [Description("Gets custom attributes for a specific member (method, property, field, event, constructor). Returns attribute types and values. Use after identifying the member via GetTypeMethods or similar tools.")]
    public static string GetMemberAttributes(
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name. Prefer full name")]
        string typeName,
        [Description("Member kind: method|property|field|event|constructor")] string memberKind,
        [Description("Member name (for methods, the simple name; first match used)")] string memberName,
        [Description("Case sensitive matching (default: false)")] bool caseSensitive = false)
    {
        try
        {
            if (!File.Exists(assemblyPath))
                return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");
            var asm = Assembly.LoadFrom(assemblyPath);
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var type = asm.GetType(typeName, false, !caseSensitive)
                       ?? asm.GetTypes().FirstOrDefault(t => string.Equals(t.FullName, typeName, comparison) || string.Equals(t.Name, typeName, comparison));
            if (type == null)
                return JsonHelpers.Error("TypeNotFound", $"Type '{typeName}' not found in assembly");
            MemberInfo? member = memberKind.ToLowerInvariant() switch
            {
                "method" => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).FirstOrDefault(m => string.Equals(m.Name, memberName, comparison)),
                "property" => type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).FirstOrDefault(p => string.Equals(p.Name, memberName, comparison)),
                "field" => type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).FirstOrDefault(f => string.Equals(f.Name, memberName, comparison)),
                "event" => type.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).FirstOrDefault(e => string.Equals(e.Name, memberName, comparison)),
                "constructor" => type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).FirstOrDefault() as MemberInfo,
                _ => null
            };
            if (member == null)
                return JsonHelpers.Error("MemberNotFound", $"Member '{memberName}' of kind '{memberKind}' not found");
            var attrs = Sherlock.MCP.Runtime.AttributeUtils.FromMember(member);
            return JsonHelpers.Envelope(
                "member.attributes",
                new { assemblyPath, typeName, memberKind, memberName, attributeCount = attrs.Length, attributes = attrs }
            );
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to get member attributes: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Gets custom attributes for a specific parameter of a method or constructor. Use when you need to inspect parameter-level attributes like [FromBody], [Required], etc.")]
    public static string GetParameterAttributes(
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name. Prefer full name")] string typeName,
        [Description("Method or constructor name")] string methodName,
        [Description("Parameter index (0-based)")] int parameterIndex,
        [Description("Case sensitive matching (default: false)")] bool caseSensitive = false)
    {
        try
        {
            if (!File.Exists(assemblyPath))
                return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");
            var asm = Assembly.LoadFrom(assemblyPath);
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var type = asm.GetType(typeName, false, !caseSensitive)
                       ?? asm.GetTypes().FirstOrDefault(t => string.Equals(t.FullName, typeName, comparison) || string.Equals(t.Name, typeName, comparison));
            if (type == null)
                return JsonHelpers.Error("TypeNotFound", $"Type '{typeName}' not found in assembly");
            var method = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                             .FirstOrDefault(m => string.Equals(m.Name, methodName, comparison))
                        ?? (MethodBase?)type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                             .FirstOrDefault();
            if (method == null)
                return JsonHelpers.Error("MemberNotFound", $"Method/Constructor '{methodName}' not found");
            var parameters = method.GetParameters();
            if (parameterIndex < 0 || parameterIndex >= parameters.Length)
                return JsonHelpers.Error("InvalidArgument", "Parameter index out of range");
            var param = parameters[parameterIndex];
            var attrs = Sherlock.MCP.Runtime.AttributeUtils.FromParameter(param);
            return JsonHelpers.Envelope(
                "parameter.attributes",
                new { assemblyPath, typeName, methodName, parameterIndex, attributeCount = attrs.Length, attributes = attrs }
            );
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to get parameter attributes: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Gets properties from a type with filtering and pagination. Returns getter/setter info, indexers, and access modifiers. Prefer over GetAllTypeMembers when only properties needed.")]
    public static string GetTypeProperties(
        IMemberAnalysisService memberAnalysisService,
        ToolMiddleware middleware,
        RuntimeOptions runtimeOptions,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name to analyze. Prefer full name (e.g., 'System.String'); simple names are also accepted")] string typeName,
        [Description("Include public members (default: true)")] bool includePublic = true,
        [Description("Include non-public members (default: false)")] bool includeNonPublic = false,
        [Description("Include static members (default: true)")] bool includeStatic = true,
        [Description("Include instance members (default: true)")] bool includeInstance = true,
        [Description("Case sensitive type/member matching (default: false)")] bool caseSensitive = false,
        [Description("Filter by name contains (optional)")] string? nameContains = null,
        [Description("Filter by attribute type contains (optional)")] string? hasAttributeContains = null,
        [Description("Items to skip (paging)")] int? skip = null,
        [Description("Items to take (paging)")] int? take = null,
        [Description("Sort by: name|access (default: name)")] string sortBy = "name",
        [Description("Sort order: asc|desc (default: asc)")] string sortOrder = "asc",
        [Description("Maximum items to return (overrides take)")] int? maxItems = null,
        [Description("Continuation token for paging")] string? continuationToken = null,
        [Description("Bypass cache for this request")] bool noCache = false)
    {
        try
        {
            if (!File.Exists(assemblyPath))
                return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");

            var cacheKey = CacheKeyHelper.Build(
                "member.properties",
                assemblyPath, typeName, includePublic, includeNonPublic, includeStatic, includeInstance,
                caseSensitive, nameContains, hasAttributeContains, sortBy, sortOrder, maxItems, continuationToken, skip, take);

            return middleware.Execute(cacheKey, () =>
            {
                var options = new MemberFilterOptions
                {
                    IncludePublic = includePublic,
                    IncludeNonPublic = includeNonPublic,
                    IncludeStatic = includeStatic,
                    IncludeInstance = includeInstance,
                    CaseSensitive = caseSensitive,
                    NameContains = nameContains,
                    HasAttributeContains = hasAttributeContains,
                    // paging controlled below
                    SortBy = sortBy,
                    SortOrder = sortOrder
                };

                // Resolve type
                var assembly = Assembly.LoadFrom(assemblyPath);
                var type = assembly.GetType(typeName)
                    ?? assembly.GetExportedTypes()
                        .FirstOrDefault(t => string.Equals(t.FullName, typeName, StringComparison.Ordinal)
                                           || string.Equals(t.Name, typeName, StringComparison.Ordinal));
                if (type?.FullName == null)
                    return JsonHelpers.Error("TypeNotFound", $"Type '{typeName}' not found in assembly");

                // Get full filtered set (no paging yet) to compute total and page safely
                var all = memberAnalysisService.GetProperties(assemblyPath, type.FullName, options);

                var defaultPageSize = runtimeOptions.GetMaxItemsForTool("GetTypeProperties");
                var pageSize = Math.Max(1, maxItems ?? take ?? defaultPageSize);
                var offset = 0;
                string salt = TokenHelper.MakeSalt(cacheKey);
                if (!string.IsNullOrWhiteSpace(continuationToken))
                {
                    if (!TokenHelper.TryParse(continuationToken!, out offset, out var parsedSalt) || parsedSalt != salt)
                    {
                        return JsonHelpers.Error("InvalidContinuationToken", "The continuation token is invalid or expired.");
                    }
                }
                else if (skip.HasValue && skip.Value > 0)
                {
                    offset = skip.Value;
                }

                var pageItems = all.Skip(offset).Take(pageSize).ToArray();
                string? nextToken = null;
                var nextOffset = offset + pageItems.Length;
                if (nextOffset < all.Length)
                {
                    nextToken = TokenHelper.Make(nextOffset, salt);
                }

                var result = new
                {
                    typeName,
                    assemblyPath,
                    total = all.Length,
                    count = pageItems.Length,
                    nextToken,
                    properties = pageItems.Select(p => new
                    {
                        name = p.Name,
                        signature = p.Signature,
                        typeName = p.TypeName,
                        accessModifier = p.AccessModifier,
                        isStatic = p.IsStatic,
                        isVirtual = p.IsVirtual,
                        isAbstract = p.IsAbstract,
                        isSealed = p.IsSealed,
                        isOverride = p.IsOverride,
                        canRead = p.CanRead,
                        canWrite = p.CanWrite,
                        isIndexer = p.IsIndexer,
                        getterAccessModifier = p.GetterAccessModifier,
                        setterAccessModifier = p.SetterAccessModifier,
                        attributes = p.CustomAttributes,
                        indexerParameters = p.IndexerParameters.Select(param => new
                        {
                            name = param.Name,
                            typeName = param.TypeName,
                            isOptional = param.IsOptional,
                            defaultValue = param.DefaultValue,
                            attributes = param.CustomAttributes
                        }).ToArray()
                    }).ToArray()
                };

                return JsonHelpers.Envelope("member.properties", result);
            }, noCache);
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to analyze properties: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Gets fields from a type with filtering and pagination. Returns const/readonly/volatile info and constant values. Fields are compact - can use larger maxItems (75+).")]
    public static string GetTypeFields(
        IMemberAnalysisService memberAnalysisService,
        ToolMiddleware middleware,
        RuntimeOptions runtimeOptions,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name to analyze. Prefer full name (e.g., 'System.String'); simple names are also accepted")] string typeName,
        [Description("Include public members (default: true)")] bool includePublic = true,
        [Description("Include non-public members (default: false)")] bool includeNonPublic = false,
        [Description("Include static members (default: true)")] bool includeStatic = true,
        [Description("Include instance members (default: true)")] bool includeInstance = true,
        [Description("Case sensitive type/member matching (default: false)")] bool caseSensitive = false,
        [Description("Filter by name contains (optional)")] string? nameContains = null,
        [Description("Filter by attribute type contains (optional)")] string? hasAttributeContains = null,
        [Description("Items to skip (paging)")] int? skip = null,
        [Description("Items to take (paging)")] int? take = null,
        [Description("Sort by: name|access (default: name)")] string sortBy = "name",
        [Description("Sort order: asc|desc (default: asc)")] string sortOrder = "asc",
        [Description("Maximum items to return (overrides take)")] int? maxItems = null,
        [Description("Continuation token for paging")] string? continuationToken = null,
        [Description("Bypass cache for this request")] bool noCache = false)
    {
        try
        {
            if (!File.Exists(assemblyPath))
                return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");

            var cacheKey = CacheKeyHelper.Build(
                "member.fields",
                assemblyPath, typeName, includePublic, includeNonPublic, includeStatic, includeInstance,
                caseSensitive, nameContains, hasAttributeContains, sortBy, sortOrder, maxItems, continuationToken, skip, take);

            return middleware.Execute(cacheKey, () =>
            {
                var options = new MemberFilterOptions
                {
                    IncludePublic = includePublic,
                    IncludeNonPublic = includeNonPublic,
                    IncludeStatic = includeStatic,
                    IncludeInstance = includeInstance,
                    CaseSensitive = caseSensitive,
                    NameContains = nameContains,
                    HasAttributeContains = hasAttributeContains,
                    // paging controlled below
                    SortBy = sortBy,
                    SortOrder = sortOrder
                };

                // Resolve type
                var assembly = Assembly.LoadFrom(assemblyPath);
                var type = assembly.GetType(typeName)
                    ?? assembly.GetExportedTypes()
                        .FirstOrDefault(t => string.Equals(t.FullName, typeName, StringComparison.Ordinal)
                                           || string.Equals(t.Name, typeName, StringComparison.Ordinal));
                if (type?.FullName == null)
                    return JsonHelpers.Error("TypeNotFound", $"Type '{typeName}' not found in assembly");

                // Get full filtered set (no paging yet) to compute total and page safely
                var all = memberAnalysisService.GetFields(assemblyPath, type.FullName, options);

                var defaultPageSize = runtimeOptions.GetMaxItemsForTool("GetTypeFields");
                var pageSize = Math.Max(1, maxItems ?? take ?? defaultPageSize);
                var offset = 0;
                string salt = TokenHelper.MakeSalt(cacheKey);
                if (!string.IsNullOrWhiteSpace(continuationToken))
                {
                    if (!TokenHelper.TryParse(continuationToken!, out offset, out var parsedSalt) || parsedSalt != salt)
                    {
                        return JsonHelpers.Error("InvalidContinuationToken", "The continuation token is invalid or expired.");
                    }
                }
                else if (skip.HasValue && skip.Value > 0)
                {
                    offset = skip.Value;
                }

                var pageItems = all.Skip(offset).Take(pageSize).ToArray();
                string? nextToken = null;
                var nextOffset = offset + pageItems.Length;
                if (nextOffset < all.Length)
                {
                    nextToken = TokenHelper.Make(nextOffset, salt);
                }

                var result = new
                {
                    typeName,
                    assemblyPath,
                    total = all.Length,
                    count = pageItems.Length,
                    nextToken,
                    fields = pageItems.Select(f => new
                    {
                        name = f.Name,
                        signature = f.Signature,
                        typeName = f.TypeName,
                        accessModifier = f.AccessModifier,
                        isStatic = f.IsStatic,
                        isReadOnly = f.IsReadOnly,
                        isConst = f.IsConst,
                        isVolatile = f.IsVolatile,
                        isInitOnly = f.IsInitOnly,
                        constantValue = f.ConstantValue,
                        attributes = f.CustomAttributes
                    }).ToArray()
                };

                return JsonHelpers.Envelope("member.fields", result);
            }, noCache);
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to analyze fields: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Gets events from a type with filtering and pagination. Returns event handler types and add/remove accessor info. Most types have few events.")]
    public static string GetTypeEvents(
        IMemberAnalysisService memberAnalysisService,
        ToolMiddleware middleware,
        RuntimeOptions runtimeOptions,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name to analyze. Prefer full name (e.g., 'System.String'); simple names are also accepted")] string typeName,
        [Description("Include public members (default: true)")] bool includePublic = true,
        [Description("Include non-public members (default: false)")] bool includeNonPublic = false,
        [Description("Include static members (default: true)")] bool includeStatic = true,
        [Description("Include instance members (default: true)")] bool includeInstance = true,
        [Description("Case sensitive type/member matching (default: false)")] bool caseSensitive = false,
        [Description("Filter by name contains (optional)")] string? nameContains = null,
        [Description("Filter by attribute type contains (optional)")] string? hasAttributeContains = null,
        [Description("Items to skip (paging)")] int? skip = null,
        [Description("Items to take (paging)")] int? take = null,
        [Description("Sort by: name|access (default: name)")] string sortBy = "name",
        [Description("Sort order: asc|desc (default: asc)")] string sortOrder = "asc",
        [Description("Maximum items to return (overrides take)")] int? maxItems = null,
        [Description("Continuation token for paging")] string? continuationToken = null,
        [Description("Bypass cache for this request")] bool noCache = false)
    {
        try
        {
            if (!File.Exists(assemblyPath))
                return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");
            var cacheKey = CacheKeyHelper.Build(
                "member.events",
                assemblyPath, typeName, includePublic, includeNonPublic, includeStatic, includeInstance,
                caseSensitive, nameContains, hasAttributeContains, sortBy, sortOrder, maxItems, continuationToken, skip, take);

            return middleware.Execute(cacheKey, () =>
            {
                var options = new MemberFilterOptions
                {
                    IncludePublic = includePublic,
                    IncludeNonPublic = includeNonPublic,
                    IncludeStatic = includeStatic,
                    IncludeInstance = includeInstance,
                    CaseSensitive = caseSensitive,
                    NameContains = nameContains,
                    HasAttributeContains = hasAttributeContains,
                    SortBy = sortBy,
                    SortOrder = sortOrder
                };

                var assembly = Assembly.LoadFrom(assemblyPath);
                var type = assembly.GetType(typeName)
                    ?? assembly.GetExportedTypes()
                        .FirstOrDefault(t => string.Equals(t.FullName, typeName, StringComparison.Ordinal)
                                           || string.Equals(t.Name, typeName, StringComparison.Ordinal));
                if (type?.FullName == null)
                    return JsonHelpers.Error("TypeNotFound", $"Type '{typeName}' not found in assembly");

                var all = memberAnalysisService.GetEvents(assemblyPath, type.FullName, options);

                var defaultPageSize = runtimeOptions.GetMaxItemsForTool("GetTypeEvents");
                var pageSize = Math.Max(1, maxItems ?? take ?? defaultPageSize);
                var offset = 0;
                string salt = TokenHelper.MakeSalt(cacheKey);
                if (!string.IsNullOrWhiteSpace(continuationToken))
                {
                    if (!TokenHelper.TryParse(continuationToken!, out offset, out var parsedSalt) || parsedSalt != salt)
                    {
                        return JsonHelpers.Error("InvalidContinuationToken", "The continuation token is invalid or expired.");
                    }
                }
                else if (skip.HasValue && skip.Value > 0)
                {
                    offset = skip.Value;
                }

                var pageItems = all.Skip(offset).Take(pageSize).ToArray();
                string? nextToken = null;
                var nextOffset = offset + pageItems.Length;
                if (nextOffset < all.Length)
                {
                    nextToken = TokenHelper.Make(nextOffset, salt);
                }

                var result = new
                {
                    typeName,
                    assemblyPath,
                    total = all.Length,
                    count = pageItems.Length,
                    nextToken,
                    events = pageItems.Select(e => new
                    {
                        name = e.Name,
                        signature = e.Signature,
                        eventHandlerTypeName = e.EventHandlerTypeName,
                        accessModifier = e.AccessModifier,
                        isStatic = e.IsStatic,
                        isVirtual = e.IsVirtual,
                        isAbstract = e.IsAbstract,
                        isSealed = e.IsSealed,
                        isOverride = e.IsOverride,
                        addMethodAccessModifier = e.AddMethodAccessModifier,
                        removeMethodAccessModifier = e.RemoveMethodAccessModifier,
                        attributes = e.CustomAttributes
                    }).ToArray()
                };

                return JsonHelpers.Envelope("member.events", result);
            }, noCache);
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to analyze events: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Gets constructors from a type with filtering and pagination. Returns parameter info and access modifiers. Most types have few constructors - use maxItems=30.")]
    public static string GetTypeConstructors(
        IMemberAnalysisService memberAnalysisService,
        ToolMiddleware middleware,
        RuntimeOptions runtimeOptions,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name to analyze. Prefer full name (e.g., 'System.String'); simple names are also accepted")] string typeName,
        [Description("Include public members (default: true)")] bool includePublic = true,
        [Description("Include non-public members (default: false)")] bool includeNonPublic = false,
        [Description("Include static members (default: true)")] bool includeStatic = true,
        [Description("Include instance members (default: true)")] bool includeInstance = true,
        [Description("Case sensitive type/member matching (default: false)")] bool caseSensitive = false,
        [Description("Filter by name contains (optional)")] string? nameContains = null,
        [Description("Filter by attribute type contains (optional)")] string? hasAttributeContains = null,
        [Description("Items to skip (paging)")] int? skip = null,
        [Description("Items to take (paging)")] int? take = null,
        [Description("Sort by: name|access (default: name)")] string sortBy = "name",
        [Description("Sort order: asc|desc (default: asc)")] string sortOrder = "asc",
        [Description("Maximum items to return (overrides take)")] int? maxItems = null,
        [Description("Continuation token for paging")] string? continuationToken = null,
        [Description("Bypass cache for this request")] bool noCache = false)
    {
        try
        {
            if (!File.Exists(assemblyPath))
                return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");

            var cacheKey = CacheKeyHelper.Build(
                "member.constructors",
                assemblyPath, typeName, includePublic, includeNonPublic, includeStatic, includeInstance,
                caseSensitive, nameContains, hasAttributeContains, sortBy, sortOrder, maxItems, continuationToken, skip, take);

            return middleware.Execute(cacheKey, () =>
            {
                var options = new MemberFilterOptions
                {
                    IncludePublic = includePublic,
                    IncludeNonPublic = includeNonPublic,
                    IncludeStatic = includeStatic,
                    IncludeInstance = includeInstance,
                    CaseSensitive = caseSensitive,
                    NameContains = nameContains,
                    HasAttributeContains = hasAttributeContains,
                    SortBy = sortBy,
                    SortOrder = sortOrder
                };

                var assembly = Assembly.LoadFrom(assemblyPath);
                var type = assembly.GetType(typeName)
                    ?? assembly.GetExportedTypes()
                        .FirstOrDefault(t => string.Equals(t.FullName, typeName, StringComparison.Ordinal)
                                           || string.Equals(t.Name, typeName, StringComparison.Ordinal));
                if (type?.FullName == null)
                    return JsonHelpers.Error("TypeNotFound", $"Type '{typeName}' not found in assembly");

                var all = memberAnalysisService.GetConstructors(assemblyPath, type.FullName, options);

                var defaultPageSize = runtimeOptions.GetMaxItemsForTool("GetTypeConstructors");
                var pageSize = Math.Max(1, maxItems ?? take ?? defaultPageSize);
                var offset = 0;
                string salt = TokenHelper.MakeSalt(cacheKey);
                if (!string.IsNullOrWhiteSpace(continuationToken))
                {
                    if (!TokenHelper.TryParse(continuationToken!, out offset, out var parsedSalt) || parsedSalt != salt)
                    {
                        return JsonHelpers.Error("InvalidContinuationToken", "The continuation token is invalid or expired.");
                    }
                }
                else if (skip.HasValue && skip.Value > 0)
                {
                    offset = skip.Value;
                }

                var pageItems = all.Skip(offset).Take(pageSize).ToArray();
                string? nextToken = null;
                var nextOffset = offset + pageItems.Length;
                if (nextOffset < all.Length)
                {
                    nextToken = TokenHelper.Make(nextOffset, salt);
                }

                var result = new
                {
                    typeName,
                    assemblyPath,
                    total = all.Length,
                    count = pageItems.Length,
                    nextToken,
                    constructors = pageItems.Select(c => new
                    {
                        signature = c.Signature,
                        accessModifier = c.AccessModifier,
                        isStatic = c.IsStatic,
                        parameters = c.Parameters.Select(p => new
                        {
                            name = p.Name,
                            typeName = p.TypeName,
                            defaultValue = p.DefaultValue,
                            isOptional = p.IsOptional,
                            isOut = p.IsOut,
                            isRef = p.IsRef,
                            isIn = p.IsIn,
                            isParams = p.IsParams
                        }).ToArray()
                    }).ToArray()
                };

                return JsonHelpers.Envelope("member.constructors", result);
            }, noCache);
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to analyze constructors: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Gets ALL members (methods, properties, fields, events, constructors) in one call. WARNING: Can produce very large responses for complex types. Consider using specific member tools (GetTypeMethods, GetTypeProperties) with filtering first for better efficiency.")]
    public static string GetAllTypeMembers(
        IMemberAnalysisService memberAnalysisService,
        ToolMiddleware middleware,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name to analyze. Prefer full name (e.g., 'System.String'); simple names are also accepted")] string typeName,
        [Description("Include public members (default: true)")] bool includePublic = true,
        [Description("Include non-public members (default: false)")] bool includeNonPublic = false,
        [Description("Include static members (default: true)")] bool includeStatic = true,
        [Description("Include instance members (default: true)")] bool includeInstance = true,
        [Description("Bypass cache for this request")] bool noCache = false)
    {
        try
        {
            if (!File.Exists(assemblyPath))
                return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");

            var cacheKey = CacheKeyHelper.Build(
                "member.all",
                assemblyPath, typeName, includePublic, includeNonPublic, includeStatic, includeInstance);

            return middleware.Execute(cacheKey, () =>
            {
                var options = new MemberFilterOptions
                {
                    IncludePublic = includePublic,
                    IncludeNonPublic = includeNonPublic,
                    IncludeStatic = includeStatic,
                    IncludeInstance = includeInstance
                };

                var assembly = Assembly.LoadFrom(assemblyPath);
                var type = assembly.GetType(typeName)
                    ?? assembly.GetExportedTypes()
                        .FirstOrDefault(t => string.Equals(t.FullName, typeName, StringComparison.Ordinal)
                                           || string.Equals(t.Name, typeName, StringComparison.Ordinal));
                if (type?.FullName == null)
                    return JsonHelpers.Error("TypeNotFound", $"Type '{typeName}' not found in assembly");

                var methods = memberAnalysisService.GetMethods(assemblyPath, type.FullName, options);
                var properties = memberAnalysisService.GetProperties(assemblyPath, type.FullName, options);
                var fields = memberAnalysisService.GetFields(assemblyPath, type.FullName, options);
                var events = memberAnalysisService.GetEvents(assemblyPath, type.FullName, options);
                var constructors = memberAnalysisService.GetConstructors(assemblyPath, type.FullName, options);

                var result = new
                {
                    typeName,
                    assemblyPath,
                    memberCounts = new
                    {
                        methods = methods.Length,
                        properties = properties.Length,
                        fields = fields.Length,
                        events = events.Length,
                        constructors = constructors.Length,
                        total = methods.Length + properties.Length + fields.Length + events.Length + constructors.Length
                    },
                    methods = methods.Select(m => new
                    {
                        name = m.Name,
                        signature = m.Signature,
                        accessModifier = m.AccessModifier,
                        isStatic = m.IsStatic,
                        returnType = m.ReturnTypeName,
                        parameterCount = m.Parameters.Length,
                        attributes = m.CustomAttributes
                    }).ToArray(),
                    properties = properties.Select(p => new
                    {
                        name = p.Name,
                        signature = p.Signature,
                        accessModifier = p.AccessModifier,
                        isStatic = p.IsStatic,
                        typeName = p.TypeName,
                        canRead = p.CanRead,
                        canWrite = p.CanWrite,
                        isIndexer = p.IsIndexer,
                        attributes = p.CustomAttributes
                    }).ToArray(),
                    fields = fields.Select(f => new
                    {
                        name = f.Name,
                        signature = f.Signature,
                        accessModifier = f.AccessModifier,
                        isStatic = f.IsStatic,
                        typeName = f.TypeName,
                        isConst = f.IsConst,
                        isReadOnly = f.IsReadOnly,
                        constantValue = f.ConstantValue,
                        attributes = f.CustomAttributes
                    }).ToArray(),
                    events = events.Select(e => new
                    {
                        name = e.Name,
                        signature = e.Signature,
                        accessModifier = e.AccessModifier,
                        isStatic = e.IsStatic,
                        eventHandlerTypeName = e.EventHandlerTypeName,
                        attributes = e.CustomAttributes
                    }).ToArray(),
                    constructors = constructors.Select(c => new
                    {
                        signature = c.Signature,
                        accessModifier = c.AccessModifier,
                        isStatic = c.IsStatic,
                        parameterCount = c.Parameters.Length,
                        attributes = c.CustomAttributes,
                        parameters = c.Parameters.Select(p => new { p.Name, p.TypeName, p.IsOptional, p.DefaultValue, attributes = p.CustomAttributes }).ToArray()
                    }).ToArray()
                };
                // Check response size before returning
                var sizeValidationError = ResponseSizeHelper.ValidateResponseSize(result, "GetAllTypeMembers");
                if (sizeValidationError != null)
                    return sizeValidationError;

                return JsonHelpers.Envelope("member.all", result);
            }, noCache);
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to analyze all members: {ex.Message}");
        }
    }
}
