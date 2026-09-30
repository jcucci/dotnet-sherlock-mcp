using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime.Completions;
using Sherlock.MCP.Server.Shared;

namespace Sherlock.MCP.Server.Completions;

public static class CompletionHandler
{
    public static ValueTask<CompleteResult> HandleAsync(RequestContext<CompleteRequestParams> request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Complete(request.Services!.GetRequiredService<ICompletionService>(), request.Params));

    public static CompleteResult Complete(ICompletionService completions, CompleteRequestParams? request)
    {
        if (request?.Ref is not ResourceTemplateReference { Uri: { } templateUri } || request.Argument is null)
            return ToResult(CompletionValues.Empty);

        try
        {
            return ToResult(Dispatch(completions, templateUri, request.Argument, request.Context?.Arguments));
        }
        catch
        {
            return ToResult(CompletionValues.Empty);
        }
    }

    private static CompletionValues Dispatch(ICompletionService completions, string templateUri, Argument argument, IDictionary<string, string>? context)
    {
        var value = argument.Value ?? "";
        return (templateUri, argument.Name) switch
        {
            (ResourceUris.TypeTemplate or ResourceUris.DocsTemplate, "path") => completions.CompleteAssemblyPath(value),
            (ResourceUris.TypeTemplate, "fullName") when ContextValue(context, "path") is { } path => completions.CompleteTypeName(path, value),
            (ResourceUris.DocsTemplate, "memberId") when ContextValue(context, "path") is { } path => completions.CompleteMemberId(path, value),
            (ResourceUris.NuGetTemplate, "packageId") => completions.CompletePackageId(value),
            (ResourceUris.NuGetTemplate, "version") when ContextValue(context, "packageId") is { } packageId => completions.CompletePackageVersion(packageId, value),
            _ => CompletionValues.Empty
        };
    }

    private static string? ContextValue(IDictionary<string, string>? context, string name) =>
        context is not null && context.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static CompleteResult ToResult(CompletionValues values) => new()
    {
        Completion = new Completion
        {
            Values = values.Values,
            Total = values.Total,
            HasMore = values.HasMore
        }
    };
}
