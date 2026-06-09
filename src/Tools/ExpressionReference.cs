using System;
using System.Collections.Generic;
using System.Linq;

namespace TestStandMCP.Tools;

// ── Expression-language reference catalogue ──────────────────────────────────
// A static, engine-free catalogue of the TestStand expression language's
// building blocks — operators, constants and built-in functions — mirroring the
// three top-level groups of the Sequence Editor's Expression Browser
// (Operators / Constants / Functions). It exists so an expression can be written
// from a quick lookup instead of trial-and-error.
//
// Correctness discipline: every `function` entry below was confirmed to EXIST
// live in the engine via evaluate_expression (Verified=true). Functions that are
// commonly *guessed* but do NOT exist (Floor, Ceil, Mod, Rnd, Now, …) are
// deliberately absent — see the notes on `%`, Round, Pow and Str for the
// idioms that replace them. Operators and the boolean constants are fixed
// language facts. Pure logic — no COM / no engine connection required.

/// <summary>A single expression-language reference entry (operator, constant or function).</summary>
public sealed class ExpressionReferenceEntry
{
    /// <summary>Token or function name, e.g. "+", "Round", "True".</summary>
    public string Name { get; init; } = "";
    /// <summary>Group: "operator", "constant" or "function".</summary>
    public string Kind { get; init; } = "";
    /// <summary>Category within the group, e.g. "Arithmetic", "Bitwise", "Numeric", "String", "Array".</summary>
    public string Category { get; init; } = "";
    /// <summary>Call/usage signature, e.g. "Round(number [, mode])".</summary>
    public string Signature { get; init; } = "";
    /// <summary>What it does.</summary>
    public string Description { get; init; } = "";
    /// <summary>A short, evaluable example, if helpful.</summary>
    public string? Example { get; init; }
    /// <summary>Gotcha / non-obvious behaviour worth knowing before use.</summary>
    public string? Note { get; init; }
    /// <summary>True when confirmed to exist live in the engine (functions) or a fixed language fact
    /// (operators/constants); false for plausible-but-unverified entries.</summary>
    public bool Verified { get; init; }
}

/// <summary>Static, searchable catalogue backing the <c>list_expression_reference</c> tool.</summary>
public static class ExpressionReference
{
    /// <summary>The three top-level groups, mirroring the Expression Browser.</summary>
    public static IReadOnlyList<string> Kinds { get; } = new[] { "operator", "constant", "function" };

    /// <summary>The complete catalogue.</summary>
    public static IReadOnlyList<ExpressionReferenceEntry> All => _all;

    private static readonly List<ExpressionReferenceEntry> _all = Build();

    /// <summary>
    /// Filter the catalogue. All filters are optional and combine with AND.
    /// <paramref name="kind"/> accepts singular or plural ("operator"/"operators"), case-insensitive.
    /// <paramref name="search"/> is a case-insensitive substring matched against name, signature,
    /// category, description and note.
    /// </summary>
    public static IReadOnlyList<ExpressionReferenceEntry> Query(
        string? kind = null, string? category = null, string? search = null)
    {
        IEnumerable<ExpressionReferenceEntry> q = _all;

        if (!string.IsNullOrWhiteSpace(kind))
        {
            var k = NormalizeKind(kind);
            q = q.Where(e => e.Kind == k);
        }

        if (!string.IsNullOrWhiteSpace(category))
            q = q.Where(e => e.Category.Equals(category.Trim(), StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(e =>
                e.Name.Contains(s, StringComparison.OrdinalIgnoreCase)        ||
                e.Signature.Contains(s, StringComparison.OrdinalIgnoreCase)   ||
                e.Category.Contains(s, StringComparison.OrdinalIgnoreCase)    ||
                e.Description.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                (e.Note != null && e.Note.Contains(s, StringComparison.OrdinalIgnoreCase)));
        }

        return q.ToList();
    }

    /// <summary>Distinct categories present, optionally scoped to one <paramref name="kind"/>.</summary>
    public static IReadOnlyList<string> Categories(string? kind = null)
    {
        IEnumerable<ExpressionReferenceEntry> q = _all;
        if (!string.IsNullOrWhiteSpace(kind))
        {
            var k = NormalizeKind(kind);
            q = q.Where(e => e.Kind == k);
        }
        return q.Select(e => e.Category).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();
    }

    /// <summary>Lower-cases and singularises a kind filter ("Operators" → "operator").</summary>
    private static string NormalizeKind(string kind)
    {
        var k = kind.Trim().ToLowerInvariant();
        if (k.EndsWith("s", StringComparison.Ordinal)) k = k.Substring(0, k.Length - 1);
        return k;
    }

    // ── Entry factories ──────────────────────────────────────────────────────
    private static ExpressionReferenceEntry Op(string name, string category, string signature,
        string description, string? example = null, string? note = null) => new()
    {
        Name = name, Kind = "operator", Category = category, Signature = signature,
        Description = description, Example = example, Note = note, Verified = true
    };

    private static ExpressionReferenceEntry Const(string name, string category,
        string description, string? example = null, string? note = null) => new()
    {
        Name = name, Kind = "constant", Category = category, Signature = name,
        Description = description, Example = example, Note = note, Verified = true
    };

    private static ExpressionReferenceEntry Fn(string name, string category, string signature,
        string description, string? example = null, string? note = null, bool verified = true) => new()
    {
        Name = name, Kind = "function", Category = category, Signature = signature,
        Description = description, Example = example, Note = note, Verified = verified
    };

    // ── The catalogue ─────────────────────────────────────────────────────────
    private static List<ExpressionReferenceEntry> Build() => new()
    {
        // ===== OPERATORS =========================================================
        // Arithmetic
        Op("+", "Arithmetic", "a + b",
            "Addition of numbers; also concatenates strings.",
            "2 + 3 == 5  ;  \"a\" + \"b\" == \"ab\"",
            "With a string operand, '+' concatenates instead of adding."),
        Op("-", "Arithmetic", "a - b   /   -a",
            "Subtraction, and unary negation.", "5 - 2 == 3  ;  -x"),
        Op("*", "Arithmetic", "a * b", "Multiplication.", "4 * 3 == 12"),
        Op("/", "Arithmetic", "a / b",
            "Division. TestStand numbers are doubles, so division is floating-point.",
            "10 / 4 == 2.5"),
        Op("%", "Arithmetic", "a % b",
            "Remainder (modulo).", "10 % 3 == 1",
            "There is NO Mod() function — use this operator. There is also no Floor/Ceil/Trunc; " +
            "use Round(x, mode) or Str(x, \"%.0f\") to drop the fraction."),

        // Comparison
        Op("==", "Comparison", "a == b",
            "Equality (numbers, booleans, strings). String comparison is case-sensitive.",
            "x == 5", "For explicit/typed string comparison use StrComp."),
        Op("!=", "Comparison", "a != b", "Inequality.", "x != 0"),
        Op("<",  "Comparison", "a < b",  "Less than.",  "x < 10"),
        Op("<=", "Comparison", "a <= b", "Less than or equal.", "x <= 10"),
        Op(">",  "Comparison", "a > b",  "Greater than.", "x > 0"),
        Op(">=", "Comparison", "a >= b", "Greater than or equal.", "x >= 0"),

        // Logical
        Op("&&", "Logical", "a && b", "Logical AND (short-circuit).", "x > 0 && x < 10"),
        Op("||", "Logical", "a || b", "Logical OR (short-circuit).",  "x < 0 || x > 10"),
        Op("!",  "Logical", "!a",     "Logical NOT.", "!Locals.Done"),

        // Bitwise
        Op("&",  "Bitwise", "a & b",  "Bitwise AND.",  "0x0F & 0x03 == 0x03"),
        Op("|",  "Bitwise", "a | b",  "Bitwise OR.",   "0x01 | 0x02 == 0x03"),
        Op("^",  "Bitwise", "a ^ b",  "Bitwise XOR.",  "0x0F ^ 0x09 == 0x06",
            "'^' is XOR, NOT exponentiation — for powers use Pow(base, exp)."),
        Op("~",  "Bitwise", "~a",     "Bitwise complement (NOT).", "~0"),
        Op("<<", "Bitwise", "a << n", "Left shift.",   "1 << 4 == 16"),
        Op(">>", "Bitwise", "a >> n", "Right shift.",  "16 >> 2 == 4"),

        // Assignment
        Op("=", "Assignment", "lvalue = value",
            "Assigns a value to a variable/property.", "Locals.Count = 5"),
        Op("+= -= *= /= %= &= |= ^= <<= >>=", "Assignment", "lvalue op= value",
            "Compound assignment: apply the operator and store back into the lvalue.",
            "Locals.Count += 1"),

        // Conditional / access / sequencing
        Op("?:", "Conditional", "cond ? a : b",
            "Ternary conditional — evaluates to 'a' when cond is true, else 'b'.",
            "Locals.X >= 0 ? \"pos\" : \"neg\""),
        Op(".", "Access", "object.member",
            "Member access on a PropertyObject / container / namespace.",
            "Locals.MyContainer.Field"),
        Op("[]", "Access", "array[index]",
            "Array element subscript (0-based).", "Locals.Data[0]"),
        Op(",", "Sequencing", "expr1, expr2, …",
            "Comma operator — evaluates several sub-expressions left to right in one expression " +
            "(e.g. a Statement step that sets several variables).",
            "Locals.A = 1, Locals.B = 2"),

        // ===== CONSTANTS =========================================================
        Const("True",  "Boolean", "Boolean true.",  "Locals.Flag = True"),
        Const("False", "Boolean", "Boolean false.", "Locals.Flag = False"),
        // NOTE: the Expression Browser also exposes Color/other constant groups. Those identifiers
        // are not catalogued here yet — add them only after live-verifying the exact names, to keep
        // every entry trustworthy (see the always-readback-test discipline).

        // ===== FUNCTIONS — Numeric ==============================================
        Fn("Abs",    "Numeric", "Abs(number)", "Absolute value.", "Abs(-3) == 3"),
        Fn("Round",  "Numeric", "Round(number [, mode])",
            "Rounds to the nearest integer.", "Round(2.5) == 3",
            "The 2nd argument is a ROUNDING-MODE enum (rounds up), NOT a decimal-place count: " +
            "Round(3.14159, 2) == 4. For decimal places use Str(x, \"%.2f\")."),
        Fn("Sqrt",   "Numeric", "Sqrt(number)", "Square root.", "Sqrt(9) == 3"),
        Fn("Pow",    "Numeric", "Pow(base, exponent)", "Raises base to a power.", "Pow(2, 10) == 1024",
            "There is no '^'/'**' power operator — '^' is bitwise XOR. Use Pow."),
        Fn("Exp",    "Numeric", "Exp(number)", "e raised to the given power.", "Exp(0) == 1"),
        Fn("Log",    "Numeric", "Log(number)", "Natural logarithm (base e).", "Log(Exp(1)) == 1",
            "This is the NATURAL log (base e). For base-10 use Log10."),
        Fn("Log10",  "Numeric", "Log10(number)", "Base-10 logarithm.", "Log10(1000) == 3"),
        Fn("Sin",    "Numeric", "Sin(radians)", "Sine (argument in radians).", "Sin(0) == 0"),
        Fn("Cos",    "Numeric", "Cos(radians)", "Cosine (argument in radians).", "Cos(0) == 1"),
        Fn("Tan",    "Numeric", "Tan(radians)", "Tangent (argument in radians).", null,
            "Trigonometric family; ASin/ACos/ATan are analogous but not live-verified.", verified: false),
        Fn("Min",    "Numeric", "Min(a, b [, …])", "Smallest of its arguments.", "Min(3, 7) == 3"),
        Fn("Max",    "Numeric", "Max(a, b [, …])", "Largest of its arguments.", "Max(3, 7) == 7"),
        Fn("Random", "Numeric", "Random(min, max)",
            "Pseudo-random double in the range [min, max].", "Random(0, 1)",
            "The function is named Random — there is no Rnd()."),

        // ===== FUNCTIONS — String ===============================================
        Fn("Len",   "String", "Len(string)", "Number of characters.", "Len(\"abc\") == 3"),
        Fn("Left",  "String", "Left(string, count)", "Leftmost 'count' characters.", "Left(\"abcd\", 2) == \"ab\""),
        Fn("Right", "String", "Right(string, count)", "Rightmost 'count' characters.", "Right(\"abcd\", 2) == \"cd\""),
        Fn("Mid",   "String", "Mid(string, start, length)",
            "Substring of 'length' chars from 'start'.", "Mid(\"abcde\", 1, 3) == \"bcd\"",
            "'start' is 0-based."),
        Fn("ToUpper", "String", "ToUpper(string)", "Upper-cases the string.", "ToUpper(\"ab\") == \"AB\""),
        Fn("ToLower", "String", "ToLower(string)", "Lower-cases the string.", "ToLower(\"AB\") == \"ab\""),
        Fn("Trim",    "String", "Trim(string)", "Removes leading/trailing whitespace.", "Trim(\"  x \") == \"x\""),
        Fn("StrComp", "String", "StrComp(a, b)",
            "Compares two strings.", "StrComp(\"a\", \"b\") == -1",
            "Returns -1, 0 or 1 (a<b, a==b, a>b)."),
        Fn("Find",    "String", "Find(string, sub)",
            "Index of the first occurrence of 'sub'.", "Find(\"abcabc\", \"c\") == 2",
            "Returns a 0-based index, or -1 when not found."),
        Fn("SearchAndReplace", "String", "SearchAndReplace(string, find, replace)",
            "Replaces occurrences of 'find' with 'replace'.", "SearchAndReplace(\"a.b.c\", \".\", \"-\") == \"a-b-c\""),
        Fn("Chr", "String", "Chr(code)", "Character for a numeric character code.", "Chr(65) == \"A\""),
        Fn("Asc", "String", "Asc(char)", "Numeric character code of the first character.", "Asc(\"A\") == 65",
            "Use Asc — there is no Ord()."),

        // ===== FUNCTIONS — Conversion / Format ==================================
        Fn("Str", "Conversion", "Str(number [, printfFormat])",
            "Converts a number to a string, optionally with a printf-style format.",
            "Str(255, \"%X\") == \"FF\"  ;  Str(3.14159, \"%.2f\") == \"3.14\"",
            "This IS the formatting mechanism — there is no Format(). For decimal places use " +
            "Str(x, \"%.2f\"); for hex use \"%X\"."),
        Fn("Val", "Conversion", "Val(string)", "Parses a string into a number.", "Val(\"3.5\") == 3.5"),

        // ===== FUNCTIONS — Array =================================================
        Fn("GetNumElements", "Array", "GetNumElements(array)",
            "Number of elements in a (1-D) array.", "GetNumElements(Locals.Data)",
            "In evaluate_expression's FileGlobals context (sequence_file_path given), reference the " +
            "global by BARE name — GetNumElements(MyGlobal) — NOT FileGlobals.MyGlobal."),
        Fn("SetNumElements", "Array", "SetNumElements(array, n)",
            "Resizes the array to 'n' elements.", "SetNumElements(Locals.Data, 10)"),
        Fn("GetArrayBounds", "Array", "GetArrayBounds(array, lowerStr, upperStr)",
            "Writes the array bounds into the two string arguments.", null,
            "Args 2 & 3 are STRING out-parameters, not arrays. GetNumElementsInDim / GetNumDimensions " +
            "do NOT exist."),

        // ===== FUNCTIONS — Misc / Property ======================================
        Fn("PropertyExists", "Property", "PropertyExists(lookupString)",
            "True if the named property/variable exists.", "PropertyExists(\"Locals.Data\")",
            "Takes a single lookup-string argument."),
        Fn("Time", "DateTime", "Time()", "Current time as an \"HH:MM:SS\" string.", "Time()",
            "No Now()/Date() built-in is confirmed."),
    };
}
