using System;
using System.Collections.Generic;
using System.Linq;
using TestStandMCP.Models;

namespace TestStandMCP.Tools;

/// <summary>
/// Shapes a native FileDiffer report for consumption over MCP: category classification, filtering,
/// grouping and a summary-only mode.
/// <para>
/// WHY: the raw report is one flat list of every leaf difference. On a real 30-sequence file a
/// first-pass rebuild produced 593 differences ≈ 165 000 characters of JSON — three times over the
/// tool-result budget, so the report could not be read at all and had to be post-processed with an
/// external script. Verification is an ITERATIVE activity (diff → fix → diff), so the diff has to be
/// readable at every iteration, and the interesting question is almost never "list all 593" but
/// "how many, of what kind, and which ones are actually reducible".
/// </para>
/// Pure logic, no engine dependency — unit-testable.
/// </summary>
public static class DiffReportShaper
{
    // Categories a difference can be classified into. Stable, machine-friendly slugs that double as
    // the filter vocabulary of diff_sequence_files.

    /// <summary>LabVIEW connector-pane / VI metadata — irreducible headless.</summary>
    public const string CatLabViewViCall   = "labview_vicall";
    /// <summary>Python adapter module configuration and its argument entries.</summary>
    public const string CatPythonModule    = "python_module";
    /// <summary>SequenceCall actual-argument bindings.</summary>
    public const string CatSeqCallArgs     = "seqcall_args";
    /// <summary>Any other code-module property.</summary>
    public const string CatModuleOther     = "module_other";
    /// <summary>Step-level properties (precondition, run mode, expressions, …).</summary>
    public const string CatStepProperties  = "step_properties";
    /// <summary>Locals, Parameters and file/station globals.</summary>
    public const string CatVariables       = "variables";
    /// <summary>Sequence-level properties (description, result recording, failure action).</summary>
    public const string CatSequenceProps   = "sequence_properties";
    /// <summary>File-level properties and the file attribute namespace.</summary>
    public const string CatFileProperties  = "file_properties";
    /// <summary>Custom data type definitions.</summary>
    public const string CatTypes           = "types";
    /// <summary>Anything not otherwise classified.</summary>
    public const string CatOther           = "other";

    /// <summary>
    /// Classifies a difference by the part of the file it belongs to. The category names double as
    /// the filter vocabulary — <c>labview_vicall</c> in particular lets a caller drop the LabVIEW
    /// connector-pane metadata that CANNOT be regenerated headlessly (a VI inside a packed library
    /// never loads), which is otherwise the single largest and least actionable block.
    /// </summary>
    public static string Categorize(FileDifferChange c)
    {
        string p = c.Path ?? "";
        bool Has(string s) => p.Contains(s, StringComparison.OrdinalIgnoreCase);

        if (Has("ViCall") || Has("Connector Pane") || Has("LabVIEW Module"))          return CatLabViewViCall;
        if (Has("Python Adapter Properties") || Has("PythonCall"))                    return CatPythonModule;
        if (Has("Actual Arguments") || Has("ActualArgs"))                             return CatSeqCallArgs;
        if (Has("Module Properties") || Has("Module"))                                return CatModuleOther;
        if (Has("> Locals") || Has("> Parameters") || Has("FileGlobals")
            || Has("Global"))                                                         return CatVariables;
        if (Has("Step Properties") || Has("> TS >"))                                  return CatStepProperties;
        if (Has("File Properties") || Has("Attributes"))                              return CatFileProperties;
        if (Has("Types") || Has("Data Types"))                                        return CatTypes;
        // A change directly under "Sequences > <name>" with no deeper marker is a sequence property
        // (Record Results, failure action, description…).
        if (Has("Sequences >"))                                                       return CatSequenceProps;
        return CatOther;
    }

    /// <summary>The sequence name a difference belongs to, or "" when it is file-level.</summary>
    public static string SequenceOf(FileDifferChange c)
    {
        var parts = (c.Path ?? "").Split(" > ", StringSplitOptions.None);
        for (int i = 0; i < parts.Length - 1; i++)
            if (string.Equals(parts[i].Trim(), "Sequences", StringComparison.OrdinalIgnoreCase))
                return parts[i + 1].Trim();
        return "";
    }

    /// <summary>Options controlling how a report is shaped.</summary>
    public sealed class Options
    {
        /// <summary>Return only tallies (no individual differences).</summary>
        public bool SummaryOnly { get; init; }
        /// <summary>Group the returned differences: "none" (default), "category" or "sequence".</summary>
        public string GroupBy { get; init; } = "none";
        /// <summary>Keep only these categories (empty = keep all).</summary>
        public IReadOnlyCollection<string> IncludeCategories { get; init; } = Array.Empty<string>();
        /// <summary>Drop these categories.</summary>
        public IReadOnlyCollection<string> ExcludeCategories { get; init; } = Array.Empty<string>();
        /// <summary>Keep only differences whose path contains this text (case-insensitive).</summary>
        public string? PathFilter { get; init; }
        /// <summary>Keep only these change types (Insert/Delete/ValueChange/…); empty = all.</summary>
        public IReadOnlyCollection<string> ChangeTypes { get; init; } = Array.Empty<string>();
        /// <summary>Maximum number of differences to return (0 = unlimited). Defaults to
        /// <see cref="DefaultMaxResults"/> so an unshaped call (compare_sequence_files in native mode)
        /// is capped too — the cap has to be the DEFAULT, not something the caller must remember.</summary>
        public int MaxResults { get; init; } = DefaultMaxResults;
    }

    /// <summary>
    /// The DEFAULT row cap for a diff response. A whole-file rebuild comparison legitimately produces
    /// 600+ differences, and returning them all does not fit one tool result — the answer gets
    /// truncated by the transport instead of by the tool, which loses the tallies that actually say
    /// where the work is. So the cap is applied by default and the truncation is reported; pass
    /// max_results=0 for the unlimited behaviour. This used to be a "always start with
    /// summary_only=true" rule in CLAUDE.md that nothing enforced.
    /// </summary>
    public const int DefaultMaxResults = 150;

    /// <summary>
    /// Applies <paramref name="opts"/> to <paramref name="report"/> and returns the MCP payload.
    /// <c>totalDifferences</c> always reports the UNFILTERED total, and a filtered/truncated response
    /// always states what was dropped — a silently shortened diff would read as "almost identical".
    /// </summary>
    public static Dictionary<string, object?> Shape(FileDifferReport report, Options opts)
    {
        var all = report.Changes ?? new List<FileDifferChange>();

        var classified = all.Select(c => (Change: c, Category: Categorize(c), Sequence: SequenceOf(c)))
                            .ToList();

        var byCategory = classified.GroupBy(x => x.Category)
                                   .OrderByDescending(g => g.Count())
                                   .ToDictionary(g => g.Key, g => g.Count());
        var byChangeType = all.GroupBy(c => string.IsNullOrEmpty(c.ChangeType) ? "Unknown" : c.ChangeType)
                              .OrderByDescending(g => g.Count())
                              .ToDictionary(g => g.Key, g => g.Count());
        var bySequence = classified.Where(x => x.Sequence.Length > 0)
                                   .GroupBy(x => x.Sequence)
                                   .OrderByDescending(g => g.Count())
                                   .ToDictionary(g => g.Key, g => g.Count());

        var payload = new Dictionary<string, object?>
        {
            ["file1"]            = report.File1,
            ["file2"]            = report.File2,
            ["totalDifferences"] = report.TotalDifferences,
            ["identical"]        = report.Identical,
            ["fileSummaries"]    = report.FileSummaries,
            ["byCategory"]       = byCategory,
            ["byChangeType"]     = byChangeType,
            ["bySequence"]       = bySequence,
        };

        if (opts.SummaryOnly)
        {
            payload["note"] = "summary_only=true — tallies only. Re-run with a category/path filter " +
                              "to see individual differences.";
            return payload;
        }

        IEnumerable<(FileDifferChange Change, string Category, string Sequence)> q = classified;

        if (opts.IncludeCategories.Count > 0)
        {
            var inc = new HashSet<string>(opts.IncludeCategories, StringComparer.OrdinalIgnoreCase);
            q = q.Where(x => inc.Contains(x.Category));
        }
        if (opts.ExcludeCategories.Count > 0)
        {
            var exc = new HashSet<string>(opts.ExcludeCategories, StringComparer.OrdinalIgnoreCase);
            q = q.Where(x => !exc.Contains(x.Category));
        }
        if (!string.IsNullOrWhiteSpace(opts.PathFilter))
            q = q.Where(x => (x.Change.Path ?? "")
                    .Contains(opts.PathFilter!, StringComparison.OrdinalIgnoreCase));
        if (opts.ChangeTypes.Count > 0)
        {
            var ct = new HashSet<string>(opts.ChangeTypes, StringComparer.OrdinalIgnoreCase);
            q = q.Where(x => ct.Contains(x.Change.ChangeType ?? ""));
        }

        var filtered = q.ToList();
        int matched  = filtered.Count;

        var limited = opts.MaxResults > 0 ? filtered.Take(opts.MaxResults).ToList() : filtered;

        payload["matchedDifferences"]  = matched;
        payload["returnedDifferences"] = limited.Count;
        if (matched < all.Count)
            payload["filteredOut"] = all.Count - matched;
        if (limited.Count < matched)
            payload["truncated"] = true;

        // Never let a filtered or truncated answer look complete.
        var notes = new List<string>();
        if (matched < all.Count)
            notes.Add($"Filters dropped {all.Count - matched} of {all.Count} differences — " +
                      "'byCategory' above still counts ALL of them.");
        if (limited.Count < matched)
            notes.Add($"Only the first {limited.Count} of {matched} matching differences are listed " +
                      $"(max_results; the default is {DefaultMaxResults} because a full list does not " +
                      "fit one tool result). Narrow the filter, page with a path_filter/category, or " +
                      "pass a higher max_results (0 = unlimited) to see the rest.");
        if (notes.Count > 0) payload["note"] = string.Join(" ", notes);

        object Project((FileDifferChange Change, string Category, string Sequence) x) => new
        {
            changeType = x.Change.ChangeType,
            category   = x.Category,
            sequence   = x.Sequence.Length > 0 ? x.Sequence : null,
            path       = x.Change.Path,
            name       = x.Change.Name,
            level      = x.Change.Level,
            file1Value = x.Change.File1Value,
            file2Value = x.Change.File2Value,
        };

        switch ((opts.GroupBy ?? "none").Trim().ToLowerInvariant())
        {
            case "category":
                payload["groups"] = limited.GroupBy(x => x.Category)
                    .OrderByDescending(g => g.Count())
                    .Select(g => new { key = g.Key, count = g.Count(), differences = g.Select(Project).ToList() })
                    .ToList();
                break;
            case "sequence":
                payload["groups"] = limited.GroupBy(x => x.Sequence.Length > 0 ? x.Sequence : "(file level)")
                    .OrderByDescending(g => g.Count())
                    .Select(g => new { key = g.Key, count = g.Count(), differences = g.Select(Project).ToList() })
                    .ToList();
                break;
            default:
                payload["differences"] = limited.Select(Project).ToList();
                break;
        }
        return payload;
    }
}
