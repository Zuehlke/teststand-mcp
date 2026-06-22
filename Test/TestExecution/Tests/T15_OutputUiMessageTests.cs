using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

[TestFixture]
[Category("OutputUiMessages")]
public class T15_OutputUiMessageTests : TestBase
{
    [Test]
    public async Task PostOutputMessage_ThenAppearsInList()
    {
        var unique = $"MCP_OUT_{Guid.NewGuid():N}";

        // The engine's output-message list is global and accumulates across the whole test run.
        // Clear first so this test is order-independent.
        await Ts.ClearOutputMessagesAsync();

        var posted = await Ts.PostOutputMessageAsync(unique, "MCPTest", "Warning");
        Assert.That(posted, Is.Not.Null);
        Assert.That(posted.Message, Does.Contain(unique));

        var messages = await Ts.GetOutputMessagesAsync(500);
        Assert.That(messages.Any(m => m.Message.Contains(unique)), Is.True,
            "Posted output message should appear in the engine list");
        TestContext.WriteLine($"Output messages: {messages.Count}");
    }

    [Test]
    public async Task GetOutputMessages_WhenCapped_ReturnsMostRecentNotOldest()
    {
        // Regression: GetOutputMessagesAsync returned the OLDEST N (index 0..N), hiding all recent
        // activity once more than N messages had accumulated. It must return the most recent N.
        await Ts.ClearOutputMessagesAsync();
        var tag = $"MCP_SEQ_{Guid.NewGuid():N}";
        for (int i = 0; i < 8; i++)                       // tag-0 (oldest) .. tag-7 (newest)
            await Ts.PostOutputMessageAsync($"{tag}-{i}", "MCPTest", "Information");

        var recent = await Ts.GetOutputMessagesAsync(3);  // cap below the 8 we posted
        var texts  = recent.Select(m => m.Message).ToList();

        Assert.That(recent.Count, Is.EqualTo(3), "the cap must be honored");
        Assert.That(texts, Does.Contain($"{tag}-7"), "the newest message must be included");
        Assert.That(texts, Does.Not.Contain($"{tag}-0"), "the oldest message must be dropped when capped");
    }

    [Test]
    public async Task ClearOutputMessages_DoesNotThrow()
    {
        await Ts.PostOutputMessageAsync($"MCP_CLR_{Guid.NewGuid():N}", "MCPTest", "Information");
        Assert.DoesNotThrowAsync(() => Ts.ClearOutputMessagesAsync());
    }

    [Test]
    public void PostUiMessage_UnknownExecution_Throws()
    {
        // Posting requires a live execution; a bogus id must surface a clear error
        // (this verifies the path is wired without starting a real execution).
        Assert.ThrowsAsync<System.Collections.Generic.KeyNotFoundException>(() =>
            Ts.PostUiMessageAsync("does-not-exist", "UserMessageBase", 1, "hi"));
    }
}
