using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.MemberAnalysis;
using System.ComponentModel;
using System.Text.Json;

namespace Sherlock.MCP.Server.Tools;

/// <summary>
/// MCP tools for detailed .NET member analysis
/// </summary>
[McpServerToolType]
public static class MemberAnalysisTools
{
    [McpServerTool]
    [Description("Gets detailed information about all methods in a type, including signatures, parameters, overloads, and modifiers")]
    public static string GetTypeMethods(
        IMemberAnalysisService memberAnalysisService,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Full name of the type to analyze (e.g., 'System.String' or 'MyNamespace.MyClass')")] string typeName,
        [Description("Include public members (default: true)")] bool includePublic = true,
        [Description("Include non-public members (default: false)")] bool includeNonPublic = false,
        [Description("Include static members (default: true)")] bool includeStatic = true,
        [Description("Include instance members (default: true)")] bool includeInstance = true)
    {
        try
        {
            if (!File.Exists(assemblyPath))
            {
                return JsonSerializer.Serialize(new { error = $"Assembly file not found: {assemblyPath}" });
            }

            var options = new MemberFilterOptions
            {
                IncludePublic = includePublic,
                IncludeNonPublic = includeNonPublic,
                IncludeStatic = includeStatic,
                IncludeInstance = includeInstance
            };

            var methods = memberAnalysisService.GetMethods(assemblyPath, typeName, options);
            
            var result = new
            {
                typeName,
                assemblyPath,
                methodCount = methods.Length,
                methods = methods.Select(m => new
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
                    parameters = m.Parameters.Select(p => new
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

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Failed to analyze methods: {ex.Message}" });
        }
    }

    [McpServerTool]
    [Description("Gets detailed information about all properties in a type, including getters, setters, indexers, and access modifiers")]
    public static string GetTypeProperties(
        IMemberAnalysisService memberAnalysisService,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Full name of the type to analyze")] string typeName,
        [Description("Include public members (default: true)")] bool includePublic = true,
        [Description("Include non-public members (default: false)")] bool includeNonPublic = false,
        [Description("Include static members (default: true)")] bool includeStatic = true,
        [Description("Include instance members (default: true)")] bool includeInstance = true)
    {
        try
        {
            if (!File.Exists(assemblyPath))
            {
                return JsonSerializer.Serialize(new { error = $"Assembly file not found: {assemblyPath}" });
            }

            var options = new MemberFilterOptions
            {
                IncludePublic = includePublic,
                IncludeNonPublic = includeNonPublic,
                IncludeStatic = includeStatic,
                IncludeInstance = includeInstance
            };

            var properties = memberAnalysisService.GetProperties(assemblyPath, typeName, options);
            
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
                    indexerParameters = p.IndexerParameters.Select(param => new
                    {
                        name = param.Name,
                        typeName = param.TypeName,
                        isOptional = param.IsOptional,
                        defaultValue = param.DefaultValue
                    }).ToArray()
                }).ToArray()
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Failed to analyze properties: {ex.Message}" });
        }
    }

    [McpServerTool]
    [Description("Gets detailed information about all fields in a type, including const, readonly, static, and volatile fields")]
    public static string GetTypeFields(
        IMemberAnalysisService memberAnalysisService,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Full name of the type to analyze")] string typeName,
        [Description("Include public members (default: true)")] bool includePublic = true,
        [Description("Include non-public members (default: false)")] bool includeNonPublic = false,
        [Description("Include static members (default: true)")] bool includeStatic = true,
        [Description("Include instance members (default: true)")] bool includeInstance = true)
    {
        try
        {
            if (!File.Exists(assemblyPath))
            {
                return JsonSerializer.Serialize(new { error = $"Assembly file not found: {assemblyPath}" });
            }

            var options = new MemberFilterOptions
            {
                IncludePublic = includePublic,
                IncludeNonPublic = includeNonPublic,
                IncludeStatic = includeStatic,
                IncludeInstance = includeInstance
            };

            var fields = memberAnalysisService.GetFields(assemblyPath, typeName, options);
            
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
                    constantValue = f.ConstantValue
                }).ToArray()
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Failed to analyze fields: {ex.Message}" });
        }
    }

    [McpServerTool]
    [Description("Gets detailed information about all events in a type, including handler types and access modifiers")]
    public static string GetTypeEvents(
        IMemberAnalysisService memberAnalysisService,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Full name of the type to analyze")] string typeName,
        [Description("Include public members (default: true)")] bool includePublic = true,
        [Description("Include non-public members (default: false)")] bool includeNonPublic = false,
        [Description("Include static members (default: true)")] bool includeStatic = true,
        [Description("Include instance members (default: true)")] bool includeInstance = true)
    {
        try
        {
            if (!File.Exists(assemblyPath))
            {
                return JsonSerializer.Serialize(new { error = $"Assembly file not found: {assemblyPath}" });
            }

            var options = new MemberFilterOptions
            {
                IncludePublic = includePublic,
                IncludeNonPublic = includeNonPublic,
                IncludeStatic = includeStatic,
                IncludeInstance = includeInstance
            };

            var events = memberAnalysisService.GetEvents(assemblyPath, typeName, options);
            
            var result = new
            {
                typeName,
                assemblyPath,
                eventCount = events.Length,
                events = events.Select(e => new
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
                    removeMethodAccessModifier = e.RemoveMethodAccessModifier
                }).ToArray()
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Failed to analyze events: {ex.Message}" });
        }
    }

    [McpServerTool]
    [Description("Gets detailed information about all constructors in a type, including parameters and access modifiers")]
    public static string GetTypeConstructors(
        IMemberAnalysisService memberAnalysisService,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Full name of the type to analyze")] string typeName,
        [Description("Include public members (default: true)")] bool includePublic = true,
        [Description("Include non-public members (default: false)")] bool includeNonPublic = false,
        [Description("Include static members (default: true)")] bool includeStatic = true,
        [Description("Include instance members (default: true)")] bool includeInstance = true)
    {
        try
        {
            if (!File.Exists(assemblyPath))
            {
                return JsonSerializer.Serialize(new { error = $"Assembly file not found: {assemblyPath}" });
            }

            var options = new MemberFilterOptions
            {
                IncludePublic = includePublic,
                IncludeNonPublic = includeNonPublic,
                IncludeStatic = includeStatic,
                IncludeInstance = includeInstance
            };

            var constructors = memberAnalysisService.GetConstructors(assemblyPath, typeName, options);
            
            var result = new
            {
                typeName,
                assemblyPath,
                constructorCount = constructors.Length,
                constructors = constructors.Select(c => new
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

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Failed to analyze constructors: {ex.Message}" });
        }
    }

    [McpServerTool]
    [Description("Gets comprehensive member information for a type, including all methods, properties, fields, events, and constructors")]
    public static string GetAllTypeMembers(
        IMemberAnalysisService memberAnalysisService,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Full name of the type to analyze")] string typeName,
        [Description("Include public members (default: true)")] bool includePublic = true,
        [Description("Include non-public members (default: false)")] bool includeNonPublic = false,
        [Description("Include static members (default: true)")] bool includeStatic = true,
        [Description("Include instance members (default: true)")] bool includeInstance = true)
    {
        try
        {
            if (!File.Exists(assemblyPath))
            {
                return JsonSerializer.Serialize(new { error = $"Assembly file not found: {assemblyPath}" });
            }

            var options = new MemberFilterOptions
            {
                IncludePublic = includePublic,
                IncludeNonPublic = includeNonPublic,
                IncludeStatic = includeStatic,
                IncludeInstance = includeInstance
            };

            var methods = memberAnalysisService.GetMethods(assemblyPath, typeName, options);
            var properties = memberAnalysisService.GetProperties(assemblyPath, typeName, options);
            var fields = memberAnalysisService.GetFields(assemblyPath, typeName, options);
            var events = memberAnalysisService.GetEvents(assemblyPath, typeName, options);
            var constructors = memberAnalysisService.GetConstructors(assemblyPath, typeName, options);
            
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
                    parameterCount = m.Parameters.Length
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
                    isIndexer = p.IsIndexer
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
                    constantValue = f.ConstantValue
                }).ToArray(),
                events = events.Select(e => new
                {
                    name = e.Name,
                    signature = e.Signature,
                    accessModifier = e.AccessModifier,
                    isStatic = e.IsStatic,
                    eventHandlerTypeName = e.EventHandlerTypeName
                }).ToArray(),
                constructors = constructors.Select(c => new
                {
                    signature = c.Signature,
                    accessModifier = c.AccessModifier,
                    isStatic = c.IsStatic,
                    parameterCount = c.Parameters.Length
                }).ToArray()
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Failed to analyze all members: {ex.Message}" });
        }
    }
}