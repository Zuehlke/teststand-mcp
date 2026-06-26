using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TestStandMCP.Models;
using TestStandMCP.Tools;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// Pure (engine-free) unit tests for the post-build reference auditor (Option B).
/// Does NOT inherit TestBase, so no TestStand engine is connected.
/// </summary>
[TestFixture]
[Category("ReferenceAudit")]
public class T28_ReferenceAuditorTests
{
    private static ExpressionEntry E(string seq, string step, string prop, string expr) =>
        new() { SequenceName = seq, StepName = step, Property = prop, Expression = expr, StepGroup = "Main" };

    private static DeclaredScope Scope(string seq, string[] locals, string[] pars) =>
        new() { SequenceName = seq, Locals = locals.ToList(), Parameters = pars.ToList() };

    private static ReferenceAuditData Data(IEnumerable<ExpressionEntry> exprs,
        DeclaredScope scope, params string[] fileGlobals) =>
        new()
        {
            Expressions = exprs.ToList(),
            Scopes      = new List<DeclaredScope> { scope },
            FileGlobals = fileGlobals.ToList()
        };

    // The exact situation that slipped through validate_sequence_plan: an If condition
    // (set via set_flow_condition) referencing a parameter that was never declared.
    [Test]
    public void UndeclaredParameter_InConditionExpr_IsReported()
    {
        var r = ReferenceAuditor.Audit(Data(
            new[] { E("__CheckUfoType", "Produkt-ID Variante A?", "ConditionExpr", "Parameters.Product_ID == 100.0001") },
            Scope("__CheckUfoType", new string[0], new string[0])));

        Assert.That(r.Valid, Is.False);
        Assert.That(r.Issues, Has.Count.EqualTo(1));
        Assert.That(r.Issues[0].Code, Is.EqualTo("E_UNDECLARED_PARAM"));
        Assert.That(r.Issues[0].Name, Is.EqualTo("Product_ID"));
        Assert.That(r.Issues[0].Property, Is.EqualTo("ConditionExpr"));
    }

    [Test]
    public void DeclaredLocalAndParameter_AreClean()
    {
        var r = ReferenceAuditor.Audit(Data(
            new[]
            {
                E("Seq", "If A?", "ConditionExpr", "Parameters.Product_ID == 100.0001"),
                E("Seq", "Set",   "PostExpression", "Locals.DOPruefkreis = True, Locals.DOBoost = False"),
            },
            Scope("Seq", new[] { "DOPruefkreis", "DOBoost" }, new[] { "Product_ID" })));

        Assert.That(r.Valid, Is.True);
        Assert.That(r.Issues, Is.Empty);
        Assert.That(r.Stats.ReferencesFound, Is.EqualTo(3));
        Assert.That(r.Stats.ExpressionsScanned, Is.EqualTo(2));
    }

    [Test]
    public void UndeclaredLocal_IsReported()
    {
        var r = ReferenceAuditor.Audit(Data(
            new[] { E("Seq", "If", "ConditionExpr", "Locals.mitBatterie == True") },
            Scope("Seq", new string[0], new string[0])));

        Assert.That(r.Issues.Single().Code, Is.EqualTo("E_UNDECLARED_LOCAL"));
        Assert.That(r.Issues.Single().Name, Is.EqualTo("mitBatterie"));
    }

    [Test]
    public void FileGlobal_IsCheckedAgainstFileGlobals()
    {
        Assert.That(ReferenceAuditor.Audit(Data(
            new[] { E("Seq", "S", "PreExpression", "FileGlobals.Counter > 0") },
            Scope("Seq", new string[0], new string[0]), "Counter")).Valid, Is.True);

        var r = ReferenceAuditor.Audit(Data(
            new[] { E("Seq", "S", "PreExpression", "FileGlobals.Counter > 0") },
            Scope("Seq", new string[0], new string[0])));
        Assert.That(r.Issues.Single().Code, Is.EqualTo("E_UNDECLARED_FILEGLOBAL"));
    }

    [Test]
    public void OtherScopes_AreNotAudited()
    {
        // RunState.Caller.Locals.X targets the CALLER's locals, StationGlobals are station-level —
        // neither is resolvable here, so neither must be matched or flagged.
        var r = ReferenceAuditor.Audit(Data(
            new[]
            {
                E("Seq", "A", "PreExpression",  "RunState.Caller.Locals.Foo == 1"),
                E("Seq", "B", "PostExpression", "StationGlobals.Bar = 2"),
            },
            Scope("Seq", new string[0], new string[0])));

        Assert.That(r.Stats.ReferencesFound, Is.EqualTo(0));
        Assert.That(r.Valid, Is.True);
    }

    [Test]
    public void IdentifierEndingInScopeKeyword_IsNotMatched()
    {
        var r = ReferenceAuditor.Audit(Data(
            new[] { E("Seq", "S", "PostExpression", "MyLocals.X = 1") },
            Scope("Seq", new string[0], new string[0])));

        Assert.That(r.Stats.ReferencesFound, Is.EqualTo(0));
        Assert.That(r.Valid, Is.True);
    }

    [Test]
    public void SameUndeclaredRef_InTwoSteps_YieldsTwoFindings()
    {
        var r = ReferenceAuditor.Audit(Data(
            new[]
            {
                E("Seq", "If A?",     "ConditionExpr", "Parameters.Product_ID == 100.0001"),
                E("Seq", "ElseIf B?", "ConditionExpr", "Parameters.Product_ID == 100.0002"),
            },
            Scope("Seq", new string[0], new string[0])));

        Assert.That(r.Issues, Has.Count.EqualTo(2));
        Assert.That(r.Stats.ReferencesFound, Is.EqualTo(2));
    }

    [Test]
    public void SubPropertyReference_ChecksTopLevelName()
    {
        // Locals.Container.Member — only the top-level "Container" is the declared variable.
        var ok = ReferenceAuditor.Audit(Data(
            new[] { E("Seq", "S", "PostExpression", "Locals.Container.Member = 1") },
            Scope("Seq", new[] { "Container" }, new string[0])));
        Assert.That(ok.Valid, Is.True);

        var bad = ReferenceAuditor.Audit(Data(
            new[] { E("Seq", "S", "PostExpression", "Locals.Container.Member = 1") },
            Scope("Seq", new string[0], new string[0])));
        Assert.That(bad.Issues.Single().Name, Is.EqualTo("Container"));
    }

    [Test]
    public void EmptyExpressions_AreSkipped()
    {
        var r = ReferenceAuditor.Audit(Data(
            new[] { E("Seq", "S", "PostExpression", "   ") },
            Scope("Seq", new string[0], new string[0])));

        Assert.That(r.Stats.ExpressionsScanned, Is.EqualTo(0));
        Assert.That(r.Valid, Is.True);
    }
}
