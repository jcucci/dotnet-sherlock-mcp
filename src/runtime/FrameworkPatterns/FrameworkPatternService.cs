using System.Collections.Concurrent;
using System.Reflection;
using Sherlock.MCP.Runtime.Contracts.ReverseLookup;
using Sherlock.MCP.Runtime.Il;
using Sherlock.MCP.Runtime.Inspection;

namespace Sherlock.MCP.Runtime.FrameworkPatterns;

public interface IFrameworkPatternService
{
    EndpointHit[] FindEndpoints(
        string[] assemblyPaths, EndpointFilter filter, ReverseLookupOptions options,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);

    ServiceRegistrationHit[] FindServiceRegistrations(
        string[] assemblyPaths, ServiceRegistrationFilter filter, ReverseLookupOptions options,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);

    EfEntityHit[] FindEfEntities(
        string[] assemblyPaths, EfEntityFilter filter, ReverseLookupOptions options,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);

    HandlerHit[] FindHandlers(
        string[] assemblyPaths, HandlerFilter filter, ReverseLookupOptions options,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);
}

public class FrameworkPatternService : IFrameworkPatternService
{
    private const string DbContextType = "Microsoft.EntityFrameworkCore.DbContext";
    private const string DbSetType = "Microsoft.EntityFrameworkCore.DbSet`1";

    private static readonly Dictionary<string, string> HandlerInterfaces = new(StringComparer.Ordinal)
    {
        ["IRequestHandler`1"] = "request",
        ["IRequestHandler`2"] = "request",
        ["INotificationHandler`1"] = "notification",
        ["IStreamRequestHandler`2"] = "stream",
        ["IPipelineBehavior`2"] = "behavior"
    };

    private static readonly string[] HandlerNamespaces = ["MediatR", "Mediator"];

    private readonly IInspectionContextProvider _contexts;
    private readonly IMetadataReaderProvider _readers;

    public FrameworkPatternService() : this(new SharedInspectionContextProvider(new RuntimeOptions()))
    {
    }

    private FrameworkPatternService(SharedInspectionContextProvider provider) : this(contexts: provider, readers: provider)
    {
    }

    public FrameworkPatternService(IInspectionContextProvider contexts, IMetadataReaderProvider readers)
    {
        _contexts = contexts;
        _readers = readers;
    }

    public EndpointHit[] FindEndpoints(
        string[] assemblyPaths, EndpointFilter filter, ReverseLookupOptions options,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var hits = new ConcurrentBag<EndpointHit>();

        AssemblyScanner.ScanInParallel(_contexts, assemblyPaths, (path, ctx) =>
        {
            foreach (var type in AssemblyScanner.GetScannableTypes(ctx, options, cancellationToken).Where(ControllerEndpointReader.IsController))
                foreach (var hit in ControllerEndpointReader.Read(path, type))
                    hits.Add(hit);

            using var metadata = _readers.AcquireMetadata(path);
            IlCallSiteWalker.Walk(metadata, site =>
            {
                if (MinimalApiDetector.Detect(path, site) is { } hit) hits.Add(hit);
            }, cancellationToken);
        }, progress, cancellationToken);

        return hits
            .Where(h => MatchesEndpoint(h, filter))
            .OrderBy(h => h.AssemblyPath, StringComparer.Ordinal)
            .ThenBy(h => h.Kind, StringComparer.Ordinal)
            .ThenBy(h => h.RouteTemplate, StringComparer.Ordinal)
            .ThenBy(h => string.Join(",", h.HttpMethods), StringComparer.Ordinal)
            .ThenBy(h => h.HandlerTypeFullName, StringComparer.Ordinal)
            .ThenBy(h => h.HandlerMethod, StringComparer.Ordinal)
            .ThenBy(h => h.RegisteredIn, StringComparer.Ordinal)
            .ToArray();
    }

    public ServiceRegistrationHit[] FindServiceRegistrations(
        string[] assemblyPaths, ServiceRegistrationFilter filter, ReverseLookupOptions options,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var hits = new ConcurrentBag<ServiceRegistrationHit>();

        AssemblyScanner.ScanMetadataInParallel(_readers, assemblyPaths, (path, metadata) =>
            IlCallSiteWalker.Walk(metadata, site =>
            {
                if (ServiceRegistrationDetector.Detect(path, metadata, site) is { } hit) hits.Add(hit);
            }, cancellationToken), progress, cancellationToken);

        return hits
            .Where(h => MatchesRegistration(h, filter, options.CaseSensitive))
            .OrderBy(h => h.AssemblyPath, StringComparer.Ordinal)
            .ThenBy(h => h.ServiceTypeFullName, StringComparer.Ordinal)
            .ThenBy(h => h.ImplementationTypeFullName, StringComparer.Ordinal)
            .ThenBy(h => h.RegisteringTypeFullName, StringComparer.Ordinal)
            .ThenBy(h => h.RegisteringMethod, StringComparer.Ordinal)
            .ThenBy(h => h.RegistrationMethod, StringComparer.Ordinal)
            .ThenBy(h => h.RegistrationKind, StringComparer.Ordinal)
            .ToArray();
    }

    public EfEntityHit[] FindEfEntities(
        string[] assemblyPaths, EfEntityFilter filter, ReverseLookupOptions options,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var hits = new ConcurrentBag<EfEntityHit>();

        AssemblyScanner.ScanInParallel(_contexts, assemblyPaths, (path, ctx) =>
        {
            foreach (var context in AssemblyScanner.GetScannableTypes(ctx, options, cancellationToken).Where(IsDbContext))
            {
                if (filter.ContextType is not null && !TypeNameMatcher.Matches(context, filter.ContextType, options.CaseSensitive)) continue;
                foreach (var hit in DbSets(path, context, filter, options.CaseSensitive))
                    hits.Add(hit);
            }
        }, progress, cancellationToken);

        return hits
            .OrderBy(h => h.AssemblyPath, StringComparer.Ordinal)
            .ThenBy(h => h.ContextTypeFullName, StringComparer.Ordinal)
            .ThenBy(h => h.PropertyName, StringComparer.Ordinal)
            .ToArray();
    }

    public HandlerHit[] FindHandlers(
        string[] assemblyPaths, HandlerFilter filter, ReverseLookupOptions options,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var hits = new ConcurrentBag<HandlerHit>();

        AssemblyScanner.ScanInParallel(_contexts, assemblyPaths, (path, ctx) =>
        {
            foreach (var type in AssemblyScanner.GetScannableTypes(ctx, options, cancellationToken))
            {
                if (!type.IsClass || type.IsAbstract) continue;
                foreach (var hit in Handlers(path, type))
                    if (MatchesHandler(hit.Hit, hit.Message, filter, options.CaseSensitive)) hits.Add(hit.Hit);
            }
        }, progress, cancellationToken);

        return hits
            .OrderBy(h => h.AssemblyPath, StringComparer.Ordinal)
            .ThenBy(h => h.Kind, StringComparer.Ordinal)
            .ThenBy(h => h.MessageTypeFullName, StringComparer.Ordinal)
            .ThenBy(h => h.HandlerTypeFullName, StringComparer.Ordinal)
            .ThenBy(h => h.InterfaceFullName, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsDbContext(Type type) =>
        type.IsClass && AssemblyScanner.GetBaseTypeChain(type).Any(b => b.FullName == DbContextType);

    private static IEnumerable<EfEntityHit> DbSets(string path, Type context, EfEntityFilter filter, bool caseSensitive)
    {
        foreach (var property in AssemblyScanner.GetPropertiesSafe(context, BindingFlags.Public | BindingFlags.Instance))
        {
            Type propertyType;
            try { propertyType = property.PropertyType; }
            catch { continue; }
            if (!propertyType.IsGenericType || GenericDefinitionName(propertyType) != DbSetType) continue;

            var entity = propertyType.GetGenericArguments()[0];
            if (filter.EntityType is not null && !TypeNameMatcher.Matches(entity, filter.EntityType, caseSensitive)) continue;

            yield return new EfEntityHit(
                AssemblyPath: path,
                ContextTypeFullName: TypeNameFormatter.FriendlyFullName(context),
                PropertyName: property.Name,
                EntityTypeFullName: TypeNameFormatter.FriendlyFullName(entity),
                TypeMetadataName: context.FullName);
        }
    }

    private static IEnumerable<(HandlerHit Hit, Type Message)> Handlers(string path, Type type)
    {
        foreach (var iface in AssemblyScanner.GetInterfacesSafe(type))
        {
            if (!iface.IsGenericType || !HandlerNamespaces.Contains(iface.Namespace)) continue;
            if (!HandlerInterfaces.TryGetValue(iface.Name, out var kind)) continue;

            Type[] arguments;
            try { arguments = iface.GetGenericArguments(); }
            catch { continue; }

            var hit = new HandlerHit(
                AssemblyPath: path,
                HandlerTypeFullName: TypeNameFormatter.FriendlyFullName(type),
                Kind: kind,
                MessageTypeFullName: TypeNameFormatter.FriendlyFullName(arguments[0]),
                ResponseTypeFullName: arguments.Length > 1 ? TypeNameFormatter.FriendlyFullName(arguments[1]) : null,
                InterfaceFullName: TypeNameFormatter.FriendlyFullName(iface),
                TypeMetadataName: type.FullName);
            yield return (hit, arguments[0]);
        }
    }

    private static string? GenericDefinitionName(Type type)
    {
        try { return type.GetGenericTypeDefinition().FullName; }
        catch { return null; }
    }

    private static bool MatchesEndpoint(EndpointHit hit, EndpointFilter filter)
    {
        if (filter.RouteContains is { Length: > 0 } fragment &&
            hit.RouteTemplate?.Contains(fragment, StringComparison.OrdinalIgnoreCase) != true) return false;

        return filter.HttpMethod is not { Length: > 0 } verb ||
               hit.HttpMethods.Length == 0 ||
               hit.HttpMethods.Contains(verb, StringComparer.OrdinalIgnoreCase);
    }

    private static bool MatchesRegistration(ServiceRegistrationHit hit, ServiceRegistrationFilter filter, bool caseSensitive)
    {
        if (filter.Lifetime is { Length: > 0 } lifetime && !hit.Lifetime.Equals(lifetime, StringComparison.OrdinalIgnoreCase)) return false;
        if (filter.ServiceType is { Length: > 0 } service && !MatchesMetadataName(hit.ServiceTypeFullName, service, caseSensitive)) return false;
        return filter.ImplementationType is not { Length: > 0 } implementation ||
               MatchesMetadataName(hit.ImplementationTypeFullName, implementation, caseSensitive);
    }

    private static bool MatchesHandler(HandlerHit hit, Type message, HandlerFilter filter, bool caseSensitive)
    {
        if (filter.Kind is { Length: > 0 } kind && !hit.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)) return false;
        return filter.MessageType is not { Length: > 0 } messageType || TypeNameMatcher.Matches(message, messageType, caseSensitive);
    }

    internal static bool MatchesMetadataName(string? candidate, string userSuppliedName, bool caseSensitive)
    {
        if (candidate is null) return false;
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (candidate.Equals(userSuppliedName, comparison)) return true;

        var genericStart = candidate.IndexOf('<');
        var openName = genericStart > 0 ? candidate[..genericStart] : candidate;
        return MetadataTypeNameMatcher.Matches(openName, userSuppliedName, caseSensitive);
    }
}
