using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Sherlock.MCP.Server.Shared;
using Sherlock.MCP.Server.Tools;

namespace Sherlock.MCP.Tests;

public class ClaudePluginTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string PluginRoot = Path.Combine(RepoRoot, "plugins", "sherlock");
    private static readonly string SkillText = File.ReadAllText(Path.Combine(PluginRoot, "skills", "sherlock", "SKILL.md"));

    private static readonly Regex ToolLikeToken = new(@"\b(?:get|find|search|analyze|resolve|update|decompile|compare)_[a-z_]+(?![a-z_*])", RegexOptions.Compiled);

    [Fact]
    public void Skill_HasNameAndDescriptionFrontmatter()
    {
        var match = Regex.Match(SkillText, @"\A---\r?\n(?<body>.*?)\r?\n---", RegexOptions.Singleline);
        Assert.True(match.Success, "SKILL.md must start with YAML frontmatter.");

        var frontmatter = match.Groups["body"].Value;
        Assert.Matches(@"(?m)^name:\s*sherlock\s*$", frontmatter);
        Assert.Matches(@"(?m)^description:\s*\S.{40,}$", frontmatter);
    }

    [Fact]
    public void Skill_OnlyReferencesCoreProfileTools()
    {
        var referenced = ToolLikeToken.Matches(SkillText).Select(m => m.Value).Distinct().ToArray();
        var unknown = referenced.Except(ToolProfile.CoreToolNames).ToArray();

        Assert.NotEmpty(referenced);
        Assert.True(unknown.Length == 0, $"SKILL.md references tools outside the core profile: {string.Join(", ", unknown)}");
    }

    [Fact]
    public void Skill_DoesNotUsePascalCaseToolNames()
    {
        var pascalCaseNames = typeof(ConfigTools).Assembly.GetTypes()
            .Where(t => t.GetCustomAttributes(typeof(McpServerToolTypeAttribute), inherit: false).Length > 0)
            .SelectMany(t => t.GetMethods())
            .Where(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false).Length > 0)
            .Select(m => m.Name)
            .Distinct();

        var found = pascalCaseNames.Where(name => Regex.IsMatch(SkillText, $@"\b{name}\b")).ToArray();

        Assert.True(found.Length == 0, $"SKILL.md uses PascalCase tool names: {string.Join(", ", found)}");
    }

    [Fact]
    public void PluginVersions_MatchServerProjectVersion()
    {
        var projectVersion = ReadProjectVersion();

        using var plugin = ReadJson(Path.Combine(PluginRoot, ".claude-plugin", "plugin.json"));
        using var marketplace = ReadJson(Path.Combine(RepoRoot, ".claude-plugin", "marketplace.json"));
        var marketplaceEntry = marketplace.RootElement.GetProperty("plugins").EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == "sherlock");

        Assert.Equal(projectVersion, plugin.RootElement.GetProperty("version").GetString());
        Assert.Equal(projectVersion, marketplaceEntry.GetProperty("version").GetString());
        Assert.Equal($"Sherlock.MCP.Server@{projectVersion}", ReadServerArgs().Last());
    }

    [Fact]
    public void McpConfig_DefaultsToCoreProfile()
    {
        var profile = ReadServerEntry().GetProperty("env").GetProperty(ToolProfile.EnvironmentVariable).GetString();

        Assert.Equal($"${{{ToolProfile.EnvironmentVariable}:-core}}", profile);
    }

    [Fact]
    public void McpConfig_PassesDnxOptionsBeforePackageId()
    {
        var args = ReadServerArgs();

        Assert.StartsWith("Sherlock.MCP.Server@", args[^1]);
        Assert.Equal(["-v", "q", "--yes"], args[..^1]);
    }

    private static string[] ReadServerArgs() =>
        ReadServerEntry().GetProperty("args").EnumerateArray().Select(a => a.GetString()!).ToArray();

    private static JsonElement ReadServerEntry()
    {
        using var config = ReadJson(Path.Combine(PluginRoot, ".mcp.json"));
        return config.RootElement.GetProperty("sherlock").Clone();
    }

    private static JsonDocument ReadJson(string path) => JsonDocument.Parse(File.ReadAllText(path));

    private static string ReadProjectVersion()
    {
        var csproj = File.ReadAllText(Path.Combine(RepoRoot, "src", "server", "Sherlock.MCP.Server.csproj"));
        return Regex.Match(csproj, "<Version>([^<]+)</Version>").Groups[1].Value;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "Sherlock.MCP.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
