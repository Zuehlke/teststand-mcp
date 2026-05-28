using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

[TestFixture]
[Category("UndoRedoSearch")]
public class T08_UndoRedoAndSearchTests : TestBase
{
    private const string Seq = "UndoTests";
    private const string Grp = "Main";

    // ── Undo / Redo ────────────────────────────────────────────────────────────

    [Test]
    public async Task UndoRedoCycle_DoesNotThrow()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, Seq);
        await Ts.InsertStepAsync(TempSeqFile, Seq, Grp, "Statement", "UndoMe");

        var stack = await Ts.GetUndoStackAsync(TempSeqFile);
        Assert.That(stack, Is.Not.Null);

        // Undo the insert
        var undone = await Ts.UndoAsync(TempSeqFile);
        Assert.That(undone, Is.True, "Undo should succeed after a step insert");

        // Redo it
        var redone = await Ts.RedoAsync(TempSeqFile);
        Assert.That(redone, Is.True, "Redo should succeed after undo");
    }

    [Test]
    public async Task BeginEndUndoGroup_DoesNotThrow()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);

        Assert.DoesNotThrowAsync(async () =>
        {
            await Ts.BeginUndoGroupAsync("TestGroup", TempSeqFile);
            await Ts.InsertSequenceAsync(TempSeqFile, "GroupSeq");
            await Ts.EndUndoGroupAsync(TempSeqFile);
        });
    }

    // ── Search ────────────────────────────────────────────────────────────────

    [Test]
    public async Task SearchSteps_FindsMatchingStepByName()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "SearchSeq");
        await Ts.InsertStepAsync(TempSeqFile, "SearchSeq", Grp, "Statement", "FindMe_SpecialName");

        var result = await Ts.SearchStepsAsync(TempSeqFile, "FindMe_SpecialName");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.TotalMatches, Is.GreaterThan(0),
            "Should find the step by its unique name");
    }

    // ── Expression check & path macros ────────────────────────────────────────

    [Test]
    public async Task CheckExpression_InvalidExpression_ReturnsError()
    {
        // CheckExprSyntax requires a loaded sequence file as context.
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        var result = await Ts.CheckExpressionAsync("this is not valid !!!###", TempSeqFile);
        Assert.That(result.IsValid, Is.False,
            "An invalid expression should be reported as an error");
    }

    [Test]
    public async Task ExpandPathMacros_KnownMacro_ReturnsExpandedPath()
    {
        var expanded = await Ts.ExpandPathMacrosAsync("<TestStand>");
        Assert.That(expanded, Is.Not.Empty);
        Assert.That(expanded, Does.Not.Contain("<TestStand>"),
            "Macro should have been expanded to an actual path");
        TestContext.WriteLine($"<TestStand> → {expanded}");
    }

    // ── Sequence analyzer ─────────────────────────────────────────────────────

    [Test]
    public async Task RunSequenceAnalyzer_ReturnsResultList()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, "AnalyzerTest");

        var messages = await Ts.RunSequenceAnalyzerAsync(TempSeqFile);

        Assert.That(messages, Is.Not.Null);
        TestContext.WriteLine($"Analyzer messages: {messages.Count}");
    }
}
