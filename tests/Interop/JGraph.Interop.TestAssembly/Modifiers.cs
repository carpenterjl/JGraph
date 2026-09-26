namespace JGTest
{
    /// <summary>ref, out, params, optional and Nullable parameters, and void against value returns.</summary>
    public static class Modifiers
    {
        public static void Double(ref double x) { x *= 2; }
        public static void Split(double x, out double whole, out double frac)
        {
            whole = System.Math.Floor(x);
            frac = x - whole;
        }
        public static bool TryHalf(int x, out int half)
        {
            half = x / 2;
            return x % 2 == 0;
        }
        public static double RefOutReturn(ref double a, out string label)
        {
            a += 1;
            label = "a=" + a;
            return a * 10;
        }
        public static void Grow(ref double[] values)
        {
            var bigger = new double[values.Length + 1];
            values.CopyTo(bigger, 0);
            bigger[values.Length] = values.Length;
            values = bigger;
        }
        public static void MakeName(out string name) { name = "made"; }
        public static double Sum(params double[] values)
        {
            double s = 0;
            foreach (double v in values) s += v;
            return s;
        }
        public static string Join(string separator, params string[] parts) => string.Join(separator, parts);
        public static int Optional(int a, int b = 5, int c = 7) => a * 100 + b * 10 + c;
        public static string Nullable(int? x) => x.HasValue ? "has " + x.Value : "null";
        public static int? MaybeNull(bool give) => give ? 3 : (int?)null;
        public static void Void() { }
        public static double Value() => 1;
    }
}
