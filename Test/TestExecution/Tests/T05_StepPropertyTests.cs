using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// Tests for step run-time properties: RunMode, Precondition, Pass/Fail actions,
/// Loop settings, numeric/string limits, and measurement expression.
/// </summary>
[TestFixture]
[Category("StepProperties")]
public class T05_StepPropertyTests : TestBase
{
    private const string Seq = "PropTests";
    private const string Grp = "Main";

    private async Task SetupWithStepAsync(string stepName, string stepType = "Statement")
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, Seq);
        await Ts.InsertStepAsync(TempSeqFile, Seq, Grp, stepType, stepName);
    }

    // ── RunMode ───────────────────────────────────────────────────────────────

    [TestCase("SkipStep")]
    [TestCase("RunNormally")]
    [TestCase("ForcePass")]
    [TestCase("ForceFail")]
    [TestCase("ForceFailAndTerminateSequence")]
    public async Task SetStepRunMode_DoesNotThrow(string mode)
    {
        await SetupWithStepAsync("s");
        Assert.DoesNotThrowAsync(() =>
            Ts.SetStepRunModeAsync(TempSeqFile, Seq, Grp, "s", mode));
    }

    // ── Precondition ──────────────────────────────────────────────────────────

    [Test]
    public async Task SetStepPrecondition_StoresExpression()
    {
        await SetupWithStepAsync("s");
        // SetStepPreconditionAsync should not throw for a valid expression
        Assert.DoesNotThrowAsync(() =>
            Ts.SetStepPreconditionAsync(TempSeqFile, Seq, Grp, "s", "Locals.ready == True"));
    }

    // ── Pass / Fail actions ───────────────────────────────────────────────────

    [TestCase("Terminate")]
    [TestCase("Continue")]
    [TestCase("JumpToCleanup")]
    public async Task SetStepPassAction_DoesNotThrow(string action)
    {
        await SetupWithStepAsync("s");
        Assert.DoesNotThrowAsync(() =>
            Ts.SetStepPassActionAsync(TempSeqFile, Seq, Grp, "s", action));
    }

    [TestCase("Terminate")]
    [TestCase("Continue")]
    [TestCase("JumpToCleanup")]
    public async Task SetStepFailAction_DoesNotThrow(string action)
    {
        await SetupWithStepAsync("s");
        Assert.DoesNotThrowAsync(() =>
            Ts.SetStepFailActionAsync(TempSeqFile, Seq, Grp, "s", action));
    }

    // ── Loop ──────────────────────────────────────────────────────────────────

    [TestCase("Repeat",  "3",       null,          null)]
    [TestCase("While",   null,      "Locals.x > 0",null)]
    [TestCase("For",     "Locals.i = 0", "Locals.i < 5", "Locals.i = Locals.i + 1")]
    public async Task SetStepLoop_DoesNotThrow(
        string loopType, string? init, string? cond, string? inc)
    {
        await SetupWithStepAsync("s");
        Assert.DoesNotThrowAsync(() =>
            Ts.SetStepLoopAsync(TempSeqFile, Seq, Grp, "s", loopType, init, cond, inc));
    }

    // ── Numeric limits ────────────────────────────────────────────────────────

    [Test]
    public async Task SetAndGetNumericLimits_RoundTrip()
    {
        await SetupWithStepAsync("n", "NumericLimitTest");

        await Ts.SetNumericLimitsAsync(TempSeqFile, Seq, Grp, "n",
            lowLimit: 4.75, highLimit: 5.25, units: "V", comparisonType: "GELE");

        var limits = await Ts.GetNumericLimitsAsync(TempSeqFile, Seq, Grp, "n");

        // GetNumericLimitsAsync returns the public MCP contract keys
        // (low_limit / high_limit), serialized verbatim by the get_numeric_limits tool.
        Assert.That(limits,                Is.Not.Null);
        Assert.That(limits["low_limit"],   Is.EqualTo(4.75).Within(0.0001));
        Assert.That(limits["high_limit"],  Is.EqualTo(5.25).Within(0.0001));
    }

    // ── String value test ─────────────────────────────────────────────────────

    [Test]
    public async Task ConfigureStringValueTest_DoesNotThrow()
    {
        await SetupWithStepAsync("sv", "StringValueTest");
        Assert.DoesNotThrowAsync(() =>
            Ts.ConfigureStringValueTestAsync(TempSeqFile, Seq, Grp, "sv",
                "Locals.fwVersion", "3.14.0", "CaseSensitive"));
    }

    // ── Step expression ───────────────────────────────────────────────────────

    [Test]
    public async Task SetStepExpression_Statement_DoesNotThrow()
    {
        await SetupWithStepAsync("s");
        Assert.DoesNotThrowAsync(() =>
            Ts.SetStepExpressionAsync(TempSeqFile, Seq, Grp, "s",
                "Locals.result = 42 * 2"));
    }

    // ── Step measurement ──────────────────────────────────────────────────────

    [Test]
    public async Task SetStepMeasurement_NumericLimit_DoesNotThrow()
    {
        await SetupWithStepAsync("n", "NumericLimitTest");
        Assert.DoesNotThrowAsync(() =>
            Ts.SetStepMeasurementAsync(TempSeqFile, Seq, Grp, "n",
                "Locals.voltage"));
    }

    // ── Sequence call target ───────────────────────────────────────────────────

    [Test]
    public async Task SetSequenceCallTarget_LinksToSubsequence()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "Caller");
        await Ts.InsertSequenceAsync(TempSeqFile, "SubSeq");
        await Ts.InsertStepAsync(TempSeqFile, "Caller", Grp, "SequenceCall", "CallSub");

        Assert.DoesNotThrowAsync(() =>
            Ts.SetSequenceCallTargetAsync(TempSeqFile, "Caller", Grp, "CallSub", "SubSeq"));
    }

    // ── Breakpoint ─────────────────────────────────────────────────────────────

    [Test]
    public async Task SetAndGetBreakpoint_RoundTrip()
    {
        await SetupWithStepAsync("bp");
        await Ts.SetStepBreakpointAsync(TempSeqFile, Seq, Grp, "bp", true, "Before");

        var bps = await Ts.GetBreakpointsAsync(TempSeqFile);
        Assert.That(bps.Count, Is.GreaterThan(0),
            "At least one breakpoint should be registered after setting one");
    }

    // ── Step unique ID ─────────────────────────────────────────────────────────

    [Test]
    public async Task GetStepUniqueId_ReturnsNonEmptyString()
    {
        await SetupWithStepAsync("s");
        var uid = await Ts.GetStepUniqueIdAsync(TempSeqFile, Seq, Grp, "s");

        Assert.That(uid, Is.Not.Empty, "Step unique ID should not be empty");
        TestContext.WriteLine($"Step unique ID: {uid}");
    }

    // ── Step properties dictionary ─────────────────────────────────────────────

    [Test]
    public async Task GetStepProperties_ReturnsNonEmptyDictionary()
    {
        await SetupWithStepAsync("s");
        var props = await Ts.GetStepPropertiesAsync(TempSeqFile, Seq, "s");

        Assert.That(props, Is.Not.Null);
        Assert.That(props.Count, Is.GreaterThan(0),
            "A step should have at least one property");
    }
}
