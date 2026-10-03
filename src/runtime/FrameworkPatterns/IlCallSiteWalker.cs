using System.Collections.Immutable;
using System.Reflection.Metadata;
using Sherlock.MCP.Runtime.Il;
using Sherlock.MCP.Runtime.Inspection;

namespace Sherlock.MCP.Runtime.FrameworkPatterns;

internal sealed record IlCallSite(
    string CallerType,
    string CallerMethod,
    ResolvedMember Target,
    int Token,
    IReadOnlyList<string> PrecedingStrings,
    IReadOnlyList<string> PrecedingTypeTokens,
    ResolvedMember? PrecedingFunction);

// Walks every method body in an assembly and reports each call/callvirt together with the operands
// pushed since the previous call: ldstr literals, typeof (ldtoken + Type.GetTypeFromHandle) types and
// the last ldftn target. This is a straight-line heuristic, not data-flow analysis: values that arrive
// through locals, fields or parameters are not recovered.
internal static class IlCallSiteWalker
{
    private const string GetTypeFromHandle = "System.Type.GetTypeFromHandle";

    internal static void Walk(MetadataReaderLease metadata, Action<IlCallSite> onCall, CancellationToken cancellationToken)
    {
        var md = metadata.Reader;
        foreach (var typeHandle in md.TypeDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var callerType = metadata.Resolver.TypeDefName(typeHandle);
            foreach (var methodHandle in md.GetTypeDefinition(typeHandle).GetMethods())
                WalkMethod(metadata, callerType, methodHandle, onCall);
        }
    }

    internal static (string TypeName, string MethodName) UserFacingCaller(string callerType, string callerMethod)
    {
        var segments = callerType.Split('+');
        var visible = segments.TakeWhile(s => !s.StartsWith('<')).ToArray();
        var typeName = visible.Length > 0 ? string.Join("+", visible) : callerType;
        var stateMachineOwner = segments.Skip(visible.Length).Select(StateMachineOwner).FirstOrDefault(owner => owner is not null);
        return (typeName, stateMachineOwner ?? CompilerGeneratedMethodName(callerMethod) ?? callerMethod);
    }

    private static string? StateMachineOwner(string typeSegment)
    {
        var marker = typeSegment.LastIndexOf(">d__", StringComparison.Ordinal);
        return typeSegment.StartsWith('<') && marker > 1 ? typeSegment[1..marker] : null;
    }

    internal static string? CompilerGeneratedMethodName(string methodName)
    {
        if (!methodName.StartsWith('<')) return null;
        var close = methodName.LastIndexOf('>');
        if (close < 2 || close + 2 >= methodName.Length) return null;
        var marker = methodName[close + 1];
        if (methodName[close + 2] != '_' || (marker != 'b' && marker != 'g')) return null;
        return $"{methodName[1..close]} (lambda)";
    }

    private static void WalkMethod(
        MetadataReaderLease metadata, string callerType, MethodDefinitionHandle methodHandle, Action<IlCallSite> onCall)
    {
        var md = metadata.Reader;
        var methodDef = md.GetMethodDefinition(methodHandle);
        if (methodDef.RelativeVirtualAddress == 0) return;

        byte[]? il;
        try { il = metadata.PEReader.GetMethodBody(methodDef.RelativeVirtualAddress).GetILBytes(); }
        catch (BadImageFormatException) { return; }

        var callerMethod = md.GetString(methodDef.Name);
        var strings = new List<string>();
        var types = new List<string>();
        ResolvedMember? function = null;

        foreach (var tokenRef in IlInstructionReader.ReadTokenInstructions(il, includeOperandLoads: true))
        {
            switch (tokenRef.Kind)
            {
                case IlRefKind.LdStr when metadata.Resolver.ResolveUserString(tokenRef.Token) is { } literal:
                    strings.Add(literal);
                    break;
                case IlRefKind.LdToken when metadata.Resolver.ResolveTypeToken(tokenRef.Token) is { } typeName:
                    types.Add(typeName);
                    break;
                case IlRefKind.LdFtn or IlRefKind.LdVirtFtn:
                    function = metadata.Resolver.Resolve(tokenRef.Token);
                    break;
                case IlRefKind.Call or IlRefKind.CallVirt:
                    if (metadata.Resolver.Resolve(tokenRef.Token) is not { } target) break;
                    if (target.Display == GetTypeFromHandle) break;
                    onCall(new IlCallSite(callerType, callerMethod, target, tokenRef.Token, strings.ToArray(), types.ToArray(), function));
                    strings.Clear();
                    types.Clear();
                    function = null;
                    break;
            }
        }
    }

    internal static ImmutableArray<string> GenericArguments(MetadataReaderLease metadata, int token) =>
        metadata.Resolver.ResolveMethodGenericArguments(token);

    internal static ImmutableArray<string> ParameterTypes(MetadataReaderLease metadata, int token) =>
        metadata.Resolver.ResolveMethodParameterTypes(token);
}
