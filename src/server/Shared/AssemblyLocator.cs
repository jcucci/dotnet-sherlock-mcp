using System.Reflection;

namespace Sherlock.MCP.Server.Shared;

internal static class AssemblyLocator
{
   public static string? FindAssemblyByClassName(string className, string workingDirectory)
   {
      if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(workingDirectory))
         return null;

      if (!Directory.Exists(workingDirectory))
         return null;

      var dllFiles = Directory.GetFiles(workingDirectory, "*.dll", SearchOption.AllDirectories);
      var exeFiles = Directory.GetFiles(workingDirectory, "*.exe", SearchOption.AllDirectories);

      var allFiles = dllFiles.Concat(exeFiles).ToLookup(Path.GetFileName);

      if (allFiles.Count == 0)
         return null;

      var result = allFiles
         .AsParallel()
         .Select(group => TryFindClassInAssembly(group, className))
         .FirstOrDefault(result => result != null);

      return result;
   }

   private static string? TryFindClassInAssembly(IEnumerable<string> assemblyPaths, string className)
   {
      try
      {
         var assemblyPath = assemblyPaths.First();
         var assembly = Assembly.LoadFrom(assemblyPath);
         var types = assembly.GetExportedTypes();
         var matchingType = types.FirstOrDefault(type =>
            type.Name.Equals(className, StringComparison.OrdinalIgnoreCase) ||
            type.FullName?.Equals(className, StringComparison.OrdinalIgnoreCase) == true);

         return matchingType != null ? assemblyPath : null;
      }
      catch (Exception)
      {
         return null;
      }
   }
}