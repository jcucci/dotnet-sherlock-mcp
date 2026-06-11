namespace Sherlock.MCP.Runtime;

internal static class TypeNameFormatter
{
    public static string FriendlyFullName(System.Type type)
    {
        if (type.IsByRef) return FriendlyFullName(type.GetElementType()!) + "&";
        if (type.IsPointer) return FriendlyFullName(type.GetElementType()!) + "*";
        if (type.IsArray)
        {
            var rank = type.GetArrayRank();
            var brackets = rank == 1 ? "[]" : "[" + new string(',', rank - 1) + "]";
            return FriendlyFullName(type.GetElementType()!) + brackets;
        }
        if (type.IsGenericParameter) return type.Name;
        if (!type.IsGenericType) return type.FullName ?? type.Name;

        var def = type.GetGenericTypeDefinition();
        var raw = def.FullName ?? def.Name;
        var tick = raw.IndexOf('`');
        var stem = tick > 0 ? raw.Substring(0, tick) : raw;
        var args = type.GetGenericArguments().Select(FriendlyFullName);
        return $"{stem}<{string.Join(", ", args)}>";
    }

    public static string FriendlyName(System.Type type)
    {
        if (type.IsByRef) return FriendlyName(type.GetElementType()!) + "&";
        if (type.IsPointer) return FriendlyName(type.GetElementType()!) + "*";
        if (type.IsArray)
        {
            var rank = type.GetArrayRank();
            var brackets = rank == 1 ? "[]" : "[" + new string(',', rank - 1) + "]";
            return FriendlyName(type.GetElementType()!) + brackets;
        }
        if (type.IsGenericType && !type.IsGenericTypeDefinition && type.GetGenericTypeDefinition().FullName == "System.Nullable`1")
            return FriendlyName(type.GetGenericArguments()[0]) + "?";
        if (type.IsGenericType)
        {
            var stem = type.GetGenericTypeDefinition().Name;
            var tick = stem.IndexOf('`');
            if (tick > 0) stem = stem.Substring(0, tick);
            var args = type.GetGenericArguments().Select(FriendlyName);
            return $"{stem}<{string.Join(", ", args)}>";
        }
        return type.FullName is string fullName && BuiltInTypeNames.TryGetValue(fullName, out var builtInName)
            ? builtInName
            : type.Name;
    }

    private static readonly Dictionary<string, string> BuiltInTypeNames = new(StringComparer.Ordinal)
    {
        ["System.Boolean"] = "bool",
        ["System.Byte"] = "byte",
        ["System.SByte"] = "sbyte",
        ["System.Char"] = "char",
        ["System.Decimal"] = "decimal",
        ["System.Double"] = "double",
        ["System.Single"] = "float",
        ["System.Int32"] = "int",
        ["System.UInt32"] = "uint",
        ["System.Int64"] = "long",
        ["System.UInt64"] = "ulong",
        ["System.Int16"] = "short",
        ["System.UInt16"] = "ushort",
        ["System.Object"] = "object",
        ["System.String"] = "string",
        ["System.Void"] = "void"
    };
}
