using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// Integration tests for actually RUNNING a sequence (start_execution / run_sequence /
/// wait_for_execution / restart_execution).
///
/// Regression guard for the bug where Engine.NewExecution created an execution that was never
/// driven — it sat parked, no step ever ran, and status was mis-reported as "Stopped". The fix
/// drives every execution with a background pump that calls Execution.WaitForEndEx(...,
/// processWindowsMsgs:true, ...); without that message pump the body never executes.
///
/// "Did the body actually run?" is proven via a StationGlobal side-effect probe: the sequence's
/// single Statement step sets StationGlobals.&lt;probe&gt; = 1. StationGlobals are engine-wide and
/// survive the execution, so we can read the value back through an independent path. Each test
/// uses a unique probe name and deletes it afterwards to leave StationGlobals.ini clean.
/// </summary>
[TestFixture]
[Category("Execution")]
public class T23_ExecutionTests : TestBase
{
    private static string NewProbeName() =>
        "MCP_ExecTestProbe_" + Guid.NewGuid().ToString("N").Substring(0, 8);

    /// <summary>Build TempSeqFile with MainSequence + one Statement step that sets the probe to 1.</summary>
    private async Task BuildProbeSequenceAsync(string probe)
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertStepAsync(TempSeqFile, "MainSequence", "Main", "Statement", "Stmt_SetProbe");
        await Ts.SetStepExpressionAsync(TempSeqFile, "MainSequence", "Main",
            "Stmt_SetProbe", $"StationGlobals.{probe} = 1");
        await Ts.SaveSequenceFileAsync(TempSeqFile);
    }

    private async Task<double> ReadProbeAsync(string probe)
    {
        var globals = await Ts.GetStationGlobalsAsync();
        var g = globals.FirstOrDefault(v => v.Name == probe);
        Assert.That(g, Is.Not.Null, $"StationGlobal '{probe}' not found");
        return Convert.ToDouble(g!.Value);
    }

    // ── run_sequence actually executes the body ─────────────────────────────────

    [Test]
    public async Task RunSequence_ActuallyExecutesBody_AndReportsStopped()
    {
        var probe = NewProbeName();
        await Ts.SetStationGlobalAsync(probe, 0);   // create + reset to 0
        try
        {
            await BuildProbeSequenceAsync(probe);

            var result = await Ts.RunSequenceAsync(TempSeqFile, "MainSequence", null, 60);

            // THE regression guard: the body must have run. The old bug left this at 0.
            Assert.That(await ReadProbeAsync(probe), Is.EqualTo(1.0).Within(1e-9),
                "Sequence body never executed — the execution was created but not driven " +
                "(missing WaitForEndEx(processWindowsMsgs:true) pump).");

            Assert.That(result.Status, Is.EqualTo("Stopped"),
                "A finished execution must report Stopped");
            // Now that the body actually runs, the engine reports a real ResultStatus. A sequence
            // with no failing test step passes, so the overall result is "Passed" (no longer empty
            // / "Unknown" as it was when the execution never ran).
            Assert.That(result.Result, Is.EqualTo("Passed"),
                "A passing direct run should report ResultStatus 'Passed'");
            Assert.That(result.ElapsedSeconds, Is.GreaterThan(0));
        }
        finally
        {
            await Ts.DeleteStationGlobalAsync(probe);
        }
    }

    // ── start_execution + wait_for_execution drives to completion ────────────────

    [Test]
    public async Task StartThenWait_DrivesToCompletion_AndStaysQueryable()
    {
        var probe = NewProbeName();
        await Ts.SetStationGlobalAsync(probe, 0);
        try
        {
            await BuildProbeSequenceAsync(probe);

            var info = await Ts.StartExecutionAsync(TempSeqFile, "MainSequence");
            Assert.That(info.ExecutionId, Is.Not.Empty);

            var result = await Ts.WaitForExecutionAsync(info.ExecutionId, 60);
            Assert.That(result.Status, Is.EqualTo("Stopped"));

            Assert.That(await ReadProbeAsync(probe), Is.EqualTo(1.0).Within(1e-9),
                "Execution body did not run via start+wait");

            // The execution must remain queryable AFTER completion (results were previously lost
            // because wait_for_execution removed it from the active set).
            var status = await Ts.GetExecutionStatusAsync(info.ExecutionId);
            Assert.That(status.Status, Is.EqualTo("Stopped"));

            var res = await Ts.GetExecutionResultsAsync(info.ExecutionId);
            Assert.That(res, Is.Not.Null);
            Assert.That(res.ContainsKey("seconds_elapsed"), Is.True);
        }
        finally
        {
            await Ts.DeleteStationGlobalAsync(probe);
        }
    }

    // ── get_active_executions excludes finished executions ───────────────────────

    [Test]
    public async Task GetActiveExecutions_ListsOnlyRunningOrPaused()
    {
        var probe = NewProbeName();
        await Ts.SetStationGlobalAsync(probe, 0);
        try
        {
            await BuildProbeSequenceAsync(probe);
            var result = await Ts.RunSequenceAsync(TempSeqFile, "MainSequence", null, 60);
            Assert.That(result.Status, Is.EqualTo("Stopped"));

            var active = await Ts.GetActiveExecutionsAsync();
            Assert.That(active.All(e => e.Status is "Running" or "Paused"), Is.True,
                "get_active_executions must only list Running/Paused executions, " +
                "never a completed (Stopped) one");
        }
        finally
        {
            await Ts.DeleteStationGlobalAsync(probe);
        }
    }

    // ── restart_execution no longer throws TargetParameterCountException ─────────

    [Test]
    public async Task RestartExecution_DoesNotThrow()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertStepAsync(TempSeqFile, "MainSequence", "Main", "Statement", "Stmt_Noop");
        await Ts.SaveSequenceFileAsync(TempSeqFile);

        var result = await Ts.RunSequenceAsync(TempSeqFile, "MainSequence", null, 60);
        Assert.That(result.Status, Is.EqualTo("Stopped"));

        // Regression: Execution.Restart takes a required bool (breakOnEntry); the old arg-less
        // call raised TargetParameterCountException via the dynamic COM binder.
        Assert.DoesNotThrowAsync(async () => await Ts.RestartExecutionAsync(result.ExecutionId));

        // Let the restarted execution settle so file close/delete in teardown is clean.
        await Ts.WaitForExecutionAsync(result.ExecutionId, 60);
    }
}
