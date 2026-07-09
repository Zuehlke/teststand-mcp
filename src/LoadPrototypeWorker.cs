using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestStandMCP.Services;

namespace TestStandMCP;

/// <summary>
/// Out-of-process worker for <c>load_module_prototype</c>. The native "Load Prototype" of a LabVIEW
/// VI — especially one inside a packed library (<c>.lvlibp</c>) — can raise the MSVC delay-load SEH
/// <c>0xC06D007E</c> (ERROR_MOD_NOT_FOUND) when a LabVIEW-runtime/adapter DLL cannot be bound in the
/// host process. That exception is process-fatal and escapes managed <c>try/catch</c>, so it would
/// take the whole MCP server down. To make that impossible, the server spawns a short-lived child
/// instance of itself (this worker) that owns its OWN engine and performs the load; if it crashes,
/// only the child dies — the server survives and reports a clean failure.
///
/// A native fault MUST kill the worker SILENTLY — no Windows Error Reporting box and, crucially, no
/// NI Error Reporter ("…encountered a problem and needs to close") dialog. NI installs its reporter
/// through an IN-PROCESS unhandled-exception hook, so merely calling <c>SetErrorMode</c> (which only
/// suppresses the OS's own fault box) is not enough. The guards installed here — chiefly a
/// <b>vectored exception handler</b> that terminates on the delay-load SEH family before any
/// frame-based / unhandled handler (i.e. before NI's hook) can run — make the death silent.
///
/// Contract: the worker prints exactly one result line to STDOUT,
/// <c>__LPWORKER_RESULT__ {"loaded":bool,"adapter":"…","note":"…|null","paramCount":N}</c>, then
/// hard-terminates (skipping CLR finalization, like the main serve path). All logging goes to STDERR.
/// A native crash prints no result line and exits abnormally — which the parent reads as "crashed".
/// </summary>
internal static class LoadPrototypeWorker
{
    [DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint uMode);
    [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern uint WerSetFlags(uint dwFlags);
    [DllImport("kernel32.dll")] private static extern IntPtr AddVectoredExceptionHandler(uint first, VectoredHandler handler);
    [DllImport("kernel32.dll")] private static extern IntPtr SetUnhandledExceptionFilter(TopLevelExceptionFilter filter);
    [DllImport("kernel32.dll")] private static extern void RaiseException(uint dwExceptionCode, uint dwExceptionFlags, uint nNumberOfArguments, IntPtr lpArguments);
    [DllImport("wer.dll", CharSet = CharSet.Unicode)] private static extern int WerAddExcludedApplication(string pwzExeName, [MarshalAs(UnmanagedType.Bool)] bool bAllUsers);
    [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl)] private static extern uint _set_abort_behavior(uint flags, uint mask);

    private const uint SEM_FAILCRITICALERRORS = 0x0001;
    private const uint SEM_NOGPFAULTERRORBOX  = 0x0002;
    private const uint SEM_NOOPENFILEERRORBOX = 0x8000;
    private const uint WER_FAULT_REPORTING_NO_UI = 0x0020;
    private const uint _WRITE_ABORT_MSG  = 0x1;
    private const uint _CALL_REPORTFAULT = 0x2;
    private const uint EXCEPTION_NONCONTINUABLE  = 0x1;
    private const int  EXCEPTION_CONTINUE_SEARCH = 0;   // let normal (handled) exceptions flow

    // The MSVC delay-load helper raises VcppException(ERROR_SEVERITY_ERROR, win32err) whose high word
    // is 0xC06D — e.g. 0xC06D007E (ERROR_MOD_NOT_FOUND) / 0xC06D007F (ERROR_PROC_NOT_FOUND). Matching
    // the whole family (not just 007E) covers every "a dependent DLL/entry point is missing" fault
    // from binding a LabVIEW runtime in-process. Normal C++ EH is 0xE06D7363 → different high word,
    // so this never fires on ordinary handled exceptions.
    private const uint DELAYLOAD_SEH_HIWORD = 0xC06D0000;
    private const uint DELAYLOAD_SEH_MASK   = 0xFFFF0000;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int VectoredHandler(IntPtr exceptionPointers);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int TopLevelExceptionFilter(IntPtr exceptionPointers);

    // Rooted so the GC never collects the native callback thunks while Windows holds their pointers.
    private static readonly VectoredHandler       _veh = OnVectoredException;
    private static readonly TopLevelExceptionFilter _uef = OnUnhandledException;

    public static async Task<int> RunAsync(string[] args)
    {
        // Install every "die silently on a native fault" guard BEFORE the engine/LabVIEW adapter is
        // touched, so no WER box and no NI Error Reporter dialog can ever appear (and the parent's
        // WaitForExit returns promptly instead of blocking ~60s on a modal dialog).
        InstallSilentDeathGuards();

        Console.OutputEncoding = new UTF8Encoding(false);

        string? file = GetArg(args, "--file");
        string? seq  = GetArg(args, "--seq");
        string? grp  = GetArg(args, "--group");
        string? step = GetArg(args, "--step");
        string  lvsrv = GetArg(args, "--lv-server") ?? "deferred";

        // Logger → STDERR only (keeps STDOUT clean for the single result line).
        using var loggerFactory = LoggerFactory.Create(b =>
        {
            b.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
            b.SetMinimumLevel(LogLevel.Warning);
        });
        var log = loggerFactory.CreateLogger("LoadPrototypeWorker");

        if (file == null || seq == null || grp == null || step == null)
        {
            WriteResult(false, "", "Worker invoked with missing arguments.", 0);
            HardExit(0);
            return 0;
        }

        var svc = new TestStandService(loggerFactory.CreateLogger<TestStandService>());
        try
        {
            if (!await svc.ConnectAsync())
            {
                WriteResult(false, "", "Worker could not connect to the TestStand engine.", 0);
                HardExit(0);
            }
            await svc.OpenSequenceFileAsync(file);

            // Fault injection (tests only) — emulate the user's native crash so the parent's
            // crash-survival AND the silent-death guards can be verified on a box that does not
            // reproduce the fault naturally:
            //   "raise" → RaiseException(0xC06D007E): exercises the vectored-handler suppression path
            //             exactly like the real delay-load SEH (must die silently, no dialog).
            //   "1"     → direct TerminateProcess: emulates an already-fatal exit.
            string? sim = Environment.GetEnvironmentVariable("TESTSTAND_MCP_LP_SIMULATE_CRASH");
            if (sim == "raise")
            {
                log.LogWarning("SIMULATE_CRASH=raise — raising 0xC06D007E to exercise silent-death guards.");
                Console.Out.Flush();
                RaiseException(0xC06D007E, EXCEPTION_NONCONTINUABLE, 0, IntPtr.Zero);
            }
            else if (sim == "1")
            {
                log.LogWarning("SIMULATE_CRASH=1 — terminating worker with 0xC06D007E.");
                Console.Out.Flush();
                TerminateProcess(GetCurrentProcess(), 0xC06D007E);
            }

            // The actual (potentially process-fatal) native load happens HERE, in the child. The
            // adapter is routed to the LabVIEW ExecServer (running ADE via ActiveX, like the editor)
            // inside LoadPrototypeInProcessAsync — ActiveX works cross-process, so this fresh worker
            // can bind LabVIEW too, giving crash-safety AND a real load together.
            var r = await svc.LoadPrototypeInProcessAsync(file, seq, grp, step, save: true, labviewServer: lvsrv);
            WriteResult(r.PrototypeLoaded, r.Adapter, r.Note, r.Parameters.Count);
        }
        catch (Exception ex)
        {
            // A managed exception is a clean failure (target unresolvable etc.) — report and exit 0.
            log.LogWarning(ex, "Prototype load raised a managed exception in the worker.");
            WriteResult(false, "", "Worker: " + ex.Message, 0);
        }

        HardExit(0);
        return 0; // not reached
    }

    /// <summary>
    /// Layered defense so a native fault kills this worker without any UI:
    /// 1) <c>SetErrorMode</c> — suppress the OS critical-error / GP-fault boxes.
    /// 2) WER: <c>WerSetFlags(NO_UI)</c> + exclude this exe — no "TestStandMCP stopped working" box.
    /// 3) CRT <c>_set_abort_behavior(0,…)</c> — no CRT abort message / WER call from abort().
    /// 4) A <b>vectored exception handler</b> (installed FIRST) that, on the MSVC delay-load SEH
    ///    family (0xC06Dxxxx), terminates immediately — running BEFORE any frame-based / unhandled
    ///    handler, i.e. before NI's in-process Error-Reporter hook can start the green NIER dialog.
    /// 5) An unhandled-exception filter that terminates silently as a backstop for other fatal,
    ///    genuinely-unhandled native faults (AV, stack overflow, …).
    /// All calls are best-effort; a missing/older API must not stop the worker from running.
    /// </summary>
    private static void InstallSilentDeathGuards()
    {
        try { SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX); } catch { }
        try { WerSetFlags(WER_FAULT_REPORTING_NO_UI); } catch { }
        try
        {
            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
                WerAddExcludedApplication(System.IO.Path.GetFileName(exe), false);
        }
        catch { }
        try { _set_abort_behavior(0, _WRITE_ABORT_MSG | _CALL_REPORTFAULT); } catch { }
        // first=1 → inserted at the FRONT of the vectored-handler chain, so it runs before a handler
        // NI may have installed at engine load; and a VEH always runs before the unhandled filter.
        try { AddVectoredExceptionHandler(1, _veh); } catch { }
        try { SetUnhandledExceptionFilter(_uef); } catch { }
    }

    // Vectored handler: fires on EVERY first-chance exception, so it must be minimal and must NOT
    // disturb ordinary (handled) managed/C++ exceptions — it only acts on the delay-load SEH family
    // and otherwise returns CONTINUE_SEARCH. No allocation; only fixed-memory reads + a kernel call.
    private static int OnVectoredException(IntPtr exceptionPointers)
    {
        try
        {
            if (exceptionPointers == IntPtr.Zero) return EXCEPTION_CONTINUE_SEARCH;
            // EXCEPTION_POINTERS { PEXCEPTION_RECORD ExceptionRecord; PCONTEXT ContextRecord; }
            IntPtr record = Marshal.ReadIntPtr(exceptionPointers);
            if (record == IntPtr.Zero) return EXCEPTION_CONTINUE_SEARCH;
            // EXCEPTION_RECORD.ExceptionCode is the first DWORD.
            uint code = unchecked((uint)Marshal.ReadInt32(record));
            if ((code & DELAYLOAD_SEH_MASK) == DELAYLOAD_SEH_HIWORD)
                TerminateProcess(GetCurrentProcess(), code);
        }
        catch { /* a guard must never raise; fall through to continue-search */ }
        return EXCEPTION_CONTINUE_SEARCH;
    }

    // Backstop for a truly-unhandled native fault the vectored handler did not match: die silently.
    private static int OnUnhandledException(IntPtr exceptionPointers)
    {
        TerminateProcess(GetCurrentProcess(), 0xC06D007E);
        return 1; // EXCEPTION_EXECUTE_HANDLER (unreached — the process is already gone)
    }

    private static void WriteResult(bool loaded, string adapter, string? note, int paramCount)
    {
        var payload = JsonSerializer.Serialize(new
        {
            loaded,
            adapter,
            note,
            paramCount
        });
        Console.Out.WriteLine(TestStandService.WorkerResultSentinel + payload);
        Console.Out.Flush();
    }

    // Hard-terminate to skip CLR/RCW finalization — the same rationale as the main serve path:
    // after a TestStand engine has been used, letting the runtime finalize the lingering COM RCWs
    // at process exit can fast-fail. This also guarantees the worker never hangs on teardown.
    private static void HardExit(uint code)
    {
        try { Console.Out.Flush(); } catch { }
        try { Console.Error.Flush(); } catch { }
        TerminateProcess(GetCurrentProcess(), code);
    }

    private static string? GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.Ordinal))
                return args[i + 1];
        return null;
    }
}
