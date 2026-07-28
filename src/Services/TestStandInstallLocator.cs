using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace TestStandMCP.Services;

/// <summary>
/// Locates the <c>Bin</c> directory of an installed NI TestStand — engine-free, so it is usable
/// before (and independently of) a COM connection, and unit-testable on its own.
///
/// <para><b>Two traps this class exists to avoid.</b></para>
///
/// <para>
/// <b>1. Probe for the tool you are actually going to launch.</b> The NI tools are not all shipped
/// by every install flavour: an engine-only install has no <c>FileDiffer.exe</c>, and the 32-bit and
/// 64-bit trees of the SAME release can differ in what they carry. Validating a candidate against a
/// fixed stand-in (e.g. always <c>AnalyzerApp.exe</c>) therefore either rejects the correct Bin or
/// returns one that does not hold the wanted tool at all.
/// </para>
///
/// <para>
/// <b>2. A 32-bit host cannot see the 64-bit install through the usual APIs.</b> This host is x86,
/// and under WOW64 <see cref="Environment.SpecialFolder.ProgramFiles"/> is redirected to
/// "…\Program Files (x86)" — the very same path as
/// <see cref="Environment.SpecialFolder.ProgramFilesX86"/>. Iterating both scans one directory twice
/// and never reaches a 64-bit install; <c>%ProgramW6432%</c> is the only reliable way to name the
/// 64-bit root from a 32-bit process. The same asymmetry applies to the registry: a 64-bit TestStand
/// registers its Engine coclass exclusively in the 64-bit view.
/// </para>
///
/// Nothing here is version-pinned — releases are always matched with a <c>TestStand*</c> wildcard.
/// </summary>
internal static class TestStandInstallLocator
{
    /// <summary>COM registration of the TestStand Engine coclass; its InprocServer32 default value
    /// is the full path to the engine DLL, and that DLL's directory is the TestStand Bin folder.</summary>
    private const string EngineInprocKey =
        @"CLSID\{B2794EF6-C0B6-11D0-939C-0020AF68E893}\InprocServer32";

    /// <summary>
    /// Resolves the TestStand <c>Bin</c> directory that actually CONTAINS <paramref name="requiredExe"/>.
    /// <para>
    /// Order: <paramref name="explicitBinDir"/> (an operator-supplied override — the manual escape
    /// hatch for a station the automatic search gets wrong) → <paramref name="engineBinDir"/> (the
    /// connected engine, i.e. the exact running version) → <c>%TESTSTANDBIN%</c> → the Engine
    /// coclass' COM registration in BOTH registry views → a newest-first scan of every install root.
    /// </para>
    /// </summary>
    /// <param name="requiredExe">File name of the tool that must be present, e.g. "FileDiffer.exe".</param>
    /// <param name="engineBinDir">The connected engine's BinDirectory, or null when not connected.</param>
    /// <param name="explicitBinDir">Operator override (connect_engine's <c>engine_path</c>), or null.</param>
    /// <returns>
    /// The directory holding <paramref name="requiredExe"/> and an empty <c>Probed</c>; or an empty
    /// <c>BinDir</c> plus a <c>Probed</c> trail of every candidate tried, so the caller can raise a
    /// diagnosable error on a station where the tool is nowhere to be found.
    /// </returns>
    internal static (string BinDir, string Probed) Resolve(
        string requiredExe, string? engineBinDir, string? explicitBinDir = null)
    {
        var probed = new List<string>();

        bool Holds(string? dir, string source)
        {
            if (string.IsNullOrWhiteSpace(dir)) return false;
            string candidate;
            try { candidate = Path.Combine(dir!, requiredExe); }
            catch (ArgumentException) { return false; }   // invalid characters in a station's env var
            probed.Add($"{source} -> {candidate}");
            return File.Exists(candidate);
        }

        // An explicit override wins over everything, including the connected engine's own Bin: the
        // operator set it precisely because the automatic order picked the wrong install.
        if (Holds(explicitBinDir, "engine_path override")) return (explicitBinDir!, "");

        if (Holds(engineBinDir, "engine BinDirectory")) return (engineBinDir!, "");

        var envBin = Environment.GetEnvironmentVariable("TESTSTANDBIN");
        if (Holds(envBin, "%TESTSTANDBIN%")) return (envBin!, "");

        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            var regBin = FindBinFromRegistry(view);
            if (Holds(regBin, $"engine COM registration ({view})")) return (regBin!, "");
        }

        foreach (var root in GetProgramFilesRoots())
            foreach (var bin in EnumerateTestStandBins(root))
                if (Holds(bin, "installed TestStand")) return (bin, "");

        return ("", probed.Count > 0 ? string.Join(" | ", probed) : "(no candidate directory)");
    }

    /// <summary>
    /// Normalises an operator-supplied TestStand path to the <c>Bin</c> directory that holds the NI
    /// tools. Accepts the engine DLL itself (<c>…\Bin\teapi.dll</c>), the <c>Bin</c> directory, or the
    /// install root (<c>…\TestStand 2026</c>, whose <c>Bin</c> subdirectory is then used).
    /// Returns null when <paramref name="path"/> is blank or does not exist, so the caller can raise a
    /// precise error instead of silently ignoring a typo — which is what the old no-op parameter did.
    /// </summary>
    internal static string? NormalizeBinDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        string full;
        try { full = Path.GetFullPath(path!.Trim().Trim('"')); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        // A file (the engine DLL, or any tool in Bin) → the directory containing it.
        if (File.Exists(full)) return Path.GetDirectoryName(full);
        if (!Directory.Exists(full)) return null;

        // A directory: prefer a "Bin" subdirectory when the install root was given.
        var bin = Path.Combine(full, "Bin");
        return Directory.Exists(bin) ? bin : full;
    }

    /// <summary>
    /// The Program Files roots to scan for NI installs — deduplicated (under WOW64 several of the
    /// sources collapse onto the same path) and existing-only. The x86 root comes first because it
    /// matches this process's bitness, and therefore the in-process 32-bit engine: when both trees
    /// carry the wanted tool, the same-bitness one wins.
    /// </summary>
    internal static IEnumerable<string> GetProgramFilesRoots()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetEnvironmentVariable("ProgramW6432"),   // 64-bit root, visible from WOW64
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            if (!seen.Add(root!.TrimEnd('\\'))) continue;
            if (Directory.Exists(root)) yield return root!;
        }
    }

    /// <summary>
    /// Yields <c>&lt;root&gt;\National Instruments\TestStand*\Bin</c> for every installed release,
    /// newest first ("TestStand 2026" before "TestStand 2021"). An unreadable install root yields
    /// nothing rather than throwing, so one bad root never aborts the scan.
    /// </summary>
    internal static IEnumerable<string> EnumerateTestStandBins(string programFilesRoot)
    {
        var niDir = Path.Combine(programFilesRoot, "National Instruments");
        string[] dirs;
        try
        {
            dirs = Directory.Exists(niDir)
                ? Directory.GetDirectories(niDir, "TestStand*")
                : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            dirs = Array.Empty<string>();
        }
        Array.Sort(dirs, (a, b) => StringComparer.OrdinalIgnoreCase.Compare(b, a));
        foreach (var dir in dirs) yield return Path.Combine(dir, "Bin");
    }

    /// <summary>
    /// Reads the registered engine's Bin directory from the Engine coclass' COM registration in the
    /// given registry view. Returns null when the key is missing or unreadable; whether the wanted
    /// tool lives there is the caller's check.
    /// </summary>
    internal static string? FindBinFromRegistry(RegistryView view)
    {
        try
        {
            using var hkcr = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view);
            using var key = hkcr.OpenSubKey(EngineInprocKey);
            // (Default) value = full path to the engine DLL; strip any stray surrounding quotes.
            var dllPath = (key?.GetValue(null) as string)?.Trim().Trim('"');
            return string.IsNullOrEmpty(dllPath) ? null : Path.GetDirectoryName(dllPath);
        }
        catch (Exception)
        {
            // Registry not readable / key absent on this station — fall through to the directory scan.
            return null;
        }
    }
}
