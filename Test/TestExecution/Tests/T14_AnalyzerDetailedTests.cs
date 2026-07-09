using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

[TestFixture]
[Category("AnalyzerDetailed")]
public class T14_AnalyzerDetailedTests : TestBase
{
    [Test]
    public async Task AnalyzeDetailed_ReturnsConsistentCounts()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "AnalyzeSeq");
        await Ts.SaveSequenceFileAsync(TempSeqFile);

        var result = await Ts.RunSequenceAnalyzerDetailedAsync(TempSeqFile, "Information");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.FilePath, Is.EqualTo(TempSeqFile));
        Assert.That(result.Messages.Count, Is.EqualTo(result.TotalMessages),
            "TotalMessages must match the returned message list size");
        Assert.That(result.ErrorCount + result.WarningCount + result.InformationCount,
            Is.EqualTo(result.TotalMessages),
            "Severity counts must sum to the total");
        TestContext.WriteLine(
            $"Analyzer: {result.ErrorCount} errors, {result.WarningCount} warnings, " +
            $"{result.InformationCount} info");
    }

    [Test]
    public async Task AnalyzeDetailed_ErrorFilter_ExcludesLowerSeverities()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "AnalyzeSeq2");
        await Ts.SaveSequenceFileAsync(TempSeqFile);

        var errorsOnly = await Ts.RunSequenceAnalyzerDetailedAsync(TempSeqFile, "Error");

        Assert.That(errorsOnly, Is.Not.Null);
        Assert.That(errorsOnly.WarningCount, Is.EqualTo(0),
            "Error filter must exclude warnings");
        Assert.That(errorsOnly.InformationCount, Is.EqualTo(0),
            "Error filter must exclude information messages");
    }

    [Test]
    public async Task AnalyzeDetailed_GroupBySeverity_GroupsPartitionFlatList()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "GroupSeqSev");
        await Ts.SaveSequenceFileAsync(TempSeqFile);

        var result = await Ts.RunSequenceAnalyzerDetailedAsync(TempSeqFile, "Information", "severity");

        Assert.That(result.GroupBy, Is.EqualTo("severity"));
        // Groups partition the flat message list exactly — no gaps, no double-counting.
        Assert.That(result.Groups.Sum(g => g.Count), Is.EqualTo(result.TotalMessages),
            "group counts must sum to the total");
        Assert.That(result.Groups.Sum(g => g.Messages.Count), Is.EqualTo(result.Messages.Count));
        foreach (var g in result.Groups)
            Assert.That(g.Messages.All(m => m.Severity == g.Key), Is.True,
                $"every message in group '{g.Key}' must carry that severity");
    }

    [Test]
    public async Task AnalyzeDetailed_GroupByNone_LeavesGroupsEmpty()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "GroupSeqNone");
        await Ts.SaveSequenceFileAsync(TempSeqFile);

        var result = await Ts.RunSequenceAnalyzerDetailedAsync(TempSeqFile, "Information", "none");

        Assert.That(result.GroupBy, Is.Empty, "group_by=none must not set a grouping label");
        Assert.That(result.Groups, Is.Empty, "group_by=none must not populate groups");
        Assert.That(result.Messages.Count, Is.EqualTo(result.TotalMessages),
            "the flat list is unaffected by the grouping option");
    }

    [Test]
    public async Task AnalyzeDetailed_Async_ReturnsJobThenSameResultAsSync()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "AsyncSeq");
        await Ts.SaveSequenceFileAsync(TempSeqFile);

        // Baseline: the synchronous result the async path must reproduce exactly.
        var sync = await Ts.RunSequenceAnalyzerDetailedAsync(TempSeqFile, "Information", "severity");

        // Async: returns IMMEDIATELY with a running handle (no messages yet) — this is the fix for
        // the ~60s transport timeout: the RPC never blocks on the analysis.
        var started = await Ts.RunSequenceAnalyzerDetailedAsync(TempSeqFile, "Information", "severity", async: true);
        Assert.That(started.JobId, Is.Not.Null.And.Not.Empty, "async start must return a job id");
        Assert.That(started.Status, Is.EqualTo("running"), "async start must report status=running");
        Assert.That(started.Messages, Is.Empty, "the running handle carries no messages yet");

        // Poll get_analysis_status until the job leaves 'running' (bounded — the analysis itself has
        // its own 120s AnalyzerApp watchdog; give it generous head-room here).
        var final = started;
        for (int i = 0; i < 120 && final.Status == "running"; i++)
        {
            await Task.Delay(1000);
            final = await Ts.GetAnalysisStatusAsync(started.JobId!);
        }

        Assert.That(final.Status, Is.EqualTo("completed"),
            $"job must complete (note: {final.Note})");
        Assert.That(final.JobId, Is.EqualTo(started.JobId));
        // Result parity with the synchronous run — the whole point of the acceptance criteria.
        Assert.That(final.TotalMessages, Is.EqualTo(sync.TotalMessages), "async total must match sync");
        Assert.That(final.ErrorCount, Is.EqualTo(sync.ErrorCount));
        Assert.That(final.WarningCount, Is.EqualTo(sync.WarningCount));
        Assert.That(final.InformationCount, Is.EqualTo(sync.InformationCount));
        Assert.That(final.Messages.Count, Is.EqualTo(final.TotalMessages),
            "counts stay self-consistent after the async round-trip");

        // An unknown/expired job id is a clean KeyNotFoundException, not a crash.
        Assert.ThrowsAsync<System.Collections.Generic.KeyNotFoundException>(
            async () => await Ts.GetAnalysisStatusAsync("does-not-exist"));
    }
}
