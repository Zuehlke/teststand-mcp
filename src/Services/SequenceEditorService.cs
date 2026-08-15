using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using TestStandMCP.Models;
using Microsoft.Extensions.Logging;

namespace TestStandMCP.Services;

// ── Interface ────────────────────────────────────────────────────────────────

/// <summary>Controls the external NI TestStand Sequence Editor (SeqEdit.exe) process.</summary>
public interface ISequenceEditorService : IDisposable
{
    /// <summary>True when a Sequence Editor process is running.</summary>
    bool IsRunning { get; }
    /// <summary>Launches the Sequence Editor (or attaches to a running instance).
    /// <paramref name="environmentPath"/> is the TestStand environment the server itself runs in; it
    /// is passed on as SeqEdit's <c>/env</c> switch so the editor does not come up in the global
    /// station configuration while the server works in a product environment.</summary>
    Task<bool> LaunchAsync(string? seqEditPath = null, string? environmentPath = null);
    /// <summary>Returns the current Sequence Editor status.</summary>
    Task<SequenceEditorInfo> GetStatusAsync();
    /// <summary>Opens a sequence file in the editor, in the given TestStand environment.</summary>
    Task OpenFileAsync(string filePath, string? environmentPath = null);
    /// <summary>Runs a sequence's entry point in the editor, in the given TestStand environment.</summary>
    Task<string> RunSequenceAsync(string sequenceFilePath, string entryPoint, string? environmentPath = null);
    /// <summary>Closes the editor, optionally forcing termination.</summary>
    Task CloseEditorAsync(bool force = false);
}

// ── Implementation ────────────────────────────────────────────────────────────

/// <summary>Default <see cref="ISequenceEditorService"/> backed by the SeqEdit.exe process.</summary>
public class SequenceEditorService : ISequenceEditorService
{
    private const string EditorProcessName = "SeqEdit";

    private readonly ILogger<SequenceEditorService> _logger;
    private Process? _editorProcess;
    private string? _resolvedPath;

    /// <inheritdoc/>
    public bool IsRunning
    {
        get
        {
            try
            {
                if (_editorProcess != null && !_editorProcess.HasExited)
                    return true;
                var procs = Process.GetProcessesByName(EditorProcessName);
                try { return procs.Length > 0; }
                finally { DisposeAll(procs); }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _logger.LogDebug(ex, "Failed to probe Sequence Editor process state.");
                return false;
            }
        }
    }

    /// <summary>Creates the service with the given logger.</summary>
    public SequenceEditorService(ILogger<SequenceEditorService> logger)
    {
        _logger = logger;
    }

    // ── Launch / Connect ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> LaunchAsync(string? seqEditPath = null, string? environmentPath = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                _resolvedPath = seqEditPath ?? FindSeqEditPath();
                if (string.IsNullOrEmpty(_resolvedPath) || !File.Exists(_resolvedPath))
                    throw new FileNotFoundException(
                        "Could not find SeqEdit.exe. Provide the path explicitly or ensure " +
                        "NI TestStand is installed and the TESTSTANDBIN environment variable is set.");

                // Check if already running
                var existing = Process.GetProcessesByName(EditorProcessName);
                if (existing.Length > 0)
                {
                    _editorProcess = existing[0];
                    // Release the handles we are not keeping.
                    for (int i = 1; i < existing.Length; i++) existing[i].Dispose();
                    _logger.LogInformation(
                        "Sequence Editor already running (PID: {Pid})", _editorProcess.Id);
                    // SeqEdit is single-instance, so an already-running editor keeps whatever
                    // environment IT was started with — /env cannot retarget it, and this process has
                    // no way to read which one that is. Say so rather than implying a match.
                    if (!string.IsNullOrWhiteSpace(environmentPath))
                        _logger.LogWarning(
                            "Attached to an already-running Sequence Editor; it keeps the environment " +
                            "it was started with, which may differ from this server's '{Env}'.",
                            environmentPath);
                    return true;
                }

                _logger.LogInformation("Launching Sequence Editor: {Path}", _resolvedPath);
                _editorProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = _resolvedPath,
                    Arguments = TestStandEnvironmentLocator.PrependEnvSwitch("", environmentPath),
                    UseShellExecute = true
                });

                if (_editorProcess == null)
                    throw new InvalidOperationException("Failed to start SeqEdit.exe process.");

                _logger.LogInformation(
                    "Sequence Editor launched (PID: {Pid})", _editorProcess.Id);
                return true;
            }
            catch (FileNotFoundException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to launch Sequence Editor");
                return false;
            }
        });
    }

    // ── Status ────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<SequenceEditorInfo> GetStatusAsync()
    {
        return await Task.Run(() =>
        {
            var processes = Process.GetProcessesByName(EditorProcessName);
            try
            {
                var info = new SequenceEditorInfo
                {
                    IsRunning  = processes.Length > 0,
                    EditorPath = _resolvedPath ?? ""
                };

                if (processes.Length > 0)
                {
                    var proc = processes[0];
                    info.ProcessId = proc.Id;
                    try { info.MainWindowTitle = proc.MainWindowTitle; }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Could not read Sequence Editor window title.");
                        info.MainWindowTitle = "";
                    }
                }

                return info;
            }
            finally { DisposeAll(processes); }
        });
    }

    // ── Open File ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task OpenFileAsync(string filePath, string? environmentPath = null)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Sequence file not found: {filePath}");

        await Task.Run(() =>
        {
            var editorPath = _resolvedPath ?? FindSeqEditPath()
                ?? throw new FileNotFoundException(
                    "Could not find SeqEdit.exe. Launch the editor first or provide the path.");

            _logger.LogInformation("Opening file in Sequence Editor: {Path}", filePath);

            // Launching seqedit.exe with a file argument opens it in the existing instance if the
            // editor is already running (single-instance behavior) — in which case /env is moot,
            // because that instance keeps the environment it was started with. The switch therefore
            // only decides the environment when THIS call is what starts the editor.
            if (!string.IsNullOrWhiteSpace(environmentPath) && IsRunning)
                _logger.LogWarning(
                    "The Sequence Editor is already running, so the file opens in that instance and " +
                    "keeps its environment — which may differ from this server's '{Env}'.",
                    environmentPath);

            Process.Start(new ProcessStartInfo
            {
                FileName  = editorPath,
                Arguments = TestStandEnvironmentLocator.PrependEnvSwitch($"\"{filePath}\"", environmentPath),
                UseShellExecute = true
            })?.Dispose();
        });
    }

    // ── Run Sequence ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<string> RunSequenceAsync(string sequenceFilePath, string entryPoint,
                                               string? environmentPath = null)
    {
        if (!File.Exists(sequenceFilePath))
            throw new FileNotFoundException(
                $"Sequence file not found: {sequenceFilePath}");

        return await Task.Run(() =>
        {
            var editorPath = _resolvedPath ?? FindSeqEditPath()
                ?? throw new FileNotFoundException(
                    "Could not find SeqEdit.exe. Launch the editor first or provide the path.");

            _logger.LogInformation(
                "Running sequence in editor: {File} / {Entry}",
                sequenceFilePath, entryPoint);

            // This one EXECUTES test code. Running it in the global station configuration while the
            // server works in a product environment means the wrong process models, type palettes and
            // station globals — the most consequential of the environment mismatches.
            var args = TestStandEnvironmentLocator.PrependEnvSwitch(
                $"\"{sequenceFilePath}\" /run /runEntryPoint \"{entryPoint}\"", environmentPath);
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName  = editorPath,
                Arguments = args,
                UseShellExecute = true
            });

            return $"Execution started in Sequence Editor. " +
                   $"File: {sequenceFilePath}, Entry Point: {entryPoint}" +
                   (process != null ? $", PID: {process.Id}" : "");
        });
    }

    // ── Close Editor ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task CloseEditorAsync(bool force = false)
    {
        await Task.Run(() =>
        {
            var processes = Process.GetProcessesByName(EditorProcessName);
            if (processes.Length == 0)
            {
                _logger.LogInformation("Sequence Editor is not running.");
                return;
            }

            foreach (var proc in processes)
            {
                try
                {
                    if (force)
                    {
                        proc.Kill();
                        _logger.LogInformation(
                            "Forcefully terminated Sequence Editor (PID: {Pid})", proc.Id);
                    }
                    else
                    {
                        proc.CloseMainWindow();
                        _logger.LogInformation(
                            "Sent close request to Sequence Editor (PID: {Pid})", proc.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to close Sequence Editor (PID: {Pid})", proc.Id);
                }
                finally
                {
                    proc.Dispose();
                }
            }
            _editorProcess = null;
        });
    }

    // ── Path Resolution ───────────────────────────────────────────────────────

    private static string? FindSeqEditPath()
    {
        // 1. Check TESTSTANDBIN environment variable
        var tsBin = Environment.GetEnvironmentVariable("TESTSTANDBIN");
        if (!string.IsNullOrEmpty(tsBin))
        {
            var seqEdit = Path.Combine(tsBin, "SeqEdit.exe");
            if (File.Exists(seqEdit)) return seqEdit;
        }

        // 2. Check TESTSTANDPUBLIC (go up to installation root, then Bin)
        var tsPublic = Environment.GetEnvironmentVariable("TESTSTANDPUBLIC");
        if (!string.IsNullOrEmpty(tsPublic))
        {
            var root = Path.GetDirectoryName(tsPublic);
            if (root != null)
            {
                var seqEdit = Path.Combine(root, "Bin", "SeqEdit.exe");
                if (File.Exists(seqEdit)) return seqEdit;
            }
        }

        // 3. Search the standard NI installation directories, newest release first. Delegated to
        //    TestStandInstallLocator: iterating SpecialFolder.ProgramFiles + ProgramFilesX86 here
        //    would scan the SAME directory twice (WOW64 redirects both to "…(x86)" in this 32-bit
        //    host) and could never find a 64-bit install.
        foreach (var root in TestStandInstallLocator.GetProgramFilesRoots())
        {
            foreach (var bin in TestStandInstallLocator.EnumerateTestStandBins(root))
            {
                var seqEdit = Path.Combine(bin, "SeqEdit.exe");
                if (File.Exists(seqEdit)) return seqEdit;
            }
        }

        return null;
    }

    /// <summary>Releases the OS handles held by a set of <see cref="Process"/> objects.</summary>
    private static void DisposeAll(Process[] processes)
    {
        foreach (var p in processes) p.Dispose();
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    /// <summary>Releases the editor process handle. Does not terminate the editor.</summary>
    public void Dispose()
    {
        // Dispose the handle only — never kill an editor the user may still be using.
        _editorProcess?.Dispose();
        _editorProcess = null;
        GC.SuppressFinalize(this);
    }
}
