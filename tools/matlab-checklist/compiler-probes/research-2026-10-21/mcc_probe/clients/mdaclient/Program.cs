using System;
using System.Diagnostics;
using MathWorks.MATLAB.Runtime;
using MathWorks.MATLAB.Types;

var sw = Stopwatch.StartNew();
string ctf = args[0];
using (dynamic matlab = MATLABRuntime.StartMATLAB(ctf))
{
    Console.WriteLine($"StartMATLAB: {sw.ElapsedMilliseconds} ms");
    sw.Restart();
    // dynamic call, double in -> what comes back?
    object r1 = matlab.typedadd(1.0, 2.0);
    Console.WriteLine($"dynamic typedadd(1.0,2.0) -> {r1?.GetType().FullName} = {r1}  ({sw.ElapsedMilliseconds} ms)"); try { double d1 = (double)(dynamic)r1; Console.WriteLine("  cast to double = " + d1); } catch (Exception e) { Console.WriteLine("  cast failed: " + e.Message); } try { System.Numerics.Complex c1 = (System.Numerics.Complex)(dynamic)r1; Console.WriteLine("  cast to Complex = " + c1); } catch (Exception e) { Console.WriteLine("  Complex cast failed: " + e.Message); }
    double r2 = matlab.typedadd(3.0, 4.0); int ri = 0; try { ri = matlab.typedadd(1, 2); Console.WriteLine("int args, int result -> " + ri); } catch (Exception e) { Console.WriteLine("typedadd(int,int) as int: " + e.GetType().Name + ": " + e.Message.Trim()); }
    Console.WriteLine($"dynamic as double -> {r2}");
    // strongly typed generated wrapper
    JgProbeData.MATLABFunctions.typedadd(matlab, new System.Numerics.Complex(5, 0), new System.Numerics.Complex(6, 0), out dynamic r3);
    Console.WriteLine($"typed wrapper typedadd(5,6) -> {((object)r3)?.GetType().FullName}"); try { double d3 = (double)r3; Console.WriteLine("  cast to double = " + d3); } catch (Exception e) { Console.WriteLine("  cast failed: " + e.GetType().Name + ": " + e.Message); }
    try
    {
        object bad = matlab.typedadd(new double[] { 1, 2 }, 3.0);
        Console.WriteLine($"typedadd([1 2],3) -> {bad}");
    }
    catch (Exception e) { Console.WriteLine($"typedadd([1 2],3): {e.GetType().FullName}: {e.Message.Trim()}"); }
    try
    {
        object bad = matlab.typedadd("x", 3.0);
        Console.WriteLine($"typedadd(\"x\",3) -> {bad?.GetType().FullName} = {(double)(dynamic)bad}");
    }
    catch (Exception e) { Console.WriteLine($"typedadd(\"x\",3): {e.GetType().FullName}: {e.Message.Trim()}"); }
    try
    {
        object r4 = matlab.addtwo(new RunOptions(nargout: 1), 10.0, 20.0);
        Console.WriteLine($"addtwo (not in ctf) -> {r4}");
    }
    catch (Exception e) { Console.WriteLine($"addtwo not in this ctf: {e.GetType().FullName}: {e.Message.Trim()}"); }
}
Console.WriteLine($"total {sw.ElapsedMilliseconds} ms");

