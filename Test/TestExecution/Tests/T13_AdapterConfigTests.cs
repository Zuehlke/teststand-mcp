using System;
using System.Linq;
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

    // ── .NET member resolution against a REAL assembly (issue #37) ──────────────
    // Everything above only proves that a nonexistent path fails. These use a fixture assembly
    // built next to the tests, so a resolver that handles nothing but 0-argument/void members —
    // which is what the module-level route actually did — cannot pass.

    /// <summary>The fixture assembly, resolved through the type system so a rename cannot rot the path.</summary>
    private static string FixtureAssembly =>
        typeof(DotNetTestAssembly.MathOps).Assembly.Location;

    private const string MathOps     = "TestStandMCP.DotNetTestAssembly.MathOps";
    private const string InstanceOps = "TestStandMCP.DotNetTestAssembly.InstanceOps";

    private static string NewProbeName() =>
        "MCP_DotNetProbe_" + Guid.NewGuid().ToString("N").Substring(0, 8);

    /// <summary>
    /// THE regression test for issue #37: a member with parameters AND a return value. The
    /// module-level LoadMemberInfo route rejects it with "Prototype does not match that found for
    /// member 'Add'" even though the member exists; only the call-level
    /// DotNetCall.LoadPrototypeFromSignature resolves it — and it must also POPULATE the interface,
    /// which is what makes the step callable at all.
    /// </summary>
    [Test]
    public async Task ConfigureDotNetModule_TwoArgumentNonVoidMethod_Resolves()
    {
        await PrepareStepAsync("AddStep");

        var result = await Ts.ConfigureDotNetModuleAsync(TempSeqFile, Seq, Grp, "AddStep",
            FixtureAssembly, MathOps, "Add", save: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.AppliedSettings, Does.ContainKey("memberResolved"),
                $"a 2-argument non-void member must resolve. note: {result.Note}");
            Assert.That(result.AppliedSettings["resolvedVia"], Is.EqualTo("signature"),
                "it can only come from the call-level prototype load");
            Assert.That(result.AppliedSettings, Does.ContainKey("signature"),
                "the caller has to learn WHICH member was bound");
        });
        Assert.That(result.AppliedSettings["signature"].ToString(), Does.StartWith("Add("));

        // The interface has to be on the step, not just in the report: return value + both arguments.
        Assert.That(await ReadSDataLeafAsync("AddStep", "TS.SData.Calls[0].Params[0]", "Name"),
            Is.EqualTo("Return Value"));
        Assert.That(await ReadSDataLeafAsync("AddStep", "TS.SData.Calls[0].Params[1]", "Name"),
            Is.EqualTo("a"));
        Assert.That(await ReadSDataLeafAsync("AddStep", "TS.SData.Calls[0].Params[2]", "Name"),
            Is.EqualTo("b"));
        TestContext.WriteLine($"resolvedVia={result.AppliedSettings["resolvedVia"]} " +
                              $"signature={result.AppliedSettings["signature"]}");
    }

    /// <summary>The one shape that already worked must keep its old route — the new tier is a
    /// fallback, not a replacement.</summary>
    [Test]
    public async Task ConfigureDotNetModule_ZeroArgVoidMethod_StillResolvesViaMemberInfo()
    {
        await PrepareStepAsync("VoidStep");

        var result = await Ts.ConfigureDotNetModuleAsync(TempSeqFile, Seq, Grp, "VoidStep",
            FixtureAssembly, MathOps, "NoArgsVoid", save: true);

        Assert.That(result.AppliedSettings, Does.ContainKey("memberResolved"), result.Note);
        Assert.That(result.AppliedSettings["resolvedVia"].ToString(), Does.StartWith("member-info"),
            "a bare 0-argument/void member resolves at module level and must not need the fallback");
    }

    /// <summary>
    /// Bug 2 of issue #37: a .NET step keeps its interface in TS.SData.Calls[i].Params, never in the
    /// flat Module.Parameters container the reader used to look at — so get_module_parameters
    /// returned [] for every .NET step, however well configured.
    /// </summary>
    [Test]
    public async Task GetModuleParameters_DotNetStep_ReturnsTheCallParameters()
    {
        await PrepareStepAsync("AddStep");
        await Ts.ConfigureDotNetModuleAsync(TempSeqFile, Seq, Grp, "AddStep",
            FixtureAssembly, MathOps, "Add", save: true);

        var parms = await Ts.GetModuleParametersAsync(TempSeqFile, Seq, Grp, "AddStep");

        Assert.That(parms, Has.Count.EqualTo(3), "return value + two arguments");
        Assert.Multiple(() =>
        {
            Assert.That(parms[0].Name, Is.EqualTo("Return Value"));
            Assert.That(parms[0].Direction, Is.EqualTo("Return"),
                "direction comes from the Flags bits, there is no Direction leaf");
            Assert.That(parms[0].DataType, Is.EqualTo("Double"));
            Assert.That(parms[1].Name, Is.EqualTo("a"));
            Assert.That(parms[1].Direction, Is.EqualTo("Input"));
            Assert.That(parms[2].Name, Is.EqualTo("b"));
            Assert.That(parms.All(p => p.Type == "DotNetParameter"), Is.True);
        });
    }

    /// <summary>
    /// The multi-entry shape the issue calls out: one step chaining several invocations (a lifecycle
    /// entry at Calls[0], the real method at Calls[1]). Their parameter names collide — both carry a
    /// "Return Value" — so entries must be prefixed with their member. Verified live against a real
    /// two-call step (constructor + instance method, both IsCallValid); built synthetically here
    /// because no tool creates a call chain yet, and this pins the READER, which is what changed.
    /// </summary>
    [Test]
    public async Task GetModuleParameters_DotNetStepWithSeveralCalls_PrefixesNamesByMember()
    {
        await PrepareStepAsync("ChainStep");
        await Ts.ConfigureDotNetModuleAsync(TempSeqFile, Seq, Grp, "ChainStep",
            FixtureAssembly, MathOps, "Add", save: true);

        // Grow the resolved single call into a chain: a second entry with its own parameter.
        await Ts.CreateStepPropertyAsync(TempSeqFile, Seq, Grp, "ChainStep",
            "TS.SData.Calls", "array_elements", numElements: 2, save: false);
        await Ts.SetStepPropertyAsync(TempSeqFile, Seq, Grp, "ChainStep",
            "TS.SData.Calls[1].MemberName", "Dispose", "string", save: false);
        await Ts.CreateStepPropertyAsync(TempSeqFile, Seq, Grp, "ChainStep",
            "TS.SData.Calls[1].Params", "array_elements", numElements: 1, save: false);
        await Ts.SetStepPropertyAsync(TempSeqFile, Seq, Grp, "ChainStep",
            "TS.SData.Calls[1].Params[0].Name", "handle", "string", save: true);

        var parms = await Ts.GetModuleParametersAsync(TempSeqFile, Seq, Grp, "ChainStep");

        Assert.That(parms.Select(p => p.Name), Is.EqualTo(new[]
        {
            "Add.Return Value", "Add.a", "Add.b", "Dispose.handle"
        }), "with several call entries every parameter has to carry its member name");
    }

    /// <summary>An out parameter has to read back as an OUTPUT, not as another input.</summary>
    [Test]
    public async Task GetModuleParameters_DotNetOutParameter_ReportsOutputDirection()
    {
        await PrepareStepAsync("SplitStep");
        await Ts.ConfigureDotNetModuleAsync(TempSeqFile, Seq, Grp, "SplitStep",
            FixtureAssembly, MathOps, "Split", save: true);

        var parms = await Ts.GetModuleParametersAsync(TempSeqFile, Seq, Grp, "SplitStep");

        Assert.That(parms, Has.Count.EqualTo(2), "void return → only the two parameters");
        Assert.That(parms.Single(p => p.Name == "half").Direction, Is.EqualTo("Output"));
        Assert.That(parms.Single(p => p.Name == "value").Direction, Is.EqualTo("Input"));
    }

    /// <summary>
    /// A bare name matches ONE overload silently, so the resolved signature is the only honest
    /// report; passing the full signature selects a specific overload.
    /// </summary>
    [Test]
    public async Task ConfigureDotNetModule_FullSignature_SelectsThatOverload()
    {
        await PrepareStepAsync("OverloadStep");

        var byName = await Ts.ConfigureDotNetModuleAsync(TempSeqFile, Seq, Grp, "OverloadStep",
            FixtureAssembly, MathOps, "Overloaded", save: true);
        Assert.That(byName.AppliedSettings, Does.ContainKey("signature"),
            "with several overloads present the caller must be told which one it got");
        TestContext.WriteLine($"bare name → {byName.AppliedSettings["signature"]}");

        var bySignature = await Ts.ConfigureDotNetModuleAsync(TempSeqFile, Seq, Grp, "OverloadStep",
            FixtureAssembly, MathOps, "Overloaded(Double, Double)", save: true);

        Assert.That(bySignature.AppliedSettings, Does.ContainKey("memberResolved"), bySignature.Note);
        Assert.That(bySignature.AppliedSettings["signature"].ToString(),
            Is.EqualTo("Overloaded(Double, Double)"));
        var parms = await Ts.GetModuleParametersAsync(TempSeqFile, Seq, Grp, "OverloadStep");
        Assert.That(parms, Has.Count.EqualTo(3), "the 2-argument overload, not the 1-argument one");
        // The step must keep the plain member name — the signature is how it is SELECTED, not what
        // gets stored; the engine executes off Calls[0].MemberName.
        Assert.That(await ReadSDataLeafAsync("OverloadStep", "TS.SData.Calls[0]", "MemberName"),
            Is.EqualTo("Overloaded"));
    }

    /// <summary>
    /// An instance member cannot be a step's first call — the adapter needs an object. Without
    /// create_object that has to reach the caller as the adapter's own reason PLUS the way out,
    /// instead of a step that silently calls nothing.
    /// </summary>
    [Test]
    public async Task ConfigureDotNetModule_InstanceMemberWithoutAnObject_SaysHowToFixIt()
    {
        await PrepareStepAsync("InstanceStep");

        var result = await Ts.ConfigureDotNetModuleAsync(TempSeqFile, Seq, Grp, "InstanceStep",
            FixtureAssembly, InstanceOps, "Triple", save: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.AppliedSettings, Does.Not.ContainKey("memberResolved"));
            Assert.That(result.Note, Is.Not.Null.And.Contains("requires an object"),
                "the specific reason from the call-level load must survive into the note");
            Assert.That(result.Note, Does.Contain("create_object"),
                "a dead end is not a report — the note has to name the way out");
        });
        TestContext.WriteLine($"note: {result.Note}");
    }

    /// <summary>
    /// create_object builds the constructor→member call chain the adapter requires: Calls[0]
    /// constructs, Calls[1] invokes on that object. Measured as the ONLY working route — an instance
    /// member as the first call is refused, and the editor's "use existing object" entry is not
    /// reachable through this API.
    /// </summary>
    [Test]
    public async Task ConfigureDotNetModule_CreateObject_BuildsTheConstructorChain()
    {
        await PrepareStepAsync("InstanceStep");

        var result = await Ts.ConfigureDotNetModuleAsync(TempSeqFile, Seq, Grp, "InstanceStep",
            FixtureAssembly, InstanceOps, "Triple", save: true, createObject: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.AppliedSettings, Does.ContainKey("memberResolved"), $"note: {result.Note}");
            Assert.That(result.AppliedSettings["resolvedVia"], Is.EqualTo("call-chain"));
            Assert.That(result.AppliedSettings["constructorSignature"], Is.EqualTo("InstanceOps()"),
                "the parameterless constructor is derived from the SHORT class name");
            Assert.That(result.AppliedSettings["signature"], Is.EqualTo("Triple(Double)"));
        });

        // Both entries have to be on the step, in order.
        Assert.That(await ReadSDataLeafAsync("InstanceStep", "TS.SData.Calls[0]", "MemberName"),
            Is.EqualTo("InstanceOps"), "Calls[0] constructs");
        Assert.That(await ReadSDataLeafAsync("InstanceStep", "TS.SData.Calls[1]", "MemberName"),
            Is.EqualTo("Triple"), "Calls[1] calls the member on it");

        // And the chain's parameters read back prefixed, which is how they are addressed.
        var parms = await Ts.GetModuleParametersAsync(TempSeqFile, Seq, Grp, "InstanceStep");
        Assert.That(parms.Select(p => p.Name), Is.EqualTo(new[]
        {
            "InstanceOps.Return Value", "Triple.Return Value", "Triple.a"
        }));
    }

    /// <summary>The proof that a constructed object really gets called: Triple(4) must write 12.</summary>
    [Test]
    public async Task ConfigureDotNetModule_CreateObject_InstanceMethodReallyExecutes()
    {
        string probe = NewProbeName();
        await Ts.SetStationGlobalAsync(probe, 0);
        try
        {
            await Ts.CreateSequenceFileAsync(TempSeqFile);
            await Ts.InsertStepAsync(TempSeqFile, "MainSequence", Grp, "Action", "InstanceStep");
            var cfg = await Ts.ConfigureDotNetModuleAsync(TempSeqFile, "MainSequence", Grp,
                "InstanceStep", FixtureAssembly, InstanceOps, "Triple", save: true, createObject: true);
            Assert.That(cfg.AppliedSettings, Does.ContainKey("memberResolved"), cfg.Note);

            await Ts.SetModuleParameterAsync(TempSeqFile, "MainSequence", Grp, "InstanceStep",
                "Triple.a", "4");
            await Ts.SetModuleParameterAsync(TempSeqFile, "MainSequence", Grp, "InstanceStep",
                "Triple.Return Value", $"StationGlobals.{probe}");

            var run = await Ts.RunSequenceAsync(TempSeqFile, "MainSequence", null, 60);
            TestContext.WriteLine($"status={run.Status} result={run.Result}");

            var globals = await Ts.GetStationGlobalsAsync();
            Assert.That(Convert.ToDouble(globals.First(g => g.Name == probe).Value),
                Is.EqualTo(12.0).Within(1e-9),
                "the constructed object's Triple(4) must have run — 0 means the chain called nothing");
        }
        finally { await Ts.DeleteStationGlobalAsync(probe); }
    }

    /// <summary>A non-default constructor is selected by signature, like an overloaded member.</summary>
    [Test]
    public async Task ConfigureDotNetModule_CreateObject_ExplicitConstructorSignature()
    {
        await PrepareStepAsync("InstanceStep");

        var result = await Ts.ConfigureDotNetModuleAsync(TempSeqFile, Seq, Grp, "InstanceStep",
            FixtureAssembly, InstanceOps, "Triple", save: true,
            createObject: true, constructor: "InstanceOps(Double)");

        Assert.That(result.AppliedSettings, Does.ContainKey("memberResolved"), $"note: {result.Note}");
        Assert.That(result.AppliedSettings["constructorSignature"], Is.EqualTo("InstanceOps(Double)"));
        var parms = await Ts.GetModuleParametersAsync(TempSeqFile, Seq, Grp, "InstanceStep");
        Assert.That(parms.Any(p => p.Name == "InstanceOps.factor"), Is.True,
            "the chosen constructor's own argument has to be bindable too");
    }

    /// <summary>dispose_object must either land on the step or be named as not applied — the house
    /// rule that a report only ever states what a read-back confirmed.</summary>
    [Test]
    public async Task ConfigureDotNetModule_CreateObject_DisposeObject_IsAppliedOrReported()
    {
        await PrepareStepAsync("InstanceStep");

        var result = await Ts.ConfigureDotNetModuleAsync(TempSeqFile, Seq, Grp, "InstanceStep",
            FixtureAssembly, InstanceOps, "Triple", save: true,
            createObject: true, disposeObject: true);

        Assert.That(result.AppliedSettings, Does.ContainKey("memberResolved"), $"note: {result.Note}");
        if (result.AppliedSettings.ContainsKey("disposeObject"))
        {
            Assert.That(await ReadSDataLeafAsync("InstanceStep", "TS.SData.Calls[0].Params[0]", "CallDispose"),
                Is.EqualTo("True"),
                "reported as applied → the step must really carry it (CallDispose on the created object)");
        }
        else
        {
            Assert.That(result.Note, Is.Not.Null.And.Contains("disposeObject"),
                "not applied → it must be named, never silently dropped");
        }
        TestContext.WriteLine($"disposeObject applied: {result.AppliedSettings.ContainsKey("disposeObject")}; " +
                              $"note: {result.Note}");
    }

    /// <summary>A constructor that does not exist must fail loudly, not leave a half-built chain
    /// reported as success.</summary>
    [Test]
    public async Task ConfigureDotNetModule_CreateObject_UnknownConstructor_IsReported()
    {
        await PrepareStepAsync("InstanceStep");

        var result = await Ts.ConfigureDotNetModuleAsync(TempSeqFile, Seq, Grp, "InstanceStep",
            FixtureAssembly, InstanceOps, "Triple", save: true,
            createObject: true, constructor: "InstanceOps(System.DateTime)");

        Assert.Multiple(() =>
        {
            Assert.That(result.AppliedSettings, Does.Not.ContainKey("memberResolved"));
            Assert.That(result.Note, Is.Not.Null.And.Contains("constructor"));
        });
        TestContext.WriteLine($"note: {result.Note}");
    }

    /// <summary>
    /// load_module_prototype's documented flow for .NET: configure while the assembly is out of reach,
    /// then load once it is there. It could never work — the generic Module.LoadPrototype every other
    /// adapter uses does not resolve a .NET member, so the tool reported prototypeLoaded=false and an
    /// empty interface however reachable the assembly was (issue #37 names this tool in its title).
    /// </summary>
    [Test]
    public async Task LoadModulePrototype_DotNetStep_ResolvesOnceTheAssemblyIsReachable()
    {
        string lateAssembly = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"TS_LateArrival_{Guid.NewGuid():N}.dll");
        await PrepareStepAsync("LateStep");
        try
        {
            var cfg = await Ts.ConfigureDotNetModuleAsync(TempSeqFile, Seq, Grp, "LateStep",
                lateAssembly, MathOps, "Add", save: true);
            Assert.That(cfg.AppliedSettings, Does.Not.ContainKey("memberResolved"),
                "the assembly does not exist yet — nothing may claim to be resolved");

            System.IO.File.Copy(FixtureAssembly, lateAssembly);

            var load = await Ts.LoadModulePrototypeAsync(TempSeqFile, Seq, Grp, "LateStep");

            Assert.Multiple(() =>
            {
                Assert.That(load.PrototypeLoaded, Is.True, $"note: {load.Note}");
                Assert.That(load.Parameters, Has.Count.EqualTo(3),
                    "the interface has to materialise: return value + two arguments");
            });
            TestContext.WriteLine($"note: {load.Note}");
        }
        finally
        {
            // The adapter may still hold the file — cleanup is best-effort by design.
            try { System.IO.File.Delete(lateAssembly); } catch { }
        }
    }

    /// <summary>
    /// The other half: re-loading the prototype of an ALREADY valid step must leave it valid. The
    /// first two tiers fail for a member with parameters, and a failed attempt must not strip the
    /// interface the step already had.
    /// </summary>
    [Test]
    public async Task LoadModulePrototype_DotNetStep_KeepsAnAlreadyValidInterface()
    {
        await PrepareStepAsync("AddStep");
        await Ts.ConfigureDotNetModuleAsync(TempSeqFile, Seq, Grp, "AddStep",
            FixtureAssembly, MathOps, "Add", save: true);

        var load = await Ts.LoadModulePrototypeAsync(TempSeqFile, Seq, Grp, "AddStep");

        Assert.That(load.PrototypeLoaded, Is.True, $"note: {load.Note}");
        Assert.That(load.Parameters, Has.Count.EqualTo(3), "re-loading must not degrade a working step");
        var parms = await Ts.GetModuleParametersAsync(TempSeqFile, Seq, Grp, "AddStep");
        Assert.That(parms, Has.Count.EqualTo(3), "and it must stay that way on the saved step");
    }

    /// <summary>set_module_parameter reaches a .NET step's arguments — it used to fall through every
    /// stage and throw, so binding was only possible through the raw property path.</summary>
    [Test]
    public async Task SetModuleParameter_DotNetStep_BindsAnArgumentByName()
    {
        await PrepareStepAsync("AddStep");
        await Ts.ConfigureDotNetModuleAsync(TempSeqFile, Seq, Grp, "AddStep",
            FixtureAssembly, MathOps, "Add", save: true);

        await Ts.SetModuleParameterAsync(TempSeqFile, Seq, Grp, "AddStep", "a", "Locals.X");
        await Ts.SetModuleParameterAsync(TempSeqFile, Seq, Grp, "AddStep", "Return Value", "Locals.Sum");

        var parms = await Ts.GetModuleParametersAsync(TempSeqFile, Seq, Grp, "AddStep");
        Assert.Multiple(() =>
        {
            Assert.That(parms.Single(p => p.Name == "a").Value, Is.EqualTo("Locals.X"));
            Assert.That(parms.Single(p => p.Name == "Return Value").Value, Is.EqualTo("Locals.Sum"),
                "binding the return entry sets the destination the result is written to");
            Assert.That(parms.Single(p => p.Name == "b").Value, Is.Empty, "untouched stays untouched");
        });
    }

    /// <summary>With a call chain the bare names collide, so the member-prefixed form is the only way
    /// to address the second entry — and it has to reach exactly that one.</summary>
    [Test]
    public async Task SetModuleParameter_DotNetChain_BindsByPrefixedName()
    {
        await PrepareStepAsync("ChainStep");
        await Ts.ConfigureDotNetModuleAsync(TempSeqFile, Seq, Grp, "ChainStep",
            FixtureAssembly, MathOps, "Add", save: true);
        await Ts.CreateStepPropertyAsync(TempSeqFile, Seq, Grp, "ChainStep",
            "TS.SData.Calls", "array_elements", numElements: 2, save: false);
        await Ts.SetStepPropertyAsync(TempSeqFile, Seq, Grp, "ChainStep",
            "TS.SData.Calls[1].MemberName", "Dispose", "string", save: false);
        await Ts.CreateStepPropertyAsync(TempSeqFile, Seq, Grp, "ChainStep",
            "TS.SData.Calls[1].Params", "array_elements", numElements: 1, save: false);
        await Ts.SetStepPropertyAsync(TempSeqFile, Seq, Grp, "ChainStep",
            "TS.SData.Calls[1].Params[0].Name", "handle", "string", save: true);

        await Ts.SetModuleParameterAsync(TempSeqFile, Seq, Grp, "ChainStep", "Dispose.handle", "Locals.H");

        var parms = await Ts.GetModuleParametersAsync(TempSeqFile, Seq, Grp, "ChainStep");
        Assert.That(parms.Single(p => p.Name == "Dispose.handle").Value, Is.EqualTo("Locals.H"));
        Assert.That(parms.Where(p => p.Name.StartsWith("Add.")).All(p => string.IsNullOrEmpty(p.Value)),
            Is.True, "the first call's parameters must not be touched");
    }

    /// <summary>A name that matches nothing must still fail loudly — the tool's contract for every
    /// other adapter, and the difference between a typo and a silent no-op.</summary>
    [Test]
    public async Task SetModuleParameter_DotNetStep_UnknownParameter_Throws()
    {
        await PrepareStepAsync("AddStep");
        await Ts.ConfigureDotNetModuleAsync(TempSeqFile, Seq, Grp, "AddStep",
            FixtureAssembly, MathOps, "Add", save: true);

        Assert.ThrowsAsync<System.InvalidOperationException>(async () =>
            await Ts.SetModuleParameterAsync(TempSeqFile, Seq, Grp, "AddStep", "nosuchparam", "1"));
    }

    /// <summary>The end-to-end proof for the binding route callers are told to use: arguments and the
    /// result destination bound with set_module_parameter, then actually executed.</summary>
    [Test]
    public async Task SetModuleParameter_DotNetStep_BoundArgumentsReallyExecute()
    {
        string probe = NewProbeName();
        await Ts.SetStationGlobalAsync(probe, 0);
        try
        {
            await Ts.CreateSequenceFileAsync(TempSeqFile);
            await Ts.InsertStepAsync(TempSeqFile, "MainSequence", Grp, "Action", "AddStep");
            var cfg = await Ts.ConfigureDotNetModuleAsync(TempSeqFile, "MainSequence", Grp, "AddStep",
                FixtureAssembly, MathOps, "Add", save: true);
            Assert.That(cfg.AppliedSettings, Does.ContainKey("memberResolved"), cfg.Note);

            await Ts.SetModuleParameterAsync(TempSeqFile, "MainSequence", Grp, "AddStep", "a", "4");
            await Ts.SetModuleParameterAsync(TempSeqFile, "MainSequence", Grp, "AddStep", "b", "5");
            await Ts.SetModuleParameterAsync(TempSeqFile, "MainSequence", Grp, "AddStep",
                "Return Value", $"StationGlobals.{probe}");

            await Ts.RunSequenceAsync(TempSeqFile, "MainSequence", null, 60);

            var globals = await Ts.GetStationGlobalsAsync();
            Assert.That(Convert.ToDouble(globals.First(g => g.Name == probe).Value),
                Is.EqualTo(9.0).Within(1e-9), "Add(4,5) must have run with the bound arguments");
        }
        finally { await Ts.DeleteStationGlobalAsync(probe); }
    }

    /// <summary>
    /// The only test that proves the step CALLS something: a resolved member with bound arguments,
    /// its return value written to a StationGlobal, executed for real. An unresolved .NET step runs
    /// as a silent no-op that still reports Passed — which is exactly what made issue #37 invisible
    /// — so nothing short of an observable side effect is evidence.
    /// </summary>
    [Test]
    public async Task DotNetStep_WithBoundArguments_ActuallyCallsTheMethod()
    {
        string probe = NewProbeName();
        await Ts.SetStationGlobalAsync(probe, 0);
        try
        {
            await Ts.CreateSequenceFileAsync(TempSeqFile);
            await Ts.InsertStepAsync(TempSeqFile, "MainSequence", Grp, "Action", "AddStep");
            var cfg = await Ts.ConfigureDotNetModuleAsync(TempSeqFile, "MainSequence", Grp, "AddStep",
                FixtureAssembly, MathOps, "Add", save: true);
            Assert.That(cfg.AppliedSettings, Does.ContainKey("memberResolved"), cfg.Note);

            // Bind both arguments and send the result to the probe. ArgVal is the parameter's
            // expression slot: an input reads from it, the return value is written TO it.
            const string p = "TS.SData.Calls[0].Params";
            await Ts.SetStepPropertyAsync(TempSeqFile, "MainSequence", Grp, "AddStep",
                $"{p}[1].ArgVal", "2", "string", save: false);
            await Ts.SetStepPropertyAsync(TempSeqFile, "MainSequence", Grp, "AddStep",
                $"{p}[2].ArgVal", "3", "string", save: false);
            await Ts.SetStepPropertyAsync(TempSeqFile, "MainSequence", Grp, "AddStep",
                $"{p}[0].ArgVal", $"StationGlobals.{probe}", "string", save: true);

            var run = await Ts.RunSequenceAsync(TempSeqFile, "MainSequence", null, 60);
            TestContext.WriteLine($"status={run.Status} result={run.Result}");

            var globals = await Ts.GetStationGlobalsAsync();
            double value = Convert.ToDouble(globals.First(g => g.Name == probe).Value);
            Assert.That(value, Is.EqualTo(5.0).Within(1e-9),
                "Add(2,3) must have run and written its result — 0 means the step called nothing");
        }
        finally
        {
            await Ts.DeleteStationGlobalAsync(probe);
        }
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
