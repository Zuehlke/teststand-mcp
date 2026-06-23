using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

[TestFixture]
[Category("AdapterConfig")]
public class T13_AdapterConfigTests : TestBase
{
    private const string Seq = "ModuleSeq";
    private const string Grp = "Main";

    private async Task PrepareStepAsync(string stepName, string stepType = "Statement")
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, Seq);
        await Ts.InsertStepAsync(TempSeqFile, Seq, Grp, stepType, stepName);
    }

    [Test]
    public async Task ConfigureDllModule_AppliesPathAndFunction()
    {
        await PrepareStepAsync("DllStep");

        var result = await Ts.ConfigureDllModuleAsync(TempSeqFile, Seq, Grp, "DllStep",
            @"C:\Dummy\mylib.dll", "MyEntryPoint", save: true);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.AppliedSettings, Does.ContainKey("dllPath"),
            "DLL path should have been applied to the C/CVI module");
        TestContext.WriteLine($"Adapter: {result.Adapter}; applied: {result.AppliedSettings.Count}");
    }

    [Test]
    public async Task ConfigureSequenceCallModule_SetsTarget()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, Seq);
        await Ts.InsertSequenceAsync(TempSeqFile, "TargetSeq");
        await Ts.InsertStepAsync(TempSeqFile, Seq, Grp, "SequenceCall", "CallStep");

        var result = await Ts.ConfigureSequenceCallModuleAsync(TempSeqFile, Seq, Grp,
            "CallStep", "TargetSeq", targetSequenceFile: "", save: true);

        Assert.That(result.AppliedSettings, Does.ContainKey("targetSequenceName"));
        Assert.That(result.AppliedSettings["targetSequenceName"], Is.EqualTo("TargetSeq"));
    }

    [Test]
    public async Task ConfigureDotNetModule_DoesNotThrow()
    {
        await PrepareStepAsync("DotNetStep");

        Assert.DoesNotThrowAsync(async () =>
        {
            var result = await Ts.ConfigureDotNetModuleAsync(TempSeqFile, Seq, Grp, "DotNetStep",
                @"C:\Dummy\MyAssembly.dll", "MyNamespace.MyClass", "MyMethod", save: true);
            Assert.That(result.AppliedSettings, Is.Not.Empty);
            TestContext.WriteLine($".NET applied: {string.Join(",", result.AppliedSettings.Keys)}");
        });
    }

    [Test]
    public async Task ConfigureLabViewModule_DoesNotThrow()
    {
        await PrepareStepAsync("LvStep");

        Assert.DoesNotThrowAsync(async () =>
        {
            var result = await Ts.ConfigureLabViewModuleAsync(TempSeqFile, Seq, Grp, "LvStep",
                @"C:\Dummy\MyVi.vi", save: true);
            TestContext.WriteLine(
                $"LabVIEW adapter: {result.Adapter}; applied: {result.AppliedSettings.Count}");
        });
    }

    [Test]
    public async Task ConfigurePythonModule_DoesNotThrow()
    {
        await PrepareStepAsync("PyStep");

        Assert.DoesNotThrowAsync(async () =>
        {
            var result = await Ts.ConfigurePythonModuleAsync(TempSeqFile, Seq, Grp, "PyStep",
                @"C:\Dummy\mymodule.py", "my_function", save: true);
            TestContext.WriteLine(
                $"Python adapter: {result.Adapter}; applied: {result.AppliedSettings.Count}");
        });
    }

    // ── Apply each adapter to an Action step and verify it is set ───────────────
    // These tests place a plain Action step, switch its adapter via the friendly
    // name (LabVIEW, .NET, Python, ActiveX, C++/DLL, None) and read the result
    // back with get_step_module_info. No module target is linked — we only assert
    // that the adapter property was applied to the step.
    //
    // The expected KEY NAMES are the ones TestStand actually resolves to on this
    // station (verified live), which can differ from the raw resolve-map value:
    // e.g. "LabVIEW" maps to "G Std Prototype Adapter" but ChangeAdapter
    // normalises it to the loaded "G Flexible VI Adapter".
    [TestCase("LabVIEW", "G Flexible VI Adapter",          "LabVIEW")]
    [TestCase("DotNet",  "DotNet Adapter",                 ".NET")]
    [TestCase(".NET",    "DotNet Adapter",                 ".NET")]
    [TestCase("Python",  "Python Adapter",                 "Python")]
    [TestCase("ActiveX", "Automation Adapter",             "ActiveX/COM")]
    [TestCase("C++/DLL", "DLL Flexible Prototype Adapter", "C/C++ DLL")]
    [TestCase("None",    "None Adapter",                   "<None>")]
    public async Task ChangeStepAdapter_OnActionStep_AppliesAdapter(
        string friendlyAdapter, string expectedKeyName, string expectedDisplayName)
    {
        await PrepareStepAsync("AdapterStep", "Action");

        await Ts.ChangeStepAdapterAsync(TempSeqFile, Seq, Grp, "AdapterStep", friendlyAdapter);

        var info = await Ts.GetStepModuleInfoAsync(TempSeqFile, Seq, Grp, "AdapterStep");

        Assert.Multiple(() =>
        {
            Assert.That(info.AdapterName, Is.EqualTo(expectedKeyName),
                $"Action step should report adapter key '{expectedKeyName}' " +
                $"after change to '{friendlyAdapter}'");
            Assert.That(info.AdapterDisplayName, Is.EqualTo(expectedDisplayName),
                $"Action step should report display name '{expectedDisplayName}' " +
                $"after change to '{friendlyAdapter}'");
        });

        TestContext.WriteLine(
            $"'{friendlyAdapter}' -> key='{info.AdapterName}', display='{info.AdapterDisplayName}'");
    }

    [Test]
    public async Task ChangeStepAdapter_ExactKeyName_AlsoApplies()
    {
        // Passing the exact TestStand key name (not just the friendly alias) must
        // work too, since unknown names pass through the resolve map unchanged.
        await PrepareStepAsync("ExactStep", "Action");

        await Ts.ChangeStepAdapterAsync(TempSeqFile, Seq, Grp, "ExactStep",
            "Automation Adapter");

        var info = await Ts.GetStepModuleInfoAsync(TempSeqFile, Seq, Grp, "ExactStep");
        Assert.That(info.AdapterName, Is.EqualTo("Automation Adapter"));
    }
}
