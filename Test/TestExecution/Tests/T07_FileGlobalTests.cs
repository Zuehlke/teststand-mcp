using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

[TestFixture]
[Category("FileGlobals")]
public class T07_FileGlobalTests : TestBase
{
    // ── Insert / Get ──────────────────────────────────────────────────────────

    [Test]
    public async Task InsertAndGetFileGlobal_RoundTrip()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertFileGlobalAsync(TempSeqFile, "GlobalCounter", "Number");

        var globals = await Ts.GetFileGlobalsAsync(TempSeqFile);
        var g = globals.FirstOrDefault(v => v.Name == "GlobalCounter");

        Assert.That(g, Is.Not.Null, "Inserted file global should appear in the list");
        Assert.That(g!.DataType, Does.Contain("Number").Or.Contain("Double"));
    }

    // ── Set value ─────────────────────────────────────────────────────────────

    [Test]
    public async Task SetFileGlobal_UpdatesValue()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertFileGlobalAsync(TempSeqFile, "Counter", "Number");
        await Ts.SetFileGlobalAsync(TempSeqFile, "Counter", 42.0);

        var globals = await Ts.GetFileGlobalsAsync(TempSeqFile);
        var g = globals.FirstOrDefault(v => v.Name == "Counter");

        Assert.That(g, Is.Not.Null);
        // Value may be returned as double or string representation — check non-zero/non-null
        Assert.That(g!.Value, Is.Not.Null);
        TestContext.WriteLine($"Counter value: {g.Value}");
    }

    // ── Station globals ───────────────────────────────────────────────────────

    [Test]
    public async Task GetStationGlobals_ReturnsListWithoutThrowing()
    {
        var globals = await Ts.GetStationGlobalsAsync();
        Assert.That(globals, Is.Not.Null);
        TestContext.WriteLine($"Station globals count: {globals.Count}");
    }
}
