namespace TestStandMCP.Services;

/// <summary>
/// Chooses the TestStand environment path from the plugin's own setting and the machine-wide
/// configuration. The plugin's <c>.mcp.json</c> always passes its setting (an optional
/// <c>userConfig</c> entry), so an unset entry must NOT mask a machine-wide
/// <c>TESTSTAND_MCP_TestStand__EnvironmentPath</c> or <c>appsettings.json</c> value — it travels under
/// its own key, <c>TestStand:PluginEnvironmentPath</c>, and only wins when it holds a real path.
/// </summary>
public static class EnvironmentPathSetting
{
    // Claude Code's behaviour for an unset optional userConfig value is unverified: it may substitute
    // an empty string or leave the "${user_config.<key>}" text in place. Both mean "not set".
    private const string UnexpandedPlaceholderPrefix = "${";

    /// <summary>The plugin value when it is a real path, otherwise the configured value (may be null/blank).</summary>
    public static string? Resolve(string? pluginValue, string? configuredValue)
    {
        return IsUsablePath(pluginValue) ? pluginValue : configuredValue;
    }

    private static bool IsUsablePath(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !value!.TrimStart().StartsWith(UnexpandedPlaceholderPrefix, System.StringComparison.Ordinal);
    }
}
