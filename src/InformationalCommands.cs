using System.Linq;

namespace TestStandMCP;

/// <summary>What the command line asks the process to answer and exit, before any engine or MCP server starts.</summary>
internal enum InformationalCommand
{
    /// <summary>No informational flag: carry on and serve MCP.</summary>
    None,
    /// <summary><c>--version</c>.</summary>
    Version,
    /// <summary><c>--list-tools</c>, as the human-readable list.</summary>
    ListTools,
    /// <summary><c>--list-tools --json</c>, as a JSON array of names.</summary>
    ListToolsJson,
}

/// <summary>
/// The parsing of the informational flags, pulled out of <c>Program.Main</c> so that it can be tested without
/// starting a process. A flag is an exact, case-sensitive match of one argument.
/// </summary>
internal static class InformationalCommands
{
    public const string VersionFlag   = "--version";
    public const string ListToolsFlag = "--list-tools";
    public const string JsonFlag      = "--json";

    /// <summary>
    /// <c>--version</c> wins over <c>--list-tools</c> when both are given. <c>--json</c> only shapes the
    /// tool list: on its own it asks for nothing, so the process carries on and serves MCP.
    /// </summary>
    public static InformationalCommand Parse(string[] args)
    {
        if (args.Contains(VersionFlag))
            return InformationalCommand.Version;
        if (args.Contains(ListToolsFlag))
            return args.Contains(JsonFlag) ? InformationalCommand.ListToolsJson : InformationalCommand.ListTools;
        return InformationalCommand.None;
    }
}
