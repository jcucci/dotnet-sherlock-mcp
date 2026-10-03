using System.Text.Json.Nodes;

namespace Sherlock.MCP.Tests;

internal sealed class AssetsFixture : IDisposable
{
    public AssetsFixture()
        : this(Path.Combine(Path.GetTempPath(), $"sherlock_assets_{Guid.NewGuid():N}"))
    {
    }

    public AssetsFixture(string root)
    {
        Root = root;
        PackageFolder = Path.Combine(Root, "packages") + Path.DirectorySeparatorChar;
        ProjectDirectory = Path.Combine(Root, "Sample");
        ProjectFile = Path.Combine(ProjectDirectory, "Sample.csproj");
        AssetsPath = Path.Combine(ProjectDirectory, "obj", "project.assets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(AssetsPath)!);
        Directory.CreateDirectory(PackageFolder);
        File.WriteAllText(ProjectFile, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
    }

    public string Root { get; }

    public string PackageFolder { get; }

    public string ProjectDirectory { get; }

    public string ProjectFile { get; }

    public string AssetsPath { get; }

    public string BinDirectory(params string[] pivot) =>
        Directory.CreateDirectory(Path.Combine([ProjectDirectory, "bin", .. pivot])).FullName;

    public string AddPackageFile(string id, string version, string asset, string? sourceFile = null)
    {
        var path = Path.GetFullPath(Path.Combine(PackageFolder, id.ToLowerInvariant(), version, asset));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (sourceFile != null) File.Copy(sourceFile, path, overwrite: true);
        else File.WriteAllBytes(path, []);
        return path;
    }

    public string CopyInto(string directory, string sourceFile)
    {
        var destination = Path.Combine(directory, Path.GetFileName(sourceFile));
        File.Copy(sourceFile, destination, overwrite: true);
        return destination;
    }

    public void WriteAssets(JsonObject targets, JsonObject libraries, JsonObject frameworks) =>
        File.WriteAllText(AssetsPath, new JsonObject
        {
            ["version"] = 3,
            ["targets"] = targets,
            ["libraries"] = libraries,
            ["packageFolders"] = new JsonObject { [PackageFolder] = new JsonObject() },
            ["project"] = new JsonObject
            {
                ["version"] = "1.0.0",
                ["restore"] = new JsonObject { ["projectName"] = "Sample", ["projectPath"] = ProjectFile },
                ["frameworks"] = frameworks
            }
        }.ToJsonString());

    public static JsonObject Package(string? compile = null, string? runtime = null, JsonObject? dependencies = null)
    {
        var library = new JsonObject { ["type"] = "package" };
        if (dependencies != null) library["dependencies"] = dependencies;
        if (compile != null) library["compile"] = new JsonObject { [compile] = new JsonObject() };
        if (runtime != null) library["runtime"] = new JsonObject { [runtime] = new JsonObject() };
        return library;
    }

    public static JsonObject Library(string type, string path) => new() { ["type"] = type, ["path"] = path };

    public static JsonObject Framework(string alias, params (string Id, string Range)[] dependencies)
    {
        var deps = new JsonObject();
        foreach (var (id, range) in dependencies)
            deps[id] = new JsonObject { ["target"] = "Package", ["version"] = range };
        return new JsonObject { ["targetAlias"] = alias, ["dependencies"] = deps };
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
