# Member Analysis Service

This document describes the comprehensive member analysis functionality implemented in the Sherlock MCP Runtime project.

## Overview

The `MemberAnalysisService` provides detailed reflection capabilities for analyzing .NET types and their members, including methods, properties, fields, events, and constructors. It extracts comprehensive metadata that is easily consumable by LLMs and other tools.

## Core Components

### Interface: `IMemberAnalysisService`

```csharp
public interface IMemberAnalysisService
{
    Task<MemberInfo[]> GetAllMembersAsync(string assemblyPath, string typeName, MemberFilterOptions? options = null);
    Task<MethodDetails[]> GetMethodsAsync(string assemblyPath, string typeName, MemberFilterOptions? options = null);
    Task<PropertyDetails[]> GetPropertiesAsync(string assemblyPath, string typeName, MemberFilterOptions? options = null);
    Task<FieldDetails[]> GetFieldsAsync(string assemblyPath, string typeName, MemberFilterOptions? options = null);
    Task<EventDetails[]> GetEventsAsync(string assemblyPath, string typeName, MemberFilterOptions? options = null);
    Task<ConstructorDetails[]> GetConstructorsAsync(string assemblyPath, string typeName, MemberFilterOptions? options = null);
}
```

### Filtering Options: `MemberFilterOptions`

```csharp
public class MemberFilterOptions
{
    public bool IncludePublic { get; set; } = true;
    public bool IncludeNonPublic { get; set; } = false;
    public bool IncludeStatic { get; set; } = true;
    public bool IncludeInstance { get; set; } = true;
    public bool IncludeInherited { get; set; } = false;
    public bool IncludeDeclaredOnly { get; set; } = true;
}
```

## Detailed Member Information

### Methods (`MethodDetails`)

Captures comprehensive method information:
- **Signature**: Human-readable method signature
- **Parameters**: Detailed parameter information including types, defaults, ref/out/in modifiers
- **Generic Type Parameters**: Information about generic method parameters
- **Modifiers**: Access level, static, virtual, abstract, sealed, override
- **Special Types**: Operators, extension methods detection
- **Return Type**: Friendly type name with generic parameter resolution

### Properties (`PropertyDetails`)

Provides detailed property analysis:
- **Signature**: Complete property signature
- **Accessibility**: Getter/setter access modifiers
- **Indexers**: Support for indexed properties with parameter details
- **Modifiers**: Static, virtual, abstract, sealed, override
- **Read/Write Capabilities**: CanRead, CanWrite flags

### Fields (`FieldDetails`)

Extracts field metadata:
- **Signature**: Field declaration signature
- **Field Types**: const, readonly, static, volatile, init-only
- **Constant Values**: Actual values for const fields
- **Access Modifiers**: public, private, protected, internal, etc.

### Events (`EventDetails`)

Captures event information:
- **Handler Type**: Event delegate type information
- **Access Modifiers**: For add/remove methods
- **Inheritance**: Virtual, abstract, sealed, override flags
- **Signature**: Complete event declaration

### Constructors (`ConstructorDetails`)

Provides constructor analysis:
- **Parameters**: Detailed parameter information
- **Access Modifiers**: public, private, protected, internal
- **Static Constructors**: Special handling for type initializers
- **Signature**: Constructor declaration with parameters

## Key Features

### 1. Comprehensive Parameter Analysis

```csharp
public record ParameterDetails(
    string Name,
    string TypeName,
    string? DefaultValue,
    bool IsOptional,
    bool IsOut,
    bool IsRef,
    bool IsIn,
    bool IsParams,
    ParameterAttributes Attributes
);
```

### 2. Type Name Resolution

The service provides friendly type names that handle:
- Generic types with parameter expansion
- Built-in type aliases (int, string, bool, etc.)
- Array types with proper rank notation
- Pointer and reference types
- Nested types

### 3. Signature Generation

Each member type includes a human-readable signature that shows:
- Complete declaration syntax
- Access modifiers
- Inheritance modifiers (virtual, abstract, sealed, override)
- Parameter lists with types and defaults
- Return types for methods

### 4. Special Member Detection

- **Operator Methods**: Identifies operator overloads
- **Extension Methods**: Detects extension method attribute
- **Indexers**: Recognizes indexed properties
- **Volatile Fields**: Detects volatile field modifier
- **Override Detection**: Identifies method overrides

## MCP Tools Integration

The service is integrated with MCP (Model Context Protocol) tools:

### Available Tools

1. **GetTypeMethods**: Analyze all methods in a type
2. **GetTypeProperties**: Analyze all properties in a type
3. **GetTypeFields**: Analyze all fields in a type
4. **GetTypeEvents**: Analyze all events in a type
5. **GetTypeConstructors**: Analyze all constructors in a type
6. **GetAllTypeMembers**: Comprehensive analysis of all member types

### Tool Parameters

All tools support filtering options:
- `includePublic`: Include public members (default: true)
- `includeNonPublic`: Include non-public members (default: false)
- `includeStatic`: Include static members (default: true)
- `includeInstance`: Include instance members (default: true)

## Usage Examples

### Basic Service Usage

```csharp
var service = new MemberAnalysisService();
var assemblyPath = "/path/to/assembly.dll";
var typeName = "MyNamespace.MyClass";

// Get all methods
var methods = await service.GetMethodsAsync(assemblyPath, typeName);

// Get all members with filtering
var options = new MemberFilterOptions
{
    IncludePublic = true,
    IncludeNonPublic = true,
    IncludeStatic = false
};
var properties = await service.GetPropertiesAsync(assemblyPath, typeName, options);
```

### MCP Tool Usage

The tools are automatically registered and available through the MCP server. They return JSON responses suitable for LLM consumption.

## Assembly Loading

The service uses `Assembly.LoadFrom()` and maintains a cache of loaded assemblies to avoid repeated loading. It supports:
- Full assembly paths
- Type name resolution by both full name and simple name
- Error handling for missing assemblies or types

## Error Handling

The service provides comprehensive error handling:
- Assembly not found
- Type not found in assembly
- Reflection exceptions
- Security exceptions for restricted members

All errors are returned as structured data suitable for tool consumers.

## Performance Considerations

- Assembly caching prevents repeated loading
- Async operations for better scalability
- Efficient reflection queries using appropriate BindingFlags
- Lazy evaluation where possible

This implementation provides LLMs and other tools with detailed, structured information about .NET types that enables sophisticated code analysis and documentation generation.