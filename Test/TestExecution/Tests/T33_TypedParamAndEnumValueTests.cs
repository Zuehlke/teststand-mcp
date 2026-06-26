using System.Threading.Tasks;
using NUnit.Framework;
using TestStandMCP.Models;

namespace TestStandMCP.IntegrationTests.Tests;

// Regression coverage for two fixes:
//  1) InsertSequenceParameterAsync must honour named (enum) / reference / container types instead
//     of silently falling back to String (it previously created a numeric PropValType with an empty
//     typeName, so every non-primitive param became a String).
//  2) Writing an ENUM instance value (set_local_variable / set_property_value) must succeed and
//     PRESERVE the enum type — a plain SetValNumber/SetValString throws "Expected type X. Found
//     type Number/String"; the fix retries with PropOption_CoerceToEnum (coerce for-this-operation).
[TestFixture]
[Category("Sequence")]
public class T33_TypedParamAndEnumValueTests : TestBase
{
    private const string EnumName = "RespKind";

    private async Task PrepareFileAsync(string seqName)
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, seqName);
        await Ts.CreateEnumAsync(TempSeqFile, EnumName, new[]
        {
            new EnumValueInfo { Name = "E_SUCCESS", Value = 0 },
            new EnumValueInfo { Name = "E_ERROR",   Value = 246 },
        });
    }

    // ── 1) Typed parameters ──────────────────────────────────────────────────────

    [Test]
    public async Task InsertSequenceParameter_EnumType_StoresEnumNotString()
    {
        await PrepareFileAsync("Seq");
        await Ts.InsertSequenceParameterAsync(TempSeqFile, "Seq",
            "RespState", EnumName, "InOut", null, true);

        var p = (await Ts.GetSequenceParametersAsync(TempSeqFile, "Seq"))
            .Find(x => x.Name == "RespState");

        Assert.That(p, Is.Not.Null);
        Assert.That(p!.DataType, Does.Contain(EnumName),
            "enum param must keep its enum type, not fall back to String");
        Assert.That(p.DataType, Does.Not.Contain("String"));
    }

    [Test]
    public async Task InsertSequenceParameter_ReferenceAndContainer_StoreTyped()
    {
        await PrepareFileAsync("Seq");
        await Ts.InsertSequenceParameterAsync(TempSeqFile, "Seq",
            "objRef", "reference", "InOut", null, true);
        await Ts.InsertSequenceParameterAsync(TempSeqFile, "Seq",
            "cont", "container", "InOut", null, true);

        var parms = await Ts.GetSequenceParametersAsync(TempSeqFile, "Seq");
        var objRef = parms.Find(x => x.Name == "objRef");
        var cont   = parms.Find(x => x.Name == "cont");

        Assert.That(objRef, Is.Not.Null);
        Assert.That(objRef!.DataType, Does.Contain("Reference"),
            "'reference' param must be an Object Reference, not String");
        Assert.That(cont, Is.Not.Null);
        Assert.That(cont!.DataType, Does.Contain("Container"),
            "'container' param must be a Container, not String");
    }

    // ── 2) Enum instance-value writes ────────────────────────────────────────────

    [Test]
    public async Task SetLocalVariable_EnumValue_SucceedsAndPreservesType()
    {
        await PrepareFileAsync("Seq");
        await Ts.InsertLocalVariableAsync(TempSeqFile, "Seq", "resp", EnumName);

        // Pre-fix this threw "Expected type RespKind. Found type Number".
        Assert.DoesNotThrowAsync(async () =>
            await Ts.SetLocalVariableValueAsync(TempSeqFile, "Seq", "resp", "246"));

        var v = (await Ts.GetLocalVariablesAsync(TempSeqFile, "Seq")).Find(x => x.Name == "resp");
        Assert.That(v, Is.Not.Null);
        Assert.That(v!.DataType, Does.Contain(EnumName),
            "writing the value must not retype the enum to Number");
    }

    [Test]
    public async Task SetPropertyValue_EnumValueByNumber_SucceedsAndPreservesType()
    {
        await PrepareFileAsync("Seq");
        await Ts.InsertLocalVariableAsync(TempSeqFile, "Seq", "resp", EnumName);

        Assert.DoesNotThrowAsync(async () =>
            await Ts.SetPropertyValueAsync(TempSeqFile, "Seq", "resp", "number", "246"));

        var v = (await Ts.GetLocalVariablesAsync(TempSeqFile, "Seq")).Find(x => x.Name == "resp");
        Assert.That(v, Is.Not.Null);
        Assert.That(v!.DataType, Does.Contain(EnumName));
    }
}
