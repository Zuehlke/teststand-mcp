using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using TestStandMCP.Models;

namespace TestStandMCP.IntegrationTests.Tests;

[TestFixture]
[Category("Enums")]
public class T22_EnumTests : TestBase
{
    private const string EnumName = "Color";

    [Test]
    public async Task CreateModifyDelete_Enum_RoundTrips()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);

        // ── Create with two constants ──────────────────────────────────────────
        await Ts.CreateEnumAsync(TempSeqFile, EnumName, new[]
        {
            new EnumValueInfo { Name = "Red",   Value = 0 },
            new EnumValueInfo { Name = "Green", Value = 1 },
        });

        var created = await Ts.GetEnumValuesAsync(TempSeqFile, EnumName);
        Assert.That(created.Values.Count, Is.EqualTo(2), "two constants after create");
        Assert.That(created.Values.Any(v => v.Name == "Red"   && v.Value == 0), Is.True);
        Assert.That(created.Values.Any(v => v.Name == "Green" && v.Value == 1), Is.True);
        TestContext.WriteLine(
            $"Created: {string.Join(", ", created.Values.Select(v => $"{v.Name}={v.Value}"))}");

        // ── Add a constant (auto-value = current max + 1) ──────────────────────
        var afterAdd = await Ts.AddEnumValueAsync(TempSeqFile, EnumName, "Blue");
        Assert.That(afterAdd.Values.Any(v => v.Name == "Blue" && v.Value == 2), Is.True,
            "Blue should auto-assign to 2");

        // ── Rename a constant (value preserved when not given) ─────────────────
        var afterRename = await Ts.RenameEnumValueAsync(TempSeqFile, EnumName, "Green", "Lime");
        Assert.That(afterRename.Values.Any(v => v.Name == "Green"), Is.False, "old name gone");
        Assert.That(afterRename.Values.Any(v => v.Name == "Lime" && v.Value == 1), Is.True,
            "renamed, original value preserved");

        // ── Remove a constant ──────────────────────────────────────────────────
        var afterRemove = await Ts.RemoveEnumValueAsync(TempSeqFile, EnumName, "Red");
        Assert.That(afterRemove.Values.Any(v => v.Name == "Red"), Is.False, "removed constant gone");
        Assert.That(afterRemove.Values.Count, Is.EqualTo(2), "Lime + Blue remain");

        // ── Replace the entire list (bulk) ─────────────────────────────────────
        var afterSet = await Ts.SetEnumValuesAsync(TempSeqFile, EnumName, new[]
        {
            new EnumValueInfo { Name = "On",  Value = 10 },
            new EnumValueInfo { Name = "Off", Value = 20 },
        });
        Assert.That(afterSet.Values.Count, Is.EqualTo(2));
        Assert.That(afterSet.Values.Any(v => v.Name == "On"  && v.Value == 10), Is.True);
        Assert.That(afterSet.Values.Any(v => v.Name == "Off" && v.Value == 20), Is.True);
        Assert.That(afterSet.Values.Any(v => v.Name == "Lime"), Is.False, "old constants replaced");

        // ── Persistence: close + reopen, values survive the save→reload round-trip ─
        await Ts.CloseSequenceFileAsync(TempSeqFile);
        await Ts.OpenSequenceFileAsync(TempSeqFile);
        var reloaded = await Ts.GetEnumValuesAsync(TempSeqFile, EnumName);
        Assert.That(reloaded.Values.Count, Is.EqualTo(2), "values survive save + reload");
        Assert.That(reloaded.Values.Any(v => v.Name == "On" && v.Value == 10), Is.True);

        // ── Delete; the enum type is then no longer resolvable ─────────────────
        await Ts.DeleteEnumAsync(TempSeqFile, EnumName);
        Assert.That(async () => await Ts.GetEnumValuesAsync(TempSeqFile, EnumName),
            Throws.InstanceOf<System.InvalidOperationException>(),
            "enum no longer found after delete");
    }

    [Test]
    public async Task EnumTypedLocalVariable_IsCreated()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.CreateEnumAsync(TempSeqFile, EnumName, new[]
        {
            new EnumValueInfo { Name = "Red",   Value = 0 },
            new EnumValueInfo { Name = "Green", Value = 1 },
        });

        // insert_local_variable with a non-builtin type name creates a named-type (enum) local.
        await Ts.InsertLocalVariableAsync(TempSeqFile, "MainSequence", "Selected", EnumName);

        var locals = await Ts.GetLocalVariablesAsync(TempSeqFile, "MainSequence");
        var sel = locals.FirstOrDefault(l => l.Name == "Selected");
        Assert.That(sel, Is.Not.Null, "enum-typed local should be created");
        Assert.That(sel!.DataType, Does.Contain(EnumName),
            "local's data type should reference the enum type");
        TestContext.WriteLine($"Local 'Selected' dataType = {sel.DataType}");
    }
}
