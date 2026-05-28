using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

[TestFixture]
[Category("Adapters")]
public class T09_AdapterAndTypePaletteTests : TestBase
{
    // ── Adapters ──────────────────────────────────────────────────────────────

    [Test]
    public async Task GetLoadedAdapters_ReturnsAtLeastOneAdapter()
    {
        var adapters = await Ts.GetLoadedAdaptersAsync();

        Assert.That(adapters, Is.Not.Empty, "At least one adapter should be loaded");
        TestContext.WriteLine($"Loaded adapters: {string.Join(", ", adapters.ConvertAll(a => a.Name))}");
    }

    [Test]
    public async Task ChangeStepAdapter_None_DoesNotThrow()
    {
        var seqFile = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"TS_AdapterTest_{System.Guid.NewGuid():N}.seq");

        try
        {
            await Ts.CreateSequenceFileAsync(seqFile);
            await Ts.InsertSequenceAsync(seqFile, "Seq");
            await Ts.InsertStepAsync(seqFile, "Seq", "Main", "Statement", "s");

            Assert.DoesNotThrowAsync(() =>
                Ts.ChangeStepAdapterAsync(seqFile, "Seq", "Main", "s", "None"));
        }
        finally
        {
            try { await Ts.CloseSequenceFileAsync(seqFile); } catch { }
            try { System.IO.File.Delete(seqFile); } catch { }
        }
    }

    [Test]
    public async Task GetAdapterDetails_None_ReturnsInfo()
    {
        var details = await Ts.GetAdapterDetailsAsync("None");
        Assert.That(details, Is.Not.Null);
        TestContext.WriteLine($"Adapter 'None' key: {details.KeyName}");
    }

    // ── Type palettes ─────────────────────────────────────────────────────────

    [Test]
    public async Task GetTypePalettes_ReturnsAtLeastOneEntry()
    {
        var palettes = await Ts.GetTypePalettesAsync();
        Assert.That(palettes, Is.Not.Empty, "At least one type palette should be registered");
        TestContext.WriteLine($"Palettes: {palettes.Count}");
    }

    [Test]
    public async Task GetDataTypes_ReturnsNonEmptyList()
    {
        var types = await Ts.GetDataTypesAsync();
        Assert.That(types, Is.Not.Empty);
        TestContext.WriteLine($"Data types: {types.Count}");
    }
}
