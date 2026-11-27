using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace TestStandMCP.Services;

/// <summary>
/// Loads the <c>NationalInstruments.TestStand.*</c> interop assemblies of the INSTALLED TestStand
/// instead of the 2023 copies of the NuGet package we compile against.
/// <para>
/// A .NET 8 load context holds one version per assembly. With the 23.0 interop in <c>bin</c>, the
/// server loaded it at startup, and an engine component of TestStand 2026 that needs 26.0 of the same
/// assembly could no longer load. The csproj therefore keeps the package's runtime files out of
/// <c>bin</c> and <c>deps.json</c>, so default probing fails and this handler answers: first from the
/// installation's <c>API\DotNET\Assemblies\CurrentVersion</c>, then its <c>Bin</c>, and only on a
/// station without TestStand (CI, <c>--list-tools</c>) from the package copy in <c>interop-fallback</c>.
/// The interop is backward compatible and .NET accepts a higher version for a lower-version reference.
/// </para>
/// </summary>
internal static class TestStandInteropResolver
{
    internal const string FallbackFolder = "interop-fallback";
    private const string Prefix = "NationalInstruments.TestStand.";

    /// <summary>Directories searched in order, resolved once.</summary>
    internal static readonly Lazy<string[]> SearchDirectories = new(() =>
    {
        var dirs = new System.Collections.Generic.List<string>();
        try
        {
            // shortcut: follows the automatic install search, not connect_engine's engine_path override
            // (the interop is loaded before any tool call); upgrade if a station needs a non-default install.
            var (bin, _) = TestStandInstallLocator.Resolve(Prefix + "Interop.API.dll", engineBinDir: null);
            if (bin.Length > 0)
            {
                var root = Path.GetDirectoryName(bin.TrimEnd('\\'));
                if (root != null) dirs.Add(Path.Combine(root, "API", "DotNET", "Assemblies", "CurrentVersion"));
                dirs.Add(bin);
            }
        }
        catch (Exception) { /* no install found: only the fallback remains */ }
        dirs.Add(Path.Combine(AppContext.BaseDirectory, FallbackFolder));
        return dirs.Where(Directory.Exists).ToArray();
    });

    /// <summary>Runs when the module loads, before any TestStand type is touched: covers the server,
    /// the prototype worker (same exe) and the test assembly that compiles this file.</summary>
    [ModuleInitializer]
    internal static void Install() => AssemblyLoadContext.Default.Resolving += Resolve;

    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        if (name.Name == null || !name.Name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var dir in SearchDirectories.Value)
        {
            var path = Path.Combine(dir, name.Name + ".dll");
            if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
        }
        return null;
    }
}
