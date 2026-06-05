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

        var posted = await Ts.PostOutputMessageAsync(unique, "MCPTest", "Warning");
        Assert.That(posted, Is.Not.Null);
        Assert.That(posted.Message, Does.Contain(unique));

        var messages = await Ts.GetOutputMessagesAsync(500);
        Assert.That(messages.Any(m => m.Message.Contains(unique)), Is.True,
            "Posted output message should appear in the engine list");
        TestContext.WriteLine($"Output messages: {messages.Count}");
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
