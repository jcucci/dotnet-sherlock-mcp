using System.Text.RegularExpressions;
using Sherlock.MCP.Runtime.Inspection;

namespace Sherlock.MCP.Runtime.FrameworkPatterns;

internal static partial class ServiceRegistrationDetector
{
    private const string DependencyInjectionNamespace = "Microsoft.Extensions.DependencyInjection";
    private const string ServiceDescriptorType = "Microsoft.Extensions.DependencyInjection.ServiceDescriptor";
    private const string HostedServiceType = "Microsoft.Extensions.Hosting.IHostedService";

    [GeneratedRegex(@"^(?<try>Try)?Add(?<keyed>Keyed)?(?<lifetime>Singleton|Scoped|Transient)$")]
    private static partial Regex CollectionMethod();

    [GeneratedRegex(@"^(?<keyed>Keyed)?(?<lifetime>Singleton|Scoped|Transient)$")]
    private static partial Regex DescriptorMethod();

    internal static ServiceRegistrationHit? Detect(string assemblyPath, MetadataReaderLease metadata, IlCallSite site)
    {
        var shape = Classify(site.Target);
        if (shape is null) return null;

        var genericArguments = IlCallSiteWalker.GenericArguments(metadata, site.Token);
        var parameters = SignificantParameters(IlCallSiteWalker.ParameterTypes(metadata, site.Token), shape.Value.IsKeyed);
        var typeParameterCount = parameters.Count(p => p == "System.Type");
        var kind = RegistrationKind(parameters, typeParameterCount);
        var (service, implementation) = shape.Value.IsHostedService
            ? (HostedServiceType, kind == "type" ? genericArguments.FirstOrDefault() : null)
            : ServiceAndImplementation(genericArguments, site.PrecedingTypeTokens, typeParameterCount, kind);
        var (callerType, callerMethod) = IlCallSiteWalker.UserFacingCaller(site.CallerType, site.CallerMethod);

        return new ServiceRegistrationHit(
            AssemblyPath: assemblyPath,
            RegisteringTypeFullName: callerType,
            RegisteringMethod: callerMethod,
            RegistrationMethod: shape.Value.DisplayName,
            Lifetime: shape.Value.Lifetime,
            ServiceTypeFullName: service,
            ImplementationTypeFullName: implementation,
            RegistrationKind: kind,
            IsTryAdd: shape.Value.IsTryAdd,
            IsKeyed: shape.Value.IsKeyed,
            TypeMetadataName: callerType);
    }

    private readonly record struct RegistrationShape(string DisplayName, string Lifetime, bool IsTryAdd, bool IsKeyed, bool IsHostedService);

    private static RegistrationShape? Classify(Il.ResolvedMember target)
    {
        if (!target.DeclaringType.StartsWith(DependencyInjectionNamespace, StringComparison.Ordinal)) return null;

        if (target.DeclaringType == ServiceDescriptorType)
        {
            var descriptor = DescriptorMethod().Match(target.MemberName);
            return descriptor.Success
                ? new RegistrationShape($"ServiceDescriptor.{target.MemberName}", Lifetime(descriptor), IsTryAdd: false, descriptor.Groups["keyed"].Success, IsHostedService: false)
                : null;
        }

        if (target.MemberName == "AddHostedService")
            return new RegistrationShape(target.MemberName, "singleton", IsTryAdd: false, IsKeyed: false, IsHostedService: true);

        var match = CollectionMethod().Match(target.MemberName);
        return match.Success
            ? new RegistrationShape(target.MemberName, Lifetime(match), match.Groups["try"].Success, match.Groups["keyed"].Success, IsHostedService: false)
            : null;
    }

    private static string Lifetime(Match match) => match.Groups["lifetime"].Value.ToLowerInvariant();

    private static List<string> SignificantParameters(IReadOnlyList<string> parameters, bool isKeyed)
    {
        var significant = parameters.Where(p => !p.EndsWith("IServiceCollection", StringComparison.Ordinal)).ToList();
        if (isKeyed)
        {
            var keyIndex = significant.IndexOf("System.Object");
            if (keyIndex >= 0) significant.RemoveAt(keyIndex);
        }
        return significant;
    }

    private static string RegistrationKind(List<string> parameters, int typeParameterCount)
    {
        if (parameters.Any(p => p.StartsWith("System.Func<", StringComparison.Ordinal))) return "factory";
        var last = parameters.LastOrDefault();
        if (last == "!!0" || (typeParameterCount == 1 && last == "System.Object")) return "instance";
        return "type";
    }

    private static (string? Service, string? Implementation) ServiceAndImplementation(
        IReadOnlyList<string> genericArguments, IReadOnlyList<string> typeTokens, int typeParameterCount, string kind)
    {
        var types = genericArguments.Count > 0
            ? genericArguments
            : typeParameterCount > 0 && typeTokens.Count >= typeParameterCount ? typeTokens.TakeLast(typeParameterCount).ToArray() : [];

        if (types.Count == 0) return (null, null);
        if (types.Count >= 2) return (types[0], types[1]);
        return (types[0], kind == "type" ? types[0] : null);
    }
}
