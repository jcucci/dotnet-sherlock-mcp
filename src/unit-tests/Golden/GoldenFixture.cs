using System.Runtime.InteropServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Sherlock.MCP.Runtime.Inspection;

namespace Sherlock.MCP.Tests.Golden;

internal sealed class GoldenFixture
{
    public const string AssemblyName = "Sherlock.Golden";
    public const string DocumentPath = "/_/Golden/Shapes.cs";

    public const string Source = """
        using System;
        using System.Reflection;
        using System.Threading.Tasks;

        [assembly: AssemblyVersion("1.2.3.0")]
        [assembly: AssemblyInformationalVersion("1.2.3-golden")]

        namespace Golden.Shapes
        {
            /// <summary>Tags a member for golden tests.</summary>
            [AttributeUsage(AttributeTargets.All)]
            public sealed class TagAttribute : Attribute
            {
                /// <summary>Creates a tag.</summary>
                /// <param name="name">The tag name.</param>
                public TagAttribute(string name) { Name = name; }

                /// <summary>The tag name.</summary>
                public string Name { get; }

                /// <summary>Relative weight of the tag.</summary>
                public int Weight { get; set; }
            }

            /// <summary>A source of orders.</summary>
            public interface IOrderSource
            {
                /// <summary>Returns the next order.</summary>
                Order Next();
            }

            /// <summary>A customer order.</summary>
            public class Order
            {
                /// <summary>Order id.</summary>
                public int Id { get; set; }

                /// <summary>Order total.</summary>
                public decimal Total { get; set; }
            }

            /// <summary>Processes orders in batches.</summary>
            /// <remarks>Used by the golden-file tests.</remarks>
            [Tag("processor", Weight = 2)]
            public class OrderProcessor : IOrderSource
            {
                /// <summary>Largest batch size.</summary>
                public const int MaxBatch = 10;

                /// <summary>Name used when none is given.</summary>
                public static readonly string DefaultName = "orders";

                private Order[] _orders = new Order[0];

                /// <summary>Creates a processor with the default name.</summary>
                public OrderProcessor() : this(DefaultName) { }

                /// <summary>Creates a named processor.</summary>
                /// <param name="name">The processor name.</param>
                public OrderProcessor(string name) { Name = name; }

                /// <summary>The processor name.</summary>
                public string Name { get; set; }

                /// <summary>Number of queued orders.</summary>
                public int Count => _orders.Length;

                /// <summary>Raised after an order is added.</summary>
                public event EventHandler? Processed;

                /// <summary>Queues an order.</summary>
                /// <param name="order">The order to queue.</param>
                [Tag("add")]
                public void Add(Order order)
                {
                    var next = new Order[_orders.Length + 1];
                    Array.Copy(_orders, next, _orders.Length);
                    next[_orders.Length] = order;
                    _orders = next;
                    Processed?.Invoke(this, EventArgs.Empty);
                }

                /// <summary>Queues a new order.</summary>
                /// <param name="id">The order id.</param>
                /// <param name="total">The order total.</param>
                public void Add(int id, [Tag("total")] decimal total) => Add(new Order { Id = id, Total = total });

                /// <summary>Applies a discount.</summary>
                /// <param name="percent">The discount percentage.</param>
                public decimal Discount([Tag("percent")] decimal percent) => percent;

                /// <summary>Returns the oldest queued order.</summary>
                public Order Next() => _orders[0];

                /// <summary>Clears the queue.</summary>
                /// <returns>The number of orders that were queued.</returns>
                public async Task<int> FlushAsync()
                {
                    await Task.Yield();
                    var count = _orders.Length;
                    _orders = new Order[0];
                    return count;
                }

                /// <summary>A batch of orders.</summary>
                public class Batch
                {
                    /// <summary>Batch size.</summary>
                    public int Size { get; set; }
                }
            }

            /// <summary>A processor that handles urgent orders first.</summary>
            public class PriorityProcessor : OrderProcessor
            {
                /// <summary>Creates a priority processor.</summary>
                public PriorityProcessor() : base("priority") { }
            }

            /// <summary>Order helpers.</summary>
            public static class OrderExtensions
            {
                /// <summary>Returns the next order from a processor.</summary>
                /// <param name="processor">The processor.</param>
                public static Order First(this OrderProcessor processor) => processor.Next();

                /// <summary>Describes an order.</summary>
                /// <param name="order">The order.</param>
                public static string Describe(this Order order) => "#" + order.Id;
            }

            /// <summary>Creates entities.</summary>
            /// <typeparam name="T">The entity type.</typeparam>
            public class Repository<T> where T : class, new()
            {
                /// <summary>Creates an entity.</summary>
                public T Create() => new T();
            }
        }

        namespace Golden.Shapes.Legacy
        {
            /// <summary>The pre-2.0 order shape.</summary>
            public class Order
            {
                /// <summary>Order reference.</summary>
                public string Reference { get; set; } = "";
            }
        }
        """;

    private static readonly Lazy<GoldenFixture> Instance = new(() => new GoldenFixture());
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private GoldenFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "sherlock-golden", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteRoot();
        AssemblyPath = Emit(Root);
        NotDotNetPath = Path.Combine(Root, "native.dll");
        File.WriteAllBytes(NotDotNetPath, Encoding.ASCII.GetBytes("this is not a portable executable"));
        BadImageMessage = ReadBadImageMessage(NotDotNetPath);
        ApiDiffLeftPath = ApiDiffFixtures.Emit(ApiDiffFixtures.Version1, Path.Combine(Root, "left"));
        ApiDiffRightPath = ApiDiffFixtures.Emit(ApiDiffFixtures.Version2, Path.Combine(Root, "right"));
        Project = GoldenProject.Create(Path.Combine(Root, "project"));
    }

    public static GoldenFixture Shared => Instance.Value;

    public string Root { get; }

    public string AssemblyPath { get; }

    public string NotDotNetPath { get; }

    public string BadImageMessage { get; }

    public string ApiDiffLeftPath { get; }

    public string ApiDiffRightPath { get; }

    public GoldenProject Project { get; }

    public IReadOnlyList<(string Value, string Placeholder)> Scrubs =>
    [
        (NuGetCacheProbe.GetCacheRoot().TrimEnd(Path.DirectorySeparatorChar), "{NuGetCache}"),
        (RuntimeEnvironment.GetRuntimeDirectory().TrimEnd(Path.DirectorySeparatorChar), "{HostRuntime}"),
        (Root, "{Fixture}"),
        ($"{Environment.Version.Major}.0.0.0", "{FrameworkAssemblyVersion}"),
        (Environment.Version.ToString(), "{FrameworkVersion}"),
        (BadImageMessage, "{BadImageMessage}")
    ];

    public string StateDirectory => Path.Combine(Root, "state");

    private void DeleteRoot()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string ReadBadImageMessage(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new PEReader(stream);
            reader.GetMetadataReader();
        }
        catch (BadImageFormatException ex)
        {
            return ex.Message;
        }
        throw new InvalidOperationException($"{path} unexpectedly loaded as a .NET assembly");
    }

    private static string Emit(string directory)
    {
        var assemblyPath = Path.Combine(directory, $"{AssemblyName}.dll");
        var text = SourceText.From(Source, Utf8, SourceHashAlgorithm.Sha256);
        var tree = CSharpSyntaxTree.ParseText(
            text,
            new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Diagnose),
            path: DocumentPath);
        var compilation = CSharpCompilation.Create(
            AssemblyName,
            [tree],
            PdbFixtures.PlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, deterministic: true));

        using var peStream = File.Create(assemblyPath);
        using var pdbStream = File.Create(Path.ChangeExtension(assemblyPath, ".pdb"));
        using var xmlStream = File.Create(Path.ChangeExtension(assemblyPath, ".xml"));
        var result = compilation.Emit(
            peStream,
            pdbStream: pdbStream,
            xmlDocumentationStream: xmlStream,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb, pdbFilePath: Path.ChangeExtension(assemblyPath, ".pdb")),
            embeddedTexts: [EmbeddedText.FromSource(DocumentPath, text)]);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return assemblyPath;
    }
}
