// ix_compile_helper.cs -- the C# file jgnet_compile compiles by name (interop plan, stage 6, ADR 0179).
// It sits in fixtures/helpers/, which the harness puts on the function path, so
// jgraph.net.compile("ix_compile_helper.cs") finds it as a script file is found. The test project does
// not compile it (JGraph.Tests.csproj removes fixtures\**\*.cs from Compile).
namespace IxCompile
{
    public static class FileHelper
    {
        public static double Hypot(double a, double b) => System.Math.Sqrt(a * a + b * b);

        public static string Greet(string name) => "hello " + name;
    }
}
