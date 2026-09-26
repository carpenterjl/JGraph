namespace JGTest
{
    /// <summary>Fields, properties, an indexer, constructors and plain methods.</summary>
    public class Members
    {
        public double Field = 1.5;
        public static double StaticField = 2.5;
        public const int Constant = 42;
        public readonly string ReadOnlyField = "fixed";

        private readonly double[] _items = { 10, 20, 30 };

        public Members() { }
        public Members(double value) { Value = value; }
        public Members(double value, string name) { Value = value; Name = name; }
        public Members(params int[] parts)
        {
            foreach (int p in parts) Value += p;
            Name = "params:" + parts.Length;
        }

        public double Value { get; set; }
        public string Name { get; set; } = "none";
        public double ReadOnly => Value * 2;
        public double WriteOnly { set => Value = value; }
        public static int Counter { get; set; }
        public static string StaticReadOnly => "static";

        /// <summary>A default indexer, which .NET names Item.</summary>
        public double this[int index]
        {
            get => _items[index];
            set => _items[index] = value;
        }

        public string Describe() => $"Members({Value}, {Name})";
        public void Bump() { Value += 1; }
        public double Add(double x) => Value + x;
        public double Add(double x, double y) => Value + x + y;
        public static int Increment() => ++Counter;
        public static void Reset() { Counter = 0; StaticField = 2.5; }
        public override string ToString() => Describe();
    }

    /// <summary>A constructor with an optional parameter.</summary>
    public class OptionalCtor
    {
        public OptionalCtor(int a, int b = 5) { A = a; B = b; }
        public int A { get; }
        public int B { get; }
    }

    /// <summary>A class with no public constructor, reached only through a static factory.</summary>
    public sealed class Factory
    {
        private Factory(string tag) { Tag = tag; }
        public string Tag { get; }
        public static Factory Make(string tag) => new Factory(tag);
    }

    /// <summary>Static members only.</summary>
    public static class Statics
    {
        public static string Version => "jgtest-1";
        public static double Pi = 3.14159;
        public static double Twice(double x) => 2 * x;
        public static int Twice(int x) => 2 * x;
        public static string Hello() => "hello";
        public static void Nothing() { }
    }
}
