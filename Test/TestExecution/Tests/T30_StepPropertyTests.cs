using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// Generic step-property writer (set_step_property): sets ANY property on a step by a dotted path
/// relative to the step's PropertyObject — the scope no other writer reaches (set_property_value/
/// set_property only see Globals/Locals; configure_*_module only the adapter module).
///
/// The motivating case is a None-adapter utility step whose configuration lives in its own
/// properties: NI_LV_RunVIAsynchronously ("Run VI Asynchronously") stores the VI in
/// VIModule.ViCall.VIPath and the target in RemoteHost/PortNumber/Timeout. configure_labview_module
/// cannot fill it (it switches the adapter and writes the wrong module); set_step_property can, and
/// crucially LEAVES THE ADAPTER UNCHANGED. See memory teststand-lv-utility-steps-none-adapter-vimodule.
/// </summary>
[TestFixture]
[Category("StepConfig")]
public class T30_StepPropertyTests : TestBase
{
    private async Task InsertRunVIAsyncAsync(string path, string stepName = "RunAsync")
    {
        await Ts.CreateSequenceFileAsync(path);
        await Ts.InsertStepAsync(path, "MainSequence", "Main", "NI_LV_RunVIAsynchronously", stepName);
        await Ts.SaveSequenceFileAsync(path);
    }

    // ── The VI path (the core "fill out" field) ────────────────────────────────

    [Test]
    public async Task SetStepProperty_SetsVIPath_OnNoneAdapterUtilityStep()
    {
        await InsertRunVIAsyncAsync(TempSeqFile);

        var r = await Ts.SetStepPropertyAsync(TempSeqFile, "MainSequence", "Main", "RunAsync",
            "VIModule.ViCall.VIPath", @"C:\VIs\MyAsyncWorker.vi", null);

        Assert.That(r.ValueType, Is.EqualTo("String"));
        Assert.That((string)r.Value!, Is.EqualTo(@"C:\VIs\MyAsyncWorker.vi"));
    }

    [Test]
    public async Task SetStepProperty_DoesNotChangeTheStepAdapter()
    {
        // The whole point vs configure_labview_module: that tool switched the adapter None→LabVIEW
        // and wrote the wrong module. set_step_property must leave the adapter as <None>.
        await InsertRunVIAsyncAsync(TempSeqFile);

        var before = await Ts.GetStepModuleInfoAsync(TempSeqFile, "MainSequence", "Main", "RunAsync");
        Assert.That(before.AdapterDisplayName, Is.EqualTo("<None>"),
            "Precondition: a fresh NI_LV_RunVIAsynchronously step uses the None adapter");

        await Ts.SetStepPropertyAsync(TempSeqFile, "MainSequence", "Main", "RunAsync",
            "VIModule.ViCall.VIPath", @"C:\VIs\MyAsyncWorker.vi", null);

        var after = await Ts.GetStepModuleInfoAsync(TempSeqFile, "MainSequence", "Main", "RunAsync");
        Assert.That(after.AdapterDisplayName, Is.EqualTo("<None>"),
            "set_step_property must NOT change the step's adapter (regression vs configure_labview_module)");
    }

    // ── Numeric / boolean / expression fields ──────────────────────────────────

    [Test]
    public async Task SetStepProperty_SetsNumericAndBooleanFields()
    {
        await InsertRunVIAsyncAsync(TempSeqFile);

        var port = await Ts.SetStepPropertyAsync(TempSeqFile, "MainSequence", "Main", "RunAsync",
            "PortNumber", "3364", "number");
        Assert.That(port.ValueType, Is.EqualTo("Number"));
        Assert.That(Convert.ToDouble(port.Value), Is.EqualTo(3364).Within(1e-9));

        var timeout = await Ts.SetStepPropertyAsync(TempSeqFile, "MainSequence", "Main", "RunAsync",
            "Timeout", "5000", "number");
        Assert.That(Convert.ToDouble(timeout.Value), Is.EqualTo(5000).Within(1e-9));

        var showFp = await Ts.SetStepPropertyAsync(TempSeqFile, "MainSequence", "Main", "RunAsync",
            "VIModule.ViCall.ShowFrnPnl", "true", "boolean");
        Assert.That(showFp.ValueType, Is.EqualTo("Boolean"));
        Assert.That((bool)showFp.Value!, Is.True);
    }

    [Test]
    public async Task SetStepProperty_AutoDetectsType_WhenValueTypeOmitted()
    {
        await InsertRunVIAsyncAsync(TempSeqFile);

        var port = await Ts.SetStepPropertyAsync(TempSeqFile, "MainSequence", "Main", "RunAsync",
            "PortNumber", "7777", null);
        Assert.That(port.ValueType, Is.EqualTo("Number"));
        Assert.That(Convert.ToDouble(port.Value), Is.EqualTo(7777).Within(1e-9));
    }

    [Test]
    public async Task SetStepProperty_SetsExpressionTypedProperty()
    {
        await InsertRunVIAsyncAsync(TempSeqFile);

        // RemoteHost is an Expression (String) — the stored text is an expression, so a literal
        // host is a quoted string literal.
        var host = await Ts.SetStepPropertyAsync(TempSeqFile, "MainSequence", "Main", "RunAsync",
            "RemoteHost", "\"192.168.0.9\"", "string");
        Assert.That(host.ValueType, Is.EqualTo("String"));
        Assert.That((string)host.Value!, Is.EqualTo("\"192.168.0.9\""));
    }

    // ── Persistence ────────────────────────────────────────────────────────────

    [Test]
    public async Task SetStepProperty_PersistsToDisk()
    {
        await InsertRunVIAsyncAsync(TempSeqFile);

        await Ts.SetStepPropertyAsync(TempSeqFile, "MainSequence", "Main", "RunAsync",
            "VIModule.ViCall.VIPath", @"C:\VIs\Persisted.vi", null, save: true);

        // Close so the next read reloads from disk (verify the write→save→reload round-trip).
        await Ts.CloseSequenceFileAsync(TempSeqFile);

        var node = await Ts.GetPropertyTreeAsync("SequenceFile", TempSeqFile,
            "Data.Seq[\"MainSequence\"].Main[\"RunAsync\"].VIModule.ViCall.VIPath", 1, true, 5);
        Assert.That((string)node.Value!, Is.EqualTo(@"C:\VIs\Persisted.vi"));
    }

    // ── Genericity across step types ───────────────────────────────────────────

    [Test]
    public async Task SetStepProperty_WorksOnAnyStepType()
    {
        // Prove it is not special-cased to LabVIEW steps: set a MessagePopup's own properties.
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertStepAsync(TempSeqFile, "MainSequence", "Main", "MessagePopup", "Pop");
        await Ts.SaveSequenceFileAsync(TempSeqFile);

        var modal = await Ts.SetStepPropertyAsync(TempSeqFile, "MainSequence", "Main", "Pop",
            "Modal", "true", "boolean");
        Assert.That((bool)modal.Value!, Is.True);

        var wait = await Ts.SetStepPropertyAsync(TempSeqFile, "MainSequence", "Main", "Pop",
            "TimeToWait", "12", "number");
        Assert.That(Convert.ToDouble(wait.Value), Is.EqualTo(12).Within(1e-9));
    }

    // ── Negative contracts ─────────────────────────────────────────────────────

    [Test]
    public async Task SetStepProperty_UnknownStep_Throws()
    {
        await InsertRunVIAsyncAsync(TempSeqFile);
        Assert.That(
            async () => await Ts.SetStepPropertyAsync(TempSeqFile, "MainSequence", "Main",
                "NoSuchStep", "PortNumber", "1", "number"),
            Throws.Exception);
    }

    [Test]
    public async Task SetStepProperty_UnknownPropertyPath_Throws()
    {
        await InsertRunVIAsyncAsync(TempSeqFile);
        Assert.That(
            async () => await Ts.SetStepPropertyAsync(TempSeqFile, "MainSequence", "Main",
                "RunAsync", "VIModule.ViCall.NoSuchProperty", "1", "number"),
            Throws.Exception);
    }
}
