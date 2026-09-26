using System.Collections.Generic;

namespace JGTest
{
    public class Box<T>
    {
        public Box() { }
        public Box(T value) { Value = value; }
        public T Value { get; set; }
        public string TypeName => typeof(T).FullName;
    }

    public class Pair<TFirst, TSecond>
    {
        public Pair(TFirst first, TSecond second) { First = first; Second = second; }
        public TFirst First { get; }
        public TSecond Second { get; }
    }

    public class Constrained<T> where T : struct
    {
        public T Value { get; set; }
    }

    public static class GenericTools
    {
        public static T Echo<T>(T value) => value;
        public static string TypeOf<T>() => typeof(T).FullName;
        public static List<double> Range(int n)
        {
            var list = new List<double>();
            for (int i = 0; i < n; i++) list.Add(i);
            return list;
        }
        public static Box<List<double>> NestedBox(int n) => new Box<List<double>>(Range(n));
        public static Dictionary<string, double> Table()
            => new Dictionary<string, double> { ["one"] = 1, ["two"] = 2 };
        public static double Total(IEnumerable<double> values)
        {
            double s = 0;
            foreach (double v in values) s += v;
            return s;
        }
    }
}
