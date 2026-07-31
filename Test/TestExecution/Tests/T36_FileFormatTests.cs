using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using TestStandMCP.Services;
using TestStandMCP.Tools;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// The on-disk SERIALIZATION of a sequence file (<c>binary</c> / <c>xml</c> / <c>ini</c>), exposed via
/// <c>create_sequence_file</c>, <c>save_sequence_file</c>, <c>set_file_properties</c> and reproduced by
/// <c>export_sequence_file</c> → <c>import_sequence_file</c>.
/// <para>
/// WHY THIS IS TESTED SEPARATELY FROM THE REBUILD SUITE. The format is the one deviation the FileDiffer
/// cannot see: it compares property trees, so a binary rebuild of an XML original reports
/// <c>identical</c> while differing in every byte on disk — measured 25 KB binary against 3.4 MB XML for
/// the same 30-sequence file. So these tests assert on the RAW BYTES (the <c>TOF1</c> magic vs an
/// <c>&lt;?xml</c> prolog), never on a diff.
/// </para>
/// </summary>
[TestFixture]
[Category("FileFormat")]
public class T36_FileFormatTests : TestBase
{
    // ── Raw-byte probes ───────────────────────────────────────────────────────
    // Format-agnostic on purpose: a binary file is zlib-compressed after the magic, so nothing inside
    // it is text-searchable, and only the first bytes distinguish the two formats reliably.

    private static string FirstBytes(string path, int count = 16)
    {
        using var fs = File.OpenRead(path);
        var buf = new byte[count];
        int n = fs.Read(buf, 0, count);
        // A BOM would render as mojibake in the failure message; strip it so the message reads as the
        // marker being compared. Verified against the real project files in C:\MEDELA_TFW: an XML .seq
        // starts EF BB BF "<?xml version=...".
        int skip = (n >= 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF) ? 3 : 0;
        return Encoding.ASCII.GetString(buf, skip, n - skip);
    }

    private static bool IsBinary(string path) => FirstBytes(path).StartsWith("TOF1", StringComparison.Ordinal);
    private static bool IsXml(string path)    => FirstBytes(path).StartsWith("<?xml", StringComparison.Ordinal);

    // A second temp path for the export/import destination (TempSeqFile is the source).
    private string _destFile = "";

    [SetUp]
    public void MakeDestPath() =>
        _destFile = Path.Combine(Path.GetTempPath(),
            $"TS_IntTest_dest_{TestContext.CurrentContext.Test.MethodName}_{Guid.NewGuid():N}.seq");

    [TearDown]
    public void CleanDest()
    {
        try
        {
            if (File.Exists(_destFile))
            {
                try { Ts.CloseSequenceFileAsync(_destFile).GetAwaiter().GetResult(); } catch { }
                File.Delete(_destFile);
            }
            var outcome = _destFile + ".import.json";
            if (File.Exists(outcome)) File.Delete(outcome);
        }
        catch { /* best-effort cleanup */ }
    }

    // ── Name parsing (engine-free) ────────────────────────────────────────────

    [Test]
    public void ParseFileWritingFormat_AcceptsNamesAliasesAndNumbers()
    {
        // The three canonical names round-trip through the describe side...
        foreach (var name in new[] { "binary", "xml", "ini" })
            Assert.That(TestStandService.DescribeFileWritingFormat(
                TestStandService.ParseFileWritingFormat(name)), Is.EqualTo(name),
                $"'{name}' should round-trip");

        // ...and the aliases land on the same values (TOF1 is the binary format's on-disk magic).
        int binary = TestStandService.ParseFileWritingFormat("binary");
        Assert.That(TestStandService.ParseFileWritingFormat("TOF1"), Is.EqualTo(binary));
        Assert.That(TestStandService.ParseFileWritingFormat("  Binary  "), Is.EqualTo(binary),
            "Trimming and casing should not matter");
        Assert.That(TestStandService.ParseFileWritingFormat("2"), Is.EqualTo(binary),
            "The raw FileWritingFormats value should be accepted too");
        Assert.That(TestStandService.ParseFileWritingFormat("text"),
            Is.EqualTo(TestStandService.ParseFileWritingFormat("ini")));
    }

    [Test]
    public void ParseFileWritingFormat_RejectsUnknownAndEmpty()
    {
        // Silently falling back to the engine default is what this whole feature exists to prevent, so
        // a typo has to be an error rather than "binary".
        Assert.Throws<ArgumentException>(() => TestStandService.ParseFileWritingFormat("xlm"));
        Assert.Throws<ArgumentException>(() => TestStandService.ParseFileWritingFormat("json"));
        Assert.Throws<ArgumentException>(() => TestStandService.ParseFileWritingFormat("   "));
    }

    // ── The declared tool schema ──────────────────────────────────────────────
    // A parameter only reaches an MCP client through the catalog: the client validates arguments against
    // the schema it cached at session start, so a param missing from the schema is rejected before the
    // handler ever runs — regardless of the handler being correct.

    [TestCase("create_sequence_file")]
    [TestCase("save_sequence_file")]
    [TestCase("set_file_properties")]
    [TestCase("import_sequence_file")]
    public void ToolSchema_DeclaresFileFormat(string toolName)
    {
        using var editor = new SequenceEditorService(NullLogger<SequenceEditorService>.Instance);
        var registry = new TestStandToolRegistry(
            new TestStandService(NullLogger<TestStandService>.Instance), editor,
            NullLogger<TestStandToolRegistry>.Instance);

        var tool = registry.GetTools().FirstOrDefault(t => t.Name == toolName);
        Assert.That(tool, Is.Not.Null, $"{toolName} is not registered");

        var props = tool!.InputSchema.GetProperty("properties");
        Assert.That(props.TryGetProperty("file_format", out var prop), Is.True,
            $"{toolName} must declare file_format");
        Assert.That(prop.GetProperty("type").GetString(), Is.EqualTo("string"));

        // Optional, never required: omitting it must keep the previous behaviour (import reproduces the
        // model's format, everything else leaves the file's own format alone).
        var required = tool.InputSchema.TryGetProperty("required", out var req)
            ? req.EnumerateArray().Select(e => e.GetString()).ToList()
            : new List<string?>();
        Assert.That(required, Does.Not.Contain("file_format"));
    }

    // ── create_sequence_file ──────────────────────────────────────────────────

    [Test]
    public async Task CreateSequenceFile_WithoutFormat_WritesCompressedBinary()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);

        Assert.That(IsBinary(TempSeqFile), Is.True,
            "The engine's own default is compressed binary — the file should start with 'TOF1'");
        var props = await Ts.GetFilePropertiesAsync(TempSeqFile);
        Assert.That(props.FileFormat, Is.EqualTo("binary"));
    }

    [Test]
    public async Task CreateSequenceFile_FormatXml_WritesXmlOnDisk()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile, overwrite: false, fileFormat: "xml");

        Assert.That(IsXml(TempSeqFile), Is.True,
            $"Expected an XML prolog, got '{FirstBytes(TempSeqFile)}'");
        var props = await Ts.GetFilePropertiesAsync(TempSeqFile);
        Assert.That(props.FileFormat, Is.EqualTo("xml"));
    }

    [Test]
    public void CreateSequenceFile_UnknownFormat_ThrowsAndWritesNothing()
    {
        Assert.ThrowsAsync<ArgumentException>(() =>
            Ts.CreateSequenceFileAsync(TempSeqFile, overwrite: false, fileFormat: "xlm"));
        Assert.That(File.Exists(TempSeqFile), Is.False,
            "A rejected format must fail before anything is written");
    }

    [Test]
    public async Task CreateSequenceFile_UnknownFormatWithOverwrite_DoesNotDeleteTheExistingFile()
    {
        // The overwrite path deletes before creating, so the format has to be validated FIRST —
        // otherwise a typo costs the file that was there and still fails.
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "KeepMe");
        long sizeBefore = new FileInfo(TempSeqFile).Length;

        Assert.ThrowsAsync<ArgumentException>(() =>
            Ts.CreateSequenceFileAsync(TempSeqFile, overwrite: true, fileFormat: "xlm"));

        Assert.That(File.Exists(TempSeqFile), Is.True, "The existing file must survive a rejected format");
        Assert.That(new FileInfo(TempSeqFile).Length, Is.EqualTo(sizeBefore));
    }

    // ── save_sequence_file / set_file_properties (conversion) ──────────────────

    [Test]
    public async Task SaveSequenceFile_FormatXml_ConvertsAnExistingBinaryFile()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        Assume.That(IsBinary(TempSeqFile), Is.True, "precondition: starts out binary");

        await Ts.SaveSequenceFileAsync(TempSeqFile, fileFormat: "xml");

        Assert.That(IsXml(TempSeqFile), Is.True, "The save should have re-serialized the file as XML");
    }

    [Test]
    public async Task SaveSequenceFile_WithoutFormat_KeepsTheFilesOwnFormat()
    {
        // The format is stored IN the file, so the ~83 internal save sites must not reset it to binary.
        await Ts.CreateSequenceFileAsync(TempSeqFile, overwrite: false, fileFormat: "xml");
        await Ts.InsertSequenceAsync(TempSeqFile, "Extra");     // an unrelated mutating call → its own save
        await Ts.SaveSequenceFileAsync(TempSeqFile);

        Assert.That(IsXml(TempSeqFile), Is.True,
            "An ordinary save must not silently convert an XML file back to binary");
    }

    [Test]
    public async Task SetFileProperties_FormatRoundTripsAndKeepsCommentAndVersion()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.SetFilePropertiesAsync(TempSeqFile, comment: "format test", version: "1.2.3");

        await Ts.SetFilePropertiesAsync(TempSeqFile, fileFormat: "xml");

        Assert.That(IsXml(TempSeqFile), Is.True);
        var props = await Ts.GetFilePropertiesAsync(TempSeqFile);
        Assert.That(props.FileFormat, Is.EqualTo("xml"));
        Assert.That(props.Comment,    Is.EqualTo("format test"), "A format change must not clear the comment");
        Assert.That(props.Version,    Is.EqualTo("1.2.3"),       "A format change must not clear the version");
    }

    [Test]
    public async Task FileFormat_SurvivesACloseAndReopen()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile, overwrite: false, fileFormat: "xml");
        await Ts.CloseSequenceFileAsync(TempSeqFile);

        // Read back from DISK — this is what proves the format is a persisted file property and not
        // just in-memory state on the loaded object.
        var props = await Ts.GetFilePropertiesAsync(TempSeqFile);
        Assert.That(props.FileFormat, Is.EqualTo("xml"));
    }

    // ── export / import round-trip ────────────────────────────────────────────

    [Test]
    public async Task ExportSequenceFile_CapturesTheSourceFormat()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile, overwrite: false, fileFormat: "xml");

        var model = await Ts.ExportSequenceFileAsync(TempSeqFile);

        Assert.That(model.File.FileFormat, Is.EqualTo("xml"),
            "The model must carry the format, otherwise an import cannot reproduce it");
    }

    [Test]
    public async Task ImportSequenceFile_ReproducesAnXmlSourceAsXml()
    {
        // This is the regression the feature exists for: before it, the rebuild of an XML original came
        // out as binary and diff_sequence_files still called it identical.
        await Ts.CreateSequenceFileAsync(TempSeqFile, overwrite: false, fileFormat: "xml");
        await Ts.InsertSequenceAsync(TempSeqFile, "Init");
        var model = await Ts.ExportSequenceFileAsync(TempSeqFile);

        await Ts.CreateSequenceFileAsync(_destFile, overwrite: true);   // default: binary
        Assume.That(IsBinary(_destFile), Is.True, "precondition: the destination starts out binary");

        var outcome = await Ts.ImportSequenceFileAsync(model, _destFile);

        Assert.That(IsXml(_destFile), Is.True,
            $"The rebuild should have been re-serialized as XML, got '{FirstBytes(_destFile)}'");
        Assert.That(outcome.FileFormat, Is.EqualTo("xml"), "The outcome should report the format applied");
    }

    [Test]
    public async Task ImportSequenceFile_ExplicitFormatOverridesTheModel()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile, overwrite: false, fileFormat: "xml");
        var model = await Ts.ExportSequenceFileAsync(TempSeqFile);

        await Ts.CreateSequenceFileAsync(_destFile, overwrite: true);
        var outcome = await Ts.ImportSequenceFileAsync(model, _destFile, fileFormat: "binary");

        Assert.That(IsBinary(_destFile), Is.True, "An explicit file_format must win over the model's");
        Assert.That(outcome.FileFormat, Is.EqualTo("binary"));
    }

    [Test]
    public async Task ImportSequenceFile_ModelWithoutFormat_LeavesTheDestinationAlone()
    {
        // A hand-written or older model has no fileFormat; the import must not invent one.
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        var model = await Ts.ExportSequenceFileAsync(TempSeqFile);
        model.File.FileFormat = null;

        await Ts.CreateSequenceFileAsync(_destFile, overwrite: true, fileFormat: "xml");
        var outcome = await Ts.ImportSequenceFileAsync(model, _destFile);

        Assert.That(IsXml(_destFile), Is.True,
            "With no format in the model the destination's own format must be preserved");
        Assert.That(outcome.FileFormat, Is.Null);
    }
}
