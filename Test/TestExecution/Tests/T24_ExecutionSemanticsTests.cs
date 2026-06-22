using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// Execution RESULT/STATUS semantics (Passed / Failed / runtime-error) and proof that real
/// sequence logic actually runs end-to-end: multiple steps, step looping, sub-sequence calls and
/// conditional (precondition) execution. Side effects are observed via a uniquely-named
/// StationGlobal probe read back through an independent path (same approach as T23); every probe
/// is deleted afterwards so StationGlobals.ini is left clean.
/// </summary>
[TestFixture]
[Category("Execution")]
public class T24_ExecutionSemanticsTests : TestBase
{
    private static string NewProbe() => "MCP_SemProbe_" + Guid.NewGuid().ToString("N").Substring(0, 8);

    private async Task<double> ReadProbeAsync(string probe)
    {
        var g = (await Ts.GetStationGlobalsAsync()).FirstOrDefault(v => v.Name == probe);
        Assert.That(g, Is.Not.Null, $"StationGlobal '{probe}' not found");
        return Convert.ToDouble(g!.Value);
    }

    // ── A. Result semantics ─────────────────────────────────────────────────────

    [Test]
    public async Task NumericLimitTest_WithinLimits_ReportsPassed()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertStepAsync(TempSeqFile, "MainSequence", "Main", "NumericLimitTest", "Num");
        await Ts.SetStepMeasurementAsync(TempSeqFile, "MainSequence", "Main", "Num", "0.5");
        await Ts.SetNumericLimitsAsync(TempSeqFile, "MainSequence", "Main", "Num", 0, 1, "", "GELE");
        await Ts.SaveSequenceFileAsync(TempSeqFile);

        var r = await Ts.RunSequenceAsync(TempSeqFile, "MainSequence", null, 30);

        Assert.That(r.Status, Is.EqualTo("Stopped"));
        Assert.That(r.Result, Is.EqualTo("Passed"),
            "Measurement 0.5 is within [0,1] → the test (and sequence) must pass");
    }

    [Test]
    public async Task NumericLimitTest_OutOfLimits_ReportsFailed()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertStepAsync(TempSeqFile, "MainSequence", "Main", "NumericLimitTest", "Num");
        await Ts.SetStepMeasurementAsync(TempSeqFile, "MainSequence", "Main", "Num", "5.0");
        await Ts.SetNumericLimitsAsync(TempSeqFile, "MainSequence", "Main", "Num", 0, 1, "", "GELE");
        await Ts.SaveSequenceFileAsync(TempSeqFile);

        var r = await Ts.RunSequenceAsync(TempSeqFile, "MainSequence", null, 30);

        Assert.That(r.Status, Is.EqualTo("Stopped"));
        Assert.That(r.Result, Is.EqualTo("Failed"),
            "Measurement 5.0 is outside [0,1] → the test (and sequence) must fail");
    }

    [Test]
    public async Task RuntimeError_Headless_PausesExecution()
    {
        // Important headless gotcha: an UNHANDLED run-time error makes TestStand enter its
        // interactive error state (normally the run-time error dialog). Headless there is no UI to
        // service it, so the execution PAUSES instead of reaching a terminal "Error" — and any
        // wait_for_execution will time out. Callers must terminate such executions. This test pins
        // that behaviour down so a future change that alters it is caught.
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertStepAsync(TempSeqFile, "MainSequence", "Main", "Statement", "Err");
        await Ts.SetStepExpressionAsync(TempSeqFile, "MainSequence", "Main", "Err",
            "Locals.DoesNotExist == 1");   // references a non-existent property → run-time error
        await Ts.SaveSequenceFileAsync(TempSeqFile);

        var info = await Ts.StartExecutionAsync(TempSeqFile, "MainSequence");
        try
        {
            string status = "";
            for (int i = 0; i < 50; i++)   // up to ~5 s for it to hit the error and pause
            {
                status = (await Ts.GetExecutionStatusAsync(info.ExecutionId)).Status;
                if (status == "Paused") break;
                await Task.Delay(100);
            }
            Assert.That(status, Is.EqualTo("Paused"),
                "An unhandled run-time error should leave the execution Paused (interactive error " +
                "state) under headless operation, not Stopped");
        }
        finally
        {
            await Ts.TerminateExecutionAsync(info.ExecutionId);
        }
    }

    // ── B. Execution depth: many steps, loops, sub-sequences, conditionals ───────

    [Test]
    public async Task MultiStep_AllStepsExecuteInOrder()
    {
        var probe = NewProbe();
        await Ts.SetStationGlobalAsync(probe, 0);
        try
        {
            await Ts.CreateSequenceFileAsync(TempSeqFile);
            foreach (var name in new[] { "S1", "S2", "S3" })
            {
                await Ts.InsertStepAsync(TempSeqFile, "MainSequence", "Main", "Statement", name);
                await Ts.SetStepExpressionAsync(TempSeqFile, "MainSequence", "Main", name,
                    $"StationGlobals.{probe} = StationGlobals.{probe} + 1");
            }
            await Ts.SaveSequenceFileAsync(TempSeqFile);

            var r = await Ts.RunSequenceAsync(TempSeqFile, "MainSequence", null, 30);

            Assert.That(r.Status, Is.EqualTo("Stopped"));
            Assert.That(await ReadProbeAsync(probe), Is.EqualTo(3.0).Within(1e-9),
                "All three Statement steps should have executed exactly once");
        }
        finally { await Ts.DeleteStationGlobalAsync(probe); }
    }

    [Test]
    public async Task StepLoop_RunsBodyConfiguredNumberOfTimes()
    {
        var probe = NewProbe();
        await Ts.SetStationGlobalAsync(probe, 0);
        try
        {
            await Ts.CreateSequenceFileAsync(TempSeqFile);
            await Ts.InsertStepAsync(TempSeqFile, "MainSequence", "Main", "Statement", "Body");
            // Body increments the probe; the step loops while the probe is below 5.
            await Ts.SetStepExpressionAsync(TempSeqFile, "MainSequence", "Main", "Body",
                $"StationGlobals.{probe} = StationGlobals.{probe} + 1");
            await Ts.SetStepLoopAsync(TempSeqFile, "MainSequence", "Main", "Body",
                "While", whileExpr: $"StationGlobals.{probe} < 5");
            await Ts.SaveSequenceFileAsync(TempSeqFile);

            var r = await Ts.RunSequenceAsync(TempSeqFile, "MainSequence", null, 30);

            Assert.That(r.Status, Is.EqualTo("Stopped"));
            Assert.That(await ReadProbeAsync(probe), Is.EqualTo(5.0).Within(1e-9),
                "The looped step body should have executed exactly 5 times");
        }
        finally { await Ts.DeleteStationGlobalAsync(probe); }
    }

    [Test]
    public async Task SequenceCall_ExecutesTargetSubsequence()
    {
        var probe = NewProbe();
        await Ts.SetStationGlobalAsync(probe, 0);
        try
        {
            await Ts.CreateSequenceFileAsync(TempSeqFile);
            // Sub-sequence whose single step sets the probe.
            await Ts.InsertSequenceAsync(TempSeqFile, "Sub");
            await Ts.InsertStepAsync(TempSeqFile, "Sub", "Main", "Statement", "SubBody");
            await Ts.SetStepExpressionAsync(TempSeqFile, "Sub", "Main", "SubBody",
                $"StationGlobals.{probe} = 1");
            // MainSequence calls Sub.
            await Ts.InsertStepAsync(TempSeqFile, "MainSequence", "Main", "SequenceCall", "CallSub");
            await Ts.SetSequenceCallTargetAsync(TempSeqFile, "MainSequence", "Main", "CallSub", "Sub");
            await Ts.SaveSequenceFileAsync(TempSeqFile);

            var r = await Ts.RunSequenceAsync(TempSeqFile, "MainSequence", null, 30);

            Assert.That(r.Status, Is.EqualTo("Stopped"));
            Assert.That(await ReadProbeAsync(probe), Is.EqualTo(1.0).Within(1e-9),
                "The called sub-sequence's step must have executed");
        }
        finally { await Ts.DeleteStationGlobalAsync(probe); }
    }

    [Test]
    public async Task StepPrecondition_FalseSkipsStep_TrueRunsStep()
    {
        var ran = NewProbe();        // set by the step whose precondition is True
        var skipped = NewProbe();    // would be set by the step whose precondition is False
        await Ts.SetStationGlobalAsync(ran, 0);
        await Ts.SetStationGlobalAsync(skipped, 0);
        try
        {
            await Ts.CreateSequenceFileAsync(TempSeqFile);

            await Ts.InsertStepAsync(TempSeqFile, "MainSequence", "Main", "Statement", "Runs");
            await Ts.SetStepExpressionAsync(TempSeqFile, "MainSequence", "Main", "Runs",
                $"StationGlobals.{ran} = 1");
            await Ts.SetStepPreconditionAsync(TempSeqFile, "MainSequence", "Main", "Runs", "True");

            await Ts.InsertStepAsync(TempSeqFile, "MainSequence", "Main", "Statement", "Skipped");
            await Ts.SetStepExpressionAsync(TempSeqFile, "MainSequence", "Main", "Skipped",
                $"StationGlobals.{skipped} = 1");
            await Ts.SetStepPreconditionAsync(TempSeqFile, "MainSequence", "Main", "Skipped", "False");

            await Ts.SaveSequenceFileAsync(TempSeqFile);

            var r = await Ts.RunSequenceAsync(TempSeqFile, "MainSequence", null, 30);

            Assert.That(r.Status, Is.EqualTo("Stopped"));
            Assert.That(await ReadProbeAsync(ran), Is.EqualTo(1.0).Within(1e-9),
                "Step with precondition True must run");
            Assert.That(await ReadProbeAsync(skipped), Is.EqualTo(0.0).Within(1e-9),
                "Step with precondition False must be skipped");
        }
        finally
        {
            await Ts.DeleteStationGlobalAsync(ran);
            await Ts.DeleteStationGlobalAsync(skipped);
        }
    }
}
