using System.Diagnostics;
using System.Text.Json;

namespace Sherlock.MCP.IntegrationTests;

public class InotifyWatchTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _out;

    public InotifyWatchTests(Xunit.Abstractions.ITestOutputHelper output) => _out = output;

    [Fact]
    public async Task Server_does_not_create_inotify_watches_on_linux()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var serverDll = Path.Combine(AppContext.BaseDirectory, "Sherlock.MCP.Server.dll");
        Assert.True(File.Exists(serverDll), $"Expected server DLL at {serverDll}");

        var tempDir = Directory.CreateTempSubdirectory("sherlock-inotify-test-").FullName;
        const int subdirCount = 200;

        for (var i = 0; i < subdirCount; i++)
            Directory.CreateDirectory(Path.Combine(tempDir, $"dir{i:D3}"));

        var psi = new ProcessStartInfo("dotnet", $"\"{serverDll}\"")
        {
            WorkingDirectory = tempDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["DOTNET_USE_POLLING_FILE_WATCHER"] = "false";

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start dotnet host for the server.");

        var stderrDrain = Task.Run(async () =>
        {
            try { await proc.StandardError.ReadToEndAsync(); } catch { }
        });

        try
        {
            await DriveServerToReadyAsync(proc, TimeSpan.FromSeconds(20));

            var watches = CountInotifyWatches(proc.Id);
            _out.WriteLine($"inotify watches held by server pid {proc.Id}: {watches} (cwd had {subdirCount} subdirectories).");

            Assert.True(
                watches < 5,
                $"Expected < 5 inotify watches held by the server process, but it is holding {watches}. " +
                $"Working directory had {subdirCount} subdirectories. " +
                $"This indicates a recursive PhysicalFileProvider watcher (e.g. from default Host configuration) is active.");
        }
        finally
        {
            try { proc.StandardInput.Close(); } catch { }
            if (!proc.WaitForExit(3_000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                proc.WaitForExit(3_000);
            }
            try { await stderrDrain; } catch { }
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static async Task DriveServerToReadyAsync(Process proc, TimeSpan timeout)
    {
        const string initializeRequest =
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"sherlock-inotify-test","version":"0.0.1"}}}""";

        await proc.StandardInput.WriteLineAsync(initializeRequest);
        await proc.StandardInput.FlushAsync();

        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            var line = await proc.StandardOutput.ReadLineAsync(cts.Token);
            if (line is null)
                throw new InvalidOperationException("MCP server stdout closed before responding to initialize.");

            if (TryParseResponseId(line, out var id) && id == 1)
                return;
        }

        throw new TimeoutException($"MCP server did not respond to initialize within {timeout.TotalSeconds:F0}s.");
    }

    private static bool TryParseResponseId(string line, out int id)
    {
        id = 0;
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!doc.RootElement.TryGetProperty("id", out var idEl)) return false;
            if (idEl.ValueKind != JsonValueKind.Number) return false;
            return idEl.TryGetInt32(out id);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int CountInotifyWatches(int pid)
    {
        var fdinfoDir = $"/proc/{pid}/fdinfo";
        if (!Directory.Exists(fdinfoDir)) return 0;

        var total = 0;
        foreach (var fdinfo in Directory.EnumerateFiles(fdinfoDir))
        {
            try
            {
                foreach (var line in File.ReadLines(fdinfo))
                    if (line.StartsWith("inotify wd:", StringComparison.Ordinal))
                        total++;
            }
            catch
            {
            }
        }
        return total;
    }
}
