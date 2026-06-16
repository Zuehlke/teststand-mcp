using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using TestStandMCP.Tools;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// Pure (engine-free) unit tests for the enum-value argument parser
/// <see cref="TestStandToolRegistry.ExtractEnumValues"/> — specifically its C-style
/// auto-numbering when a <c>value</c> is omitted. Does NOT inherit TestBase.
/// </summary>
[TestFixture]
[Category("EnumUnit")]
public class T24_EnumValueParsingTests
{
    private static JsonElement El(string json) => JsonDocument.Parse(json).RootElement;

    [Test]
    public void ExplicitValues_ArePreserved()
    {
        var r = TestStandToolRegistry.ExtractEnumValues(
            El("{\"values\":[{\"name\":\"A\",\"value\":10},{\"name\":\"B\",\"value\":20}]}"), "values");
        Assert.That(r.Select(v => v.Name),  Is.EqualTo(new[] { "A", "B" }));
        Assert.That(r.Select(v => v.Value), Is.EqualTo(new[] { 10.0, 20.0 }));
    }

    [Test]
    public void OmittedValues_AutoNumberFromZero()
    {
        var r = TestStandToolRegistry.ExtractEnumValues(
            El("{\"values\":[{\"name\":\"X\"},{\"name\":\"Y\"},{\"name\":\"Z\"}]}"), "values");
        Assert.That(r.Select(v => v.Name),  Is.EqualTo(new[] { "X", "Y", "Z" }));
        Assert.That(r.Select(v => v.Value), Is.EqualTo(new[] { 0.0, 1.0, 2.0 }));
    }

    [Test]
    public void MixedValues_ContinueCStyleAfterLastExplicit()
    {
        // A=5 (explicit) → B auto=6 → C=1 (explicit, resets the running counter) → D auto=2.
        var r = TestStandToolRegistry.ExtractEnumValues(
            El("{\"values\":[{\"name\":\"A\",\"value\":5},{\"name\":\"B\"}," +
               "{\"name\":\"C\",\"value\":1},{\"name\":\"D\"}]}"), "values");
        Assert.That(r.Select(v => v.Name),  Is.EqualTo(new[] { "A", "B", "C", "D" }));
        Assert.That(r.Select(v => v.Value), Is.EqualTo(new[] { 5.0, 6.0, 1.0, 2.0 }));
    }

    [Test]
    public void MissingArray_ReturnsEmpty()
    {
        Assert.That(TestStandToolRegistry.ExtractEnumValues(El("{}"), "values"), Is.Empty);
    }

    [Test]
    public void NonObjectItems_AreSkipped()
    {
        var r = TestStandToolRegistry.ExtractEnumValues(
            El("{\"values\":[\"bare\",{\"name\":\"Ok\",\"value\":7}]}"), "values");
        Assert.That(r.Select(v => v.Name), Is.EqualTo(new[] { "Ok" }));
        Assert.That(r[0].Value, Is.EqualTo(7.0));
    }
}
