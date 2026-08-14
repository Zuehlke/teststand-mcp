using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Win32.SafeHandles;
using TestStandMCP.Services;
using TestStandMCP.Tools;

namespace TestStandMCP;

internal class Program
{
    [DllImport("kernel32.dll")] private static extern bool AllocConsole();
    [DllImport("kernel32.dll")] private static extern bool SetConsoleTitle(string title);
    [DllImport("kernel32.dll")] private static extern bool SetConsoleOutputCP(uint cp);
    [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CreateFile(string name, uint access, uint share,
        IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);
    private const uint GENERIC_WRITE  = 0x40000000;
    private const uint FILE_SHARE_WRITE = 2;
    private const uint OPEN_EXISTING  = 3;
    private const int  STD_ERROR_HANDLE = -12;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    static async Task<int> Main(string[] args)
    {
        // Out-of-process prototype-load worker (spawned by the server itself). Handle it FIRST —
        // before any console/banner/DI setup — so it stays silent on stdout except for its single
        // result line, and never allocates a console window. It owns its own short-lived engine,
        // performs the (possibly process-fatal) native LabVIEW Load Prototype, and hard-exits. See
        // LoadPrototypeWorker for the rationale (isolating the 0xC06D007E .lvlibp delay-load crash).
        if (args.Length > 0 && args[0] == "--load-prototype-worker")
            return await LoadPrototypeWorker.RunAsync(args);

        // When launched as an MCP subprocess stdin is redirected — allocate a visible
        // console window so Console.Error output (banner + command panel) is visible.
        if (Console.IsInputRedirected)
        {
            AllocConsole();
            SetConsoleTitle("TestStand MCP Server");
            SetConsoleOutputCP(65001); // UTF-8

            // Re-point Console.Error at the new console window (CONOUT$)
            var handle = CreateFile("CONOUT$", GENERIC_WRITE, FILE_SHARE_WRITE,
                                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle != new IntPtr(-1))
            {
                // Windows 10 conhost does NOT enable ANSI/VT processing on a freshly
                // AllocConsole'd output buffer, so the banner's color + OSC-8 hyperlink
                // escapes would be printed literally. (Windows 11 conhost enables it by
                // default — hence the banner renders fine on the dev machine but not here.)
                EnableVirtualTerminalProcessing(handle);

                var stream = new FileStream(new SafeFileHandle(handle, true), FileAccess.Write);
                Console.SetError(new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true });
            }
        }
        else
        {
            // Direct launch (stdin not redirected): the inherited console may also lack VT
            // processing on Windows 10 — enable it on the stderr handle the banner uses.
            EnableVirtualTerminalProcessing(GetStdHandle(STD_ERROR_HANDLE));
        }

        // OSC 8 hyperlink: clickable "Zühlke" in terminals that support ANSI hyperlinks
        // (Windows Terminal, VS Code terminal, etc.)
        const string url   = "https://www.zuehlke.com/en/industries/industrial-sector";
        const string osc8  = "\x1b]8;;";
        const string st    = "\x1b\\";
        // ANSI: light-violet background (RGB 180 130 220), bright-white foreground, bold
        const string bgOn  = "\x1b[48;2;180;130;220m\x1b[97m\x1b[1m";
        const string bgOff = "\x1b[0m";
        Console.Error.WriteLine($"{bgOn}  TestStand MCP: developed by {osc8}{url}{st}Zühlke{osc8}{st} Claude  {bgOff}");
        Console.Error.WriteLine();

        // Explicit opt-in maintenance command — NEVER runs on the normal MCP serve path.
        // Mirrors .claude\agents\link-agents.bat: junctions %USERPROFILE%\.claude\agents to
        // the .claude\agents folder shipped next to this exe, so Claude Code sees the deployed
        // agents user-wide. Meaningless under non-Claude hosts (Copilot etc.) that don't read
        // ~/.claude/agents — which is exactly why it stays a deliberate manual step.
        if (args.Contains("--setup-agents"))
            return SetupAgents();

        // ── Configuration ─────────────────────────────────────────────────────
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables("TESTSTAND_MCP_")
            .AddCommandLine(args)
            .Build();

        // ── Dependency Injection ──────────────────────────────────────────────
        var services = new ServiceCollection();

        // Logging: write to stderr so stdout remains clean for MCP JSON-RPC
        services.AddLogging(b =>
        {
            b.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
            b.SetMinimumLevel(ParseLogLevel(
                config["Logging:LogLevel:Default"] ?? "Information"));
        });

        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ITestStandService, TestStandService>();
        services.AddSingleton<ISequenceEditorService, SequenceEditorService>();
        services.AddSingleton<TestStandToolRegistry>();
        services.AddSingleton<TestStandResourceProvider>();
        services.AddSingleton<TestStandPromptProvider>();
        services.AddSingleton<McpServer>();

        var sp = services.BuildServiceProvider();

        // ── Station defaults ──────────────────────────────────────────────────
        // Read here rather than injecting IConfiguration into the service, so TestStandService stays
        // framework-agnostic (and constructible from the tests with nothing but a logger). The
        // TestStand ENVIRONMENT belongs in configuration, not in a per-call parameter: it can only be
        // chosen before the engine is created, so it is a property of the server process, and MCP
        // hosts configure a server exactly once — through args/env in their mcp.json.
        // Deliberately NOT reading the older TestStand:AutoConnect / TestStand:EnginePath keys: they
        // have never been consumed, and quietly activating them now would change behaviour on any
        // station that filled them in expecting them to work.
        sp.GetRequiredService<ITestStandService>().ApplyStationDefaults(
            environmentPath:       config["TestStand:EnvironmentPath"],
            environmentAutoDetect: bool.TryParse(config["TestStand:EnvironmentAutoDetect"], out var autoDetect) && autoDetect,
            connectTimeoutSeconds: int.TryParse(config["TestStand:ConnectTimeoutSeconds"], out var timeout) ? timeout : 0);

        // ── Run ───────────────────────────────────────────────────────────────
        var logger = sp.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("TestStand MCP Server v1.0.0 starting...");
        logger.LogInformation("Platform: {OS}", Environment.OSVersion);
        logger.LogInformation("Runtime: {Runtime}", RuntimeInformation.FrameworkDescription);

        // Handle --version flag
        if (args.Contains("--version"))
        {
            Console.WriteLine("TestStand MCP Server 1.0.0");
            return 0;
        }

        // Handle --list-tools flag (useful for debugging)
        if (args.Contains("--list-tools"))
        {
            var registry = sp.GetRequiredService<TestStandToolRegistry>();
            Console.Error.WriteLine("\nAvailable Tools:");
            foreach (var tool in registry.GetTools())
                Console.Error.WriteLine($"  {tool.Name,-35} {tool.Description}");
            Console.Error.WriteLine();
            return 0;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            logger.LogInformation("Shutdown signal received.");
            cts.Cancel();
        };

        var server = sp.GetRequiredService<McpServer>();

        int exitCode;
        try
        {
            await server.RunAsync(cts.Token);
            exitCode = 0;
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Server shut down cleanly.");
            exitCode = 0;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Fatal error");
            exitCode = 1;
        }
        finally
        {
            // Ensure TestStand engine is released (ShutDown + suppress the last engine's RCW
            // finalization — see DisconnectAsync).
            var ts = sp.GetService<ITestStandService>();
            ts?.Dispose();
        }

        // Hard-terminate to skip CLR shutdown finalization. After the engine has been ShutDown,
        // the lingering TestStand / NI License-Manager (NILM) COM RCWs otherwise crash the .NET 8
        // runtime during finalization at process exit (fast-fail 0xC0000409) and can raise a
        // Windows Error Reporting dialog. TerminateProcess exits immediately with our exit code,
        // before the runtime touches those RCWs. (Mirrors the integration-test ProcessExit guard.)
        Console.Out.Flush();
        Console.Error.Flush();
        TerminateProcess(GetCurrentProcess(), (uint)exitCode);
        return exitCode; // not reached
    }

    /// <summary>
    /// Creates a directory junction %USERPROFILE%\.claude\agents → the .claude\agents folder
    /// shipped next to this executable, mirroring .claude\agents\link-agents.bat so Claude Code
    /// picks up the deployed agents user-wide. Junctions need no admin rights. Aborts (non-zero)
    /// if the target already exists as a REAL directory, so a user's own agents are never clobbered.
    /// Explicit opt-in only — this is never invoked on the MCP serve path.
    /// </summary>
    private static int SetupAgents()
    {
        string src  = Path.Combine(AppContext.BaseDirectory, ".claude", "agents")
                          .TrimEnd(Path.DirectorySeparatorChar);
        string link = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "agents");

        Console.Error.WriteLine();
        Console.Error.WriteLine($"  Junction : {link}");
        Console.Error.WriteLine($"  Target   : {src}");
        Console.Error.WriteLine();

        if (!Directory.Exists(src))
        {
            Console.Error.WriteLine($"  [ERROR] Agents source folder not found: {src}");
            return 1;
        }

        // Ensure the parent .claude directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);

        // Handle an existing target
        if (Directory.Exists(link))
        {
            bool isReparse = (new DirectoryInfo(link).Attributes & FileAttributes.ReparsePoint) != 0;
            if (isReparse)
            {
                Console.Error.WriteLine("  Existing link found - removing and recreating...");
                Directory.Delete(link, false); // removes only the junction, not the target contents
            }
            else
            {
                Console.Error.WriteLine($"  [ABORTED] \"{link}\" already exists as a real directory.");
                Console.Error.WriteLine("            Please back up/remove its contents and run again.");
                return 1;
            }
        }

        // Create the junction via cmd's mklink /J (needs no admin rights; Directory.CreateSymbolicLink
        // would create a symlink requiring privilege / Developer Mode).
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{src}\"")
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.StandardOutput.ReadToEnd();
        string err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        if (proc.ExitCode != 0)
        {
            Console.Error.WriteLine($"  [ERROR] Failed to create the junction. {err.Trim()}");
            return 1;
        }

        Console.Error.WriteLine("  Done. All projects now use the agents from this folder.");
        Console.Error.WriteLine();
        return 0;
    }

    /// <summary>
    /// Best-effort: turn on ENABLE_VIRTUAL_TERMINAL_PROCESSING so the console interprets ANSI
    /// escape sequences (24-bit color, OSC-8 hyperlinks) instead of printing them as raw text.
    /// No-op when the handle is not a real console (e.g. output redirected to a pipe/file).
    /// </summary>
    private static void EnableVirtualTerminalProcessing(IntPtr handle)
    {
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return;
        if (GetConsoleMode(handle, out uint mode))
            SetConsoleMode(handle, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
    }

    private static LogLevel ParseLogLevel(string level) => level.ToLowerInvariant() switch
    {
        "trace"       => LogLevel.Trace,
        "debug"       => LogLevel.Debug,
        "information" => LogLevel.Information,
        "warning"     => LogLevel.Warning,
        "error"       => LogLevel.Error,
        "critical"    => LogLevel.Critical,
        _             => LogLevel.Information
    };
}
