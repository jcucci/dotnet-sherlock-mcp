using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Sherlock.MCP.Tests;

internal sealed record ApiDiffPair(string LeftPath, string RightPath);

internal static class ApiDiffFixtures
{
    public const string AssemblyName = "Acme.Lib";

    public const string Version1 = """
        namespace Acme.Lib
        {
            public class Removed { }
            public class WillSeal { }
            public class WillBeInternal { }
            public static class Helpers { public static void Run() { } }

            public interface IShape { double Area(); }

            public abstract class Animal { public abstract string Speak(); }

            public class Widget
            {
                public int Count;
                public readonly int Fixed = 1;
                public int Mutable;
                public const int Max = 10;
                public string Name { get; set; } = "";
                public int Spin(int turns) => turns;
                public void Rename(int oldName) { }
                public void Optional(int x = 1) { }
                public void Gone() { }
                public virtual void Hook() { }
                public long Size() => 0;
                public void Overload(int x) { }
                protected void Helper() { }
                public int Setter { get; set; }
                public int Getter { get; set; }
                public string Init { get; set; } = "";
                public void Params(int[] xs) { }
                public void Unparams(params int[] xs) { }
            }

            public class Renamed<T> where T : class { }

            public class BaseThing { }
            public class Derived : BaseThing
            {
                public virtual void Moved() { }
                public void Promoted() { }
            }

            public enum Color { Red = 1, Green = 2 }

            public class Box<T> { }

            public class Shape : IShape { public double Area() => 0; }

            public sealed class Locked { protected internal void Hidden() { } }
        }

        namespace Acme.Lib.Internal
        {
            public class Plumbing { public void Pipe() { } }
        }
        """;

    public const string Version2 = """
        namespace Acme.Lib
        {
            public sealed class WillSeal { }
            internal class WillBeInternal { }
            public static class Helpers { public static void Run() { } }

            public interface IShape { double Area(); double Perimeter(); int Sides => 0; }

            public abstract class Animal { public abstract string Speak(); public abstract void Eat(); }

            public class Widget
            {
                public int Count;
                public int Fixed = 1;
                public readonly int Mutable;
                public const int Max = 20;
                public string Name { get; } = "";
                public int Spin(int turns) => turns;
                public void Rename(int newName) { }
                public void Optional(int x) { }
                public void Hook() { }
                public int Size() => 0;
                public void Overload(long x) { }
                protected void Helper() { }
                public void Added() { }
                public int Setter { get; protected set; }
                public int Getter { protected get; set; }
                public string Init { get; init; } = "";
                public void Params(params int[] xs) { }
                public void Unparams(int[] xs) { }
            }

            public class Renamed<TItem> where TItem : class { }

            public class BaseThing
            {
                public virtual void Moved() { }
                public virtual void Promoted() { }
            }
            public class Derived : BaseThing
            {
                public override void Promoted() { }
            }

            public enum Color { Red = 1, Green = 3, Blue = 4 }

            public class Box<T> where T : class { }

            public class Shape { public double Area() => 0; }

            public sealed class Locked { protected internal void Renamed() { } }

            public class NewType { }
        }

        namespace Acme.Lib.Internal
        {
            public class Plumbing { public void Pipe() { } public void Drain() { } }
        }
        """;

    public static ApiDiffPair EmitPair(string left = Version1, string right = Version2) =>
        new(Emit(left), Emit(right));

    public static string Emit(string source, string? directory = null)
    {
        directory ??= TestHandles.NewStateDirectory();
        Directory.CreateDirectory(directory);
        var assemblyPath = Path.Combine(directory, $"{AssemblyName}.dll");
        var compilation = CSharpCompilation.Create(
            AssemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            PdbFixtures.PlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var peStream = File.Create(assemblyPath);
        var result = compilation.Emit(peStream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return assemblyPath;
    }
}
