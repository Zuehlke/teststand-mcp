using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestStandMCP.Tools;

// ── Build-Plan validation (Phase 3 gate) ─────────────────────────────────────
// Deterministic, engine-independent validation of a sequence "build plan"
// before it is written to TestStand. Same input shape as insert_steps_bulk so
// the agent validates and then builds the identical step array. Pure logic —
// no COM / no engine connection required, which is what makes it reproducible.

/// <summary>A single step as described by the build plan (mirrors a bulk step spec).</summary>
public class PlanStepInput
{
    /// <summary>Step name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Step type.</summary>
    public string StepType { get; set; } = "";
    /// <summary>Step expression / flow condition, if any.</summary>
    public string? Expression { get; set; }
    /// <summary>Target sequence for a SequenceCall step, if linked.</summary>
    public string? TargetSequenceName { get; set; }
    /// <summary>Target sequence file for a SequenceCall step, if linked.</summary>
    public string? TargetSequenceFile { get; set; }
    /// <summary>Step comment, if any.</summary>
    public string? Comment { get; set; }
}

/// <summary>A single validation finding (error or warning) with optional step location.</summary>
public class PlanValidationIssue
{
    /// <summary>Severity: "error" or "warning".</summary>
    public string Severity { get; set; } = "error";
    /// <summary>Machine-readable issue code (e.g. E_UNCLOSED_BLOCK).</summary>
    public string Code { get; set; } = "";
    /// <summary>Index of the step the issue relates to, if any.</summary>
    public int? StepIndex { get; set; }
    /// <summary>Name of the step the issue relates to, if any.</summary>
    public string? StepName { get; set; }
    /// <summary>Human-readable message.</summary>
    public string Message { get; set; } = "";
}

/// <summary>Aggregate statistics about a validated plan.</summary>
public class PlanValidationStats
{
    /// <summary>Total number of steps.</summary>
    public int StepCount { get; set; }
    /// <summary>Number of NI_Flow_* steps.</summary>
    public int FlowSteps { get; set; }
    /// <summary>Number of action steps.</summary>
    public int ActionSteps { get; set; }
    /// <summary>Number of test steps.</summary>
    public int TestSteps { get; set; }
    /// <summary>Number of SequenceCall steps with no target (placeholders).</summary>
    public int UnlinkedSequenceCalls { get; set; }
    /// <summary>Number of declared local variables.</summary>
    public int LocalsDeclared { get; set; }
    /// <summary>Maximum flow-block nesting depth.</summary>
    public int MaxNestingDepth { get; set; }
}

/// <summary>Outcome of validating a build plan: validity flag, counts, issues and stats.</summary>
public class PlanValidationResult
{
    /// <summary>True when the plan has no errors.</summary>
    public bool Valid { get; set; }
    /// <summary>Number of errors.</summary>
    public int ErrorCount { get; set; }
    /// <summary>Number of warnings.</summary>
    public int WarningCount { get; set; }
    /// <summary>The error-severity issues.</summary>
    public List<PlanValidationIssue> Errors { get; init; } = new();
    /// <summary>The warning-severity issues.</summary>
    public List<PlanValidationIssue> Warnings { get; init; } = new();
    /// <summary>Aggregate plan statistics.</summary>
    public PlanValidationStats Stats { get; init; } = new();
}

/// <summary>
/// Deterministic, engine-free validator for a sequence build plan. Checks flow-block
/// balance, forbidden/unknown step types, duplicate names and local-variable references.
/// </summary>
public static class SequencePlanValidator
{
    private static readonly HashSet<string> Openers = new(StringComparer.OrdinalIgnoreCase)
        { "NI_Flow_If", "NI_Flow_While", "NI_Flow_DoWhile", "NI_Flow_For",
          "NI_Flow_ForEach", "NI_Flow_Select",
          "NI_Flow_SweepLoop", "NI_Flow_StreamLoop" };

    private static readonly HashSet<string> LoopTypes = new(StringComparer.OrdinalIgnoreCase)
        { "NI_Flow_While", "NI_Flow_DoWhile", "NI_Flow_For", "NI_Flow_ForEach",
          "NI_Flow_SweepLoop", "NI_Flow_StreamLoop" };

    // Flow steps that carry a boolean condition (warn when the expression is empty).
    private static readonly HashSet<string> ConditionBearing = new(StringComparer.OrdinalIgnoreCase)
        { "NI_Flow_If", "NI_Flow_ElseIf", "NI_Flow_While", "NI_Flow_DoWhile" };

    private static readonly HashSet<string> TestTypes = new(StringComparer.OrdinalIgnoreCase)
        { "NumericLimitTest", "StringValueTest", "PassFailTest", "NI_MultipleNumericLimitTest" };

    private static readonly HashSet<string> ActionTypes = new(StringComparer.OrdinalIgnoreCase)
        { "Statement", "Action", "MessagePopup", "CallExecutable", "SequenceCall", "NI_Wait" };

    private static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase)
        { "Goto", "Label" };

    // All non-flow types that are otherwise recognised (for the "unknown type" check).
    private static bool IsFlowType(string t) =>
        t.StartsWith("NI_Flow_", StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownType(string t) =>
        IsFlowType(t) || TestTypes.Contains(t) || ActionTypes.Contains(t) || Forbidden.Contains(t);

    private static readonly Regex LocalRef =
        new(@"Locals\.([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    private sealed class Block
    {
        public string Type { get; init; } = "";
        public string Name { get; init; } = "";
        public int Index { get; init; }
        public bool ElseSeen { get; set; }
    }

    /// <summary>Validates a build plan and returns its issues and statistics.</summary>
    public static PlanValidationResult Validate(
        string sequenceName, IReadOnlyList<PlanStepInput> steps, IReadOnlyList<string> localNames)
    {
        var r = new PlanValidationResult();
        void Err(string code, string msg, int? idx = null, string? name = null) =>
            r.Errors.Add(new PlanValidationIssue { Severity = "error", Code = code, Message = msg, StepIndex = idx, StepName = name });
        void Warn(string code, string msg, int? idx = null, string? name = null) =>
            r.Warnings.Add(new PlanValidationIssue { Severity = "warning", Code = code, Message = msg, StepIndex = idx, StepName = name });

        if (string.IsNullOrWhiteSpace(sequenceName))
            Err("E_NO_SEQUENCE", "Plan has no sequenceName.");

        var locals = new HashSet<string>(localNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        r.Stats.LocalsDeclared = locals.Count;
        r.Stats.StepCount = steps?.Count ?? 0;

        if (steps == null || steps.Count == 0)
        {
            Err("E_NO_STEPS", "Plan contains no steps.");
            Finish(r);
            return r;
        }

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<Block>();
        var referencedLocals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int unlinkedSequenceCalls = 0;

        for (int i = 0; i < steps.Count; i++)
        {
            var s     = steps[i];
            var name  = (s.Name ?? "").Trim();
            var type  = (s.StepType ?? "").Trim();
            var label = name.Length == 0 ? $"#{i}" : name;

            if (name.Length == 0) Err("E_EMPTY_NAME", "Step has an empty name.", i, null);
            if (type.Length == 0) { Err("E_EMPTY_TYPE", "Step has an empty stepType.", i, label); continue; }

            if (name.Length > 0 && !seenNames.Add(name))
                Err("E_DUP_NAME", $"Duplicate step name '{name}' — names must be unique (lookups by name break otherwise).", i, label);

            if (Forbidden.Contains(type))
                Err("E_FORBIDDEN_TYPE", $"Step type '{type}' is forbidden (CLAUDE.md). Use NI_Flow_* constructs instead.", i, label);
            else if (!IsKnownType(type))
                Err("E_UNKNOWN_TYPE", $"Unknown step type '{type}'.", i, label);

            // Category stats
            if (IsFlowType(type)) r.Stats.FlowSteps++;
            else if (TestTypes.Contains(type)) r.Stats.TestSteps++;
            else if (ActionTypes.Contains(type)) r.Stats.ActionSteps++;

            // Unlinked SequenceCall placeholders — tallied here to avoid a second pass.
            if (type.Equals("SequenceCall", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(s.TargetSequenceName))
                unlinkedSequenceCalls++;

            // Collect Locals.X references from the condition/expression
            if (!string.IsNullOrEmpty(s.Expression))
                foreach (Match m in LocalRef.Matches(s.Expression))
                    referencedLocals.Add(m.Groups[1].Value);

            // ── Flow structure bookkeeping ───────────────────────────────────
            if (Openers.Contains(type))
            {
                if (ConditionBearing.Contains(type) && string.IsNullOrWhiteSpace(s.Expression))
                    Warn("W_NO_CONDITION", $"{type} '{label}' has no condition expression — TestStand will use its default.", i, label);
                stack.Push(new Block { Type = type, Name = name, Index = i });
                if (stack.Count > r.Stats.MaxNestingDepth) r.Stats.MaxNestingDepth = stack.Count;
            }
            else if (type.Equals("NI_Flow_ElseIf", StringComparison.OrdinalIgnoreCase) ||
                     type.Equals("NI_Flow_Else",   StringComparison.OrdinalIgnoreCase))
            {
                var top = stack.Count > 0 ? stack.Peek() : null;
                if (top == null || !top.Type.Equals("NI_Flow_If", StringComparison.OrdinalIgnoreCase))
                    Err("E_ELSE_WITHOUT_IF", $"{type} '{label}' is not inside an NI_Flow_If block.", i, label);
                else if (top.ElseSeen)
                    Err("E_ELSE_ORDER", $"{type} '{label}' appears after an NI_Flow_Else in the same If block.", i, label);
                else if (type.Equals("NI_Flow_Else", StringComparison.OrdinalIgnoreCase))
                    top.ElseSeen = true;

                if (ConditionBearing.Contains(type) && string.IsNullOrWhiteSpace(s.Expression))
                    Warn("W_NO_CONDITION", $"{type} '{label}' has no condition expression.", i, label);
            }
            else if (type.Equals("NI_Flow_Case", StringComparison.OrdinalIgnoreCase))
            {
                // A Case opens its OWN block that must be closed by a matching NI_Flow_End
                // before the next Case — unlike ElseIf/Else, which are clauses of an If and
                // need no End of their own. TestStand renders Cases as siblings ONLY when each
                // is explicitly closed; with a single End for the whole Select the editor nests
                // each Case inside the previous one. The Case's immediate parent must therefore
                // be the Select itself: if the previous Case was not closed, the top of the
                // stack is that still-open Case (not the Select) — the error we report here.
                var top = stack.Count > 0 ? stack.Peek() : null;
                if (top == null || !top.Type.Equals("NI_Flow_Select", StringComparison.OrdinalIgnoreCase))
                    Err("E_CASE_WITHOUT_SELECT",
                        $"NI_Flow_Case '{label}' is not directly inside an NI_Flow_Select block — each Case must be closed by its own NI_Flow_End before the next Case (or the Select's End).",
                        i, label);
                stack.Push(new Block { Type = type, Name = name, Index = i });
                if (stack.Count > r.Stats.MaxNestingDepth) r.Stats.MaxNestingDepth = stack.Count;
            }
            else if (type.Equals("NI_Flow_End", StringComparison.OrdinalIgnoreCase))
            {
                if (stack.Count == 0)
                    Err("E_UNMATCHED_END", $"NI_Flow_End '{label}' has no matching opener.", i, label);
                else
                    stack.Pop();
            }
            else if (type.Equals("NI_Flow_Break",    StringComparison.OrdinalIgnoreCase) ||
                     type.Equals("NI_Flow_Continue", StringComparison.OrdinalIgnoreCase))
            {
                bool inLoop = stack.Any(b => LoopTypes.Contains(b.Type));
                // Break may also terminate a Select; Continue is loop-only.
                bool inSelect = stack.Any(b => b.Type.Equals("NI_Flow_Select", StringComparison.OrdinalIgnoreCase));
                bool ok = inLoop || (type.Equals("NI_Flow_Break", StringComparison.OrdinalIgnoreCase) && inSelect);
                if (!ok)
                    Err("E_JUMP_OUTSIDE_LOOP", $"{type} '{label}' is not inside a loop.", i, label);
            }
        }

        // Unclosed flow blocks
        while (stack.Count > 0)
        {
            var b = stack.Pop();
            Err("E_UNCLOSED_BLOCK", $"{b.Type} '{b.Name}' is never closed by an NI_Flow_End.", b.Index, b.Name);
        }

        // Locals referenced in expressions but not declared
        foreach (var refName in referencedLocals)
            if (!locals.Contains(refName))
                Err("E_UNDECLARED_LOCAL", $"Expression references Locals.{refName}, which is not declared in the plan's locals.");

        // Declared locals never used (informational)
        foreach (var dec in locals)
            if (!referencedLocals.Contains(dec))
                Warn("W_UNUSED_LOCAL", $"Local '{dec}' is declared but never referenced by any step expression.");

        // Unlinked SequenceCalls (informational — this is the intended placeholder pattern)
        r.Stats.UnlinkedSequenceCalls = unlinkedSequenceCalls;
        if (unlinkedSequenceCalls > 0)
            Warn("W_UNLINKED_CALLS", $"{unlinkedSequenceCalls} SequenceCall step(s) have no target (unresolved placeholders — link later).");

        Finish(r);
        return r;
    }

    private static void Finish(PlanValidationResult r)
    {
        r.ErrorCount   = r.Errors.Count;
        r.WarningCount = r.Warnings.Count;
        r.Valid        = r.ErrorCount == 0;
    }
}
