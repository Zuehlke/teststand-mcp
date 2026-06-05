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
}
