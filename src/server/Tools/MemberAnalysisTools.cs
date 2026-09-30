using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.MemberAnalysis;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Schemas;
using Sherlock.MCP.Server.Shared;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class MemberAnalysisTools
{
    [McpServerTool(Title = "Get Type Methods", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolEnvelope<TypeMethodsData>))]
    [Description("Gets methods from a type with filtering and pagination. Returns a lean summary ({ name, signature }) by default - the signature already encodes return type, parameters, and modifiers in C# form. Pass projection='full' when you need structured fields (parameters[], attributes, returnType, isStatic/Virtual/Abstract/..., genericTypeParameters); prefer analyze_method for one method. Large types may have 100+ methods - use nameContains filter or maxItems=25 for efficiency.")]
    public static CallToolResult GetTypeMethods(
        IMemberAnalysisService memberAnalysisService,
        IInspectionContextProvider contexts,
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
        [Description("Bypass cache for this request")] bool noCache = false,
        [Description("Response shape. 'summary' (default, token-lean): { name, signature } only - the C# signature already carries return type, parameters, and modifiers. 'full': adds parameters[], attributes, returnType, and all modifier booleans - use only when you need structured access to those fields.")] string projection = "summary",
        RequestContext<CallToolRequestParams>? context = null)
    {
        var elicitation = ElicitationContext.From(context);
        typeName = Elicitation.ApplyTypeChoice(elicitation, typeName);
        try
        {
            if (!File.Exists(assemblyPath))
                return ToolResponse.Result(ToolErrors.AssemblyNotFound(assemblyPath));

            var normalizedProjection = (projection ?? "summary").Trim().ToLowerInvariant();
            if (normalizedProjection != "summary" && normalizedProjection != "full")
                return ToolResponse.Result(JsonHelpers.Error("InvalidProjection", "projection must be 'summary' or 'full'"));

            // Salt seed: identifies the result set (filters + ordering). MUST exclude pagination
            // params (continuationToken, skip, take, maxItems) and rendering params (projection)
            // so a token minted on page 1 still validates on page 2.
            var assemblyStamp = CacheKeyHelper.FileStamp(assemblyPath);
            var saltSeed = CacheKeyHelper.Build(
                "member.methods.salt",
                assemblyStamp, typeName, includePublic, includeNonPublic, includeStatic, includeInstance,
                caseSensitive, nameContains, hasAttributeContains, sortBy, sortOrder);

            var cacheKey = CacheKeyHelper.Build(
                "member.methods",
                assemblyStamp, typeName, includePublic, includeNonPublic, includeStatic, includeInstance,
                caseSensitive, nameContains, hasAttributeContains, sortBy, sortOrder, maxItems, continuationToken, skip, take, normalizedProjection);

            return ToolResponse.Result(middleware.Execute(cacheKey, () =>
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

                var defaultPageSize = runtimeOptions.GetMaxItemsForTool("get_type_methods");
                var pageSize = Math.Max(1, maxItems ?? take ?? defaultPageSize);
                var offset = 0;
                string salt = TokenHelper.MakeSalt(saltSeed);
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

                Sherlock.MCP.Runtime.Contracts.Common.PagedResult<MethodDetails> page;
                try
                {
                    page = memberAnalysisService.GetMethodsPage(assemblyPath, typeName, options, offset, pageSize);
                }
                catch (ArgumentException)
                {
                    return ToolErrors.TypeNotFound(contexts, assemblyPath, typeName);
                }

                var pageItems = page.Items;
                string? nextToken = null;
                var nextOffset = offset + pageItems.Length;
                if (nextOffset < page.Total)
                {
                    nextToken = TokenHelper.Make(nextOffset, salt);
                }

                object methods = normalizedProjection == "summary"
                    ? pageItems.Select(m => new
                    {
                        name = m.Name,
                        signature = m.Signature
                    }).ToArray()
                    : pageItems.Select(m => new
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

                var methodsJson = JsonSerializer.Serialize(methods, JsonHelpers.DefaultOptions);
                var result = new
                {
                    typeName,
                    assemblyPath,
                    projection = normalizedProjection,
                    total = page.Total,
                    count = pageItems.Length,
                    nextToken,
                    pagination = PaginationMetadata.Create(page.Total, pageItems.Length, nextToken, methodsJson.Length),
                    methods
                };

                return JsonHelpers.Envelope("member.methods", result);
            }, noCache));
        }
        catch (AmbiguousTypeNameException ex)
        {
            return ToolResponse.Result(Elicitation.AmbiguousType(elicitation, ex));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResponse.Result(ToolErrors.FromException(ex, "analyze methods"));
        }
    }



    [McpServerTool(Title = "Get Member Attributes", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets custom attributes for a specific member (method, property, field, event, constructor). Returns attribute types and values. Use after identifying the member via get_type_methods or similar tools.")]
    public static string GetMemberAttributes(
        IInspectionContextProvider contexts,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name. Prefer full name")]
        string typeName,
        [Description("Member kind: method|property|field|event|constructor")] string memberKind,
        [Description("Member name (for methods, the simple name; first match used)")] string memberName,
        [Description("Case sensitive matching (default: false)")] bool caseSensitive = false,
        RequestContext<CallToolRequestParams>? context = null)
    {
        var elicitation = ElicitationContext.From(context);
        typeName = Elicitation.ApplyTypeChoice(elicitation, typeName);
        try
        {
            if (!File.Exists(assemblyPath))
                return ToolErrors.AssemblyNotFound(assemblyPath);
            using var lease = contexts.Acquire(assemblyPath);
            var asm = lease.Assembly;
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var type = TypeNameResolver.Resolve(asm, typeName, comparison).OrThrowIfAmbiguous(typeName);
            if (type == null)
                return ToolErrors.TypeNotFound(lease.Context, typeName);
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
                return ToolErrors.MemberNotFound(type, memberName, memberKind, message: $"Member '{memberName}' of kind '{memberKind}' not found");
            var attrs = Sherlock.MCP.Runtime.AttributeUtils.FromMember(member);
            return JsonHelpers.Envelope(
                "member.attributes",
                new { assemblyPath, typeName, memberKind, memberName, attributeCount = attrs.Length, attributes = attrs }
            );
        }
        catch (AmbiguousTypeNameException ex)
        {
            return Elicitation.AmbiguousType(elicitation, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "get member attributes");
        }
    }

    [McpServerTool(Title = "Get Parameter Attributes", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets custom attributes for a specific parameter of a method or constructor. Use when you need to inspect parameter-level attributes like [FromBody], [Required], etc.")]
    public static string GetParameterAttributes(
        IInspectionContextProvider contexts,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name. Prefer full name")] string typeName,
        [Description("Method or constructor name")] string methodName,
        [Description("Parameter index (0-based)")] int parameterIndex,
        [Description("Case sensitive matching (default: false)")] bool caseSensitive = false,
        RequestContext<CallToolRequestParams>? context = null)
    {
        var elicitation = ElicitationContext.From(context);
        typeName = Elicitation.ApplyTypeChoice(elicitation, typeName);
        try
        {
            if (!File.Exists(assemblyPath))
                return ToolErrors.AssemblyNotFound(assemblyPath);
            using var lease = contexts.Acquire(assemblyPath);
            var asm = lease.Assembly;
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var type = TypeNameResolver.Resolve(asm, typeName, comparison).OrThrowIfAmbiguous(typeName);
            if (type == null)
                return ToolErrors.TypeNotFound(lease.Context, typeName);
            var method = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                             .FirstOrDefault(m => string.Equals(m.Name, methodName, comparison))
                        ?? (MethodBase?)type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                             .FirstOrDefault();
            if (method == null)
                return ToolErrors.MemberNotFound(type, methodName, "method", message: $"Method/Constructor '{methodName}' not found");
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
        catch (AmbiguousTypeNameException ex)
        {
            return Elicitation.AmbiguousType(elicitation, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "get parameter attributes");
        }
    }

    [McpServerTool(Title = "Get Type Properties", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets properties from a type with filtering and pagination. Returns getter/setter info, indexers, and access modifiers. Prefer over get_all_type_members when only properties needed.")]
    public static string GetTypeProperties(
        IMemberAnalysisService memberAnalysisService,
        IInspectionContextProvider contexts,
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
        [Description("Bypass cache for this request")] bool noCache = false,
        RequestContext<CallToolRequestParams>? context = null)
    {
        var elicitation = ElicitationContext.From(context);
        typeName = Elicitation.ApplyTypeChoice(elicitation, typeName);
        try
        {
            if (!File.Exists(assemblyPath))
                return ToolErrors.AssemblyNotFound(assemblyPath);

            var assemblyStamp = CacheKeyHelper.FileStamp(assemblyPath);
            var saltSeed = CacheKeyHelper.Build(
                "member.properties.salt",
                assemblyStamp, typeName, includePublic, includeNonPublic, includeStatic, includeInstance,
                caseSensitive, nameContains, hasAttributeContains, sortBy, sortOrder);

            var cacheKey = CacheKeyHelper.Build(
                "member.properties",
                assemblyStamp, typeName, includePublic, includeNonPublic, includeStatic, includeInstance,
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

                var defaultPageSize = runtimeOptions.GetMaxItemsForTool("get_type_properties");
                var pageSize = Math.Max(1, maxItems ?? take ?? defaultPageSize);
                var offset = 0;
                string salt = TokenHelper.MakeSalt(saltSeed);
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

                Sherlock.MCP.Runtime.Contracts.Common.PagedResult<PropertyDetails> page;
                try
                {
                    page = memberAnalysisService.GetPropertiesPage(assemblyPath, typeName, options, offset, pageSize);
                }
                catch (ArgumentException)
                {
                    return ToolErrors.TypeNotFound(contexts, assemblyPath, typeName);
                }

                var pageItems = page.Items;
                string? nextToken = null;
                var nextOffset = offset + pageItems.Length;
                if (nextOffset < page.Total)
                {
                    nextToken = TokenHelper.Make(nextOffset, salt);
                }

                var result = new
                {
                    typeName,
                    assemblyPath,
                    total = page.Total,
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
        catch (AmbiguousTypeNameException ex)
        {
            return Elicitation.AmbiguousType(elicitation, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "analyze properties");
        }
    }

    [McpServerTool(Title = "Get Type Fields", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets fields from a type with filtering and pagination. Returns const/readonly/volatile info and constant values. Fields are compact - can use larger maxItems (75+).")]
    public static string GetTypeFields(
        IMemberAnalysisService memberAnalysisService,
        IInspectionContextProvider contexts,
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
        [Description("Bypass cache for this request")] bool noCache = false,
        RequestContext<CallToolRequestParams>? context = null)
    {
        var elicitation = ElicitationContext.From(context);
        typeName = Elicitation.ApplyTypeChoice(elicitation, typeName);
        try
        {
            if (!File.Exists(assemblyPath))
                return ToolErrors.AssemblyNotFound(assemblyPath);

            var assemblyStamp = CacheKeyHelper.FileStamp(assemblyPath);
            var saltSeed = CacheKeyHelper.Build(
                "member.fields.salt",
                assemblyStamp, typeName, includePublic, includeNonPublic, includeStatic, includeInstance,
                caseSensitive, nameContains, hasAttributeContains, sortBy, sortOrder);

            var cacheKey = CacheKeyHelper.Build(
                "member.fields",
                assemblyStamp, typeName, includePublic, includeNonPublic, includeStatic, includeInstance,
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

                var defaultPageSize = runtimeOptions.GetMaxItemsForTool("get_type_fields");
                var pageSize = Math.Max(1, maxItems ?? take ?? defaultPageSize);
                var offset = 0;
                string salt = TokenHelper.MakeSalt(saltSeed);
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

                Sherlock.MCP.Runtime.Contracts.Common.PagedResult<FieldDetails> page;
                try
                {
                    page = memberAnalysisService.GetFieldsPage(assemblyPath, typeName, options, offset, pageSize);
                }
                catch (ArgumentException)
                {
                    return ToolErrors.TypeNotFound(contexts, assemblyPath, typeName);
                }

                var pageItems = page.Items;
                string? nextToken = null;
                var nextOffset = offset + pageItems.Length;
                if (nextOffset < page.Total)
                {
                    nextToken = TokenHelper.Make(nextOffset, salt);
                }

                var result = new
                {
                    typeName,
                    assemblyPath,
                    total = page.Total,
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
        catch (AmbiguousTypeNameException ex)
        {
            return Elicitation.AmbiguousType(elicitation, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "analyze fields");
        }
    }

    [McpServerTool(Title = "Get Type Events", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets events from a type with filtering and pagination. Returns event handler types and add/remove accessor info. Most types have few events.")]
    public static string GetTypeEvents(
        IMemberAnalysisService memberAnalysisService,
        IInspectionContextProvider contexts,
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
        [Description("Bypass cache for this request")] bool noCache = false,
        RequestContext<CallToolRequestParams>? context = null)
    {
        var elicitation = ElicitationContext.From(context);
        typeName = Elicitation.ApplyTypeChoice(elicitation, typeName);
        try
        {
            if (!File.Exists(assemblyPath))
                return ToolErrors.AssemblyNotFound(assemblyPath);
            var assemblyStamp = CacheKeyHelper.FileStamp(assemblyPath);
            var saltSeed = CacheKeyHelper.Build(
                "member.events.salt",
                assemblyStamp, typeName, includePublic, includeNonPublic, includeStatic, includeInstance,
                caseSensitive, nameContains, hasAttributeContains, sortBy, sortOrder);

            var cacheKey = CacheKeyHelper.Build(
                "member.events",
                assemblyStamp, typeName, includePublic, includeNonPublic, includeStatic, includeInstance,
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

                var defaultPageSize = runtimeOptions.GetMaxItemsForTool("get_type_events");
                var pageSize = Math.Max(1, maxItems ?? take ?? defaultPageSize);
                var offset = 0;
                string salt = TokenHelper.MakeSalt(saltSeed);
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

                Sherlock.MCP.Runtime.Contracts.Common.PagedResult<EventDetails> page;
                try
                {
                    page = memberAnalysisService.GetEventsPage(assemblyPath, typeName, options, offset, pageSize);
                }
                catch (ArgumentException)
                {
                    return ToolErrors.TypeNotFound(contexts, assemblyPath, typeName);
                }

                var pageItems = page.Items;
                string? nextToken = null;
                var nextOffset = offset + pageItems.Length;
                if (nextOffset < page.Total)
                {
                    nextToken = TokenHelper.Make(nextOffset, salt);
                }

                var result = new
                {
                    typeName,
                    assemblyPath,
                    total = page.Total,
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
        catch (AmbiguousTypeNameException ex)
        {
            return Elicitation.AmbiguousType(elicitation, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "analyze events");
        }
    }

    [McpServerTool(Title = "Get Type Constructors", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets constructors from a type with filtering and pagination. Returns parameter info and access modifiers. Most types have few constructors - use maxItems=30.")]
    public static string GetTypeConstructors(
        IMemberAnalysisService memberAnalysisService,
        IInspectionContextProvider contexts,
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
        [Description("Bypass cache for this request")] bool noCache = false,
        RequestContext<CallToolRequestParams>? context = null)
    {
        var elicitation = ElicitationContext.From(context);
        typeName = Elicitation.ApplyTypeChoice(elicitation, typeName);
        try
        {
            if (!File.Exists(assemblyPath))
                return ToolErrors.AssemblyNotFound(assemblyPath);

            var assemblyStamp = CacheKeyHelper.FileStamp(assemblyPath);
            var saltSeed = CacheKeyHelper.Build(
                "member.constructors.salt",
                assemblyStamp, typeName, includePublic, includeNonPublic, includeStatic, includeInstance,
                caseSensitive, nameContains, hasAttributeContains, sortBy, sortOrder);

            var cacheKey = CacheKeyHelper.Build(
                "member.constructors",
                assemblyStamp, typeName, includePublic, includeNonPublic, includeStatic, includeInstance,
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

                var defaultPageSize = runtimeOptions.GetMaxItemsForTool("get_type_constructors");
                var pageSize = Math.Max(1, maxItems ?? take ?? defaultPageSize);
                var offset = 0;
                string salt = TokenHelper.MakeSalt(saltSeed);
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

                Sherlock.MCP.Runtime.Contracts.Common.PagedResult<ConstructorDetails> page;
                try
                {
                    page = memberAnalysisService.GetConstructorsPage(assemblyPath, typeName, options, offset, pageSize);
                }
                catch (ArgumentException)
                {
                    return ToolErrors.TypeNotFound(contexts, assemblyPath, typeName);
                }

                var pageItems = page.Items;
                string? nextToken = null;
                var nextOffset = offset + pageItems.Length;
                if (nextOffset < page.Total)
                {
                    nextToken = TokenHelper.Make(nextOffset, salt);
                }

                var result = new
                {
                    typeName,
                    assemblyPath,
                    total = page.Total,
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
        catch (AmbiguousTypeNameException ex)
        {
            return Elicitation.AmbiguousType(elicitation, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "analyze constructors");
        }
    }

    [McpServerTool(Title = "Get All Type Members", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets ALL members (methods, properties, fields, events, constructors) in one call. WARNING: Can produce very large responses for complex types. Consider using specific member tools (get_type_methods, get_type_properties) with filtering first for better efficiency.")]
    public static string GetAllTypeMembers(
        IMemberAnalysisService memberAnalysisService,
        IInspectionContextProvider contexts,
        ToolMiddleware middleware,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name to analyze. Prefer full name (e.g., 'System.String'); simple names are also accepted")] string typeName,
        [Description("Include public members (default: true)")] bool includePublic = true,
        [Description("Include non-public members (default: false)")] bool includeNonPublic = false,
        [Description("Include static members (default: true)")] bool includeStatic = true,
        [Description("Include instance members (default: true)")] bool includeInstance = true,
        [Description("Bypass cache for this request")] bool noCache = false,
        RequestContext<CallToolRequestParams>? context = null)
    {
        var elicitation = ElicitationContext.From(context);
        typeName = Elicitation.ApplyTypeChoice(elicitation, typeName);
        try
        {
            if (!File.Exists(assemblyPath))
                return ToolErrors.AssemblyNotFound(assemblyPath);

            var cacheKey = CacheKeyHelper.Build(
                "member.all",
                CacheKeyHelper.FileStamp(assemblyPath), typeName, includePublic, includeNonPublic, includeStatic, includeInstance);

            return middleware.Execute(cacheKey, () =>
            {
                var options = new MemberFilterOptions
                {
                    IncludePublic = includePublic,
                    IncludeNonPublic = includeNonPublic,
                    IncludeStatic = includeStatic,
                    IncludeInstance = includeInstance
                };

                MethodDetails[] methods;
                PropertyDetails[] properties;
                FieldDetails[] fields;
                EventDetails[] events;
                ConstructorDetails[] constructors;
                try
                {
                    methods = memberAnalysisService.GetMethods(assemblyPath, typeName, options);
                    properties = memberAnalysisService.GetProperties(assemblyPath, typeName, options);
                    fields = memberAnalysisService.GetFields(assemblyPath, typeName, options);
                    events = memberAnalysisService.GetEvents(assemblyPath, typeName, options);
                    constructors = memberAnalysisService.GetConstructors(assemblyPath, typeName, options);
                }
                catch (ArgumentException)
                {
                    return ToolErrors.TypeNotFound(contexts, assemblyPath, typeName);
                }

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
                var sizeValidationError = ResponseSizeHelper.ValidateResponseSize(result, "get_all_type_members");
                if (sizeValidationError != null)
                    return sizeValidationError;

                return JsonHelpers.Envelope("member.all", result);
            }, noCache);
        }
        catch (AmbiguousTypeNameException ex)
        {
            return Elicitation.AmbiguousType(elicitation, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "analyze all members");
        }
    }
}
