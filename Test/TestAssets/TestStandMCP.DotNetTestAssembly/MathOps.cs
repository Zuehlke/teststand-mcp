namespace TestStandMCP.DotNetTestAssembly
{
    /// <summary>
    /// Static members covering the signatures a .NET step has to be able to bind: zero arguments,
    /// arguments, void and non-void returns, an out parameter, and overloads that only a full
    /// signature can tell apart.
    /// </summary>
    public static class MathOps
    {
        /// <summary>The ONLY shape the module-level LoadMemberInfo route ever resolved.</summary>
        public static void NoArgsVoid() { }

        /// <summary>Two arguments AND a return value — the case from issue #37.</summary>
        public static double Add(double a, double b) { return a + b; }

        public static void OneArgVoid(double a) { }

        public static void Split(double value, out double half) { half = value / 2.0; }

        public static double Overloaded(double a) { return a; }

        public static double Overloaded(double a, double b) { return a + b; }
    }

    /// <summary>An instance member cannot be the first call of a step — the adapter needs an object
    /// first. Used to assert that this is reported with its real reason instead of a silent
    /// no-op.</summary>
    public class InstanceOps
    {
        public double Triple(double a) { return a * 3.0; }
    }
}
