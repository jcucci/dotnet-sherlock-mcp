namespace Sherlock.MCP.Runtime.FrameworkPatterns;

public record EndpointHit(
    string AssemblyPath,
    string Kind,
    string[] HttpMethods,
    string? RouteTemplate,
    string? HandlerTypeFullName,
    string? HandlerMethod,
    string? RegisteredIn,
    string? TypeMetadataName);

public record ServiceRegistrationHit(
    string AssemblyPath,
    string RegisteringTypeFullName,
    string RegisteringMethod,
    string RegistrationMethod,
    string Lifetime,
    string? ServiceTypeFullName,
    string? ImplementationTypeFullName,
    string RegistrationKind,
    bool IsTryAdd,
    bool IsKeyed,
    string? TypeMetadataName);

public record EfEntityHit(
    string AssemblyPath,
    string ContextTypeFullName,
    string PropertyName,
    string EntityTypeFullName,
    string? TypeMetadataName);

public record HandlerHit(
    string AssemblyPath,
    string HandlerTypeFullName,
    string Kind,
    string MessageTypeFullName,
    string? ResponseTypeFullName,
    string InterfaceFullName,
    string? TypeMetadataName);

public record EndpointFilter(string? RouteContains = null, string? HttpMethod = null);

public record ServiceRegistrationFilter(string? ServiceType = null, string? ImplementationType = null, string? Lifetime = null);

public record EfEntityFilter(string? ContextType = null, string? EntityType = null);

public record HandlerFilter(string? MessageType = null, string? Kind = null);
