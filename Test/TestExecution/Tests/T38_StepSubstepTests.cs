using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using TestStandMCP.Models;
using TestStandMCP.Tools;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// get_step_substeps / run_step_substep (issue #51). Only NON-interactive substeps run here: an Edit
/// substep of a built-in type opens a dialog and would block the test host.
/// </summary>
[TestFixture]
[Category("StepConfig")]
public class T38_StepSubstepTests : TestBase
{
    private async Task InsertAsync(string stepType)
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertStepAsync(TempSeqFile, "MainSequence", "Main", stepType, "S");
    }

    [Test]
    public async Task GetStepSubsteps_NiWait_ListsKindsAndEditButtonText()
    {
        await InsertAsync("NI_Wait");
        var r = await Ts.GetStepSubstepsAsync(TempSeqFile, "MainSequence", "Main", "S");

        Assert.That(r.StepType, Is.EqualTo("NI_Wait"));
        Assert.That(r.Substeps.Select(s => s.Kind), Is.EqualTo(new[] { "OnNewStep", "Post", "Edit" }));
        var edit = r.Substeps.Single(s => s.Kind == "Edit");
        Assert.That(edit.MenuText, Is.EqualTo("Configure Wait..."));
        Assert.That(edit.HasEditPanel, Is.True);
        Assert.That(edit.Adapter, Is.EqualTo("DLL Flexible Prototype Adapter"));
        Assert.That(edit.ModulePath, Is.EqualTo("SyncSteps.dll").IgnoreCase);
        Assert.That(r.Substeps.All(s => s.CanExecute));
    }

    [Test]
    public async Task RunStepSubstep_OnNewStep_RunsHeadlessAndReportsDiff()
    {
        await InsertAsync("NI_Wait");
        var r = await Ts.RunStepSubstepAsync(TempSeqFile, "MainSequence", "Main", "S",
            "OnNewStep", null, timeoutSeconds: 30);

        Assert.That(r.SubstepIndex, Is.EqualTo(0));
        Assert.That(r.SubstepName, Is.EqualTo("OnNewStep"));
        Assert.That(r.Saved, Is.False);
        // Measured: NI_Wait's OnNewStep authors the 'time to wait' result hint, yet ExecuteSubstep
        // returns false (it does not bump the change count). Only the read-back diff shows the effect.
        Assert.That(r.Changes.Any(c => c.Path == "TS.AdditionalResultsHints[0].ValueToLog" &&
            (string?)c.After == "Evaluate(Step.TimeExpr)"), Is.True,
            string.Join("; ", r.Changes.Select(c => $"{c.Path}: {c.Before} -> {c.After}")));
        Assert.That(r.Changes.Where(c => c.Before == null && c.After == null), Is.Empty,
            "an empty array or container is structure, not a changed leaf");
        Assert.That(r.Note, Does.Contain("save_sequence_file"));
    }

    [Test]
    public void RunStepSubstep_UnknownName_IsRefusedWithHint()
    {
        Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await InsertAsync("NI_Wait");
            await Ts.RunStepSubstepAsync(TempSeqFile, "MainSequence", "Main", "S", "Refresh", null);
        });
    }

    [Test]
    public void RunStepSubstep_IndexOutOfRange_IsRefused()
    {
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await InsertAsync("NI_Wait");
            await Ts.RunStepSubstepAsync(TempSeqFile, "MainSequence", "Main", "S", null, 99);
        });
    }

    // ── Bulk form ───────────────────────────────────────────────────────────

    private async Task BuildBulkFileAsync()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "Sub");
        await Ts.InsertStepAsync(TempSeqFile, "MainSequence", "Main", "NI_Wait", "W");
        await Ts.InsertStepAsync(TempSeqFile, "MainSequence", "Main", "MessagePopup", "P");
        await Ts.InsertStepAsync(TempSeqFile, "MainSequence", "Cleanup", "NI_Wait", "W");
        await Ts.InsertStepAsync(TempSeqFile, "Sub", "Main", "NI_Wait", "W2");
    }

    [Test]
    public async Task RunSubstepOnSteps_WholeFile_RunsEveryStepOfTheTypeOnly()
    {
        await BuildBulkFileAsync();
        var r = await Ts.RunSubstepOnStepsAsync(TempSeqFile, "NI_Wait", "OnNewStep", null, save: true);

        Assert.That(r.Matched, Is.EqualTo(3));
        Assert.That(r.Ran, Is.EqualTo(3));
        Assert.That(r.Changed, Is.EqualTo(3));
        Assert.That(r.Failed, Is.EqualTo(0));
        Assert.That(r.Saved, Is.True);
        Assert.That(r.Steps.Select(s => $"{s.Sequence}/{s.StepGroup}/{s.StepIndex}/{s.StepName}"),
            Is.EqualTo(new[] { "MainSequence/Main/0/W", "MainSequence/Cleanup/0/W", "Sub/Main/0/W2" }));
        Assert.That(r.Steps.All(s => s.Changes == null), "changes only with include_changes");

        // sequence_name narrows the scope; include_changes adds the per-step leaf list.
        var again = await Ts.RunSubstepOnStepsAsync(TempSeqFile, "NI_Wait", "OnNewStep", null,
            sequenceName: "Sub", includeChanges: true);
        Assert.That(again.Matched, Is.EqualTo(1));
        Assert.That(again.Steps[0].Changes, Is.Not.Null);
    }

    [Test]
    public async Task RunSubstepOnSteps_NoStepOfType_ReportsInsteadOfFailing()
    {
        await BuildBulkFileAsync();
        var r = await Ts.RunSubstepOnStepsAsync(TempSeqFile, "NumericLimitTest", "Edit", null);
        Assert.That(r.Matched, Is.EqualTo(0));
        Assert.That(r.Note, Does.Contain("No step of type"));
    }

    // ── LabVIEW / UI guards (pure) ──────────────────────────────────────────

    private static StepSubstepInfo Lv(string path, bool? panel = false) =>
        new() { Name = "Refresh", Adapter = "G Flexible VI Adapter", ModulePath = path, ShowsFrontPanel = panel };

    [Test]
    public void Guard_PackedLibraryVi_IsRefusedUnlessForced()
    {
        var sub = Lv(@"C:\Lib\Steps.lvlibp\X_gui.vi");
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TestStandMCP.Services.TestStandService.GuardSubstep(sub, allowUi: false, forceUnsafeInProcess: false));
        Assert.That(ex!.Message, Does.Contain("packed library"));
        Assert.DoesNotThrow(() =>
            TestStandMCP.Services.TestStandService.GuardSubstep(sub, allowUi: false, forceUnsafeInProcess: true));
    }

    [Test]
    public void Guard_FrontPanel_IsRefusedUnlessAllowUi_PlainViPasses()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TestStandMCP.Services.TestStandService.GuardSubstep(Lv(@"C:\VIs\X_gui.vi", true), false, false));
        Assert.DoesNotThrow(() =>
            TestStandMCP.Services.TestStandService.GuardSubstep(Lv(@"C:\VIs\X_gui.vi", true), true, false));
        Assert.DoesNotThrow(() =>
            TestStandMCP.Services.TestStandService.GuardSubstep(Lv(@"C:\VIs\X_gui.vi"), false, false));
    }

    // ── PropertyTreeDiff (pure) ─────────────────────────────────────────────

    private static PropertyNode Leaf(string n, object? v) => new() { Name = n, Value = v };

    [Test]
    public void PropertyTreeDiff_FlattensDottedAndIndexedPaths()
    {
        var tree = new PropertyNode { Children = new() {
            new() { Name = "Config", Children = new() { Leaf("Description", "a") } },
            new() { Name = "Arr", Children = new() { Leaf("[0]", 1.0) } } } };
        var flat = PropertyTreeDiff.Flatten(tree);
        Assert.That(flat.Keys, Is.EquivalentTo(new[] { "Config.Description", "Arr[0]" }));
    }

    [Test]
    public void PropertyTreeDiff_EmptyArrayGainingAnElement_ReportsOnlyTheNewLeaves()
    {
        var before = new PropertyNode { Children = new() { new() { Name = "Hints" } } };
        var after  = new PropertyNode { Children = new() { new() { Name = "Hints", Children = new() {
            new() { Name = "[0]", Children = new() { Leaf("Name", "x"), new() { Name = "Elements" } } } } } } };
        var d = PropertyTreeDiff.Diff(PropertyTreeDiff.Flatten(before), PropertyTreeDiff.Flatten(after));
        Assert.That(d.Select(c => c.Path), Is.EqualTo(new[] { "Hints[0].Name" }));
    }

    [Test]
    public void PropertyTreeDiff_ReportsChangedAddedAndRemovedLeaves()
    {
        var before = new Dictionary<string, object?> { ["A"] = "x", ["B"] = 1.0, ["Gone"] = true };
        var after  = new Dictionary<string, object?> { ["A"] = "y", ["B"] = 1.0, ["New"] = "n" };
        var d = PropertyTreeDiff.Diff(before, after);
        Assert.That(d.Select(c => c.Path), Is.EqualTo(new[] { "A", "Gone", "New" }));
        Assert.That(d[0].Before, Is.EqualTo("x"));
        Assert.That(d[0].After, Is.EqualTo("y"));
    }
}
