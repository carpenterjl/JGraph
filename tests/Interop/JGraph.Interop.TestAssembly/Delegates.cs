using System;
using System.Threading.Tasks;

namespace JGTest
{
    public delegate double Unary(double x);
    public delegate void RefOut(ref double a, out double b);
    public delegate string Describe(string name, int count);

    public static class Invoker
    {
        public static double Apply(Func<double, double> f, double x) => f(x);
        public static double ApplyUnary(Unary f, double x) => f(x);
        public static double Combine(Func<double, double, double> f, double a, double b) => f(a, b);
        public static void Run(Action a) => a();
        public static string Call(Describe d) => d("n", 3);

        public static double CallRefOut(RefOut d, double a)
        {
            d(ref a, out double b);
            return a * 1000 + b;
        }

        /// <summary>Invokes f on a thread-pool thread and blocks until it answers: the deadlock case.</summary>
        public static double ApplyOnThread(Func<double, double> f, double x) => Task.Run(() => f(x)).Result;

        public static Unary Doubler() => x => 2 * x;
    }
}
