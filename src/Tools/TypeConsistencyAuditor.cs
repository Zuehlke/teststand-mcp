using System;
using System.Collections.Generic;
using System.Linq;
using TestStandMCP.Models;

namespace TestStandMCP.Tools;

// ── Type-registration audit ──────────────────────────────────────────────────
// Closes the last blind spot in verifying a rebuilt .seq. Every other check passes on a file that the
// Sequence Editor refuses to open cleanly:
//   • diff_sequence_files (the native FileDiffer) reported identical:true for a file that raises the
//     "type conflict" dialog — it compares CONTENT, and a duplicate/divergent type REGISTRATION is not
//     content.
//   • audit_sequence_references only looks at expressions.
// So the documented remedy was "ask the user to open the file in the editor", which cannot be automated
// (SeqEdit 2026 renders its UI with CEF). This auditor reads the file's TypeUsageList instead, where the
// conflict is plainly visible: the same type name registered twice, or a locally modified copy of a type
// that TestStand will compare against the already-loaded definition on open.
//
// Pure logic, no COM: the engine-dependent reads happen in TestStandService and arrive as
// TypeConsistencyData, so every rule here is unit-testable without a live engine.

/// <summary>
/// Deterministic, engine-free auditor for a sequence file's TYPE REGISTRATIONS — the defect class the
/// FileDiffer cannot see.
/// </summary>
public static class TypeConsistencyAuditor
{
    /// <summary>Audits engine-read type registrations and returns the findings, errors first.</summary>
    public static TypeConsistencyResult Audit(TypeConsistencyData data)
    {
        data ??= new TypeConsistencyData();
        var r = new TypeConsistencyResult();
        var issues = new List<TypeConsistencyIssue>();

        var byName = data.File
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        r.Stats.TypeRegistrations = data.File.Count;
        r.Stats.DistinctTypeNames = byName.Count;
        r.Stats.AttachedTypes     = data.File.Count(t => t.Attached);
        r.Stats.ModifiedTypes     = data.File.Count(t => t.IsModified);
        r.Stats.ComparedAgainst   = data.ReferencePath;

        // 1) THE conflict: one name, several registrations. This is what the editor's dialog reports —
        // "… conflicts with the type already loaded" — and what cloning a typed PropertyObject produces.
        foreach (var g in byName.Where(g => g.Count() > 1))
        {
            r.Stats.DuplicateNames++;
            var entries = g.OrderBy(t => t.Index).ToList();
            issues.Add(new TypeConsistencyIssue
            {
                Code     = "E_DUPLICATE_TYPE_NAME",
                TypeName = g.Key,
                Category = entries[0].Category,
                Severity = "error",
                Detail =
                    $"registered {entries.Count} times (TypeUsageList indices " +
                    string.Join(", ", entries.Select(e => e.Index)) + "; versions " +
                    string.Join(", ", entries.Select(e => Show(e.TypeVersion))) +
                    "). A second registration of the same type name is what makes the Sequence Editor " +
                    "raise a type-conflict dialog on open. Cause is almost always a whole-subtree clone " +
                    "onto a node that carries a NAMED TYPE — write the leaf scalars by value instead."
            });
        }

        // 2) A locally MODIFIED copy of a type. Not automatically a defect (a file may legitimately own a
        // modified type), but it is the state TestStand compares against the loaded definition on open,
        // so it is the second-best predictor of the dialog and worth naming.
        foreach (var t in data.File.Where(t => t.IsModified))
            issues.Add(new TypeConsistencyIssue
            {
                Code     = "W_MODIFIED_TYPE",
                TypeName = t.Name,
                Category = t.Category,
                Severity = "warning",
                Detail =
                    $"TestStand flags this type as locally MODIFIED (index {t.Index}, version " +
                    $"{Show(t.TypeVersion)}). On open, TestStand compares it against the definition " +
                    "already loaded in the engine and prompts when they differ. Expected if you edited " +
                    "the type on purpose; suspicious in a 1:1 rebuild, which should carry the original " +
                    "definition unchanged."
            });

        // 3) Against a reference file (the rebuild's source): same name, different definition.
        if (data.Reference != null)
        {
            var refByName = data.Reference
                .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var g in byName)
            {
                if (!refByName.TryGetValue(g.Key, out var refType))
                {
                    issues.Add(new TypeConsistencyIssue
                    {
                        Code     = "W_TYPE_ONLY_IN_FILE",
                        TypeName = g.Key,
                        Category = g.First().Category,
                        Severity = "warning",
                        Detail =
                            "present in the audited file but not in the reference. Expected after an " +
                            "import with keep_unused_types=true, which re-attaches types the save would " +
                            "have dropped; unexpected otherwise."
                    });
                    continue;
                }

                var mine = g.OrderBy(t => t.Index).First();

                if (!VersionsMatch(mine.TypeVersion, refType.TypeVersion))
                {
                    issues.Add(new TypeConsistencyIssue
                    {
                        Code     = "E_TYPE_VERSION_MISMATCH",
                        TypeName = g.Key,
                        Category = mine.Category,
                        Severity = "error",
                        Detail =
                            $"version {Show(mine.TypeVersion)} here vs {Show(refType.TypeVersion)} in " +
                            "the reference. Same name, different definition — the file carries its own " +
                            "variant of the type, which conflicts with the reference's whenever both are " +
                            "loaded. copy_typedefs clones a type by GUID and preserves its version, so a " +
                            "mismatch means something re-created the type instead of copying it."
                    });
                    continue;   // the version difference is the finding; a member delta adds nothing
                }

                if (mine.MemberSignature != null && refType.MemberSignature != null
                    && !string.Equals(mine.MemberSignature, refType.MemberSignature, StringComparison.Ordinal))
                    issues.Add(new TypeConsistencyIssue
                    {
                        Code     = "W_TYPE_STRUCTURE_MISMATCH",
                        TypeName = g.Key,
                        Category = mine.Category,
                        Severity = "warning",
                        Detail =
                            $"same version ({Show(mine.TypeVersion)}) but different members — here " +
                            $"[{mine.MemberSignature}], reference [{refType.MemberSignature}]. A type " +
                            "edited without a version bump; both definitions claim to be the same type."
                    });
            }

            foreach (var name in refByName.Keys.Where(
                         n => !byName.Any(g => string.Equals(g.Key, n, StringComparison.OrdinalIgnoreCase))))
                issues.Add(new TypeConsistencyIssue
                {
                    Code     = "W_TYPE_ONLY_IN_REFERENCE",
                    TypeName = name,
                    Category = refByName[name].Category,
                    Severity = "warning",
                    Detail =
                        "present in the reference but missing from the audited file. A type survives a " +
                        "save only if it is attached or still referenced, so this is the expected shape " +
                        "of a SUBSET rebuild — and a real loss in a whole-file one."
                });
        }

        // Errors first: the duplicate registrations are what actually break opening the file.
        r.Issues.AddRange(issues.Where(i => i.Severity == "error"));
        r.Issues.AddRange(issues.Where(i => i.Severity != "error"));
        r.ErrorCount = r.Issues.Count(i => i.Severity == "error");
        r.IssueCount = r.Issues.Count;
        r.Valid      = r.ErrorCount == 0;
        r.Note       = BuildNote(r);
        return r;
    }

    private static bool VersionsMatch(string? a, string? b)
    {
        // Treat null/empty as "not reported" rather than as a difference: an unreadable version on
        // either side must not manufacture an error.
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return true;
        return string.Equals(a!.Trim(), b!.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string Show(string? v) => string.IsNullOrWhiteSpace(v) ? "(none)" : v!;

    private static string BuildNote(TypeConsistencyResult r)
    {
        string what = r.ErrorCount > 0
            ? $"{r.ErrorCount} type-registration ERROR(S): this file will very likely raise a " +
              "type-conflict dialog when it is opened in the Sequence Editor."
            : "No conflicting type registration found.";

        return what +
            " This audit reads the file's TypeUsageList, which is where a type conflict lives — the " +
            "native FileDiffer cannot see it and reports such a file as identical. It is therefore the " +
            "complement to diff_sequence_files, not a replacement: the diff proves the CONTENT matches, " +
            "this proves the TYPE REGISTRY is sane. Neither replaces opening a file once in the editor " +
            "when it matters, but a clean result here removes the only defect class that used to require it.";
    }
}
