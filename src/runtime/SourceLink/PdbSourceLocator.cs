using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

namespace Sherlock.MCP.Runtime.SourceLink;

public sealed record PdbLookup(bool HasPdb, IReadOnlyList<MemberSourceLocation?> Locations);

public static class PdbSourceLocator
{
    private static readonly Guid SourceLinkKind = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");
    private static readonly Guid EmbeddedSourceKind = new("0E8A571B-6926-466E-B4AD-8AB04611F5FE");
    private static readonly string[] StateMachineAttributes =
    [
        "System.Runtime.CompilerServices.AsyncStateMachineAttribute",
        "System.Runtime.CompilerServices.IteratorStateMachineAttribute",
        "System.Runtime.CompilerServices.AsyncIteratorStateMachineAttribute"
    ];

    public static PdbLookup Locate(string assemblyPath, IReadOnlyList<MemberInfo> members, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        using var provider = OpenPdb(pe, assemblyPath);
        if (provider == null)
            return new PdbLookup(HasPdb: false, members.Select(_ => (MemberSourceLocation?)null).ToArray());

        var reader = provider.GetMetadataReader();
        var sourceLink = ReadSourceLink(reader);
        var documents = new Dictionary<DocumentHandle, SourceDocument>();
        var locations = members
            .Select(member =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Locate(reader, member, sourceLink, documents);
            })
            .ToArray();
        return new PdbLookup(HasPdb: true, locations);
    }

    private static MetadataReaderProvider? OpenPdb(PEReader pe, string assemblyPath)
    {
        try
        {
            return pe.TryOpenAssociatedPortablePdb(assemblyPath, OpenIfExists, out var provider, out _) ? provider : null;
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Stream? OpenIfExists(string path) => File.Exists(path) ? File.OpenRead(path) : null;

    private static SourceLinkMap? ReadSourceLink(MetadataReader reader)
    {
        foreach (var handle in reader.GetCustomDebugInformation(EntityHandle.ModuleDefinition))
        {
            var info = reader.GetCustomDebugInformation(handle);
            if (reader.GetGuid(info.Kind) == SourceLinkKind)
                return SourceLinkMap.Parse(Encoding.UTF8.GetString(reader.GetBlobBytes(info.Value)));
        }
        return null;
    }

    private static MemberSourceLocation? Locate(
        MetadataReader reader, MemberInfo member, SourceLinkMap? sourceLink, Dictionary<DocumentHandle, SourceDocument> documents)
    {
        var points = MethodsOf(member)
            .SelectMany(method => SequencePoints(reader, method))
            .ToArray();
        if (points.Length == 0) return null;

        var isConstructor = member is ConstructorInfo;
        var documentHandle = isConstructor ? points[^1].Document : points[0].Document;
        var inDocument = points.Where(point => point.Document == documentHandle).ToArray();
        var anchor = isConstructor ? inDocument[^1] : inDocument[0];
        if (!documents.TryGetValue(documentHandle, out var document))
        {
            document = ReadDocument(reader, documentHandle, sourceLink);
            documents[documentHandle] = document;
        }

        return new MemberSourceLocation(
            document,
            StartLine: inDocument.Min(point => point.StartLine),
            EndLine: inDocument.Max(point => point.EndLine),
            AnchorLine: anchor.StartLine);
    }

    private static SequencePoint[] SequencePoints(MetadataReader reader, MethodBase method)
    {
        var handle = MetadataTokens.EntityHandle(method.MetadataToken);
        if (handle.Kind != HandleKind.MethodDefinition) return [];

        var debugHandle = ((MethodDefinitionHandle)handle).ToDebugInformationHandle();
        if (MetadataTokens.GetRowNumber(debugHandle) > reader.MethodDebugInformation.Count) return [];

        var info = reader.GetMethodDebugInformation(debugHandle);
        return info.SequencePointsBlob.IsNil ? [] : info.GetSequencePoints().Where(point => !point.IsHidden).ToArray();
    }

    private static IEnumerable<MethodBase> MethodsOf(MemberInfo member) => member switch
    {
        MethodBase method => WithStateMachine(method),
        PropertyInfo property => new[] { Safe(() => property.GetGetMethod(true)), Safe(() => property.GetSetMethod(true)) }.OfType<MethodBase>(),
        EventInfo evt => new[] { Safe(() => evt.GetAddMethod(true)), Safe(() => evt.GetRemoveMethod(true)), Safe(() => evt.GetRaiseMethod(true)) }.OfType<MethodBase>(),
        _ => []
    };

    private static IEnumerable<MethodBase> WithStateMachine(MethodBase method)
    {
        yield return method;
        if (StateMachineMoveNext(method) is { } moveNext)
            yield return moveNext;
    }

    private static MethodInfo? StateMachineMoveNext(MethodBase method) => Safe(() =>
    {
        var attribute = method.GetCustomAttributesData()
            .FirstOrDefault(data => StateMachineAttributes.Contains(data.AttributeType.FullName, StringComparer.Ordinal));
        return attribute?.ConstructorArguments is [{ Value: Type stateMachine }]
            ? stateMachine.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            : null;
    });

    private static SourceDocument ReadDocument(MetadataReader reader, DocumentHandle handle, SourceLinkMap? sourceLink)
    {
        var document = reader.GetDocument(handle);
        var path = reader.GetString(document.Name);
        return new SourceDocument(
            path,
            document.HashAlgorithm.IsNil ? Guid.Empty : reader.GetGuid(document.HashAlgorithm),
            document.Hash.IsNil ? [] : reader.GetBlobBytes(document.Hash),
            ReadEmbeddedSource(reader, handle),
            sourceLink?.GetUrl(path));
    }

    private static byte[]? ReadEmbeddedSource(MetadataReader reader, DocumentHandle handle)
    {
        foreach (var cdiHandle in reader.GetCustomDebugInformation(handle))
        {
            var info = reader.GetCustomDebugInformation(cdiHandle);
            if (reader.GetGuid(info.Kind) != EmbeddedSourceKind) continue;

            var blob = reader.GetBlobReader(info.Value);
            var format = blob.ReadInt32();
            if (format < 0 || blob.RemainingBytes > SourceDocument.MaxBytes) return null;
            var bytes = blob.ReadBytes(blob.RemainingBytes);
            return format > 0 ? Inflate(bytes, format) : bytes;
        }
        return null;
    }

    internal static byte[]? Inflate(byte[] compressed, int uncompressedSize)
    {
        if (uncompressedSize > SourceDocument.MaxBytes) return null;
        try
        {
            using var input = new DeflateStream(new MemoryStream(compressed), CompressionMode.Decompress);
            var inflated = BoundedReader.ReadAll(input, uncompressedSize);
            return inflated?.Length == uncompressedSize ? inflated : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static T? Safe<T>(Func<T?> read) where T : class
    {
        try { return read(); }
        catch { return null; }
    }
}
