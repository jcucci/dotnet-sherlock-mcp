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
}
