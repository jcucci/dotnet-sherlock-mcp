using System.Reflection;
using ModelContextProtocol.Server;
using Sherlock.MCP.Server.Shared;
using Sherlock.MCP.Server.Tools;

namespace Sherlock.MCP.Tests;

public class ToolProfileTests
{
    private static readonly HashSet<string> RegisteredWireNames = typeof(ConfigTools).Assembly.GetTypes()
        .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
        .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null)
        .Select(m => McpServerTool.Create(m).ProtocolTool.Name)
        .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void NoArgumentsOrEnvironment_DefaultsToFull()
    {
        Assert.True(ToolProfile.TryResolve([], environmentValue: null, out var profile, out _));

        Assert.Same(ToolProfile.Full, profile);
        Assert.False(profile.IsRestricted);
        Assert.True(profile.Includes("get_type_methods"));
    }

    [Theory]
    [InlineData(new[] { "--profile", "core" }, null)]
    [InlineData(new[] { "--profile=CORE" }, null)]
    [InlineData(new string[0], "core")]
    [InlineData(new[] { "--profile", "core" }, "full")]
    public void Core_IsSelectedByFlagOrEnvironment(string[] args, string? environmentValue)
    {
        Assert.True(ToolProfile.TryResolve(args, environmentValue, out var profile, out _));

        Assert.Same(ToolProfile.Core, profile);
    }

    [Fact]
    public void CommandLineFlag_WinsOverEnvironment()
    {
        Assert.True(ToolProfile.TryResolve(["--profile", "full"], "core", out var profile, out _));

        Assert.Same(ToolProfile.Full, profile);
    }

    [Theory]
    [InlineData(new[] { "--profile", "tiny" }, null)]
    [InlineData(new[] { "--profile" }, null)]
    [InlineData(new string[0], "tiny")]
    public void UnknownProfile_ReturnsError(string[] args, string? environmentValue)
    {
        Assert.False(ToolProfile.TryResolve(args, environmentValue, out _, out var error));

        Assert.Contains(ToolProfile.EnvironmentVariable, error);
    }

    [Fact]
    public void CoreTools_AreAllRegistered() =>
        Assert.DoesNotContain(ToolProfile.CoreToolNames, name => !RegisteredWireNames.Contains(name));

    [Fact]
    public void Core_ExcludesDeprecatedMemberTools()
    {
        string[] deprecated =
        [
            "get_type_methods", "get_type_properties", "get_type_fields", "get_type_events",
            "get_type_constructors", "get_all_type_members", "analyze_type"
        ];

        Assert.All(deprecated, name => Assert.False(ToolProfile.Core.Includes(name)));
        Assert.True(ToolProfile.Core.Includes("get_type_members"));
    }
}
