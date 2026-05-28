using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TestStandMCP.Models;
using Microsoft.Extensions.Logging;

namespace TestStandMCP.Services;

// ── Interface ────────────────────────────────────────────────────────────────

public interface ISequenceEditorService : IDisposable
{
    bool IsRunning { get; }
    Task<bool> LaunchAsync(string? seqEditPath = null);
    Task<SequenceEditorInfo> GetStatusAsync();
    Task OpenFileAsync(string filePath);
    Task<string> RunSequenceAsync(string sequenceFilePath, string entryPoint);
    Task CloseEditorAsync(bool force = false);
}

// ── Implementation ────────────────────────────────────────────────────────────

public class SequenceEditorService : ISequenceEditorService
{
    private readonly ILogger<SequenceEditorService> _logger;
    private Process? _editorProcess;
    private string? _resolvedPath;

    public bool IsRunning
    {
        get
        {
            try
            {
                if (_editorProcess != null && !_editorProcess.HasExited)
                    return true;
                return Process.GetProcessesByName("SeqEdit").Length > 0;
            }
            catch { return false; }
        }
    }

    public SequenceEditorService(ILogger<SequenceEditorService> logger)
    {
        _logger = logger;
    }

    // ── Launch / Connect ──────────────────────────────────────────────────────

    public async Task<bool> LaunchAsync(string? seqEditPath = null)
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
                var existing = Process.GetProcessesByName("SeqEdit");
                if (existing.Length > 0)
                {
                    _editorProcess = existing[0];
                    _logger.LogInformation(
                        "Sequence Editor already running (PID: {Pid})", _editorProcess.Id);
                    return true;
                }

                _logger.LogInformation("Launching Sequence Editor: {Path}", _resolvedPath);
                _editorProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = _resolvedPath,
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

    public async Task<SequenceEditorInfo> GetStatusAsync()
    {
        return await Task.Run(() =>
        {
            var processes = Process.GetProcessesByName("SeqEdit");
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
                catch { info.MainWindowTitle = ""; }
            }

            return info;
        });
    }

    // ── Open File ─────────────────────────────────────────────────────────────

    public async Task OpenFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Sequence file not found: {filePath}");

        await Task.Run(() =>
        {
            var editorPath = _resolvedPath ?? FindSeqEditPath()
                ?? throw new FileNotFoundException(
                    "Could not find SeqEdit.exe. Launch the editor first or provide the path.");

            _logger.LogInformation("Opening file in Sequence Editor: {Path}", filePath);

            // Launching seqedit.exe with a file argument opens it in the existing instance
            // if the editor is already running (single-instance behavior)
            Process.Start(new ProcessStartInfo
            {
                FileName  = editorPath,
                Arguments = $"\"{filePath}\"",
                UseShellExecute = true
            });
        });
    }

    // ── Run Sequence ──────────────────────────────────────────────────────────

    public async Task<string> RunSequenceAsync(string sequenceFilePath, string entryPoint)
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

            var args = $"\"{sequenceFilePath}\" /run /runEntryPoint \"{entryPoint}\"";
            var process = Process.Start(new ProcessStartInfo
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

    public async Task CloseEditorAsync(bool force = false)
    {
        await Task.Run(() =>
        {
            var processes = Process.GetProcessesByName("SeqEdit");
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

        // 3. Search standard NI installation directories
        var programDirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        foreach (var pf in programDirs)
        {
            if (string.IsNullOrEmpty(pf)) continue;
            var niDir = Path.Combine(pf, "National Instruments");
            if (!Directory.Exists(niDir)) continue;

            try
            {
                // Prefer newer TestStand versions
                foreach (var dir in Directory.GetDirectories(niDir, "TestStand*")
                             .OrderByDescending(d => d))
                {
                    var seqEdit = Path.Combine(dir, "Bin", "SeqEdit.exe");
                    if (File.Exists(seqEdit)) return seqEdit;
                }
            }
            catch { }
        }

        return null;
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        _editorProcess = null;
        GC.SuppressFinalize(this);
    }
}
