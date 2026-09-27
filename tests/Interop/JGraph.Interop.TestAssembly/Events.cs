using System;
using System.Threading;

namespace JGTest
{
    public class CustomArgs : EventArgs
    {
        public CustomArgs(double value, string tag) { Value = value; Tag = tag; }
        public double Value { get; }
        public string Tag { get; }
    }

    /// <summary>A delegate that is not (object sender, EventArgs e).</summary>
    public delegate void NonStandardHandler(int code, string text);

    public class Publisher
    {
        public event EventHandler Fired;
        public event EventHandler<CustomArgs> Custom;
        public event NonStandardHandler NonStandard;
        public static event EventHandler StaticFired;

        public string Name { get; set; } = "pub";

        public int FiredHandlerCount => Fired?.GetInvocationList().Length ?? 0;

        public void RaiseSync() => Fired?.Invoke(this, EventArgs.Empty);
        public void RaiseCustom(double value, string tag) => Custom?.Invoke(this, new CustomArgs(value, tag));
        public void RaiseNonStandard(int code, string text) => NonStandard?.Invoke(code, text);
        public static void RaiseStatic() => StaticFired?.Invoke(null, EventArgs.Empty);

        /// <summary>
        /// Raises Custom count times from another thread, delayMs apart, and returns at once. A thread
        /// of its own rather than the pool's, so a busy pool (a parallel test run) cannot hold the
        /// events back past the point a script waits for them.
        /// </summary>
        public void RaiseOnThreadPool(int count, int delayMs)
        {
            var thread = new Thread(() =>
            {
                for (int i = 0; i < count; i++)
                {
                    Thread.Sleep(delayMs);
                    Custom?.Invoke(this, new CustomArgs(i + 1, "pool"));
                }
            })
            { IsBackground = true };
            thread.Start();
        }
    }
}
