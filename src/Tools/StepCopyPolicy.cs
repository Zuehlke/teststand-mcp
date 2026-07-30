using System;

namespace TestStandMCP.Tools;

/// <summary>How a single step property should be reproduced on the target step.</summary>
internal enum StepPropertyAction
{
    /// <summary>Source and target already agree — do not touch the step at all.</summary>
    SkipIdentical,
    /// <summary>Write the scalar leaf BY VALUE onto the node the target already has.</summary>
    WriteScalarValue,
    /// <summary>Replace the whole subtree with a flag-preserving clone.</summary>
    CloneSubtree,
}

/// <summary>
/// The write-as-little-as-possible policy behind <c>copy_step_module</c> and the import's module pass.
/// <para>
/// WHY THIS IS NOT "just clone everything". <c>SetPropertyObject</c> REPLACES the property object, and
/// many step properties belong to the step TYPE. Replacing one registers a second, conflicting instance
/// of that type, so the file's copy of e.g. <c>NI_Flow_If</c> no longer matches the loaded one and the
/// Sequence Editor greets the rebuild with a "Type Conflict in File" dialog. The file is functionally
/// complete and the native FileDiffer calls it <c>identical</c> — it cannot see type-registration
/// conflicts — so this was invisible to every automated check and only surfaced on opening the file.
/// Measured on TFW_Symphony_DutCom.seq: cloning every path touched 79 subtrees and raised the dialog;
/// deciding per property brought that to 47 and the dialog disappeared.
/// </para>
/// Hence: a scalar leaf is compared and written by VALUE only when it actually differs, an empty
/// list-like node carries nothing and is skipped, and the object copy is the last resort.
/// </summary>
internal static class StepCopyPolicy
{
    /// <summary>True for a node whose payload lives in ARRAY ELEMENTS rather than subproperties — an
    /// array or a TestStand "Argument List". Such a node reports 0 subproperties even when it holds
    /// data, so the scalar branch must not claim it.</summary>
    internal static bool IsListLike(string? typeDisplay) =>
        typeDisplay != null
        && (typeDisplay.Contains("Array", StringComparison.Ordinal)
            || typeDisplay.Contains("Argument List", StringComparison.Ordinal));

    /// <summary>
    /// Decides how to reproduce one property. <paramref name="srcScalar"/>/<paramref name="tgtScalar"/>
    /// are only meaningful for a scalar leaf and <paramref name="srcElements"/>/
    /// <paramref name="tgtElements"/> only for a list-like node; the caller reads whichever pair applies
    /// so no COM call is made for a branch that cannot be taken.
    /// </summary>
    internal static StepPropertyAction Decide(string? typeDisplay, int srcSubProperties,
        string? srcScalar, string? tgtScalar, int srcElements, int tgtElements)
    {
        bool listLike = IsListLike(typeDisplay);

        // A container/structure with members always goes through the object copy: its payload is the
        // subtree, and comparing it scalar-wise is not possible.
        if (srcSubProperties != 0) return StepPropertyAction.CloneSubtree;

        if (listLike)
        {
            // Both sides empty ⇒ nothing to carry. Replacing an empty array with an empty array is
            // exactly the pointless write that triggers the type conflict.
            return srcElements == 0 && tgtElements == 0
                ? StepPropertyAction.SkipIdentical
                : StepPropertyAction.CloneSubtree;
        }

        // Scalar leaf.
        if (srcScalar == tgtScalar) return StepPropertyAction.SkipIdentical;
        // Unreadable source scalar (an exotic leaf type): fall back to the object copy rather than
        // silently leaving the target's value in place.
        return srcScalar != null
            ? StepPropertyAction.WriteScalarValue
            : StepPropertyAction.CloneSubtree;
    }
}
