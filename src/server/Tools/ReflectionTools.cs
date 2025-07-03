using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Server.Shared;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;

namespace Sherlock.MCP.Server.Tools;

/// <summary>
/// MCP tools for .NET assembly reflection and type analysis
/// </summary>
[McpServerToolType]
public static class ReflectionTools
{
    [McpServerTool]
    [Description("Analyzes a .NET assembly and returns information about all public types, their members, and metadata")]
    public static string AnalyzeAssembly(
        IAssemblyDiscoveryService discoveryService,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath)
    {
        try
        {
            if (!File.Exists(assemblyPath))
            {
                return JsonSerializer.Serialize(new { error = $"Assembly file not found: {assemblyPath}" });
            }

            var assembly = Assembly.LoadFrom(assemblyPath);
            var types = assembly.GetExportedTypes();

            var result = new
            {
                assemblyName = assembly.FullName,
                location = assembly.Location,
                typeCount = types.Length,
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

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Failed to analyze assembly: {ex.Message}" });
        }
    }

    [McpServerTool]
    [Description("Gets detailed information about a specific type including all its members, methods, properties, and fields")]
    public static string AnalyzeType(
        IAssemblyDiscoveryService discoveryService,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Full name of the type to analyze (e.g., 'System.String' or 'MyNamespace.MyClass')")] string typeName)
    {
        try
        {
            if (!File.Exists(assemblyPath))
            {
                return JsonSerializer.Serialize(new { error = $"Assembly file not found: {assemblyPath}" });
            }

            var assembly = Assembly.LoadFrom(assemblyPath);
            var type = assembly.GetType(typeName);

            if (type == null)
            {
                return JsonSerializer.Serialize(new { error = $"Type '{typeName}' not found in assembly" });
            }

            var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .Select(c => new
                {
                    name = c.Name,
                    parameters = c.GetParameters().Select(p => new
                    {
                        name = p.Name,
                        type = p.ParameterType.FullName,
                        hasDefaultValue = p.HasDefaultValue,
                        defaultValue = p.HasDefaultValue ? p.DefaultValue?.ToString() : null
                    }).ToArray()
                }).ToArray();

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => !m.IsSpecialName) // Exclude property getters/setters
                .Select(m => new
                {
                    name = m.Name,
                    isStatic = m.IsStatic,
                    isAbstract = m.IsAbstract,
                    isVirtual = m.IsVirtual,
                    returnType = m.ReturnType.FullName,
                    parameters = m.GetParameters().Select(p => new
                    {
                        name = p.Name,
                        type = p.ParameterType.FullName,
                        hasDefaultValue = p.HasDefaultValue,
                        defaultValue = p.HasDefaultValue ? p.DefaultValue?.ToString() : null
                    }).ToArray()
                }).ToArray();

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Select(p => new
                {
                    name = p.Name,
                    type = p.PropertyType.FullName,
                    canRead = p.CanRead,
                    canWrite = p.CanWrite,
                    isStatic = p.GetGetMethod()?.IsStatic ?? p.GetSetMethod()?.IsStatic ?? false
                }).ToArray();

            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Select(f => new
                {
                    name = f.Name,
                    type = f.FieldType.FullName,
                    isStatic = f.IsStatic,
                    isReadOnly = f.IsInitOnly,
                    isLiteral = f.IsLiteral
                }).ToArray();

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
                constructors,
                methods,
                properties,
                fields
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Failed to analyze type: {ex.Message}" });
        }
    }

    [McpServerTool]
    [Description("Searches for assemblies that contain a specific type name across common locations")]
    public static async Task<string> FindAssembliesByTypeName(
        IAssemblyDiscoveryService discoveryService,
        [Description("Name of the type to search for (can be partial, e.g., 'String', 'List', 'HttpClient')")] string typeName,
        [Description("Optional hint path to a directory or specific assembly file where the type might be located")] string? hintPath = null,
        [Description("Optional hint indicating if the type is likely from a NuGet package")] bool? isNuGetPackage = null)
    {
        try
        {
            var assemblies = await discoveryService.FindAssemblyByTypeNameAsync(typeName, hintPath, isNuGetPackage);
            
            var result = new
            {
                searchTerm = typeName,
                hintPath,
                isNuGetPackage,
                foundAssemblies = assemblies.Select(path => new
                {
                    path,
                    fileName = Path.GetFileName(path),
                    directory = Path.GetDirectoryName(path)
                }).ToArray(),
                count = assemblies.Length
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Failed to search for assemblies: {ex.Message}" });
        }
    }

    [McpServerTool]
    [Description("Lists all assemblies in a specified directory, with optional recursive search")]
    public static string ListAssembliesInDirectory(
        IAssemblyDiscoveryService discoveryService,
        [Description("Directory path to search for assemblies")] string directoryPath,
        [Description("Whether to search subdirectories recursively (default: true)")] bool recursive = true)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
            {
                return JsonSerializer.Serialize(new { error = $"Directory not found: {directoryPath}" });
            }

            var assemblies = discoveryService.FindAssembliesInDirectory(directoryPath, recursive);
            
            var result = new
            {
                directory = directoryPath,
                recursive,
                assemblies = assemblies.Select(path => new
                {
                    path,
                    fileName = Path.GetFileName(path),
                    size = new FileInfo(path).Length,
                    lastModified = new FileInfo(path).LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
                }).ToArray(),
                count = assemblies.Length
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Failed to list assemblies: {ex.Message}" });
        }
    }

    [McpServerTool]
    [Description("Gets the locations of .NET Framework and runtime assemblies")]
    public static string GetFrameworkAssemblies(
        IAssemblyDiscoveryService discoveryService)
    {
        try
        {
            var assemblies = discoveryService.GetFrameworkAssemblyLocations();
            
            var result = new
            {
                frameworkAssemblies = assemblies.Select(path => new
                {
                    path,
                    fileName = Path.GetFileName(path),
                    directory = Path.GetDirectoryName(path)
                }).ToArray(),
                count = assemblies.Length,
                runtimeVersion = Environment.Version.ToString(),
                frameworkDescription = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Failed to get framework assemblies: {ex.Message}" });
        }
    }

    [McpServerTool]
    [Description("Analyzes a method signature and provides detailed information about parameters, return type, and attributes")]
    public static string AnalyzeMethod(
        IAssemblyDiscoveryService discoveryService,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Full name of the type containing the method")] string typeName,
        [Description("Name of the method to analyze")] string methodName)
    {
        try
        {
            if (!File.Exists(assemblyPath))
            {
                return JsonSerializer.Serialize(new { error = $"Assembly file not found: {assemblyPath}" });
            }

            var assembly = Assembly.LoadFrom(assemblyPath);
            var type = assembly.GetType(typeName);

            if (type == null)
            {
                return JsonSerializer.Serialize(new { error = $"Type '{typeName}' not found in assembly" });
            }

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m.Name == methodName)
                .ToArray();

            if (methods.Length == 0)
            {
                return JsonSerializer.Serialize(new { error = $"Method '{methodName}' not found in type '{typeName}'" });
            }

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
                        isParams = p.GetCustomAttributes(typeof(ParamArrayAttribute), false).Any()
                    }).ToArray(),
                    attributes = method.GetCustomAttributes().Select(attr => new
                    {
                        type = attr.GetType().FullName,
                        toString = attr.ToString()
                    }).ToArray()
                }).ToArray()
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Failed to analyze method: {ex.Message}" });
        }
    }
}