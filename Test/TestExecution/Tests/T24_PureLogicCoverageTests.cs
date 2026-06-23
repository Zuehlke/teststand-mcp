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

    // ── ParseRuleSeverities: analyzer report rule-catalog → RuleId→severity map ──
    // The report carries the default TestStand namespace, so the parser must match by local-name.
    // Rule-catalog Objs expose Id + Severity; result-message Objs expose RuleId and must be skipped.
    private const string SampleReportXml =
@"<?xml version='1.0' encoding='UTF-8'?>
<teststandfileheader type='TEOLEDataSource' xmlns='http://www.ni.com/TestStand/23.0.0/PropertyObjectFile'>
  <TEOLEDataSource>
    <ReportData classname='Obj'>
      <subprops>
        <Obj classname='Obj'><subprops>
          <Id classname='Str'><value>NI_AlphaRule</value></Id>
          <Name classname='Str'><value>Alpha</value></Name>
          <Severity classname='Num'><value>0</value></Severity>
        </subprops></Obj>
        <Obj classname='Obj'><subprops>
          <Id classname='Str'><value>NI_BetaRule</value></Id>
          <Severity classname='Num'><value>1</value></Severity>
        </subprops></Obj>
        <Obj classname='Obj'><subprops>
          <Id classname='Str'><value>NI_GammaRule</value></Id>
          <Severity classname='Num'><value>2</value></Severity>
        </subprops></Obj>
        <Obj classname='Obj'><subprops>
          <RuleId classname='Str'><value>NI_AlphaRule</value></RuleId>
          <Text classname='Str'><value>a finding, not a rule</value></Text>
        </subprops></Obj>
      </subprops>
    </ReportData>
  </TEOLEDataSource>
</teststandfileheader>";

    [Test]
    public void ParseRuleSeverities_MapsCatalogRules_SkipsMessageObjs()
    {
        var map = TestStandService.ParseRuleSeverities(SampleReportXml, _ => { });
        Assert.That(map.Count, Is.EqualTo(3),
            "only the three Id+Severity rule objs map; the RuleId message obj is skipped");
        Assert.That(map["NI_AlphaRule"], Is.EqualTo(0));
        Assert.That(map["NI_BetaRule"],  Is.EqualTo(1));
        Assert.That(map["NI_GammaRule"], Is.EqualTo(2));
    }

    [Test]
    public void ParseRuleSeverities_RuleIdLookupIsCaseInsensitive()
    {
        var map = TestStandService.ParseRuleSeverities(SampleReportXml, _ => { });
        Assert.That(map.ContainsKey("ni_alpharule"), Is.True);
    }

    [Test]
    public void ParseRuleSeverities_EmptyOrInvalid_ReturnsEmptyMap()
    {
        Assert.That(TestStandService.ParseRuleSeverities("", _ => { }), Is.Empty);
        Assert.That(TestStandService.ParseRuleSeverities("<not-valid-xml", _ => { }), Is.Empty);
    }

    // ── ParseAnalyzerMessages: nested Locations[] → Location/SequenceName/StepName ──
    // A finding's location is the first element of a Locations[] array of Objs, each exposing
    // PropertyPath (step-ID form), PropertyPathWithNames (friendly names) and FilePath.
    private static string MsgProjectXml(string locationSubprops) =>
@"<teststandfileheader><Messages classname='Objs'><value lbound='[0]' ubound='[0]'><value arrayindex='[0]'>
  <Obj name=''><subprops>
    <RuleId classname='Str'><value>NI_TestRule</value></RuleId>
    <Text classname='Str'><value>finding text</value></Text>
    <Locations classname='Objs'><value lbound='[0]' ubound='[0]'><value><Obj name=''><subprops>"
+ locationSubprops +
@"</subprops></Obj></value></value></Locations>
  </subprops></Obj>
</value></value></Messages></teststandfileheader>";

    [Test]
    public void ParseAnalyzerMessages_StepLocation_ExtractsFriendlyPathSeqAndStep()
    {
        string xml = MsgProjectXml(
            @"<PropertyPath classname='Str'><value>Data.Seq[""MainSequence""].Main[""ID#:abc123""].TS.Mode</value></PropertyPath>
              <PropertyPathWithNames classname='Str'><value>Data.Seq[""MainSequence""].Main[""Label_Disabled""].TS.Mode</value></PropertyPathWithNames>
              <FilePath classname='Str'><value>C:\proj\Demo.seq</value></FilePath>");

        var msgs = TestStandService.ParseAnalyzerMessages(xml, _ => { });
        Assert.That(msgs.Count, Is.EqualTo(1), "the nested location Obj must NOT be counted as a message");
        var m = msgs[0];
        Assert.That(m.RuleId, Is.EqualTo("NI_TestRule"));
        Assert.That(m.Location, Is.EqualTo(@"Data.Seq[""MainSequence""].Main[""Label_Disabled""].TS.Mode"),
            "friendly PropertyPathWithNames must win over the ID-based PropertyPath");
        Assert.That(m.SequenceName, Is.EqualTo("MainSequence"));
        Assert.That(m.StepName, Is.EqualTo("Label_Disabled"));
    }

    [Test]
    public void ParseAnalyzerMessages_NonStepLocation_HasSequenceButNoStep()
    {
        // A parameter/variable location has only PropertyPath (no friendly names) and no step token.
        string xml = MsgProjectXml(
            @"<PropertyPath classname='Str'><value>Data.Seq[""MainSequence""].Parameters.Schnittstelle</value></PropertyPath>
              <FilePath classname='Str'><value>C:\proj\Demo.seq</value></FilePath>");

        var m = TestStandService.ParseAnalyzerMessages(xml, _ => { })[0];
        Assert.That(m.Location, Is.EqualTo(@"Data.Seq[""MainSequence""].Parameters.Schnittstelle"));
        Assert.That(m.SequenceName, Is.EqualTo("MainSequence"));
        Assert.That(m.StepName, Is.Empty, "a non-step location must not yield a step name");
    }

    [Test]
    public void ParseAnalyzerMessages_SeverityComesFromRuleMap()
    {
        string xml = MsgProjectXml(
            @"<PropertyPath classname='Str'><value>Data.Seq[""S""].Parameters.X</value></PropertyPath>");
        var map = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            { ["NI_TestRule"] = 1 };
        var m = TestStandService.ParseAnalyzerMessages(xml, _ => { }, map)[0];
        Assert.That(m.Severity, Is.EqualTo("Warning"), "severity must come from the rule map (1=Warning)");
    }

    // ── ParseDifferReport: native FileDiffer report → classified change list ─────
    // The report is a row/col tree (col0 = name + BlockLevel, col1 = file1, col2 = file2 + StyleID),
    // carrying the default DifferReport namespace. The parser tracks ancestors via BlockLevel and
    // emits only leaf changes (ID_Children rows are context that build the path).
    private const string SampleDifferReportXml =
@"<?xml version='1.0' encoding='UTF-8'?>
<DifferReport xmlns='http://www.ni.com/TestStand/23.0.0/DifferReport'>
  <Header StationID='S' Date='d' Time='t' TSVersion='v'>
    <File><Path>C:\a.seq</Path><Name>File 1: a.seq</Name>
      <Changes Count='0'><LocalizedText>Changes</LocalizedText></Changes>
      <Insertions Count='0'><LocalizedText>Ins</LocalizedText></Insertions>
      <Deletions Count='0'><LocalizedText>Del</LocalizedText></Deletions></File>
    <File><Path>C:\b.seq</Path><Name>File 2: b.seq</Name>
      <Changes Count='1'><LocalizedText>Changes</LocalizedText></Changes>
      <Insertions Count='0'><LocalizedText>Ins</LocalizedText></Insertions>
      <Deletions Count='1'><LocalizedText>Del</LocalizedText></Deletions></File>
    <AppliedFilters></AppliedFilters>
  </Header>
  <RowDifference>
    <ColDifference><DifferenceInfo BlockLevel='0'><Text>MainSequence</Text></DifferenceInfo></ColDifference>
    <ColDifference><DifferenceInfo><Text>MainSequence</Text></DifferenceInfo></ColDifference>
    <ColDifference><DifferenceInfo StyleID='ID_Children'><Text>MainSequence</Text></DifferenceInfo></ColDifference>
  </RowDifference>
  <RowDifference>
    <ColDifference><DifferenceInfo BlockLevel='1'><Text>Cleanup</Text></DifferenceInfo></ColDifference>
    <ColDifference><DifferenceInfo><Text>Cleanup</Text></DifferenceInfo></ColDifference>
    <ColDifference><DifferenceInfo StyleID='ID_Children'><Text>Cleanup</Text></DifferenceInfo></ColDifference>
  </RowDifference>
  <RowDifference>
    <ColDifference><DifferenceInfo BlockLevel='2'><Text>OldStep</Text></DifferenceInfo></ColDifference>
    <ColDifference><DifferenceInfo><Text>OldStep</Text></DifferenceInfo></ColDifference>
    <ColDifference><DifferenceInfo StyleID='ID_Delete'><Text>OldStep</Text></DifferenceInfo></ColDifference>
  </RowDifference>
  <RowDifference>
    <ColDifference><DifferenceInfo BlockLevel='1'><Text>Setup</Text></DifferenceInfo></ColDifference>
    <ColDifference><DifferenceInfo><Text>Setup</Text></DifferenceInfo></ColDifference>
    <ColDifference><DifferenceInfo StyleID='ID_Children'><Text>Setup</Text></DifferenceInfo></ColDifference>
  </RowDifference>
  <RowDifference>
    <ColDifference><DifferenceInfo BlockLevel='2'><Text>Value</Text></DifferenceInfo></ColDifference>
    <ColDifference><DifferenceInfo><Text>old</Text></DifferenceInfo></ColDifference>
    <ColDifference><DifferenceInfo StyleID='ID_ValueChange'><Text>new</Text></DifferenceInfo></ColDifference>
  </RowDifference>
</DifferReport>";

    [Test]
    public void ParseDifferReport_ReadsHeaderTallies()
    {
        var r = TestStandService.ParseDifferReport(SampleDifferReportXml, @"C:\a.seq", @"C:\b.seq", _ => { });
        Assert.That(r.File1, Is.EqualTo(@"C:\a.seq"));
        Assert.That(r.FileSummaries.Count, Is.EqualTo(2));
        var f2 = r.FileSummaries[1];
        Assert.That(f2.Changes, Is.EqualTo(1));
        Assert.That(f2.Deletions, Is.EqualTo(1));
        Assert.That(r.TotalDifferences, Is.EqualTo(2), "sum of per-file changes+insertions+deletions");
        Assert.That(r.Identical, Is.False);
    }

    [Test]
    public void ParseDifferReport_EmitsLeafChanges_WithTypePathAndValues()
    {
        var r = TestStandService.ParseDifferReport(SampleDifferReportXml, @"C:\a.seq", @"C:\b.seq", _ => { });
        Assert.That(r.Changes.Count, Is.EqualTo(2), "ID_Children context rows must NOT be emitted as changes");

        var del = r.Changes[0];
        Assert.That(del.ChangeType, Is.EqualTo("Delete"));
        Assert.That(del.Name, Is.EqualTo("OldStep"));
        Assert.That(del.Path, Is.EqualTo("MainSequence > Cleanup"));

        var vc = r.Changes[1];
        Assert.That(vc.ChangeType, Is.EqualTo("ValueChange"));
        Assert.That(vc.Name, Is.EqualTo("Value"));
        Assert.That(vc.Path, Is.EqualTo("MainSequence > Setup"));
        Assert.That(vc.File1Value, Is.EqualTo("old"));
        Assert.That(vc.File2Value, Is.EqualTo("new"));
    }

    [Test]
    public void ParseDifferReport_EmptyOrInvalid_ReturnsIdentical()
    {
        var r = TestStandService.ParseDifferReport("", @"C:\a", @"C:\b", _ => { });
        Assert.That(r.Changes, Is.Empty);
        Assert.That(r.Identical, Is.True);
        Assert.That(TestStandService.ParseDifferReport("<nope", @"C:\a", @"C:\b", _ => { }).Changes, Is.Empty);
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
