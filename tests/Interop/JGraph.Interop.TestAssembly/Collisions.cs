// Names that collide with MATLAB builtins, for the name-resolution probes.

namespace JGTest
{
    public static class Sum
    {
        public static double Of(double[] values)
        {
            double s = 0;
            foreach (double v in values) s += v;
            return s;
        }
    }

    public class plot
    {
        public string Kind => "JGTest.plot";
    }
}

namespace JGTest.max
{
    public class Thing
    {
        public string Where() => "JGTest.max.Thing";
    }
}
