using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

using Sherlock.MCP.Runtime.Inspection;

namespace Sherlock.MCP.Runtime.ProjectEvaluation;

public sealed class DotnetCliProjectEvaluator : IProjectEvaluator
{
    public const string SdkNotFoundReason = "dotnet SDK not found";

    private const int MaxReasonLength = 300;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    private readonly TimeSpan _timeout;
    private readonly Func<string?> _locateDotnet;

    public DotnetCliProjectEvaluator(TimeSpan? timeout = null, Func<string?>? locateDotnet = null)
    {
        _timeout = timeout ?? DefaultTimeout;
        _locateDotnet = locateDotnet ?? LocateDotnet;
    }

    public async Task<ProjectEvaluationResult> EvaluateAsync(
        string projectPath,
        IReadOnlyDictionary<string, string> globalProperties,
        IReadOnlyList<string> properties,
        IReadOnlyList<string> items,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dotnet = _locateDotnet();
        if (dotnet is null)
            return ProjectEvaluationResult.Failed(SdkNotFoundReason);

        using var process = new Process { StartInfo = BuildStartInfo(dotnet, projectPath, globalProperties, properties, items) };
        try
        {
            process.Start();
            process.StandardInput.Close();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return ProjectEvaluationResult.Failed(SdkNotFoundReason);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout;
            var error = await stderr;
            return process.ExitCode == 0
                ? Parse(output)
                : ProjectEvaluationResult.Failed(FailureReason(output, error, process.ExitCode));
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            cancellationToken.ThrowIfCancellationRequested();
            return ProjectEvaluationResult.Failed($"MSBuild evaluation timed out after {_timeout.TotalSeconds:0}s");
        }
    }

    internal static ProjectEvaluationResult Parse(string output)
    {
        var start = output.IndexOf('{');
        if (start < 0)
            return ProjectEvaluationResult.Failed("MSBuild returned no evaluation output");

        try
        {
            using var document = JsonDocument.Parse(output[start..]);
            var root = document.RootElement;
            return new ProjectEvaluationResult(
                Success: true,
                FailureReason: null,
                Properties: ReadProperties(root),
                Items: ReadItems(root));
        }
        catch (JsonException)
        {
            return ProjectEvaluationResult.Failed("MSBuild returned unreadable evaluation output");
        }
    }

    internal static string? LocateDotnet()
    {
        var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(hostPath) && File.Exists(hostPath))
            return hostPath;

        var executable = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var fromRoot = FrameworkReferenceResolver.DotnetRoots()
            .Where(root => Directory.Exists(Path.Combine(root, "sdk")))
            .Select(root => Path.Combine(root, executable))
            .FirstOrDefault(File.Exists);
        if (fromRoot is not null)
            return fromRoot;

        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, executable))
            .FirstOrDefault(File.Exists);
    }

    private static ProcessStartInfo BuildStartInfo(
        string dotnet,
        string projectPath,
        IReadOnlyDictionary<string, string> globalProperties,
        IReadOnlyList<string> properties,
        IReadOnlyList<string> items)
    {
        var fullPath = Path.GetFullPath(projectPath);
        var startInfo = new ProcessStartInfo(dotnet)
        {
            WorkingDirectory = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(fullPath);
        startInfo.ArgumentList.Add("-nologo");
        foreach (var (name, value) in globalProperties)
            startInfo.ArgumentList.Add($"-property:{name}={value}");
        foreach (var property in ForceJsonOutput(properties, items))
            startInfo.ArgumentList.Add($"-getProperty:{property}");
        foreach (var item in items)
            startInfo.ArgumentList.Add($"-getItem:{item}");

        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        startInfo.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        return startInfo;
    }

    private static IEnumerable<string> ForceJsonOutput(IReadOnlyList<string> properties, IReadOnlyList<string> items) =>
        properties.Count + items.Count >= 2 ? properties : properties.Append("MSBuildProjectFullPath");

    private static Dictionary<string, string> ReadProperties(JsonElement root)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("Properties", out var element) && element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
                properties[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : property.Value.ToString();
        return properties;
    }

    private static Dictionary<string, EvaluatedItem[]> ReadItems(JsonElement root)
    {
        var items = new Dictionary<string, EvaluatedItem[]>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("Items", out var element) || element.ValueKind != JsonValueKind.Object)
            return items;

        foreach (var itemType in element.EnumerateObject())
            items[itemType.Name] = itemType.Value.ValueKind == JsonValueKind.Array
                ? itemType.Value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).Select(ReadItem).ToArray()
                : [];
        return items;
    }

    private static EvaluatedItem ReadItem(JsonElement item)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in item.EnumerateObject())
            metadata[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : property.Value.ToString();
        return new EvaluatedItem(metadata.GetValueOrDefault("Identity", string.Empty), metadata);
    }

    private static string FailureReason(string output, string error, int exitCode)
    {
        var lines = (error + "\n" + output)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var line = lines.FirstOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase))
            ?? lines.FirstOrDefault()
            ?? $"dotnet msbuild exited with code {exitCode}";
        return line.Length <= MaxReasonLength ? line : line[..MaxReasonLength];
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }
    }
}
