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
    [Description("Gets detailed information about all methods in a type, including signatures, parameters, overloads, and modifiers")]
    public static string GetTypeMethods(
        IMemberAnalysisService memberAnalysisService,
        ToolMiddleware middleware,
        RuntimeOptions runtimeOptions,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name to analyze. Prefer full name (e.g., 'System.String'); simple names are also accepted")]
        string typeName,
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

                var defaultPageSize = runtimeOptions.DefaultMaxItems > 0 ? runtimeOptions.DefaultMaxItems : 200;
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
                    methods = pageItems.Select(m => new
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
                    }).ToArray()
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
    [Description("Gets custom attributes for a member (method, property, field, event, constructor)")]
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
                "method" => type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static).FirstOrDefault(m => string.Equals(m.Name, memberName, comparison)),
                "property" => type.GetProperties(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static).FirstOrDefault(p => string.Equals(p.Name, memberName, comparison)),
                "field" => type.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static).FirstOrDefault(f => string.Equals(f.Name, memberName, comparison)),
                "event" => type.GetEvents(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static).FirstOrDefault(e => string.Equals(e.Name, memberName, comparison)),
                "constructor" => type.GetConstructors(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static).FirstOrDefault() as MemberInfo,
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
    [Description("Gets custom attributes for a parameter of a method or constructor")]
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
            var method = type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static)
                             .FirstOrDefault(m => string.Equals(m.Name, methodName, comparison))
                        ?? (MethodBase?) type.GetConstructors(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static)
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
    [Description("Gets detailed information about all properties in a type, including getters, setters, indexers, and access modifiers")]
    public static string GetTypeProperties(
        IMemberAnalysisService memberAnalysisService,
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
        [Description("Sort order: asc|desc (default: asc)")] string sortOrder = "asc")
    {
        try
        {
            if (!File.Exists(assemblyPath))
                return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");
    
            var options = new MemberFilterOptions
            {
                IncludePublic = includePublic,
                IncludeNonPublic = includeNonPublic,
                IncludeStatic = includeStatic,
                IncludeInstance = includeInstance,
                CaseSensitive = caseSensitive,
                NameContains = nameContains,
                HasAttributeContains = hasAttributeContains,
                Skip = skip,
                Take = take,
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

            var properties = memberAnalysisService.GetProperties(assemblyPath, type.FullName, options);
    
            var result = new
            {
                typeName,
                assemblyPath,
                propertyCount = properties.Length,
                properties = properties.Select(p => new
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
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to analyze properties: {ex.Message}");
        }
    }
    
    [McpServerTool]
    [Description("Gets detailed information about all fields in a type, including const, readonly, static, and volatile fields")]
    public static string GetTypeFields(
        IMemberAnalysisService memberAnalysisService,
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
        [Description("Sort order: asc|desc (default: asc)")] string sortOrder = "asc")
    {
        try
        {
            if (!File.Exists(assemblyPath))
                return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");
    
            var options = new MemberFilterOptions
            {
                IncludePublic = includePublic,
                IncludeNonPublic = includeNonPublic,
                IncludeStatic = includeStatic,
                IncludeInstance = includeInstance,
                CaseSensitive = caseSensitive,
                NameContains = nameContains,
                HasAttributeContains = hasAttributeContains,
                Skip = skip,
                Take = take,
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

            var fields = memberAnalysisService.GetFields(assemblyPath, type.FullName, options);
    
            var result = new
            {
                typeName,
                assemblyPath,
                fieldCount = fields.Length,
                fields = fields.Select(f => new
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
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to analyze fields: {ex.Message}");
        }
    }
    
    [McpServerTool]
    [Description("Gets detailed information about all events in a type, including handler types and access modifiers")]
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

                var defaultPageSize = runtimeOptions.DefaultMaxItems > 0 ? runtimeOptions.DefaultMaxItems : 200;
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
    [Description("Gets detailed information about all constructors in a type, including parameters and access modifiers")]
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

                var defaultPageSize = runtimeOptions.DefaultMaxItems > 0 ? runtimeOptions.DefaultMaxItems : 200;
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
    [Description("Gets comprehensive member information for a type, including all methods, properties, fields, events, and constructors")]
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
                return JsonSerializer.Serialize(new { error = $"Assembly file not found: {assemblyPath}" });

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
                    return JsonSerializer.Serialize(new { error = $"Type '{typeName}' not found in assembly" });

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
                return JsonHelpers.Envelope("member.all", result);
            }, noCache);
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to analyze all members: {ex.Message}");
        }
    }
}
