using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using TestStandMCP.Models;
using TestStandMCP.Services;
using TestStandMCP.Tools;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// End-to-end MCP-wiring tests for the "Group B" tools that are headless-testable but
/// previously had no coverage. Like <see cref="T21_NewToolDispatchTests"/>, every test
/// exercises the FULL dispatch path (JSON arguments → TestStandToolRegistry.CallToolAsync
/// → handler → service → engine → JSON/text result), so tool registration, schema and
/// argument extraction are covered alongside the service logic.
///
/// Tools covered here:
///   Properties/variables/arrays : get_property, set_property, set_station_global,
///                                 set_local_variable, get_step, get_step_type,
///                                 get_array_variable, set_array_element,
///                                 resize_array_variable, delete_data_type
///   Step config/modules/templates: insert_steps_bulk, set_step_module_path,
///                                 get_step_module_info, get_module_parameters,
///                                 set_module_parameter, get_step_templates,
///                                 insert_step_from_template, configure_message_popup,
///                                 configure_property_loader
///   File/undo/misc              : compare_sequence_files, cancel_undo_group, find_file,
///                                 validate_sequence_plan
///
/// NOTE: disconnect_engine is intentionally NOT tested here — the whole suite shares ONE
/// engine (AssemblySetup), and disconnecting it mid-run would break every subsequent
/// fixture and re-trigger the COM teardown hang the run-settings are designed to avoid.
/// </summary>
[TestFixture]
[Category("HeadlessDispatch")]
public class T22_HeadlessToolDispatchTests : TestBase
{
    private TestStandToolRegistry _registry = null!;
    private ISequenceEditorService _editor  = null!;

    [OneTimeSetUp]
    public void BuildRegistry()
    {
        _editor   = new SequenceEditorService(NullLogger<SequenceEditorService>.Instance);
        _registry = new TestStandToolRegistry(AssemblySetup.Ts, _editor,
            NullLogger<TestStandToolRegistry>.Instance);
    }

    [OneTimeTearDown]
    public void DisposeEditor() => _editor?.Dispose();

    // ── helpers (mirrors T21) ───────────────────────────────────────────────────
    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;
    private static string      TextOf(CallToolResult r) => r.Content[0].Text;
    private static string      J(string s) => JsonSerializer.Serialize(s);
    private static JsonElement Doc(CallToolResult r) => JsonDocument.Parse(TextOf(r)).RootElement;

    private Task<CallToolResult> Call(string tool, string json) =>
        _registry.CallToolAsync(tool, Args(json));

    // ── Registration smoke ──────────────────────────────────────────────────────

    [Test]
    public void GroupBTools_AreRegistered()
    {
        var names = _registry.GetTools().Select(t => t.Name).ToList();
        foreach (var t in new[]
        {
            "get_property", "set_property", "set_station_global", "set_local_variable",
            "get_step", "get_step_type", "get_array_variable", "set_array_element",
            "resize_array_variable", "delete_data_type", "insert_steps_bulk",
            "set_step_module_path", "get_step_module_info", "get_module_parameters",
            "set_module_parameter", "get_step_templates", "insert_step_from_template",
            "configure_message_popup", "configure_property_loader",
            "compare_sequence_files", "cancel_undo_group", "find_file",
            "validate_sequence_plan"
        })
            Assert.That(names, Does.Contain(t), $"Tool '{t}' should be registered");
    }

    // ── set_station_global / get_property / set_property (round-trip chain) ───────

    [Test]
    public async Task StationGlobal_SetGetSet_RoundTripsViaProperties()
    {
        // Uniquely named so we never collide with a real station global.
        var name   = $"TS_T22_Probe_{Guid.NewGuid():N}";
        var lookup = $"StationGlobals.{name}";

        try
        {
            // set_station_global creates the global (and commits it to disk).
            var setG = await Call("set_station_global",
                $"{{\"variable_name\":{J(name)},\"value\":\"41\"}}");
            Assert.That(setG.IsError, Is.False, TextOf(setG));

            // get_property reads it back.
            var get1 = await Call("get_property", $"{{\"lookup_string\":{J(lookup)}}}");
            Assert.That(get1.IsError, Is.False, TextOf(get1));
            Assert.That(Doc(get1).GetProperty("value").GetDouble(),
                Is.EqualTo(41.0).Within(1e-9));

            // set_property updates the now-existing global.
            var setP = await Call("set_property",
                $"{{\"lookup_string\":{J(lookup)},\"value\":\"42\"}}");
            Assert.That(setP.IsError, Is.False, TextOf(setP));

            // get_property confirms the update.
            var get2 = await Call("get_property", $"{{\"lookup_string\":{J(lookup)}}}");
            Assert.That(get2.IsError, Is.False, TextOf(get2));
            Assert.That(Doc(get2).GetProperty("value").GetDouble(),
                Is.EqualTo(42.0).Within(1e-9));

            // Remove the global we created and confirm it is gone — leaves the station's
            // StationGlobals.ini exactly as we found it.
            await Ts.DeleteStationGlobalAsync(name);
            var gone = await Call("get_property", $"{{\"lookup_string\":{J(lookup)}}}");
            Assert.That(gone.IsError, Is.True,
                "Station global should no longer resolve after deletion");
        }
        finally
        {
            // Best-effort cleanup in case an assertion above threw before the explicit delete,
            // so a failed run never leaves a probe global behind on the station.
            try { await Ts.DeleteStationGlobalAsync(name); } catch { /* ignore */ }
        }
    }

    // ── set_local_variable ───────────────────────────────────────────────────────

    [Test]
    public async Task SetLocalVariable_UpdatesValue()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "LSeq");
        await Ts.InsertLocalVariableAsync(TempSeqFile, "LSeq", "Counter", "number", "0");

        var r = await Call("set_local_variable",
            $"{{\"file_path\":{J(TempSeqFile)},\"sequence_name\":\"LSeq\"," +
            $"\"variable_name\":\"Counter\",\"value\":\"7\"}}");
        Assert.That(r.IsError, Is.False, TextOf(r));

        var locals = await Ts.GetLocalVariablesAsync(TempSeqFile, "LSeq");
        var counter = locals.FirstOrDefault(v => v.Name == "Counter");
        Assert.That(counter, Is.Not.Null, "Counter local should exist");
        Assert.That(Convert.ToDouble(counter!.Value), Is.EqualTo(7.0).Within(1e-9));
    }

    // ── get_step ──────────────────────────────────────────────────────────────────

    [Test]
    public async Task GetStep_ReturnsNameAndType()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "GSeq");
        await Ts.InsertStepAsync(TempSeqFile, "GSeq", "Main", "Statement", "MyStmt");

        var r = await Call("get_step",
            $"{{\"sequence_file_path\":{J(TempSeqFile)},\"sequence_name\":\"GSeq\"," +
            $"\"step_name\":\"MyStmt\"}}");
        Assert.That(r.IsError, Is.False, TextOf(r));

        var doc = Doc(r);
        Assert.That(doc.GetProperty("name").GetString(), Is.EqualTo("MyStmt"));
        Assert.That(doc.GetProperty("stepType").GetString(), Is.EqualTo("Statement"));
    }

    // ── get_step_type ──────────────────────────────────────────────────────────────

    [Test]
    public async Task GetStepType_ReturnsRequestedType()
    {
        var r = await Call("get_step_type", "{\"step_type_name\":\"Statement\"}");
        Assert.That(r.IsError, Is.False, TextOf(r));
        Assert.That(Doc(r).GetProperty("name").GetString(), Is.EqualTo("Statement"));
    }

    // ── array variable trio: resize → set element → get ──────────────────────────

    [Test]
    public async Task ArrayVariable_Resize_SetElement_Get_RoundTrips()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "ASeq");
        // The "number[]" suffix makes insert_local_variable declare a 1-D array Local
        // (the only headless way to obtain an array variable for the array tools to act on).
        await Ts.InsertLocalVariableAsync(TempSeqFile, "ASeq", "NumArr", "number[]");

        var resize = await Call("resize_array_variable",
            $"{{\"file_path\":{J(TempSeqFile)},\"sequence_name\":\"ASeq\"," +
            "\"variable_name\":\"NumArr\",\"new_size\":3}");
        Assert.That(resize.IsError, Is.False, TextOf(resize));

        for (int i = 0; i < 3; i++)
        {
            var set = await Call("set_array_element",
                $"{{\"file_path\":{J(TempSeqFile)},\"sequence_name\":\"ASeq\"," +
                $"\"variable_name\":\"NumArr\",\"index\":{i},\"value\":\"{(i + 1) * 10}\"}}");
            Assert.That(set.IsError, Is.False, TextOf(set));
        }

        var get = await Call("get_array_variable",
            $"{{\"file_path\":{J(TempSeqFile)},\"sequence_name\":\"ASeq\"," +
            "\"variable_name\":\"NumArr\"}");
        Assert.That(get.IsError, Is.False, TextOf(get));

        var arr = Doc(get);
        Assert.That(arr.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(arr.GetArrayLength(), Is.EqualTo(3));
        Assert.That(arr[0].GetProperty("value").GetDouble(), Is.EqualTo(10.0).Within(1e-9));
        Assert.That(arr[2].GetProperty("value").GetDouble(), Is.EqualTo(30.0).Within(1e-9));
    }

    // ── array file global (insert_file_global "number[]" → array tools) ─────────

    [Test]
    public async Task ArrayFileGlobal_Resize_SetElement_Get_RoundTrips()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        // insert_file_global now honours the "[]" suffix → a real array file global, which the
        // array tools address by omitting sequence_name.
        await Ts.InsertFileGlobalAsync(TempSeqFile, "FgArr", "number[]");

        var resize = await Call("resize_array_variable",
            $"{{\"file_path\":{J(TempSeqFile)},\"variable_name\":\"FgArr\",\"new_size\":2}}");
        Assert.That(resize.IsError, Is.False, TextOf(resize));

        var set = await Call("set_array_element",
            $"{{\"file_path\":{J(TempSeqFile)},\"variable_name\":\"FgArr\"," +
            "\"index\":1,\"value\":\"55\"}");
        Assert.That(set.IsError, Is.False, TextOf(set));

        var get = await Call("get_array_variable",
            $"{{\"file_path\":{J(TempSeqFile)},\"variable_name\":\"FgArr\"}}");
        Assert.That(get.IsError, Is.False, TextOf(get));
        var arr = Doc(get);
        Assert.That(arr.GetArrayLength(), Is.EqualTo(2));
        Assert.That(arr[1].GetProperty("value").GetDouble(), Is.EqualTo(55.0).Within(1e-9));
    }

    // ── delete_data_type ──────────────────────────────────────────────────────────

    [Test]
    public async Task DeleteDataType_RemovesType()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.CreateDataTypeAsync(TempSeqFile, "MyType", "Object");

        // create → list is now coherent: the freshly created type is listed by get_data_types.
        var before = await Ts.GetDataTypesAsync(TempSeqFile);
        Assert.That(before.Any(t => t.Name == "MyType"), Is.True,
            "Created data type should be listed by get_data_types");

        var r = await Call("delete_data_type",
            $"{{\"file_path\":{J(TempSeqFile)},\"type_name\":\"MyType\"}}");
        Assert.That(r.IsError, Is.False, TextOf(r));

        var after = await Ts.GetDataTypesAsync(TempSeqFile);
        Assert.That(after.Any(t => t.Name == "MyType"), Is.False,
            "Type should be gone after deletion");
    }

    // ── insert_steps_bulk ──────────────────────────────────────────────────────────

    [Test]
    public async Task InsertStepsBulk_AppendsAllSteps()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "BulkSeq");

        var json =
            $"{{\"sequence_file_path\":{J(TempSeqFile)},\"sequence_name\":\"BulkSeq\"," +
            "\"step_group\":\"Main\",\"steps\":[" +
            "{\"step_name\":\"S1\",\"step_type\":\"Statement\"}," +
            "{\"step_name\":\"If1\",\"step_type\":\"NI_Flow_If\",\"expression\":\"True\"}," +
            "{\"step_name\":\"S2\",\"step_type\":\"Statement\"}," +
            "{\"step_name\":\"End1\",\"step_type\":\"NI_Flow_End\"}]}";

        var r = await Call("insert_steps_bulk", json);
        Assert.That(r.IsError, Is.False, TextOf(r));
        Assert.That(Doc(r).GetProperty("insertedCount").GetInt32(), Is.EqualTo(4));

        var steps = await Ts.GetStepsAsync(TempSeqFile, "BulkSeq");
        Assert.That(steps.Select(s => s.Name),
            Is.SupersetOf(new[] { "S1", "If1", "S2", "End1" }));
    }

    // ── validate_sequence_plan (engine-free; valid + invalid) ─────────────────────

    [Test]
    public async Task ValidateSequencePlan_ValidPlan_ReportsValid()
    {
        var json =
            "{\"sequence_name\":\"P\",\"steps\":[" +
            "{\"step_name\":\"If1\",\"step_type\":\"NI_Flow_If\",\"expression\":\"True\"}," +
            "{\"step_name\":\"S1\",\"step_type\":\"Statement\"}," +
            "{\"step_name\":\"End1\",\"step_type\":\"NI_Flow_End\"}]}";

        var r = await Call("validate_sequence_plan", json);
        Assert.That(r.IsError, Is.False, TextOf(r));
        var doc = Doc(r);
        Assert.That(doc.GetProperty("valid").GetBoolean(), Is.True, TextOf(r));
        Assert.That(doc.GetProperty("errorCount").GetInt32(), Is.EqualTo(0));
    }

    [Test]
    public async Task ValidateSequencePlan_ForbiddenGoto_ReportsInvalid()
    {
        var json =
            "{\"sequence_name\":\"P\",\"steps\":[" +
            "{\"step_name\":\"G\",\"step_type\":\"Goto\"}]}";

        var r = await Call("validate_sequence_plan", json);
        Assert.That(r.IsError, Is.False, TextOf(r));
        var doc = Doc(r);
        Assert.That(doc.GetProperty("valid").GetBoolean(), Is.False);
        Assert.That(doc.GetProperty("errorCount").GetInt32(), Is.GreaterThan(0));
    }

    // ── set_step_module_path (LabVIEW VIPath) ─────────────────────────────────────

    [Test]
    public async Task SetStepModulePath_PersistsViaModuleInfo()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "MSeq");
        await Ts.InsertStepAsync(TempSeqFile, "MSeq", "Main", "Action", "LvStep");
        // Give the step a LabVIEW module so it carries a VIPath property.
        await Ts.ConfigureLabViewModuleAsync(TempSeqFile, "MSeq", "Main", "LvStep",
            @"C:\Dummy\Old.vi", save: true);

        var r = await Call("set_step_module_path",
            $"{{\"sequence_file_path\":{J(TempSeqFile)},\"sequence_name\":\"MSeq\"," +
            $"\"step_group\":\"Main\",\"step_name\":\"LvStep\"," +
            $"\"module_path\":{J(@"C:\Dummy\New.vi")}}}");
        Assert.That(r.IsError, Is.False, TextOf(r));

        var info = await Call("get_step_module_info",
            $"{{\"file_path\":{J(TempSeqFile)},\"sequence_name\":\"MSeq\"," +
            $"\"step_group\":\"Main\",\"step_name\":\"LvStep\"}}");
        Assert.That(info.IsError, Is.False, TextOf(info));
        Assert.That(TextOf(info), Does.Contain("New.vi"),
            "Updated VIPath should be visible in the module info");
    }

    // ── get_step_module_info (SequenceCall) ───────────────────────────────────────

    [Test]
    public async Task GetStepModuleInfo_ForSequenceCall_ReturnsStepName()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "CallerSeq");
        await Ts.InsertSequenceAsync(TempSeqFile, "TargetSeq");
        await Ts.InsertStepAsync(TempSeqFile, "CallerSeq", "Main", "SequenceCall", "CallStep");
        await Ts.ConfigureSequenceCallModuleAsync(TempSeqFile, "CallerSeq", "Main",
            "CallStep", "TargetSeq", targetSequenceFile: "", save: true);

        var r = await Call("get_step_module_info",
            $"{{\"file_path\":{J(TempSeqFile)},\"sequence_name\":\"CallerSeq\"," +
            $"\"step_group\":\"Main\",\"step_name\":\"CallStep\"}}");
        Assert.That(r.IsError, Is.False, TextOf(r));
        Assert.That(Doc(r).GetProperty("stepName").GetString(), Is.EqualTo("CallStep"));
    }

    // ── get_module_parameters (DLL module) ────────────────────────────────────────

    [Test]
    public async Task GetModuleParameters_ReturnsArray()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "PSeq");
        await Ts.InsertStepAsync(TempSeqFile, "PSeq", "Main", "Statement", "DllStep");
        await Ts.ConfigureDllModuleAsync(TempSeqFile, "PSeq", "Main", "DllStep",
            @"C:\Dummy\lib.dll", "MyFunc", save: true);

        var r = await Call("get_module_parameters",
            $"{{\"file_path\":{J(TempSeqFile)},\"sequence_name\":\"PSeq\"," +
            $"\"step_group\":\"Main\",\"step_name\":\"DllStep\"}}");
        Assert.That(r.IsError, Is.False, TextOf(r));
        Assert.That(Doc(r).ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    // ── set_module_parameter (insert-if-missing on a SequenceCall) ────────────────

    [Test]
    public async Task SetModuleParameter_HappyOrClearError()
    {
        // A SequenceCall whose target declares a parameter is the closest a headless engine
        // gets to a step with a real, named module parameter (DLL/.NET parameters need an
        // actual prototype). If the binding materialises, set its value; otherwise the tool
        // must surface a clear, structured error — either way the full dispatch path of
        // set_module_parameter (registration → schema → arg extraction → handler → service)
        // is exercised.
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "Tgt");
        await Ts.InsertSequenceParameterAsync(TempSeqFile, "Tgt", "MyArg", "number");
        await Ts.InsertSequenceAsync(TempSeqFile, "CSeq");
        await Ts.InsertStepAsync(TempSeqFile, "CSeq", "Main", "SequenceCall", "Call1");
        await Ts.ConfigureSequenceCallModuleAsync(TempSeqFile, "CSeq", "Main",
            "Call1", "Tgt", targetSequenceFile: "", save: true);

        var paramsRes = await Call("get_module_parameters",
            $"{{\"file_path\":{J(TempSeqFile)},\"sequence_name\":\"CSeq\"," +
            "\"step_group\":\"Main\",\"step_name\":\"Call1\"}");
        Assert.That(paramsRes.IsError, Is.False, TextOf(paramsRes));
        var listed = Doc(paramsRes);

        var paramName = listed.ValueKind == JsonValueKind.Array && listed.GetArrayLength() > 0
            ? listed[0].GetProperty("name").GetString()
            : null;

        if (!string.IsNullOrEmpty(paramName))
        {
            var ok = await Call("set_module_parameter",
                $"{{\"file_path\":{J(TempSeqFile)},\"sequence_name\":\"CSeq\"," +
                $"\"step_group\":\"Main\",\"step_name\":\"Call1\"," +
                $"\"parameter_name\":{J(paramName!)},\"value\":\"123\",\"use_expression\":true}}");
            Assert.That(ok.IsError, Is.False, TextOf(ok));
        }
        else
        {
            // No materialised parameter binding headless — verify the clear error path.
            var err = await Call("set_module_parameter",
                $"{{\"file_path\":{J(TempSeqFile)},\"sequence_name\":\"CSeq\"," +
                "\"step_group\":\"Main\",\"step_name\":\"Call1\"," +
                "\"parameter_name\":\"__nope__\",\"value\":\"1\",\"use_expression\":true}");
            Assert.That(err.IsError, Is.True,
                "Setting a non-existent module parameter should report a clear error");
            Assert.That(TextOf(err), Does.Contain("Could not set module parameter"));
            TestContext.WriteLine(
                "No materialised SequenceCall parameter binding headless — verified error path.");
        }
    }

    // ── get_step_templates ─────────────────────────────────────────────────────────

    [Test]
    public async Task GetStepTemplates_ReturnsArray()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);

        var r = await Call("get_step_templates", $"{{\"file_path\":{J(TempSeqFile)}}}");
        Assert.That(r.IsError, Is.False, TextOf(r));
        Assert.That(Doc(r).ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    // ── insert_step_from_template (adaptive: happy path if a template exists) ─────

    [Test]
    public async Task InsertStepFromTemplate_HappyOrClearError()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "TSeq");

        var templates = await Ts.GetStepTemplatesAsync(TempSeqFile);
        if (templates.Count > 0)
        {
            var name = templates[0].Name;
            var r = await Call("insert_step_from_template",
                $"{{\"file_path\":{J(TempSeqFile)},\"sequence_name\":\"TSeq\"," +
                $"\"step_group\":\"Main\",\"template_name\":{J(name)}," +
                $"\"new_step_name\":\"FromTpl\"}}");
            Assert.That(r.IsError, Is.False, TextOf(r));

            var steps = await Ts.GetStepsAsync(TempSeqFile, "TSeq");
            Assert.That(steps.Any(s => s.Name == "FromTpl"), Is.True,
                "Step inserted from template should be present");
        }
        else
        {
            // No templates on this station — a bogus name must surface a clear error,
            // which still proves the tool's dispatch + argument extraction path.
            var r = await Call("insert_step_from_template",
                $"{{\"file_path\":{J(TempSeqFile)},\"sequence_name\":\"TSeq\"," +
                "\"step_group\":\"Main\",\"template_name\":\"__nope__\"," +
                "\"new_step_name\":\"FromTpl\"}");
            Assert.That(r.IsError, Is.True,
                "A non-existent template name should produce an error result");
            Assert.That(TextOf(r), Is.Not.Empty);
            TestContext.WriteLine("No step templates on this station — verified error path.");
        }
    }

    // ── configure_message_popup ──────────────────────────────────────────────────

    [Test]
    public async Task ConfigureMessagePopup_PersistsSettings()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "MpSeq");
        await Ts.InsertStepAsync(TempSeqFile, "MpSeq", "Main", "MessagePopup", "Popup1");

        var r = await Call("configure_message_popup",
            $"{{\"file_path\":{J(TempSeqFile)},\"sequence_name\":\"MpSeq\"," +
            "\"step_group\":\"Main\",\"step_name\":\"Popup1\"," +
            "\"message\":\"Connect the DUT\",\"title\":\"Action Required\"," +
            "\"buttons\":\"OKCancel\",\"timeout\":5}");
        Assert.That(r.IsError, Is.False, TextOf(r));

        // Readback: MessagePopup settings persist as TOP-LEVEL step properties (MessageExpr/
        // TitleExpr expression literals, ButtonNLabel, TimeToWait) — not the old TS.MessagePopup.*.
        var svc = (TestStandService)Ts;
        Assert.That(svc.ReadStepPropertyString(TempSeqFile, "MpSeq", "Main", "Popup1", "MessageExpr"),
            Does.Contain("Connect the DUT"), "message must persist as MessageExpr");
        Assert.That(svc.ReadStepPropertyString(TempSeqFile, "MpSeq", "Main", "Popup1", "TitleExpr"),
            Does.Contain("Action Required"), "title must persist as TitleExpr");
        Assert.That(svc.ReadStepPropertyString(TempSeqFile, "MpSeq", "Main", "Popup1", "Button1Label"),
            Does.Contain("OK"), "OKCancel → Button1Label = OK");
        Assert.That(svc.ReadStepPropertyString(TempSeqFile, "MpSeq", "Main", "Popup1", "Button2Label"),
            Does.Contain("Cancel"), "OKCancel → Button2Label = Cancel");
        Assert.That(svc.ReadStepPropertyNumber(TempSeqFile, "MpSeq", "Main", "Popup1", "TimeToWait"),
            Is.EqualTo(5).Within(0.001), "timeout must persist as TimeToWait (seconds)");
    }

    // ── configure_property_loader ─────────────────────────────────────────────────

    [Test]
    public async Task ConfigurePropertyLoader_PersistsPath_OnRealLoaderStep()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "PlSeq");
        await Ts.InsertStepAsync(TempSeqFile, "PlSeq", "Main", "NI_PropertyLoader", "Loader1");

        var r = await Call("configure_property_loader",
            $"{{\"file_path\":{J(TempSeqFile)},\"sequence_name\":\"PlSeq\"," +
            "\"step_group\":\"Main\",\"step_name\":\"Loader1\"," +
            $"\"file_path_expr\":{J(@"C:\config.ini")},\"mode\":\"Read\"}}");
        Assert.That(r.IsError, Is.False, TextOf(r));

        // Readback: path persists in the first source's Location (not the old TS.PropertyLoader.*).
        var svc = (TestStandService)Ts;
        Assert.That(svc.ReadStepPropertyString(TempSeqFile, "PlSeq", "Main", "Loader1",
                "PropertyLoaderSources[0].Options.CommonOptions.Source.Location"),
            Does.Contain("config.ini"), "file path must persist in the source Location");
    }

    [Test]
    public async Task ConfigurePropertyLoader_OnNonLoaderStep_ReportsClearError()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "PlSeq2");
        await Ts.InsertStepAsync(TempSeqFile, "PlSeq2", "Main", "Action", "NotALoader");

        var r = await Call("configure_property_loader",
            $"{{\"file_path\":{J(TempSeqFile)},\"sequence_name\":\"PlSeq2\"," +
            "\"step_group\":\"Main\",\"step_name\":\"NotALoader\"," +
            $"\"file_path_expr\":{J(@"C:\x.ini")},\"mode\":\"Read\"}}");
        Assert.That(r.IsError, Is.True,
            "configuring a non-PropertyLoader step must report a clear error, not swallow it");
        Assert.That(TextOf(r), Does.Contain("NI_PropertyLoader"),
            "error should hint at the correct step type");
    }

    // ── compare_sequence_files ────────────────────────────────────────────────────

    [Test]
    public async Task CompareSequenceFiles_ReportsSequenceOnlyInOne()
    {
        var file2 = Path.Combine(Path.GetTempPath(),
            $"TS_T22_cmp_{Guid.NewGuid():N}.seq");
        try
        {
            await Ts.CreateSequenceFileAsync(TempSeqFile);
            await Ts.InsertSequenceAsync(TempSeqFile, "OnlyInOne");

            await Ts.CreateSequenceFileAsync(file2);

            var r = await Call("compare_sequence_files",
                $"{{\"file_path_1\":{J(TempSeqFile)},\"file_path_2\":{J(file2)}}}");
            Assert.That(r.IsError, Is.False, TextOf(r));

            var doc  = Doc(r);
            var only1 = doc.GetProperty("sequencesOnlyInFile1")
                           .EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.That(only1, Does.Contain("OnlyInOne"));
        }
        finally
        {
            try { await Ts.CloseSequenceFileAsync(file2); } catch { }
            try { if (File.Exists(file2)) File.Delete(file2); } catch { }
        }
    }

    // ── cancel_undo_group ──────────────────────────────────────────────────────────

    [Test]
    public async Task CancelUndoGroup_Succeeds()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.BeginUndoGroupAsync("T22Group", TempSeqFile);

        var r = await Call("cancel_undo_group",
            $"{{\"file_path\":{J(TempSeqFile)}}}");
        Assert.That(r.IsError, Is.False, TextOf(r));
    }

    // ── find_file ────────────────────────────────────────────────────────────────

    [Test]
    public async Task FindFile_RunsWithoutError()
    {
        // Use a name guaranteed NOT to be on the search path: FindFileAsync disables the
        // engine's "locate file" / "add to search list" prompts, so a not-found file returns
        // "" headlessly instead of popping a modal dialog that would block the test. This
        // verifies the dispatch + argument path AND the no-dialog contract, without depending
        // on any real installed sequence file (e.g. NI_StandardModelCallbacks.seq).
        var r = await Call("find_file", "{\"filename\":\"__nonexistent_T22_probe.seq\"}");
        Assert.That(r.IsError, Is.False, TextOf(r));
    }

    // ── Metadata-driven dispatch smoke test (covers the arg-extraction path of ALL
    //    tools that declare required args, in one shot) ────────────────────────────

    [Test]
    public async Task EveryRequiredArgTool_MissingArgs_YieldsStructuredError()
    {
        var failures = new List<string>();

        foreach (var tool in _registry.GetTools())
        {
            // Only tools with >=1 required arg are safe to probe: their handlers extract the
            // required args first, so an empty payload yields a structured error BEFORE any
            // engine side effect. Tools with no required args would actually execute (some are
            // destructive, e.g. disconnect_engine / abort_all) — skip those.
            if (!tool.InputSchema.TryGetProperty("required", out var req) ||
                req.ValueKind != JsonValueKind.Array || req.GetArrayLength() == 0)
                continue;

            CallToolResult r;
            try
            {
                r = await _registry.CallToolAsync(tool.Name, Args("{}"));
            }
            catch (Exception ex)
            {
                // CallToolAsync is supposed to trap handler exceptions and return an error
                // result — an escaping exception is itself a defect.
                failures.Add($"{tool.Name}: threw {ex.GetType().Name} instead of an error result");
                continue;
            }

            if (!r.IsError)
                failures.Add($"{tool.Name}: empty args produced a success result " +
                             "(schema marks args as required)");
            else if (string.IsNullOrWhiteSpace(TextOf(r)))
                failures.Add($"{tool.Name}: error result had empty text");
        }

        Assert.That(failures, Is.Empty,
            "Tools mishandling missing required args:\n  " + string.Join("\n  ", failures));
    }
}
