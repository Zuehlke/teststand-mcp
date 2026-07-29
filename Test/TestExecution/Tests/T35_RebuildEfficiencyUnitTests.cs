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
}
