using NUnit.Framework;
using TestStandMCP.Services;

// Deliberately NOT TestStandMCP.IntegrationTests: that namespace owns the [SetUpFixture] that connects
// the TestStand engine once for the whole run, and these tests must run on a machine without one.
namespace TestStandMCP.UnitTests;

/// <summary>
/// <see cref="EnvironmentPathSetting.Resolve"/> — engine-free. The plugin's own (optional) setting wins
/// over the machine-wide configuration only when the user actually filled it in.
/// </summary>
[TestFixture]
[Category("EngineFree")]
public class EnvironmentPathSettingTests
{
    private const string PluginValue = @"C:\Envs\Plugin.tsenv";
    private const string ConfiguredValue = @"C:\Envs\Machine.tsenv";
    private const string UnexpandedPlaceholder = "${user_config.tsenv_path}";

    [Test]
    public void Resolve_PluginValueSet_WinsOverConfigured()
    {
        Assert.That(EnvironmentPathSetting.Resolve(PluginValue, ConfiguredValue), Is.EqualTo(PluginValue));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Resolve_PluginValueBlank_FallsBackToConfigured(string? pluginValue)
    {
        Assert.That(EnvironmentPathSetting.Resolve(pluginValue, ConfiguredValue), Is.EqualTo(ConfiguredValue));
    }

    [Test]
    public void Resolve_PluginValueIsUnexpandedPlaceholder_FallsBackToConfigured()
    {
        Assert.That(EnvironmentPathSetting.Resolve(UnexpandedPlaceholder, ConfiguredValue), Is.EqualTo(ConfiguredValue));
    }

    [Test]
    public void Resolve_PlaceholderWithSurroundingWhitespace_FallsBackToConfigured()
    {
        Assert.That(EnvironmentPathSetting.Resolve("  " + UnexpandedPlaceholder, ConfiguredValue), Is.EqualTo(ConfiguredValue));
    }

    // Nothing usable from the plugin: the configured value comes back exactly as it was, blank or null included.
    [TestCase(null, null, null)]
    [TestCase("", "", "")]
    [TestCase("  ", null, null)]
    [TestCase(UnexpandedPlaceholder, null, null)]
    [TestCase(UnexpandedPlaceholder, "", "")]
    [TestCase(null, "   ", "   ")]
    [TestCase("  " + UnexpandedPlaceholder, "   ", "   ")]
    public void Resolve_NothingUsable_ReturnsTheConfiguredValueUnchanged(string? pluginValue, string? configuredValue, string? expected)
    {
        Assert.That(EnvironmentPathSetting.Resolve(pluginValue, configuredValue), Is.EqualTo(expected));
    }
}
