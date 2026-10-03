using System.Text.RegularExpressions;

namespace Sherlock.MCP.Runtime.FrameworkPatterns;

internal static partial class MinimalApiDetector
{
    [GeneratedRegex(@"^Map(?<verb>Get|Post|Put|Delete|Patch|Methods|Group|Fallback)?\d*$")]
    private static partial Regex MapMethod();

    internal static EndpointHit? Detect(string assemblyPath, IlCallSite site)
    {
        if (!IsRouteBuilderExtension(site.Target.DeclaringType)) return null;

        var match = MapMethod().Match(site.Target.MemberName);
        if (!match.Success) return null;

        var verb = match.Groups["verb"].Value;
        var (callerType, callerMethod) = IlCallSiteWalker.UserFacingCaller(site.CallerType, site.CallerMethod);
        var handler = site.PrecedingFunction is { } function
            ? IlCallSiteWalker.UserFacingCaller(function.DeclaringType, function.MemberName)
            : ((string TypeName, string MethodName)?)null;

        return new EndpointHit(
            AssemblyPath: assemblyPath,
            Kind: verb == "Group" ? "minimalApiGroup" : "minimalApi",
            HttpMethods: HttpMethods(verb, site.PrecedingStrings),
            RouteTemplate: site.PrecedingStrings.Count > 0 ? site.PrecedingStrings[0] : null,
            HandlerTypeFullName: handler?.TypeName,
            HandlerMethod: handler?.MethodName,
            RegisteredIn: $"{callerType}.{callerMethod}",
            TypeMetadataName: handler?.TypeName ?? callerType);
    }

    private static bool IsRouteBuilderExtension(string declaringType) =>
        declaringType.EndsWith("EndpointRouteBuilderExtensions", StringComparison.Ordinal) ||
        declaringType.Contains("GeneratedRouteBuilderExtensions", StringComparison.Ordinal);

    private static string[] HttpMethods(string verb, IReadOnlyList<string> strings) => verb switch
    {
        "" or "Group" or "Fallback" => [],
        "Methods" => strings.Skip(1).Select(s => s.ToUpperInvariant()).ToArray(),
        _ => [verb.ToUpperInvariant()]
    };
}
