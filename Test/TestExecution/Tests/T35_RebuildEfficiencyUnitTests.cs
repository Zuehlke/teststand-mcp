using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using TestStandMCP.Models;
using TestStandMCP.Services;
using TestStandMCP.Tools;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// Pure (engine-free) tests for the rebuild-efficiency layer added after a 30-sequence 1:1 rebuild
/// showed where the tool surface was costing calls and fidelity:
///   • <see cref="DiffReportShaper"/> — classification, filtering, grouping and summary-only mode, so
///     a 600-difference report is readable instead of blowing the tool-result budget;
///   • the export/import transport model round-tripping through its own JSON options.
/// Does NOT inherit TestBase, so no TestStand engine is connected.
/// </summary>
[TestFixture]
[Category("PureLogic")]
public class T35_RebuildEfficiencyUnitTests
{
    private static FileDifferChange Ch(string type, string path, string name = "Value",
        string f1 = "a", string f2 = "b") =>
        new() { ChangeType = type, Path = path, Name = name, File1Value = f1, File2Value = f2 };

    private static FileDifferReport Report(params FileDifferChange[] changes)
    {
        var r = new FileDifferReport { File1 = "orig.seq", File2 = "rebuild.seq" };
        foreach (var c in changes) r.Changes.Add(c);
        r.TotalDifferences = r.Changes.Count;
        return r;
    }

    // ── Categorize ───────────────────────────────────────────────────────────────

    [Test]
    public void Categorize_LabViewViCall_IsRecognised()
    {
        Assert.That(DiffReportShaper.Categorize(
            Ch("ValueChange", "Sequences > Init > Main > Start > ViCall > Parameters")),
            Is.EqualTo(DiffReportShaper.CatLabViewViCall));
    }

    [Test]
    public void Categorize_PythonBeatsGenericModule()
    {
        // The path also contains "Module Properties"; the Python marker has to win, otherwise the
        // python_module filter would not select anything.
        Assert.That(DiffReportShaper.Categorize(Ch("ValueChange",
            "Sequences > Close > Cleanup > X > Step Properties > Module Properties > " +
            "Python Adapter Properties > Class Name")),
            Is.EqualTo(DiffReportShaper.CatPythonModule));
    }

    [Test]
    public void Categorize_ActualArguments_IsSeqCallArgs()
    {
        Assert.That(DiffReportShaper.Categorize(Ch("ValueChange",
            "Sequences > A > Main > Call B > Step Properties > Module Properties > " +
            "Actual Arguments > mdc_com > Flags")),
            Is.EqualTo(DiffReportShaper.CatSeqCallArgs));
    }

    [Test]
    public void Categorize_LocalsAndParameters_AreVariables()
    {
        Assert.That(DiffReportShaper.Categorize(Ch("ValueChange",
            "Sequences > A > Locals > Resp > RespState")),
            Is.EqualTo(DiffReportShaper.CatVariables));
        Assert.That(DiffReportShaper.Categorize(Ch("ValueChange",
            "Sequences > A > Parameters > vid")),
            Is.EqualTo(DiffReportShaper.CatVariables));
    }

    [Test]
    public void Categorize_FileAttributes_IsFileProperties()
    {
        Assert.That(DiffReportShaper.Categorize(
            Ch("Delete", "File Properties > Attributes > NI > Analyzer")),
            Is.EqualTo(DiffReportShaper.CatFileProperties));
    }

    // ── SequenceOf ───────────────────────────────────────────────────────────────

    [Test]
    public void SequenceOf_ExtractsTheSequenceName()
    {
        Assert.That(DiffReportShaper.SequenceOf(
            Ch("ValueChange", "Sequences > _MDC_com > Main > Wait > TS > Preconditions")),
            Is.EqualTo("_MDC_com"));
    }

    [Test]
    public void SequenceOf_FileLevelChange_IsEmpty()
    {
        Assert.That(DiffReportShaper.SequenceOf(Ch("Delete", "File Properties > Attributes")),
            Is.EqualTo(""));
    }

    // ── Shape: tallies always cover the FULL report ───────────────────────────────

    [Test]
    public void Shape_SummaryOnly_OmitsDifferencesButKeepsTallies()
    {
        var rep = Report(
            Ch("ValueChange", "Sequences > A > Main > S > ViCall > Parms"),
            Ch("ValueChange", "Sequences > A > Locals > X"),
            Ch("Delete",      "Sequences > B > Locals > Y"));

        var outp = DiffReportShaper.Shape(rep, new DiffReportShaper.Options { SummaryOnly = true });

        Assert.That(outp["totalDifferences"], Is.EqualTo(3));
        Assert.That(outp.ContainsKey("differences"), Is.False);
        var byCat = (Dictionary<string, int>)outp["byCategory"]!;
        Assert.That(byCat[DiffReportShaper.CatVariables], Is.EqualTo(2));
        Assert.That(byCat[DiffReportShaper.CatLabViewViCall], Is.EqualTo(1));
        var bySeq = (Dictionary<string, int>)outp["bySequence"]!;
        Assert.That(bySeq["A"], Is.EqualTo(2));
        Assert.That(bySeq["B"], Is.EqualTo(1));
    }

    [Test]
    public void Shape_ExcludeCategories_DropsThemButTalliesStayComplete()
    {
        var rep = Report(
            Ch("ValueChange", "Sequences > A > Main > S > ViCall > Parms"),
            Ch("ValueChange", "Sequences > A > Main > S > ViCall > Namespace"),
            Ch("ValueChange", "Sequences > A > Locals > X"));

        var outp = DiffReportShaper.Shape(rep, new DiffReportShaper.Options
        {
            ExcludeCategories = new[] { DiffReportShaper.CatLabViewViCall }
        });

        Assert.That(outp["matchedDifferences"], Is.EqualTo(1));
        Assert.That(outp["filteredOut"], Is.EqualTo(2));
        // The tallies must still describe the whole report — otherwise a filtered answer would read
        // as "almost identical".
        Assert.That(outp["totalDifferences"], Is.EqualTo(3));
        Assert.That(((Dictionary<string, int>)outp["byCategory"]!)[DiffReportShaper.CatLabViewViCall],
            Is.EqualTo(2));
        Assert.That(outp["note"], Is.Not.Null);
    }

    [Test]
    public void Shape_IncludeCategories_KeepsOnlyThose()
    {
        var rep = Report(
            Ch("ValueChange", "Sequences > A > Main > S > ViCall > Parms"),
            Ch("ValueChange", "Sequences > A > Locals > X"));

        var outp = DiffReportShaper.Shape(rep, new DiffReportShaper.Options
        {
            IncludeCategories = new[] { DiffReportShaper.CatVariables }
        });
        Assert.That(outp["matchedDifferences"], Is.EqualTo(1));
    }

    [Test]
    public void Shape_PathFilter_IsCaseInsensitiveSubstring()
    {
        var rep = Report(
            Ch("ValueChange", "Sequences > SetVacuum > Locals > X"),
            Ch("ValueChange", "Sequences > GetVacuum > Locals > X"));

        var outp = DiffReportShaper.Shape(rep,
            new DiffReportShaper.Options { PathFilter = "setvacuum" });
        Assert.That(outp["matchedDifferences"], Is.EqualTo(1));
    }

    [Test]
    public void Shape_ChangeTypes_Filters()
    {
        var rep = Report(
            Ch("ValueChange", "Sequences > A > Locals > X"),
            Ch("Delete",      "Sequences > A > Locals > Y"),
            Ch("Insert",      "Sequences > A > Locals > Z"));

        var outp = DiffReportShaper.Shape(rep,
            new DiffReportShaper.Options { ChangeTypes = new[] { "Delete", "Insert" } });
        Assert.That(outp["matchedDifferences"], Is.EqualTo(2));
    }

    [Test]
    public void Shape_MaxResults_TruncatesAndSaysSo()
    {
        var rep = Report(Enumerable.Range(0, 10)
            .Select(i => Ch("ValueChange", $"Sequences > A > Locals > X{i}")).ToArray());

        var outp = DiffReportShaper.Shape(rep, new DiffReportShaper.Options { MaxResults = 3 });

        Assert.That(outp["matchedDifferences"], Is.EqualTo(10));
        Assert.That(outp["returnedDifferences"], Is.EqualTo(3));
        Assert.That(outp["truncated"], Is.EqualTo(true));
        Assert.That((string)outp["note"]!, Does.Contain("max_results"));
    }

    [Test]
    public void Shape_GroupByCategory_GroupsInsteadOfFlatList()
    {
        var rep = Report(
            Ch("ValueChange", "Sequences > A > Main > S > ViCall > Parms"),
            Ch("ValueChange", "Sequences > A > Locals > X"),
            Ch("ValueChange", "Sequences > A > Locals > Y"));

        var outp = DiffReportShaper.Shape(rep,
            new DiffReportShaper.Options { GroupBy = "category" });

        Assert.That(outp.ContainsKey("differences"), Is.False);
        Assert.That(outp["groups"], Is.Not.Null);
    }

    [Test]
    public void Shape_GroupBySequence_UsesFileLevelBucketForFileChanges()
    {
        var rep = Report(
            Ch("Delete", "File Properties > Attributes > NI"),
            Ch("ValueChange", "Sequences > A > Locals > X"));

        var outp = DiffReportShaper.Shape(rep,
            new DiffReportShaper.Options { GroupBy = "sequence" });
        Assert.That(outp["groups"], Is.Not.Null);
    }

    [Test]
    public void Shape_EmptyReport_IsIdenticalAndHasNoGroups()
    {
        var outp = DiffReportShaper.Shape(Report(), new DiffReportShaper.Options());
        Assert.That(outp["identical"], Is.EqualTo(true));
        Assert.That(outp["matchedDifferences"], Is.EqualTo(0));
    }

    // ── Export/import transport model ────────────────────────────────────────────

    [Test]
    public void SequenceFileModel_RoundTripsThroughItsOwnJsonOptions()
    {
        var model = new SequenceFileModel
        {
            SourcePath         = @"C:\x\orig.seq",
            TypeDefsSourcePath = @"C:\x\orig.seq",
            File               = new FileMetaModel { Comment = "c", Version = "0.0.0.0" },
        };
        model.TypeDefs.Add(new TypeDefModel { Name = "CmdEnum", Attached = false });
        model.TypeDefs.Add(new TypeDefModel { Name = "ButtonEnum", Attached = true });
        model.FileGlobals.Add(new VarModel
        {
            Name = "com_port", DataType = "reference", ValueType = "Empty"
        });

        var seq = new SequenceModel { Name = "A", Description = "d", DisableResults = true };
        seq.Parameters.Add(new VarModel
        {
            Name = "vid", DataType = "number", ValueType = "Number", Value = "1155",
            Representation = "UInt64", NumberFormat = "%#.4x",
            Direction = "Input", PassByReference = false, Flags = 0,
        });
        seq.Locals.Add(new VarModel
        {
            Name = "Cmd", DataType = "container", ValueType = "Container",
            Members = new List<VarModel>
            {
                new() { Name = "CmdEnum", DataType = "CmdEnum", ValueType = "Enum",
                        Ordinal = 4609, Value = "GetVacuumLevel", IsDefault = false },
            },
        });
        seq.Steps.Add(new StepModel
        {
            Group = "Main", Name = "Call B", StepType = "SequenceCall",
            Precondition = "FileGlobals.x != Nothing",
            PostExpression = "Locals.i += 1",
            IgnoreRuntimeErrors = true,
            Module = new StepModuleModel
            {
                Kind = "SequenceCall", TargetSequenceName = "B", UseCurrentFile = true,
                StoredFilePath = "stale.seq",
                Arguments = new List<ModuleArgModel>
                {
                    new() { Name = "a", Value = "FileGlobals.a", Flags = 0, UseDefault = false },
                },
            },
        });
        model.Sequences.Add(seq);

        var json = System.Text.Json.JsonSerializer.Serialize(model, SequenceFileModel.Json);
        var back = System.Text.Json.JsonSerializer.Deserialize<SequenceFileModel>(
            json, SequenceFileModel.Json)!;

        Assert.That(back.SchemaVersion, Is.EqualTo(1));
        Assert.That(back.File.Comment, Is.EqualTo("c"));
        Assert.That(back.TypeDefs.Single(t => t.Name == "ButtonEnum").Attached, Is.True);
        Assert.That(back.TypeDefs.Single(t => t.Name == "CmdEnum").Attached, Is.False);

        var bs = back.Sequences.Single();
        Assert.That(bs.DisableResults, Is.True);
        var vid = bs.Parameters.Single();
        Assert.That(vid.Representation, Is.EqualTo("UInt64"));
        Assert.That(vid.NumberFormat, Is.EqualTo("%#.4x"));
        var cmdEnum = bs.Locals.Single().Members!.Single();
        Assert.That(cmdEnum.Ordinal, Is.EqualTo(4609));
        Assert.That(cmdEnum.Value, Is.EqualTo("GetVacuumLevel"));
        var step = bs.Steps.Single();
        Assert.That(step.Precondition, Is.EqualTo("FileGlobals.x != Nothing"));
        Assert.That(step.IgnoreRuntimeErrors, Is.True);
        Assert.That(step.Module!.StoredFilePath, Is.EqualTo("stale.seq"));
        Assert.That(step.Module!.Arguments!.Single().Value, Is.EqualTo("FileGlobals.a"));
    }

    // ── Cross-file SequenceCall detection (drives the isolated prototype-load pass) ──

    [Test]
    public void CrossFileSequenceCall_SurvivesTheJsonRoundTrip()
    {
        // The import decides which steps need the out-of-process prototype load from exactly these
        // three fields. If UseCurrentFile came back null instead of false, the pass would silently
        // find nothing to do — which is how a missing prototype cache hid once already.
        var model = new SequenceFileModel();
        var seq = new SequenceModel { Name = "_MDC_com" };
        seq.Steps.Add(new StepModel
        {
            Group = "Main", Name = "Call Log", StepType = "SequenceCall",
            Module = new StepModuleModel
            {
                Kind = "SequenceCall", TargetSequenceName = "Log_anythingAsJSon",
                TargetSequenceFile = "Easy.Log.seq", StoredFilePath = "Easy.Log.seq",
                UseCurrentFile = false,
            },
        });
        // A same-file call must NOT be picked up: its cache is empty in the original too.
        seq.Steps.Add(new StepModel
        {
            Group = "Main", Name = "Call Local", StepType = "SequenceCall",
            Module = new StepModuleModel
            {
                Kind = "SequenceCall", TargetSequenceName = "Other", UseCurrentFile = true,
                StoredFilePath = "Self.seq",
            },
        });
        model.Sequences.Add(seq);

        var json = System.Text.Json.JsonSerializer.Serialize(model, SequenceFileModel.Json);
        Assert.That(json, Does.Contain("\"useCurrentFile\": false"),
            "false must be written, not omitted as a default");

        var back = System.Text.Json.JsonSerializer.Deserialize<SequenceFileModel>(
            json, SequenceFileModel.Json)!;

        var crossFile = back.Sequences
            .SelectMany(s => s.Steps)
            .Where(st => st.Module?.Kind == "SequenceCall"
                         && st.Module.UseCurrentFile == false
                         && !string.IsNullOrWhiteSpace(st.Module.TargetSequenceFile))
            .ToList();

        Assert.That(crossFile, Has.Count.EqualTo(1));
        Assert.That(crossFile[0].Name, Is.EqualTo("Call Log"));
        Assert.That(crossFile[0].Module!.TargetSequenceFile, Is.EqualTo("Easy.Log.seq"));
    }

    // ── Analyzer: a zero result must be flagged, not reported as clean ───────────

    [Test]
    public void BuildAnalyzerResult_NoMessagesAtAll_IsFlaggedSuspect()
    {
        // AnalyzerApp can bail out early (LabVIEW/Python unavailable for the "module is loadable"
        // rule), save an empty project and still exit successfully. That must not read as "clean".
        var r = TestStandService.BuildAnalyzerResultForTest(
            "x.seq", new List<AnalyzerMessage>(), "Information", "rule");

        Assert.That(r.ResultSuspect, Is.True);
        Assert.That(r.TotalMessages, Is.EqualTo(0));
        Assert.That(r.Note, Does.Contain("SUSPECT"));
    }

    [Test]
    public void BuildAnalyzerResult_MessagesPresent_IsNotSuspect()
    {
        var msgs = new List<AnalyzerMessage>
        {
            new() { Severity = "Information", RuleId = "NI_StepCount",  Text = "3 steps" },
            new() { Severity = "Warning",     RuleId = "NI_UnusedSequence", Text = "unused" },
        };
        var r = TestStandService.BuildAnalyzerResultForTest("x.seq", msgs, "Information", "rule");

        Assert.That(r.ResultSuspect, Is.False);
        Assert.That(r.Note, Is.Null);
        Assert.That(r.TotalMessages, Is.EqualTo(2));
        Assert.That(r.WarningCount, Is.EqualTo(1));
        Assert.That(r.InformationCount, Is.EqualTo(1));
    }

    [Test]
    public void BuildAnalyzerResult_FilteredToZeroBySeverity_IsNotSuspect()
    {
        // min_severity='Error' legitimately filters a warning-only file to zero. That is NOT the
        // "analysis did not run" case, so the suspect flag must key off the RAW message count.
        var msgs = new List<AnalyzerMessage>
        {
            new() { Severity = "Warning", RuleId = "NI_UnusedSequence", Text = "unused" },
        };
        var r = TestStandService.BuildAnalyzerResultForTest("x.seq", msgs, "Error", "none");

        Assert.That(r.TotalMessages, Is.EqualTo(0));
        Assert.That(r.ResultSuspect, Is.False, "raw messages existed — only the filter emptied it");
        Assert.That(r.Note, Is.Null);
    }

    [Test]
    public void SequenceFileModel_OmitsNulls_SoAnExportStaysCompact()
    {
        var model = new SequenceFileModel();
        model.Sequences.Add(new SequenceModel { Name = "A" });
        var json = System.Text.Json.JsonSerializer.Serialize(model, SequenceFileModel.Json);
        Assert.That(json, Does.Not.Contain("\"description\""));
        Assert.That(json, Does.Not.Contain("null"));
    }

    // ── import_sequence_file must never DEFAULT to a prototype load ──────────────
    //
    // Measured 2026-07-29: an in-process LabVIEW prototype load of a packed-library VI raises the
    // native delay-load fault 0xC06D007E, which escapes managed try/catch and kills the server process
    // (NI Error Reporter, MCP session gone). The import used to do exactly that in its LabVIEW pass, so
    // the *default* is the safety-relevant property here — hence a test on the declared schema, which is
    // what a client sees and what a future edit would silently change back.
    //
    // Both the registry and the service constructor are engine-free (the service only stores its
    // logger, the registry only builds schemas), so this stays a PureLogic test.

    private static System.Text.Json.JsonElement ImportSchema()
    {
        using var editor = new SequenceEditorService(NullLogger<SequenceEditorService>.Instance);
        var registry = new TestStandToolRegistry(
            new TestStandService(NullLogger<TestStandService>.Instance), editor,
            NullLogger<TestStandToolRegistry>.Instance);
        var tool = registry.GetTools().FirstOrDefault(t => t.Name == "import_sequence_file");
        Assert.That(tool, Is.Not.Null, "import_sequence_file is not registered");
        return tool!.InputSchema.GetProperty("properties");
    }

    [TestCase("labview_panes")]
    [TestCase("cross_file_prototypes")]
    public void ImportSchema_ModuleReproductionDefaultsToCopy_NotToTheProcessFatalLoad(string param)
    {
        var prop = ImportSchema().GetProperty(param);

        Assert.That(prop.GetProperty("default").GetString(), Is.EqualTo("copy"),
            $"{param} must default to cloning from the source file — 'load' can kill the server process");

        var allowed = prop.GetProperty("enum").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.That(allowed, Is.EquivalentTo(new[] { "copy", "load", "skip" }));
    }

    [Test]
    public void ImportSchema_Variables_DefaultToBeingClonedFromTheSourceFile()
    {
        // Rebuilding a variable declaratively cannot reproduce a type instance's member that has NO
        // value of its own: instantiating the named type materialises the member with its default
        // written out, which TestStand counts as explicitly set (FileDiffer [Debug] vs {Debug}). Only a
        // flag-preserving clone carries that state, so 'copy' has to stay the default.
        var prop = ImportSchema().GetProperty("variables");

        Assert.That(prop.GetProperty("default").GetString(), Is.EqualTo("copy"));

        var allowed = prop.GetProperty("enum").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.That(allowed, Is.EquivalentTo(new[] { "copy", "model" }));
    }

    [Test]
    public void ImportSchema_KeepUnusedTypes_DefaultsToKeeping()
    {
        // A type survives a save only if it is attached or still referenced, so importing a SUBSET of
        // the sequences drops the types only the omitted ones used — silently, after copy_typedefs has
        // already reported them copied. Keeping them is the safe default.
        Assert.That(ImportSchema().GetProperty("keep_unused_types").GetProperty("default").GetBoolean(),
            Is.True);
    }

    [Test]
    public void ImportSchema_StillAcceptsTheDeprecatedBoolean_ForOlderCallers()
    {
        var prop = ImportSchema().GetProperty("load_labview_prototypes");
        Assert.That(prop.GetProperty("type").GetString(), Is.EqualTo("boolean"));
        Assert.That(prop.GetProperty("description").GetString(), Does.Contain("DEPRECATED"));
    }

    [Test]
    public void ImportSchema_Modules_DefaultToBeingClonedFromTheSourceFile()
    {
        var prop = ImportSchema().GetProperty("modules");

        Assert.That(prop.GetProperty("default").GetString(), Is.EqualTo("copy"));
        Assert.That(prop.GetProperty("enum").EnumerateArray().Select(e => e.GetString()).ToList(),
            Is.EquivalentTo(new[] { "copy", "model" }));
    }

    // ── Type-inspection tool gaps (found comparing Python enums against a .seq) ──

    private static System.Text.Json.JsonElement SchemaOf(string toolName)
    {
        using var editor = new SequenceEditorService(NullLogger<SequenceEditorService>.Instance);
        var registry = new TestStandToolRegistry(
            new TestStandService(NullLogger<TestStandService>.Instance), editor,
            NullLogger<TestStandToolRegistry>.Instance);
        var tool = registry.GetTools().FirstOrDefault(t => t.Name == toolName);
        Assert.That(tool, Is.Not.Null, $"{toolName} is not registered");
        return tool!.InputSchema.GetProperty("properties");
    }

    [Test]
    public void ListFileTypedefs_CanReturnEnumeratorsInOneCall_ButNotByDefault()
    {
        // Comparing a protocol file's 17 enums against their Python definitions cost 17 separate
        // get_enum_values calls. Values stay opt-in so a plain listing does not carry ~230 constants.
        var prop = SchemaOf("list_file_typedefs").GetProperty("include_values");

        Assert.That(prop.GetProperty("type").GetString(), Is.EqualTo("boolean"));
        Assert.That(prop.GetProperty("default").GetBoolean(), Is.False);
    }

    [Test]
    public void GetDataTypeFields_DocumentsThatItResolvesTypeUsageListTypes()
    {
        // The behaviour itself needs an engine; what a pure test can pin is that the tool no longer
        // advertises a file-root-only lookup — that description is what sends a caller down the wrong
        // path after get_data_type_fields threw on a type list_file_typedefs had just reported.
        using var editor = new SequenceEditorService(NullLogger<SequenceEditorService>.Instance);
        var registry = new TestStandToolRegistry(
            new TestStandService(NullLogger<TestStandService>.Instance), editor,
            NullLogger<TestStandToolRegistry>.Instance);
        string desc = registry.GetTools().First(t => t.Name == "get_data_type_fields").Description;

        Assert.That(desc, Does.Contain("TypeUsageList"));
        Assert.That(desc, Does.Contain("get_enum_values"),
            "an enum's constants are not subproperties — the description must point elsewhere");
    }

    // ── StepCopyPolicy — the TYPE-CONFLICT regression ────────────────────────────
    //
    // Cloning every module path replaced step properties that belong to the step TYPE, which registers a
    // conflicting instance of that type: the rebuilt file opened with a "Type Conflict in File" dialog
    // while the native FileDiffer reported it as IDENTICAL. No automated check caught it, so these tests
    // are the guard — each one pins a case where the policy must NOT reach for the object copy.

    [Test]
    public void Decide_ScalarAlreadyEqual_TouchesNothing()
    {
        // The common case on a plain flow step. Writing here is what produced the dialog.
        Assert.That(StepCopyPolicy.Decide("Number", 0, "3", "3", 0, 0),
            Is.EqualTo(StepPropertyAction.SkipIdentical));
    }

    [Test]
    public void Decide_ScalarDiffers_IsWrittenByValue_NotCloned()
    {
        Assert.That(StepCopyPolicy.Decide("Number", 0, "5", "3", 0, 0),
            Is.EqualTo(StepPropertyAction.WriteScalarValue));
    }

    [Test]
    public void Decide_EmptyArgumentListOnBothSides_IsSkipped()
    {
        // An "Argument List" reports 0 subproperties, so without the list-like check it would land in
        // the scalar branch, read null on both sides and be skipped for the WRONG reason — and a
        // NON-empty one would then never be carried at all.
        Assert.That(StepCopyPolicy.Decide("Argument List", 0, null, null, 0, 0),
            Is.EqualTo(StepPropertyAction.SkipIdentical));
    }

    [TestCase(2, 0)]
    [TestCase(0, 2)]
    public void Decide_ArgumentListWithElementsOnEitherSide_IsCloned(int srcElems, int tgtElems)
    {
        Assert.That(StepCopyPolicy.Decide("Argument List", 0, null, null, srcElems, tgtElems),
            Is.EqualTo(StepPropertyAction.CloneSubtree));
    }

    [Test]
    public void Decide_ContainerWithMembers_IsCloned()
    {
        // A real module subtree (TS.SData with 29 members) — the payload IS the subtree.
        Assert.That(StepCopyPolicy.Decide("SeqCallStepAdditions", 29, null, null, 0, 0),
            Is.EqualTo(StepPropertyAction.CloneSubtree));
    }

    [Test]
    public void Decide_UnreadableSourceScalar_FallsBackToTheObjectCopy()
    {
        // An exotic leaf the scalar reader cannot render: copying the object is still better than
        // leaving the target's differing value in place.
        Assert.That(StepCopyPolicy.Decide("Path", 0, null, "C:\\old.vi", 0, 0),
            Is.EqualTo(StepPropertyAction.CloneSubtree));
    }

    [TestCase("Number", false)]
    [TestCase("Boolean", false)]
    [TestCase("Number [Array]", true)]
    [TestCase("Argument List", true)]
    [TestCase(null, false)]
    public void IsListLike_RecognisesArraysAndArgumentLists(string? typeDisplay, bool expected)
    {
        Assert.That(StepCopyPolicy.IsListLike(typeDisplay), Is.EqualTo(expected));
    }

    // ── Mode normalisation ───────────────────────────────────────────────────────

    [TestCase("copy", "copy")]
    [TestCase("load", "load")]
    [TestCase("skip", "skip")]
    [TestCase("  COPY  ", "copy")]
    [TestCase("", "copy")]
    [TestCase(null, "copy")]
    public void NormalizeModuleMode_AcceptsTheThreeModes_AndDefaultsToCopy(string? input, string expected)
    {
        Assert.That(TestStandService.NormalizeModuleMode(input, "labview_panes"), Is.EqualTo(expected));
    }

    [Test]
    public void NormalizeModuleMode_RejectsAnUnknownMode_NamingTheParameter()
    {
        // A typo must not silently degrade to the default and quietly skip a fidelity pass.
        var ex = Assert.Throws<System.ArgumentException>(
            () => TestStandService.NormalizeModuleMode("clone", "labview_panes"));
        Assert.That(ex!.Message, Does.Contain("labview_panes"));
    }

    [TestCase("copy", "copy")]
    [TestCase("model", "model")]
    [TestCase("MODEL", "model")]
    [TestCase(null, "copy")]
    public void NormalizeVariableMode_AcceptsCopyAndModel(string? input, string expected)
    {
        Assert.That(TestStandService.NormalizeVariableMode(input), Is.EqualTo(expected));
    }

    [Test]
    public void NormalizeVariableMode_RejectsAModuleModeThatDoesNotApplyHere()
    {
        // 'skip' is valid for panes but meaningless for variables — accepting it would drop them.
        Assert.Throws<System.ArgumentException>(() => TestStandService.NormalizeVariableMode("skip"));
    }

    // ── Source-file resolution ───────────────────────────────────────────────────

    [Test]
    public void FirstExistingPath_PrefersTheFirstCandidateThatIsOnDisk()
    {
        string real = System.IO.Path.GetTempFileName();
        try
        {
            Assert.That(TestStandService.FirstExistingPath(null, "  ", @"X:\gone.seq", real),
                Is.EqualTo(real));
        }
        finally { System.IO.File.Delete(real); }
    }

    [Test]
    public void FirstExistingPath_NoCandidateExists_IsNull()
    {
        // This is what makes the import fall back from 'copy' to 'model' with a warning instead of
        // throwing when the model outlived the file it was exported from.
        Assert.That(TestStandService.FirstExistingPath(null, "", @"X:\gone.seq"), Is.Null);
    }

    // ── The module property bag ──────────────────────────────────────────────────

    [Test]
    public void ModuleProperties_SurviveTheJsonRoundTrip()
    {
        // The typed fields cover a fraction of a real module (4 of a SequenceCall's 29 SData
        // properties), so the export walks every scalar leaf. An ActiveX step has no typed branch at
        // all and is carried as kind "Other" plus this bag.
        var model = new SequenceFileModel { SourcePath = @"C:\src.seq" };
        model.Sequences.Add(new SequenceModel
        {
            Name  = "Init",
            Steps =
            {
                new StepModel
                {
                    Name = "ActiveX call", StepType = "Action", Adapter = "Automation Adapter",
                    Module = new StepModuleModel
                    {
                        Kind       = "Other",
                        Properties = new List<ModulePropModel>
                        {
                            new() { Path = "ThreadOpt",         Value = "0",    Type = "number"  },
                            new() { Path = "Call.Member Name",  Value = "Open", Type = "string"  },
                            new() { Path = "Call.UseDefault",   Value = "true", Type = "boolean" },
                        },
                    },
                },
            },
        });

        var json  = System.Text.Json.JsonSerializer.Serialize(model, SequenceFileModel.Json);
        var back  = System.Text.Json.JsonSerializer.Deserialize<SequenceFileModel>(
                        json, SequenceFileModel.Json)!;
        var props = back.Sequences[0].Steps[0].Module!.Properties!;

        Assert.That(back.Sequences[0].Steps[0].Module!.Kind, Is.EqualTo("Other"));
        Assert.That(props.Select(p => p.Path),
            Is.EqualTo(new[] { "ThreadOpt", "Call.Member Name", "Call.UseDefault" }),
            "order matters — a nested member needs its parent written first");
        Assert.That(props[2].Type, Is.EqualTo("boolean"));
        Assert.That(props[1].Value, Is.EqualTo("Open"));
    }

    // ── Packed-library guard — the rule that used to live only in CLAUDE.md ───────
    //
    // An IN-PROCESS prototype load of a VI inside a .lvlibp raises the uncatchable delay-load SEH
    // 0xC06D007E and kills the server process. That was documented in three places and enforced
    // nowhere, so forgetting it cost the whole server. These tests pin the detection and the wording.

    [TestCase(@"C:\lib\MyLib.lvlibp\Public\Init.vi", true)]
    [TestCase(@"C:\lib\MYLIB.LVLIBP\Public\Init.vi", true)]   // case-insensitive, like the filesystem
    [TestCase(@"MyLib.lvlibp\Init.vi",               true)]   // relative, as a .seq stores it
    [TestCase(@"C:\vis\Init.vi",                     false)]
    [TestCase(@"C:\lib\MyLib.lvlib\Init.vi",         false)]  // a plain library is NOT packed
    [TestCase("",                                    false)]
    [TestCase(null,                                  false)]
    public void IsPackedLibraryModulePath_RecognisesAPackedLibraryVi(string? path, bool expected)
    {
        Assert.That(InputGuards.IsPackedLibraryModulePath(path), Is.EqualTo(expected));
    }

    [Test]
    public void PackedLibraryRefusal_NamesTheWorkingRouteAndTheFault()
    {
        string msg = InputGuards.PackedLibraryInProcessLoadRefusal("Init", @"MyLib.lvlibp\Init.vi");

        // A refusal that does not say what to do instead just moves the dead end.
        Assert.That(msg, Does.Contain("copy_step_module"));
        Assert.That(msg, Does.Contain("labview_panes='copy'"));
        Assert.That(msg, Does.Contain("0xC06D007E"));
        Assert.That(msg, Does.Contain("Init"));
    }

    [Test]
    public void PackedLibraryAutoLoadSkippedNote_SaysTheStepIsStillConfigured()
    {
        // The VI path IS written — a note that reads like a failure would send the caller re-running a
        // call that already did its job.
        string note = InputGuards.PackedLibraryAutoLoadSkippedNote(@"MyLib.lvlibp\Init.vi");
        Assert.That(note, Does.Contain("SKIPPED"));
        Assert.That(note, Does.Contain("step is configured"));
        Assert.That(note, Does.Contain("copy_step_module"));
    }

    [Test]
    public void PackedLibraryAutoLoadSkippedNote_DoesNotClaimTheParametersAreEmpty()
    {
        // Measured live: reconfiguring an EXISTING packed-library step returns its 18 cached connector-pane
        // parameters, because the interface is read back whether or not a load ran. An earlier wording
        // said the pane "is empty", which would send the caller cloning a pane they already have.
        string note = InputGuards.PackedLibraryAutoLoadSkippedNote(@"MyLib.lvlibp\Init.vi");
        Assert.That(note, Does.Not.Contain("is empty"));
        Assert.That(note, Does.Contain("already cached"));
    }

    // ── The diff row cap is a DEFAULT, not a reminder ────────────────────────────

    [Test]
    public void ShapeOptions_CapRowsByDefault()
    {
        // compare_sequence_files (native mode) shapes with a bare Options instance. Before the default
        // moved into Options it returned every row of a 600-difference report.
        Assert.That(new DiffReportShaper.Options().MaxResults,
            Is.EqualTo(DiffReportShaper.DefaultMaxResults));
        Assert.That(DiffReportShaper.DefaultMaxResults, Is.GreaterThan(0));
    }

    [Test]
    public void Shape_DefaultOptions_TruncateALargeReport_AndKeepTheFullTallies()
    {
        int n   = DiffReportShaper.DefaultMaxResults + 25;
        var rep = Report(Enumerable.Range(0, n)
            .Select(i => Ch("ValueChange", $"Sequences > A > Locals > X{i}")).ToArray());

        var outp = DiffReportShaper.Shape(rep, new DiffReportShaper.Options());

        Assert.That(outp["returnedDifferences"], Is.EqualTo(DiffReportShaper.DefaultMaxResults));
        Assert.That(outp["truncated"], Is.EqualTo(true));
        // The tallies must still count EVERYTHING — they are what a truncated answer is judged on.
        Assert.That(outp["totalDifferences"], Is.EqualTo(n));
        var byCategory = (Dictionary<string, int>)outp["byCategory"]!;
        Assert.That(byCategory.Values.Sum(), Is.EqualTo(n));
        Assert.That((string)outp["note"]!, Does.Contain("unlimited"));
    }

    [Test]
    public void Shape_ExplicitZeroMaxResults_StillMeansUnlimited()
    {
        int n   = DiffReportShaper.DefaultMaxResults + 5;
        var rep = Report(Enumerable.Range(0, n)
            .Select(i => Ch("ValueChange", $"Sequences > A > Locals > X{i}")).ToArray());

        var outp = DiffReportShaper.Shape(rep, new DiffReportShaper.Options { MaxResults = 0 });

        Assert.That(outp["returnedDifferences"], Is.EqualTo(n));
        Assert.That(outp.ContainsKey("truncated"), Is.False);
    }

    // ── TypeConsistencyAuditor — the defect the FileDiffer cannot see ─────────────

    private static TypeRegistrationInfo Ty(string name, int index, string? version = "1.0.0.0",
        bool modified = false, bool attached = true, string? members = "A|B") =>
        new()
        {
            Name = name, Index = index, TypeVersion = version, IsModified = modified,
            Attached = attached, MemberSignature = members, Category = "TypeCategory_CustomDataTypes"
        };

    [Test]
    public void Audit_CleanFile_IsValidAndSaysWhatItProved()
    {
        var r = TypeConsistencyAuditor.Audit(new TypeConsistencyData
        { File = { Ty("LogEvent", 0), Ty("Error", 1) } });

        Assert.That(r.Valid, Is.True);
        Assert.That(r.ErrorCount, Is.Zero);
        Assert.That(r.Stats.TypeRegistrations, Is.EqualTo(2));
        Assert.That(r.Stats.DistinctTypeNames, Is.EqualTo(2));
        // The note must not overclaim: a clean registry is not a clean file.
        Assert.That(r.Note, Does.Contain("FileDiffer"));
    }

    [Test]
    public void Audit_SameTypeNameRegisteredTwice_IsAnError()
    {
        // THE finding. This is what the editor reports as a type conflict and what a whole-subtree
        // clone onto a typed node produces — while diff_sequence_files says identical:true.
        var r = TypeConsistencyAuditor.Audit(new TypeConsistencyData
        { File = { Ty("NI_Flow_If", 3), Ty("NI_Flow_If", 17, version: "2.0.0.0") } });

        Assert.That(r.Valid, Is.False);
        Assert.That(r.ErrorCount, Is.EqualTo(1));
        Assert.That(r.Stats.DuplicateNames, Is.EqualTo(1));
        var issue = r.Issues[0];
        Assert.That(issue.Code, Is.EqualTo("E_DUPLICATE_TYPE_NAME"));
        Assert.That(issue.TypeName, Is.EqualTo("NI_Flow_If"));
        Assert.That(issue.Detail, Does.Contain("3").And.Contain("17"),
            "the indices are what makes the duplicate findable");
        Assert.That(r.Note, Does.Contain("type-conflict dialog"));
    }

    [Test]
    public void Audit_DuplicateNamesAreCaseInsensitive_LikeTestStand()
    {
        var r = TypeConsistencyAuditor.Audit(new TypeConsistencyData
        { File = { Ty("LogEvent", 0), Ty("logevent", 1) } });

        Assert.That(r.ErrorCount, Is.EqualTo(1));
    }

    [Test]
    public void Audit_ModifiedType_IsAWarningNotAnError()
    {
        // A file may legitimately own a modified type — but it is the state TestStand compares against
        // the loaded definition on open, so it must be named without failing the audit.
        var r = TypeConsistencyAuditor.Audit(new TypeConsistencyData
        { File = { Ty("LogEvent", 0, modified: true) } });

        Assert.That(r.Valid, Is.True, "a warning must not clear 'valid' to false");
        Assert.That(r.IssueCount, Is.EqualTo(1));
        Assert.That(r.Issues[0].Code, Is.EqualTo("W_MODIFIED_TYPE"));
        Assert.That(r.Stats.ModifiedTypes, Is.EqualTo(1));
    }

    [Test]
    public void Audit_ErrorsAreListedBeforeWarnings()
    {
        var r = TypeConsistencyAuditor.Audit(new TypeConsistencyData
        {
            File =
            {
                Ty("Modified", 0, modified: true),
                Ty("Dup", 1), Ty("Dup", 2),
            }
        });

        Assert.That(r.Issues[0].Severity, Is.EqualTo("error"));
        Assert.That(r.Issues.Last().Severity, Is.EqualTo("warning"));
    }

    [Test]
    public void Audit_AgainstReference_DifferentVersionUnderTheSameName_IsAnError()
    {
        var r = TypeConsistencyAuditor.Audit(new TypeConsistencyData
        {
            File          = { Ty("LogEvent", 0, version: "2.0.0.0") },
            Reference     = new List<TypeRegistrationInfo> { Ty("LogEvent", 0, version: "1.0.0.0") },
            ReferencePath = @"C:\orig.seq",
        });

        Assert.That(r.ErrorCount, Is.EqualTo(1));
        Assert.That(r.Issues[0].Code, Is.EqualTo("E_TYPE_VERSION_MISMATCH"));
        Assert.That(r.Issues[0].Detail, Does.Contain("copy_typedefs"));
        Assert.That(r.Stats.ComparedAgainst, Is.EqualTo(@"C:\orig.seq"));
    }

    [Test]
    public void Audit_AgainstReference_MissingVersionOnEitherSide_IsNotAMismatch()
    {
        // An unreadable version must not manufacture an error — the whole point of the audit is that a
        // reported conflict is trustworthy.
        var r = TypeConsistencyAuditor.Audit(new TypeConsistencyData
        {
            File      = { Ty("LogEvent", 0, version: null) },
            Reference = new List<TypeRegistrationInfo> { Ty("LogEvent", 0, version: "1.0.0.0") },
        });

        Assert.That(r.ErrorCount, Is.Zero);
    }

    [Test]
    public void Audit_AgainstReference_SameVersionDifferentMembers_IsAWarning()
    {
        var r = TypeConsistencyAuditor.Audit(new TypeConsistencyData
        {
            File      = { Ty("LogEvent", 0, members: "A|B|C") },
            Reference = new List<TypeRegistrationInfo> { Ty("LogEvent", 0, members: "A|B") },
        });

        Assert.That(r.Valid, Is.True);
        Assert.That(r.Issues.Single().Code, Is.EqualTo("W_TYPE_STRUCTURE_MISMATCH"));
    }

    [Test]
    public void Audit_AgainstReference_OneSidedTypesAreWarnings_BecauseAnImportAddsSome()
    {
        // keep_unused_types=true deliberately re-attaches types the save would have dropped, so an extra
        // type in the rebuild is expected — reporting it as an error would cry wolf on every import.
        var r = TypeConsistencyAuditor.Audit(new TypeConsistencyData
        {
            File      = { Ty("OnlyHere", 0) },
            Reference = new List<TypeRegistrationInfo> { Ty("OnlyThere", 0) },
        });

        Assert.That(r.Valid, Is.True);
        Assert.That(r.Issues.Select(i => i.Code),
            Is.EquivalentTo(new[] { "W_TYPE_ONLY_IN_FILE", "W_TYPE_ONLY_IN_REFERENCE" }));
    }

    [Test]
    public void Audit_NullData_DoesNotThrow()
    {
        var r = TypeConsistencyAuditor.Audit(null!);
        Assert.That(r.Valid, Is.True);
        Assert.That(r.IssueCount, Is.Zero);
    }

    [Test]
    public void AuditTypeConsistency_IsRegisteredAndPointsAtTheFileDifferBlindSpot()
    {
        var registry = new TestStandToolRegistry(
            new TestStandService(NullLogger<TestStandService>.Instance),
            new SequenceEditorService(NullLogger<SequenceEditorService>.Instance),
            NullLogger<TestStandToolRegistry>.Instance);
        var tool = registry.GetTools().First(t => t.Name == "audit_type_consistency");

        Assert.That(tool.Description, Does.Contain("identical:true"),
            "the description has to say WHY the diff alone is not enough");
        Assert.That(tool.Description, Does.Contain("E_DUPLICATE_TYPE_NAME"));
    }
}
