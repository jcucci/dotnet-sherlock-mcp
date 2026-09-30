using ModelContextProtocol.Protocol;
using Sherlock.MCP.Runtime.Completions;
using Sherlock.MCP.Server.Completions;
using Sherlock.MCP.Server.Shared;

namespace Sherlock.MCP.Tests;

public class CompletionHandlerTests
{
    private readonly RecordingCompletionService _completions = new();

    [Theory]
    [InlineData(ResourceUris.TypeTemplate, "path", "path:x")]
    [InlineData(ResourceUris.DocsTemplate, "path", "path:x")]
    [InlineData(ResourceUris.TypeTemplate, "fullName", "type:/a.dll:x")]
    [InlineData(ResourceUris.DocsTemplate, "memberId", "member:/a.dll:x")]
    [InlineData(ResourceUris.NuGetTemplate, "packageId", "package:x")]
    [InlineData(ResourceUris.NuGetTemplate, "version", "version:pkg:x")]
    public void Complete_DispatchesByTemplateAndArgument(string template, string argument, string expected)
    {
        var context = new Dictionary<string, string> { ["path"] = "/a.dll", ["packageId"] = "pkg" };

        var result = CompletionHandler.Complete(_completions, Request(template, argument, "x", context));

        Assert.Equal([expected], result.Completion.Values);
        Assert.Equal(1, result.Completion.Total);
    }

    [Theory]
    [InlineData(ResourceUris.TypeTemplate, "fullName")]
    [InlineData(ResourceUris.DocsTemplate, "memberId")]
    [InlineData(ResourceUris.NuGetTemplate, "version")]
    public void Complete_MissingDependentArgument_ReturnsEmpty(string template, string argument)
    {
        var result = CompletionHandler.Complete(_completions, Request(template, argument, "x", context: null));

        Assert.Empty(result.Completion.Values);
    }

    [Fact]
    public void Complete_UnknownTemplate_ReturnsEmpty()
    {
        var result = CompletionHandler.Complete(_completions, Request("sherlock://other/{path}", "path", "x", context: null));

        Assert.Empty(result.Completion.Values);
    }

    [Fact]
    public void Complete_PromptReference_ReturnsEmpty()
    {
        var request = new CompleteRequestParams
        {
            Ref = new PromptReference { Name = "anything" },
            Argument = new Argument { Name = "path", Value = "x" }
        };

        Assert.Empty(CompletionHandler.Complete(_completions, request).Completion.Values);
    }

    [Fact]
    public void Complete_ServiceThrows_ReturnsEmpty()
    {
        _completions.Throw = true;

        var result = CompletionHandler.Complete(_completions, Request(ResourceUris.NuGetTemplate, "packageId", "x", context: null));

        Assert.Empty(result.Completion.Values);
    }

    private static CompleteRequestParams Request(string template, string argument, string value, IDictionary<string, string>? context) => new()
    {
        Ref = new ResourceTemplateReference { Uri = template },
        Argument = new Argument { Name = argument, Value = value },
        Context = context is null ? null : new CompleteContext { Arguments = context }
    };

    private sealed class RecordingCompletionService : ICompletionService
    {
        public bool Throw { get; set; }

        public CompletionValues CompleteAssemblyPath(string value) => Respond($"path:{value}");
        public CompletionValues CompleteTypeName(string assemblyPath, string value) => Respond($"type:{assemblyPath}:{value}");
        public CompletionValues CompleteMemberId(string assemblyPath, string value) => Respond($"member:{assemblyPath}:{value}");
        public CompletionValues CompletePackageId(string value) => Respond($"package:{value}");
        public CompletionValues CompletePackageVersion(string packageId, string value) => Respond($"version:{packageId}:{value}");

        private CompletionValues Respond(string value) =>
            Throw ? throw new InvalidOperationException("boom") : new CompletionValues([value], 1, false);
    }
}
