using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TestStandMCP.Models;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// Pure (engine-free) unit tests for <see cref="AnalyzerGrouping"/> — the helper that
/// reproduces the Sequence Editor Analysis-Results pane's "Group By" behaviour for the
/// run_sequence_analyzer / analyze_sequence_file tools.
/// Does NOT inherit TestBase, so no TestStand engine is connected.
/// </summary>
[TestFixture]
[Category("PureLogic")]
public class T28_AnalyzerGroupingTests
{
    private static List<AnalyzerMessage> Sample() => new()
    {
        new() { Severity = "Error",       RuleId = "RuleA", Text = "e1" },
        new() { Severity = "Error",       RuleId = "RuleB", Text = "e2" },
        new() { Severity = "Warning",     RuleId = "RuleA", Text = "w1" },
        new() { Severity = "Information", RuleId = "RuleA", Text = "i1" },
        new() { Severity = "Information", RuleId = "RuleC", Text = "i2" },
        new() { Severity = "Information", RuleId = "",      Text = "i3" }, // no rule
    };

    // ── IsGrouped ────────────────────────────────────────────────────────────────

    [TestCase("severity", true)]
    [TestCase("rule", true)]
    [TestCase("Severity", true)]   // case-insensitive
    [TestCase("  rule  ", true)]   // trimmed
    [TestCase("none", false)]
    [TestCase("None", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void IsGrouped_ClassifiesValue(string? groupBy, bool expected)
        => Assert.That(AnalyzerGrouping.IsGrouped(groupBy), Is.EqualTo(expected));

    // ── Group by severity ──────────────────────────────────────────────────────

    [Test]
    public void GroupBySeverity_OrdersErrorWarningInformation()
    {
        var groups = AnalyzerGrouping.Group(Sample(), "severity");

        Assert.That(groups.Select(g => g.Key),
            Is.EqualTo(new[] { "Error", "Warning", "Information" }),
            "severity groups must be in Error → Warning → Information order");
        Assert.That(groups.Select(g => g.Count), Is.EqualTo(new[] { 2, 1, 3 }));
    }

    [Test]
    public void GroupBySeverity_PartitionsEveryMessageExactlyOnce()
    {
        var src    = Sample();
        var groups = AnalyzerGrouping.Group(src, "severity");

        Assert.That(groups.Sum(g => g.Count), Is.EqualTo(src.Count),
            "group counts must sum to the input size");
        Assert.That(groups.Sum(g => g.Messages.Count), Is.EqualTo(src.Count));
        foreach (var g in groups)
        {
            Assert.That(g.Count, Is.EqualTo(g.Messages.Count), $"group '{g.Key}' Count must match its list");
            Assert.That(g.Messages.All(m => m.Severity == g.Key), Is.True,
                $"every message in group '{g.Key}' must carry that severity");
        }
    }

    [Test]
    public void Group_UnknownKey_FallsBackToSeverity()
    {
        // Any non-empty value that is not "rule" groups by severity.
        var bySomething = AnalyzerGrouping.Group(Sample(), "whatever");
        var bySeverity  = AnalyzerGrouping.Group(Sample(), "severity");
        Assert.That(bySomething.Select(g => g.Key), Is.EqualTo(bySeverity.Select(g => g.Key)));
    }

    // ── Group by rule ────────────────────────────────────────────────────────────

    [Test]
    public void GroupByRule_MostFrequentFirst_AndPartitions()
    {
        var src    = Sample();
        var groups = AnalyzerGrouping.Group(src, "rule");

        Assert.That(groups.First().Key, Is.EqualTo("RuleA"), "RuleA (3×) must lead");
        Assert.That(groups.First().Count, Is.EqualTo(3));
        Assert.That(groups.Select(g => g.Count), Is.Ordered.Descending,
            "rule groups must be ordered by descending count");
        Assert.That(groups.Sum(g => g.Count), Is.EqualTo(src.Count));
        foreach (var g in groups)
            Assert.That(g.Messages.All(m => (string.IsNullOrEmpty(m.RuleId) ? "(no rule)" : m.RuleId) == g.Key),
                Is.True, $"every message in group '{g.Key}' must carry that rule id");
    }

    [Test]
    public void GroupByRule_EmptyRuleId_BecomesNoRuleBucket()
    {
        var groups = AnalyzerGrouping.Group(Sample(), "rule");
        Assert.That(groups.Any(g => g.Key == "(no rule)"), Is.True);
    }

    // ── Edge cases ───────────────────────────────────────────────────────────────

    [Test]
    public void Group_EmptyInput_ReturnsNoGroups()
    {
        Assert.That(AnalyzerGrouping.Group(new List<AnalyzerMessage>(), "severity"), Is.Empty);
        Assert.That(AnalyzerGrouping.Group(new List<AnalyzerMessage>(), "rule"), Is.Empty);
    }
}
