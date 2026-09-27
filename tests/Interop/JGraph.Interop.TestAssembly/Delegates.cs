using System;
using System.Threading;
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

        /// <summary>
        /// Invokes f on a thread of its own and blocks until it answers: the deadlock case. A thread,
        /// not Task.Run: a waiting Task.Result may run a task not yet started inline on the waiting
        /// thread, which a busy thread pool makes likely, and then nothing crosses threads at all.
        /// </summary>
        public static double ApplyOnThread(Func<double, double> f, double x) => ApplyLater(f, x).Result;

        /// <summary>
        /// Invokes f on a thread of its own and returns at once; the task completes when f has answered
        /// (a TaskCompletionSource's task, which a waiter cannot run inline).
        /// </summary>
        public static Task<double> ApplyLater(Func<double, double> f, double x)
        {
            var done = new TaskCompletionSource<double>();
            var thread = new Thread(() =>
            {
                try
                {
                    done.SetResult(f(x));
                }
                catch (Exception fault)
                {
                    done.SetException(fault);
                }
            })
            { IsBackground = true };
            thread.Start();
            return done.Task;
        }

        public static Unary Doubler() => x => 2 * x;
    }
}
