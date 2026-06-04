using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using TestStandMCP.Services;

namespace TestStandMCP.IntegrationTests.Tests;

[TestFixture]
[Category("Connection")]
public class T01_ConnectionTests : TestBase
{
    [Test]
    public void IsConnected_AfterConnect_ReturnsTrue()
    {
        Assert.That(Ts.IsConnected, Is.True);
    }

    [Test]
    public async Task GetStationInfo_ReturnsValidData()
    {
        var info = await Ts.GetStationInfoAsync();

        Assert.That(info, Is.Not.Null);
        Assert.That(info.TestStandVersion, Is.Not.Empty, "TestStand version should not be empty");
        Assert.That(info.StationName,      Is.Not.Null,  "Station name should be populated");
        TestContext.WriteLine($"Station: {info.StationName}, TS version: {info.TestStandVersion}");
    }

    [Test]
    public async Task GetEnginePaths_ReturnsNonEmptyPaths()
    {
        var paths = await Ts.GetEnginePathsAsync();

        Assert.That(paths, Is.Not.Null);
        Assert.That(paths.TestStandDirectory, Is.Not.Empty);
        TestContext.WriteLine($"TestStand directory: {paths.TestStandDirectory}");
    }

    [Test]
    public async Task GetStationOptions_DoesNotThrow()
    {
        var opts = await Ts.GetStationOptionsAsync();
        Assert.That(opts, Is.Not.Null);
    }

    [Test]
    public async Task GetStepTypes_ReturnsFlowControlTypes()
    {
        var types = await Ts.GetStepTypesAsync();

        Assert.That(types, Is.Not.Empty, "At least one step type must be registered");

        var typeNames = types.ConvertAll(t => t.Name);
        Assert.That(typeNames, Does.Contain("NI_Flow_If"),    "NI_Flow_If must exist");
        Assert.That(typeNames, Does.Contain("NI_Flow_While"), "NI_Flow_While must exist");
        Assert.That(typeNames, Does.Contain("Statement"),     "Statement must exist");
        Assert.That(typeNames, Does.Contain("NumericLimitTest"), "NumericLimitTest must exist");
        TestContext.WriteLine($"Total step types: {types.Count}");
    }

    [Test]
    public async Task CheckExpression_ValidExpression_ReturnsNoError()
    {
        // CheckExprSyntax requires a loaded sequence file as context.
        // Create a minimal temp file, open it, then check the expression.
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        var result = await Ts.CheckExpressionAsync("1 + 1", TempSeqFile);
        Assert.That(result.IsValid, Is.True,
            $"Valid expression reported error: {result.ErrorMessage}");
    }

    // [Explicit] — excluded from normal runs. This test spins up a SECOND in-process
    // TestStand engine. TestStand cannot cleanly tear down a secondary engine while
    // another one is still alive: it leaves NI-licensing / engine threads behind that
    // prevent the test host process from exiting, so the *whole* assembly run hangs at
    // the end (every test passes first). The real MCP server only ever creates one
    // engine, so this scenario never occurs in production. Run it manually/in isolation
    // when you specifically want to validate multi-instance behavior.
    [Test, Explicit(
        "Creates a second in-process TestStand engine; TestStand can't cleanly tear it " +
        "down while the shared engine lives, which hangs the test host on exit. " +
        "Run in isolation only — the MCP server uses a single engine.")]
    public void SecondServiceInstance_CanConnectAndDisconnect()
    {
        // Verifies that creating a second service instance works without
        // disturbing the shared instance used by all other tests.
        using var svc = new TestStandService(NullLogger<TestStandService>.Instance);
        var connected = svc.ConnectAsync().GetAwaiter().GetResult();
        Assert.That(connected, Is.True);
        svc.DisconnectAsync().GetAwaiter().GetResult();
        Assert.That(svc.IsConnected, Is.False);
    }
}
