using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

[TestFixture]
[Category("FindReplace")]
public class T12_FindReplaceTests : TestBase
{
    private const string Seq = "FindSeq";
    private const string Grp = "Main";

    [Test]
    public async Task FindInFile_FindsStepByName()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, Seq);
        await Ts.InsertStepAsync(TempSeqFile, Seq, Grp, "Statement", "UniqueFindMarker");
        await Ts.SaveSequenceFileAsync(TempSeqFile);

        var result = await Ts.FindInFileAsync(TempSeqFile, "UniqueFindMarker",
            elements: "name");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.TotalMatches, Is.GreaterThan(0),
            "Native search should find the step by its unique name");
        TestContext.WriteLine(
            $"Matches: {result.TotalMatches}; first path: {result.Matches[0].PropertyPath}");
    }

    [Test]
    public async Task FindInFile_NoMatch_ReturnsZero()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, Seq);
        await Ts.SaveSequenceFileAsync(TempSeqFile);

        var result = await Ts.FindInFileAsync(TempSeqFile, "ZZZ_NoSuchText_ZZZ");
        Assert.That(result.TotalMatches, Is.EqualTo(0));
    }

    [Test]
    public async Task ReplaceInFile_ReplacesStringValue()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, Seq);
        // A local string variable gives a deterministic, editable string value.
        await Ts.InsertLocalVariableAsync(TempSeqFile, Seq, "Marker", "String",
            "ReplaceMePlaceholder");
        await Ts.SaveSequenceFileAsync(TempSeqFile);

        var result = await Ts.ReplaceInFileAsync(TempSeqFile, "ReplaceMePlaceholder",
            "AlreadyReplaced", elements: "values");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.TotalMatches, Is.GreaterThan(0),
            "The placeholder value should be located before replacing");
        Assert.That(result.ReplacedCount, Is.GreaterThan(0),
            "The string value should be replaced");

        // The original text must be gone, the new text present.
        var oldAfter = await Ts.FindInFileAsync(TempSeqFile, "ReplaceMePlaceholder",
            elements: "values");
        Assert.That(oldAfter.TotalMatches, Is.EqualTo(0),
            "Original text should no longer be found after replace");

        var newAfter = await Ts.FindInFileAsync(TempSeqFile, "AlreadyReplaced",
            elements: "values");
        Assert.That(newAfter.TotalMatches, Is.GreaterThan(0),
            "Replacement text should be present after replace");
        TestContext.WriteLine($"Replacements made: {result.ReplacedCount}");
    }
}
