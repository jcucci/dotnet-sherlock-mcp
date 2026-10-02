using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Sherlock.MCP.Tests;

internal enum PdbKind
{
    None,
    SideBySide,
    Embedded
}

internal sealed record EmittedAssembly(string AssemblyPath, string DocumentPath, byte[] DocumentBytes);

internal static class PdbFixtures
{
    public const string DocumentDirectory = "/nonexistent-sherlock-build/src/";
    public const string SourceLinkUrlPrefix = "https://raw.githubusercontent.com/acme/widgets/0123abcd/src/";

    public const string WidgetSource = """
        namespace Acme.Widgets;

        public class Widget
        {
            /// <summary>Spins the widget.</summary>
            public int Spin(int turns)
            {
                // the original comment
                return turns * 2;
            }

            public string Name { get; set; } = "widget";
        }
        """;

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static EmittedAssembly Emit(
        PdbKind pdb, bool embedSource = false, string sourceLinkUrlPrefix = SourceLinkUrlPrefix,
        IReadOnlyDictionary<string, string>? documents = null)
    {
        documents ??= new Dictionary<string, string> { ["Widget.cs"] = WidgetSource };
        var directory = TestHandles.NewStateDirectory();
        Directory.CreateDirectory(directory);
        var assemblyName = $"Acme.Widgets.{Guid.NewGuid():N}";
        var assemblyPath = Path.Combine(directory, $"{assemblyName}.dll");
        var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        var texts = documents.ToDictionary(
            document => DocumentDirectory + document.Key,
            document => SourceText.From(document.Value, Utf8, SourceHashAlgorithm.Sha256));
        var trees = texts.Select(text => CSharpSyntaxTree.ParseText(text.Value, path: text.Key)).ToArray();
        var compilation = CSharpCompilation.Create(
            assemblyName, trees, PlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug));

        using var peStream = File.Create(assemblyPath);
        using var pdbStream = pdb == PdbKind.SideBySide ? File.Create(pdbPath) : null;
        using var sourceLink = new MemoryStream(Encoding.UTF8.GetBytes(
            $$$"""{"documents":{"{{{DocumentDirectory}}}*":"{{{sourceLinkUrlPrefix}}}*"}}"""));
        var format = pdb == PdbKind.Embedded ? DebugInformationFormat.Embedded : DebugInformationFormat.PortablePdb;
        var result = compilation.Emit(
            peStream,
            pdbStream: pdbStream,
            options: new EmitOptions(debugInformationFormat: format, pdbFilePath: pdbPath),
            sourceLinkStream: pdb == PdbKind.None ? null : sourceLink,
            embeddedTexts: embedSource ? texts.Select(text => EmbeddedText.FromSource(text.Key, text.Value)).ToArray() : null);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));

        var first = documents.First();
        return new EmittedAssembly(assemblyPath, DocumentDirectory + first.Key, Utf8.GetBytes(first.Value));
    }

    private static IEnumerable<MetadataReference> PlatformReferences() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path) is "System.Runtime.dll" or "System.Private.CoreLib.dll" or "netstandard.dll")
            .Select(path => MetadataReference.CreateFromFile(path));
}
