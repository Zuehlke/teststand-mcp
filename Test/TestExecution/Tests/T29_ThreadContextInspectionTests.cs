using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// Live thread-context inspection (runtime debugging): inspect_thread_context,
/// evaluate_in_thread_context, get_runtime_variable, set_runtime_variable and
/// get_runstate_summary. These read/write the LIVE SequenceContext (== ThisContext) and
/// RunState of a running/paused thread at a chosen call-stack frame — the runtime values that
/// get_property_tree / evaluate_expression cannot see (they resolve against engine Globals).
///
/// The sequence used here parks a thread on a MessagePopup inside a called subsequence:
///   MainSequence: Locals.Counter=42, Locals.Message="hi"  →  CallSub → Sub
///   Sub:          Locals.SubLocal=123                      →  Hold (MessagePopup)
/// Headless there is no UI to answer the popup, so the thread sits parked on it with a 2-deep
/// call stack (frame 0 = Sub, frame 1 = MainSequence), giving a stable window to read/write both
/// frames' live variables. See memory teststand-runstate-inaccessible-at-breakpoint.
/// </summary>
[TestFixture]
[Category("Execution")]
public class T29_ThreadContextInspectionTests : TestBase
{
    // ── Fixture helpers ──────────────────────────────────────────────────────────

    private async Task BuildTwoFrameSequenceAsync(string path)
    {
        await Ts.CreateSequenceFileAsync(path);
        await Ts.InsertSequenceAsync(path, "Sub");

        await Ts.InsertLocalVariableAsync(path, "MainSequence", "Counter", "number", "0");
        await Ts.InsertLocalVariableAsync(path, "MainSequence", "Message", "string", null);
        await Ts.InsertLocalVariableAsync(path, "Sub", "SubLocal", "number", "0");

        await Ts.InsertStepAsync(path, "MainSequence", "Main", "Statement", "Prepare");
        await Ts.SetStepExpressionAsync(path, "MainSequence", "Main", "Prepare",
            "Locals.Counter = 42, Locals.Message = \"hi\"");
        await Ts.InsertStepAsync(path, "MainSequence", "Main", "SequenceCall", "CallSub");
        await Ts.SetSequenceCallTargetAsync(path, "MainSequence", "Main", "CallSub", "Sub");

        await Ts.InsertStepAsync(path, "Sub", "Main", "Statement", "SetSub");
        await Ts.SetStepExpressionAsync(path, "Sub", "Main", "SetSub", "Locals.SubLocal = 123");
        await Ts.InsertStepAsync(path, "Sub", "Main", "MessagePopup", "Hold");

        await Ts.SaveSequenceFileAsync(path);
    }

    // Builds the sequence, starts it and waits until the thread is parked on Sub/Hold with a
    // 2-deep call stack. Returns the execution id. Caller must terminate it.
    private async Task<string> BuildAndParkAsync()
    {
        await BuildTwoFrameSequenceAsync(TempSeqFile);
        var info = await Ts.StartExecutionAsync(TempSeqFile, "MainSequence");

        for (int i = 0; i < 50; i++)
        {
            var threads = await Ts.GetExecutionThreadsAsync(info.ExecutionId);
            if (threads.Count > 0
                && threads[0].CurrentStepName     == "Hold"
                && threads[0].CurrentSequenceName == "Sub"
                && threads[0].StackDepth          >= 2)
                return info.ExecutionId;
            await Task.Delay(100);
        }

        try { await Ts.TerminateExecutionAsync(info.ExecutionId); } catch { }
        throw new Exception("Execution did not park on Sub/Hold within the timeout.");
    }

    // ── inspect_thread_context ─────────────────────────────────────────────────

    [Test]
    public async Task InspectThreadContext_RunStateScope_ExposesLiveExecutionCursor()
    {
        var execId = await BuildAndParkAsync();
        try
        {
            var node = await Ts.InspectThreadContextAsync(execId, null, 0, "runstate", null, 2, false, 50);

            Assert.That(node.Name, Is.EqualTo("RunState"));
            Assert.That(node.Children, Is.Not.Null.And.Not.Empty);
            var names = node.Children!.Select(c => c.Name).ToList();
            Assert.That(names, Does.Contain("NextStepIndex"));
            Assert.That(names, Does.Contain("StepIndex"));
            Assert.That(names, Does.Contain("StepGroup"));
            Assert.That(names, Does.Contain("SequenceFailed"));
        }
        finally { await Ts.TerminateExecutionAsync(execId); }
    }

    [Test]
    public async Task InspectThreadContext_LocalsScope_ReturnsRuntimeValue()
    {
        var execId = await BuildAndParkAsync();
        try
        {
            // Frame 0 = Sub → Locals.SubLocal was set to 123 by the SetSub step at runtime.
            var node = await Ts.InspectThreadContextAsync(execId, null, 0, "locals", null, 2, false, 50);

            Assert.That(node.Name, Is.EqualTo("Locals"));
            var subLocal = node.Children!.FirstOrDefault(c => c.Name == "SubLocal");
            Assert.That(subLocal, Is.Not.Null, "Locals.SubLocal should be present on the Sub frame");
            Assert.That(Convert.ToDouble(subLocal!.Value), Is.EqualTo(123).Within(1e-9),
                "The RUNTIME value must be 123 (not the static file default 0)");
        }
        finally { await Ts.TerminateExecutionAsync(execId); }
    }

    [Test]
    public async Task InspectThreadContext_CallerFrame_SeesCallerLocals()
    {
        var execId = await BuildAndParkAsync();
        try
        {
            // Frame 1 = the MainSequence caller → its Locals set by the Prepare step.
            var node = await Ts.InspectThreadContextAsync(execId, null, 1, "locals", null, 2, false, 50);

            var counter = node.Children!.FirstOrDefault(c => c.Name == "Counter");
            var message = node.Children!.FirstOrDefault(c => c.Name == "Message");
            Assert.That(counter, Is.Not.Null);
            Assert.That(message, Is.Not.Null);
            Assert.That(Convert.ToDouble(counter!.Value), Is.EqualTo(42).Within(1e-9));
            Assert.That((string)message!.Value!, Is.EqualTo("hi"));
        }
        finally { await Ts.TerminateExecutionAsync(execId); }
    }

    // ── evaluate_in_thread_context ─────────────────────────────────────────────

    [Test]
    public async Task EvaluateInThreadContext_ResolvesLocalsRunStateAndComputes()
    {
        var execId = await BuildAndParkAsync();
        try
        {
            var raw = await Ts.EvaluateInThreadContextAsync(execId, null, 0, "Locals.SubLocal");
            Assert.That(raw.IsValid, Is.True, raw.ErrorMessage);
            Assert.That(Convert.ToDouble(raw.Value), Is.EqualTo(123).Within(1e-9));

            // A computed expression in the live scope.
            var computed = await Ts.EvaluateInThreadContextAsync(execId, null, 0, "Locals.SubLocal * 2");
            Assert.That(Convert.ToDouble(computed.Value), Is.EqualTo(246).Within(1e-9));

            // RunState resolves in the same scope.
            var group = await Ts.EvaluateInThreadContextAsync(execId, null, 0, "RunState.StepGroup");
            Assert.That((string)group.Value!, Is.EqualTo("Main"));

            // The caller frame sees the caller's Locals.
            var callerCounter = await Ts.EvaluateInThreadContextAsync(execId, null, 1, "Locals.Counter");
            Assert.That(Convert.ToDouble(callerCounter.Value), Is.EqualTo(42).Within(1e-9));
        }
        finally { await Ts.TerminateExecutionAsync(execId); }
    }

    [Test]
    public async Task EvaluateInThreadContext_InvalidExpression_ReportsError()
    {
        var execId = await BuildAndParkAsync();
        try
        {
            var r = await Ts.EvaluateInThreadContextAsync(execId, null, 0, "Locals.DoesNotExist + 1");
            Assert.That(r.IsValid, Is.False);
            Assert.That(r.ErrorMessage, Is.Not.Null.And.Not.Empty);
        }
        finally { await Ts.TerminateExecutionAsync(execId); }
    }

    // ── get_runtime_variable ───────────────────────────────────────────────────

    [Test]
    public async Task GetRuntimeVariable_ReadsTypedLiveValues()
    {
        var execId = await BuildAndParkAsync();
        try
        {
            var num = await Ts.GetRuntimeVariableAsync(execId, null, 0, "Locals.SubLocal");
            Assert.That(num.ValueType, Is.EqualTo("Number"));
            Assert.That(Convert.ToDouble(num.Value), Is.EqualTo(123).Within(1e-9));
            Assert.That(num.Written, Is.False);

            var str = await Ts.GetRuntimeVariableAsync(execId, null, 1, "Locals.Message");
            Assert.That(str.ValueType, Is.EqualTo("String"));
            Assert.That((string)str.Value!, Is.EqualTo("hi"));
        }
        finally { await Ts.TerminateExecutionAsync(execId); }
    }

    // ── set_runtime_variable ───────────────────────────────────────────────────

    [Test]
    public async Task SetRuntimeVariable_PatchesLocal_AndChangeIsVisibleInContext()
    {
        var execId = await BuildAndParkAsync();
        try
        {
            var set = await Ts.SetRuntimeVariableAsync(execId, null, 0, "Locals.SubLocal", "999", "number");
            Assert.That(set.Written, Is.True);
            Assert.That(Convert.ToDouble(set.Value), Is.EqualTo(999).Within(1e-9));

            // The write must be visible to a subsequent live evaluation (same context).
            var check = await Ts.EvaluateInThreadContextAsync(execId, null, 0, "Locals.SubLocal");
            Assert.That(Convert.ToDouble(check.Value), Is.EqualTo(999).Within(1e-9));
        }
        finally { await Ts.TerminateExecutionAsync(execId); }
    }

    [Test]
    public async Task SetRuntimeVariable_SetNextStepIndex_IsWritable()
    {
        // The "Set Next Step" debugger action: RunState.NextStepIndex is writable at runtime.
        var execId = await BuildAndParkAsync();
        try
        {
            var set = await Ts.SetRuntimeVariableAsync(execId, null, 0, "RunState.NextStepIndex", "0", "number");
            Assert.That(set.Written, Is.True);
            Assert.That(set.ValueType, Is.EqualTo("Number"));
            Assert.That(Convert.ToDouble(set.Value), Is.EqualTo(0).Within(1e-9),
                "RunState.NextStepIndex should read back as the value we set");
        }
        finally { await Ts.TerminateExecutionAsync(execId); }
    }

    [Test]
    public async Task SetRuntimeVariable_AutoDetectsType_WhenValueTypeOmitted()
    {
        var execId = await BuildAndParkAsync();
        try
        {
            // No value_type → auto-detected as a number from the literal.
            var set = await Ts.SetRuntimeVariableAsync(execId, null, 0, "Locals.SubLocal", "77", null);
            Assert.That(set.ValueType, Is.EqualTo("Number"));
            Assert.That(Convert.ToDouble(set.Value), Is.EqualTo(77).Within(1e-9));
        }
        finally { await Ts.TerminateExecutionAsync(execId); }
    }

    // ── get_runstate_summary ───────────────────────────────────────────────────

    [Test]
    public async Task GetRunStateSummary_ReportsPositionAndFlags()
    {
        var execId = await BuildAndParkAsync();
        try
        {
            var s = await Ts.GetRunStateSummaryAsync(execId, null, 0);

            Assert.That(s.CurrentStepName,     Is.EqualTo("Hold"));
            Assert.That(s.CurrentSequenceName, Is.EqualTo("Sub"));
            Assert.That(s.StepGroup,           Is.EqualTo("Main"));
            Assert.That(s.CurrentFilePath, Does.Contain(Path.GetFileName(TempSeqFile)));
            Assert.That(s.SequenceFailed, Is.False);
            Assert.That(s.ErrorOccurred,  Is.False);
            Assert.That(s.ErrorCode,      Is.EqualTo(0));
        }
        finally { await Ts.TerminateExecutionAsync(execId); }
    }

    // ── Negative contracts ─────────────────────────────────────────────────────

    [Test]
    public void InspectThreadContext_UnknownExecution_ThrowsKeyNotFound()
    {
        Assert.That(
            async () => await Ts.InspectThreadContextAsync("does-not-exist-9999", null, 0,
                "runstate", null, 2, false, 50),
            Throws.TypeOf<System.Collections.Generic.KeyNotFoundException>());
    }

    [Test]
    public async Task InspectThreadContext_FrameIndexOutOfRange_Throws()
    {
        var execId = await BuildAndParkAsync();
        try
        {
            Assert.That(
                async () => await Ts.InspectThreadContextAsync(execId, null, 99,
                    "runstate", null, 2, false, 50),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }
        finally { await Ts.TerminateExecutionAsync(execId); }
    }
}
