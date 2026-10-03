using System.Runtime.CompilerServices;
using VerifyTests;

namespace Sherlock.MCP.Tests.Golden;

internal static class GoldenSettings
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifierSettings.UseStrictJson();
        VerifierSettings.DontIgnoreEmptyCollections();
    }
}
