using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

[TestFixture]
[Category("DataTypeFields")]
public class T17_DataTypeFieldTests : TestBase
{
    private const string TypeName = "MyCustomType";

    [Test]
    public async Task AddListRemoveFields_RoundTrips()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.CreateDataTypeAsync(TempSeqFile, TypeName, "Object");

        await Ts.AddDataTypeFieldAsync(TempSeqFile, TypeName, "Voltage", "Number");
        await Ts.AddDataTypeFieldAsync(TempSeqFile, TypeName, "Label", "String");

        var fields = await Ts.GetDataTypeFieldsAsync(TempSeqFile, TypeName);
        Assert.That(fields.Count, Is.GreaterThanOrEqualTo(2),
            "Both added fields should be present");
        Assert.That(fields.Any(f => f.Name == "Voltage"), Is.True);
        Assert.That(fields.Any(f => f.Name == "Label"), Is.True);
        TestContext.WriteLine($"Fields: {string.Join(", ", fields.Select(f => f.Name))}");

        await Ts.RemoveDataTypeFieldAsync(TempSeqFile, TypeName, "Voltage");
        var afterRemove = await Ts.GetDataTypeFieldsAsync(TempSeqFile, TypeName);
        Assert.That(afterRemove.Any(f => f.Name == "Voltage"), Is.False,
            "Removed field should be gone");
    }
}
