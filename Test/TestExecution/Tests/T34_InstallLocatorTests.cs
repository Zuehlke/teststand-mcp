using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using NUnit.Framework;
using TestStandMCP.Services;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// Pure (engine-free) tests for <see cref="TestStandInstallLocator"/> and the child-environment
/// normalisation — the path-resolution layer that decides WHICH installed TestStand's tools get
/// launched. Regression cover for three defects that together made <c>diff_sequence_files</c> fail
/// on a station whose FileDiffer.exe lives in the 64-bit install:
///   1. every candidate was validated against a FIXED stand-in (AnalyzerApp.exe) instead of the tool
///      actually being launched, so a Bin holding FileDiffer but not AnalyzerApp was discarded;
///   2. the install scan iterated SpecialFolder.ProgramFiles + ProgramFilesX86, which under WOW64
///      are the SAME directory — the 64-bit install root was unreachable from this x86 host;
///   3. the COM-registration probe only read the 32-bit registry view, so a 64-bit TestStand
///      registration was invisible.
/// Does NOT inherit TestBase, so no TestStand engine is connected.
/// </summary>
[TestFixture]
[Category("PureLogic")]
public class T34_InstallLocatorTests
{
    private string _tmp = "";

    [SetUp]
    public void SetUp()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "ts_mcp_locator_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_tmp)) Directory.Delete(_tmp, recursive: true); } catch (IOException) { }
    }

    /// <summary>Creates a directory below the scratch root and drops the given empty files in it.</summary>
    private string MakeBin(string name, params string[] files)
    {
        var dir = Path.Combine(_tmp, name);
        Directory.CreateDirectory(dir);
        foreach (var f in files) File.WriteAllText(Path.Combine(dir, f), "");
        return dir;
    }

    // ── GetProgramFilesRoots: the WOW64 trap (defect 2) ──────────────────────────

    [Test]
    public void GetProgramFilesRoots_ReachesTheSixtyFourBitRoot()
    {
        var w6432 = Environment.GetEnvironmentVariable("ProgramW6432");
        Assume.That(w6432, Is.Not.Null.And.Not.Empty,
            "32-bit-only Windows has no %ProgramW6432% — nothing to reach.");

        Assert.That(TestStandInstallLocator.GetProgramFilesRoots(),
            Has.Some.EqualTo(w6432).IgnoreCase,
            "the 64-bit install root must be scanned; under WOW64 SpecialFolder.ProgramFiles " +
            "is redirected to the (x86) path and can never name it");
    }

    [Test]
    public void GetProgramFilesRoots_ReachesTheX86Root()
    {
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        Assume.That(Directory.Exists(pf86), Is.True);

        Assert.That(TestStandInstallLocator.GetProgramFilesRoots(),
            Has.Some.EqualTo(pf86).IgnoreCase);
    }

    [Test]
    public void GetProgramFilesRoots_HasNoDuplicates()
    {
        // The original bug: SpecialFolder.ProgramFiles and ProgramFilesX86 collapse onto the same
        // path in a 32-bit process, so the scan walked one directory twice and the other never.
        var roots = TestStandInstallLocator.GetProgramFilesRoots().ToList();

        Assert.That(roots.Select(r => r.TrimEnd('\\').ToLowerInvariant()).Distinct().Count(),
            Is.EqualTo(roots.Count), "roots must be deduplicated");
    }

    [Test]
    public void GetProgramFilesRoots_OnlyReturnsExistingDirectories()
    {
        Assert.That(TestStandInstallLocator.GetProgramFilesRoots(),
            Is.All.Matches<string>(Directory.Exists));
    }

    // ── EnumerateTestStandBins: version wildcard + newest-first ordering ─────────

    [Test]
    public void EnumerateTestStandBins_YieldsNewestReleaseFirst_AndOnlyTestStand()
    {
        var ni = Path.Combine(_tmp, "National Instruments");
        foreach (var d in new[] { "TestStand 2021", "TestStand 2026", "TestStand 2019", "LabVIEW 2025" })
            Directory.CreateDirectory(Path.Combine(ni, d, "Bin"));

        var bins = TestStandInstallLocator.EnumerateTestStandBins(_tmp).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(bins, Has.Count.EqualTo(3), "LabVIEW must not be enumerated");
            Assert.That(bins[0], Is.EqualTo(Path.Combine(ni, "TestStand 2026", "Bin")));
            Assert.That(bins[1], Is.EqualTo(Path.Combine(ni, "TestStand 2021", "Bin")));
            Assert.That(bins[2], Is.EqualTo(Path.Combine(ni, "TestStand 2019", "Bin")));
        });
    }

    [Test]
    public void EnumerateTestStandBins_MissingInstallRoot_YieldsNothing()
    {
        Assert.That(TestStandInstallLocator.EnumerateTestStandBins(Path.Combine(_tmp, "nope")),
            Is.Empty);
    }

    // ── Resolve: probe for the tool actually being launched (defect 1) ───────────

    [Test]
    public void Resolve_SkipsEngineBin_WhenItLacksTheRequestedTool()
    {
        // THE reported failure: the engine's Bin carries AnalyzerApp but no FileDiffer, while a
        // different install carries the differ. Probing for a fixed stand-in returned the engine's
        // Bin and the launch then failed on a path that does not exist.
        var engineBin = MakeBin("engine", "AnalyzerApp.exe");
        var differBin = MakeBin("differ", "FileDiffer.exe");

        var prev = Environment.GetEnvironmentVariable("TESTSTANDBIN");
        try
        {
            Environment.SetEnvironmentVariable("TESTSTANDBIN", differBin);
            var (binDir, probed) = TestStandInstallLocator.Resolve("FileDiffer.exe", engineBin);

            Assert.Multiple(() =>
            {
                Assert.That(binDir, Is.EqualTo(differBin),
                    "must keep probing past a Bin that does not hold the requested tool");
                Assert.That(probed, Is.Empty, "a successful resolve reports no probe trail");
            });
        }
        finally { Environment.SetEnvironmentVariable("TESTSTANDBIN", prev); }
    }

    [Test]
    public void Resolve_PrefersEngineBin_WhenItHoldsTheRequestedTool()
    {
        // The engine's own Bin is the exact running version and must win over every fallback.
        var engineBin = MakeBin("engine", "AnalyzerApp.exe");
        var otherBin  = MakeBin("other",  "AnalyzerApp.exe");

        var prev = Environment.GetEnvironmentVariable("TESTSTANDBIN");
        try
        {
            Environment.SetEnvironmentVariable("TESTSTANDBIN", otherBin);
            var (binDir, _) = TestStandInstallLocator.Resolve("AnalyzerApp.exe", engineBin);

            Assert.That(binDir, Is.EqualTo(engineBin));
        }
        finally { Environment.SetEnvironmentVariable("TESTSTANDBIN", prev); }
    }

    [Test]
    public void Resolve_FallsBackToEnvironmentVariable_WhenNotConnected()
    {
        var envBin = MakeBin("envbin", "FileDiffer.exe");

        var prev = Environment.GetEnvironmentVariable("TESTSTANDBIN");
        try
        {
            Environment.SetEnvironmentVariable("TESTSTANDBIN", envBin);
            var (binDir, _) = TestStandInstallLocator.Resolve("FileDiffer.exe", engineBinDir: null);

            Assert.That(binDir, Is.EqualTo(envBin));
        }
        finally { Environment.SetEnvironmentVariable("TESTSTANDBIN", prev); }
    }

    [Test]
    public void Resolve_UnknownTool_ReturnsEmptyBinAndADiagnosableProbeTrail()
    {
        var engineBin = MakeBin("engine", "AnalyzerApp.exe");

        var (binDir, probed) = TestStandInstallLocator.Resolve("NoSuchNiTool_9f3a.exe", engineBin);

        Assert.Multiple(() =>
        {
            Assert.That(binDir, Is.Empty);
            Assert.That(probed, Does.Contain("engine BinDirectory"));
            Assert.That(probed, Does.Contain("NoSuchNiTool_9f3a.exe"),
                "the trail must name the tool that was looked for");
        });
    }

    [Test]
    public void Resolve_ProbesBothRegistryViews()
    {
        // Defect 3: only Registry32 was read, so a 64-bit TestStand registration stayed invisible.
        Assume.That(TestStandInstallLocator.FindBinFromRegistry(RegistryView.Registry64),
            Is.Not.Null, "no 64-bit TestStand engine registered on this station");

        var (_, probed) = TestStandInstallLocator.Resolve("NoSuchNiTool_9f3a.exe", engineBinDir: null);

        Assert.That(probed, Does.Contain("Registry64"));
    }

    [Test]
    public void FindBinFromRegistry_ReturnsTheDirectoryOfTheRegisteredEngineDll()
    {
        var bin = TestStandInstallLocator.FindBinFromRegistry(RegistryView.Registry32)
                  ?? TestStandInstallLocator.FindBinFromRegistry(RegistryView.Registry64);
        Assume.That(bin, Is.Not.Null, "no TestStand engine registered on this station");

        Assert.That(Directory.Exists(bin!), Is.True,
            "the registration must resolve to a real Bin directory");
    }

    // ── engine_path override: normalisation + precedence (defect 6) ──────────────

    [Test]
    public void NormalizeBinDirectory_EngineDllPath_ResolvesToItsDirectory()
    {
        var bin = MakeBin("Bin", "teapi.dll");

        Assert.That(TestStandInstallLocator.NormalizeBinDirectory(Path.Combine(bin, "teapi.dll")),
            Is.EqualTo(bin));
    }

    [Test]
    public void NormalizeBinDirectory_InstallRoot_ResolvesToItsBinSubdirectory()
    {
        var root = Path.Combine(_tmp, "TestStand 2026");
        var bin  = Path.Combine(root, "Bin");
        Directory.CreateDirectory(bin);

        Assert.That(TestStandInstallLocator.NormalizeBinDirectory(root), Is.EqualTo(bin));
    }

    [Test]
    public void NormalizeBinDirectory_BinDirectoryItself_IsKept()
    {
        var bin = MakeBin("Bin", "teapi.dll");

        Assert.That(TestStandInstallLocator.NormalizeBinDirectory(bin), Is.EqualTo(bin));
    }

    [Test]
    public void NormalizeBinDirectory_StripsSurroundingQuotesAndWhitespace()
    {
        var bin = MakeBin("Bin", "teapi.dll");

        Assert.That(TestStandInstallLocator.NormalizeBinDirectory($"  \"{bin}\"  "), Is.EqualTo(bin));
    }

    [Test]
    public void NormalizeBinDirectory_MissingOrBlank_ReturnsNull()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TestStandInstallLocator.NormalizeBinDirectory(null), Is.Null);
            Assert.That(TestStandInstallLocator.NormalizeBinDirectory("   "), Is.Null);
            Assert.That(TestStandInstallLocator.NormalizeBinDirectory(Path.Combine(_tmp, "nope")),
                Is.Null, "a typo must be reportable, not silently ignored");
        });
    }

    [Test]
    public void Resolve_ExplicitOverride_BeatsTheEngineBin()
    {
        // The override exists precisely because the automatic order picked the wrong install,
        // so it must outrank even the connected engine's own Bin.
        var engineBin   = MakeBin("engine",   "FileDiffer.exe");
        var overrideBin = MakeBin("override", "FileDiffer.exe");

        var (binDir, _) = TestStandInstallLocator.Resolve("FileDiffer.exe", engineBin, overrideBin);

        Assert.That(binDir, Is.EqualTo(overrideBin));
    }

    [Test]
    public void Resolve_ExplicitOverrideLackingTheTool_FallsThroughAndIsListedInTheTrail()
    {
        // An override that does not hold the tool must not dead-end the search.
        var engineBin   = MakeBin("engine",   "FileDiffer.exe");
        var overrideBin = MakeBin("override", "SomethingElse.exe");

        var (binDir, _) = TestStandInstallLocator.Resolve("FileDiffer.exe", engineBin, overrideBin);
        var (_, trail)  = TestStandInstallLocator.Resolve("NoSuchNiTool_9f3a.exe", engineBin, overrideBin);

        Assert.Multiple(() =>
        {
            Assert.That(binDir, Is.EqualTo(engineBin));
            Assert.That(trail, Does.Contain("engine_path override"));
        });
    }

    [Test]
    public void ConnectAsync_NonExistentEnginePath_ThrowsInsteadOfSilentlyIgnoringIt()
    {
        // Regression: engine_path used to be accepted and then dropped on the floor. Validation
        // happens before any engine is created, so this never spins up a second engine.
        var svc = new TestStandService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TestStandService>.Instance);

        Assert.That(async () => await svc.ConnectAsync(Path.Combine(_tmp, "no_such_teststand")),
            Throws.InstanceOf<DirectoryNotFoundException>().And.Message.Contains("engine_path"));
    }

    // ── Child environment: fill, never overwrite (defect 4) ─────────────────────

    [Test]
    public void ApplyTestStandToolChildEnv_SuppliesTheMissingSystemFolders()
    {
        var psi = new ProcessStartInfo { UseShellExecute = false };
        psi.Environment.Clear();   // simulate the stripped environment a stdio launcher can hand us

        TestStandService.ApplyTestStandToolChildEnv(psi);

        Assert.Multiple(() =>
        {
            Assert.That(psi.Environment["ProgramFiles(x86)"], Is.Not.Null.And.Not.Empty,
                "lvrt.dll builds its paths from %ProgramFiles(x86)% and crashes without it");
            Assert.That(psi.Environment["ProgramData"],   Is.Not.Null.And.Not.Empty);
            Assert.That(psi.Environment["ComSpec"],       Is.Not.Null.And.Not.Empty);
            Assert.That(psi.Environment["TEMP"],          Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void ApplyTestStandToolChildEnv_SuppliesTheSixtyFourBitRootNames()
    {
        Assume.That(Environment.GetEnvironmentVariable("ProgramW6432"), Is.Not.Null.And.Not.Empty);

        var psi = new ProcessStartInfo { UseShellExecute = false };
        psi.Environment.Clear();

        TestStandService.ApplyTestStandToolChildEnv(psi);

        Assert.Multiple(() =>
        {
            // A 64-bit NI tool reads these to find the 64-bit tree; previously they were never set.
            Assert.That(psi.Environment["ProgramW6432"],
                Is.EqualTo(Environment.GetEnvironmentVariable("ProgramW6432")));
            Assert.That(psi.Environment["ProgramW6432"],
                Is.Not.EqualTo(psi.Environment["ProgramFiles(x86)"]),
                "the 64-bit root must not collapse onto the x86 one");
        });
    }

    [Test]
    public void ApplyTestStandToolChildEnv_DoesNotOverwriteValuesTheChildAlreadyHas()
    {
        // Fill-only: ComposeChildEnvironmentFromRegistry merges the real logon environment first and
        // calls this as a gap-filler, so an already-correct value must survive. Overwriting would
        // also force this x86 host's WOW64-redirected paths onto a 64-bit child.
        var psi = new ProcessStartInfo { UseShellExecute = false };
        psi.Environment.Clear();
        psi.Environment["ProgramFiles"]       = @"X:\Custom\Program Files";
        psi.Environment["CommonProgramFiles"] = @"X:\Custom\Common";

        TestStandService.ApplyTestStandToolChildEnv(psi);

        Assert.Multiple(() =>
        {
            Assert.That(psi.Environment["ProgramFiles"],       Is.EqualTo(@"X:\Custom\Program Files"));
            Assert.That(psi.Environment["CommonProgramFiles"], Is.EqualTo(@"X:\Custom\Common"));
            Assert.That(psi.Environment["ProgramFiles(x86)"],  Is.Not.Null.And.Not.Empty,
                "genuinely missing variables are still filled in");
        });
    }
}
