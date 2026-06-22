using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// Tests for STARTING a sequence through the station process model (the "Single Pass" / "Test UUTs"
/// entry points), as opposed to a direct client-sequence run.
///
/// A process-model run is the real-world way TestStand sequences are launched and — unlike a direct
/// run — it populates step results and generates a report. StartExecutionAsync resolves a model
/// entry point (name not in the client file but matching a model entry-point sequence, spaces/casing
/// optional) and runs the client THROUGH the model.
///
/// Headless boundary (characterized): "Single Pass" runs unattended to completion; "Test UUTs" parks
/// on the UUT serial-number dialog that has no UI to answer it.
/// </summary>
[TestFixture]
[Category("Execution")]
public class T27_ExecutionStartTests : TestBase
{
    /// <summary>Build TempSeqFile with a single NumericLimitTest (measurement vs [low,high]).</summary>
    private async Task BuildNumericClientAsync(string measurement, double low, double high)
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertStepAsync(TempSeqFile, "MainSequence", "Main", "NumericLimitTest", "Num");
        await Ts.SetStepMeasurementAsync(TempSeqFile, "MainSequence", "Main", "Num", measurement);
        await Ts.SetNumericLimitsAsync(TempSeqFile, "MainSequence", "Main", "Num", low, high, "", "GELE");
        await Ts.SaveSequenceFileAsync(TempSeqFile);
    }

    private static int StepResultCount(System.Collections.Generic.Dictionary<string, object?> results) =>
        (results.TryGetValue("step_results", out var sr) ? sr as System.Collections.IEnumerable : null)
            ?.Cast<object>().Count() ?? 0;

    // ── Single Pass: runs the client THROUGH the model, with step results ────────

    [Test]
    public async Task SinglePass_RunsClientThroughModel_PassedWithStepResults()
    {
        await BuildNumericClientAsync("0.5", 0, 1);   // measurement within limits → pass

        var result = await Ts.RunSequenceAsync(TempSeqFile, "SinglePass", null, 60);

        Assert.That(result.Status, Is.EqualTo("Stopped"));
        Assert.That(result.Result, Is.EqualTo("Passed"));

        // The payoff of a process-model run: step results are populated (a direct run yields none).
        var res = await Ts.GetExecutionResultsAsync(result.ExecutionId);
        Assert.That(StepResultCount(res), Is.GreaterThan(0),
            "A Single Pass (process-model) run must populate step_results; a direct run does not");
    }

    [Test]
    public async Task SinglePass_FailingClient_ReportsFailed()
    {
        await BuildNumericClientAsync("5.0", 0, 1);   // measurement out of limits → fail

        var result = await Ts.RunSequenceAsync(TempSeqFile, "SinglePass", null, 60);

        Assert.That(result.Status, Is.EqualTo("Stopped"));
        Assert.That(result.Result, Is.EqualTo("Failed"));
    }

    [Test]
    public async Task SinglePass_WithSpaceInName_AlsoResolvesToModelEntryPoint()
    {
        await BuildNumericClientAsync("0.5", 0, 1);

        // Exact model entry-point sequence name (with space) must resolve just like "SinglePass".
        var result = await Ts.RunSequenceAsync(TempSeqFile, "Single Pass", null, 60);

        Assert.That(result.Status, Is.EqualTo("Stopped"));
        Assert.That(result.Result, Is.EqualTo("Passed"));
    }

    // ── Test UUTs: made headless via LOCAL callback overrides (NI KB kA00Z000000kElmSAE) ────

    [Test]
    public async Task TestUUTs_WithLocalCallbackOverrides_RunsHeadlessAndProducesResults()
    {
        // By default "Test UUTs" hangs headless on the UUT serial-number dialog (and then on the
        // PostUUT pass/fail banner). Per NI KB kA00Z000000kElmSAE we override the model callbacks
        // IN THIS FILE ONLY and skip the dialog steps. Because the overrides live in the client
        // file (deleted in TearDown), the station process model and its dialogs are untouched — the
        // UUT dialog reappears for every other file / normal interactive use.
        await BuildNumericClientAsync("0.5", 0, 1);

        // PreUUT: drop the serial-number dialog; PostUUT: drop the pass/fail banner.
        await Ts.AddCallbackOverrideAsync(TempSeqFile, "PreUUT");
        await Ts.SetStepRunModeAsync(TempSeqFile, "PreUUT", "Main", "Call DoPreUUT", "Skip");
        await Ts.AddCallbackOverrideAsync(TempSeqFile, "PostUUT");
        await Ts.SetStepRunModeAsync(TempSeqFile, "PostUUT", "Main", "Call DoPostUUT", "Skip");

        // With the dialog skipped the serial would be "NONE" (which stalls the report generator),
        // so PreUUT assigns a real serial and ends the loop after exactly one UUT (RanOnce flag).
        await Ts.SetPropertyValueAsync(TempSeqFile, null, "RanOnce", "number", "0");
        await Ts.InsertStepAsync(TempSeqFile, "PreUUT", "Main", "Statement", "HeadlessSetup");
        await Ts.SetStepExpressionAsync(TempSeqFile, "PreUUT", "Main", "HeadlessSetup",
            "Parameters.UUT.SerialNumber = \"SN_HEADLESS\", " +
            "Parameters.ContinueTesting = ! FileGlobals.RanOnce, FileGlobals.RanOnce = True");
        await Ts.SaveSequenceFileAsync(TempSeqFile);

        var result = await Ts.RunSequenceAsync(TempSeqFile, "TestUUTs", null, 60);

        Assert.That(result.Status, Is.EqualTo("Stopped"),
            "With the local PreUUT/PostUUT overrides, Test UUTs must run headless to completion");
        Assert.That(result.Result, Is.EqualTo("Passed"));

        var res = await Ts.GetExecutionResultsAsync(result.ExecutionId);
        Assert.That(StepResultCount(res), Is.GreaterThan(0),
            "The headless UUT run must still produce step results");
    }
}
