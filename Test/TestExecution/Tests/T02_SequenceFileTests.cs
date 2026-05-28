using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

[TestFixture]
[Category("SequenceFile")]
public class T02_SequenceFileTests : TestBase
{
    // ── Create / Open / Close ─────────────────────────────────────────────────

    [Test]
    public async Task CreateSequenceFile_CreatesPhysicalFile()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);

        Assert.That(File.Exists(TempSeqFile), Is.True, "Sequence file should be written to disk");
    }

    [Test]
    public async Task OpenSequenceFile_ReturnsFileInfo()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        var info = await Ts.OpenSequenceFileAsync(TempSeqFile);

        Assert.That(info,          Is.Not.Null);
        Assert.That(info.FilePath, Is.EqualTo(TempSeqFile));
        Assert.That(info.FileName, Is.EqualTo(Path.GetFileName(TempSeqFile)));
    }

    [Test]
    public async Task GetLoadedSequenceFiles_ContainsNewlyCreatedFile()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);

        var files = await Ts.GetLoadedSequenceFilesAsync();

        Assert.That(files.Any(f => f.FilePath == TempSeqFile), Is.True,
            "Loaded files should include the newly created file");
    }

    [Test]
    public async Task SaveSequenceFile_DoesNotThrow()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        Assert.DoesNotThrowAsync(() => Ts.SaveSequenceFileAsync(TempSeqFile));
    }

    [Test]
    public async Task CloseSequenceFile_RemovesFromLoadedList()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.CloseSequenceFileAsync(TempSeqFile);

        var files = await Ts.GetLoadedSequenceFilesAsync();

        Assert.That(files.Any(f => f.FilePath == TempSeqFile), Is.False,
            "Closed file should not appear in loaded-files list");
    }

    // ── File Properties (Comment / Version) ───────────────────────────────────

    [Test]
    public async Task SetAndGetFileProperties_CommentAndVersion_RoundTrip()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        const string expectedComment = "Integration test file — do not modify manually.";
        const string expectedVersion = "2.5.1";

        await Ts.SetFilePropertiesAsync(TempSeqFile, comment: expectedComment, version: expectedVersion);

        var props = await Ts.GetFilePropertiesAsync(TempSeqFile);

        Assert.That(props.Comment, Is.EqualTo(expectedComment), "Comment round-trip failed");
        Assert.That(props.Version, Is.EqualTo(expectedVersion), "Version round-trip failed");
    }

    [Test]
    public async Task SetFileProperties_OnlyComment_DoesNotClearVersion()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.SetFilePropertiesAsync(TempSeqFile, version: "1.0.0");
        await Ts.SetFilePropertiesAsync(TempSeqFile, comment: "Only comment changed");

        var props = await Ts.GetFilePropertiesAsync(TempSeqFile);

        Assert.That(props.Comment, Is.EqualTo("Only comment changed"));
        Assert.That(props.Version, Is.EqualTo("1.0.0"), "Version should be preserved when only comment is updated");
    }
}
