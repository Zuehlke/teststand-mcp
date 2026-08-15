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
/// End-to-end MCP-wiring tests for the new tools: they exercise the full path
/// (JSON arguments → TestStandToolRegistry.CallToolAsync → handler → service → engine
/// → JSON result), not just the service layer, so the tool registration, schema and
/// argument extraction are all covered.
/// </summary>
[TestFixture]
[Category("PropertyObject")]
public class T21_NewToolDispatchTests : TestBase
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

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;
    private static string      TextOf(CallToolResult r) => r.Content[0].Text;
    private static string      J(string s) => JsonSerializer.Serialize(s);

    [Test]
    public void NewTools_AreRegistered()
    {
        var names = _registry.GetTools().Select(t => t.Name).ToList();
        Assert.That(names, Does.Contain("evaluate_expression"));
        Assert.That(names, Does.Contain("get_property_object"));
        Assert.That(names, Does.Contain("set_property_value"));
        Assert.That(names, Does.Contain("delete_sub_property"));
    }

    [Test]
    public async Task SetWaitTime_Tool_RegisteredAndConfiguresWait()
    {
        // Covers the full MCP path for the new tool: registration + schema + arg extraction +
        // handler → service → engine (the service method alone is covered in T25).
        Assert.That(_registry.GetTools().Select(t => t.Name), Does.Contain("set_wait_time"));

        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertStepAsync(TempSeqFile, "MainSequence", "Main", "NI_Wait", "W");

        var r = await _registry.CallToolAsync("set_wait_time",
            Args("{\"file_path\":" + J(TempSeqFile) +
                 ",\"sequence_name\":\"MainSequence\",\"step_group\":\"Main\"," +
                 "\"step_name\":\"W\",\"time_expression\":\"1.5\"}"));
        Assert.That(r.IsError, Is.False, TextOf(r));

        await Ts.SaveSequenceFileAsync(TempSeqFile);
        var result = await Ts.RunSequenceAsync(TempSeqFile, "MainSequence", null, 30);
        Assert.That(result.ElapsedSeconds, Is.GreaterThanOrEqualTo(1.3),
            "set_wait_time via the full tool path must configure a real NI_Wait duration");
    }

    [Test]
    public async Task AddCallbackOverride_Tool_AddsCallbackWithDefaultSteps()
    {
        Assert.That(_registry.GetTools().Select(t => t.Name), Does.Contain("add_callback_override"));

        await Ts.CreateSequenceFileAsync(TempSeqFile);
        var r = await _registry.CallToolAsync("add_callback_override",
            Args("{\"file_path\":" + J(TempSeqFile) + ",\"callback_name\":\"PreUUT\"}"));
        Assert.That(r.IsError, Is.False, TextOf(r));

        // The override must exist and carry the model's default "Call DoPreUUT" step (copy defaults).
        var steps = await Ts.GetStepsAsync(TempSeqFile, "PreUUT");
        Assert.That(steps.Any(s => s.Name.Contains("DoPreUUT")), Is.True,
            "Override should include the default 'Call DoPreUUT' step");
    }

    // ── .NET instance methods over the full MCP path (issue #37 follow-up) ──────────
    // The T13 tests call the SERVICE directly, so they cannot catch a mismatch between the
    // advertised schema name and the name the handler reads — an MCP client would send
    // "create_object" and silently get the default. These two close that gap.

    private static string FixtureAssembly => typeof(DotNetTestAssembly.MathOps).Assembly.Location;
    private const string InstanceOpsClass = "TestStandMCP.DotNetTestAssembly.InstanceOps";

    [Test]
    public void ConfigureDotNetModule_Schema_AdvertisesTheObjectLifecycleArguments()
    {
        var tool = _registry.GetTools().Single(t => t.Name == "configure_dotnet_module");
        var props = tool.InputSchema.GetProperty("properties");

        Assert.Multiple(() =>
        {
            Assert.That(props.TryGetProperty("create_object", out _), Is.True,
                "a client can only pass what the schema advertises");
            // NOT "constructor": that name makes the client reject the whole tools/list.
            Assert.That(props.TryGetProperty("constructor_signature", out _), Is.True);
            Assert.That(props.TryGetProperty("constructor", out _), Is.False,
                "a JavaScript prototype key as a parameter name empties the tool catalogue");
            Assert.That(props.TryGetProperty("dispose_object", out _), Is.True);
        });
    }

    /// <summary>
    /// The whole chain exactly as a client drives it: configure with create_object, bind both
    /// arguments by their prefixed names, run — and the constructed object's Triple(4) has to write
    /// 12. Nothing here bypasses the tool layer.
    /// </summary>
    [Test]
    public async Task ConfigureDotNetModule_InstanceMethod_OverTheToolPath_Executes()
    {
        string probe = "MCP_DispatchProbe_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
        await Ts.SetStationGlobalAsync(probe, 0);
        try
        {
            await Ts.CreateSequenceFileAsync(TempSeqFile);
            await Ts.InsertStepAsync(TempSeqFile, "MainSequence", "Main", "Action", "Inst");

            string common = "{\"file_path\":" + J(TempSeqFile) +
                            ",\"sequence_name\":\"MainSequence\",\"step_group\":\"Main\"," +
                            "\"step_name\":\"Inst\"";

            var cfg = await _registry.CallToolAsync("configure_dotnet_module",
                Args(common + ",\"assembly_path\":" + J(FixtureAssembly) +
                     ",\"class_name\":" + J(InstanceOpsClass) +
                     ",\"method_name\":\"Triple\",\"create_object\":true}"));
            Assert.That(cfg.IsError, Is.False, TextOf(cfg));

            var applied = JsonDocument.Parse(TextOf(cfg)).RootElement.GetProperty("appliedSettings");
            Assert.That(applied.GetProperty("resolvedVia").GetString(), Is.EqualTo("call-chain"),
                "create_object must reach the handler — a default here means the argument name is wrong");
            Assert.That(applied.GetProperty("constructorSignature").GetString(), Is.EqualTo("InstanceOps()"));

            foreach (var (name, value) in new[] { ("Triple.a", "4"), ("Triple.Return Value", $"StationGlobals.{probe}") })
            {
                var bind = await _registry.CallToolAsync("set_module_parameter",
                    Args(common + ",\"parameter_name\":" + J(name) + ",\"value\":" + J(value) + "}"));
                Assert.That(bind.IsError, Is.False, TextOf(bind));
            }

            var run = await Ts.RunSequenceAsync(TempSeqFile, "MainSequence", null, 60);
            TestContext.WriteLine($"status={run.Status} result={run.Result}");

            var globals = await Ts.GetStationGlobalsAsync();
            Assert.That(System.Convert.ToDouble(globals.First(g => g.Name == probe).Value),
                Is.EqualTo(12.0).Within(1e-9),
                "driven purely through the MCP tool layer, the constructed object's Triple(4) must write 12");
        }
        finally { await Ts.DeleteStationGlobalAsync(probe); }
    }

    [Test]
    public async Task EvaluateExpression_Tool_ReturnsComputedValue()
    {
        var r = await _registry.CallToolAsync("evaluate_expression",
            Args("{\"expression\":\"6 * 7\"}"));

        Assert.That(r.IsError, Is.False, TextOf(r));
        var doc = JsonDocument.Parse(TextOf(r)).RootElement;
        Assert.That(doc.GetProperty("isValid").GetBoolean(), Is.True);
        Assert.That(doc.GetProperty("value").GetDouble(), Is.EqualTo(42.0).Within(1e-9));
    }

    [Test]
    public async Task SetThenGetPropertyObject_Tools_RoundTrip()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);

        var setRes = await _registry.CallToolAsync("set_property_value",
            Args("{\"file_path\":" + J(TempSeqFile) +
                 ",\"property_name\":\"DispNum\",\"value_type\":\"number\",\"value\":\"12.25\"}"));
        Assert.That(setRes.IsError, Is.False, TextOf(setRes));

        var getRes = await _registry.CallToolAsync("get_property_object",
            Args("{\"file_path\":" + J(TempSeqFile) + ",\"property_name\":\"DispNum\"}"));
        Assert.That(getRes.IsError, Is.False, TextOf(getRes));

        var doc = JsonDocument.Parse(TextOf(getRes)).RootElement;
        Assert.That(doc.GetProperty("valueType").GetString(), Is.EqualTo("Number"));
        Assert.That(doc.GetProperty("value").GetDouble(), Is.EqualTo(12.25).Within(1e-9));
    }

    [Test]
    public async Task DeleteSubProperty_Tool_RemovesProperty()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await _registry.CallToolAsync("set_property_value",
            Args("{\"file_path\":" + J(TempSeqFile) +
                 ",\"property_name\":\"Gone\",\"value_type\":\"string\",\"value\":\"x\"}"));

        var delRes = await _registry.CallToolAsync("delete_sub_property",
            Args("{\"file_path\":" + J(TempSeqFile) + ",\"property_name\":\"Gone\"}"));
        Assert.That(delRes.IsError, Is.False, TextOf(delRes));

        var globals = await Ts.GetFileGlobalsAsync(TempSeqFile);
        Assert.That(globals.Exists(v => v.Name == "Gone"), Is.False);
    }
}
