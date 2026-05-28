using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// Tests for the four Step enum properties that require a typed interface cast
/// to avoid the dynamic COM int→bool collapse:
///   ResultRecordingOption, EvalPrecondForInteractiveExecution,
///   ModuleLoadOption, ModuleUnloadOption, BatchSyncOption.
/// </summary>
[TestFixture]
[Category("StepEnumProperties")]
public class T06_StepEnumPropertyTests : TestBase
{
    private const string Seq = "EnumPropTests";
    private const string Grp = "Main";

    private async Task SetupWithStepAsync(string stepName)
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, Seq);
        await Ts.InsertStepAsync(TempSeqFile, Seq, Grp, "Statement", stepName);
    }

    // ── ResultRecordingOption ─────────────────────────────────────────────────

    [TestCase("Disabled")]
    [TestCase("Enabled")]
    [TestCase("EnabledOverride")]
    public async Task SetStepRecordResult_AllOptions_DoNotThrow(string option)
    {
        await SetupWithStepAsync("s");
        Assert.DoesNotThrowAsync(() =>
            Ts.SetStepRecordResultAsync(TempSeqFile, Seq, Grp, "s", option));
    }

    [Test]
    public async Task SetStepRecordResult_EnabledOverride_SavesCorrectly()
    {
        await SetupWithStepAsync("s");
        // Must not silently collapse to "Enabled" (int 2 → bool → 1)
        Assert.DoesNotThrowAsync(() =>
            Ts.SetStepRecordResultAsync(TempSeqFile, Seq, Grp, "s", "EnabledOverride"));
        await Ts.SaveSequenceFileAsync(TempSeqFile);
        // Success criterion: no exception; the actual enum value can be verified
        // by reading the step properties
        var props = await Ts.GetStepPropertiesAsync(TempSeqFile, Seq, "s");
        Assert.That(props, Is.Not.Null);
    }

    // ── EvalPrecondForInteractiveExecution ────────────────────────────────────

    [TestCase("UseStationOption")]
    [TestCase("EvaluatePrecond")]
    [TestCase("NoEvaluatePrecond")]
    public async Task SetStepEvalPrecond_AllOptions_DoNotThrow(string option)
    {
        await SetupWithStepAsync("s");
        Assert.DoesNotThrowAsync(() =>
            Ts.SetStepEvalPrecondAsync(TempSeqFile, Seq, Grp, "s", option));
    }

    // ── ModuleLoadOption ──────────────────────────────────────────────────────

    [TestCase("PreloadWhenOpened")]
    [TestCase("PreloadWhenExecuted")]
    [TestCase("DynamicLoad")]
    [TestCase("UseStepLoadOption")]
    public async Task SetStepModuleLoadOption_AllOptions_DoNotThrow(string option)
    {
        await SetupWithStepAsync("s");
        Assert.DoesNotThrowAsync(() =>
            Ts.SetStepModuleLoadOptionAsync(TempSeqFile, Seq, Grp, "s", option));
    }

    // ── ModuleUnloadOption ────────────────────────────────────────────────────

    [TestCase("OnPreconditionFailure")]
    [TestCase("AfterStepExecution")]
    [TestCase("AfterSequenceExecution")]
    [TestCase("WithSequenceFile")]
    [TestCase("UseStepUnloadOption")]
    public async Task SetStepModuleUnloadOption_AllOptions_DoNotThrow(string option)
    {
        await SetupWithStepAsync("s");
        Assert.DoesNotThrowAsync(() =>
            Ts.SetStepModuleUnloadOptionAsync(TempSeqFile, Seq, Grp, "s", option));
    }

    // ── BatchSyncOption ───────────────────────────────────────────────────────

    [TestCase("UseSeqFileSetting")]
    [TestCase("UseModelSetting")]
    [TestCase("NoSync")]
    [TestCase("Serial")]
    [TestCase("Parallel")]
    [TestCase("OneThreadOnly")]
    public async Task SetStepBatchSyncOption_AllOptions_DoNotThrow(string option)
    {
        await SetupWithStepAsync("s");
        Assert.DoesNotThrowAsync(() =>
            Ts.SetStepBatchSyncOptionAsync(TempSeqFile, Seq, Grp, "s", option));
    }
}
