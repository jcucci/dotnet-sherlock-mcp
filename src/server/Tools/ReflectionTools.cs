using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Server.Shared;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class ReflectionTools
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    [McpServerTool]
    [Description("Analyzes a .NET assembly and returns information about all public types, their members, and metadata")]
    public static string AnalyzeAssembly(
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

            var assembly = Assembly.LoadFrom(assemblyPath);
            var allTypes = assembly.GetExportedTypes();

            // Pagination logic
            var defaultPageSize = runtimeOptions.DefaultMaxItems > 0 ? runtimeOptions.DefaultMaxItems : 50;
            var pageSize = Math.Max(1, maxItems ?? defaultPageSize);
            var offset = 0;

            var cacheKey = $"analyze_assembly_{assemblyPath}_{pageSize}";
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
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to analyze assembly: {ex.Message}");
        }
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

    [McpServerTool]
    [Description("Gets detailed information about a specific type including all its members, methods, properties, and fields")]
    public static string AnalyzeType(
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

            var assembly = Assembly.LoadFrom(assemblyPath);
            var type = assembly.GetType(typeName)
                ?? assembly.GetExportedTypes().FirstOrDefault(t => string.Equals(t.FullName, typeName, StringComparison.Ordinal) || string.Equals(t.Name, typeName, StringComparison.Ordinal));

            if (type == null)
                return JsonHelpers.Error("TypeNotFound", $"Type '{typeName}' not found in assembly");

            // Pagination logic
            var defaultPageSize = 25;
            var pageSize = Math.Max(1, maxItems ?? defaultPageSize);
            var offset = 0;

            var cacheKey = $"analyze_type_{assemblyPath}_{typeName}_{pageSize}";
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
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to analyze type: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Searches for an assembly by its class name in common binary folders (bin/Debug, bin/Release, etc.).")]
    public static string FindAssemblyByClassName(
        [Description("The class name to search for (e.g., 'MyClass').")] string className,
        [Description("The root directory to start the search from.")] string workingDirectory)
    {
        try
        {
            var assemblyPath = AssemblyLocator.FindAssemblyByClassName(className, workingDirectory);

            if (assemblyPath == null)
                return JsonHelpers.Error("AssemblyNotFound", $"Assembly '{className}' not found in common binary folders.");

            var result = new
            {
                searchTerm = className,
                workingDirectory,
                foundAssembly = assemblyPath,
            };

            return JsonHelpers.Envelope("reflection.findByClassName", result);
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to search for assembly: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Searches for an assembly by its file name in common binary folders (bin/Debug, bin/Release, etc.).")]
    public static string FindAssemblyByFileName(
        [Description("The file name of the assembly to search for (e.g., 'MyProject.dll').")] string assemblyFileName,
        [Description("The root directory to start the search from.")] string workingDirectory)
    {
        try
        {
            var assemblyPath = Directory.GetFiles(workingDirectory, assemblyFileName, SearchOption.AllDirectories).FirstOrDefault();

            if (assemblyPath == null)
                return JsonHelpers.Error("AssemblyNotFound", $"Assembly '{assemblyFileName}' not found in common binary folders.");

            var result = new
            {
                searchTerm = assemblyFileName,
                workingDirectory,
                foundAssembly = assemblyPath,
            };

            return JsonHelpers.Envelope("reflection.findByFileName", result);
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to search for assembly: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Gets detailed information about a method, including overloads, parameters, attributes, and return type")]
    public static string AnalyzeMethod(
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name containing the method. Prefer full name (e.g., 'System.String'); simple names are also accepted")] string typeName,
        [Description("Name of the method to analyze")] string methodName)
    {
        try
        {
            if (!File.Exists(assemblyPath))
                return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");

            var assembly = Assembly.LoadFrom(assemblyPath);
            var type = assembly.GetType(typeName)
                ?? assembly.GetExportedTypes()
                    .FirstOrDefault(t => string.Equals(t.FullName, typeName, StringComparison.Ordinal)
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
                        defaultValue = p.HasDefaultValue ? p.DefaultValue?.ToString() : null,
                        isIn = p.IsIn,
                        isOut = p.IsOut,
                        isParams = p.GetCustomAttributes(typeof(ParamArrayAttribute), false).Length > 0
                    }).ToArray(),
                    attributes = method.GetCustomAttributes().Select(attr => new
                    {
                        type = attr.GetType().FullName,
                        toString = attr.ToString()
                    }).ToArray()
                }).ToArray()
            };

            return JsonHelpers.Envelope("reflection.method", result);
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to analyze method: {ex.Message}");
        }
    }
}
