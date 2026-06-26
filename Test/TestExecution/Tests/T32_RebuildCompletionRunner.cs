using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using TestStandMCP.Models;

namespace TestStandMCP.IntegrationTests.Tests;

/// <summary>
/// EXPLICIT one-shot runner that finishes the TFW_DemoModule 1:1 rebuild
/// (C:\temp\DemoModule_rebuild.seq) using ONLY the public service surface the MCP
/// tools wrap — create_step_property / set_step_property / set_step_property_flags /
/// insert_file_global. It materialises the pieces that were unreachable before those
/// tools existed: ViCall.Parms prototypes, SequenceCall ActualArgs+Prototype,
/// AdditionalResultsHints, Result.TimeoutOccurred, TS.ErrorDialogOptions, the
/// Object-Reference file global, VIDescriptions with bare CRs, and the VIModule flags.
///
/// The authoritative SPEC for each subtree is read from the original file via
/// get_property_tree and re-authored node by node through the creation tools — the
/// same calls an MCP client would make, just batched in-process.
///
/// Run manually:
///   dotnet test --filter "FullyQualifiedName~T32_RebuildCompletionRunner"
/// </summary>
[TestFixture]
[Explicit("One-shot completion for the local 1:1 rebuild exercise — needs C:\\temp\\DemoModule_rebuild.seq")]
public class T32_RebuildCompletionRunner : TestBase
{
    private const string Orig = @"C:\Projects\TestStandMcp\TestStandMCP\.Demo_jcm\Demo\TFW_DemoModule.seq";
    private const string Reb  = @"C:\temp\DemoModule_rebuild.seq";

    private sealed record StepRef(string Seq, string Group, string Step);

    [Test]
    public async Task CompleteRebuild()
    {
        if (!File.Exists(Orig) || !File.Exists(Reb))
            Assert.Ignore("original or rebuild file not present on this machine");

        // ── 1) FileGlobal ErrorHandlerThread: String → Object Reference ────────
        await Ts.DeleteSubPropertyAsync(Reb, null, "ErrorHandlerThread");
        await Ts.InsertFileGlobalAsync(Reb, "ErrorHandlerThread", "reference");

        // ── 2) ViCall.Parms prototypes on all LabVIEW steps ────────────────────
        var viCallSteps = new (StepRef Step, string Base)[]
        {
            (new("Init", "Main", "Start Module"),                          "TS.SData.ViCall"),
            (new("Init", "Main", "Synchronize Module Events"),             "TS.SData.ViCall"),
            (new("InitDemoModule", "Main", "Action"),                  "TS.SData.ViCall"),
            (new("OpenControllerValve", "Main", "OpenControllerValve"),    "TS.SData.ViCall"),
            (new("CloseControllerValve", "Main", "CloseControllerValve"),  "TS.SData.ViCall"),
            (new("SetSetpoint", "Main", "Set Setpoint"),                   "TS.SData.ViCall"),
            (new("DeinitDemoModule", "Main", "Deinit DemoModule"), "TS.SData.ViCall"),
            (new("Close", "Cleanup", "Stop Module"),                       "TS.SData.ViCall"),
            (new("ErrorHandler", "Setup", "Get Queue"),                    "TS.SData.ViCall"),
            (new("ErrorHandler", "Main", "Dequeue Element"),               "TS.SData.ViCall"),
            (new("ErrorHandler", "Cleanup", "Release Queue"),              "TS.SData.ViCall"),
            (new("DebugDriver", "Main", "LaunchTestCode"),                 "TS.SData.ViCall"),
            (new("ErrorHandler", "Main", "Launch DQMH Event Handler"),     "VIModule.ViCall"),
        };
        foreach (var (step, viBase) in viCallSteps)
        {
            await SyncSubtreeAsync(step, $"{viBase}.Parms");
            // VIDescription verbatim (bare \r and non-ASCII survive in-proc).
            await SyncLeafAsync(step, $"{viBase}.VIDescription");
        }

        // ── 3) SequenceCall ActualArgs + Prototype ─────────────────────────────
        var seqCallSteps = new StepRef[]
        {
            new("TESTCODE", "Main", "Call InitDemoModule"),
            new("TESTCODE", "Main", "Call SetSetpoint"),
            new("TESTCODE", "Main", "Call DeinitDemoModule"),
            new("ErrorHandler", "Main", "Launch DQMH Event Handler"),
        };
        foreach (var step in seqCallSteps)
        {
            await SyncSubtreeAsync(step, "TS.SData.ActualArgs");
            await SyncSubtreeAsync(step, "TS.SData.Prototype");
        }

        // ── 4) NI_Wait editor-side extras ──────────────────────────────────────
        var waitTestcode = new StepRef("TESTCODE", "Main", "Wait");
        var waitClose    = new StepRef("Close", "Cleanup", "Wait For Error Handler Thread to End");
        await SyncSubtreeAsync(waitTestcode, "TS.AdditionalResultsHints");
        await SyncSubtreeAsync(waitClose,    "TS.AdditionalResultsHints");
        await SyncSubtreeAsync(waitClose,    "TS.ErrorDialogOptions");
        await SyncSubtreeAsync(waitClose,    "Result.TimeoutOccurred");

        // ── 5) VIModule container flags on the RunVIAsync step ─────────────────
        await Ts.SetStepPropertyFlagsAsync(Reb, "ErrorHandler", "Main",
            "Launch DQMH Event Handler", "VIModule", 0x200000, save: false);

        await Ts.SaveSequenceFileAsync(Reb);

        // ── 6) Verify: native FileDiffer must report ZERO differences ──────────
        // FileDiffer pairs array elements by their ELEMENT NAME (PropertyObject.Name) —
        // ViCall.Parms entries are named after the connector-pane label, and the element
        // names are mirrored above via RenameStepPropertyAsync. With the names in place
        // the rebuild is byte-for-byte equivalent for the differ.
        var diff = await Ts.DiffSequenceFilesAsync(Orig, Reb);
        TestContext.WriteLine($"TOTAL DIFFS AFTER COMPLETION: {diff.TotalDifferences}");
        foreach (var c in diff.Changes.Take(40))
            TestContext.WriteLine($"  [{c.ChangeType}] {c.Path} > {c.Name}: " +
                                  $"'{Trunc(c.File1Value)}' vs '{Trunc(c.File2Value)}'");

        Assert.That(diff.Identical, Is.True,
            "the completed rebuild must be identical to the original per the native FileDiffer");
    }

    private static string Trunc(string? s) =>
        s == null ? "" : (s.Length <= 60 ? s : s[..60] + "…");

    /// <summary>Reads one leaf value from the original and writes it verbatim to the rebuild.</summary>
    private async Task SyncLeafAsync(StepRef step, string relPath)
    {
        var src = await ReadOrigAsync(step, relPath);
        if (src?.ValueType is "Number" or "Boolean" or "String")
            await WriteLeafAsync(step, relPath, src);
    }

    /// <summary>
    /// Recursively mirrors a step-relative subtree of the original into the rebuild using
    /// the creation-capable service surface (CreateStepPropertyAsync / SetStepPropertyAsync).
    /// </summary>
    private async Task SyncSubtreeAsync(StepRef step, string relPath)
    {
        var src = await ReadOrigAsync(step, relPath);
        if (src == null)
        {
            TestContext.WriteLine($"  (skip) {step.Step}: {relPath} not present in original");
            return;
        }
        await SyncNodeAsync(step, relPath, src);
    }

    private async Task<PropertyNode?> ReadOrigAsync(StepRef step, string relPath)
    {
        string lookup = $"Data.Seq[\"{step.Seq}\"].{step.Group}[\"{step.Step}\"].{relPath}";
        try
        {
            return await Ts.GetPropertyTreeAsync("SequenceFile", Orig, lookup,
                maxDepth: 25, includeHidden: true, maxArrayElements: 0);
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"  (no source) {lookup}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Maps "Array of Xs[..]" display strings to a create_step_property element type.</summary>
    private static string? ArrayElementTypeName(string? typeDisplay)
    {
        if (string.IsNullOrEmpty(typeDisplay) || !typeDisplay.StartsWith("Array of ", StringComparison.Ordinal))
            return null;
        string elem = typeDisplay["Array of ".Length..];
        int bracket = elem.IndexOf('[');
        if (bracket >= 0) elem = elem[..bracket];
        return elem switch
        {
            "Containers" => "container",
            "Strings"    => "string",
            "Numbers"    => "number",
            "Booleans"   => "boolean",
            _            => elem, // named type, e.g. "VIParameter", "NI_CustomResult"
        };
    }

    private async Task SyncNodeAsync(StepRef step, string relPath, PropertyNode src)
    {
        string typeDisp = src.Type ?? "";
        switch (src.ValueType)
        {
            case "Array":
            {
                int n = src.ArraySize ?? src.Children?.Count ?? 0;
                await Ts.CreateStepPropertyAsync(Reb, step.Seq, step.Group, step.Step,
                    relPath, "array_elements", typeName: ArrayElementTypeName(typeDisp),
                    numElements: n, save: false);
                if (src.Children != null)
                    for (int i = 0; i < src.Children.Count; i++)
                    {
                        await SyncNodeAsync(step, $"{relPath}[{i}]", src.Children[i]);
                        // Named array elements (ViCall.Parms entries carry the connector-pane
                        // label as their element name — FileDiffer pairs elements by it).
                        if (!string.IsNullOrEmpty(src.Children[i].ElementName))
                            await Ts.RenameStepPropertyAsync(Reb, step.Seq, step.Group, step.Step,
                                $"{relPath}[{i}]", src.Children[i].ElementName!, save: false);
                    }
                break;
            }
            case "Container":
            {
                // Named type ("SequenceArgument (Container)") vs anonymous "Container".
                if (typeDisp.EndsWith(" (Container)", StringComparison.Ordinal))
                {
                    string typeName = typeDisp[..^" (Container)".Length];
                    await Ts.CreateStepPropertyAsync(Reb, step.Seq, step.Group, step.Step,
                        relPath, "named_type", typeName: typeName, save: false);
                }
                else
                {
                    await Ts.CreateStepPropertyAsync(Reb, step.Seq, step.Group, step.Step,
                        relPath, "container", save: false);
                }
                foreach (var child in src.Children ?? new List<PropertyNode>())
                    await SyncNodeAsync(step, $"{relPath}.{child.Name}", child);
                break;
            }
            case "Number" or "Boolean" or "String":
                await WriteLeafAsync(step, relPath, src);
                break;
            case "Empty":
                // Value-less nodes still need their STRUCTURE mirrored: an unset Prototype
                // slot is an empty anonymous Container, an empty typed array reads as Empty
                // ("Array of X[0..empty]"). Truly value-less scalars are skipped.
                if (typeDisp.EndsWith(" (Container)", StringComparison.Ordinal))
                {
                    await Ts.CreateStepPropertyAsync(Reb, step.Seq, step.Group, step.Step,
                        relPath, "named_type",
                        typeName: typeDisp[..^" (Container)".Length], save: false);
                }
                else if (typeDisp == "Container")
                {
                    await Ts.CreateStepPropertyAsync(Reb, step.Seq, step.Group, step.Step,
                        relPath, "container", save: false);
                }
                else if (ArrayElementTypeName(typeDisp) is string elemType)
                {
                    await Ts.CreateStepPropertyAsync(Reb, step.Seq, step.Group, step.Step,
                        relPath, "array_elements", typeName: elemType,
                        numElements: 0, save: false);
                }
                break;
        }

        // Mirror non-default property flags (e.g. PassByReference=0x4 on Prototype members).
        // 0x200000 (module marker) and inherited defaults are handled where they matter.
        if (src.Flags == 4)
        {
            try
            {
                await Ts.SetStepPropertyFlagsAsync(Reb, step.Seq, step.Group, step.Step,
                    relPath, src.Flags, save: false);
            }
            catch (Exception ex)
            {
                TestContext.WriteLine($"  (flags skip) {relPath}: {ex.Message}");
            }
        }
    }

    private async Task WriteLeafAsync(StepRef step, string relPath, PropertyNode src)
    {
        string vt = src.ValueType switch
        {
            "Number"  => "number",
            "Boolean" => "boolean",
            _         => "string",
        };
        string val = src.Value switch
        {
            null      => "",
            bool b    => b ? "true" : "false",
            double d  => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            _         => src.Value.ToString() ?? "",
        };
        // CreateStepPropertyAsync is idempotent: creates the leaf when missing (typed
        // members of named types already exist), then applies the value verbatim.
        await Ts.CreateStepPropertyAsync(Reb, step.Seq, step.Group, step.Step,
            relPath, vt, value: val, save: false);
    }
}
