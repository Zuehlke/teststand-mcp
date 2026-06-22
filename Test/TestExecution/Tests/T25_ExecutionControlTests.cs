using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// Execution control / async / lifecycle: an execution is observably Running while in progress,
/// its thread reports its current position mid-run, terminate stops it, a running execution does
/// not block other engine calls, and independent executions run concurrently to completion. Plus
/// negative contracts (unknown sequence / unknown execution id).
///
/// A MessagePopup step is used as a deterministic, non-busy "holder": headless there is no UI to
/// answer it, so the execution sits Running (parked on the step) until terminated — giving a stable
/// window to observe Running/thread state without a timing-sensitive delay step.
/// </summary>
[TestFixture]
[Category("Execution")]
public class T25_ExecutionControlTests : TestBase
{
    private static string TempPath(string tag) =>
        Path.Combine(Path.GetTempPath(), $"TS_T25_{tag}_{Guid.NewGuid():N}.seq");

    private async Task BuildPopupHolderAsync(string path)
    {
        await Ts.CreateSequenceFileAsync(path);
        await Ts.InsertStepAsync(path, "MainSequence", "Main", "MessagePopup", "Pop");
        await Ts.SaveSequenceFileAsync(path);
    }

    private async Task BuildProbeSequenceAsync(string path, string probe)
    {
        await Ts.CreateSequenceFileAsync(path);
        await Ts.InsertStepAsync(path, "MainSequence", "Main", "Statement", "SetProbe");
        await Ts.SetStepExpressionAsync(path, "MainSequence", "Main", "SetProbe",
            $"StationGlobals.{probe} = 1");
        await Ts.SaveSequenceFileAsync(path);
    }

    private async Task<double> ReadProbeAsync(string probe)
    {
        var g = (await Ts.GetStationGlobalsAsync()).FirstOrDefault(v => v.Name == probe);
        Assert.That(g, Is.Not.Null, $"StationGlobal '{probe}' not found");
        return Convert.ToDouble(g!.Value);
    }

    private async Task TryCleanupAsync(string execId, string path)
    {
        if (execId != null) { try { await Ts.TerminateExecutionAsync(execId); } catch { } }
        if (path != null)
        {
            try { await Ts.CloseSequenceFileAsync(path); } catch { }
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    // ── Observably running ───────────────────────────────────────────────────────

    [Test]
    public async Task StartExecution_BlockedStep_IsObservablyRunning()
    {
        await BuildPopupHolderAsync(TempSeqFile);
        var info = await Ts.StartExecutionAsync(TempSeqFile, "MainSequence");
        try
        {
            Assert.That(info.Status, Is.EqualTo("Running"),
                "A started execution parked on a MessagePopup should report Running");
            // Still running a moment later (it cannot finish without a UI answer).
            var later = await Ts.GetExecutionStatusAsync(info.ExecutionId);
            Assert.That(later.Status, Is.EqualTo("Running"));
        }
        finally { await Ts.TerminateExecutionAsync(info.ExecutionId); }
    }

    // ── Threads report current position mid-run ──────────────────────────────────

    [Test]
    public async Task GetExecutionThreads_MidRun_ReportsCurrentStepAndDepth()
    {
        await BuildPopupHolderAsync(TempSeqFile);
        var info = await Ts.StartExecutionAsync(TempSeqFile, "MainSequence");
        try
        {
            // Wait until the thread is parked on the step.
            string step = "", seq = "", state = "";
            int depth = 0;
            for (int i = 0; i < 30; i++)
            {
                var threads = await Ts.GetExecutionThreadsAsync(info.ExecutionId);
                if (threads.Count > 0 && !string.IsNullOrEmpty(threads[0].CurrentStepName))
                {
                    step  = threads[0].CurrentStepName;
                    seq   = threads[0].CurrentSequenceName;
                    depth = threads[0].StackDepth;
                    state = threads[0].State;
                    break;
                }
                await Task.Delay(100);
            }

            Assert.That(step, Is.EqualTo("Pop"),
                "Thread should report the step it is parked on (regression: this used to be empty " +
                "because GetSequenceContext was called with the wrong arity)");
            Assert.That(seq, Is.EqualTo("MainSequence"));
            Assert.That(depth, Is.GreaterThanOrEqualTo(1),
                "Stack depth should reflect the active call-stack frame (was always 0 before)");
            Assert.That(state, Is.EqualTo("Running"));
        }
        finally { await Ts.TerminateExecutionAsync(info.ExecutionId); }
    }

    [Test]
    public async Task GetThreadCallStack_MidRun_ReturnsFramesFromCurrentStepToEntryPoint()
    {
        // MainSequence → SequenceCall → Sub (MessagePopup) gives a 2-deep call stack while parked.
        // Regression: get_thread_call_stack always returned an empty list because it read a
        // non-existent "StackDepth" (→ 0) and called GetSequenceContext with the wrong arity.
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "Sub");
        await Ts.InsertStepAsync(TempSeqFile, "Sub", "Main", "MessagePopup", "Pop");
        await Ts.InsertStepAsync(TempSeqFile, "MainSequence", "Main", "SequenceCall", "CallSub");
        await Ts.SetSequenceCallTargetAsync(TempSeqFile, "MainSequence", "Main", "CallSub", "Sub");
        await Ts.SaveSequenceFileAsync(TempSeqFile);

        var info = await Ts.StartExecutionAsync(TempSeqFile, "MainSequence");
        try
        {
            int frameCount = 0;
            string deepestSeq = "", deepestStep = "";
            bool includesCaller = false;
            for (int i = 0; i < 30; i++)
            {
                var stack = await Ts.GetThreadCallStackAsync(info.ExecutionId, "0");
                if (stack.Count >= 2)
                {
                    frameCount     = stack.Count;
                    deepestSeq     = stack[0].SequenceName;   // frame 0 = current (deepest) frame
                    deepestStep    = stack[0].StepName;
                    includesCaller = stack.Any(f => f.SequenceName == "MainSequence");
                    break;
                }
                await Task.Delay(100);
            }

            Assert.That(frameCount, Is.GreaterThanOrEqualTo(2),
                "Call stack should expose at least 2 frames (Sub at the popup + the MainSequence " +
                "caller); it used to always come back empty");
            Assert.That(deepestSeq, Is.EqualTo("Sub"));
            Assert.That(deepestStep, Is.EqualTo("Pop"));
            Assert.That(includesCaller, Is.True,
                "The call stack must include the calling MainSequence frame");
        }
        finally { await Ts.TerminateExecutionAsync(info.ExecutionId); }
    }

    // ── Terminate stops a running execution ──────────────────────────────────────

    [Test]
    public async Task TerminateExecution_StopsRunningExecution()
    {
        await BuildPopupHolderAsync(TempSeqFile);
        var info = await Ts.StartExecutionAsync(TempSeqFile, "MainSequence");

        var activeBefore = await Ts.GetActiveExecutionsAsync();
        Assert.That(activeBefore.Any(e => e.ExecutionId == info.ExecutionId), Is.True,
            "Execution should be active while parked on the popup");

        await Ts.TerminateExecutionAsync(info.ExecutionId);

        var activeAfter = await Ts.GetActiveExecutionsAsync();
        Assert.That(activeAfter.Any(e => e.ExecutionId == info.ExecutionId), Is.False,
            "Terminated execution must no longer be active");
    }

    // ── A running execution does not block other engine calls ───────────────────

    [Test]
    public async Task RunningExecution_DoesNotBlockOtherEngineCalls()
    {
        var otherPath = TempPath("other");
        await BuildPopupHolderAsync(TempSeqFile);
        var info = await Ts.StartExecutionAsync(TempSeqFile, "MainSequence");
        try
        {
            // While the execution is parked and Running, ordinary file operations on a DIFFERENT
            // file must still work (the engine is shared MTA; the message pump runs on its own
            // thread). This would hang/contend if executions were driven on a shared request thread.
            await Ts.CreateSequenceFileAsync(otherPath);
            await Ts.InsertStepAsync(otherPath, "MainSequence", "Main", "Statement", "X");
            await Ts.SaveSequenceFileAsync(otherPath);
            var steps = await Ts.GetStepsAsync(otherPath, "MainSequence");

            Assert.That(steps.Any(s => s.Name == "X"), Is.True,
                "File operations should succeed while another execution is running");

            var stillRunning = await Ts.GetExecutionStatusAsync(info.ExecutionId);
            Assert.That(stillRunning.Status, Is.EqualTo("Running"));
        }
        finally
        {
            await Ts.TerminateExecutionAsync(info.ExecutionId);
            await TryCleanupAsync(null!, otherPath);
        }
    }

    // ── Independent executions run concurrently to completion ────────────────────

    [Test]
    public async Task TwoExecutions_RunConcurrentlyToCompletion()
    {
        var probeA = "MCP_T25_A_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var probeB = "MCP_T25_B_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var pathA = TempPath("A");
        var pathB = TempPath("B");
        await Ts.SetStationGlobalAsync(probeA, 0);
        await Ts.SetStationGlobalAsync(probeB, 0);
        try
        {
            await BuildProbeSequenceAsync(pathA, probeA);
            await BuildProbeSequenceAsync(pathB, probeB);

            var a = await Ts.StartExecutionAsync(pathA, "MainSequence");
            var b = await Ts.StartExecutionAsync(pathB, "MainSequence");

            await Ts.WaitForExecutionAsync(a.ExecutionId, 30);
            await Ts.WaitForExecutionAsync(b.ExecutionId, 30);

            Assert.That(await ReadProbeAsync(probeA), Is.EqualTo(1.0).Within(1e-9),
                "First execution did not run to completion");
            Assert.That(await ReadProbeAsync(probeB), Is.EqualTo(1.0).Within(1e-9),
                "Second execution did not run to completion");
        }
        finally
        {
            await Ts.DeleteStationGlobalAsync(probeA);
            await Ts.DeleteStationGlobalAsync(probeB);
            await TryCleanupAsync(null!, pathA);
            await TryCleanupAsync(null!, pathB);
        }
    }

    // ── Negative contracts ───────────────────────────────────────────────────────

    [Test]
    public async Task StartExecution_UnknownSequenceName_Throws()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.SaveSequenceFileAsync(TempSeqFile);

        Assert.That(async () => await Ts.StartExecutionAsync(TempSeqFile, "NoSuchSequence"),
            Throws.Exception,
            "Starting an entry point that does not exist must raise an error, not silently no-op");
    }

    [Test]
    public void GetExecutionStatus_UnknownId_ThrowsKeyNotFound()
    {
        Assert.That(async () => await Ts.GetExecutionStatusAsync("does-not-exist-1234"),
            Throws.TypeOf<System.Collections.Generic.KeyNotFoundException>());
    }
}
