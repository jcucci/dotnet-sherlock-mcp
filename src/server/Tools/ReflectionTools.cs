using ModelContextProtocol.Protocol;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;
using Sherlock.MCP.Runtime.Handles;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Schemas;
using Sherlock.MCP.Server.Shared;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class ReflectionTools
{
    [McpServerTool(Title = "Analyze Assembly", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Lists all public types in an assembly with metadata summary. Returns totalTypeCount for pagination planning. Use maxItems=25 for large assemblies (100+ types). Follow with get_type_info for specific types.")]
    public static string AnalyzeAssembly(
        IInspectionContextProvider contexts,
        ToolMiddleware middleware,
        RuntimeOptions runtimeOptions,
        IAssemblyHandleRegistry handles,
        [Description("Path to the .NET assembly file (.dll or .exe). Omit when passing assemblyHandle.")] string? assemblyPath = null,
        [Description("Handle returned by open_assembly; pass instead of assemblyPath")] string? assemblyHandle = null,
        [Description("Maximum number of types to return (default: 50)")] int? maxItems = null,
        [Description("Items to skip (paging)")] int? skip = null,
        [Description("Continuation token for paging")] string? continuationToken = null,
        [Description("Bypass cache for this request")] bool noCache = false)
    {
        try
        {
            var target = AssemblyScope.ResolveTarget(handles, assemblyPath, assemblyHandle);
            if (target.Error != null)
                return target.Error;
            assemblyPath = target.Path;

            var pageSize = Math.Max(1, maxItems ?? runtimeOptions.GetMaxItemsForTool("analyze_assembly"));
            var saltSeed = $"analyze_assembly_{CacheKeyHelper.FileStamp(assemblyPath)}_{pageSize}";
            var cacheKey = CacheKeyHelper.Build(
                "reflection.assembly",
                CacheKeyHelper.FileStamp(assemblyPath), pageSize, skip, continuationToken);

            return middleware.Execute(cacheKey, () =>
            {
                using var lease = contexts.Acquire(assemblyPath);
                var assembly = lease.Assembly;
                Type[] allTypes;
                try
                {
                    allTypes = assembly.GetExportedTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    allTypes = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
                }

                var offset = 0;
                var salt = TokenHelper.MakeSalt(saltSeed);

                if (!string.IsNullOrWhiteSpace(continuationToken))
                {
                    if (!TokenHelper.TryParse(continuationToken, out offset, out var parsedSalt) || parsedSalt != salt)
                        return JsonHelpers.Error("InvalidContinuationToken", "The continuation token is invalid or expired.");
                }
                else if (skip.HasValue && skip.Value > 0)
                {
                    offset = skip.Value;
                }

                var types = allTypes.Skip(offset).Take(pageSize).ToArray();
                string? nextToken = null;
                var nextOffset = offset + types.Length;
                if (nextOffset < allTypes.Length)
                    nextToken = TokenHelper.Make(nextOffset, salt);

                var result = new
                {
                    assemblyName = assembly.FullName,
                    location = assembly.Location,
                    totalTypeCount = allTypes.Length,
                    returnedTypeCount = types.Length,
                    nextToken,
                    types = types.Select(type => new
                    {
                        name = type.Name,
                        fullName = type.FullName,
                        namespace_ = type.Namespace,
                        isClass = type.IsClass,
                        isInterface = type.IsInterface,
                        isEnum = type.IsEnum,
                        isAbstract = type.IsAbstract,
                        isSealed = type.IsSealed,
                        isGeneric = type.IsGenericType,
                        baseType = type.BaseType?.FullName,
                        interfaces = type.GetInterfaces().Select(i => i.FullName).ToArray(),
                        memberCount = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Length
                    }).ToArray()
                };

                // Check response size before returning
                var sizeValidationError = ResponseSizeHelper.ValidateResponseSize(result, "analyze_assembly");
                if (sizeValidationError != null)
                    return sizeValidationError;

                return JsonHelpers.Envelope("reflection.assembly", result);
            }, noCache);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "analyze assembly");
        }
    }

    [McpServerTool(Title = "Get Assembly Info", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolEnvelope<AssemblyInfoData>))]
    [Description("Gets assembly-level metadata: identity/version, target framework, and referenced assemblies. Lightweight orientation tool — call before deep type analysis. Use projection='full' for all assembly-level attributes structurally.")]
    public static CallToolResult GetAssemblyInfo(
        IInspectionContextProvider contexts,
        ToolMiddleware middleware,
        IAssemblyHandleRegistry handles,
        [Description("Path to the .NET assembly file (.dll or .exe). Omit when passing assemblyHandle.")] string? assemblyPath = null,
        [Description("Handle returned by open_assembly; pass instead of assemblyPath")] string? assemblyHandle = null,
        [Description("Detail level: 'summary' (default, lean) or 'full' (adds all assembly attributes)")] string projection = "summary",
        [Description("Bypass cache for this request")] bool noCache = false)
    {
        try
        {
            var target = AssemblyScope.ResolveTarget(handles, assemblyPath, assemblyHandle);
            if (target.Error != null)
                return ToolResponse.Result(target.Error);
            assemblyPath = target.Path;

            var normalizedProjection = (projection ?? "summary").Trim().ToLowerInvariant();
            if (normalizedProjection != "summary" && normalizedProjection != "full")
                return ToolResponse.Result(JsonHelpers.Error("InvalidProjection", "projection must be 'summary' or 'full'"));

            var cacheKey = CacheKeyHelper.Build("reflection.assemblyInfo", CacheKeyHelper.FileStamp(assemblyPath), normalizedProjection);
            return ToolResponse.Result(middleware.Execute(cacheKey, () =>
            {
                using var lease = contexts.Acquire(assemblyPath);
                var assembly = lease.Assembly;
                var name = assembly.GetName();

                var referencedAssemblies = assembly.GetReferencedAssemblies()
                    .Select(r => $"{r.Name}@{r.Version}")
                    .OrderBy(r => r, StringComparer.Ordinal)
                    .ToArray();

                var isFull = normalizedProjection == "full";

                object result = isFull
                    ? new
                    {
                        projection = "full",
                        name = name.Name,
                        version = name.Version?.ToString(),
                        fullName = assembly.FullName,
                        location = assembly.Location,
                        targetFramework = ReadTargetFramework(assembly),
                        referencedAssemblies,
                        attributes = assembly.GetCustomAttributesData().Select(AttributeUtils.Convert).ToArray()
                    }
                    : new
                    {
                        projection = "summary",
                        name = name.Name,
                        version = name.Version?.ToString(),
                        fullName = assembly.FullName,
                        location = assembly.Location,
                        targetFramework = ReadTargetFramework(assembly),
                        referencedAssemblies
                    };

                var sizeValidationError = ResponseSizeHelper.ValidateResponseSize(result, "get_assembly_info");
                if (sizeValidationError != null)
                    return sizeValidationError;

                return JsonHelpers.Envelope("reflection.assemblyInfo", result);
            }, noCache));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResponse.Result(ToolErrors.FromException(ex, "get assembly info"));
        }
    }

    internal static string? ReadTargetFramework(Assembly assembly)
    {
        try
        {
            var attr = assembly.GetCustomAttributesData()
                .FirstOrDefault(a => a.AttributeType.FullName == "System.Runtime.Versioning.TargetFrameworkAttribute");
            return attr?.ConstructorArguments.Count > 0 ? attr.ConstructorArguments[0].Value as string : null;
        }
        catch
        {
            return null;
        }
    }

    private static object? SafeRawDefault(ParameterInfo p)
    {
        try { return p.RawDefaultValue; }
        catch { return null; }
    }

    private static List<dynamic> CollectAllMembers(
        ConstructorInfo[] constructors, MethodInfo[] methods,
        PropertyInfo[] properties, FieldInfo[] fields,
        bool includeConstructors, bool includeMethods,
        bool includeProperties, bool includeFields)
    {
        var allMembers = new List<dynamic>();

        if (includeConstructors)
        {
            foreach (var constructor in constructors)
            {
                allMembers.Add(new
                {
                    memberType = "constructor",
                    name = constructor.Name,
                    parameters = constructor.GetParameters().Select(p => new
                    {
                        name = p.Name,
                        type = p.ParameterType.FullName,
                        hasDefaultValue = p.HasDefaultValue,
                        defaultValue = p.HasDefaultValue ? SafeRawDefault(p)?.ToString() : null
                    }).ToArray()
                });
            }
        }

        if (includeMethods)
        {
            foreach (var method in methods)
            {
                allMembers.Add(new
                {
                    memberType = "method",
                    name = method.Name,
                    isStatic = method.IsStatic,
                    isAbstract = method.IsAbstract,
                    isVirtual = method.IsVirtual,
                    returnType = method.ReturnType.FullName,
                    parameters = method.GetParameters().Select(p => new
                    {
                        name = p.Name,
                        type = p.ParameterType.FullName,
                        hasDefaultValue = p.HasDefaultValue,
                        defaultValue = p.HasDefaultValue ? SafeRawDefault(p)?.ToString() : null
                    }).ToArray()
                });
            }
        }

        if (includeProperties)
        {
            foreach (var property in properties)
            {
                allMembers.Add(new
                {
                    memberType = "property",
                    name = property.Name,
                    propertyType = property.PropertyType.FullName,
                    canRead = property.CanRead,
                    canWrite = property.CanWrite,
                    isStatic = (property.GetGetMethod() ?? property.GetSetMethod())?.IsStatic ?? false,
                    isIndexer = property.GetIndexParameters().Length > 0,
                    indexParameters = property.GetIndexParameters().Select(p => new
                    {
                        name = p.Name,
                        type = p.ParameterType.FullName
                    }).ToArray()
                });
            }
        }

        if (includeFields)
        {
            foreach (var field in fields)
            {
                allMembers.Add(new
                {
                    memberType = "field",
                    name = field.Name,
                    fieldType = field.FieldType.FullName,
                    isStatic = field.IsStatic,
                    isReadOnly = field.IsInitOnly,
                    isConstant = field.IsLiteral,
                    constantValue = field.IsLiteral ? field.GetRawConstantValue()?.ToString() : null
                });
            }
        }

        return allMembers;
    }

    [McpServerTool(Title = "Analyze Type", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Deprecated: use get_type_info for type metadata plus get_type_members with projection='full' for members. Gets type metadata with paginated members (constructors, methods, properties, fields). Returns member totals for pagination planning. Use include* flags to filter member categories.")]
    public static string AnalyzeType(
        IInspectionContextProvider contexts,
        ToolMiddleware middleware,
        IAssemblyHandleRegistry handles,
        [Description("Type name to analyze. Prefer full name (e.g., 'System.String'); simple names are also accepted")] string typeName,
        [Description("Path to the .NET assembly file (.dll or .exe). Omit when passing assemblyHandle.")] string? assemblyPath = null,
        [Description("Handle returned by open_assembly; pass instead of assemblyPath")] string? assemblyHandle = null,
        [Description("Maximum number of members to return per category (default: 25)")] int? maxItems = null,
        [Description("Items to skip (paging)")] int? skip = null,
        [Description("Continuation token for paging")] string? continuationToken = null,
        [Description("Include constructors in results (default: true)")] bool includeConstructors = true,
        [Description("Include methods in results (default: true)")] bool includeMethods = true,
        [Description("Include properties in results (default: true)")] bool includeProperties = true,
        [Description("Include fields in results (default: true)")] bool includeFields = true,
        [Description("Bypass cache for this request")] bool noCache = false,
        RequestContext<CallToolRequestParams>? context = null)
    {
        var elicitation = ElicitationContext.From(context);
        typeName = Elicitation.ApplyTypeChoice(elicitation, typeName);
        try
        {
            var target = AssemblyScope.ResolveTarget(handles, assemblyPath, assemblyHandle);
            if (target.Error != null)
                return target.Error;
            assemblyPath = target.Path;

            var pageSize = Math.Max(1, maxItems ?? 25);
            var saltSeed = $"analyze_type_{CacheKeyHelper.FileStamp(assemblyPath)}_{typeName}_{pageSize}";
            var cacheKey = CacheKeyHelper.Build(
                "reflection.type",
                CacheKeyHelper.FileStamp(assemblyPath), typeName, pageSize, skip, continuationToken,
                includeConstructors, includeMethods, includeProperties, includeFields);

            return middleware.Execute(cacheKey, () =>
            {
                using var lease = contexts.Acquire(assemblyPath);
                var assembly = lease.Assembly;
                Type[] exportedTypes;
                try
                {
                    exportedTypes = assembly.GetExportedTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    exportedTypes = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
                }

                var type = TypeNameResolver.Resolve(assembly, () => exportedTypes, typeName).OrThrowIfAmbiguous(typeName);

                if (type == null)
                    return ToolErrors.TypeNotFound(lease.Context, typeName, searchedTypes: exportedTypes);

                var offset = 0;
                var salt = TokenHelper.MakeSalt(saltSeed);

                if (!string.IsNullOrWhiteSpace(continuationToken))
                {
                    if (!TokenHelper.TryParse(continuationToken, out offset, out var parsedSalt) || parsedSalt != salt)
                        return JsonHelpers.Error("InvalidContinuationToken", "The continuation token is invalid or expired.");
                }
                else if (skip.HasValue && skip.Value > 0)
                {
                    offset = skip.Value;
                }

                // Get all constructors if requested
                var allConstructors = includeConstructors ?
                    type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).ToArray() :
                    [];

                // Get all methods if requested
                var allMethods = includeMethods ?
                    type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                        .Where(m => !m.IsSpecialName).ToArray() :
                    [];

                // Get all properties if requested
                var allProperties = includeProperties ?
                    type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).ToArray() :
                    [];

                // Get all fields if requested
                var allFields = includeFields ?
                    type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).ToArray() :
                    [];

                // Apply pagination across all member types
                var allMembers = CollectAllMembers(allConstructors, allMethods, allProperties, allFields,
                    includeConstructors, includeMethods, includeProperties, includeFields);

                var totalMembers = allMembers.Count;
                var pagedMembers = allMembers.Skip(offset).Take(pageSize).ToArray();

                // Calculate next token
                var nextOffset = offset + pagedMembers.Length;
                string? nextToken = null;
                if (nextOffset < totalMembers)
                    nextToken = TokenHelper.Make(nextOffset, salt);

                // Separate members by type for response
                var constructors = pagedMembers.Where(m => m.memberType == "constructor").ToArray();
                var methods = pagedMembers.Where(m => m.memberType == "method").ToArray();
                var properties = pagedMembers.Where(m => m.memberType == "property").ToArray();
                var fields = pagedMembers.Where(m => m.memberType == "field").ToArray();

                var result = new
                {
                    typeName = type.FullName,
                    namespace_ = type.Namespace,
                    assemblyName = type.Assembly.FullName,
                    isClass = type.IsClass,
                    isInterface = type.IsInterface,
                    isEnum = type.IsEnum,
                    isAbstract = type.IsAbstract,
                    isSealed = type.IsSealed,
                    isGeneric = type.IsGenericType,
                    baseType = type.BaseType?.FullName,
                    interfaces = type.GetInterfaces().Select(i => i.FullName).ToArray(),
                    totalConstructors = allConstructors.Length,
                    totalMethods = allMethods.Length,
                    totalProperties = allProperties.Length,
                    totalFields = allFields.Length,
                    nextToken,
                    constructors,
                    methods,
                    properties,
                    fields
                };

                // Check response size before returning
                var sizeValidationError = ResponseSizeHelper.ValidateResponseSize(result, "analyze_type");
                if (sizeValidationError != null)
                    return sizeValidationError;

                return JsonHelpers.Envelope("reflection.type", result);
            }, noCache);
        }
        catch (AmbiguousTypeNameException ex)
        {
            return Elicitation.AmbiguousType(elicitation, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "analyze type");
        }
    }

    [McpServerTool(Title = "Find Assembly by Class Name", ReadOnly = true, Destructive = false)]
    [Description("Recursively searches workingDirectory for .dll/.exe files that declare a public type matching className, skipping obj/, ref/, refint/, node_modules/, packages/, TestResults/ and dot-directories. Matches are ranked bin/ first, then newest, then shortest path; returns the best match as foundAssembly plus other candidates. Use when you know the class but not the assembly path.")]
    public static string FindAssemblyByClassName(
        [Description("The class name to search for: simple (e.g., 'MyClass'), full ('My.Namespace.MyClass') or nested ('Outer+Inner').")] string className,
        [Description("The root directory to start the search from.")] string workingDirectory,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default) =>
        FindAssembly(
            kind: "reflection.findByClassName",
            searchTerm: className,
            workingDirectory: workingDirectory,
            notFoundMessage: $"No assembly declaring a public type '{className}' was found under '{workingDirectory}'.",
            locate: () => AssemblyLocator.FindByClassName(workingDirectory, className, ProgressAdapter.ForPhase(progress), cancellationToken));

    [McpServerTool(Title = "Find Assembly by File Name", ReadOnly = true, Destructive = false)]
    [Description("Recursively searches workingDirectory for an assembly file name, skipping obj/, ref/, refint/, node_modules/, packages/, TestResults/ and dot-directories. Matches are ranked bin/ first, then newest, then shortest path; returns the best match as foundAssembly plus other candidates. Use when you know the assembly name but not its full path.")]
    public static string FindAssemblyByFileName(
        [Description("The file name of the assembly to search for (e.g., 'MyProject.dll'). Wildcards (*, ?) are allowed.")] string assemblyFileName,
        [Description("The root directory to start the search from.")] string workingDirectory,
        CancellationToken cancellationToken = default) =>
        FindAssembly(
            kind: "reflection.findByFileName",
            searchTerm: assemblyFileName,
            workingDirectory: workingDirectory,
            notFoundMessage: $"No assembly named '{assemblyFileName}' was found under '{workingDirectory}'.",
            locate: () => AssemblyLocator.FindByFileName(workingDirectory, assemblyFileName, cancellationToken));

    private const int MaxCandidates = 10;

    private static string FindAssembly(string kind, string searchTerm, string workingDirectory, string notFoundMessage, Func<IReadOnlyList<string>> locate)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return JsonHelpers.ErrorWithGuidance("InvalidArgument", "A search term must be provided.");

        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            return JsonHelpers.ErrorWithGuidance(
                "InvalidArgument",
                $"workingDirectory '{workingDirectory}' does not exist.",
                suggestion: "Pass an existing directory, typically the solution or project root.");

        try
        {
            var candidates = locate();

            if (candidates.Count == 0)
                return JsonHelpers.ErrorWithGuidance(
                    "AssemblyNotFound",
                    $"{notFoundMessage} Skipped directories: {string.Join(", ", AssemblyLocator.ExcludedDirectoryNames)} and dot-directories.",
                    suggestion: "Build the project first, or resolve its output path or NuGet package directly.",
                    alternativeTools: ["get_project_output_paths", "find_assembly_by_nuget_package"]);

            var result = new
            {
                searchTerm,
                workingDirectory,
                foundAssembly = candidates[0],
                candidateCount = candidates.Count,
                candidates = candidates.Count > 1 ? candidates.Take(MaxCandidates).ToArray() : null
            };

            return JsonHelpers.Envelope(kind, result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "search for assembly");
        }
    }

    [McpServerTool(Title = "Find Assembly by NuGet Package", ReadOnly = true, Destructive = false)]
    [Description("Finds an assembly in the local NuGet cache by package id. Probes ~/.nuget/packages (or NUGET_PACKAGES env var). Use when you know the package id/version but not the DLL path. Picks the highest version when omitted. When tfm is omitted and the package ships several, clients that support elicitation are asked which one to use; otherwise the best TFM is picked. The response lists availableVersions and availableTfms.")]
    public static async Task<string> FindAssemblyByNugetPackage(
        IProjectAnalysisService projectAnalysis,
        [Description("The NuGet package id (case-insensitive, e.g., 'Newtonsoft.Json').")] string packageId,
        [Description("Optional package version (e.g., '13.0.3'). If omitted, the highest available version is picked.")] string? version = null,
        [Description("Optional target framework moniker (e.g., 'net9.0'). If omitted, the client is asked to choose when several are available, or the best available TFM is picked.")] string? tfm = null,
        RequestContext<CallToolRequestParams>? context = null)
    {
        var elicitation = ElicitationContext.From(context);
        var chosenTfm = tfm ?? elicitation.Answer(Elicitation.TfmKey);
        try
        {
            if (string.IsNullOrWhiteSpace(packageId))
                return JsonHelpers.Error("InvalidArgument", "packageId must be provided.");

            NugetAssemblyLookup lookup;
            try
            {
                lookup = await projectAnalysis.FindAssemblyInNugetCacheAsync(packageId, version, chosenTfm);
            }
            catch (ArgumentException ex)
            {
                return JsonHelpers.Error("InvalidArgument", ex.Message);
            }

            if (lookup.Failure is NugetLookupFailure failure)
                return JsonHelpers.Error(
                    NuGetLookupResponse.FailureCode(failure),
                    NuGetLookupResponse.FailureMessage(lookup, failure),
                    NuGetLookupResponse.FailureDetails(lookup));

            if (ShouldAskForTfm(elicitation, chosenTfm, lookup))
                throw Elicitation.ChooseOne(
                    key: Elicitation.TfmKey,
                    message: $"'{lookup.PackageId}' {lookup.ResolvedVersion} ships {lookup.AvailableTfms.Length} target frameworks. Which one should be inspected?",
                    options: lookup.AvailableTfms,
                    defaultOption: lookup.ResolvedTfm);

            return JsonHelpers.Envelope(NuGetLookupResponse.Kind, NuGetLookupResponse.Success(lookup));
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not InputRequiredException)
        {
            return ToolErrors.FromException(ex, "resolve NuGet package");
        }
    }

    private static bool ShouldAskForTfm(ElicitationContext elicitation, string? chosenTfm, NugetAssemblyLookup lookup) =>
        chosenTfm is null
        && lookup.AvailableTfms.Length > 1
        && elicitation.CanElicit
        && !elicitation.HasResponse(Elicitation.TfmKey);

    [McpServerTool(Title = "Analyze Method", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets detailed info about a specific method including all overloads, parameters, attributes, and return types. Use after finding the method via get_type_members or search_members. Lightweight response.")]
    public static string AnalyzeMethod(
        IInspectionContextProvider contexts,
        ToolMiddleware middleware,
        IAssemblyHandleRegistry handles,
        [Description("Type name containing the method. Prefer full name (e.g., 'System.String'); simple names are also accepted")] string typeName,
        [Description("Name of the method to analyze")] string methodName,
        [Description("Path to the .NET assembly file (.dll or .exe). Omit when passing assemblyHandle.")] string? assemblyPath = null,
        [Description("Handle returned by open_assembly; pass instead of assemblyPath")] string? assemblyHandle = null,
        [Description("Bypass cache for this request")] bool noCache = false,
        RequestContext<CallToolRequestParams>? context = null)
    {
        var elicitation = ElicitationContext.From(context);
        typeName = Elicitation.ApplyTypeChoice(elicitation, typeName);
        try
        {
            var target = AssemblyScope.ResolveTarget(handles, assemblyPath, assemblyHandle);
            if (target.Error != null)
                return target.Error;
            assemblyPath = target.Path;

            var cacheKey = CacheKeyHelper.Build("reflection.method", CacheKeyHelper.FileStamp(assemblyPath), typeName, methodName);

            return middleware.Execute(cacheKey, () =>
            {
                using var lease = contexts.Acquire(assemblyPath);
                var assembly = lease.Assembly;
                Type[] exportedTypes;
                try
                {
                    exportedTypes = assembly.GetExportedTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    exportedTypes = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
                }

                var type = TypeNameResolver.Resolve(assembly, () => exportedTypes, typeName).OrThrowIfAmbiguous(typeName);
                if (type == null)
                    return ToolErrors.TypeNotFound(lease.Context, typeName, searchedTypes: exportedTypes);

                const BindingFlags searchedMethods = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
                var methods = type.GetMethods(searchedMethods)
                    .Where(m => m.Name == methodName)
                    .ToArray();

                if (methods.Length == 0)
                    return ToolErrors.MemberNotFound(
                        type,
                        methodName,
                        "method",
                        message: $"Method '{methodName}' not found in type '{typeName}'",
                        bindingFlags: searchedMethods);

                var result = new
                {
                    typeName = type.FullName,
                    methodName,
                    overloads = methods.Select(method => new
                    {
                        signature = method.ToString(),
                        isStatic = method.IsStatic,
                        isAbstract = method.IsAbstract,
                        isVirtual = method.IsVirtual,
                        isGeneric = method.IsGenericMethod,
                        returnType = method.ReturnType.FullName,
                        parameters = method.GetParameters().Select(p => new
                        {
                            name = p.Name,
                            type = p.ParameterType.FullName,
                            position = p.Position,
                            hasDefaultValue = p.HasDefaultValue,
                            defaultValue = p.HasDefaultValue ? SafeRawDefault(p)?.ToString() : null,
                            isIn = p.IsIn,
                            isOut = p.IsOut,
                            isParams = p.CustomAttributes.Any(a => a.AttributeType.FullName == "System.ParamArrayAttribute")
                        }).ToArray(),
                        attributes = method.GetCustomAttributesData().Select(attr => new
                        {
                            type = attr.AttributeType.FullName,
                            toString = attr.ToString()
                        }).ToArray()
                    }).ToArray()
                };

                return JsonHelpers.Envelope("reflection.method", result);
            }, noCache);
        }
        catch (AmbiguousTypeNameException ex)
        {
            return Elicitation.AmbiguousType(elicitation, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolErrors.FromException(ex, "analyze method");
        }
    }
}
