using ModelContextProtocol.Server;

using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Sherlock.MCP.Server.Shared;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class ReflectionTools
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    [McpServerTool]
    [Description("Analyzes a .NET assembly and returns information about all public types, their members, and metadata")]
    public static string AnalyzeAssembly([Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath)
    {
        try
        {
            if (!File.Exists(assemblyPath))
                return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");

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

            return JsonHelpers.Envelope("reflection.assembly", result);
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to analyze assembly: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Gets detailed information about a specific type including all its members, methods, properties, and fields")]
    public static string AnalyzeType(
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name to analyze. Prefer full name (e.g., 'System.String'); simple names are also accepted")] string typeName)
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
                .Where(m => !m.IsSpecialName)
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
                        isParams = p.GetCustomAttributes(typeof(ParamArrayAttribute), false).Any()
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
