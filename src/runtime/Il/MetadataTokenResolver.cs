using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace Sherlock.MCP.Runtime.Il;

internal enum MemberRefKind
{
    Method,
    Field
}

internal readonly record struct ResolvedMember(string DeclaringType, string MemberName, MemberRefKind Kind)
{
    public string Display => string.IsNullOrEmpty(DeclaringType) ? MemberName : $"{DeclaringType}.{MemberName}";
}

// Resolves an IL metadata-token operand to a "Namespace.Type.Member" display string.
// Pure metadata: no type-system loading. Declaring-type names use the same format as Type.FullName
// (namespace + '.' name, '+' for nested types, backtick arity preserved) so callers can compare
// against MLC Type.FullName values directly.
internal sealed class MetadataTokenResolver
{
    private readonly MetadataReader _md;
    private readonly StringSignatureTypeProvider _provider;
    private readonly StringSignatureTypeProvider _instantiatedProvider;
    private readonly ConcurrentDictionary<int, ResolvedMember?> _resolved = new();
    private readonly ConcurrentDictionary<TypeDefinitionHandle, string> _typeDefNames = new();

    public MetadataTokenResolver(MetadataReader md)
    {
        _md = md;
        _provider = new StringSignatureTypeProvider(md);
        _instantiatedProvider = new StringSignatureTypeProvider(md, keepGenericArguments: true);
    }

    public ResolvedMember? Resolve(int token) => _resolved.GetOrAdd(token, ResolveUncached);

    private ResolvedMember? ResolveUncached(int token)
    {
        EntityHandle handle;
        try { handle = MetadataTokens.EntityHandle(token); }
        catch (ArgumentException) { return null; }
        if (handle.IsNil) return null;

        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
            {
                var def = _md.GetMethodDefinition((MethodDefinitionHandle)handle);
                return new ResolvedMember(TypeDefName(def.GetDeclaringType()), _md.GetString(def.Name), MemberRefKind.Method);
            }
            case HandleKind.FieldDefinition:
            {
                var def = _md.GetFieldDefinition((FieldDefinitionHandle)handle);
                return new ResolvedMember(TypeDefName(def.GetDeclaringType()), _md.GetString(def.Name), MemberRefKind.Field);
            }
            case HandleKind.MemberReference:
            {
                var mr = _md.GetMemberReference((MemberReferenceHandle)handle);
                var kind = mr.GetKind() == MemberReferenceKind.Field ? MemberRefKind.Field : MemberRefKind.Method;
                return new ResolvedMember(ParentName(mr.Parent), _md.GetString(mr.Name), kind);
            }
            case HandleKind.MethodSpecification:
            {
                var spec = _md.GetMethodSpecification((MethodSpecificationHandle)handle);
                return ResolveUncached(MetadataTokens.GetToken(spec.Method));
            }
            default:
                return null;
        }
    }

    public ImmutableArray<string> ResolveMethodGenericArguments(int token)
    {
        if (!TryGetEntityHandle(token, out var handle) || handle.Kind != HandleKind.MethodSpecification)
            return ImmutableArray<string>.Empty;

        try { return _md.GetMethodSpecification((MethodSpecificationHandle)handle).DecodeSignature(_instantiatedProvider, null); }
        catch (BadImageFormatException) { return ImmutableArray<string>.Empty; }
    }

    public ImmutableArray<string> ResolveMethodParameterTypes(int token)
    {
        if (!TryGetEntityHandle(token, out var handle)) return ImmutableArray<string>.Empty;
        if (handle.Kind == HandleKind.MethodSpecification)
            handle = _md.GetMethodSpecification((MethodSpecificationHandle)handle).Method;

        try
        {
            return handle.Kind switch
            {
                HandleKind.MethodDefinition => _md.GetMethodDefinition((MethodDefinitionHandle)handle).DecodeSignature(_instantiatedProvider, null).ParameterTypes,
                HandleKind.MemberReference when _md.GetMemberReference((MemberReferenceHandle)handle).GetKind() == MemberReferenceKind.Method =>
                    _md.GetMemberReference((MemberReferenceHandle)handle).DecodeMethodSignature(_instantiatedProvider, null).ParameterTypes,
                _ => ImmutableArray<string>.Empty
            };
        }
        catch (BadImageFormatException) { return ImmutableArray<string>.Empty; }
    }

    public string? ResolveTypeToken(int token)
    {
        if (!TryGetEntityHandle(token, out var handle)) return null;

        try
        {
            return handle.Kind switch
            {
                HandleKind.TypeDefinition => _instantiatedProvider.GetTypeFromDefinition(_md, (TypeDefinitionHandle)handle, 0),
                HandleKind.TypeReference => _instantiatedProvider.GetTypeFromReference(_md, (TypeReferenceHandle)handle, 0),
                HandleKind.TypeSpecification => _md.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(_instantiatedProvider, null),
                _ => null
            };
        }
        catch (BadImageFormatException) { return null; }
    }

    public string? ResolveUserString(int token)
    {
        try
        {
            var handle = MetadataTokens.Handle(token);
            return handle.Kind == HandleKind.UserString ? _md.GetUserString((UserStringHandle)handle) : null;
        }
        catch (ArgumentException) { return null; }
        catch (BadImageFormatException) { return null; }
    }

    private static bool TryGetEntityHandle(int token, out EntityHandle handle)
    {
        try { handle = MetadataTokens.EntityHandle(token); }
        catch (ArgumentException) { handle = default; }
        return !handle.IsNil;
    }

    public MethodDefinitionHandle LocalMethodDefinition(int token)
    {
        EntityHandle handle;
        try { handle = MetadataTokens.EntityHandle(token); }
        catch (ArgumentException) { return default; }
        if (handle.IsNil) return default;

        return LocalMethodDefinition(handle);
    }

    private MethodDefinitionHandle LocalMethodDefinition(EntityHandle handle) => handle.Kind switch
    {
        HandleKind.MethodDefinition => (MethodDefinitionHandle)handle,
        HandleKind.MethodSpecification => LocalMethodDefinition(_md.GetMethodSpecification((MethodSpecificationHandle)handle).Method),
        HandleKind.MemberReference => LocalMethodForReference(_md.GetMemberReference((MemberReferenceHandle)handle)),
        _ => default
    };

    private MethodDefinitionHandle LocalMethodForReference(MemberReference reference)
    {
        if (reference.GetKind() != MemberReferenceKind.Method) return default;

        var declaringType = LocalTypeDefinition(reference.Parent);
        if (declaringType.IsNil) return default;

        var name = _md.GetString(reference.Name);
        var signature = _md.GetBlobContent(reference.Signature);
        MethodDefinitionHandle onlyByName = default;
        var nameMatches = 0;
        foreach (var candidate in _md.GetTypeDefinition(declaringType).GetMethods())
        {
            var definition = _md.GetMethodDefinition(candidate);
            if (!_md.StringComparer.Equals(definition.Name, name)) continue;
            if (_md.GetBlobContent(definition.Signature).SequenceEqual(signature)) return candidate;
            onlyByName = candidate;
            nameMatches++;
        }

        return nameMatches == 1 ? onlyByName : default;
    }

    private TypeDefinitionHandle LocalTypeDefinition(EntityHandle parent)
    {
        if (parent.Kind == HandleKind.TypeDefinition) return (TypeDefinitionHandle)parent;
        if (parent.Kind != HandleKind.TypeSpecification) return default;

        try
        {
            var blob = _md.GetBlobReader(_md.GetTypeSpecification((TypeSpecificationHandle)parent).Signature);
            if (blob.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance) return default;
            blob.ReadSignatureTypeCode();
            var genericType = blob.ReadTypeHandle();
            return genericType.Kind == HandleKind.TypeDefinition ? (TypeDefinitionHandle)genericType : default;
        }
        catch (BadImageFormatException)
        {
            return default;
        }
    }

    public string TypeDefName(TypeDefinitionHandle handle) =>
        _typeDefNames.GetOrAdd(handle, h => _provider.GetTypeFromDefinition(_md, h, 0));

    private string ParentName(EntityHandle parent)
    {
        if (parent.IsNil) return string.Empty;
        switch (parent.Kind)
        {
            case HandleKind.TypeDefinition:
                return _provider.GetTypeFromDefinition(_md, (TypeDefinitionHandle)parent, 0);
            case HandleKind.TypeReference:
                return _provider.GetTypeFromReference(_md, (TypeReferenceHandle)parent, 0);
            case HandleKind.TypeSpecification:
                try { return _md.GetTypeSpecification((TypeSpecificationHandle)parent).DecodeSignature(_provider, null); }
                catch (BadImageFormatException) { return "<generic>"; }
            case HandleKind.MethodDefinition:
            {
                var def = _md.GetMethodDefinition((MethodDefinitionHandle)parent);
                return _provider.GetTypeFromDefinition(_md, def.GetDeclaringType(), 0);
            }
            default:
                return "<unknown>";
        }
    }
}

// Produces type names as strings while decoding metadata signatures. Only the declaring-type
// portion is needed for call resolution, so by default generic instantiations collapse to the open
// generic-type-definition name (e.g. System.Collections.Generic.List`1). With keepGenericArguments
// they render like TypeNameFormatter.FriendlyFullName (e.g. System.Collections.Generic.List<System.String>).
internal sealed class StringSignatureTypeProvider : ISignatureTypeProvider<string, object?>
{
    private readonly MetadataReader _md;
    private readonly bool _keepGenericArguments;

    public StringSignatureTypeProvider(MetadataReader md, bool keepGenericArguments = false)
    {
        _md = md;
        _keepGenericArguments = keepGenericArguments;
    }

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var td = reader.GetTypeDefinition(handle);
        var name = reader.GetString(td.Name);
        var declaring = td.GetDeclaringType();
        if (!declaring.IsNil)
            return $"{GetTypeFromDefinition(reader, declaring, 0)}+{name}";
        var ns = reader.GetString(td.Namespace);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var tr = reader.GetTypeReference(handle);
        var name = reader.GetString(tr.Name);
        var scope = tr.ResolutionScope;
        if (!scope.IsNil && scope.Kind == HandleKind.TypeReference)
            return $"{GetTypeFromReference(reader, (TypeReferenceHandle)scope, 0)}+{name}";
        var ns = reader.GetString(tr.Namespace);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
        _keepGenericArguments ? $"{StripArity(genericType)}<{string.Join(", ", typeArguments)}>" : genericType;

    private static string StripArity(string name) =>
        string.Join("+", name.Split('+').Select(segment => segment.IndexOf('`') is var tick and > 0 ? segment[..tick] : segment));

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Boolean => "System.Boolean",
        PrimitiveTypeCode.Byte => "System.Byte",
        PrimitiveTypeCode.SByte => "System.SByte",
        PrimitiveTypeCode.Char => "System.Char",
        PrimitiveTypeCode.Int16 => "System.Int16",
        PrimitiveTypeCode.UInt16 => "System.UInt16",
        PrimitiveTypeCode.Int32 => "System.Int32",
        PrimitiveTypeCode.UInt32 => "System.UInt32",
        PrimitiveTypeCode.Int64 => "System.Int64",
        PrimitiveTypeCode.UInt64 => "System.UInt64",
        PrimitiveTypeCode.Single => "System.Single",
        PrimitiveTypeCode.Double => "System.Double",
        PrimitiveTypeCode.IntPtr => "System.IntPtr",
        PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
        PrimitiveTypeCode.Object => "System.Object",
        PrimitiveTypeCode.String => "System.String",
        PrimitiveTypeCode.Void => "System.Void",
        PrimitiveTypeCode.TypedReference => "System.TypedReference",
        _ => "System.Object"
    };

    public string GetSZArrayType(string elementType) => $"{elementType}[]";
    public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[]";
    public string GetByReferenceType(string elementType) => $"{elementType}&";
    public string GetPointerType(string elementType) => $"{elementType}*";
    public string GetPinnedType(string elementType) => elementType;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";
    public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "System.IntPtr";
}
