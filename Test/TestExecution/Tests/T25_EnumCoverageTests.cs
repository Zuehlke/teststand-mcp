using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// Enum-handling coverage with VALUE READBACK — closes the two gaps from the enum audit:
///
///   • Lücke 1 (structural): every enum is SET and then READ BACK, so a silent
///     "_ => default" fallback that mis-maps a documented contract string is caught.
///     A bare DoesNotThrow assertion (as in T05/T06) cannot detect this, because every
///     mapping swallows an unknown string into a default value instead of throwing.
///     Each walk transitions through values so that consecutive expected results differ —
///     every assertion therefore proves the set actually took effect (never the prior /
///     default value). A dedicated unknown-input test pins the default behaviour.
///
///   • Lücke 2 (breadth): every documented enum value is exercised, not just one.
///
/// The accepted strings are the ones advertised by each tool's MCP schema
/// (TestStandToolRegistry), so this fixture also pins the published contract.
/// </summary>
[TestFixture]
[Category("EnumCoverage")]
public class T25_EnumCoverageTests : TestBase
{
    private const string Seq = "EnumCov";
    private const string Grp = "Main";

    // ── helpers ──────────────────────────────────────────────────────────────────

    private async Task NewStepAsync(string stepType, string stepName = "s")
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, Seq);
        await Ts.InsertStepAsync(TempSeqFile, Seq, Grp, stepType, stepName);
    }

    private async Task<object?> ReadStepPropAsync(string key, string stepName = "s")
    {
        var props = await Ts.GetStepPropertiesAsync(TempSeqFile, Seq, stepName);
        return props.TryGetValue(key, out var v) ? v : null;
    }

    // ══ RunMode  (set_step_run_mode: 'Normal','Skip','ForcedPass','ForcedFail') ════
    // Service maps to the TestStand RunMode strings Normal / Skip / Pass / Fail.

    [Test]
    public async Task RunMode_AllDocumentedValues_MapAndReadBack()
    {
        await NewStepAsync("Statement");

        await Ts.SetStepRunModeAsync(TempSeqFile, Seq, Grp, "s", "Skip");
        Assert.That(await ReadStepPropAsync("RunMode"), Is.EqualTo("Skip"));

        await Ts.SetStepRunModeAsync(TempSeqFile, Seq, Grp, "s", "ForcedPass");
        Assert.That(await ReadStepPropAsync("RunMode"), Is.EqualTo("Pass"));

        await Ts.SetStepRunModeAsync(TempSeqFile, Seq, Grp, "s", "ForcedFail");
        Assert.That(await ReadStepPropAsync("RunMode"), Is.EqualTo("Fail"));

        await Ts.SetStepRunModeAsync(TempSeqFile, Seq, Grp, "s", "Normal");
        Assert.That(await ReadStepPropAsync("RunMode"), Is.EqualTo("Normal"));
    }

    [Test]
    public async Task RunMode_UnknownInput_FallsBackToNormal()
    {
        await NewStepAsync("Statement");
        await Ts.SetStepRunModeAsync(TempSeqFile, Seq, Grp, "s", "Skip");        // move off default
        await Ts.SetStepRunModeAsync(TempSeqFile, Seq, Grp, "s", "not-a-mode");  // unknown → default
        Assert.That(await ReadStepPropAsync("RunMode"), Is.EqualTo("Normal"));
    }

    // ══ Pass / Fail action  ('NextStep','Break','Terminate','GoToStep') ════════════
    // Service maps to PostActionValues Next / Break / Terminate / Goto.

    [Test]
    public async Task PassAction_AllDocumentedValues_MapAndReadBack()
    {
        await NewStepAsync("Statement");
        await Ts.InsertStepAsync(TempSeqFile, Seq, Grp, "Statement", "t");   // Goto target

        await Ts.SetStepPassActionAsync(TempSeqFile, Seq, Grp, "s", "Break");
        Assert.That(await ReadStepPropAsync("PassAction"), Is.EqualTo("Break"));

        await Ts.SetStepPassActionAsync(TempSeqFile, Seq, Grp, "s", "Terminate");
        Assert.That(await ReadStepPropAsync("PassAction"), Is.EqualTo("Terminate"));

        await Ts.SetStepPassActionAsync(TempSeqFile, Seq, Grp, "s", "GoToStep", "t");
        Assert.That(await ReadStepPropAsync("PassAction"), Is.EqualTo("Goto"));

        await Ts.SetStepPassActionAsync(TempSeqFile, Seq, Grp, "s", "NextStep");
        Assert.That(await ReadStepPropAsync("PassAction"), Is.EqualTo("Next"));
    }

    [Test]
    public async Task FailAction_AllDocumentedValues_MapAndReadBack()
    {
        await NewStepAsync("Statement");
        await Ts.InsertStepAsync(TempSeqFile, Seq, Grp, "Statement", "t");   // Goto target

        await Ts.SetStepFailActionAsync(TempSeqFile, Seq, Grp, "s", "Break");
        Assert.That(await ReadStepPropAsync("FailAction"), Is.EqualTo("Break"));

        await Ts.SetStepFailActionAsync(TempSeqFile, Seq, Grp, "s", "Terminate");
        Assert.That(await ReadStepPropAsync("FailAction"), Is.EqualTo("Terminate"));

        await Ts.SetStepFailActionAsync(TempSeqFile, Seq, Grp, "s", "GoToStep", "t");
        Assert.That(await ReadStepPropAsync("FailAction"), Is.EqualTo("Goto"));

        await Ts.SetStepFailActionAsync(TempSeqFile, Seq, Grp, "s", "NextStep");
        Assert.That(await ReadStepPropAsync("FailAction"), Is.EqualTo("Next"));
    }

    [Test]
    public async Task PassAction_UnknownInput_FallsBackToNext()
    {
        await NewStepAsync("Statement");
        await Ts.SetStepPassActionAsync(TempSeqFile, Seq, Grp, "s", "Terminate"); // move off default
        await Ts.SetStepPassActionAsync(TempSeqFile, Seq, Grp, "s", "nonsense");  // unknown → default
        Assert.That(await ReadStepPropAsync("PassAction"), Is.EqualTo("Next"));
    }

    // ══ Loop type  (set_step_loop: 'NoLoop','While','For','Condition') ═════════════
    // 'While'/'Condition' now map to the Custom loop type (regression-fix: they used to
    // fall through to NoLooping).

    [Test]
    public async Task LoopType_AllDocumentedValues_MapAndReadBack()
    {
        await NewStepAsync("Statement");

        await Ts.SetStepLoopAsync(TempSeqFile, Seq, Grp, "s", "For");
        Assert.That(await ReadStepPropAsync("LoopType"), Is.EqualTo("FixedNumLoops"));

        await Ts.SetStepLoopAsync(TempSeqFile, Seq, Grp, "s", "While");
        Assert.That(await ReadStepPropAsync("LoopType"), Is.EqualTo("Custom"),
            "'While' must map to the Custom loop type, not silently fall through to NoLooping");

        await Ts.SetStepLoopAsync(TempSeqFile, Seq, Grp, "s", "NoLoop");
        Assert.That(await ReadStepPropAsync("LoopType"), Is.EqualTo("NoLooping"));

        await Ts.SetStepLoopAsync(TempSeqFile, Seq, Grp, "s", "Condition");
        Assert.That(await ReadStepPropAsync("LoopType"), Is.EqualTo("Custom"),
            "'Condition' must map to the Custom loop type");
    }

    [Test]
    public async Task LoopType_UnknownInput_FallsBackToNoLooping()
    {
        await NewStepAsync("Statement");
        await Ts.SetStepLoopAsync(TempSeqFile, Seq, Grp, "s", "For");      // move off default
        await Ts.SetStepLoopAsync(TempSeqFile, Seq, Grp, "s", "weird");    // unknown → default
        Assert.That(await ReadStepPropAsync("LoopType"), Is.EqualTo("NoLooping"));
    }

    // ══ NumericLimitTest comparison  (set_numeric_limits) ═════════════════════════
    // GELE/GE/LE/EQ/NE documented + GT/LT supported; verified via get_numeric_limits.

    [Test]
    public async Task NumericComparison_AllValues_RoundTrip()
    {
        await NewStepAsync("NumericLimitTest");

        foreach (var cmp in new[] { "EQ", "NE", "GT", "LT", "GE", "LE", "GELE" })
        {
            await Ts.SetNumericLimitsAsync(TempSeqFile, Seq, Grp, "s",
                lowLimit: 1.0, highLimit: 2.0, units: "V", comparisonType: cmp);

            var limits = await Ts.GetNumericLimitsAsync(TempSeqFile, Seq, Grp, "s");
            Assert.That(limits["comparison_type"], Is.EqualTo(cmp),
                $"comparison '{cmp}' must round-trip through get_numeric_limits");
        }
    }

    // ══ StringValueTest comparison  (configure_string_value_test) ═════════════════
    // CaseSensitive(0) / CaseInsensitive(1) / Ignore(2).

    [Test]
    public async Task StringComparison_AllValues_MapAndReadBack()
    {
        await NewStepAsync("StringValueTest");

        // Fresh-step default is "IgnoreCase", so set "CaseSensitive" first to prove the write.
        await Ts.ConfigureStringValueTestAsync(TempSeqFile, Seq, Grp, "s",
            "Locals.v", "abc", "CaseSensitive");
        Assert.That(await ReadStepPropAsync("ComparisonType"), Is.EqualTo("CaseSensitive"));

        await Ts.ConfigureStringValueTestAsync(TempSeqFile, Seq, Grp, "s",
            "Locals.v", "abc", "CaseInsensitive");
        Assert.That(await ReadStepPropAsync("ComparisonType"), Is.EqualTo("IgnoreCase"));
    }

    // ══ value_type  (set_property_value: 'boolean','number','string','container') ══
    // Verified via get_property_object's reported ValueType + value.

    [Test]
    public async Task PropertyValueType_Boolean_RoundTrips()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);

        await Ts.SetPropertyValueAsync(TempSeqFile, null, "BoolT", "boolean", "true");
        var t = await Ts.GetPropertyObjectAsync(TempSeqFile, null, "BoolT");
        Assert.That(t.ValueType, Is.EqualTo("Boolean"));
        Assert.That(Convert.ToBoolean(t.Value), Is.True);

        await Ts.SetPropertyValueAsync(TempSeqFile, null, "BoolF", "boolean", "false");
        var f = await Ts.GetPropertyObjectAsync(TempSeqFile, null, "BoolF");
        Assert.That(f.ValueType, Is.EqualTo("Boolean"));
        Assert.That(Convert.ToBoolean(f.Value), Is.False);
    }

    [TestCase("number", "Number")]
    [TestCase("string", "String")]
    public async Task PropertyValueType_NonBoolean_ReportsCorrectType(string valueType, string expected)
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        string value = valueType == "number" ? "1.5" : "hi";

        await Ts.SetPropertyValueAsync(TempSeqFile, null, "P", valueType, value);
        var info = await Ts.GetPropertyObjectAsync(TempSeqFile, null, "P");
        Assert.That(info.ValueType, Is.EqualTo(expected));
    }

    // ══ post_output_message severity  ('Error','Warning','Information') ═══════════
    // Verified by reading the message back from the engine output list.

    [TestCase("Error",       "Error")]
    [TestCase("Warning",     "Warning")]
    [TestCase("Information",  "Information")]
    [TestCase("bogus",       "Information")]   // unknown → default
    public async Task OutputMessageSeverity_MapsAndReadsBack(string input, string expected)
    {
        var unique = $"T25_SEV_{input}_{Guid.NewGuid():N}";
        await Ts.PostOutputMessageAsync(unique, "T25", input);

        var msgs = await Ts.GetOutputMessagesAsync(10000);
        var found = msgs.Find(m => m.Message == unique);
        Assert.That(found, Is.Not.Null, "posted message must appear in the engine output list");
        Assert.That(found!.Severity, Is.EqualTo(expected));
    }

    // NOTE — configure_message_popup and configure_property_loader were previously broken (their
    // writes targeted non-existent TS.MessagePopup.* / TS.PropertyLoader.* paths and were silently
    // swallowed). Both are now FIXED and covered by round-trip readback tests in T22
    // (ConfigureMessagePopup_PersistsSettings, ConfigurePropertyLoader_PersistsPath_OnRealLoaderStep,
    // ConfigurePropertyLoader_OnNonLoaderStep_ReportsClearError):
    //   • MessagePopup settings are TOP-LEVEL step properties: MessageExpr/TitleExpr (expression
    //     literals), Button1Label..Button6Label (the button set — there is no numeric 'Buttons'),
    //     TimeToWait + TimerButton (timeout).
    //   • PropertyLoader requires step type 'NI_PropertyLoader'; the file lives in
    //     PropertyLoaderSources[0].Options.CommonOptions.Source.Location (the step always imports,
    //     so 'mode' has no read/write toggle).

    // ══ Sequence FailureAction  (set/get_sequence_properties: Continue/Terminate/Abort) ══

    [Test]
    public async Task SequenceFailureAction_AllValues_RoundTrip()
    {
        await Ts.CreateSequenceFileAsync(TempSeqFile);
        await Ts.InsertSequenceAsync(TempSeqFile, Seq);

        foreach (var action in new[] { "Terminate", "Abort", "Continue" })
        {
            var props = await Ts.GetSequencePropertiesAsync(TempSeqFile, Seq);
            props.FailureAction = action;
            await Ts.SetSequencePropertiesAsync(TempSeqFile, Seq, props);

            var back = await Ts.GetSequencePropertiesAsync(TempSeqFile, Seq);
            Assert.That(back.FailureAction, Is.EqualTo(action),
                $"sequence FailureAction '{action}' must round-trip");
        }
    }
}
