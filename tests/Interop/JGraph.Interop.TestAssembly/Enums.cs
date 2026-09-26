using System;

namespace JGTest
{
    public enum Color { Red, Green, Blue }

    [Flags]
    public enum Access { None = 0, Read = 1, Write = 2, Execute = 4, All = Read | Write | Execute }

    public enum Small : byte { Low = 1, High = 250 }

    public enum Big : long { Negative = -5, Large = 4000000000L }

    /// <summary>High has bit 63 set, so it is past 2^53 and past Int64.</summary>
    public enum Huge : ulong { One = 1, High = 0x8000000000000001UL }

    public static class EnumTools
    {
        public static Color Next(Color c) => (Color)(((int)c + 1) % 3);
        public static string Name(Color c) => c.ToString();
        public static bool CanWrite(Access a) => (a & Access.Write) != 0;
        public static Access Combine(Access a, Access b) => a | b;
        public static Small SmallHigh() => Small.High;
        public static Big BigLarge() => Big.Large;
        public static Huge HugeHigh() => Huge.High;
        public static ulong HugeValue(Huge h) => (ulong)h;
    }
}
