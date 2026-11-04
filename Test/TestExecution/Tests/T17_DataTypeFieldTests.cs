using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// Custom data types live in the file's TypeUsageList (issue #43). The writers used to address a
/// file-ROOT subproperty instead: every real type (editor-made or copied) failed with "Unknown variable
/// or property name", and what create_data_type made could not be instantiated and vanished on save.
/// Type names carry a GUID suffix because the engine keeps a type loaded while any file holding it is
/// open, and create_data_type refuses a name the engine already knows.
/// </summary>
[TestFixture]
[Category("DataTypeFields")]
public class T17_DataTypeFieldTests : TestBase
{
    private static string Unique(string prefix) => $"{prefix}_{Guid.NewGuid():N}".Substring(0, prefix.Length + 9);

    [Test]
    public async Task AddListRemoveFields_RoundTrips()
    {
        string typeName = Unique("MyCustomType");
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.CreateDataTypeAsync(TempSeqFile, typeName, "Object");

        await Ts.AddDataTypeFieldAsync(TempSeqFile, typeName, "Voltage", "Number");
        await Ts.AddDataTypeFieldAsync(TempSeqFile, typeName, "Label", "String");

        var fields = await Ts.GetDataTypeFieldsAsync(TempSeqFile, typeName);
        Assert.That(fields.Select(f => f.Name), Is.SupersetOf(new[] { "Voltage", "Label" }));

        var afterRemove = await Ts.RemoveDataTypeFieldAsync(TempSeqFile, typeName, "Voltage");
        Assert.That(afterRemove.Any(f => f.Name == "Voltage"), Is.False, "Removed field should be gone");
    }

    [Test]
    public async Task CreatedType_IsARealType_UsableAndPersisted()
    {
        string typeName = Unique("NewType");
        string inner    = Unique("InnerType");
        await Ts.CreateSequenceFileAsync(TempSeqFile);

        var info = await Ts.CreateDataTypeAsync(TempSeqFile, typeName);
        Assert.That(info.Description, Is.EqualTo("attached-to-file"));
        Assert.That(info.BaseType, Is.EqualTo("Container"), "an empty container used to report Unknown");
        await Ts.CreateDataTypeAsync(TempSeqFile, inner);
        await Ts.AddDataTypeFieldAsync(TempSeqFile, inner, "Flag", "Boolean");

        await Ts.AddDataTypeFieldAsync(TempSeqFile, typeName, "Amount", "Number");
        await Ts.AddDataTypeFieldAsync(TempSeqFile, typeName, "Setup.Inner.Flag", "Boolean");   // nested
        await Ts.AddDataTypeFieldAsync(TempSeqFile, typeName, "Items", inner + "[]");           // array of a custom type
        var renamed = await Ts.RenameDataTypeFieldAsync(TempSeqFile, typeName, "Amount", "Total");
        Assert.That(renamed.Select(f => f.Name), Is.EquivalentTo(new[] { "Total", "Setup", "Items" }));

        // Listed where a type is listed …
        var typedefs = await Ts.GetFileTypeDefsAsync(TempSeqFile);
        Assert.That(typedefs.Any(t => t.Name == typeName), Is.True, "list_file_typedefs must see it");
        var dataTypes = await Ts.GetDataTypesAsync(TempSeqFile);
        Assert.That(dataTypes.Any(t => t.Name == typeName), Is.True, "get_data_types must see it");

        // … usable for a variable (this threw "Could not find type") …
        await Ts.InsertFileGlobalAsync(TempSeqFile, "gNew", typeName);

        // … and persisted: close, reopen from disk, everything is still there.
        await Ts.SaveSequenceFileAsync(TempSeqFile);
        await Ts.CloseSequenceFileAsync(TempSeqFile);
        await Ts.OpenSequenceFileAsync(TempSeqFile);
        var reloaded = await Ts.GetDataTypeFieldsAsync(TempSeqFile, typeName);
        Assert.That(reloaded.Select(f => f.Name), Is.EquivalentTo(new[] { "Total", "Setup", "Items" }));
        var globals = await Ts.GetFileGlobalsAsync(TempSeqFile);
        Assert.That(globals.Any(g => g.Name == "gNew"), Is.True);

        var audit = await Ts.AuditTypeConsistencyAsync(TempSeqFile);
        Assert.That(audit.Valid, Is.True, string.Join("; ", audit.Issues.Select(i => i.Code + " " + i.TypeName)));
        Assert.That(audit.Issues.Any(i => i.Code == "W_MODIFIED_TYPE"), Is.False,
            "every edit stamps a version and clears the modified flag");
    }

    [Test]
    public async Task CopiedType_CanBeEditedAndDeleted()
    {
        string typeName = Unique("ExampleType");
        string source   = Path.Combine(Path.GetTempPath(), $"TS_IntTest_types_{Guid.NewGuid():N}.seq");
        try
        {
            await Ts.CreateSequenceFileAsync(source);
            await Ts.CreateDataTypeAsync(source, typeName);
            await Ts.AddDataTypeFieldAsync(source, typeName, "Amount", "Number");
            await Ts.AddDataTypeFieldAsync(source, typeName, "Label", "String");

            await Ts.CreateSequenceFileAsync(TempSeqFile);
            await Ts.CopyTypeDefsAsync(source, TempSeqFile, new[] { typeName }, attach: "all");
            await Ts.CloseSequenceFileAsync(source);

            // The exact calls from issue #43 that failed with "Unknown variable or property name".
            var added = await Ts.AddDataTypeFieldAsync(TempSeqFile, typeName, "NewField", "Number");
            Assert.That(added.Any(f => f.Name == "NewField"), Is.True);
            var removed = await Ts.RemoveDataTypeFieldAsync(TempSeqFile, typeName, "Label");
            Assert.That(removed.Select(f => f.Name), Is.EquivalentTo(new[] { "Amount", "NewField" }));

            // Still instantiated → refused; after the instance is gone → removed.
            await Ts.InsertFileGlobalAsync(TempSeqFile, "gInst", typeName);
            var ex = Assert.ThrowsAsync<InvalidOperationException>(
                () => Ts.DeleteDataTypeAsync(TempSeqFile, typeName));
            Assert.That(ex!.Message, Does.Contain("still used").And.Contain("FileGlobals.gInst"));

            await Ts.DeleteSubPropertyAsync(TempSeqFile, null, "gInst");
            await Ts.DeleteDataTypeAsync(TempSeqFile, typeName);
            var after = await Ts.GetFileTypeDefsAsync(TempSeqFile);
            Assert.That(after.Any(t => t.Name == typeName), Is.False, "type should be gone");
        }
        finally
        {
            try { await Ts.CloseSequenceFileAsync(source); } catch { }
            try { File.Delete(source); } catch { }
        }
    }

    [Test]
    public async Task CreateDataType_RefusesDuplicateName()
    {
        string typeName = Unique("DupType");
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.CreateDataTypeAsync(TempSeqFile, typeName);
        Assert.ThrowsAsync<InvalidOperationException>(() => Ts.CreateDataTypeAsync(TempSeqFile, typeName));
    }

    [TestCase("1.0.0.0", "1.0.0.1")]
    [TestCase("2.3",     "2.3.0.1")]
    [TestCase("",        "1.0.0.1")]
    [TestCase("x.y",     "1.0.0.1")]
    public void BumpTypeVersion_IncrementsLastComponent(string input, string expected)
    {
        Assert.That(TestStandMCP.Services.TestStandService.BumpTypeVersion(input), Is.EqualTo(expected));
    }
}
