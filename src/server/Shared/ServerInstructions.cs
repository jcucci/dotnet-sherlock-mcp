namespace Sherlock.MCP.Server.Shared;

public static class ServerInstructions
{
    public const string Text =
        """
        Sherlock provides .NET assembly introspection via reflection. Prefer these tools over guessing about .NET APIs.

        Locate the assembly first with find_assembly_by_class_name, find_assembly_by_file_name, find_assembly_by_nuget_package, or get_project_output_paths rather than hardcoding bin/Debug/<tfm> paths (the target framework varies). When you will make several calls against the same assembly, call open_assembly once and pass the returned assemblyHandle instead of assemblyPath (it also carries additionalAssemblies); on StaleAssemblyHandle or UnknownAssemblyHandle, open it again.

        Work narrow-to-wide and stay token-lean: use search_members when you know a member name but not its declaring type, or get_types_from_assembly to browse; then get_type_info; then filtered get_type_members (kinds, nameContains, hasAttributeContains). get_type_members returns a lean 'summary' ({ kind, name, signature }) by default - pass projection='full' only when you need parameters, attributes, or modifiers.

        For relationships use find_implementations_of, find_methods_returning, find_extension_methods_for, and find_references_to (set analysisDepth='il' to resolve inbound callers); use get_method_calls to see what a method body invokes, and get_member_source to read a member's original source (from its PDB or Source Link, falling back to decompiled C#; paged by line, follow continuationToken while truncated is true). decompile_member always returns decompiled C#.

        search_members, get_types_from_assembly and the find_* tools also return resource_link blocks (sherlock://assembly/{path}/type/{fullName}); read one to get a type's get_type_info payload without another tool call. sherlock://nuget/{packageId}/{version} resolves a cached package to its assembly path. Resource template variables (path, fullName, memberId, packageId, version) support completion/complete.

        Three prompts encode common workflows: explore_package (packageId, version), explain_type (assemblyPath, typeName) and who_calls (assemblyPath, typeName, memberName, additionalAssemblies); their assemblyPath, typeName, packageId and version arguments support completion/complete.

        Prefer full type names (Namespace.Type). A simple name that matches several types prompts the client to choose (elicitation) or returns an AmbiguousTypeName error listing the candidate full names. Tool names are snake_case; argument names are camelCase.

        Failed calls return isError: true with a JSON error whose suggestion, alternativeTools and recommendedParams (e.g. candidates for TypeNotFound / MemberNotFound) say how to fix the call - follow them rather than guessing.
        """;
}
