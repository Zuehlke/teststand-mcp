using System;
using System.IO;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using TestStandMCP.Services;

namespace TestStandMCP.IntegrationTests;

// ── Assembly-level one-time setup ────────────────────────────────────────────
// Creates ONE TestStandService for the entire test assembly so that
// the expensive COM engine connection is made once and cleaned up once,
// avoiding the COM STA/MTA deadlock that occurs when rapidly
// creating and destroying Engine instances between fixtures.

[SetUpFixture]
public class AssemblySetup
{
    private static ITestStandService? _ts;
    private static ILoggerFactory?    _loggerFactory;

    public static ITestStandService Ts =>
        _ts ?? throw new InvalidOperationException("TestStandService not initialized");

    [OneTimeSetUp]
    public void ConnectOnce()
    {
        _loggerFactory = LoggerFactory.Create(b =>
        {
            b.AddConsole();
            b.SetMinimumLevel(LogLevel.Warning);
        });

        var logger = _loggerFactory.CreateLogger<TestStandService>();
        _ts = new TestStandService(logger);

        var connected = _ts.ConnectAsync().GetAwaiter().GetResult();
        if (!connected)
            throw new Exception(
                "Could not connect to TestStand engine. " +
                "Ensure TestStand is installed and licensed.");
    }

    // Hard process termination that skips CLR shutdown finalization entirely. The lingering
    // TestStand engine / NI License-Manager (NILM) COM RCWs otherwise park the finalizer thread
    // on EventPairLow LPC waits during normal shutdown, hanging the test host long after every
    // result has been reported. TerminateProcess(GetCurrentProcess(), 0) exits cleanly (code 0)
    // without running those finalizers.
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [OneTimeTearDown]
    public void DisconnectOnce()
    {
        _ts?.Dispose();
        _ts = null;
        _loggerFactory?.Dispose();
        _loggerFactory = null;

        Console.Out.Flush();
        Console.Error.Flush();

        // Do NOT call Environment.Exit() here: OneTimeTearDown runs BEFORE the NUnit adapter
        // sends its ExecutionComplete message to the vstest runner, so terminating now drops the
        // socket mid-session and vstest reports "Test host process crashed" (exit 1) even though
        // every test passed. Instead we let teardown return normally so the adapter completes the
        // protocol, then hard-terminate from ProcessExit — which fires only once the CLR has begun
        // shutting the host down (i.e. after ExecutionComplete was delivered), right before it
        // would otherwise hang finalizing the TestStand/NILM COM RCWs.
        AppDomain.CurrentDomain.ProcessExit += (_, __) =>
        {
            TerminateProcess(GetCurrentProcess(), 0);
        };
    }
}

// ── Per-fixture base class ────────────────────────────────────────────────────

/// <summary>
/// Base class for all integration test fixtures.
/// Provides the shared <see cref="TestStandService"/> instance and
/// a temporary sequence-file path that is cleaned up after each test.
/// </summary>
[TestFixture]
public abstract class TestBase
{
    // Shared service — one connection for the entire test run.
    protected ITestStandService Ts => AssemblySetup.Ts;

    // ── Per-test temporary file ────────────────────────────────────────────────
    protected string TempSeqFile { get; private set; } = "";

    // ── Canonical project-root path ───────────────────────────────────────────
    protected static readonly string ProjectRoot =
        Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            @"..\..\..\..\"));   // Test\TestExecution → project root

    // ─────────────────────────────────────────────────────────────────────────
    [SetUp]
    public void CreateTempFile()
    {
        TempSeqFile = Path.Combine(
            Path.GetTempPath(),
            $"TS_IntTest_{TestContext.CurrentContext.Test.MethodName}_{Guid.NewGuid():N}.seq");
    }

    [TearDown]
    public void DeleteTempFile()
    {
        try
        {
            if (File.Exists(TempSeqFile))
            {
                // Close in TestStand first, then delete from disk
                try { Ts.CloseSequenceFileAsync(TempSeqFile).GetAwaiter().GetResult(); } catch { }
                File.Delete(TempSeqFile);
            }
        }
        catch { /* best-effort cleanup */ }
    }
}
