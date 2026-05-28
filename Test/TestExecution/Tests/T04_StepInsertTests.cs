using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// Verifies that steps of every supported type can be inserted into a sequence,
/// and that the step name and comment round-trip correctly after save.
/// </summary>
[TestFixture]
[Category("StepInsert")]
public class T04_StepInsertTests : TestBase
{
    private const string Seq  = "StepTests";
    private const string Grp  = "Main";

    private async Task SetupAsync()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, Seq);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private async Task AssertStepRoundTrip(string stepType, string stepName, string comment)
    {
        await Ts.InsertStepAsync(TempSeqFile, Seq, Grp, stepType, stepName);
        await Ts.SetStepCommentAsync(TempSeqFile, Seq, Grp, stepName, comment);
        await Ts.SaveSequenceFileAsync(TempSeqFile);

        var steps = await Ts.GetStepsAsync(TempSeqFile, Seq);
        var step  = steps.FirstOrDefault(s => s.Name == stepName);

        Assert.That(step,             Is.Not.Null,             $"Step '{stepName}' not found after insert+save");
        Assert.That(step!.StepType,   Is.EqualTo(stepType),    $"StepType mismatch for '{stepName}'");
        Assert.That(step.Description, Is.EqualTo(comment),     $"Comment mismatch for '{stepName}'");
    }

    // ── Action step types ─────────────────────────────────────────────────────

    [Test]
    public async Task Insert_Statement_NameAndCommentRoundTrip()
    {
        await SetupAsync();
        await AssertStepRoundTrip("Statement", "Stmt_SetVar",
            "Sets a local variable to its initial value.");
    }

    [Test]
    public async Task Insert_Action_NameAndCommentRoundTrip()
    {
        await SetupAsync();
        await AssertStepRoundTrip("Action", "Act_Initialize",
            "Performs device initialization.");
    }

    [Test]
    public async Task Insert_MessagePopup_NameAndCommentRoundTrip()
    {
        await SetupAsync();
        await AssertStepRoundTrip("MessagePopup", "Msg_UserPrompt",
            "Asks the operator to connect the DUT.");
    }

    [Test]
    public async Task Insert_CallExecutable_NameAndCommentRoundTrip()
    {
        await SetupAsync();
        await AssertStepRoundTrip("CallExecutable", "Exec_LaunchTool",
            "Launches an external calibration tool.");
    }

    [Test]
    public async Task Insert_SequenceCall_NameAndCommentRoundTrip()
    {
        await SetupAsync();
        await AssertStepRoundTrip("SequenceCall", "Call_SubTest",
            "Calls the sub-test sequence.");
    }

    // ── Test step types ───────────────────────────────────────────────────────

    [Test]
    public async Task Insert_NumericLimitTest_NameAndCommentRoundTrip()
    {
        await SetupAsync();
        await AssertStepRoundTrip("NumericLimitTest", "Num_SupplyVoltage",
            "Verifies that the supply voltage is within spec.");
    }

    [Test]
    public async Task Insert_StringValueTest_NameAndCommentRoundTrip()
    {
        await SetupAsync();
        await AssertStepRoundTrip("StringValueTest", "Str_FirmwareVersion",
            "Checks that the firmware version matches the expected string.");
    }

    [Test]
    public async Task Insert_PassFailTest_NameAndCommentRoundTrip()
    {
        await SetupAsync();
        await AssertStepRoundTrip("PassFailTest", "PF_ConnectionCheck",
            "Verifies device is properly connected.");
    }

    [Test]
    public async Task Insert_NI_MultipleNumericLimitTest_NameAndCommentRoundTrip()
    {
        await SetupAsync();
        await AssertStepRoundTrip("NI_MultipleNumericLimitTest", "Multi_SensorReadings",
            "Measures multiple sensor channels simultaneously.");
    }

    // ── Flow control step types ────────────────────────────────────────────────

    [Test]
    public async Task Insert_NI_Flow_If_NameAndCommentRoundTrip()
    {
        await SetupAsync();
        await AssertStepRoundTrip("NI_Flow_If",   "If_ValueCheck",   "Branches if value is positive.");
        await AssertStepRoundTrip("NI_Flow_ElseIf","ElseIf_Zero",    "Handles zero case.");
        await AssertStepRoundTrip("NI_Flow_Else",  "Else_Negative",  "Handles negative case.");
        await AssertStepRoundTrip("NI_Flow_End",   "End_IfBlock",    "Ends the if/elseif/else block.");
    }

    [Test]
    public async Task Insert_NI_Flow_While_NameAndCommentRoundTrip()
    {
        await SetupAsync();
        await AssertStepRoundTrip("NI_Flow_While", "While_Polling",  "Polls until condition is met.");
        await AssertStepRoundTrip("NI_Flow_End",   "End_While",      "Ends the while loop.");
    }

    [Test]
    public async Task Insert_NI_Flow_DoWhile_NameAndCommentRoundTrip()
    {
        await SetupAsync();
        await AssertStepRoundTrip("NI_Flow_DoWhile", "DoWhile_Retry",  "Retries at least once.");
        await AssertStepRoundTrip("NI_Flow_End",     "End_DoWhile",    "Ends the do-while loop.");
    }

    [Test]
    public async Task Insert_NI_Flow_For_NameAndCommentRoundTrip()
    {
        await SetupAsync();
        await AssertStepRoundTrip("NI_Flow_For", "For_Iterations", "Iterates a fixed number of times.");
        await AssertStepRoundTrip("NI_Flow_End", "End_For",        "Ends the for loop.");
    }

    [Test]
    public async Task Insert_NI_Flow_ForEach_NameAndCommentRoundTrip()
    {
        await SetupAsync();
        await AssertStepRoundTrip("NI_Flow_ForEach", "ForEach_Items", "Iterates over each array element.");
        await AssertStepRoundTrip("NI_Flow_End",     "End_ForEach",   "Ends the for-each loop.");
    }

    [Test]
    public async Task Insert_NI_Flow_Select_NameAndCommentRoundTrip()
    {
        await SetupAsync();
        await AssertStepRoundTrip("NI_Flow_Select", "Select_Mode",    "Selects execution path based on mode.");
        await AssertStepRoundTrip("NI_Flow_Case",   "Case_ModeA",     "Handles mode A.");
        await AssertStepRoundTrip("NI_Flow_Case",   "Case_ModeB",     "Handles mode B.");
        await AssertStepRoundTrip("NI_Flow_End",    "End_Select",     "Ends the select block.");
    }

    [Test]
    public async Task Insert_NI_Flow_Break_And_Continue_NameAndCommentRoundTrip()
    {
        await SetupAsync();
        // Must be inside a loop context for TestStand to accept Break/Continue
        await Ts.InsertStepAsync(TempSeqFile, Seq, Grp, "NI_Flow_While",    "While_Outer");
        await Ts.InsertStepAsync(TempSeqFile, Seq, Grp, "NI_Flow_If",       "If_BreakCond");
        await Ts.InsertStepAsync(TempSeqFile, Seq, Grp, "NI_Flow_Break",    "Break_Exit");
        await Ts.InsertStepAsync(TempSeqFile, Seq, Grp, "NI_Flow_End",      "End_IfBreak");
        await Ts.InsertStepAsync(TempSeqFile, Seq, Grp, "NI_Flow_If",       "If_ContCond");
        await Ts.InsertStepAsync(TempSeqFile, Seq, Grp, "NI_Flow_Continue", "Continue_Next");
        await Ts.InsertStepAsync(TempSeqFile, Seq, Grp, "NI_Flow_End",      "End_IfCont");
        await Ts.InsertStepAsync(TempSeqFile, Seq, Grp, "NI_Flow_End",      "End_WhileOuter");

        // Set comments
        var stepsWithComments = new (string Name, string Comment)[]
        {
            ("While_Outer",    "Outer polling loop."),
            ("If_BreakCond",   "Checks break condition."),
            ("Break_Exit",     "Breaks out of outer loop."),
            ("End_IfBreak",    "Ends break-condition block."),
            ("If_ContCond",    "Checks continue condition."),
            ("Continue_Next",  "Skips remainder of loop body."),
            ("End_IfCont",     "Ends continue-condition block."),
            ("End_WhileOuter", "Ends outer polling loop."),
        };

        foreach (var (name, comment) in stepsWithComments)
            await Ts.SetStepCommentAsync(TempSeqFile, Seq, Grp, name, comment);

        await Ts.SaveSequenceFileAsync(TempSeqFile);

        var steps = await Ts.GetStepsAsync(TempSeqFile, Seq);
        foreach (var (name, comment) in stepsWithComments)
        {
            var s = steps.FirstOrDefault(x => x.Name == name);
            Assert.That(s, Is.Not.Null, $"Step '{name}' not found");
            Assert.That(s!.Description, Is.EqualTo(comment), $"Comment mismatch for '{name}'");
        }
    }

    // ── Rename step ────────────────────────────────────────────────────────────

    [Test]
    public async Task RenameStep_ChangesName()
    {
        await SetupAsync();
        await Ts.InsertStepAsync(TempSeqFile, Seq, Grp, "Statement", "OldStep");
        await Ts.RenameStepAsync(TempSeqFile, Seq, Grp, "OldStep", "NewStep");
        await Ts.SaveSequenceFileAsync(TempSeqFile);

        Assert.That(await Ts.StepNameExistsAsync(TempSeqFile, Seq, "OldStep"), Is.False);
        Assert.That(await Ts.StepNameExistsAsync(TempSeqFile, Seq, "NewStep"), Is.True);
    }

    // ── Enable / Disable ──────────────────────────────────────────────────────

    [Test]
    public async Task EnableDisableStep_TogglesEnabled()
    {
        await SetupAsync();
        await Ts.InsertStepAsync(TempSeqFile, Seq, Grp, "Statement", "Toggleable");
        await Ts.EnableStepAsync(TempSeqFile, Seq, "Toggleable", false);
        await Ts.SaveSequenceFileAsync(TempSeqFile);

        var steps = await Ts.GetStepsAsync(TempSeqFile, Seq);
        var s     = steps.FirstOrDefault(x => x.Name == "Toggleable");

        Assert.That(s, Is.Not.Null);
        Assert.That(s!.Enabled, Is.False, "Step should be disabled after EnableStep(false)");
    }

    // ── Delete step ────────────────────────────────────────────────────────────

    [Test]
    public async Task DeleteStep_RemovesStep()
    {
        await SetupAsync();
        await Ts.InsertStepAsync(TempSeqFile, Seq, Grp, "Statement", "ToDelete");
        await Ts.DeleteStepAsync(TempSeqFile, Seq, Grp, "ToDelete");
        await Ts.SaveSequenceFileAsync(TempSeqFile);

        Assert.That(await Ts.StepNameExistsAsync(TempSeqFile, Seq, "ToDelete"), Is.False);
    }

    // ── Move step ─────────────────────────────────────────────────────────────

    [Test]
    public async Task MoveStep_ChangesPosition()
    {
        await SetupAsync();
        await Ts.InsertStepAsync(TempSeqFile, Seq, Grp, "Statement", "Alpha");
        await Ts.InsertStepAsync(TempSeqFile, Seq, Grp, "Statement", "Beta");
        await Ts.InsertStepAsync(TempSeqFile, Seq, Grp, "Statement", "Gamma");

        // Move Alpha (index 0) to index 2 (last)
        await Ts.MoveStepAsync(TempSeqFile, Seq, Grp, "Alpha", 2);
        await Ts.SaveSequenceFileAsync(TempSeqFile);

        var steps = await Ts.GetStepsAsync(TempSeqFile, Seq);
        Assert.That(steps[0].Name, Is.EqualTo("Beta"),  "Beta should now be first");
        Assert.That(steps[2].Name, Is.EqualTo("Alpha"), "Alpha should now be last");
    }
}
