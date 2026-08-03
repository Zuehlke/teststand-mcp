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

    /// <summary>
    /// Asserts the step's own property tree, not just the tool's report — the weakness that let the
    /// .NET module bug survive. Audited 2026-08-03: the CommonCModule properties really do land in
    /// TS.SData.Call.LibPath / .Func.
    /// </summary>
    [Test]
    public async Task ConfigureDllModule_AppliesPathAndFunction()
    {
        await PrepareStepAsync("DllStep");

        var result = await Ts.ConfigureDllModuleAsync(TempSeqFile, Seq, Grp, "DllStep",
            @"C:\Dummy\mylib.dll", "MyEntryPoint", save: true);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.AppliedSettings, Does.ContainKey("dllPath"),
            "DLL path should have been applied to the C/CVI module");
        Assert.That(await ReadSDataLeafAsync("DllStep", "TS.SData.Call", "LibPath"),
            Is.EqualTo(@"C:\Dummy\mylib.dll"), "the DLL path must reach the step, not just the report");
        Assert.That(await ReadSDataLeafAsync("DllStep", "TS.SData.Call", "Func"),
            Is.EqualTo("MyEntryPoint"));
        TestContext.WriteLine($"Adapter: {result.Adapter}; applied: {result.AppliedSettings.Count}");
    }

    /// <summary>Same tree-level assertion for the LabVIEW adapter: VIPath lands in
    /// TS.SData.ViCall.VIPath. load_prototype is off so the test needs no LabVIEW.</summary>
    [Test]
    public async Task ConfigureLabViewModule_WritesViPathToTheStep()
    {
        await PrepareStepAsync("LvStep");

        var result = await Ts.ConfigureLabViewModuleAsync(TempSeqFile, Seq, Grp, "LvStep",
            @"C:\Dummy\MyVi.vi", save: true, loadPrototype: false);

        Assert.That(result.AppliedSettings, Does.ContainKey("viPath"));
        Assert.That(await ReadSDataLeafAsync("LvStep", "TS.SData.ViCall", "VIPath"),
            Is.EqualTo(@"C:\Dummy\MyVi.vi"));
    }

    /// <summary>
    /// The object-oriented Python settings were previously unexercised — the old test only reached
    /// module_path + function_name. These all live in the STEP's tree (TS.SData.PythonCall.*), so a
    /// wrong leaf name would throw rather than configure. Note the leaf is ClassInstanceLocation even
    /// though the typed interface calls the property ClassInstanceLocationExpr.
    /// </summary>
    [Test]
    public async Task ConfigurePythonModule_WritesObjectOrientedSettingsToTheStep()
    {
        await PrepareStepAsync("PyStep");

        var result = await Ts.ConfigurePythonModuleAsync(TempSeqFile, Seq, Grp, "PyStep",
            @"C:\Dummy\mymod.py", "do_thing", save: true, loadPrototype: false,
            className: "MyClass", classInstanceLocation: "FileGlobals.inst",
            operationType: 1, operationScope: 2, pythonVersion: "3.11");

        Assert.Multiple(() =>
        {
            Assert.That(result.AppliedSettings, Does.ContainKey("className"));
            Assert.That(result.AppliedSettings, Does.ContainKey("operationType"));
            Assert.That(result.AppliedSettings, Does.ContainKey("pythonVersion"));
        });

        const string pc = "TS.SData.PythonCall";
        Assert.That(await ReadSDataLeafAsync("PyStep", pc, "ModulePath"), Is.EqualTo(@"C:\Dummy\mymod.py"));
        Assert.That(await ReadSDataLeafAsync("PyStep", pc, "FunctionOrAttributeName"), Is.EqualTo("do_thing"));
        Assert.That(await ReadSDataLeafAsync("PyStep", pc, "ClassName"), Is.EqualTo("MyClass"));
        Assert.That(await ReadSDataLeafAsync("PyStep", pc, "ClassInstanceLocation"), Is.EqualTo("FileGlobals.inst"));
        Assert.That(await ReadSDataLeafAsync("PyStep", pc, "OperationType"), Is.EqualTo("1"));
        Assert.That(await ReadSDataLeafAsync("PyStep", pc, "OperationScope"), Is.EqualTo("2"));
        Assert.That(await ReadSDataLeafAsync("PyStep", pc, "PythonVersion"), Is.EqualTo("3.11"));
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
        // Tree-level: the engine reads the callee off SeqName / UseCurFile, not off the report.
        Assert.That(await ReadSDataLeafAsync("CallStep", "TS.SData", "SeqName"), Is.EqualTo("TargetSeq"));
        Assert.That(await ReadSDataLeafAsync("CallStep", "TS.SData", "UseCurFile"), Is.EqualTo("True"));
    }

    /// <summary>Reads one leaf under the step's TS.SData subtree, so the assertions below check the
    /// property tree the ENGINE will execute rather than only the tool's own report.</summary>
    private async Task<string?> ReadSDataLeafAsync(string stepName, string lookup, string leafName)
    {
        var node = await Ts.GetPropertyTreeAsync("SequenceFile", TempSeqFile, lookup,
            maxDepth: 2, includeHidden: true, maxArrayElements: 10,
            sequenceName: Seq, stepGroup: Grp, stepName: stepName);
        var leaf = node.Children?.Find(c => c.Name == leafName);
        return leaf?.Value?.ToString();
    }

    /// <summary>
    /// Regression guard for the silent-write bug: <c>configure_dotnet_module</c> used to report
    /// assemblyPath and methodName as applied while writing NEITHER, because SetAssembly was called
    /// with swapped arguments and the member name went to <c>NameOfMethodToCreate</c> (code
    /// generation) instead of <c>MemberName</c>. The step then executed as a no-op that still
    /// reported Passed. Asserting the step's own property tree — not just AppliedSettings — is the
    /// point: the predecessor test only checked DoesNotThrow + a non-empty dictionary, which is
    /// exactly why the bug survived.
    /// </summary>
    [Test]
    public async Task ConfigureDotNetModule_WritesAssemblyClassAndMemberToTheStep()
    {
        await PrepareStepAsync("DotNetStep");
        const string asm = @"C:\Dummy\MyAssembly.dll";

        var result = await Ts.ConfigureDotNetModuleAsync(TempSeqFile, Seq, Grp, "DotNetStep",
            asm, "MyNamespace.MyClass", "MyMethod", save: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.AppliedSettings, Does.ContainKey("assemblyPath"));
            Assert.That(result.AppliedSettings, Does.ContainKey("className"));
            Assert.That(result.AppliedSettings, Does.ContainKey("methodName"));
            Assert.That(result.AppliedSettings, Does.ContainKey("memberType"));
        });

        // The engine reads the assembly off TS.SData and the member off the call entry — the root
        // TS.SData.FunctionName stays empty even for a correctly configured step, so asserting that
        // one would be wrong.
        Assert.That(await ReadSDataLeafAsync("DotNetStep", "TS.SData", "AssemblyPath"),
            Is.EqualTo(asm), "the assembly path must reach the step, not just the result report");
        Assert.That(await ReadSDataLeafAsync("DotNetStep", "TS.SData.Calls[0]", "MemberName"),
            Is.EqualTo("MyMethod"), "the member to invoke lives in TS.SData.Calls[0].MemberName");
        Assert.That(await ReadSDataLeafAsync("DotNetStep", "TS.SData.Calls[0]", "ClassName"),
            Is.EqualTo("MyNamespace.MyClass"));
        Assert.That(await ReadSDataLeafAsync("DotNetStep", "TS.SData.Calls[0]", "MemberType"),
            Is.EqualTo("1"), "1 = DotNetMember_CallMethod; 0 (DoNotCall) executes as a silent no-op");

        TestContext.WriteLine($".NET applied: {string.Join(",", result.AppliedSettings.Keys)}");
        TestContext.WriteLine($"note: {result.Note}");
    }

    /// <summary>
    /// The other half of the honesty contract: an assembly that cannot be loaded must NOT be reported
    /// as a resolved member, and the reason must reach the caller via Note. C:\Dummy\MyAssembly.dll
    /// does not exist, so resolution has to fail on every machine.
    /// </summary>
    [Test]
    public async Task ConfigureDotNetModule_UnresolvableAssembly_IsReportedNotFakedAsSuccess()
    {
        await PrepareStepAsync("DotNetStep");

        var result = await Ts.ConfigureDotNetModuleAsync(TempSeqFile, Seq, Grp, "DotNetStep",
            @"C:\Dummy\MyAssembly.dll", "MyNamespace.MyClass", "MyMethod", save: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.AppliedSettings, Does.Not.ContainKey("memberResolved"),
                "a member that cannot be resolved must never be reported as resolved");
            Assert.That(result.Note, Is.Not.Null.And.Contains("could not be resolved"),
                "the caller has to learn WHY the step will not call anything");
        });
        TestContext.WriteLine($"note: {result.Note}");
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
