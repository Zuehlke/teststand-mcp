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
