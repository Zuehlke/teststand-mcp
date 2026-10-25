using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using NUnit.Framework;
using TestStandMCP.Services;

namespace TestStandMCP.UnitTests;

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
/// The fix for (1)-(3) then crossed bitness (an x86 host launched 64-bit tools), so resolution is
/// now same-bitness only: the connected engine's Bin, else the process's own root and view.
/// Outside the TestStandMCP.IntegrationTests namespace, so the assembly SetUpFixture connects no engine.
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

    // ── GetProgramFilesRoot: one root per bitness, WOW64-safe (defect 2) ─────────

    [Test]
    public void GetProgramFilesRoot_SixtyFourBit_IsProgramW6432()
    {
        var w6432 = Environment.GetEnvironmentVariable("ProgramW6432");
        Assume.That(w6432, Is.Not.Null.And.Not.Empty,
            "32-bit-only Windows has no %ProgramW6432% — nothing to reach.");

        Assert.That(TestStandInstallLocator.GetProgramFilesRoot(is64: true), Is.EqualTo(w6432).IgnoreCase,
            "under WOW64 SpecialFolder.ProgramFiles is redirected to the (x86) path and can never name it");
    }

    [Test]
    public void GetProgramFilesRoot_ThirtyTwoBit_IsTheX86Folder()
    {
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        Assume.That(Directory.Exists(pf86), Is.True);

        Assert.That(TestStandInstallLocator.GetProgramFilesRoot(is64: false), Is.EqualTo(pf86).IgnoreCase);
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

    // ── Resolve: the tool actually launched, from the engine's own bitness only ──

    private const string FakeTool = "NoSuchNiTool_9f3a.exe";

    /// <summary>Creates <c>&lt;root&gt;\National Instruments\TestStand 2026\Bin</c> holding the files.</summary>
    private string MakeInstall(string rootName, params string[] files)
    {
        var bin = Path.Combine(_tmp, rootName, "National Instruments", "TestStand 2026", "Bin");
        Directory.CreateDirectory(bin);
        foreach (var f in files) File.WriteAllText(Path.Combine(bin, f), "");
        return bin;
    }

    private static T WithTestStandBin<T>(string? value, Func<T> body)
    {
        var prev = Environment.GetEnvironmentVariable("TESTSTANDBIN");
        try { Environment.SetEnvironmentVariable("TESTSTANDBIN", value); return body(); }
        finally { Environment.SetEnvironmentVariable("TESTSTANDBIN", prev); }
    }

    [Test, Category("EngineFree")]
    public void Resolve_ConnectedEngineLacksTheTool_FailsInsteadOfCrossingBitness()
    {
        // The engine's Bin has no FileDiffer while the other bitness's install has one: returning
        // that one would diff against a different TestStand than the one the server drives.
        var engineBin = MakeInstall("x86root", "AnalyzerApp.exe");
        var otherBin  = MakeInstall("x64root", "FileDiffer.exe");

        var (binDir, probed) = WithTestStandBin(otherBin, () => TestStandInstallLocator.Resolve(
            "FileDiffer.exe", engineBin, is64BitProcess: false,
            programFilesRoot: Path.Combine(_tmp, "x64root")));

        Assert.Multiple(() =>
        {
            Assert.That(binDir, Is.Empty);
            Assert.That(probed, Does.Contain("FileDiffer.exe"), "the error must name the tool");
            Assert.That(probed, Does.StartWith("32-bit process"), "the error must name the bitness");
            Assert.That(probed, Does.Contain(engineBin), "the error must list the probed path");
            Assert.That(probed, Does.Not.Contain(otherBin), "the other bitness is never probed");
        });
    }

    [Test, Category("EngineFree")]
    public void Resolve_PrefersEngineBin_WhenItHoldsTheRequestedTool()
    {
        var engineBin = MakeBin("engine", "AnalyzerApp.exe");
        var otherBin  = MakeBin("other",  "AnalyzerApp.exe");

        var (binDir, _) = WithTestStandBin(otherBin, () => TestStandInstallLocator.Resolve(
            "AnalyzerApp.exe", engineBin, programFilesRoot: _tmp));

        Assert.That(binDir, Is.EqualTo(engineBin));
    }

    [Test, Category("EngineFree")]
    public void Resolve_NotConnected_UsesTheEnvironmentVariableUnderTheOwnRoot()
    {
        var envBin = MakeInstall("own", FakeTool);

        var (binDir, _) = WithTestStandBin(envBin, () => TestStandInstallLocator.Resolve(
            FakeTool, engineBinDir: null, programFilesRoot: Path.Combine(_tmp, "own")));

        Assert.That(binDir, Is.EqualTo(envBin));
    }

    [Test, Category("EngineFree")]
    public void Resolve_NotConnected_IgnoresAnEnvironmentVariableOutsideTheOwnRoot()
    {
        // The installer's %TESTSTANDBIN% names whichever bitness was installed last.
        var foreignBin = MakeInstall("other", FakeTool);
        Directory.CreateDirectory(Path.Combine(_tmp, "own"));

        var (binDir, probed) = WithTestStandBin(foreignBin, () => TestStandInstallLocator.Resolve(
            FakeTool, engineBinDir: null, programFilesRoot: Path.Combine(_tmp, "own")));

        Assert.Multiple(() =>
        {
            Assert.That(binDir, Is.Empty);
            Assert.That(probed, Does.Not.Contain("%TESTSTANDBIN%"));
        });
    }

    [Test, Category("EngineFree")]
    public void Resolve_NotConnected_ScansOnlyTheOwnRoot()
    {
        var x86Bin = MakeInstall("x86root", FakeTool);
        var x64Bin = MakeInstall("x64root", FakeTool);

        var (as64, _) = WithTestStandBin(null, () => TestStandInstallLocator.Resolve(
            FakeTool, engineBinDir: null, is64BitProcess: true, programFilesRoot: Path.Combine(_tmp, "x64root")));
        var (as32, _) = WithTestStandBin(null, () => TestStandInstallLocator.Resolve(
            FakeTool, engineBinDir: null, is64BitProcess: false, programFilesRoot: Path.Combine(_tmp, "x86root")));

        Assert.Multiple(() =>
        {
            Assert.That(as64, Is.EqualTo(x64Bin));
            Assert.That(as32, Is.EqualTo(x86Bin));
        });
    }

    [TestCase(true,  "Registry64", "Registry32")]
    [TestCase(false, "Registry32", "Registry64")]
    public void Resolve_NotConnected_ProbesOnlyTheOwnRegistryView(bool is64, string own, string other)
    {
        Assume.That(TestStandInstallLocator.FindBinFromRegistry(
                is64 ? RegistryView.Registry64 : RegistryView.Registry32),
            Is.Not.Null, $"no TestStand engine registered in {own} on this station");

        var (_, probed) = WithTestStandBin(null, () => TestStandInstallLocator.Resolve(
            FakeTool, engineBinDir: null, is64BitProcess: is64));

        Assert.Multiple(() =>
        {
            Assert.That(probed, Does.Contain(own));
            Assert.That(probed, Does.Not.Contain(other));
        });
    }

    [Test]
    public void Resolve_NotConnected_RealStation_NeverProbesTheOtherRoot()
    {
        var w6432 = Environment.GetEnvironmentVariable("ProgramW6432");
        Assume.That(w6432, Is.Not.Null.And.Not.Empty);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var (as64, trail64) = WithTestStandBin(null, () =>
            TestStandInstallLocator.Resolve(FakeTool, engineBinDir: null, is64BitProcess: true));
        var (as32, trail32) = WithTestStandBin(null, () =>
            TestStandInstallLocator.Resolve(FakeTool, engineBinDir: null, is64BitProcess: false));

        Assert.Multiple(() =>
        {
            Assert.That(as64 + as32, Is.Empty);
            Assert.That(trail64, Does.Not.Contain(pf86), "a 64-bit resolve must not probe the x86 root");
            Assert.That(trail32, Does.Not.Contain(w6432 + @"\National"), "a 32-bit resolve must not probe the 64-bit root");
        });
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

        Assert.That(binDir, Is.EqualTo(overrideBin),
            "the override may even name the other bitness's Bin: the operator chose it");
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
