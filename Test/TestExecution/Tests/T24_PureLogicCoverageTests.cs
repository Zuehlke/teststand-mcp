using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TestStandMCP.Services;
using TestStandMCP.Tools;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// Pure (engine-free) unit tests filling coverage gaps in branchy helper logic that was
/// previously only exercised indirectly through the COM-bound integration tests:
///   • TestStandService.ReplaceString  — find/replace string engine (case, whole-word, regex, fallback)
///   • TestStandService.MakeRelativePath — sequence-file path relativisation
///   • TestStandService.ResolveAdapterKeyName — friendly-name → adapter-key aliasing
///   • TestStandService.ResolvePaletteName — step-type → type-palette resolution rules
///   • SequencePlanValidator — the remaining validation rules not covered by T10/T23
/// Does NOT inherit TestBase, so no TestStand engine is connected.
/// </summary>
[TestFixture]
[Category("PureLogic")]
public class T24_PureLogicCoverageTests
{
    // ── ReplaceString: case / whole-word / regex / invalid-regex fallback ────────

    [Test]
    public void ReplaceString_CaseSensitive_OnlyExactCaseReplaced()
    {
        var r = TestStandService.ReplaceString("abc ABC", "abc", "X",
            matchCase: true, wholeWord: false, regex: false);
        Assert.That(r, Is.EqualTo("X ABC"));
    }

    [Test]
    public void ReplaceString_CaseInsensitive_ReplacesBothCases()
    {
        var r = TestStandService.ReplaceString("abc ABC", "abc", "X",
            matchCase: false, wholeWord: false, regex: false);
        Assert.That(r, Is.EqualTo("X X"));
    }

    [Test]
    public void ReplaceString_WholeWord_DoesNotMatchSubstring()
    {
        var r = TestStandService.ReplaceString("abc abcd", "abc", "X",
            matchCase: true, wholeWord: true, regex: false);
        Assert.That(r, Is.EqualTo("X abcd"), "the substring inside 'abcd' must not match a whole word");
    }

    [Test]
    public void ReplaceString_LiteralMode_EscapesRegexMetacharacters()
    {
        // regex:false → the '.' is a literal dot, NOT the regex any-char.
        var r = TestStandService.ReplaceString("axb.c", ".", "-",
            matchCase: true, wholeWord: false, regex: false);
        Assert.That(r, Is.EqualTo("axb-c"));
    }

    [Test]
    public void ReplaceString_RegexMode_AppliesPattern()
    {
        var r = TestStandService.ReplaceString("a1b2c3", @"\d", "#",
            matchCase: true, wholeWord: false, regex: true);
        Assert.That(r, Is.EqualTo("a#b#c#"));
    }

    [Test]
    public void ReplaceString_InvalidRegex_FallsBackToLiteralReplace()
    {
        // "[" is an invalid regex → must not throw; falls back to a plain literal replace.
        var r = TestStandService.ReplaceString("a[b", "[", "X",
            matchCase: true, wholeWord: false, regex: true);
        Assert.That(r, Is.EqualTo("aXb"));
    }

    [Test]
    public void ReplaceString_EmptyInput_ReturnedUnchanged()
    {
        Assert.That(TestStandService.ReplaceString("", "a", "b", true, false, false), Is.EqualTo(""));
    }

    // ── MakeRelativePath: relativisation rules + passthroughs ────────────────────

    [Test]
    public void MakeRelativePath_SubdirectoryTarget_BecomesRelative()
    {
        var r = TestStandService.MakeRelativePath(@"C:\proj\seq", @"C:\proj\seq\sub\Test.seq");
        Assert.That(r, Is.EqualTo(@"sub\Test.seq"));
    }

    [Test]
    public void MakeRelativePath_ParentTarget_UsesDotDot()
    {
        var r = TestStandService.MakeRelativePath(@"C:\proj\seq\inner", @"C:\proj\seq\Test.seq");
        Assert.That(r, Is.EqualTo(@"..\Test.seq"));
    }

    [Test]
    public void MakeRelativePath_SameDirectory_JustFileName()
    {
        var r = TestStandService.MakeRelativePath(@"C:\proj\seq", @"C:\proj\seq\Test.seq");
        Assert.That(r, Is.EqualTo("Test.seq"));
    }

    [Test]
    public void MakeRelativePath_AlreadyRelativeTarget_ReturnedUnchanged()
    {
        Assert.That(TestStandService.MakeRelativePath(@"C:\proj\seq", @"sub\Test.seq"),
            Is.EqualTo(@"sub\Test.seq"));
    }

    [Test]
    public void MakeRelativePath_EmptyInputs_ReturnTargetUnchanged()
    {
        Assert.That(TestStandService.MakeRelativePath(@"C:\proj", ""), Is.EqualTo(""));
        Assert.That(TestStandService.MakeRelativePath("", @"C:\proj\Test.seq"),
            Is.EqualTo(@"C:\proj\Test.seq"));
    }

    // ── ResolveAdapterKeyName: friendly-name aliasing (case-insensitive) ─────────

    [Test]
    public void ResolveAdapterKeyName_LabViewAliases_AllMapToSameKey()
    {
        foreach (var alias in new[] { "labview", "lv", "g", "vi" })
            Assert.That(TestStandService.ResolveAdapterKeyName(alias),
                Is.EqualTo("G Std Prototype Adapter"), $"alias '{alias}'");
    }

    [Test]
    public void ResolveAdapterKeyName_IsCaseInsensitive()
    {
        Assert.That(TestStandService.ResolveAdapterKeyName("LabVIEW"), Is.EqualTo("G Std Prototype Adapter"));
        Assert.That(TestStandService.ResolveAdapterKeyName(".NET"), Is.EqualTo("DotNet Adapter"));
    }

    [Test]
    public void ResolveAdapterKeyName_KnownAdapters_Map()
    {
        Assert.That(TestStandService.ResolveAdapterKeyName("dotnet"), Is.EqualTo("DotNet Adapter"));
        Assert.That(TestStandService.ResolveAdapterKeyName("python"), Is.EqualTo("Python Adapter"));
        Assert.That(TestStandService.ResolveAdapterKeyName("sequence"), Is.EqualTo("Sequence Adapter"));
    }

    [Test]
    public void ResolveAdapterKeyName_UnknownOrNull_PassesThroughOrEmpty()
    {
        Assert.That(TestStandService.ResolveAdapterKeyName("MyCustomAdapter"), Is.EqualTo("MyCustomAdapter"));
        Assert.That(TestStandService.ResolveAdapterKeyName(null), Is.EqualTo(""));
    }

    // ── ResolvePaletteName: step-type → palette resolution rules ─────────────────

    [Test]
    public void ResolvePaletteName_VersionSpecific_Wins()
    {
        var none = Array.Empty<string>();
        Assert.That(TestStandService.ResolvePaletteName("whatever", "23.0.0.2", none), Is.EqualTo("NI_FlowControl"));
        Assert.That(TestStandService.ResolvePaletteName("whatever", "23.0.0.3", none), Is.EqualTo("NI_PropertyLoader"));
        Assert.That(TestStandService.ResolvePaletteName("whatever", "23.0.0.49152", none), Is.EqualTo("NI_SubstepTypes"));
    }

    [Test]
    public void ResolvePaletteName_FlowPrefix_GoesToFlowControl()
    {
        Assert.That(TestStandService.ResolvePaletteName("NI_Flow_If", "", Array.Empty<string>()),
            Is.EqualTo("NI_FlowControl"));
    }

    [Test]
    public void ResolvePaletteName_DatabaseAndSyncTypes_MapToTheirPalettes()
    {
        Assert.That(TestStandService.ResolvePaletteName("NI_OpenDatabase", "", Array.Empty<string>()),
            Is.EqualTo("NI_DatabaseTypes"));
        Assert.That(TestStandService.ResolvePaletteName("NI_Queue", "", Array.Empty<string>()),
            Is.EqualTo("NI_SyncTypes"));
    }

    [Test]
    public void ResolvePaletteName_UnknownType_FallsBackToNiTypesWhenAvailable()
    {
        Assert.That(TestStandService.ResolvePaletteName("SomeCustomType", "", new[] { "NI_Types" }),
            Is.EqualTo("NI_Types"));
        Assert.That(TestStandService.ResolvePaletteName("SomeCustomType", "", Array.Empty<string>()),
            Is.EqualTo(""), "no NI_Types palette available → empty");
    }

    // ── SequencePlanValidator: rules not covered by T10 / T23 ────────────────────

    private static PlanStepInput Step(string name, string type, string? expr = null) =>
        new() { Name = name, StepType = type, Expression = expr };

    [Test]
    public void Validator_EmptySequenceName_IsError()
    {
        var r = SequencePlanValidator.Validate("", new[] { Step("A", "Statement") }, Array.Empty<string>());
        Assert.That(r.Errors.Any(e => e.Code == "E_NO_SEQUENCE"), Is.True);
    }

    [Test]
    public void Validator_NoSteps_IsError()
    {
        var r = SequencePlanValidator.Validate("Seq", new List<PlanStepInput>(), Array.Empty<string>());
        Assert.That(r.Valid, Is.False);
        Assert.That(r.Errors.Any(e => e.Code == "E_NO_STEPS"), Is.True);
    }

    [Test]
    public void Validator_EmptyNameOrType_AreErrors()
    {
        var r = SequencePlanValidator.Validate("Seq",
            new[] { Step("", "Statement"), Step("B", "") }, Array.Empty<string>());
        Assert.That(r.Errors.Any(e => e.Code == "E_EMPTY_NAME"), Is.True);
        Assert.That(r.Errors.Any(e => e.Code == "E_EMPTY_TYPE"), Is.True);
    }

    [Test]
    public void Validator_UnknownStepType_IsError()
    {
        var r = SequencePlanValidator.Validate("Seq",
            new[] { Step("A", "TotallyMadeUpType") }, Array.Empty<string>());
        Assert.That(r.Errors.Any(e => e.Code == "E_UNKNOWN_TYPE"), Is.True);
    }

    [Test]
    public void Validator_CaseWithoutSelect_IsError()
    {
        var r = SequencePlanValidator.Validate("Seq", new[]
        {
            Step("If", "NI_Flow_If", "True"),
            Step("C", "NI_Flow_Case"),
            Step("End", "NI_Flow_End")
        }, Array.Empty<string>());
        Assert.That(r.Errors.Any(e => e.Code == "E_CASE_WITHOUT_SELECT"), Is.True);
    }

    [Test]
    public void Validator_BreakInsideSelect_IsAllowed()
    {
        // Break may terminate a Select (not only a loop) — must NOT raise E_JUMP_OUTSIDE_LOOP.
        var r = SequencePlanValidator.Validate("Seq", new[]
        {
            Step("Sel",  "NI_Flow_Select", "Locals.X"),
            Step("Case", "NI_Flow_Case"),
            Step("Brk",  "NI_Flow_Break"),
            Step("End",  "NI_Flow_End")
        }, Array.Empty<string>());
        Assert.That(r.Errors.Any(e => e.Code == "E_JUMP_OUTSIDE_LOOP"), Is.False);
    }

    [Test]
    public void Validator_BreakAndContinueInsideLoop_AreValid()
    {
        var r = SequencePlanValidator.Validate("Seq", new[]
        {
            Step("For",  "NI_Flow_For", "True"),
            Step("Brk",  "NI_Flow_Break"),
            Step("Cont", "NI_Flow_Continue"),
            Step("End",  "NI_Flow_End")
        }, Array.Empty<string>());
        Assert.That(r.Valid, Is.True, string.Join(";", r.Errors.Select(e => e.Code)));
    }

    [Test]
    public void Validator_ForEachLoop_IsRecognisedAsValidLoop()
    {
        var r = SequencePlanValidator.Validate("Seq", new[]
        {
            Step("FE",   "NI_Flow_ForEach", "True"),
            Step("Cont", "NI_Flow_Continue"),
            Step("End",  "NI_Flow_End")
        }, Array.Empty<string>());
        Assert.That(r.Valid, Is.True, string.Join(";", r.Errors.Select(e => e.Code)));
    }

    [Test]
    public void Validator_ConditionBearingFlowWithoutExpression_WarnsNotErrors()
    {
        var r = SequencePlanValidator.Validate("Seq", new[]
        {
            Step("If",  "NI_Flow_If"),       // no condition expression
            Step("End", "NI_Flow_End")
        }, Array.Empty<string>());
        Assert.That(r.Valid, Is.True);
        Assert.That(r.Warnings.Any(w => w.Code == "W_NO_CONDITION"), Is.True);
    }
}
