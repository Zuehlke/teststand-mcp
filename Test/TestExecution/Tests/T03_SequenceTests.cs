using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

[TestFixture]
[Category("Sequence")]
public class T03_SequenceTests : TestBase
{
    private async Task<string> CreateFileWithSequenceAsync(string seqName = "TestSeq")
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, seqName);
        return seqName;
    }

    // ── Insert / Exists ────────────────────────────────────────────────────────

    [Test]
    public async Task InsertSequence_CreatesSequence()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "MySequence");

        var exists = await Ts.SequenceNameExistsAsync(TempSeqFile, "MySequence");
        Assert.That(exists, Is.True);
    }

    [Test]
    public async Task SequenceNameExists_NonExistentSeq_ReturnsFalse()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        var exists = await Ts.SequenceNameExistsAsync(TempSeqFile, "NoSuchSeq");
        Assert.That(exists, Is.False);
    }

    // ── Get Sequence ───────────────────────────────────────────────────────────

    [Test]
    public async Task GetSequence_ReturnsSequenceInfo()
    {
        await CreateFileWithSequenceAsync("MySeq");
        var seq = await Ts.GetSequenceAsync(TempSeqFile, "MySeq");

        Assert.That(seq,      Is.Not.Null);
        Assert.That(seq.Name, Is.EqualTo("MySeq"));
    }

    // ── Rename ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task RenameSequence_ChangesName()
    {
        await CreateFileWithSequenceAsync("OldName");
        await Ts.RenameSequenceAsync(TempSeqFile, "OldName", "NewName");

        Assert.That(await Ts.SequenceNameExistsAsync(TempSeqFile, "OldName"), Is.False);
        Assert.That(await Ts.SequenceNameExistsAsync(TempSeqFile, "NewName"), Is.True);
    }

    // ── Delete ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task DeleteSequence_RemovesSequence()
    {
        await CreateFileWithSequenceAsync("ToDelete");
        await Ts.DeleteSequenceAsync(TempSeqFile, "ToDelete");

        Assert.That(await Ts.SequenceNameExistsAsync(TempSeqFile, "ToDelete"), Is.False);
    }

    // ── Comment / Properties ────────────────────────────────────────────────────

    [Test]
    public async Task SetAndGetSequenceProperties_Comment_RoundTrip()
    {
        await CreateFileWithSequenceAsync("Documented");
        const string expectedComment = "This sequence verifies the power-on self-test.";

        await Ts.SetSequencePropertiesAsync(TempSeqFile, "Documented",
            new Models.SequenceProperties { Description = expectedComment });

        var props = await Ts.GetSequencePropertiesAsync(TempSeqFile, "Documented");

        Assert.That(props.Description, Is.EqualTo(expectedComment), "Sequence comment round-trip failed");
    }

    // ── Local Variables ─────────────────────────────────────────────────────────

    [Test]
    public async Task InsertAndGetLocalVariable_RoundTrip()
    {
        await CreateFileWithSequenceAsync("WithLocals");
        await Ts.InsertLocalVariableAsync(TempSeqFile, "WithLocals", "myVar", "Number", "0");

        var vars = await Ts.GetLocalVariablesAsync(TempSeqFile, "WithLocals");
        var found = vars.Find(v => v.Name == "myVar");

        Assert.That(found, Is.Not.Null, "Inserted variable should appear in local-variable list");
        Assert.That(found!.DataType,
            Does.Contain("Number").Or.Contain("Double"),
            "Data type should be numeric");
    }

    [Test]
    public async Task SetLocalVariableComment_RoundTrip()
    {
        await CreateFileWithSequenceAsync("VarWithComment");
        await Ts.InsertLocalVariableAsync(TempSeqFile, "VarWithComment", "counter", "Number", "0");
        await Ts.SetLocalVariableCommentAsync(TempSeqFile, "VarWithComment", "counter",
            "Loop iteration counter");

        var vars = await Ts.GetLocalVariablesAsync(TempSeqFile, "VarWithComment");
        var v    = vars.Find(x => x.Name == "counter");

        Assert.That(v?.Description, Is.EqualTo("Loop iteration counter"));
    }

    [Test]
    public async Task DeleteLocalVariable_RemovesVariable()
    {
        await CreateFileWithSequenceAsync("VarDelete");
        await Ts.InsertLocalVariableAsync(TempSeqFile, "VarDelete", "temp", "Number");
        await Ts.DeleteLocalVariableAsync(TempSeqFile, "VarDelete", "temp");

        var vars = await Ts.GetLocalVariablesAsync(TempSeqFile, "VarDelete");
        Assert.That(vars.Exists(v => v.Name == "temp"), Is.False);
    }

    // ── Duplicate ──────────────────────────────────────────────────────────────

    [Test]
    public async Task DuplicateSequence_CreatesNewSequence()
    {
        await CreateFileWithSequenceAsync("Original");
        await Ts.DuplicateSequenceAsync(TempSeqFile, "Original", "Copy");

        Assert.That(await Ts.SequenceNameExistsAsync(TempSeqFile, "Copy"), Is.True);
        Assert.That(await Ts.SequenceNameExistsAsync(TempSeqFile, "Original"), Is.True,
            "Original should still exist after duplicate");
    }

    // ── Parameters ─────────────────────────────────────────────────────────────

    [Test]
    public async Task InsertAndGetSequenceParameter_RoundTrip()
    {
        await CreateFileWithSequenceAsync("Parameterized");
        await Ts.InsertSequenceParameterAsync(TempSeqFile, "Parameterized",
            "InputVoltage", "Number", "Input", "5.0");

        var parameters = await Ts.GetSequenceParametersAsync(TempSeqFile, "Parameterized");
        var p = parameters.Find(x => x.Name == "InputVoltage");

        Assert.That(p, Is.Not.Null, "Inserted parameter should be retrievable");
        Assert.That(p!.Direction, Does.Contain("Input"));
    }
}
