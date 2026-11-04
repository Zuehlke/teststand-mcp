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
/// <b>2. Never cross bitness.</b> The engine is an in-process COM server, so a 32-bit host drives
/// 32-bit TestStand and a 64-bit host 64-bit TestStand; a helper tool from the other tree would
/// analyse or diff against a different install (a 32-bit host once launched the 64-bit analyzer
/// because the 32-bit install lacked a file). So once connected, the engine's own Bin is the only
/// automatic candidate, and without an engine only the process's own root and registry view are
/// probed: <c>%ProgramW6432%</c> and Registry64 for x64, <c>%ProgramFiles(x86)%</c> and Registry32
/// for x86 (under WOW64 <see cref="Environment.SpecialFolder.ProgramFiles"/> is redirected to the
/// x86 path, so it never names the 64-bit root). Only the operator's <c>engine_path</c> may point
/// elsewhere.
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
    /// hatch for a station the automatic search gets wrong). Then, when connected, ONLY
    /// <paramref name="engineBinDir"/>; when not connected, <c>%TESTSTANDBIN%</c> if it lies under the
    /// process's own Program Files root, the Engine coclass' COM registration in the process's own
    /// registry view, and a newest-first scan of that root.
    /// </para>
    /// </summary>
    /// <param name="requiredExe">File name of the tool that must be present, e.g. "FileDiffer.exe".</param>
    /// <param name="engineBinDir">The connected engine's BinDirectory, or null when not connected.</param>
    /// <param name="explicitBinDir">Operator override (connect_engine's <c>engine_path</c>), or null.</param>
    /// <param name="is64BitProcess">The bitness to resolve for; null = this process's.</param>
    /// <param name="programFilesRoot">The install root to scan; null = the bitness's own root.</param>
    /// <returns>
    /// The directory holding <paramref name="requiredExe"/> and an empty <c>Probed</c>; or an empty
    /// <c>BinDir</c> plus a <c>Probed</c> trail naming the bitness and every candidate tried, so the
    /// caller can raise a diagnosable error on a station where the tool is nowhere to be found.
    /// </returns>
    internal static (string BinDir, string Probed) Resolve(
        string requiredExe, string? engineBinDir, string? explicitBinDir = null,
        bool? is64BitProcess = null, string? programFilesRoot = null)
    {
        bool is64 = is64BitProcess ?? Environment.Is64BitProcess;
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

        string DescribeProbeFailure() => $"{(is64 ? 64 : 32)}-bit process: " +
            (probed.Count > 0 ? string.Join(" | ", probed) : "(no candidate directory)");

        // An explicit override wins over everything, including the connected engine's own Bin: the
        // operator set it precisely because the automatic order picked the wrong install.
        if (Holds(explicitBinDir, "engine_path override")) return (explicitBinDir!, "");

        if (!string.IsNullOrWhiteSpace(engineBinDir))
            return Holds(engineBinDir, "engine BinDirectory") ? (engineBinDir!, "") : ("", DescribeProbeFailure());

        var root = programFilesRoot ?? GetProgramFilesRoot(is64);

        var envBin = Environment.GetEnvironmentVariable("TESTSTANDBIN");
        if (IsUnder(envBin, root) && Holds(envBin, "%TESTSTANDBIN%")) return (envBin!, "");

        var view = is64 ? RegistryView.Registry64 : RegistryView.Registry32;
        var regBin = FindBinFromRegistry(view);
        if (Holds(regBin, $"engine COM registration ({view})")) return (regBin!, "");

        if (root != null)
            foreach (var bin in EnumerateTestStandBins(root))
                if (Holds(bin, "installed TestStand")) return (bin, "");

        return ("", DescribeProbeFailure());
    }

    /// <summary>True when <paramref name="path"/> lies below <paramref name="root"/>.</summary>
    private static bool IsUnder(string? path, string? root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;
        try
        {
            var full = Path.GetFullPath(path!).TrimEnd('\\') + "\\";
            return full.StartsWith(Path.GetFullPath(root!).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
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
    /// The Program Files root that holds the installs of the given bitness, or null when it does
    /// not exist: <c>%ProgramW6432%</c> for 64-bit (visible from WOW64, unlike
    /// <see cref="Environment.SpecialFolder.ProgramFiles"/>), the x86 folder for 32-bit.
    /// </summary>
    internal static string? GetProgramFilesRoot(bool is64)
    {
        var root = is64
            ? Environment.GetEnvironmentVariable("ProgramW6432")
            : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return !string.IsNullOrWhiteSpace(root) && Directory.Exists(root) ? root : null;
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
