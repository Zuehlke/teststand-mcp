using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using TestStandMCP.Tools;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// Authoring-completeness additions (from the TFW_DemoModule 1:1 rebuild exercise):
///
///  - create_step_property: creates NEW subproperties on a step (scalar / container /
///    reference / NAMED type) and resizes typed arrays (SetNumElements) — the pieces a
///    1:1 file rebuild needs for Result.TimeoutOccurred, TS.ErrorDialogOptions,
///    SequenceCall ActualArgs entries and LabVIEW ViCall.Parms prototypes.
///  - set_step_property unescape: writes bare control chars (\r) through an MCP string.
///  - set_step_property_flags: raw PropFlags on any step property.
///  - insert_file_global 'reference' → a true Object Reference (was: silent String).
///  - get/set_module_parameter: SequenceCall ActualArgs + LabVIEW ViCall.Parms bindings.
///  - open_sequence_file now maps sequence PARAMETERS (was always []), with type names.
///  - plan validator: unknown (non-forbidden) step types warn instead of erroring.
/// </summary>
[TestFixture]
[Category("StepConfig")]
public class T31_StepPropertyCreationTests : TestBase
{
    private async Task<string> NewFileWithStepAsync(string stepType, string stepName,
        string adapter = "")
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertStepAsync(TempSeqFile, "MainSequence", "Main", stepType, stepName,
            adapterName: string.IsNullOrEmpty(adapter) ? null : adapter);
        await Ts.SaveSequenceFileAsync(TempSeqFile);
        return TempSeqFile;
    }

    // ── create_step_property: scalar on a step Result ──────────────────────────

    [Test]
    public async Task CreateStepProperty_CreatesBooleanUnderResult()
    {
        await NewFileWithStepAsync("NI_Wait", "W");

        var r = await Ts.CreateStepPropertyAsync(TempSeqFile, "MainSequence", "Main", "W",
            "Result.TimeoutOccurred", "boolean", value: "false");

        Assert.That(r.ValueType, Is.EqualTo("Boolean"));
        Assert.That((bool)r.Value!, Is.False);
    }

    // ── create_step_property: engine NAMED type (ErrorDialogOptions) ───────────

    [Test]
    public async Task CreateStepProperty_CreatesNamedTypeContainer()
    {
        // 'Error' is a standard type every engine session can resolve. Step-type-owned types
        // (e.g. 'ErrorDialogOptions') resolve the same way once ANY loaded file or palette
        // defines them — the engine-level NewPropertyObject fallback covers both.
        await NewFileWithStepAsync("Statement", "S");

        var r = await Ts.CreateStepPropertyAsync(TempSeqFile, "MainSequence", "Main", "S",
            "TS.MyError", "named_type", typeName: "Error");

        Assert.That(r.ValueType, Is.EqualTo("Container"),
            "a named-type property must come in as a container instance of that type");

        // Members of the named type must exist and be settable via set_step_property.
        var ig = await Ts.SetStepPropertyAsync(TempSeqFile, "MainSequence", "Main", "S",
            "TS.MyError.Occurred", "true", "boolean");
        Assert.That((bool)ig.Value!, Is.True);
    }

    // ── create_step_property: idempotent on an existing path ───────────────────

    [Test]
    public async Task CreateStepProperty_IsIdempotentAndAppliesValue()
    {
        await NewFileWithStepAsync("NI_Wait", "W");
        await Ts.CreateStepPropertyAsync(TempSeqFile, "MainSequence", "Main", "W",
            "Result.TimeoutOccurred", "boolean", value: "false");

        // Second call on the same path must not throw and must apply the new value.
        var r = await Ts.CreateStepPropertyAsync(TempSeqFile, "MainSequence", "Main", "W",
            "Result.TimeoutOccurred", "boolean", value: "true");
        Assert.That((bool)r.Value!, Is.True);
    }

    // ── create_step_property: typed array resize (ViCall.Parms) ────────────────

    [Test]
    public async Task CreateStepProperty_ArrayElements_AuthorsViCallParms()
    {
        await NewFileWithStepAsync("Action", "A", "LabVIEW");
        await Ts.ConfigureLabViewModuleAsync(TempSeqFile, "MainSequence", "Main", "A",
            @"Lib.lvlibp\Sub\My.vi");

        var arr = await Ts.CreateStepPropertyAsync(TempSeqFile, "MainSequence", "Main", "A",
            "TS.SData.ViCall.Parms", "array_elements", numElements: 2);
        Assert.That(arr.IsArray, Is.True);
        Assert.That(arr.NumElements, Is.EqualTo(2));

        // The elements must be typed VIParameter containers → Label/ArgVal exist.
        await Ts.SetStepPropertyAsync(TempSeqFile, "MainSequence", "Main", "A",
            "TS.SData.ViCall.Parms[0].Label", "error in (no error)", "string", save: false);
        await Ts.SetStepPropertyAsync(TempSeqFile, "MainSequence", "Main", "A",
            "TS.SData.ViCall.Parms[0].ArgVal", "Locals.X", "string", save: false);
        await Ts.SetStepPropertyAsync(TempSeqFile, "MainSequence", "Main", "A",
            "TS.SData.ViCall.Parms[1].Label", "error out", "string");

        var parms = await Ts.GetModuleParametersAsync(TempSeqFile, "MainSequence", "Main", "A");
        Assert.That(parms.Select(p => p.Name),
            Does.Contain("error in (no error)").And.Contain("error out"),
            "get_module_parameters must now read ViCall.Parms");
        Assert.That(parms.First(p => p.Name == "error in (no error)").Value,
            Is.EqualTo("Locals.X"));
    }

    // ── set_module_parameter: LabVIEW binding via Label ────────────────────────

    [Test]
    public async Task SetModuleParameter_BindsViCallParmByLabel()
    {
        await NewFileWithStepAsync("Action", "A", "LabVIEW");
        await Ts.ConfigureLabViewModuleAsync(TempSeqFile, "MainSequence", "Main", "A",
            @"Lib.lvlibp\Sub\My.vi");
        await Ts.CreateStepPropertyAsync(TempSeqFile, "MainSequence", "Main", "A",
            "TS.SData.ViCall.Parms", "array_elements", numElements: 1);
        await Ts.SetStepPropertyAsync(TempSeqFile, "MainSequence", "Main", "A",
            "TS.SData.ViCall.Parms[0].Label", "Wait for Event Sync?", "string");

        await Ts.SetModuleParameterAsync(TempSeqFile, "MainSequence", "Main", "A",
            "Wait for Event Sync?", "Locals.WaitForEventSync");

        var parms = await Ts.GetModuleParametersAsync(TempSeqFile, "MainSequence", "Main", "A");
        Assert.That(parms.Single(p => p.Name == "Wait for Event Sync?").Value,
            Is.EqualTo("Locals.WaitForEventSync"));
    }

    // ── set_module_parameter: creates SequenceCall ActualArgs on demand ────────

    [Test]
    public async Task SetModuleParameter_CreatesSequenceCallArgument()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "Callee");
        await Ts.InsertSequenceParameterAsync(TempSeqFile, "Callee", "SetPoint", "number",
            defaultValue: "0");
        await Ts.InsertStepAsync(TempSeqFile, "MainSequence", "Main", "SequenceCall", "CallIt");
        await Ts.SetSequenceCallTargetAsync(TempSeqFile, "MainSequence", "Main", "CallIt", "Callee");

        await Ts.SetModuleParameterAsync(TempSeqFile, "MainSequence", "Main", "CallIt",
            "SetPoint", "Locals.Setpoint");

        var parms = await Ts.GetModuleParametersAsync(TempSeqFile, "MainSequence", "Main", "CallIt");
        var arg = parms.SingleOrDefault(p => p.Name == "SetPoint");
        Assert.That(arg, Is.Not.Null, "ActualArgs entry must be created on demand");
        Assert.That(arg!.Value, Is.EqualTo("Locals.Setpoint"));
        Assert.That(arg.Type, Is.EqualTo("SequenceArgument"));
    }

    // ── set_step_property: unescape writes bare control characters ─────────────

    [Test]
    public async Task SetStepProperty_Unescape_WritesCarriageReturns()
    {
        await NewFileWithStepAsync("Action", "A", "LabVIEW");
        await Ts.ConfigureLabViewModuleAsync(TempSeqFile, "MainSequence", "Main", "A",
            @"Lib.lvlibp\Sub\My.vi");

        var r = await Ts.SetStepPropertyAsync(TempSeqFile, "MainSequence", "Main", "A",
            "TS.SData.ViCall.VIDescription", @"line1\r\nline2\rtail", "string",
            save: true, unescape: true);

        Assert.That((string)r.Value!, Is.EqualTo("line1\r\nline2\rtail"),
            "\\r/\\n escapes must decode to bare control characters");
    }

    [Test]
    public void UnescapeValue_DecodesSupportedEscapes()
    {
        Assert.That(TestStandMCP.Services.TestStandService.UnescapeValue(
            @"a\rb\nc\td\\eüf"), Is.EqualTo("a\rb\nc\td\\eüf"));
        Assert.That(TestStandMCP.Services.TestStandService.UnescapeValue("no-escapes"),
            Is.EqualTo("no-escapes"));
        Assert.That(TestStandMCP.Services.TestStandService.UnescapeValue(@"keep\q"),
            Is.EqualTo(@"keep\q"), "unknown escapes stay verbatim");
    }

    // ── rename_step_property: named array elements (FileDiffer pairs by name) ──

    [Test]
    public async Task RenameStepProperty_NamesArrayElement()
    {
        await NewFileWithStepAsync("Action", "A", "LabVIEW");
        await Ts.ConfigureLabViewModuleAsync(TempSeqFile, "MainSequence", "Main", "A",
            @"Lib.lvlibp\Sub\My.vi");
        await Ts.CreateStepPropertyAsync(TempSeqFile, "MainSequence", "Main", "A",
            "TS.SData.ViCall.Parms", "array_elements", numElements: 1, save: false);

        var r = await Ts.RenameStepPropertyAsync(TempSeqFile, "MainSequence", "Main", "A",
            "TS.SData.ViCall.Parms[0]", "error in (no error)");
        Assert.That(r.Value?.ToString(), Is.EqualTo("error in (no error)"));

        // The element name must surface through get_property_tree's elementName field.
        var tree = await Ts.GetPropertyTreeAsync("SequenceFile", TempSeqFile,
            "Data.Seq[\"MainSequence\"].Main[\"A\"].TS.SData.ViCall.Parms", 3, true, 10);
        Assert.That(tree.Children, Is.Not.Null.And.Count.EqualTo(1));
        Assert.That(tree.Children![0].ElementName, Is.EqualTo("error in (no error)"),
            "get_property_tree must report the real element name");
    }

    // ── set_step_property_flags ────────────────────────────────────────────────

    [Test]
    public async Task SetStepPropertyFlags_SetsAndReadsBackFlags()
    {
        await NewFileWithStepAsync("NI_LV_RunVIAsynchronously", "RunAsync");

        var r = await Ts.SetStepPropertyFlagsAsync(TempSeqFile, "MainSequence", "Main",
            "RunAsync", "VIModule", 0x200000);

        Assert.That(Convert.ToInt32(r.Value), Is.EqualTo(0x200000));
    }

    // ── insert_file_global / insert_local_variable: Object Reference ───────────

    [Test]
    public async Task InsertFileGlobal_Reference_CreatesObjectReference()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);

        await Ts.InsertFileGlobalAsync(TempSeqFile, "ThreadRef", "reference");

        var globals = await Ts.GetFileGlobalsAsync(TempSeqFile);
        var g = globals.Single(v => v.Name == "ThreadRef");
        Assert.That(g.DataType, Does.Contain("Reference"),
            $"'reference' must create an Object Reference, not '{g.DataType}'");
    }

    [Test]
    public async Task InsertLocalVariable_Reference_CreatesObjectReference()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);

        await Ts.InsertLocalVariableAsync(TempSeqFile, "MainSequence", "Ref", "reference");

        var locals = await Ts.GetLocalVariablesAsync(TempSeqFile, "MainSequence");
        Assert.That(locals.Single(v => v.Name == "Ref").DataType, Does.Contain("Reference"));
    }

    // ── open_sequence_file: parameters are mapped (was always []) ──────────────

    [Test]
    public async Task OpenSequenceFile_MapsSequenceParameters()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceParameterAsync(TempSeqFile, "MainSequence", "TimedOut", "boolean",
            defaultValue: "false", passByReference: true);
        await Ts.SaveSequenceFileAsync(TempSeqFile);

        var info = await Ts.OpenSequenceFileAsync(TempSeqFile);
        var main = info.Sequences.Single(s => s.Name == "MainSequence");

        Assert.That(main.Parameters, Has.Count.EqualTo(1),
            "open_sequence_file must map sequence parameters");
        Assert.That(main.Parameters[0].Name, Is.EqualTo("TimedOut"));
        Assert.That(main.Parameters[0].PassByReference, Is.True);
        Assert.That(main.Parameters[0].DataType, Is.Not.Empty,
            "parameter dataType must be populated (GetTypeDisplayString)");
    }

    // ── plan validator: unknown types warn, forbidden types still error ────────

    [Test]
    public void Validator_UnknownStepType_IsWarningNotError()
    {
        var r = SequencePlanValidator.Validate("T",
            new[]
            {
                new PlanStepInput { Name = "L", StepType = "NI_LV_RunVIAsynchronously" }
            },
            Array.Empty<string>());

        Assert.That(r.Errors.Select(e => e.Code), Does.Not.Contain("E_UNKNOWN_TYPE"));
        Assert.That(r.Warnings.Select(w => w.Code), Does.Contain("W_UNKNOWN_TYPE"));
        Assert.That(r.Valid, Is.True, "an installed custom step type must not block the build");
    }

    [Test]
    public void Validator_ForbiddenTypes_StillError()
    {
        var r = SequencePlanValidator.Validate("T",
            new[] { new PlanStepInput { Name = "G", StepType = "Goto" } },
            Array.Empty<string>());

        Assert.That(r.Valid, Is.False);
        Assert.That(r.Errors.Select(e => e.Code), Does.Contain("E_FORBIDDEN_TYPE"));
    }
}
