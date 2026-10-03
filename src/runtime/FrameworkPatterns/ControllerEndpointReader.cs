using System.Reflection;
using System.Text.RegularExpressions;

namespace Sherlock.MCP.Runtime.FrameworkPatterns;

internal static partial class ControllerEndpointReader
{
    private const string MvcNamespace = "Microsoft.AspNetCore.Mvc";
    private const string ControllerBaseType = "Microsoft.AspNetCore.Mvc.ControllerBase";
    private const string RouteAttributeType = "Microsoft.AspNetCore.Mvc.RouteAttribute";
    private const string AcceptVerbsAttributeType = "Microsoft.AspNetCore.Mvc.AcceptVerbsAttribute";

    [GeneratedRegex(@"^Microsoft\.AspNetCore\.Mvc\.Http(?<verb>Get|Post|Put|Delete|Patch|Head|Options)Attribute$")]
    private static partial Regex HttpVerbAttribute();

    [GeneratedRegex(@"\[(controller|action)\]", RegexOptions.IgnoreCase)]
    private static partial Regex RouteToken();

    private sealed record RouteSpec(string[] HttpMethods, string? Template);

    internal static bool IsController(Type type)
    {
        if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters) return false;
        if (HasAttribute(type, $"{MvcNamespace}.NonControllerAttribute")) return false;

        var lineage = AssemblyScanner.GetBaseTypeChain(type).Prepend(type).ToArray();
        return lineage.Any(t => t.FullName == ControllerBaseType) ||
               lineage.Any(t => HasAttribute(t, $"{MvcNamespace}.ControllerAttribute") || HasAttribute(t, $"{MvcNamespace}.ApiControllerAttribute"));
    }

    internal static IEnumerable<EndpointHit> Read(string assemblyPath, Type controller)
    {
        var controllerName = TrimSuffix(controller.Name, "Controller");
        var classTemplates = ClassRouteTemplates(controller);
        var controllerFullName = TypeNameFormatter.FriendlyFullName(controller);

        foreach (var action in Actions(controller))
        {
            var actionName = TrimSuffix(action.Name, "Async");
            foreach (var spec in ActionRoutes(action, classTemplates))
            {
                yield return new EndpointHit(
                    AssemblyPath: assemblyPath,
                    Kind: "controller",
                    HttpMethods: spec.HttpMethods,
                    RouteTemplate: spec.Template is null ? null : ReplaceTokens(spec.Template, controllerName, actionName),
                    HandlerTypeFullName: controllerFullName,
                    HandlerMethod: action.Name,
                    RegisteredIn: null,
                    TypeMetadataName: controller.FullName);
            }
        }
    }

    private static IEnumerable<MethodInfo> Actions(Type controller)
    {
        MethodInfo[] methods;
        try { methods = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance); }
        catch { yield break; }

        foreach (var method in methods)
        {
            if (method.IsSpecialName || method.IsGenericMethodDefinition) continue;
            var declaring = method.DeclaringType;
            if (declaring is null || declaring.FullName == "System.Object") continue;
            if (declaring.Namespace?.StartsWith(MvcNamespace, StringComparison.Ordinal) == true) continue;
            if (HasAttribute(method, $"{MvcNamespace}.NonActionAttribute")) continue;
            yield return method;
        }
    }

    private static string?[] ClassRouteTemplates(Type controller)
    {
        foreach (var type in AssemblyScanner.GetBaseTypeChain(controller).Prepend(controller))
        {
            var templates = Attributes(type)
                .Where(a => AttributeName(a) == RouteAttributeType)
                .Select(a => FirstStringArgument(a))
                .ToArray();
            if (templates.Length > 0) return templates;
        }
        return [null];
    }

    private static IEnumerable<RouteSpec> ActionRoutes(MethodInfo action, string?[] classTemplates)
    {
        var attributes = Attributes(action);
        var methodTemplates = attributes
            .Where(a => AttributeName(a) == RouteAttributeType)
            .Select(a => FirstStringArgument(a))
            .ToArray();
        var verbRoutes = attributes.Select(VerbRoute).OfType<RouteSpec>().ToArray();

        IEnumerable<RouteSpec> actionSpecs = verbRoutes.Length switch
        {
            0 when methodTemplates.Length == 0 => [new RouteSpec([], null)],
            0 => methodTemplates.Select(t => new RouteSpec([], t)),
            _ => verbRoutes.SelectMany(v => v.Template is not null || methodTemplates.Length == 0
                ? [v]
                : methodTemplates.Select(t => v with { Template = t }))
        };

        return actionSpecs.SelectMany(spec => IsAbsolute(spec.Template)
            ? [spec with { Template = spec.Template!.TrimStart('~') }]
            : classTemplates.Select(c => spec with { Template = Combine(c, spec.Template) }));
    }

    private static RouteSpec? VerbRoute(CustomAttributeData attribute)
    {
        var name = AttributeName(attribute);
        if (name is null) return null;

        var verb = HttpVerbAttribute().Match(name);
        if (verb.Success)
            return new RouteSpec([verb.Groups["verb"].Value.ToUpperInvariant()], FirstStringArgument(attribute) ?? NamedString(attribute, "Template"));

        return name == AcceptVerbsAttributeType
            ? new RouteSpec(AcceptedVerbs(attribute), NamedString(attribute, "Route"))
            : null;
    }

    private static string[] AcceptedVerbs(CustomAttributeData attribute) =>
        attribute.ConstructorArguments
            .SelectMany(arg => arg.Value switch
            {
                string single => [single],
                IEnumerable<CustomAttributeTypedArgument> many => many.Select(m => m.Value as string).OfType<string>(),
                _ => []
            })
            .Select(v => v.ToUpperInvariant())
            .ToArray();

    private static bool IsAbsolute(string? template) =>
        template is not null && (template.StartsWith('/') || template.StartsWith("~/", StringComparison.Ordinal));

    private static string? Combine(string? prefix, string? template)
    {
        if (string.IsNullOrEmpty(prefix)) return template;
        if (string.IsNullOrEmpty(template)) return prefix;
        return $"{prefix.TrimEnd('/')}/{template.TrimStart('/')}";
    }

    private static string ReplaceTokens(string template, string controllerName, string actionName) =>
        RouteToken().Replace(template, m => m.Groups[1].Value.Equals("controller", StringComparison.OrdinalIgnoreCase) ? controllerName : actionName);

    private static string TrimSuffix(string name, string suffix) =>
        name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal) ? name[..^suffix.Length] : name;

    private static string? FirstStringArgument(CustomAttributeData attribute) =>
        attribute.ConstructorArguments.Select(a => a.Value).OfType<string>().FirstOrDefault();

    private static string? NamedString(CustomAttributeData attribute, string name) =>
        attribute.NamedArguments.FirstOrDefault(n => n.MemberName == name).TypedValue.Value as string;

    private static bool HasAttribute(MemberInfo member, string attributeFullName) =>
        Attributes(member).Any(a => AttributeName(a) == attributeFullName);

    private static string? AttributeName(CustomAttributeData attribute)
    {
        try { return attribute.AttributeType.FullName; }
        catch { return null; }
    }

    private static IList<CustomAttributeData> Attributes(MemberInfo member)
    {
        try { return member.GetCustomAttributesData(); }
        catch { return []; }
    }
}
