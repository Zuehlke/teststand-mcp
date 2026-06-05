using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

[TestFixture]
[Category("SyncAndSmoke")]
public class T19_SyncAndSmokeTests : TestBase
{
    [Test]
    public async Task CreateRendezvousSyncObject_SucceedsOrSyncMgrUnavailable()
    {
        var name = $"MCP_RV_{Guid.NewGuid():N}";
        try
        {
            await Ts.CreateSyncObjectAsync(name, "Rendezvous", 1, 2);
            TestContext.WriteLine("Rendezvous sync object created.");
        }
        catch (InvalidOperationException ex)
        {
            // Headless engines may not expose a SyncManager (no execution context).
            TestContext.WriteLine($"SyncManager unavailable (expected headless): {ex.Message}");
            Assert.Pass();
        }
        finally
        {
            try { await Ts.DeleteSyncObjectAsync(name); } catch { }
        }
    }

    [Test]
    public async Task CreateBatchSyncObject_SucceedsOrIsUnsupported()
    {
        var name = $"MCP_BATCH_{Guid.NewGuid():N}";
        try
        {
            await Ts.CreateBatchSyncObjectAsync(name);
            TestContext.WriteLine("Batch sync object created.");
        }
        catch (NotSupportedException ex)
        {
            // Documented outcome: batch sync is provided by the batch process model.
            TestContext.WriteLine($"Batch sync not standalone (expected): {ex.Message}");
            Assert.Pass();
        }
        catch (InvalidOperationException ex)
        {
            // Headless engines may not expose a SyncManager at all.
            TestContext.WriteLine($"SyncManager unavailable (expected headless): {ex.Message}");
            Assert.Pass();
        }
        finally
        {
            try { await Ts.DeleteSyncObjectAsync(name); } catch { }
        }
    }

    [Test]
    public void CreateResultLog_DoesNotThrow()
    {
        Assert.DoesNotThrowAsync(async () =>
        {
            var msg = await Ts.CreateResultLogAsync("", "ASCII");
            TestContext.WriteLine(msg);
        });
    }

    [Test]
    public async Task RunStepsInteractively_SetsUpArgs()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "ISeq");
        await Ts.InsertStepAsync(TempSeqFile, "ISeq", "Main", "Statement", "S1");

        var result = await Ts.RunStepsInteractivelyAsync(
            TempSeqFile, "ISeq", "Main", new List<string> { "S1" }, 30);
        Assert.That(result, Is.Not.Empty);
        TestContext.WriteLine(result);
    }

    [Test]
    public void AddReportSection_UnknownExecution_Throws()
    {
        // Smoke: needs a live execution; a bogus id must surface a clear error.
        Assert.ThrowsAsync<System.Collections.Generic.KeyNotFoundException>(() =>
            Ts.AddReportSectionAsync("does-not-exist", "Title", "Body"));
    }
}
