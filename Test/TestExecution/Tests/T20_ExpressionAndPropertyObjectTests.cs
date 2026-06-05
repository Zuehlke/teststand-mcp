using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// Integration tests for the expression-evaluation and structured property-object
/// tools (evaluate_expression, get_property_object, set_property_value,
/// delete_sub_property).
/// </summary>
[TestFixture]
[Category("PropertyObject")]
public class T20_ExpressionAndPropertyObjectTests : TestBase
{
    // ── evaluate_expression ────────────────────────────────────────────────────

    [Test]
    public async Task Evaluate_ArithmeticLiteral_ReturnsNumber()
    {
        var r = await Ts.EvaluateExpressionAsync("1 + 2 * 3");

        Assert.That(r.IsValid, Is.True, $"Expression should be valid: {r.ErrorMessage}");
        Assert.That(r.ValueType, Is.EqualTo("Number"));
        Assert.That(Convert.ToDouble(r.Value), Is.EqualTo(7.0).Within(1e-9));
    }

    [Test]
    public async Task Evaluate_StringConcatenation_ReturnsString()
    {
        var r = await Ts.EvaluateExpressionAsync("\"Hello, \" + \"World\"");

        Assert.That(r.IsValid, Is.True, $"Expression should be valid: {r.ErrorMessage}");
        Assert.That(r.ValueType, Is.EqualTo("String"));
        Assert.That(r.Value, Is.EqualTo("Hello, World"));
    }

    [Test]
    public async Task Evaluate_InFileGlobalsContext_ResolvesVariable()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.SetPropertyValueAsync(TempSeqFile, null, "FG_Num", "number", "21");

        var r = await Ts.EvaluateExpressionAsync("FG_Num * 2", TempSeqFile);

        Assert.That(r.IsValid, Is.True, $"Expression should be valid: {r.ErrorMessage}");
        Assert.That(Convert.ToDouble(r.Value), Is.EqualTo(42.0).Within(1e-9));
    }

    [Test]
    public async Task Evaluate_InvalidExpression_ReportsNotValid()
    {
        var r = await Ts.EvaluateExpressionAsync("1 +");

        Assert.That(r.IsValid, Is.False, "An incomplete expression must not be reported valid");
        Assert.That(r.ErrorMessage, Is.Not.Null.And.Not.Empty);
    }

    // ── set_property_value / get_property_object (scalars) ──────────────────────

    [Test]
    public async Task SetAndGet_NumberFileGlobal_RoundTrips()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.SetPropertyValueAsync(TempSeqFile, null, "MyNum", "number", "3.5");

        var info = await Ts.GetPropertyObjectAsync(TempSeqFile, null, "MyNum");

        Assert.That(info.ValueType, Is.EqualTo("Number"));
        Assert.That(Convert.ToDouble(info.Value), Is.EqualTo(3.5).Within(1e-9));
        Assert.That(info.IsArray, Is.False);
    }

    [Test]
    public async Task SetAndGet_StringFileGlobal_RoundTrips()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.SetPropertyValueAsync(TempSeqFile, null, "MyStr", "string", "hello");

        var info = await Ts.GetPropertyObjectAsync(TempSeqFile, null, "MyStr");

        Assert.That(info.ValueType, Is.EqualTo("String"));
        Assert.That(info.Value, Is.EqualTo("hello"));
    }

    // ── Container + nested subproperty ──────────────────────────────────────────

    [Test]
    public async Task SetContainer_WithNestedSubproperty_IsReportedStructurally()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.SetPropertyValueAsync(TempSeqFile, null, "MyCont", "container", null);
        await Ts.SetPropertyValueAsync(TempSeqFile, null, "MyCont.Inner", "number", "7");

        var info = await Ts.GetPropertyObjectAsync(TempSeqFile, null, "MyCont");

        Assert.That(info.ValueType, Is.EqualTo("Container"));
        var inner = info.SubProperties.FirstOrDefault(p => p.Name == "Inner");
        Assert.That(inner, Is.Not.Null, "Nested subproperty 'Inner' should be listed");
        Assert.That(Convert.ToDouble(inner!.Value), Is.EqualTo(7.0).Within(1e-9));
    }

    // ── Local variable on a sequence (Locals context) ───────────────────────────

    [Test]
    public async Task SetAndGet_LocalVariable_OnSequence_RoundTrips()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "MySeq");
        await Ts.SetPropertyValueAsync(TempSeqFile, "MySeq", "MyLocal", "number", "9");

        var info = await Ts.GetPropertyObjectAsync(TempSeqFile, "MySeq", "MyLocal");

        Assert.That(info.ValueType, Is.EqualTo("Number"));
        Assert.That(Convert.ToDouble(info.Value), Is.EqualTo(9.0).Within(1e-9));
    }

    // ── delete_sub_property ─────────────────────────────────────────────────────

    [Test]
    public async Task DeleteSubProperty_RemovesFileGlobal()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.SetPropertyValueAsync(TempSeqFile, null, "ToDelete", "number", "1");

        var before = await Ts.GetFileGlobalsAsync(TempSeqFile);
        Assert.That(before.Any(v => v.Name == "ToDelete"), Is.True,
            "Property should exist before deletion");

        await Ts.DeleteSubPropertyAsync(TempSeqFile, null, "ToDelete");

        var after = await Ts.GetFileGlobalsAsync(TempSeqFile);
        Assert.That(after.Any(v => v.Name == "ToDelete"), Is.False,
            "Property should be gone after deletion");
    }
}
