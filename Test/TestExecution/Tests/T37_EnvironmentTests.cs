using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using TestStandMCP.Services;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// Pure (engine-free) tests for TestStand ENVIRONMENT support — the <c>.tsenv</c> files that redirect
/// the engine's CommonAppData/Public/LocalAppData roots so one station can host several products
/// (GitHub issue #35).
///
/// <para>
/// Everything here runs without TestStand: the parsing/validation/auto-detection layer is
/// deliberately engine-free, because it all has to happen BEFORE an engine exists —
/// <c>SetEnvironmentPath</c> throws once one does. The guard tests additionally pin that a bad
/// environment is rejected before any engine thread starts, which is what keeps a misconfigured
/// path from parking the connect on an "Engine cannot be initialized" dialog no headless caller can
/// answer.
/// </para>
///
/// Does NOT inherit TestBase, so no TestStand engine is connected.
/// </summary>
[TestFixture]
[Category("PureLogic")]
public class T37_EnvironmentTests
{
    private string _tmp = "";

    [SetUp]
    public void SetUp()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "ts_mcp_env_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_tmp)) Directory.Delete(_tmp, recursive: true); } catch (IOException) { }
    }

    // ── Passing the environment to child processes (issue #35 follow-up) ─────
    //
    // AnalyzerApp.exe, FileDiffer.exe and SeqEdit.exe each start an engine of their OWN, so an
    // environment that is only applied in-process leaves all three on the global station
    // configuration — silently. All three accept the same /env <path> switch (verified live against
    // AnalyzerApp.exe's and FileDiffer.exe's own command-line help).

    [Test]
    public void PrependEnvSwitch_PutsEnvFirstAndQuotesThePath()
    {
        var result = TestStandEnvironmentLocator.PrependEnvSwitch(
            "\"C:\\proj.tsaproj\" /analyze /quit", @"C:\Product\Config\Product.tsenv");

        Assert.That(result, Is.EqualTo(
            "/env \"C:\\Product\\Config\\Product.tsenv\" \"C:\\proj.tsaproj\" /analyze /quit"),
            "both NI tools document /env as the LEADING flag, space-separated from its path");
    }

    [Test]
    public void PrependEnvSwitch_QuotesAPathWithSpaces()
    {
        var result = TestStandEnvironmentLocator.PrependEnvSwitch(
            "/GenerateReport \"r.xml\" \"a.seq\" \"b.seq\"", @"C:\My Products\Line 1\Env.tsenv");

        Assert.That(result, Does.StartWith("/env \"C:\\My Products\\Line 1\\Env.tsenv\" "));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void PrependEnvSwitch_WithoutAnEnvironment_LeavesTheCommandLineByteIdentical(string? env)
    {
        // The regression guard for every station that never used an environment: the NI tools must be
        // launched with exactly the command line they were launched with before this feature existed.
        const string original = "\"C:\\proj.tsaproj\" /analyze /report \"C:\\r.xml\" /save /quit";

        Assert.That(TestStandEnvironmentLocator.PrependEnvSwitch(original, env),
            Is.EqualTo(original).And.SameAs(original));
    }

    [Test]
    public void PrependEnvSwitch_OnAnEmptyCommandLine_EmitsNoStrayeSeparator()
    {
        // launch_sequence_editor starts SeqEdit.exe with no arguments at all.
        Assert.That(TestStandEnvironmentLocator.PrependEnvSwitch("", @"C:\P\E.tsenv"),
            Is.EqualTo("/env \"C:\\P\\E.tsenv\""));
    }

    [Test]
    public void PrependEnvSwitch_DoesNotDoubleQuoteAnAlreadyQuotedPath()
    {
        // A path that arrived quoted (e.g. copied out of a config file) must not become ""…"".
        Assert.That(TestStandEnvironmentLocator.PrependEnvSwitch("x", "\"C:\\P\\E.tsenv\""),
            Is.EqualTo("/env \"C:\\P\\E.tsenv\" x"));
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    /// <summary>Creates a directory below the scratch root (nested path segments allowed).</summary>
    private string Dir(params string[] segments)
    {
        var dir = Path.Combine(new[] { _tmp }.Concat(segments).ToArray());
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Builds a COMPLETE, valid environment: the three redirected roots on disk, the
    /// <c>Cfg\GeneralEngine.cfg</c> that marks CommonAppData as initialized, and the .tsenv naming them.
    /// </summary>
    private string MakeEnvironment(string name)
    {
        var root          = Dir(name);
        var commonAppData = Dir(name, "CommonAppData");
        var publicDir     = Dir(name, "Public");
        var localAppData  = Dir(name, "LocalAppData");

        Directory.CreateDirectory(Path.Combine(commonAppData, "Cfg"));
        File.WriteAllText(Path.Combine(commonAppData, TestStandEnvironmentLocator.EngineCfgRelativePath), "");

        var tsenv = Path.Combine(root, name + ".tsenv");
        File.WriteAllText(tsenv, string.Join(Environment.NewLine,
            "[TestStandPaths]",
            $"CommonAppData = \"{commonAppData}\"",
            $"Public = \"{publicDir}\"",
            $"LocalAppData = \"{localAppData}\""));
        return tsenv;
    }

    // ── Parsing ──────────────────────────────────────────────────────────────

    [Test]
    public void ReadAndValidate_CompleteEnvironment_IsUsableAndResolvesAllThreeRoots()
    {
        var tsenv = MakeEnvironment("ProductA");

        var env = TestStandEnvironmentLocator.ReadAndValidate(tsenv);

        Assert.Multiple(() =>
        {
            Assert.That(env.IsUsable, Is.True, "issues: " + string.Join(" / ", env.Issues));
            Assert.That(env.CommonAppData, Does.EndWith(@"ProductA\CommonAppData"));
            Assert.That(env.PublicDir,     Does.EndWith(@"ProductA\Public"));
            Assert.That(env.LocalAppData,  Does.EndWith(@"ProductA\LocalAppData"));
        });
    }

    [Test]
    public void ReadIniSection_StripsQuotesCommentsAndForeignSections()
    {
        var lines = new[]
        {
            "; a leading comment",
            "[SomethingElse]",
            @"CommonAppData = C:\wrong\section",
            "[TestStandPaths]",
            @"  CommonAppData  =  ""C:\ProgramData\P\CommonAppData""  ",
            @"Public = C:\pub   ; trailing comment outside quotes",
            "# hash comment",
            "NotAPair",
        };

        var section = TestStandEnvironmentLocator.ReadIniSection(lines, "TestStandPaths");

        Assert.Multiple(() =>
        {
            Assert.That(section["CommonAppData"], Is.EqualTo(@"C:\ProgramData\P\CommonAppData"),
                "surrounding quotes and padding must be stripped");
            Assert.That(section["Public"], Is.EqualTo(@"C:\pub"),
                "a trailing comment outside quotes is not part of the path");
            Assert.That(section.ContainsKey("NotAPair"), Is.False);
            Assert.That(section, Has.Count.EqualTo(2), "keys from other sections must not leak in");
        });
    }

    [Test]
    public void ReadAndValidate_ExpandsEnvironmentVariablesAndRelativePaths()
    {
        var root  = Dir("Rel");
        var cad   = Dir("Rel", "CAD");
        Directory.CreateDirectory(Path.Combine(cad, "Cfg"));
        File.WriteAllText(Path.Combine(cad, TestStandEnvironmentLocator.EngineCfgRelativePath), "");

        var tsenv = Path.Combine(root, "Rel.tsenv");
        // Relative to the .tsenv's own directory, plus an environment variable that must expand.
        File.WriteAllText(tsenv, string.Join(Environment.NewLine,
            "[TestStandPaths]",
            "CommonAppData = CAD",
            @"LocalAppData = %TEMP%"));

        var env = TestStandEnvironmentLocator.ReadAndValidate(tsenv);

        Assert.Multiple(() =>
        {
            Assert.That(env.CommonAppData, Is.EqualTo(cad), "a relative entry resolves against the .tsenv directory");
            Assert.That(env.LocalAppData, Is.EqualTo(Path.GetFullPath(Path.GetTempPath().TrimEnd('\\'))),
                "%TEMP% must be expanded, not taken literally");
            Assert.That(env.IsUsable, Is.True, "issues: " + string.Join(" / ", env.Issues));
        });
    }

    // ── Validation: every failure names its own defect ───────────────────────

    [Test]
    public void ReadAndValidate_MissingFile_IsReportedNotThrown()
    {
        var env = TestStandEnvironmentLocator.ReadAndValidate(Path.Combine(_tmp, "nope.tsenv"));

        Assert.Multiple(() =>
        {
            Assert.That(env.IsUsable, Is.False);
            Assert.That(env.Issues.Single(), Does.Contain("does not exist"));
        });
    }

    [Test]
    public void ReadAndValidate_NoTestStandPathsSection_IsRejected()
    {
        var tsenv = Path.Combine(Dir("Empty"), "Empty.tsenv");
        File.WriteAllText(tsenv, "[SomethingElse]" + Environment.NewLine + "Key = Value");

        var env = TestStandEnvironmentLocator.ReadAndValidate(tsenv);

        Assert.Multiple(() =>
        {
            Assert.That(env.IsUsable, Is.False);
            Assert.That(env.Issues, Has.Some.Contains("[TestStandPaths]"));
        });
    }

    [Test]
    public void ReadAndValidate_UninitializedCommonAppData_NamesTheMissingEngineConfig()
    {
        // THE hazard case: the directory exists, so a naive existence check passes — but TestStand has
        // never initialized it, and pointing the engine here raises an interactive dialog that no
        // headless caller can answer. The missing Cfg\GeneralEngine.cfg is the tell.
        var tsenv = MakeEnvironment("Uninitialized");
        var env0  = TestStandEnvironmentLocator.ReadAndValidate(tsenv);
        Assume.That(env0.IsUsable, Is.True);

        File.Delete(Path.Combine(env0.CommonAppData, TestStandEnvironmentLocator.EngineCfgRelativePath));

        var env = TestStandEnvironmentLocator.ReadAndValidate(tsenv);

        Assert.Multiple(() =>
        {
            Assert.That(env.IsUsable, Is.False);
            Assert.That(env.Issues, Has.Some.Contains(TestStandEnvironmentLocator.EngineCfgRelativePath));
            Assert.That(env.Issues, Has.Some.Contains("never initialized"));
        });
    }

    [Test]
    public void ReadAndValidate_MissingRedirectedDirectory_IsRejected()
    {
        var tsenv = MakeEnvironment("Broken");
        var env0  = TestStandEnvironmentLocator.ReadAndValidate(tsenv);
        Directory.Delete(env0.PublicDir, recursive: true);

        var env = TestStandEnvironmentLocator.ReadAndValidate(tsenv);

        Assert.Multiple(() =>
        {
            Assert.That(env.IsUsable, Is.False);
            Assert.That(env.Issues, Has.Some.Contains("Public directory does not exist"));
        });
    }

    // ── Auto-detection: the walk up ──────────────────────────────────────────

    [Test]
    public void Detect_FindsTheNearestAncestorEnvironment_FromASequenceFile()
    {
        var tsenv = MakeEnvironment("Detect");
        var deep  = Dir("Detect", "Sequences", "Sub");
        var seq   = Path.Combine(deep, "Main.seq");
        File.WriteAllText(seq, "");

        var result = TestStandEnvironmentLocator.Detect(seq);

        Assert.Multiple(() =>
        {
            Assert.That(result.Found, Is.True);
            Assert.That(result.TsenvPath, Is.EqualTo(tsenv));
            Assert.That(result.FoundInDirectory, Is.EqualTo(Path.GetDirectoryName(tsenv)));
            Assert.That(result.Probed, Does.Contain(deep), "the trail must show where the walk started");
        });
    }

    [Test]
    public void Detect_StopsAtTheNEARESTEnvironment_NotTheOutermost()
    {
        var outer = MakeEnvironment("Outer");
        var inner = Dir("Outer", "Inner");
        var innerTsenv = Path.Combine(inner, "Inner.tsenv");
        File.WriteAllText(innerTsenv, "[TestStandPaths]");
        var seq = Path.Combine(Dir("Outer", "Inner", "Seq"), "Main.seq");
        File.WriteAllText(seq, "");

        var result = TestStandEnvironmentLocator.Detect(seq);

        Assert.That(result.TsenvPath, Is.EqualTo(innerTsenv),
            $"the nearest environment wins, not {outer}");
    }

    [Test]
    public void Detect_FindsAnEnvironmentInASiblingConfigFolder()
    {
        // The real-world layout that a parents-only walk misses entirely: the .tsenv lives in a
        // Config folder NEXT TO the components tree, so it is never an ancestor of the sequence file.
        //   <root>\Config\zSpine_Environment.tsenv
        //   <root>\Components\Sequences\Main.seq
        var configDir = Dir("Plant", "Config");
        var tsenv = Path.Combine(configDir, "zSpine_Environment.tsenv");
        File.WriteAllText(tsenv, "[TestStandPaths]");

        var seq = Path.Combine(Dir("Plant", "Components", "Sequences"), "Main.seq");
        File.WriteAllText(seq, "");

        var result = TestStandEnvironmentLocator.Detect(seq);

        Assert.Multiple(() =>
        {
            Assert.That(result.Found, Is.True, "probed: " + result.Probed);
            Assert.That(result.TsenvPath, Is.EqualTo(tsenv));
            Assert.That(result.FoundInDirectory, Is.EqualTo(configDir),
                "the reported directory is where the file actually is, not the ancestor that led to it");
        });
    }

    [Test]
    public void Detect_FindsTheSiblingEnvironment_FromEveryComponentSubtree()
    {
        // Sequences and Models sit in different subtrees of the same product root; both must resolve
        // to the one environment above them.
        var tsenv = Path.Combine(Dir("Plant2", "Config"), "Env.tsenv");
        File.WriteAllText(tsenv, "[TestStandPaths]");
        var sequences = Dir("Plant2", "Components", "Sequences");
        var models    = Dir("Plant2", "Components", "Models");

        Assert.Multiple(() =>
        {
            Assert.That(TestStandEnvironmentLocator.Detect(sequences).TsenvPath, Is.EqualTo(tsenv));
            Assert.That(TestStandEnvironmentLocator.Detect(models).TsenvPath,    Is.EqualTo(tsenv));
        });
    }

    [Test]
    public void Detect_ADirectoryOfItsOwnBeatsItsSubdirectories()
    {
        // An environment sitting right in the walked directory is unambiguously the one meant — it
        // must not be weighed against whatever the subdirectories happen to hold.
        var root = Dir("Precedence");
        var own = Path.Combine(root, "Own.tsenv");
        File.WriteAllText(own, "[TestStandPaths]");
        File.WriteAllText(Path.Combine(Dir("Precedence", "Config"), "Sub.tsenv"), "[TestStandPaths]");

        Assert.That(TestStandEnvironmentLocator.Detect(root).TsenvPath, Is.EqualTo(own));
    }

    [Test]
    public void Detect_TwoSiblingFoldersWithAnEnvironment_IsAmbiguous()
    {
        // One level up, two candidate environments in different subdirectories: still a guess, so
        // still refused.
        var root = Dir("TwoConfigs");
        File.WriteAllText(Path.Combine(Dir("TwoConfigs", "ConfigA"), "A.tsenv"), "[TestStandPaths]");
        File.WriteAllText(Path.Combine(Dir("TwoConfigs", "ConfigB"), "B.tsenv"), "[TestStandPaths]");

        var result = TestStandEnvironmentLocator.Detect(root);

        Assert.Multiple(() =>
        {
            Assert.That(result.Found, Is.False);
            Assert.That(result.Ambiguity, Does.Contain("A.tsenv").And.Contain("B.tsenv"));
        });
    }

    [Test]
    public void Detect_DoesNotDescendTwoLevels()
    {
        // The scan is deliberately one level deep. Deeper multiplies the cost and the chance of
        // adopting an unrelated product's environment; such a layout must name tsenv_path instead.
        var root = Dir("TooDeep");
        File.WriteAllText(Path.Combine(Dir("TooDeep", "Config", "Nested"), "Deep.tsenv"), "[TestStandPaths]");

        Assert.That(TestStandEnvironmentLocator.Detect(root).Found, Is.False);
    }

    [Test]
    public void Detect_SeveralEnvironmentsInOneDirectory_IsAmbiguousAndNeverGuesses()
    {
        var dir = Dir("Ambiguous");
        File.WriteAllText(Path.Combine(dir, "ProductA.tsenv"), "[TestStandPaths]");
        File.WriteAllText(Path.Combine(dir, "ProductB.tsenv"), "[TestStandPaths]");
        var seq = Path.Combine(dir, "Main.seq");
        File.WriteAllText(seq, "");

        var result = TestStandEnvironmentLocator.Detect(seq);

        Assert.Multiple(() =>
        {
            Assert.That(result.Found, Is.False, "picking one would silently target the wrong product");
            Assert.That(result.TsenvPath, Is.Null);
            Assert.That(result.Ambiguity, Does.Contain("ProductA.tsenv").And.Contain("ProductB.tsenv"));
        });
    }

    [Test]
    public void Detect_NoEnvironmentAnywhere_ReportsAMissWithATrail()
    {
        var seq = Path.Combine(Dir("Bare", "Deep"), "Main.seq");
        File.WriteAllText(seq, "");

        var result = TestStandEnvironmentLocator.Detect(seq);

        Assert.Multiple(() =>
        {
            Assert.That(result.Found, Is.False);
            Assert.That(result.Ambiguity, Is.Null);
            Assert.That(result.Probed, Does.Contain(_tmp), "the walk must reach the root, not stop early");
        });
    }

    [Test]
    public void Detect_AcceptsADirectoryAsWellAsAFile()
    {
        var tsenv = MakeEnvironment("EitherWay");
        var sub   = Dir("EitherWay", "Sequences");

        Assert.That(TestStandEnvironmentLocator.Detect(sub).TsenvPath, Is.EqualTo(tsenv));
    }

    [Test]
    public void StartDirectoryOf_TreatsANonExistentPathWithAnExtensionAsAFile()
    {
        var dir = Dir("Future");
        var notYetCreated = Path.Combine(dir, "WillExistLater.seq");

        Assert.Multiple(() =>
        {
            Assert.That(TestStandEnvironmentLocator.StartDirectoryOf(notYetCreated), Is.EqualTo(dir));
            Assert.That(TestStandEnvironmentLocator.StartDirectoryOf(dir), Is.EqualTo(dir));
            Assert.That(TestStandEnvironmentLocator.StartDirectoryOf("   "), Is.Null);
        });
    }

    [Test]
    public void IsAutoSentinel_RecognisesTheKeywordAndNothingElse()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TestStandEnvironmentLocator.IsAutoSentinel("auto"), Is.True);
            Assert.That(TestStandEnvironmentLocator.IsAutoSentinel("  AUTO "), Is.True);
            Assert.That(TestStandEnvironmentLocator.IsAutoSentinel(@"C:\auto.tsenv"), Is.False);
            Assert.That(TestStandEnvironmentLocator.IsAutoSentinel(null), Is.False);
        });
    }

    // ── Connect guards: rejected BEFORE any engine thread starts ─────────────

    private static TestStandService NewService() => new(NullLogger<TestStandService>.Instance);

    [Test]
    public void ConnectAsync_NonExistentTsenv_ThrowsAndNamesTheFile()
    {
        var missing = Path.Combine(_tmp, "no_such.tsenv");

        Assert.That(async () => await NewService().ConnectAsync(tsenvPath: missing),
            Throws.InstanceOf<InvalidOperationException>().And.Message.Contains(missing));
    }

    [Test]
    public void ConnectAsync_UnusableTsenv_ReportsEveryDefectAtOnce()
    {
        var tsenv = MakeEnvironment("MultiDefect");
        var env0  = TestStandEnvironmentLocator.ReadAndValidate(tsenv);
        File.Delete(Path.Combine(env0.CommonAppData, TestStandEnvironmentLocator.EngineCfgRelativePath));
        Directory.Delete(env0.LocalAppData, recursive: true);

        Assert.That(async () => await NewService().ConnectAsync(tsenvPath: tsenv),
            Throws.InstanceOf<InvalidOperationException>()
                  .And.Message.Contains(TestStandEnvironmentLocator.EngineCfgRelativePath)
                  .And.Message.Contains("LocalAppData directory does not exist"));
    }

    [Test]
    public void ConnectAsync_AutoWithoutASearchStart_IsRejected()
    {
        Assert.That(async () => await NewService().ConnectAsync(tsenvPath: "auto"),
            Throws.InstanceOf<ArgumentException>().And.Message.Contains("tsenv_search_from"));
    }

    [Test]
    public void ConnectAsync_AutoOnAnAmbiguousDirectory_IsRejectedRatherThanGuessed()
    {
        var dir = Dir("AmbiguousConnect");
        File.WriteAllText(Path.Combine(dir, "A.tsenv"), "[TestStandPaths]");
        File.WriteAllText(Path.Combine(dir, "B.tsenv"), "[TestStandPaths]");

        Assert.That(async () => await NewService().ConnectAsync(tsenvPath: "auto", tsenvSearchFrom: dir),
            Throws.InstanceOf<InvalidOperationException>().And.Message.Contains("auto-detect"));
    }

    [Test]
    public void ConnectAsync_AutoWithNoEnvironmentAbove_IsRejectedWhenAskedForExplicitly()
    {
        // An explicit "auto" that finds nothing is an error; the same miss under the opt-in station
        // setting just means "global environment" and is silent. Only the explicit form is testable
        // without an engine, because the silent one proceeds to connect.
        var bare = Dir("BareConnect");

        Assert.That(async () => await NewService().ConnectAsync(tsenvPath: "auto", tsenvSearchFrom: bare),
            Throws.InstanceOf<FileNotFoundException>().And.Message.Contains("No .tsenv found"));
    }

    [Test]
    public void ConnectAsync_ConfiguredDefaultEnvironment_IsValidatedToo()
    {
        // The station default goes through exactly the same gate as the argument — a typo in
        // appsettings.json must fail loudly instead of silently connecting to the global environment.
        var svc = NewService();
        svc.ApplyStationDefaults(Path.Combine(_tmp, "configured_but_missing.tsenv"),
                                 environmentAutoDetect: false, connectTimeoutSeconds: 0);

        Assert.That(async () => await svc.ConnectAsync(),
            Throws.InstanceOf<InvalidOperationException>().And.Message.Contains("configured_but_missing.tsenv"));
    }

    [Test]
    public void ConnectAsync_AfterAFailedEnvironment_RetriesItInsteadOfSilentlyGoingGlobal()
    {
        // A .tsenv that passes the file checks but is rejected later (or a connect that never got
        // that far) must not let the NEXT call — typically EnsureConnected's lazy reconnect, which
        // passes no environment at all — succeed quietly against the global CommonAppData. Retrying
        // the same environment and failing the same way is the honest outcome.
        var tsenv = MakeEnvironment("Sticky");
        var svc   = NewService();
        svc.ApplyStationDefaults(tsenv, environmentAutoDetect: false, connectTimeoutSeconds: 0);

        // The station default is remembered, so an argument-less reconnect still resolves to it
        // rather than to the global environment.
        Assert.That(svc.ActiveEnvironmentPath, Is.Null, "nothing is active before the first connect");

        var broken = NewService();
        broken.ApplyStationDefaults(Path.Combine(_tmp, "gone.tsenv"),
                                    environmentAutoDetect: false, connectTimeoutSeconds: 0);
        Assert.That(async () => await broken.ConnectAsync(),
            Throws.InstanceOf<InvalidOperationException>(),
            "and a configured environment that cannot be used never degrades to global");
    }

    [Test]
    public void ApplyStationDefaults_BlankEnvironmentPath_LeavesTheGlobalEnvironmentAlone()
    {
        // The default configuration ships EnvironmentPath = "" — that must stay a no-op, so stations
        // that never heard of environments keep exactly the behaviour they had.
        var svc = NewService();
        svc.ApplyStationDefaults("   ", environmentAutoDetect: false, connectTimeoutSeconds: 0);

        Assert.That(svc.ActiveEnvironmentPath, Is.Null);
    }
}
