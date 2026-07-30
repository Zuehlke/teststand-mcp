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
    // ── A3: comment / description encoding is WINDOWS-1252 ───────────────────────
    // TestStand stores step / sequence / file comments, descriptions AND string values in the
    // Windows-1252 code page (the .seq binary/XML string encoding). Verified empirically by a
    // save -> close -> reopen round-trip: en dash (U+2013), em dash (U+2014), ellipsis (U+2026),
    // bullet (U+2022), Euro (U+20AC), trademark (U+2122), curly quotes ... all SURVIVE -- they are
    // part of Windows-1252 (the printable 0x80-0x9F block ISO-8859-1 lacks). Only code points that
    // Windows-1252 genuinely cannot represent (e.g. arrow U+2192, checkmark U+2713, CJK, emoji)
    // become a literal '?'. This is a FILE-FORMAT limitation, not a write-API one: the same loss
    // happens on expression / string-value writes (SetValString) too, so it cannot be avoided by
    // "writing UTF-8".
    //
    // The guard therefore flags a character iff it is NOT representable in Windows-1252 -- i.e.
    // above U+00FF AND not one of the 27 extra printable code points 1252 maps into 0x80-0x9F. The
    // previous guard used a plain "> U+00FF" (ISO-8859-1) test and so FALSE-flagged U+2013/U+2014/
    // U+2026/U+20AC/U+2122/curly-quotes as lost when they actually round-trip fine.

    /// <summary>The 27 Unicode code points above U+00FF that Windows-1252 maps into its 0x80-0x9F
    /// range (so they DO survive TestStand's comment/string encoding, unlike other &gt; U+00FF chars):
    /// Euro, curly quotes, en/em dash, ellipsis, bullet, dagger, per-mille, trademark, OElig, etc.</summary>
    // Code points (as integers — ASCII-only source, no glyph literals, so the set is byte-exact
    // regardless of how the .cs file is decoded) that Windows-1252 maps into its 0x80-0x9F block.
    private static readonly HashSet<int> Cp1252Extras = new()
    {
        0x20AC,        // 0x80 Euro sign
        0x201A,        // 0x82 single low-9 quotation mark
        0x0192,        // 0x83 latin small letter f with hook (florin)
        0x201E,        // 0x84 double low-9 quotation mark
        0x2026,        // 0x85 horizontal ellipsis
        0x2020, 0x2021,// 0x86/87 dagger / double dagger
        0x02C6,        // 0x88 modifier letter circumflex accent
        0x2030,        // 0x89 per mille sign
        0x0160,        // 0x8A latin capital letter S with caron
        0x2039,        // 0x8B single left-pointing angle quotation mark
        0x0152,        // 0x8C latin capital ligature OE
        0x017D,        // 0x8E latin capital letter Z with caron
        0x2018, 0x2019,// 0x91/92 left/right single quotation mark
        0x201C, 0x201D,// 0x93/94 left/right double quotation mark
        0x2022,        // 0x95 bullet
        0x2013, 0x2014,// 0x96/97 en dash / em dash
        0x02DC,        // 0x98 small tilde
        0x2122,        // 0x99 trade mark sign
        0x0161,        // 0x9A latin small letter s with caron
        0x203A,        // 0x9B single right-pointing angle quotation mark
        0x0153,        // 0x9C latin small ligature oe
        0x017E,        // 0x9E latin small letter z with caron
        0x0178,        // 0x9F latin capital letter Y with diaeresis
    };

    /// <summary>True when <paramref name="ch"/> is representable in Windows-1252 (so it survives a
    /// comment/description/string round-trip): any code point &lt;= U+00FF, plus the 27 extras 1252
    /// maps into 0x80-0x9F (en/em dash, ellipsis, bullet, Euro, trademark, curly quotes, ...).</summary>
    public static bool IsWindows1252Representable(char ch) => ch <= 0xFF || Cp1252Extras.Contains(ch);

    /// <summary>Returns the distinct characters in <paramref name="text"/> that TestStand's
    /// Windows-1252 comment/string encoding cannot represent and would replace with '?'. Empty when
    /// safe. (Name kept for back-compat; the check is Windows-1252-accurate, not plain ISO Latin-1.)</summary>
    public static IReadOnlyList<char> FindNonLatin1Characters(string? text)
    {
        var offending = new List<char>();
        if (string.IsNullOrEmpty(text)) return offending;
        var seen = new HashSet<char>();
        foreach (var ch in text!)
        {
            if (IsWindows1252Representable(ch)) continue; // survives the 1252 round-trip
            if (seen.Add(ch)) offending.Add(ch);          // distinct, first-seen order
        }
        return offending;
    }

    /// <summary>Builds a human-readable warning when <paramref name="text"/> contains characters
    /// that TestStand's Windows-1252 comment/string encoding would replace with '?'. Null when the
    /// text is safe. Characters representable in Windows-1252 (en/em dash, ellipsis, bullet, Euro,
    /// trademark, curly quotes, umlauts, accents, ...) are NOT flagged -- they survive the round-trip.</summary>
    public static string? DescribeLatin1Loss(string? text, string fieldLabel = "text")
    {
        var bad = FindNonLatin1Characters(text);
        if (bad.Count == 0) return null;

        var sb = new StringBuilder();
        sb.Append("WARNING: the ").Append(fieldLabel)
          .Append(" contains character(s) outside Windows-1252 that TestStand stores as '?': ");
        for (int i = 0; i < bad.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append('\'').Append(bad[i]).Append("' (U+")
              .Append(((int)bad[i]).ToString("X4")).Append(')');
        }
        sb.Append(". Use an ASCII/Windows-1252 equivalent (e.g. '->' for a U+2192 arrow, '[x]' for a "
                + "checkmark). Note: en/em dashes, ellipsis, bullet, Euro, trademark and curly quotes "
                + "ARE supported and need no substitution.");
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

    // ── A6: in-process prototype load of a PACKED-LIBRARY VI is process-fatal ────
    // Measured twice (2026-07-29): loading the connector pane of a VI inside a packed library
    // (.lvlibp) IN-PROCESS raises the MSVC delay-load SEH 0xC06D007E for the LabVIEW Run-Time —
    // even with a LabVIEW 2026 32-bit ADE already started and responsive. That fault escapes
    // managed try/catch, so the SERVER PROCESS DIES and the NI Error Reporter appears; the
    // silent-death guards only exist in the isolated worker and cannot help a fault raised in the
    // server itself. The isolated worker is crash-safe but cannot bind the running ADE, so it only
    // times out. There is therefore NO working prototype-load route for such a VI on this station —
    // the connector pane has to be CLONED from a source .seq (copy_step_module, or
    // import_sequence_file's default labview_panes='copy'), which needs no LabVIEW at all.
    //
    // This guard is the reason that fact no longer has to be REMEMBERED: an in-process load of a
    // packed-library VI is refused outright, and the auto-load inside configure_labview_module is
    // skipped for such a VI instead of taking the server down.

    /// <summary>True when a module path points into a LabVIEW packed library (<c>.lvlibp</c>), whose
    /// VIs cannot have their connector pane loaded on this station — in-process the load is
    /// process-fatal, in the isolated worker it times out.</summary>
    public static bool IsPackedLibraryModulePath(string? modulePath) =>
        !string.IsNullOrWhiteSpace(modulePath)
        && modulePath!.IndexOf(".lvlibp", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>The refusal message for an in-process prototype load of a packed-library VI: what
    /// would happen, and the route that actually works.</summary>
    public static string PackedLibraryInProcessLoadRefusal(string stepName, string? viPath) =>
        $"Step '{stepName}' calls a VI inside a packed library" +
        (string.IsNullOrWhiteSpace(viPath) ? "" : $" ('{viPath}')") +
        ". Loading its connector pane IN-PROCESS (isolate=false) raises the native delay-load fault " +
        "0xC06D007E, which escapes managed try/catch and KILLS THE SERVER PROCESS — measured with a " +
        "running, responsive LabVIEW ADE. The isolated worker (isolate=true) cannot bind that ADE and " +
        "only times out, so there is no working load route for such a VI. CLONE the cached connector " +
        "pane instead: copy_step_module from a source .seq that already has the step, or " +
        "import_sequence_file with its default labview_panes='copy' (~1s per step, no LabVIEW). " +
        "Pass force_unsafe_inprocess=true only if you accept losing the server process.";

    /// <summary>The advisory note for a <c>configure_labview_module</c> call on a packed-library VI
    /// whose automatic prototype load was skipped to keep the server alive.</summary>
    public static string PackedLibraryAutoLoadSkippedNote(string? viPath) =>
        "The automatic prototype load was SKIPPED because the VI lives in a packed library" +
        (string.IsNullOrWhiteSpace(viPath) ? "" : $" ('{viPath}')") +
        ", where an in-process load raises the native delay-load fault 0xC06D007E and would take the " +
        "server down. The VI path was still written, so the step is configured. Its connector pane was " +
        "NOT refreshed from the VI: 'parameters' reports whatever was already cached on the step — the " +
        "full pane when you reconfigured an existing step, empty on a freshly inserted one. To produce " +
        "a missing pane, CLONE it: copy_step_module from a source .seq that has the step, or " +
        "import_sequence_file with its default labview_panes='copy'.";

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
