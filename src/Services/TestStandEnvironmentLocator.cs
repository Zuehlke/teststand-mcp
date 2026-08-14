using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TestStandMCP.Services;

/// <summary>
/// Reads, validates and locates TestStand <b>environments</b> (<c>.tsenv</c> files) — engine-free, so
/// every check runs BEFORE any engine exists, and the whole class is unit-testable without TestStand.
///
/// <para><b>What a .tsenv is.</b> A station that hosts several products isolates each one's TestStand
/// <c>CommonAppData</c> / <c>Public</c> / <c>LocalAppData</c> directories in a separate environment.
/// The Sequence Editor selects one with its <c>/env &lt;path.tsenv&gt;</c> command-line switch; the
/// in-process equivalent is <c>EngineInitializationSettings.SetEnvironmentPath</c>, which must run
/// BEFORE the engine is constructed. The file is INI-shaped:</para>
/// <code>
/// [TestStandPaths]
/// CommonAppData = "C:\ProgramData\&lt;Product&gt;\CommonAppData"
/// Public        = "C:\Program Files (x86)\&lt;Vendor&gt;\TestStand Env\&lt;version&gt;\Env\Public"
/// LocalAppData  = "C:\ProgramData\&lt;Product&gt;\LocalAppData"
/// </code>
///
/// <para><b>Why the validation matters more than usual.</b> Pointing the engine at a
/// <c>CommonAppData</c> that TestStand has never initialized (no <c>Cfg\GeneralEngine.cfg</c>) makes it
/// pop an INTERACTIVE "Engine cannot be initialized" dialog rather than failing cleanly — and headless
/// nobody can answer it, so the connect hangs forever instead of returning an error. Everything here
/// therefore fails loudly and names the concrete defect. It is still only the FIRST gate: the
/// authority is TestStand's own <c>IEngineInitializationSettings.CanInitializeEngine()</c>, which
/// <see cref="TestStandService"/> calls after applying the path and before creating the engine.</para>
/// </summary>
internal static class TestStandEnvironmentLocator
{
    /// <summary>The INI section holding the three redirected roots.</summary>
    private const string PathsSection = "TestStandPaths";

    /// <summary>File TestStand writes when it first initializes a <c>CommonAppData</c> tree. Its
    /// absence is the reliable tell for the unanswerable-dialog case described on the class.</summary>
    internal const string EngineCfgRelativePath = @"Cfg\GeneralEngine.cfg";

    /// <summary>Sentinel accepted wherever a <c>.tsenv</c> path is taken, requesting the walk-up
    /// search of <see cref="Detect"/> instead of a literal path.</summary>
    internal const string AutoSentinel = "auto";

    // ── Results ──────────────────────────────────────────────────────────────

    /// <summary>A parsed + validated <c>.tsenv</c>. <see cref="Issues"/> empty means usable.</summary>
    internal sealed class EnvironmentInfo
    {
        /// <summary>Absolute path of the <c>.tsenv</c> file itself.</summary>
        public string TsenvPath { get; init; } = "";
        /// <summary>Resolved <c>CommonAppData</c> root, or "" when the key is absent.</summary>
        public string CommonAppData { get; init; } = "";
        /// <summary>Resolved <c>Public</c> root, or "" when the key is absent.</summary>
        public string PublicDir { get; init; } = "";
        /// <summary>Resolved <c>LocalAppData</c> root, or "" when the key is absent.</summary>
        public string LocalAppData { get; init; } = "";
        /// <summary>Every reason this environment cannot be used, each naming the concrete defect.</summary>
        public List<string> Issues { get; init; } = new();
        /// <summary>True when nothing objected — the engine may be pointed at this environment.</summary>
        public bool IsUsable => Issues.Count == 0;
    }

    /// <summary>Outcome of the walk-up search for a <c>.tsenv</c> next to (or above) a sequence file.</summary>
    internal sealed class DetectionResult
    {
        /// <summary>The single <c>.tsenv</c> found, or null when there was none / it was ambiguous.</summary>
        public string? TsenvPath { get; init; }
        /// <summary>Directory the hit came from — reported so an implicit pick is never invisible.</summary>
        public string? FoundInDirectory { get; init; }
        /// <summary>Set when one directory held SEVERAL <c>.tsenv</c> files; the search then aborts
        /// rather than picking one, and this names the candidates.</summary>
        public string? Ambiguity { get; init; }
        /// <summary>Every directory visited, so a miss is diagnosable instead of just "not found".</summary>
        public string Probed { get; init; } = "";
        /// <summary>True when exactly one environment was identified.</summary>
        public bool Found => TsenvPath is not null;
    }

    // ── Path normalisation ───────────────────────────────────────────────────

    /// <summary>
    /// Normalises an operator-supplied <c>.tsenv</c> path: trims blanks and stray quotes and makes it
    /// absolute. Returns null when blank or syntactically unusable, so the caller raises a precise
    /// error rather than silently connecting to the global environment — the failure mode this whole
    /// feature exists to prevent.
    /// </summary>
    internal static string? NormalizeTsenvPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path!.Trim().Trim('"'))); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>True when <paramref name="value"/> is the <c>auto</c> sentinel rather than a path.</summary>
    internal static bool IsAutoSentinel(string? value) =>
        value is not null && value.Trim().Equals(AutoSentinel, StringComparison.OrdinalIgnoreCase);

    // ── Reading + validating ─────────────────────────────────────────────────

    /// <summary>
    /// Reads a <c>.tsenv</c> and checks everything that can be checked without an engine. Never throws
    /// for a bad file — the defects come back in <see cref="EnvironmentInfo.Issues"/> so the caller can
    /// report all of them at once.
    /// </summary>
    internal static EnvironmentInfo ReadAndValidate(string tsenvPath)
    {
        var issues = new List<string>();
        var full = NormalizeTsenvPath(tsenvPath);

        if (full is null)
            return new EnvironmentInfo { Issues = { $"'{tsenvPath}' is not a usable file path." } };

        if (!File.Exists(full))
            return new EnvironmentInfo { TsenvPath = full, Issues = { $"The environment file does not exist: {full}" } };

        if (!string.Equals(Path.GetExtension(full), ".tsenv", StringComparison.OrdinalIgnoreCase))
            issues.Add($"Expected a .tsenv file, got '{Path.GetExtension(full)}': {full}");

        Dictionary<string, string> paths;
        try { paths = ReadIniSection(File.ReadAllLines(full), PathsSection); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new EnvironmentInfo { TsenvPath = full, Issues = { $"The environment file cannot be read: {ex.Message}" } };
        }

        if (paths.Count == 0)
            issues.Add($"No [{PathsSection}] section (or it is empty) in {full}");

        var baseDir = Path.GetDirectoryName(full) ?? "";
        string Resolve(string key) => ResolveEntry(paths, key, baseDir);

        var commonAppData = Resolve("CommonAppData");
        var publicDir     = Resolve("Public");
        var localAppData  = Resolve("LocalAppData");

        // CommonAppData is the decisive one: it holds the engine configuration the engine reads at
        // startup, and it is the key whose absence produces the unanswerable dialog.
        if (commonAppData.Length == 0)
        {
            issues.Add($"[{PathsSection}] has no CommonAppData entry in {full}");
        }
        else if (!Directory.Exists(commonAppData))
        {
            issues.Add($"CommonAppData directory does not exist: {commonAppData}");
        }
        else
        {
            var cfg = Path.Combine(commonAppData, EngineCfgRelativePath);
            if (!File.Exists(cfg))
                issues.Add($"TestStand has never initialized this environment — {EngineCfgRelativePath} " +
                           $"is missing under {commonAppData}. Open it once in the Sequence Editor " +
                           $"(/env \"{full}\") so the engine configuration is created.");
        }

        if (publicDir.Length    > 0 && !Directory.Exists(publicDir))    issues.Add($"Public directory does not exist: {publicDir}");
        if (localAppData.Length > 0 && !Directory.Exists(localAppData)) issues.Add($"LocalAppData directory does not exist: {localAppData}");

        return new EnvironmentInfo
        {
            TsenvPath     = full,
            CommonAppData = commonAppData,
            PublicDir     = publicDir,
            LocalAppData  = localAppData,
            Issues        = issues,
        };
    }

    /// <summary>
    /// Walks up from <paramref name="startPath"/> (a sequence file or a directory) looking for the
    /// nearest ancestor that identifies EXACTLY ONE <c>.tsenv</c>.
    ///
    /// <para><b>Each ancestor is checked two ways</b>, because real layouts rarely put the
    /// environment directly above the sequences. First the directory itself, then its IMMEDIATE
    /// subdirectories — so a tree like</para>
    /// <code>
    /// C:\Product\Config\Product.tsenv          &lt;- the environment
    /// C:\Product\Components\Sequences\Main.seq &lt;- the sequence files
    /// </code>
    /// <para>
    /// resolves at <c>C:\Product</c> (whose <c>Config</c> subdirectory holds it), which a
    /// parents-only walk never reaches: <c>Config</c> is a SIBLING of the path being walked, not an
    /// ancestor of it. The directory itself always wins over its subdirectories, and the search stops
    /// at the first ancestor that yields anything — so a nearer environment is never shadowed by a
    /// more distant one.
    /// </para>
    ///
    /// <para>
    /// The scan goes exactly ONE level deep. Deeper would multiply both the cost and the chance of
    /// adopting some unrelated product's environment; for a layout this does not cover, name the file
    /// with <c>tsenv_path</c> instead of widening the guess.
    /// </para>
    ///
    /// <para>
    /// Several <c>.tsenv</c> files at the same ancestor abort the search with an
    /// <see cref="DetectionResult.Ambiguity"/> instead of picking one: guessing which environment a
    /// station meant would silently redirect every subsequent write to the wrong product's
    /// <c>CommonAppData</c>.
    /// </para>
    /// </summary>
    internal static DetectionResult Detect(string? startPath)
    {
        var probed = new List<string>();

        var dir = StartDirectoryOf(startPath);
        if (dir is null)
            return new DetectionResult { Probed = "(no usable start path)" };

        for (var current = dir; current is not null; current = Path.GetDirectoryName(current))
        {
            probed.Add(current);

            // The directory itself wins: an environment sitting right there is unambiguously the one
            // meant, and must not be weighed against whatever the subdirectories hold.
            var hits = TsenvFilesIn(current);
            if (hits.Count == 0)
            {
                hits = TsenvFilesInSubdirectoriesOf(current);
                if (hits.Count > 0) probed.Add(current + @"\*");
            }

            if (hits.Count == 1)
                return new DetectionResult
                {
                    TsenvPath        = hits[0],
                    FoundInDirectory = Path.GetDirectoryName(hits[0]),
                    Probed           = string.Join(" | ", probed),
                };

            if (hits.Count > 1)
            {
                hits.Sort(StringComparer.OrdinalIgnoreCase);
                return new DetectionResult
                {
                    FoundInDirectory = current,
                    Ambiguity        = $"{hits.Count} .tsenv files at or below {current} " +
                                       $"({string.Join(", ", hits)}) — " +
                                       "pass tsenv_path explicitly to choose one.",
                    Probed           = string.Join(" | ", probed),
                };
            }
        }

        return new DetectionResult { Probed = string.Join(" | ", probed) };
    }

    /// <summary>The <c>.tsenv</c> files directly in <paramref name="directory"/>. An unreadable
    /// directory yields nothing rather than throwing, so one bad folder never aborts the walk.</summary>
    private static List<string> TsenvFilesIn(string directory)
    {
        try { return Directory.GetFiles(directory, "*.tsenv").ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// The <c>.tsenv</c> files in the IMMEDIATE subdirectories of <paramref name="directory"/> — the
    /// "environment lives in a sibling <c>Config</c> folder" case.
    /// <para>
    /// Skipped entirely at a drive root, where the subdirectories are unrelated top-level folders and
    /// a hit would say nothing about the file the caller started from. Hidden directories and reparse
    /// points (junctions, symlinks) are skipped too: the former are not where a station keeps its
    /// configuration, and the latter can point anywhere, including back into the tree.
    /// </para>
    /// </summary>
    private static List<string> TsenvFilesInSubdirectoriesOf(string directory)
    {
        var found = new List<string>();
        if (Path.GetDirectoryName(directory) is null) return found;   // drive root

        string[] subdirectories;
        try { subdirectories = Directory.GetDirectories(directory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return found;
        }

        foreach (var sub in subdirectories)
        {
            try
            {
                var attributes = new DirectoryInfo(sub).Attributes;
                if ((attributes & (FileAttributes.Hidden | FileAttributes.ReparsePoint)) != 0) continue;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            found.AddRange(TsenvFilesIn(sub));
        }

        return found;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The directory a walk-up starts from: the containing folder for a file, the folder itself for a
    /// directory. Falls back to the parent of a non-existent path so detection still works for a
    /// sequence file that is about to be created.
    /// </summary>
    internal static string? StartDirectoryOf(string? startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath)) return null;

        string full;
        try { full = Path.GetFullPath(startPath!.Trim().Trim('"')); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        if (Directory.Exists(full)) return full;
        if (File.Exists(full))      return Path.GetDirectoryName(full);
        // Not on disk (yet): treat a path with an extension as a file, anything else as a directory.
        return Path.HasExtension(full) ? Path.GetDirectoryName(full) : full;
    }

    /// <summary>
    /// Minimal INI reader for the one section we need. Deliberately not a general INI parser: it takes
    /// <c>key = value</c> lines, strips <c>;</c>/<c>#</c> comments and surrounding quotes, and ignores
    /// everything outside <paramref name="section"/>. Keys are matched case-insensitively.
    /// </summary>
    internal static Dictionary<string, string> ReadIniSection(IEnumerable<string> lines, string section)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inSection = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;

            if (line[0] == '[')
            {
                var end = line.IndexOf(']');
                inSection = end > 1 &&
                            line.Substring(1, end - 1).Trim().Equals(section, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSection) continue;

            var sep = line.IndexOf('=');
            if (sep <= 0) continue;

            var key = line.Substring(0, sep).Trim();
            var value = line.Substring(sep + 1).Trim();

            // A trailing comment is only a comment OUTSIDE quotes — a path may legitimately hold '#'.
            if (value.Length > 0 && value[0] != '"')
            {
                var cut = value.IndexOfAny(new[] { ';', '#' });
                if (cut >= 0) value = value.Substring(0, cut).Trim();
            }

            if (key.Length > 0) result[key] = value.Trim().Trim('"').Trim();
        }

        return result;
    }

    /// <summary>
    /// Resolves one <c>[TestStandPaths]</c> entry: expands environment variables and makes a relative
    /// path absolute against the <c>.tsenv</c>'s own directory. Returns "" when the key is absent or
    /// empty, which the caller distinguishes from "present but wrong".
    /// </summary>
    private static string ResolveEntry(IReadOnlyDictionary<string, string> entries, string key, string baseDir)
    {
        if (!entries.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) return "";

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(value.Trim());
            return Path.GetFullPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(baseDir, expanded));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return value.Trim();   // keep the raw text so the caller's "does not exist" names it
        }
    }
}
