using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using TestStandMCP.Models;
using TestStandMCP.Services;
using TestStandMCP.Tools;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// Pure (engine-free) regression tests covering the risks introduced by this session's
/// refactor. Each region targets one change:
///   • JsonElementExtensions — the rewritten <c>GetBoolOrDefault</c> (ternary → switch) and the
///     new <c>GetDoubleOrDefault</c> helper that replaced 4 inline timeout ternaries.
///   • SchemaBuilder — the cached <see cref="JsonSerializerOptions"/> must still emit camelCase
///     and omit null members (WhenWritingNull).
///   • Models — owned collections were changed to <c>{ get; init; }</c>; this must not break
///     System.Text.Json serialization OR round-trip deserialization.
///   • SequencePlanValidator — UnlinkedSequenceCalls is now tallied in the single main loop.
///   • SequenceEditorService — Process handles are disposed and the IsRunning catch was narrowed;
///     it must stay non-throwing and stable across repeated calls.
/// Does NOT inherit TestBase, so no TestStand engine is required.
/// </summary>
[TestFixture]
[Category("ChangeRisk")]
public class T23_ChangeRiskUnitTests
{
    // JsonDocument is intentionally not disposed: the returned RootElement must outlive this call
    // for the test body, and the documents are tiny and GC-collected at test end.
    private static JsonElement El(string json) => JsonDocument.Parse(json).RootElement;

    private static readonly JsonSerializerOptions AppLikeOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ── JsonElementExtensions: GetBoolOrDefault (rewritten as a switch) ───────────

    [Test]
    public void GetBoolOrDefault_TrueAndFalse_ReturnTheJsonBoolean()
    {
        Assert.That(El("""{"flag":true}""").GetBoolOrDefault("flag", false), Is.True);
        Assert.That(El("""{"flag":false}""").GetBoolOrDefault("flag", true), Is.False);
    }

    [Test]
    public void GetBoolOrDefault_MissingKey_ReturnsDefault_BothWays()
    {
        Assert.That(El("""{}""").GetBoolOrDefault("flag", true), Is.True);
        Assert.That(El("""{}""").GetBoolOrDefault("flag", false), Is.False);
    }

    [Test]
    public void GetBoolOrDefault_NonBooleanValue_ReturnsDefault()
    {
        // Only a real JSON boolean counts — a string "true" or a number must fall back.
        Assert.That(El("""{"flag":"true"}""").GetBoolOrDefault("flag", false), Is.False);
        Assert.That(El("""{"flag":1}""").GetBoolOrDefault("flag", false), Is.False);
        Assert.That(El("""{"flag":null}""").GetBoolOrDefault("flag", true), Is.True);
    }

    // ── JsonElementExtensions: GetDoubleOrDefault (new helper for timeouts) ────────

    [Test]
    public void GetDoubleOrDefault_ReadsFractionalAndIntegerNumbers()
    {
        Assert.That(El("""{"timeout_seconds":5.5}""").GetDoubleOrDefault("timeout_seconds", 30), Is.EqualTo(5.5));
        Assert.That(El("""{"timeout_seconds":10}""").GetDoubleOrDefault("timeout_seconds", 30), Is.EqualTo(10.0));
        // -1 is the documented "infinite" sentinel and must survive unchanged.
        Assert.That(El("""{"timeout_seconds":-1}""").GetDoubleOrDefault("timeout_seconds", 30), Is.EqualTo(-1.0));
    }

    [Test]
    public void GetDoubleOrDefault_MissingOrNonNumeric_ReturnsDefault()
    {
        Assert.That(El("""{}""").GetDoubleOrDefault("timeout_seconds", 30), Is.EqualTo(30.0));
        Assert.That(El("""{"timeout_seconds":"x"}""").GetDoubleOrDefault("timeout_seconds", 30), Is.EqualTo(30.0));
        Assert.That(El("""{"timeout_seconds":true}""").GetDoubleOrDefault("timeout_seconds", 7), Is.EqualTo(7.0));
    }

    // ── JsonElementExtensions: the other parsers used across the dispatcher ───────

    [Test]
    public void GetIntOrDefault_ReadsIntOrFallsBack()
    {
        Assert.That(El("""{"n":42}""").GetIntOrDefault("n", 0), Is.EqualTo(42));
        Assert.That(El("""{}""").GetIntOrDefault("n", 99), Is.EqualTo(99));
        Assert.That(El("""{"n":"x"}""").GetIntOrDefault("n", 99), Is.EqualTo(99));
    }

    [Test]
    public void GetRequiredString_ReturnsValue_OrThrowsClearError()
    {
        Assert.That(El("""{"name":"abc"}""").GetRequiredString("name"), Is.EqualTo("abc"));

        var missing = Assert.Throws<ArgumentException>(() => El("""{}""").GetRequiredString("name"));
        Assert.That(missing!.Message, Does.Contain("name"));

        // present-but-null must also be rejected (the dispatcher relies on this).
        Assert.Throws<ArgumentException>(() => El("""{"name":null}""").GetRequiredString("name"));
    }

    [Test]
    public void GetStringOrDefault_And_GetStringOrNull_Behave()
    {
        Assert.That(El("""{"s":"v"}""").GetStringOrDefault("s", "def"), Is.EqualTo("v"));
        Assert.That(El("""{}""").GetStringOrDefault("s", "def"), Is.EqualTo("def"));
        Assert.That(El("""{"s":"v"}""").GetStringOrNull("s"), Is.EqualTo("v"));
        Assert.That(El("""{}""").GetStringOrNull("s"), Is.Null);
    }

    [Test]
    public void GetDictionaryOrNull_MapsScalarsAndReturnsNullWhenAbsent()
    {
        var dict = El("""{"p":{"num":2,"flagT":true,"flagF":false,"str":"x"}}""").GetDictionaryOrNull("p");
        Assert.That(dict, Is.Not.Null);
        Assert.That(dict!["num"], Is.EqualTo(2.0));      // numbers map to double
        Assert.That(dict["flagT"], Is.EqualTo(true));
        Assert.That(dict["flagF"], Is.EqualTo(false));
        Assert.That(dict["str"], Is.EqualTo("x"));

        Assert.That(El("""{}""").GetDictionaryOrNull("p"), Is.Null);
        Assert.That(El("""{"p":"notObject"}""").GetDictionaryOrNull("p"), Is.Null);
    }

    // ── SchemaBuilder: cached options must keep camelCase + null-omission ─────────

    [Test]
    public void SchemaBuilder_EmitsCamelCase_AndOmitsNullMembers()
    {
        var schema = SchemaBuilder.Build(s => s
            .AddRequired("name", "string", "The name")
            .AddOptional("count", "integer", "How many", 5));

        Assert.That(schema.GetProperty("type").GetString(), Is.EqualTo("object"));

        var props = schema.GetProperty("properties");
        var name  = props.GetProperty("name");
        Assert.That(name.GetProperty("type").GetString(), Is.EqualTo("string"));
        Assert.That(name.GetProperty("description").GetString(), Is.EqualTo("The name"));

        // WhenWritingNull: a property with no enum/default must not emit those members.
        Assert.That(name.TryGetProperty("enum", out _), Is.False, "null 'enum' must be omitted");
        Assert.That(name.TryGetProperty("default", out _), Is.False, "null 'default' must be omitted");

        // The optional property's default IS present.
        Assert.That(props.GetProperty("count").GetProperty("default").GetInt32(), Is.EqualTo(5));

        // 'required' contains the required field.
        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.That(required, Does.Contain("name"));
    }

    [Test]
    public void SchemaBuilder_ArrayProperty_HasItemSchema()
    {
        var schema = SchemaBuilder.Build(s => s
            .AddArray("steps", "The steps", item => item
                .AddRequired("name", "string", "Step name")));

        var steps = schema.GetProperty("properties").GetProperty("steps");
        Assert.That(steps.GetProperty("type").GetString(), Is.EqualTo("array"));
        Assert.That(steps.GetProperty("items").GetProperty("type").GetString(), Is.EqualTo("object"));
    }

    // ── MCP wire conformance (2026-08-15) ────────────────────────────────────────
    // The server fell out of the client's tool catalog while its process was healthy and
    // tools/list answered with all 252 tools. Three handshake-level defects were the
    // discriminator against the other local servers; these pin the fixes.

    [Test]
    public void SchemaBuilder_NoArgumentTool_StillEmitsAnEmptyPropertiesObject()
    {
        // A missing "properties" key makes the whole tool malformed for a strict validator.
        var schema = SchemaBuilder.Build(_ => { });

        Assert.That(schema.GetProperty("type").GetString(), Is.EqualTo("object"));
        Assert.That(schema.TryGetProperty("properties", out var props), Is.True,
            "an object schema must always carry a 'properties' key");
        Assert.That(props.ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(props.EnumerateObject().Count(), Is.Zero);
    }

    [Test]
    public void SchemaBuilder_ScalarArrayProperty_GetsAnItemSchema()
    {
        var schema = SchemaBuilder.Build(s => s
            .AddOptional("type_names", "array", "Names to copy")
            .AddRequired("ids", "array", "Ids", itemType: "number"));

        var props = schema.GetProperty("properties");
        Assert.That(props.GetProperty("type_names").GetProperty("items").GetProperty("type").GetString(),
            Is.EqualTo("string"), "an array without 'items' is an incomplete schema");
        Assert.That(props.GetProperty("ids").GetProperty("items").GetProperty("type").GetString(),
            Is.EqualTo("number"));
    }

    [Test]
    public void SchemaBuilder_RefusesAJavaScriptPrototypeKeyAsAParameterName()
    {
        // The client validates inputSchema.properties as a RECORD; an own "constructor" key
        // fails that check and the ENTIRE tools/list is discarded — every tool of the server
        // vanishes. Fail loudly at registration instead.
        foreach (var reserved in new[] { "constructor", "__proto__", "toString" })
        {
            Assert.That(() => SchemaBuilder.Build(s => s.AddOptional(reserved, "string", "x")),
                Throws.ArgumentException, $"'{reserved}' must be refused as a parameter name");
            Assert.That(() => SchemaBuilder.Build(s => s.AddRequired(reserved, "string", "x")),
                Throws.ArgumentException);
        }

        // A normal name still works.
        Assert.That(() => SchemaBuilder.Build(s => s.AddOptional("constructor_signature", "string", "x")),
            Throws.Nothing);
    }

    [Test]
    public void NoRegisteredTool_DeclaresAReservedParameterName()
    {
        using var editor = new SequenceEditorService(NullLogger<SequenceEditorService>.Instance);
        var registry = new TestStandToolRegistry(
            new TestStandService(NullLogger<TestStandService>.Instance), editor,
            NullLogger<TestStandToolRegistry>.Instance);

        var offenders = registry.GetTools()
            .SelectMany(t => t.InputSchema.GetProperty("properties").EnumerateObject()
                .Where(p => SchemaBuilder.ReservedPropertyNames.Contains(p.Name))
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        Assert.That(offenders, Is.Empty, string.Join(", ", offenders));
    }

    [Test]
    public void EveryRegisteredTool_HasAWellFormedInputSchema()
    {
        using var editor = new SequenceEditorService(NullLogger<SequenceEditorService>.Instance);
        var registry = new TestStandToolRegistry(
            new TestStandService(NullLogger<TestStandService>.Instance), editor,
            NullLogger<TestStandToolRegistry>.Instance);

        var problems = new List<string>();
        foreach (var tool in registry.GetTools())
        {
            var schema = tool.InputSchema;
            if (schema.GetProperty("type").GetString() != "object")
                problems.Add($"{tool.Name}: schema type is not 'object'");
            if (!schema.TryGetProperty("properties", out var props))
            {
                problems.Add($"{tool.Name}: no 'properties' key");
                continue;
            }
            foreach (var p in props.EnumerateObject())
            {
                if (!p.Value.TryGetProperty("type", out var t))
                {
                    problems.Add($"{tool.Name}.{p.Name}: no 'type'");
                    continue;
                }
                if (t.GetString() == "array" && !p.Value.TryGetProperty("items", out _))
                    problems.Add($"{tool.Name}.{p.Name}: array without 'items'");
            }
            if (schema.TryGetProperty("required", out var req))
                foreach (var r in req.EnumerateArray())
                    if (!props.TryGetProperty(r.GetString() ?? "", out _))
                        problems.Add($"{tool.Name}: required '{r.GetString()}' is not a property");
        }

        Assert.That(problems, Is.Empty, string.Join("; ", problems.Take(20)));
    }

    [Test]
    public void ProtocolVersion_EchoesTheClientsRevision_AndNeverPinsTheOldestOne()
    {
        Assert.That(McpProtocol.Negotiate("2025-06-18"),
            Is.EqualTo("2025-06-18"), "a supported revision must be echoed back");
        Assert.That(McpProtocol.Negotiate("2024-11-05"), Is.EqualTo("2024-11-05"));

        // Unknown or absent -> our newest, never a hard-coded old one.
        Assert.That(McpProtocol.Negotiate("2099-01-01"), Is.EqualTo(McpProtocol.Newest));
        Assert.That(McpProtocol.Negotiate(null),         Is.EqualTo(McpProtocol.Newest));
        Assert.That(McpProtocol.Newest, Is.Not.EqualTo("2024-11-05"));
    }

    // ── Models: { get; init; } collections must serialize AND round-trip ─────────

    [Test]
    public void ExecutionResult_WithInitCollection_RoundTrips()
    {
        var original = new ExecutionResult
        {
            ExecutionId = "exec-1",
            Status      = "Done",
            Result      = "Passed",
            StepResults =
            {
                new StepResult { StepName = "S1", Result = "Passed", MeasuredValue = 1.5 },
                new StepResult { StepName = "S2", Result = "Failed" }
            }
        };

        var json = JsonSerializer.Serialize(original, AppLikeOpts);
        Assert.That(json, Does.Contain("\"stepResults\""), "init collection must serialize (camelCase)");

        var back = JsonSerializer.Deserialize<ExecutionResult>(json, AppLikeOpts);
        Assert.That(back, Is.Not.Null);
        Assert.That(back!.StepResults, Has.Count.EqualTo(2), "init collection must deserialize");
        Assert.That(back.StepResults[0].StepName, Is.EqualTo("S1"));
        Assert.That(back.StepResults[0].MeasuredValue, Is.EqualTo(1.5));
        Assert.That(back.StepResults[1].Result, Is.EqualTo("Failed"));
    }

    [Test]
    public void SequenceFileInfo_MultipleInitCollections_RoundTrip()
    {
        var original = new SequenceFileInfo
        {
            FilePath = @"C:\x\Demo.seq",
            FileName = "Demo.seq",
            Sequences = { new SequenceInfo { Name = "MainSequence" } },
            FileGlobals = { new VariableInfo { Name = "G1", DataType = "Number" } }
        };

        var json = JsonSerializer.Serialize(original, AppLikeOpts);
        var back = JsonSerializer.Deserialize<SequenceFileInfo>(json, AppLikeOpts);

        Assert.That(back, Is.Not.Null);
        Assert.That(back!.Sequences.Select(s => s.Name), Does.Contain("MainSequence"));
        Assert.That(back.FileGlobals.Select(g => g.Name), Does.Contain("G1"));
        Assert.That(back.StationGlobals, Is.Empty, "an untouched init collection stays an empty list, not null");
    }

    [Test]
    public void ExecutionInfo_NullMembersOmitted_RequiredCamelCasePresent()
    {
        var info = new ExecutionInfo { ExecutionId = "e1", Status = "Running" };  // ErrorMessage null
        var json = JsonSerializer.Serialize(info, AppLikeOpts);

        Assert.That(json, Does.Contain("\"executionId\""));            // camelCase
        Assert.That(json, Does.Not.Contain("errorMessage"), "null member must be omitted");
    }

    // ── SequencePlanValidator: single-pass UnlinkedSequenceCalls tally ───────────

    [Test]
    public void Validator_CountsOnlyUnlinkedSequenceCalls()
    {
        var steps = new List<PlanStepInput>
        {
            new() { Name = "Linked",    StepType = "SequenceCall", TargetSequenceName = "Sub1" },
            new() { Name = "Unlinked1", StepType = "SequenceCall" },                       // no target
            new() { Name = "Unlinked2", StepType = "SequenceCall", TargetSequenceName = "" }, // blank target
            new() { Name = "Action",    StepType = "Statement" }
        };

        var r = SequencePlanValidator.Validate("Seq", steps, Array.Empty<string>());

        Assert.That(r.Valid, Is.True, string.Join(";", r.Errors.Select(e => e.Code)));
        Assert.That(r.Stats.UnlinkedSequenceCalls, Is.EqualTo(2), "only the 2 untargeted calls count");
        Assert.That(r.Warnings.Count(w => w.Code == "W_UNLINKED_CALLS"), Is.EqualTo(1));
    }

    [Test]
    public void Validator_NoUnlinkedCalls_NoWarning()
    {
        var steps = new List<PlanStepInput>
        {
            new() { Name = "Linked", StepType = "SequenceCall", TargetSequenceName = "Sub1" }
        };
        var r = SequencePlanValidator.Validate("Seq", steps, Array.Empty<string>());
        Assert.That(r.Stats.UnlinkedSequenceCalls, Is.EqualTo(0));
        Assert.That(r.Warnings.Any(w => w.Code == "W_UNLINKED_CALLS"), Is.False);
    }

    [Test]
    public void Validator_NestedFlowBlocks_TrackViaBlockProperties()
    {
        // Exercises the Block type (Type/Name/Index/ElseSeen now auto-properties) end-to-end.
        var steps = new List<PlanStepInput>
        {
            new() { Name = "If1",   StepType = "NI_Flow_If",   Expression = "True" },
            new() { Name = "While", StepType = "NI_Flow_While", Expression = "True" },
            new() { Name = "Brk",   StepType = "NI_Flow_Break" },
            new() { Name = "EndW",  StepType = "NI_Flow_End" },
            new() { Name = "Else1", StepType = "NI_Flow_Else" },
            new() { Name = "EndIf", StepType = "NI_Flow_End" }
        };
        var r = SequencePlanValidator.Validate("Seq", steps, Array.Empty<string>());
        Assert.That(r.Valid, Is.True, string.Join(";", r.Errors.Select(e => e.Code + ":" + e.Message)));
        Assert.That(r.Stats.MaxNestingDepth, Is.EqualTo(2));
    }

    [Test]
    public void Validator_ElseAfterElse_IsError_ViaElseSeenFlag()
    {
        var steps = new List<PlanStepInput>
        {
            new() { Name = "If1",   StepType = "NI_Flow_If", Expression = "True" },
            new() { Name = "Else1", StepType = "NI_Flow_Else" },
            new() { Name = "Else2", StepType = "NI_Flow_Else" },   // second Else → ElseSeen already true
            new() { Name = "EndIf", StepType = "NI_Flow_End" }
        };
        var r = SequencePlanValidator.Validate("Seq", steps, Array.Empty<string>());
        Assert.That(r.Errors.Any(e => e.Code == "E_ELSE_ORDER"), Is.True);
    }

    // ── SequenceEditorService: disposal + narrowed catch must stay robust ────────

    [Test]
    public void SequenceEditorService_IsRunning_DoesNotThrow_AndIsStable()
    {
        var svc = new SequenceEditorService(NullLogger<SequenceEditorService>.Instance);

        bool first = false, second = false;
        // The narrowed catch + Process[] disposal must not throw and must be repeatable.
        Assert.DoesNotThrow(() => first = svc.IsRunning);
        Assert.DoesNotThrow(() => second = svc.IsRunning);
        Assert.That(first, Is.EqualTo(second), "IsRunning must be stable across repeated probes");
    }

    [Test]
    public void SequenceEditorService_GetStatus_ReturnsInfo_WithoutThrowing()
    {
        var svc = new SequenceEditorService(NullLogger<SequenceEditorService>.Instance);

        SequenceEditorInfo? info = null;
        Assert.DoesNotThrow(() => info = svc.GetStatusAsync().GetAwaiter().GetResult());
        Assert.That(info, Is.Not.Null);
        // When the editor is not running, ProcessId stays 0 (the headless test case).
        if (!info!.IsRunning) Assert.That(info.ProcessId, Is.EqualTo(0));
    }

    [Test]
    public void SequenceEditorService_Dispose_IsIdempotent()
    {
        var svc = new SequenceEditorService(NullLogger<SequenceEditorService>.Instance);
        Assert.DoesNotThrow(() => svc.Dispose());
        Assert.DoesNotThrow(() => svc.Dispose());   // disposing the handle twice must be safe
    }
}
