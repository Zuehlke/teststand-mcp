using NUnit.Framework;
using TestStandMCP.Models;

namespace TestStandMCP.UnitTests;

/// <summary>
/// What a client sees in the MCP <c>initialize</c> response's <c>serverInfo</c> is built by
/// <see cref="VersionInfo.ToServerInfo"/> — the same source <c>--version</c> prints. The full
/// <c>initialize</c> round trip, and whether the shipped exe is stamped at all, need the real exe
/// (this assembly links VersionInfo.cs and carries its own version); the packaging smoke test covers
/// both over stdio.
/// </summary>
[TestFixture]
[Category("EngineFree")]
public class ServerInfoVersionTests
{
    [Test]
    public void ToServerInfo_CarriesProductNameAndTheResolvedVersion()
    {
        var info = VersionInfo.Resolve("1.9.0+abc123", versionTxtPath: null);

        var serverInfo = info.ToServerInfo();

        Assert.That(serverInfo.Name, Is.EqualTo(VersionInfo.ProductName));
        Assert.That(serverInfo.Version, Is.EqualTo("1.9.0"));
    }
}
