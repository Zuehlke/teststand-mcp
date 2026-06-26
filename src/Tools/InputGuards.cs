using System;
using System.Collections.Generic;
using System.Text;

namespace TestStandMCP.Tools;

/// <summary>
/// Pure, engine-free input guards shared by the MCP tool handlers and service. They encode
/// hard-won TestStand behavioural facts (see CLAUDE.md / memory) directly in the server so the
/// protections travel WITH the MCP — instead of living only in the client's CLAUDE.md/memory and
/// being lost the moment another client (or a fresh session) drives the tools. Every method is
/// static and deterministic, so they are unit-testable without a live TestStand engine.
/// </summary>
public static class InputGuards
{
    // ── A3: comment / description encoding is Latin-1 ────────────────────────────
    // TestStand round-trips step / sequence / file comments through a Windows-1252 / Latin-1
    // path. Characters representable in Latin-1 (ASCII plus the Latin-1 supplement — German
    // umlauts ä ö ü ß, é, °, ± …) survive; anything OUTSIDE it (→ U+2192, — U+2014, • U+2022,
    // … U+2026, and any other code point above U+00FF) is silently written as a literal '?'.
    // We flag exactly the characters above U+00FF: that catches the proven data-loss cases
    // while never false-flagging a Latin-1 character such as an umlaut.

    /// <summary>Returns the distinct characters in <paramref name="text"/> that TestStand's
    /// Latin-1 comment encoding cannot represent and would replace with '?'. Empty when safe.</summary>
    public static IReadOnlyList<char> FindNonLatin1Characters(string? text)
    {
        var offending = new List<char>();
        if (string.IsNullOrEmpty(text)) return offending;
        var seen = new HashSet<char>();
        foreach (var ch in text!)
        {
            if (ch <= 0xFF) continue;            // representable in Latin-1
            if (seen.Add(ch)) offending.Add(ch); // distinct, first-seen order
        }
        return offending;
    }

    /// <summary>Builds a human-readable warning when <paramref name="text"/> contains characters
    /// that TestStand's Latin-1 comment encoding would replace with '?'. Null when the text is safe.</summary>
    public static string? DescribeLatin1Loss(string? text, string fieldLabel = "text")
    {
        var bad = FindNonLatin1Characters(text);
        if (bad.Count == 0) return null;

        var sb = new StringBuilder();
        sb.Append("WARNING: the ").Append(fieldLabel)
          .Append(" contains character(s) outside Latin-1 that TestStand stores as '?': ");
        for (int i = 0; i < bad.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append('\'').Append(bad[i]).Append("' (U+")
              .Append(((int)bad[i]).ToString("X4")).Append(')');
        }
        sb.Append(". Use ASCII equivalents (e.g. -> for the arrow, - for a dash, ... for an ellipsis, * for a bullet).");
        return sb.ToString();
    }

    // ── A2: per-step module unload option ────────────────────────────────────────
    // ModuleUnloadOptions value 5 (UseStepUnloadOption) is only valid at the sequence-file /
    // model level — TestStand REJECTS it on an individual step. Values 1-4 are per-step valid.

    /// <summary>True when <paramref name="unloadOptionValue"/> (a ModuleUnloadOptions numeric value)
    /// is not accepted on an individual step, i.e. 5 = UseStepUnloadOption (file/model-level only).</summary>
    public static bool IsFileLevelOnlyUnloadOption(int unloadOptionValue) => unloadOptionValue == 5;

    // ── A1: None-adapter LabVIEW utility steps ───────────────────────────────────
    // Steps like "Run VI Asynchronously" (NI_LV_RunVIAsynchronously) use the None adapter and
    // store their VI configuration in the step's OWN properties (VIModule.ViCall.VIPath, …), NOT
    // in the adapter module. configure_labview_module switches the step's adapter to LabVIEW and
    // thereby corrupts that configuration. Detect these so the tool can refuse and point at
    // set_step_property instead.

    /// <summary>True when the step is a None-adapter LabVIEW utility step whose configuration lives
    /// in its own properties (e.g. NI_LV_RunVIAsynchronously) — configuring it via the LabVIEW
    /// adapter would switch the adapter and corrupt it.</summary>
    public static bool IsNoneAdapterLabViewUtilityStep(string? stepTypeName, string? adapterKeyName)
    {
        if (string.IsNullOrEmpty(stepTypeName)) return false;
        bool isLvUtilityType =
            stepTypeName!.StartsWith("NI_LV_", StringComparison.OrdinalIgnoreCase);
        if (!isLvUtilityType) return false;
        // A blank/None adapter is the marker for the utility variant. If the adapter is unknown
        // (blank), still treat an NI_LV_ type as a utility step — the LabVIEW-adapter forms use a
        // real adapter key, so a blank/None here means the properties-based utility step.
        bool isNoneAdapter =
            string.IsNullOrWhiteSpace(adapterKeyName) ||
            adapterKeyName!.IndexOf("None", StringComparison.OrdinalIgnoreCase) >= 0;
        return isNoneAdapter;
    }

    // ── A4 / A5: flow-branch condition targets ───────────────────────────────────
    // set_flow_condition and the bulk 'expression' auto-routing only make sense on the flow steps
    // that actually EVALUATE a branch condition. NI_Flow_End (and any non-branch step) cannot hold
    // a branch condition — writing one there is silently dropped (the classic DoWhile-condition-on-
    // End trap: the loop condition belongs on the NI_Flow_DoWhile opener, not its End).

    private static readonly HashSet<string> ConditionExprSteps = new(StringComparer.OrdinalIgnoreCase)
        { "NI_Flow_If", "NI_Flow_ElseIf", "NI_Flow_While", "NI_Flow_DoWhile",
          // A counted For loop keeps its loop-continue test in ConditionExpr too (verified via a
          // property-tree dump: InitializationExpr / ConditionExpr / IncrementExpr). Routing the
          // bulk 'expression' here lets a For be written in one line; init/increment go through
          // ForLoopExtraExpr below. (ForEach/Sweep/Stream use a different property model — excluded.)
          "NI_Flow_For" };

    private static readonly HashSet<string> ItemExprSteps = new(StringComparer.OrdinalIgnoreCase)
        { "NI_Flow_Select", "NI_Flow_Case" };

    /// <summary>The counted For loop — the only loop opener whose init/condition/increment live in
    /// the dedicated InitializationExpr/ConditionExpr/IncrementExpr step properties this server sets.</summary>
    public static bool IsCountedForLoop(string? stepTypeName) =>
        string.Equals(stepTypeName, "NI_Flow_For", StringComparison.OrdinalIgnoreCase);

    /// <summary>The ForEach loop — iterates a collection; its config lives in ArrayExpr (the
    /// collection) and ArrayElementExpr (the per-element variable), plus optional Offset/Subscript.</summary>
    public static bool IsForEachLoop(string? stepTypeName) =>
        string.Equals(stepTypeName, "NI_Flow_ForEach", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the step is an NI_Flow_Case (whose value(s) live in ItemExpr and which
    /// can be marked as the default branch via IsDefault).</summary>
    public static bool IsCaseStep(string? stepTypeName) =>
        string.Equals(stepTypeName, "NI_Flow_Case", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the step type carries an evaluable branch condition
    /// (If/ElseIf/While/DoWhile/For → ConditionExpr, Select/Case → ItemExpr).</summary>
    public static bool IsFlowConditionTarget(string? stepTypeName) =>
        !string.IsNullOrEmpty(stepTypeName) &&
        (ConditionExprSteps.Contains(stepTypeName!) || ItemExprSteps.Contains(stepTypeName!));

    /// <summary>The property that stores a flow step's branch condition — "ConditionExpr" for
    /// If/ElseIf/While/DoWhile, "ItemExpr" for Select/Case, or null when the step carries no
    /// branch condition.</summary>
    public static string? FlowConditionProperty(string? stepTypeName)
    {
        if (string.IsNullOrEmpty(stepTypeName)) return null;
        if (ConditionExprSteps.Contains(stepTypeName!)) return "ConditionExpr";
        if (ItemExprSteps.Contains(stepTypeName!))      return "ItemExpr";
        return null;
    }

    /// <summary>Builds the rejection message when set_flow_condition targets a step that cannot
    /// hold a branch condition (e.g. NI_Flow_End). Null when the step IS a valid condition target.</summary>
    public static string? DescribeInvalidFlowConditionTarget(string? stepName, string? stepTypeName)
    {
        if (IsFlowConditionTarget(stepTypeName)) return null;
        bool isEnd = string.Equals(stepTypeName, "NI_Flow_End", StringComparison.OrdinalIgnoreCase);
        var hint = isEnd
            ? "A flow condition on an NI_Flow_End has no effect — a DoWhile's loop condition belongs on the " +
              "NI_Flow_DoWhile opener, and While/If conditions on their own opener."
            : "set_flow_condition only applies to branch steps (NI_Flow_If/ElseIf/While/DoWhile/For/Select/Case).";
        return $"Step '{stepName}' is '{stepTypeName}' — {hint}";
    }

    // ── A6: freshly inserted NI_Wait has no time set ─────────────────────────────

    /// <summary>True when the step type is an NI_Wait, which does not actually wait until a time /
    /// target is configured (fresh NI_Wait has an empty TimeExpr).</summary>
    public static bool IsWaitStep(string? stepTypeName) =>
        string.Equals(stepTypeName, "NI_Wait", StringComparison.OrdinalIgnoreCase);
}
