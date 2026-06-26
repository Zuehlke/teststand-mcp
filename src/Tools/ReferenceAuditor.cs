using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TestStandMCP.Models;

namespace TestStandMCP.Tools;

// ── Post-build reference audit (Option B) ────────────────────────────────────
// Scans the expressions ACTUALLY stored on a built sequence and reports every
// Locals./Parameters./FileGlobals. reference that is not declared. This complements
// SequencePlanValidator, which (a) only checks Locals references and (b) only sees
// the build PLAN — so conditions written afterwards via set_flow_condition (which
// land in ConditionExpr/ItemExpr) are invisible to it. The auditor below runs on
// data read from the real sequence, so it covers BOTH insertion paths. Pure logic:
// no COM / no engine — the engine-dependent reads happen in TestStandService and
// are handed in as a ReferenceAuditData.

/// <summary>
/// Deterministic, engine-free auditor that flags undeclared variable references in
/// the expressions of a built sequence.
/// </summary>
public static class ReferenceAuditor
{
    // An auditable scope keyword (Locals/Parameters/FileGlobals) immediately followed by
    // a member name. The negative lookbehind on '.'/identifier chars ensures we only match
    // the keyword as a TOP-LEVEL scope: it skips dynamic references like
    // RunState.Caller.Locals.X (which target a DIFFERENT scope we cannot resolve here) and
    // avoids false positives on identifiers that merely end in the keyword (e.g. "MyLocals").
    private static readonly Regex RefRx = new(
        @"(?<![A-Za-z0-9_.])(Locals|Parameters|FileGlobals)\.([A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);

    /// <summary>Audits engine-read data and returns the undeclared-reference findings.</summary>
    public static ReferenceAuditResult Audit(ReferenceAuditData data)
    {
        var r = new ReferenceAuditResult();
        data ??= new ReferenceAuditData();

        // Per-sequence declared-name lookups (names are matched case-insensitively, like TestStand).
        var locals = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var pars   = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var sc in data.Scopes)
        {
            locals[sc.SequenceName] = new HashSet<string>(sc.Locals     ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            pars[sc.SequenceName]   = new HashSet<string>(sc.Parameters ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        }
        var fileGlobals = new HashSet<string>(data.FileGlobals ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

        // De-dupe identical findings at the same location (e.g. a name referenced twice in one expression).
        var dedupe = new HashSet<string>(StringComparer.Ordinal);

        foreach (var e in data.Expressions)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.Expression)) continue;
            r.Stats.ExpressionsScanned++;

            foreach (Match m in RefRx.Matches(e.Expression))
            {
                r.Stats.ReferencesFound++;
                var scope = m.Groups[1].Value;
                var name  = m.Groups[2].Value;

                bool declared = scope switch
                {
                    "Locals"      => locals.TryGetValue(e.SequenceName, out var ls) && ls.Contains(name),
                    "Parameters"  => pars.TryGetValue(e.SequenceName,   out var ps) && ps.Contains(name),
                    "FileGlobals" => fileGlobals.Contains(name),
                    _             => true
                };
                if (declared) continue;

                var key = string.Join("|", e.SequenceName, scope, name, e.StepGroup, e.StepName, e.Property);
                if (!dedupe.Add(key)) continue;

                r.Issues.Add(new ReferenceIssue
                {
                    Severity     = "error",
                    Code         = scope == "Locals"     ? "E_UNDECLARED_LOCAL"
                                 : scope == "Parameters" ? "E_UNDECLARED_PARAM"
                                 :                         "E_UNDECLARED_FILEGLOBAL",
                    Scope        = scope,
                    Name         = name,
                    SequenceName = e.SequenceName,
                    StepGroup    = e.StepGroup,
                    StepName     = e.StepName,
                    Property     = e.Property,
                    Expression   = e.Expression
                });
            }
        }

        r.Stats.SequencesAudited = data.Scopes.Count;
        r.Stats.UndeclaredCount  = r.Issues.Count;
        r.IssueCount             = r.Issues.Count;
        r.Valid                  = r.Issues.Count == 0;
        return r;
    }
}
