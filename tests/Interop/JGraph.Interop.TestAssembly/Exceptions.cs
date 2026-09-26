using System;

namespace JGTest
{
    public class JGTestException : Exception
    {
        public JGTestException(string message, int code, Exception inner) : base(message, inner) { Code = code; }
        public int Code { get; }
    }

    /// <summary>Throws from every kind of member.</summary>
    public class Thrower
    {
        public Thrower() { }
        public Thrower(bool fail)
        {
            if (fail) throw new ArgumentException("constructor refused", nameof(fail));
        }

        public double Bad => throw new InvalidOperationException("getter refused");
        public double Settable { get => 0; set => throw new ArgumentOutOfRangeException(nameof(value), "setter refused"); }

        public void Throw(string message) => throw new InvalidOperationException(message);
        public static void ThrowStatic() => throw new NotSupportedException("static refused");
        public static void ThrowCustom()
        {
            try
            {
                throw new FormatException("inner cause");
            }
            catch (FormatException inner)
            {
                throw new JGTestException("outer failure", 17, inner);
            }
        }
        public static int DivideByZero(int x) => x / (x - x);
        public static void NullRef() { string s = null; _ = s.Length; }
    }
}
