using System.Text.Json;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.SourceLink;
using Sherlock.MCP.Server.Tools;

namespace Sherlock.MCP.Tests;

public class SourceFetchConfigTests
{
    [Fact]
    public void UpdateRuntimeOptions_ReplacesTheHostListInsteadOfMutatingIt()
    {
        var options = new RuntimeOptions();
        var before = options.SourceFetchHosts;

        ConfigTools.UpdateRuntimeOptions(options, addSourceFetchHosts: ["git.example.com", "GIT.example.com"], removeSourceFetchHosts: ["gitlab.com"]);

        Assert.Equal(RuntimeOptions.KnownSourceHosts, before);
        Assert.Contains("git.example.com", options.SourceFetchHosts);
        Assert.Single(options.SourceFetchHosts, host => host.Equals("git.example.com", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("gitlab.com", options.SourceFetchHosts);
    }

    [Theory]
    [InlineData("off", SourceFetchMode.Off)]
    [InlineData("known-hosts", SourceFetchMode.KnownHosts)]
    [InlineData("any", SourceFetchMode.AnyHost)]
    public void UpdateRuntimeOptions_SetsSourceFetch(string value, SourceFetchMode expected)
    {
        var options = new RuntimeOptions();

        using var doc = JsonDocument.Parse(ConfigTools.UpdateRuntimeOptions(options, sourceFetch: value));

        Assert.Equal(expected, options.SourceFetch);
        Assert.Equal(value, doc.RootElement.GetProperty("data").GetProperty("sourceFetch").GetString());
    }

    [Fact]
    public void UpdateRuntimeOptions_RejectsAnUnknownSourceFetchMode()
    {
        using var doc = JsonDocument.Parse(ConfigTools.UpdateRuntimeOptions(new RuntimeOptions(), sourceFetch: "sometimes"));

        Assert.Equal("InvalidArgument", doc.RootElement.GetProperty("code").GetString());
    }
}
