using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Sherlock.MCP.Runtime.ProjectAssets;

internal static class TargetFrameworkReader
{
    public static string? Read(string assemblyPath)
    {
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata) return null;

            var reader = peReader.GetMetadataReader();
            if (!reader.IsAssembly) return null;

            foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
            {
                var attribute = reader.GetCustomAttribute(handle);
                if (!IsTargetFrameworkAttribute(reader, attribute.Constructor)) continue;

                var value = reader.GetBlobReader(attribute.Value);
                return value.ReadUInt16() == 1 ? value.ReadSerializedString() : null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException)
        {
        }
        return null;
    }

    private static bool IsTargetFrameworkAttribute(MetadataReader reader, EntityHandle constructor)
    {
        if (constructor.Kind != HandleKind.MemberReference) return false;

        var parent = reader.GetMemberReference((MemberReferenceHandle)constructor).Parent;
        if (parent.Kind != HandleKind.TypeReference) return false;

        var type = reader.GetTypeReference((TypeReferenceHandle)parent);
        return reader.StringComparer.Equals(type.Name, "TargetFrameworkAttribute")
            && reader.StringComparer.Equals(type.Namespace, "System.Runtime.Versioning");
    }
}
