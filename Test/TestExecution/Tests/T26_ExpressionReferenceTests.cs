using System;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using TestStandMCP.Models;
using TestStandMCP.Services;
using TestStandMCP.Tools;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// Pure (engine-free) unit tests for the expression-language reference catalogue behind the
/// <c>list_expression_reference</c> tool:
///   • ExpressionReference.Query   — kind/category/search filtering + kind normalisation
///   • the catalogue's content invariants (every entry well-formed; known funcs present;
///     known NON-existent funcs absent — the anti-fabrication guard)
///   • the gotcha notes are actually baked in (Round mode, no Mod/Floor)
///   • the full MCP dispatch path (registry → handler → JSON) without a connected engine,
///     which is possible because the handler is a pure static lookup.
/// Does NOT inherit TestBase, so no TestStand engine is connected.
/// </summary>
[TestFixture]
[Category("PureLogic")]
public class T26_ExpressionReferenceTests
{
    // ── Catalogue shape ──────────────────────────────────────────────────────────

    [Test]
    public void Kinds_AreTheThreeExpressionBrowserGroups()
    {
        Assert.That(ExpressionReference.Kinds, Is.EquivalentTo(new[] { "operator", "constant", "function" }));
    }

    [Test]
    public void All_ContainsEntriesOfEveryKind()
    {
        var kinds = ExpressionReference.All.Select(e => e.Kind).Distinct().ToList();
        Assert.That(kinds, Does.Contain("operator"));
        Assert.That(kinds, Does.Contain("constant"));
        Assert.That(kinds, Does.Contain("function"));
        Assert.That(ExpressionReference.All.Count, Is.GreaterThan(40),
            "the catalogue should be reasonably complete");
    }

    [Test]
    public void EveryEntry_HasTheRequiredFields()
    {
        foreach (var e in ExpressionReference.All)
        {
            Assert.That(e.Name,        Is.Not.Empty, "Name");
            Assert.That(e.Kind,        Is.Not.Empty, $"Kind of '{e.Name}'");
            Assert.That(e.Category,    Is.Not.Empty, $"Category of '{e.Name}'");
            Assert.That(e.Signature,   Is.Not.Empty, $"Signature of '{e.Name}'");
            Assert.That(e.Description, Is.Not.Empty, $"Description of '{e.Name}'");
            Assert.That(ExpressionReference.Kinds, Does.Contain(e.Kind), $"Kind of '{e.Name}' is a known group");
        }
    }

    [Test]
    public void EntryNames_AreUniqueWithinAKind()
    {
        foreach (var kind in ExpressionReference.Kinds)
        {
            var names = ExpressionReference.Query(kind).Select(e => e.Name).ToList();
            Assert.That(names, Is.Unique, $"duplicate name within kind '{kind}'");
        }
    }

    // ── Filtering ──────────────────────────────────────────────────────────────

    [Test]
    public void Query_NoFilters_ReturnsWholeCatalogue()
    {
        Assert.That(ExpressionReference.Query().Count, Is.EqualTo(ExpressionReference.All.Count));
    }

    [Test]
    public void Query_KindFilter_ReturnsOnlyThatKind()
    {
        var ops = ExpressionReference.Query(kind: "operator");
        Assert.That(ops, Is.Not.Empty);
        Assert.That(ops.All(e => e.Kind == "operator"), Is.True);
    }

    [Test]
    public void Query_KindFilter_AcceptsPluralAndIsCaseInsensitive()
    {
        var singular = ExpressionReference.Query(kind: "function").Count;
        Assert.That(ExpressionReference.Query(kind: "functions").Count, Is.EqualTo(singular), "plural");
        Assert.That(ExpressionReference.Query(kind: "Functions").Count, Is.EqualTo(singular), "mixed case");
        Assert.That(ExpressionReference.Query(kind: "FUNCTION").Count,  Is.EqualTo(singular), "upper case");
    }

    [Test]
    public void Query_CategoryFilter_IsCaseInsensitiveAndScoped()
    {
        var arr = ExpressionReference.Query(category: "array");
        Assert.That(arr, Is.Not.Empty);
        Assert.That(arr.All(e => e.Category.Equals("Array", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(arr.Any(e => e.Name == "GetNumElements"), Is.True);
    }

    [Test]
    public void Query_Search_MatchesNameDescriptionAndNote_CaseInsensitively()
    {
        var round = ExpressionReference.Query(search: "ROUND");
        Assert.That(round.Any(e => e.Name == "Round"), Is.True, "search hits the name case-insensitively");

        var shift = ExpressionReference.Query(search: "shift");
        Assert.That(shift.Select(e => e.Name), Does.Contain("<<").And.Contain(">>"),
            "search hits the description text");
    }

    [Test]
    public void Query_CombinedFilters_AndTogether()
    {
        var r = ExpressionReference.Query(kind: "function", category: "String", search: "case");
        Assert.That(r, Is.Not.Empty);
        Assert.That(r.All(e => e.Kind == "function" && e.Category == "String"), Is.True);
        Assert.That(r.Select(e => e.Name), Does.Contain("ToUpper").Or.Contain("ToLower"));
    }

    [Test]
    public void Query_UnknownCategory_ReturnsEmpty()
    {
        Assert.That(ExpressionReference.Query(category: "NoSuchCategory"), Is.Empty);
    }

    [Test]
    public void Categories_ScopedToKind_AreDistinctAndRelevant()
    {
        var fnCats = ExpressionReference.Categories("function");
        Assert.That(fnCats, Does.Contain("Numeric").And.Contain("String").And.Contain("Array"));
        Assert.That(fnCats, Is.Unique);
        Assert.That(fnCats, Does.Not.Contain("Arithmetic"), "Arithmetic is an operator category, not a function one");
    }

    // ── Content correctness (verified facts in, fabrications out) ────────────────

    [Test]
    public void KnownFunctions_ArePresentAndMarkedVerified()
    {
        foreach (var name in new[] { "Abs", "Round", "Sqrt", "Pow", "Str", "Len", "Mid",
                                     "GetNumElements", "StrComp", "PropertyExists" })
        {
            var e = ExpressionReference.All.SingleOrDefault(x => x.Kind == "function" && x.Name == name);
            Assert.That(e, Is.Not.Null, $"function '{name}' should be catalogued");
            Assert.That(e!.Verified, Is.True, $"function '{name}' is live-verified");
        }
    }

    [Test]
    public void KnownNonExistentFunctions_AreNotCatalogued()
    {
        // These are common false guesses proven NOT to exist — cataloguing them would defeat the
        // whole purpose of the reference (it must not send the user down a dead end).
        foreach (var name in new[] { "Floor", "Ceil", "Ceiling", "Trunc", "Truncate",
                                     "Mod", "Rnd", "Format", "Now", "Ord", "FileExists" })
        {
            Assert.That(ExpressionReference.All.Any(e => e.Name == name), Is.False,
                $"'{name}' does not exist in TestStand and must not be catalogued");
        }
    }

    [Test]
    public void GotchaNotes_AreBakedIn()
    {
        var round = ExpressionReference.All.Single(e => e.Name == "Round");
        Assert.That(round.Note, Does.Contain("MODE").IgnoreCase,
            "Round's note must warn that the 2nd arg is a rounding mode, not decimals");

        var modulo = ExpressionReference.All.Single(e => e.Name == "%");
        Assert.That(modulo.Note, Does.Contain("Mod").And.Contain("Floor"),
            "the % note must point out there is no Mod()/Floor()");

        var pow = ExpressionReference.All.Single(e => e.Name == "Pow");
        Assert.That(pow.Note, Does.Contain("XOR").Or.Contain("^"),
            "Pow's note should disambiguate it from '^' (XOR)");
    }

    [Test]
    public void ArrayFunctions_DocumentTheBareNameFileGlobalsRule()
    {
        var getNum = ExpressionReference.All.Single(e => e.Name == "GetNumElements");
        Assert.That(getNum.Note, Does.Contain("BARE").IgnoreCase,
            "the FileGlobals bare-name trap must be documented where it bites");
    }

    // ── Full MCP dispatch path (engine-free) ─────────────────────────────────────

    [Test]
    public void Dispatch_ToolIsRegistered_AndReturnsCatalogueJson()
    {
        // Both service ctors are side-effect-free (store the logger only) and the handler never
        // touches the service, so the whole dispatch path runs without a connected engine.
        var registry = new TestStandToolRegistry(
            new TestStandService(NullLogger<TestStandService>.Instance),
            new SequenceEditorService(NullLogger<SequenceEditorService>.Instance),
            NullLogger<TestStandToolRegistry>.Instance);

        Assert.That(registry.GetTools().Select(t => t.Name), Does.Contain("list_expression_reference"));

        var result = registry.CallToolAsync("list_expression_reference",
            JsonDocument.Parse("{\"kind\":\"function\",\"search\":\"array\"}").RootElement).Result;

        Assert.That(result.IsError, Is.Not.EqualTo(true), "dispatch should succeed");
        var root = JsonDocument.Parse(result.Content[0].Text).RootElement;

        var count = root.GetProperty("count").GetInt32();
        Assert.That(count, Is.GreaterThan(0));
        Assert.That(root.GetProperty("entries").GetArrayLength(), Is.EqualTo(count));
        Assert.That(root.GetProperty("entries")[0].GetProperty("name").GetString(), Is.Not.Empty);
        Assert.That(root.GetProperty("entries")[0].GetProperty("kind").GetString(), Is.EqualTo("function"));
        // camelCase serialisation (matches OkJson's policy)
        Assert.That(root.GetProperty("entries")[0].TryGetProperty("signature", out _), Is.True);
    }
}
