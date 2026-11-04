using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

// Deliberately NOT TestStandMCP.IntegrationTests: that namespace owns the [SetUpFixture] that connects
// the TestStand engine once for the whole run, and these tests must run on a machine without one.
namespace TestStandMCP.UnitTests;

/// <summary>
/// <see cref="VersionInfo.Resolve"/> and <see cref="VersionInfo.FormatForCli"/> — engine-free. Version
/// and commit come from the assembly's informational version (<c>X.Y.Z+sha</c>); VERSION.txt only adds
/// tag, build time and run URL.
/// </summary>
[TestFixture]
[Category("EngineFree")]
public class VersionInfoTests
{
    private const string TaggedInformationalVersion = "1.9.0+abc123";
    private const string VersionTxtName = "VERSION.txt";
    private const int MaxVersionTxtBytes = 4096;

    private string _tempDir = "";

    [SetUp]
    public void CreateTempDir()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "VersionInfoTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void DeleteTempDir()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string VersionTxtPath => Path.Combine(_tempDir, VersionTxtName);

    private string WriteVersionTxt(string content)
    {
        File.WriteAllText(VersionTxtPath, content);
        return VersionTxtPath;
    }

    private static string Lines(params string[] lines) =>
        string.Concat(lines.Select(line => line + Environment.NewLine));

    // ── Informational version (version + commit) ──────────────────────────────

    [Test]
    public void InformationalVersion_WithCommitSuffix_SplitsVersionAndCommit()
    {
        var info = VersionInfo.Resolve(TaggedInformationalVersion, versionTxtPath: null);

        Assert.That(info.Version, Is.EqualTo("1.9.0"));
        Assert.That(info.Commit, Is.EqualTo("abc123"));
    }

    [Test]
    public void InformationalVersion_WithoutCommitSuffix_HasNoCommit()
    {
        var info = VersionInfo.Resolve("1.9.0", versionTxtPath: null);

        Assert.That(info.Version, Is.EqualTo("1.9.0"));
        Assert.That(info.Commit, Is.Null);
    }

    [Test]
    public void InformationalVersion_PreRelease_KeepsThePreReleaseLabel()
    {
        var info = VersionInfo.Resolve("2.0.0-rc.1+abc123", versionTxtPath: null);

        Assert.That(info.Version, Is.EqualTo("2.0.0-rc.1"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void InformationalVersion_Missing_ReportsTheUntaggedVersion(string? informationalVersion)
    {
        var info = VersionInfo.Resolve(informationalVersion, versionTxtPath: null);

        Assert.That(info.Version, Is.EqualTo("0.0.0-dev"));
        Assert.That(info.Commit, Is.Null);
    }

    [TestCase("1.9.0+", "1.9.0", null)]
    [TestCase("+abc123", "0.0.0-dev", "abc123")]
    [TestCase("1.0+a+b", "1.0", "a+b")]
    [TestCase(" 1.9.0 ", "1.9.0", null)]
    [TestCase(" 1.9.0 + abc123 ", "1.9.0", "abc123")]
    public void InformationalVersion_OddShapes_SplitAtTheFirstPlusAndTrim(
        string informationalVersion, string expectedVersion, string? expectedCommit)
    {
        var info = VersionInfo.Resolve(informationalVersion, versionTxtPath: null);

        Assert.That(info.Version, Is.EqualTo(expectedVersion));
        Assert.That(info.Commit, Is.EqualTo(expectedCommit));
    }

    // ── VERSION.txt (tag / built / run) ───────────────────────────────────────

    [Test]
    public void VersionTxt_Present_AddsTagBuiltAndRun()
    {
        var path = WriteVersionTxt(Lines(
            "tag: v1.9.0", "version: 1.9.0", "commit: abc123",
            "built: 2026-10-24T08:15:00Z", "run: https://github.com/o/r/actions/runs/42"));

        var info = VersionInfo.Resolve(TaggedInformationalVersion, path);

        Assert.That(info.Tag, Is.EqualTo("v1.9.0"));
        Assert.That(info.BuiltAt, Is.EqualTo("2026-10-24T08:15:00Z"), "the value must keep its own colons");
        Assert.That(info.RunUrl, Is.EqualTo("https://github.com/o/r/actions/runs/42"));
        Assert.That(info.Note, Is.Null);
    }

    [Test]
    public void VersionTxt_DisagreeingVersionAndCommit_AssemblyWins()
    {
        var path = WriteVersionTxt(Lines("tag: v9.9.9", "version: 9.9.9", "commit: ffffff"));

        var info = VersionInfo.Resolve(TaggedInformationalVersion, path);

        Assert.That(info.Version, Is.EqualTo("1.9.0"));
        Assert.That(info.Commit, Is.EqualTo("abc123"));
    }

    [Test]
    public void VersionTxt_KeysAreCaseInsensitiveAndPaddingIsTrimmed()
    {
        var path = WriteVersionTxt("  TAG :   v1.9.0  \r\n");

        var info = VersionInfo.Resolve("1.9.0", path);

        Assert.That(info.Tag, Is.EqualTo("v1.9.0"));
    }

    [Test]
    public void VersionTxt_ControlCharactersInValues_AreStripped()
    {
        var path = WriteVersionTxt("tag: v1.9.0\u001b[31m\nbuilt: 2026\u0007-10-24\n");

        var info = VersionInfo.Resolve(TaggedInformationalVersion, path);

        Assert.That(info.Tag, Is.EqualTo("v1.9.0[31m"));
        Assert.That(info.BuiltAt, Is.EqualTo("2026-10-24"));
    }

    [Test]
    public void VersionTxt_AtTheSizeLimit_IsStillRead()
    {
        const string tagLine = "tag: v1.9.0\n";
        var path = WriteVersionTxt(tagLine + new string('#', MaxVersionTxtBytes - tagLine.Length));
        Assume.That(new FileInfo(path).Length, Is.EqualTo(MaxVersionTxtBytes));

        var info = VersionInfo.Resolve(TaggedInformationalVersion, path);

        Assert.That(info.Tag, Is.EqualTo("v1.9.0"));
        Assert.That(info.Note, Is.Null);
    }

    [Test]
    public void VersionTxt_OverTheSizeLimit_IsSkippedWithANote()
    {
        const string tagLine = "tag: v1.9.0\n";
        var path = WriteVersionTxt(tagLine + new string('#', MaxVersionTxtBytes - tagLine.Length + 1));
        Assume.That(new FileInfo(path).Length, Is.EqualTo(MaxVersionTxtBytes + 1));

        var info = VersionInfo.Resolve(TaggedInformationalVersion, path);

        Assert.That(info.Version, Is.EqualTo("1.9.0"));
        Assert.That(info.Tag, Is.Null);
        Assert.That(info.Note, Does.Contain("unreadable"));
    }

    [Test]
    public void VersionTxt_Missing_ForATaggedBuild_NotesTheAbsenceAndKeepsVersionAndCommit()
    {
        var info = VersionInfo.Resolve(TaggedInformationalVersion, VersionTxtPath);

        Assert.That(info.Version, Is.EqualTo("1.9.0"));
        Assert.That(info.Commit, Is.EqualTo("abc123"));
        Assert.That(info.Tag, Is.Null);
        Assert.That(info.Note, Does.Contain("absent"));
    }

    [Test]
    public void VersionTxt_NullPath_ForATaggedBuild_NotesTheAbsence()
    {
        var info = VersionInfo.Resolve(TaggedInformationalVersion, versionTxtPath: null);

        Assert.That(info.Note, Does.Contain("absent"));
    }

    // File.Exists never throws: a path with a NUL, and a directory that is called VERSION.txt, are both "not a file",
    // which a tagged build reports as absent. Neither reaches the catch block of ReadReleaseMetadata.
    [Test]
    public void VersionTxt_PathWithAnIllegalCharacter_IsReportedAsAbsent()
    {
        var path = "C:\\bad\0name\\" + VersionTxtName;

        var info = VersionInfo.Resolve(TaggedInformationalVersion, path);

        Assert.That(info.Version, Is.EqualTo("1.9.0"));
        Assert.That(info.Note, Is.EqualTo($"release metadata absent ({path} not found)"));
    }

    [Test]
    public void VersionTxt_DirectoryCalledVersionTxt_IsReportedAsAbsent()
    {
        Directory.CreateDirectory(VersionTxtPath);

        var info = VersionInfo.Resolve(TaggedInformationalVersion, VersionTxtPath);

        Assert.That(info.Version, Is.EqualTo("1.9.0"));
        Assert.That(info.Tag, Is.Null);
        Assert.That(info.Note, Is.EqualTo($"release metadata absent ({VersionTxtPath} not found)"));
    }

    // The real exception path: the file exists, but opening it for reading fails (another process holds it
    // exclusively). The failure becomes a note naming the problem; the version survives.
    [Test]
    public void VersionTxt_FileHeldExclusivelyByAnotherProcess_ReturnsTheVersionWithAnUnreadableNoteInsteadOfThrowing()
    {
        var path = WriteVersionTxt(Lines("tag: v1.9.0"));
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var info = VersionInfo.Resolve(TaggedInformationalVersion, path);

        Assert.That(info.Version, Is.EqualTo("1.9.0"));
        Assert.That(info.Commit, Is.EqualTo("abc123"));
        Assert.That(info.Tag, Is.Null, "nothing could be read from the locked file");
        Assert.That(info.Note, Does.StartWith("release metadata unreadable ("));
    }

    [TestCase("")]
    [TestCase("no key value lines here\n\njust prose")]
    [TestCase("unknown: value\n: no key\n")]
    [TestCase("tag:\n")]
    [TestCase("tag:   \nbuilt:\n")]
    public void VersionTxt_WithoutRecognisedKeyValueLines_ForATaggedBuild_NotesItIsUnreadable(string content)
    {
        var path = WriteVersionTxt(content);

        var info = VersionInfo.Resolve(TaggedInformationalVersion, path);

        Assert.That(info.Version, Is.EqualTo("1.9.0"));
        Assert.That(info.Commit, Is.EqualTo("abc123"));
        Assert.That(info.Tag, Is.Null);
        Assert.That(info.Note, Does.Contain("unreadable"));
    }

    [Test]
    public void VersionTxt_LockedByAnotherProcess_ForATaggedBuild_DoesNotThrow()
    {
        var path = WriteVersionTxt("tag: v1.9.0\n");
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        var info = VersionInfo.Resolve(TaggedInformationalVersion, path);

        Assert.That(info.Version, Is.EqualTo("1.9.0"));
        Assert.That(info.Note, Does.Contain("unreadable"));
    }

    [Test]
    public void VersionTxt_RecognisedKeysButNoTag_ForATaggedBuild_NotesTheMissingTag()
    {
        var path = WriteVersionTxt(Lines("built: 2026-10-24T08:15:00Z", "run: https://example.test/runs/1"));

        var info = VersionInfo.Resolve(TaggedInformationalVersion, path);

        Assert.That(info.Tag, Is.Null);
        Assert.That(info.BuiltAt, Is.EqualTo("2026-10-24T08:15:00Z"));
        Assert.That(info.Note, Is.EqualTo("release metadata incomplete: no tag"));
    }

    // ── Untagged builds: only the "absent" note is silent ─────────────────────

    [Test]
    public void UntaggedBuild_WithoutVersionTxt_HasNoNote()
    {
        var info = VersionInfo.Resolve(informationalVersion: null, VersionTxtPath);

        Assert.That(info.Version, Is.EqualTo("0.0.0-dev"));
        Assert.That(info.Note, Is.Null);
    }

    [Test]
    public void UntaggedBuild_WithUnreadableVersionTxt_StillNotesIt()
    {
        var path = WriteVersionTxt("just prose\n");

        var info = VersionInfo.Resolve(informationalVersion: null, path);

        Assert.That(info.Note, Does.Contain("unreadable"));
    }

    [Test]
    public void UntaggedBuild_WithVersionTxtButNoTag_HasNoNote()
    {
        var path = WriteVersionTxt(Lines("built: 2026-10-24T08:15:00Z"));

        var info = VersionInfo.Resolve(informationalVersion: null, path);

        Assert.That(info.Note, Is.Null);
    }

    // ── --version text ────────────────────────────────────────────────────────

    [Test]
    public void FormatForCli_FullyPopulatedBuild_ListsEveryFieldInOrder()
    {
        var path = WriteVersionTxt(Lines(
            "tag: v1.9.0", "built: 2026-10-24T08:15:00Z", "run: https://github.com/o/r/actions/runs/42"));

        var text = (VersionInfo.Resolve(TaggedInformationalVersion, path) with { Arch = "x64" }).FormatForCli();

        Assert.That(text, Is.EqualTo(Lines(
            "TestStand MCP Server 1.9.0",
            "  tag:    v1.9.0",
            "  commit: abc123",
            "  arch:   x64",
            "  built:  2026-10-24T08:15:00Z",
            "  run:    https://github.com/o/r/actions/runs/42")));
    }

    [Test]
    public void FormatForCli_UntaggedBuildWithoutMetadata_IsTheHeaderAndTheArchitecture()
    {
        var text = (VersionInfo.Resolve(informationalVersion: null, versionTxtPath: null) with { Arch = "x86" }).FormatForCli();

        Assert.That(text, Is.EqualTo(Lines("TestStand MCP Server 0.0.0-dev", "  arch:   x86")));
    }

    [Test]
    public void Arch_IsTheRunningProcessArchitecture()
    {
        var info = VersionInfo.Resolve(informationalVersion: null, versionTxtPath: null);

        Assert.That(info.Arch, Is.EqualTo(Environment.Is64BitProcess ? "x64" : "x86"));
    }

    [Test]
    public void Arch_InVersionTxt_DoesNotOverrideTheProcess()
    {
        var path = WriteVersionTxt(Lines("tag: v1.9.0", "arch: arm64"));

        var info = VersionInfo.Resolve(TaggedInformationalVersion, path);

        Assert.That(info.Arch, Is.EqualTo(Environment.Is64BitProcess ? "x64" : "x86"));
    }

    [Test]
    public void FormatForCli_TaggedBuildWithoutVersionTxt_AddsCommitAndTheAbsentNote()
    {
        var text = (VersionInfo.Resolve(TaggedInformationalVersion, VersionTxtPath) with { Arch = "x86" }).FormatForCli();

        Assert.That(text, Is.EqualTo(Lines(
            "TestStand MCP Server 1.9.0",
            "  commit: abc123",
            "  arch:   x86",
            $"  note:   release metadata absent ({VersionTxtPath} not found)")));
    }
}
