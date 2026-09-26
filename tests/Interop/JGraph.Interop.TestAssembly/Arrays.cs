namespace JGTest
{
    public static class ArrayMaker
    {
        public static double[] Ramp(int n)
        {
            var a = new double[n];
            for (int i = 0; i < n; i++) a[i] = i + 1;
            return a;
        }

        /// <summary>Element (r, c) is 10*r + c, zero-based.</summary>
        public static double[,] Grid(int rows, int cols)
        {
            var a = new double[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    a[r, c] = 10 * r + c;
            return a;
        }

        public static double[][] Jagged() => new[] { new double[] { 1 }, new double[] { 2, 3 }, new double[] { 4, 5, 6 } };
        public static int[] Ints() => new[] { 1, -2, 3 };
        public static bool[] Bools() => new[] { true, false, true };
        public static char[] Chars() => "abc".ToCharArray();
        public static byte[] Bytes() => new byte[] { 0, 127, 255 };
        public static string[] Words() => new[] { "alpha", "beta", "gamma" };
        public static object[] Mixed() => new object[] { 1.5, "two", 3, true, null };
        public static double[] Empty() => new double[0];

        public static double Sum(double[] a)
        {
            double s = 0;
            foreach (double v in a) s += v;
            return s;
        }
        public static double Sum2(double[,] a)
        {
            double s = 0;
            foreach (double v in a) s += v;
            return s;
        }
        public static string Shape(double[,] a) => a.GetLength(0) + "x" + a.GetLength(1);
        public static int Count(string[] words) => words.Length;
        public static string Kinds(object[] items)
        {
            var parts = new string[items.Length];
            for (int i = 0; i < items.Length; i++) parts[i] = items[i]?.GetType().FullName ?? "null";
            return string.Join(",", parts);
        }
    }
}
