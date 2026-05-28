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
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CreateFile(string name, uint access, uint share,
        IntPtr security, uint creation, uint flags, IntPtr template);
    private const uint GENERIC_WRITE  = 0x40000000;
    private const uint FILE_SHARE_WRITE = 2;
    private const uint OPEN_EXISTING  = 3;

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
                var stream = new FileStream(new SafeFileHandle(handle, true), FileAccess.Write);
                Console.SetError(new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true });
            }
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
        logger.LogInformation("Runtime: {Runtime}", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);

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

        try
        {
            await server.RunAsync(cts.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Server shut down cleanly.");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Fatal error");
            return 1;
        }
        finally
        {
            // Ensure TestStand engine is released
            var ts = sp.GetService<ITestStandService>();
            ts?.Dispose();
        }
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
