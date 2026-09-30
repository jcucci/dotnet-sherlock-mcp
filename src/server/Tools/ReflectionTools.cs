using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Server.Shared;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class ReflectionTools
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    [McpServerTool(Title = "Analyze Assembly", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Lists all public types in an assembly with metadata summary. Returns totalTypeCount for pagination planning. Use maxItems=25 for large assemblies (100+ types). Follow with GetTypeInfo for specific types.")]
    public static string AnalyzeAssembly(
        IInspectionContextProvider contexts,
        RuntimeOptions runtimeOptions,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Maximum number of types to return (default: 50)")] int? maxItems = null,
        [Description("Items to skip (paging)")] int? skip = null,
        [Description("Continuation token for paging")] string? continuationToken = null)
    {
        try
        {
            if (!File.Exists(assemblyPath))
                return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");

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

            // Pagination logic
            var defaultPageSize = runtimeOptions.GetMaxItemsForTool("AnalyzeAssembly");
            var pageSize = Math.Max(1, maxItems ?? defaultPageSize);
            var offset = 0;

            var cacheKey = $"analyze_assembly_{CacheKeyHelper.FileStamp(assemblyPath)}_{pageSize}";
            var salt = TokenHelper.MakeSalt(cacheKey);

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
            var sizeValidationError = ResponseSizeHelper.ValidateResponseSize(result, "AnalyzeAssembly");
            if (sizeValidationError != null)
                return sizeValidationError;

            return JsonHelpers.Envelope("reflection.assembly", result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return JsonHelpers.Error("InternalError", $"Failed to analyze assembly: {ex.Message}");
        }
    }

    [McpServerTool(Title = "Get Assembly Info", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets assembly-level metadata: identity/version, target framework, and referenced assemblies. Lightweight orientation tool — call before deep type analysis. Use projection='full' for all assembly-level attributes structurally.")]
    public static string GetAssemblyInfo(
        IInspectionContextProvider contexts,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Detail level: 'summary' (default, lean) or 'full' (adds all assembly attributes)")] string projection = "summary")
    {
        try
        {
            if (!File.Exists(assemblyPath))
                return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");

            var normalizedProjection = (projection ?? "summary").Trim().ToLowerInvariant();
            if (normalizedProjection != "summary" && normalizedProjection != "full")
                return JsonHelpers.Error("InvalidProjection", "projection must be 'summary' or 'full'");

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

            var sizeValidationError = ResponseSizeHelper.ValidateResponseSize(result, "GetAssemblyInfo");
            if (sizeValidationError != null)
                return sizeValidationError;

            return JsonHelpers.Envelope("reflection.assemblyInfo", result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return JsonHelpers.Error("InternalError", $"Failed to get assembly info: {ex.Message}");
        }
    }

    private static string? ReadTargetFramework(Assembly assembly)
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
                        defaultValue = p.HasDefaultValue ? p.DefaultValue?.ToString() : null
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
                        defaultValue = p.HasDefaultValue ? p.DefaultValue?.ToString() : null
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
    [Description("Gets type metadata with paginated members (constructors, methods, properties, fields). Returns member totals for pagination planning. Use include* flags to filter member categories. Consider GetTypeMethods etc. for targeted queries.")]
    public static string AnalyzeType(
        IInspectionContextProvider contexts,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name to analyze. Prefer full name (e.g., 'System.String'); simple names are also accepted")] string typeName,
        [Description("Maximum number of members to return per category (default: 25)")] int? maxItems = null,
        [Description("Items to skip (paging)")] int? skip = null,
        [Description("Continuation token for paging")] string? continuationToken = null,
        [Description("Include constructors in results (default: true)")] bool includeConstructors = true,
        [Description("Include methods in results (default: true)")] bool includeMethods = true,
        [Description("Include properties in results (default: true)")] bool includeProperties = true,
        [Description("Include fields in results (default: true)")] bool includeFields = true)
    {
        try
        {
            if (!File.Exists(assemblyPath))
                return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");

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

            var type = assembly.GetType(typeName)
                ?? exportedTypes.FirstOrDefault(t => string.Equals(t.FullName, typeName, StringComparison.Ordinal) || string.Equals(t.Name, typeName, StringComparison.Ordinal));

            if (type == null)
                return JsonHelpers.Error("TypeNotFound", $"Type '{typeName}' not found in assembly");

            // Pagination logic
            var defaultPageSize = 25;
            var pageSize = Math.Max(1, maxItems ?? defaultPageSize);
            var offset = 0;

            var cacheKey = $"analyze_type_{CacheKeyHelper.FileStamp(assemblyPath)}_{typeName}_{pageSize}";
            var salt = TokenHelper.MakeSalt(cacheKey);

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
            var sizeValidationError = ResponseSizeHelper.ValidateResponseSize(result, "AnalyzeType");
            if (sizeValidationError != null)
                return sizeValidationError;

            return JsonHelpers.Envelope("reflection.type", result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return JsonHelpers.Error("InternalError", $"Failed to analyze type: {ex.Message}");
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
            return JsonHelpers.Error("InternalError", $"Failed to search for assembly: {ex.Message}");
        }
    }

    [McpServerTool(Title = "Find Assembly by NuGet Package", ReadOnly = true, Destructive = false)]
    [Description("Finds an assembly in the local NuGet cache by package id. Probes ~/.nuget/packages (or NUGET_PACKAGES env var). Use when you know the package id/version but not the DLL path. Picks highest version and best TFM when omitted.")]
    public static async Task<string> FindAssemblyByNugetPackage(
        IProjectAnalysisService projectAnalysis,
        [Description("The NuGet package id (case-insensitive, e.g., 'Newtonsoft.Json').")] string packageId,
        [Description("Optional package version (e.g., '13.0.3'). If omitted, the highest available version is picked.")] string? version = null,
        [Description("Optional target framework moniker (e.g., 'net9.0'). If omitted, the best available TFM is picked.")] string? tfm = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(packageId))
                return JsonHelpers.Error("InvalidArgument", "packageId must be provided.");

            NugetAssemblyLookup lookup;
            try
            {
                lookup = await projectAnalysis.FindAssemblyInNugetCacheAsync(packageId, version, tfm);
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

            return JsonHelpers.Envelope(NuGetLookupResponse.Kind, NuGetLookupResponse.Success(lookup));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return JsonHelpers.Error("InternalError", $"Failed to resolve NuGet package: {ex.Message}");
        }
    }

    [McpServerTool(Title = "Analyze Method", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets detailed info about a specific method including all overloads, parameters, attributes, and return types. Use after finding method via GetTypeMethods. Lightweight response.")]
    public static string AnalyzeMethod(
        IInspectionContextProvider contexts,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name containing the method. Prefer full name (e.g., 'System.String'); simple names are also accepted")] string typeName,
        [Description("Name of the method to analyze")] string methodName)
    {
        try
        {
            if (!File.Exists(assemblyPath))
                return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");

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

            var type = assembly.GetType(typeName)
                ?? exportedTypes.FirstOrDefault(t => string.Equals(t.FullName, typeName, StringComparison.Ordinal)
                                       || string.Equals(t.Name, typeName, StringComparison.Ordinal));
            if (type == null)
                return JsonHelpers.Error("TypeNotFound", $"Type '{typeName}' not found in assembly");

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m.Name == methodName)
                .ToArray();

            if (methods.Length == 0)
                return JsonHelpers.Error("MemberNotFound", $"Method '{methodName}' not found in type '{typeName}'");

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
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return JsonHelpers.Error("InternalError", $"Failed to analyze method: {ex.Message}");
        }
    }
}
