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

        // The headless Engine API does not record edits into an undo stack automatically
        // (automatic undo recording is a Sequence Editor feature). We therefore verify
        // that the undo-stack query and the undo/redo operations are callable without
        // throwing — Undo/Redo simply report "nothing to undo" on an empty stack.
        var stack = await Ts.GetUndoStackAsync(TempSeqFile);
        Assert.That(stack, Is.Not.Null);
        Assert.That(stack.CanUndo, Is.False,
            "A freshly created engine undo stack records nothing automatically");

        Assert.DoesNotThrowAsync(async () =>
        {
            await Ts.UndoAsync(TempSeqFile);
            await Ts.RedoAsync(TempSeqFile);
        });
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
