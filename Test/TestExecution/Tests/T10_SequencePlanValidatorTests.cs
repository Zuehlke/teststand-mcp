using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TestStandMCP.Tools;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// Pure (engine-free) unit tests for the Phase-3 build-plan validation gate.
/// Does NOT inherit TestBase, so no TestStand engine is connected.
/// </summary>
[TestFixture]
[Category("PlanValidator")]
public class T10_SequencePlanValidatorTests
{
    private static PlanStepInput S(string name, string type, string? expr = null,
        string? target = null) =>
        new() { Name = name, StepType = type, Expression = expr, TargetSequenceName = target };

    private static PlanValidationResult Validate(IEnumerable<PlanStepInput> steps,
        params string[] locals) =>
        SequencePlanValidator.Validate("Test", steps.ToList(), locals);

    // ── The real flowchart sequence we just built must validate clean ─────────
    [Test]
    public void RealFlowchartPlan_IsValid_WithExpectedStats()
    {
        var steps = new List<PlanStepInput>
        {
            S("Prepare Test Plan", "SequenceCall"),
            S("Create Test Environment", "SequenceCall"),
            S("While_Outer", "NI_Flow_While", "True"),
            S("Set Stress Level", "SequenceCall"),
            S("Create New Scenario", "SequenceCall"),
            S("Set Performance Parameters", "SequenceCall"),
            S("While_Inner", "NI_Flow_While", "True"),
            S("Execute the Test", "SequenceCall"),
            S("If_WantMoreTest", "NI_Flow_If", "Locals.WantMoreTest == True"),
            S("Break_MoreTest", "NI_Flow_Break"),
            S("End_If_WantMoreTest", "NI_Flow_End"),
            S("Analyse Test Result", "SequenceCall"),
            S("If_NotSatisfied", "NI_Flow_If", "Locals.Satisfied == False"),
            S("Modify Application", "SequenceCall"),
            S("Continue_Inner", "NI_Flow_Continue"),
            S("End_If_NotSatisfied", "NI_Flow_End"),
            S("Break_Satisfied", "NI_Flow_Break"),
            S("End_While_Inner", "NI_Flow_End"),
            S("If_MoreTest_Outer", "NI_Flow_If", "Locals.WantMoreTest == True"),
            S("Continue_Outer", "NI_Flow_Continue"),
            S("End_If_MoreTest_Outer", "NI_Flow_End"),
            S("If_SystemExhausted", "NI_Flow_If", "Locals.SystemExhausted == True"),
            S("Break_Outer", "NI_Flow_Break"),
            S("End_If_SystemExhausted", "NI_Flow_End"),
            S("End_While_Outer", "NI_Flow_End"),
            S("Exit", "Statement"),
        };

        var r = Validate(steps, "WantMoreTest", "Satisfied", "SystemExhausted");

        Assert.That(r.Valid, Is.True, "Plan should be valid: " +
            string.Join("; ", r.Errors.Select(e => e.Code + ":" + e.Message)));
        Assert.That(r.ErrorCount, Is.EqualTo(0));
        Assert.That(r.Stats.StepCount, Is.EqualTo(26));
        Assert.That(r.Stats.FlowSteps, Is.EqualTo(17));
        Assert.That(r.Stats.MaxNestingDepth, Is.EqualTo(3));
        Assert.That(r.Stats.UnlinkedSequenceCalls, Is.EqualTo(8));
        // 8 unlinked SequenceCalls → exactly one W_UNLINKED_CALLS warning, no unused-local warnings
        Assert.That(r.Warnings.Any(w => w.Code == "W_UNLINKED_CALLS"), Is.True);
        Assert.That(r.Warnings.Any(w => w.Code == "W_UNUSED_LOCAL"), Is.False);
    }

    // ── Each error rule fires ─────────────────────────────────────────────────
    [Test]
    public void UnclosedBlock_IsError()
    {
        var r = Validate(new[] { S("W", "NI_Flow_While", "True"), S("A", "SequenceCall") });
        Assert.That(r.Valid, Is.False);
        Assert.That(r.Errors.Any(e => e.Code == "E_UNCLOSED_BLOCK"), Is.True);
    }

    [Test]
    public void UnmatchedEnd_IsError()
    {
        var r = Validate(new[] { S("A", "SequenceCall"), S("E", "NI_Flow_End") });
        Assert.That(r.Errors.Any(e => e.Code == "E_UNMATCHED_END"), Is.True);
    }

    [Test]
    public void ElseWithoutIf_IsError()
    {
        var r = Validate(new[]
        {
            S("W", "NI_Flow_While", "True"), S("Else", "NI_Flow_Else"), S("E", "NI_Flow_End")
        });
        Assert.That(r.Errors.Any(e => e.Code == "E_ELSE_WITHOUT_IF"), Is.True);
    }

    [Test]
    public void BreakOutsideLoop_IsError()
    {
        var r = Validate(new[]
        {
            S("If", "NI_Flow_If", "True"), S("B", "NI_Flow_Continue"), S("E", "NI_Flow_End")
        });
        Assert.That(r.Errors.Any(e => e.Code == "E_JUMP_OUTSIDE_LOOP"), Is.True);
    }

    [Test]
    public void ForbiddenGoto_IsError()
    {
        var r = Validate(new[] { S("G", "Goto") });
        Assert.That(r.Errors.Any(e => e.Code == "E_FORBIDDEN_TYPE"), Is.True);
    }

    [Test]
    public void UndeclaredLocal_IsError()
    {
        var r = Validate(new[]
        {
            S("If", "NI_Flow_If", "Locals.Missing == True"), S("E", "NI_Flow_End")
        });
        Assert.That(r.Errors.Any(e => e.Code == "E_UNDECLARED_LOCAL"), Is.True);
    }

    [Test]
    public void DuplicateName_IsError()
    {
        var r = Validate(new[] { S("A", "SequenceCall"), S("A", "Statement") });
        Assert.That(r.Errors.Any(e => e.Code == "E_DUP_NAME"), Is.True);
    }

    [Test]
    public void UnusedLocal_IsWarningNotError()
    {
        var r = Validate(new[] { S("A", "SequenceCall") }, "NeverUsed");
        Assert.That(r.Valid, Is.True);
        Assert.That(r.Warnings.Any(w => w.Code == "W_UNUSED_LOCAL"), Is.True);
    }

    // ── Sweep/Stream loops are real loops: they open a block (End matches) and ──
    // ── Break/Continue inside them is valid (not E_JUMP_OUTSIDE_LOOP). ──────────
    [Test]
    public void SweepLoop_IsLoop_BreakInsideIsValid()
    {
        var r = Validate(new[]
        {
            S("Sweep", "NI_Flow_SweepLoop"),
            S("Brk",   "NI_Flow_Break"),
            S("End",   "NI_Flow_End"),
        });
        Assert.That(r.Valid, Is.True, "SweepLoop should be a valid loop block: " +
            string.Join("; ", r.Errors.Select(e => e.Code + ":" + e.Message)));
    }

    [Test]
    public void StreamLoop_IsLoop_ContinueInsideIsValid()
    {
        var r = Validate(new[]
        {
            S("Stream", "NI_Flow_StreamLoop"),
            S("Cont",   "NI_Flow_Continue"),
            S("End",    "NI_Flow_End"),
        });
        Assert.That(r.Valid, Is.True, "StreamLoop should be a valid loop block: " +
            string.Join("; ", r.Errors.Select(e => e.Code + ":" + e.Message)));
    }

    [Test]
    public void SweepLoop_Unclosed_IsError()
    {
        var r = Validate(new[] { S("Sweep", "NI_Flow_SweepLoop"), S("A", "SequenceCall") });
        Assert.That(r.Valid, Is.False);
        Assert.That(r.Errors.Any(e => e.Code == "E_UNCLOSED_BLOCK"), Is.True,
            "An unclosed SweepLoop must report E_UNCLOSED_BLOCK");
    }
}
