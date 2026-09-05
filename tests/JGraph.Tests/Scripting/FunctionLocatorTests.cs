using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>Where each language's definitions are found, and what must not pass for one.</summary>
public class FunctionLocatorTests
{
    [Fact]
    public void Jgs_FindsAFnByName_AndNotItsCalls()
    {
        const string code = """
            let y = twice(2)
            fn twice(n) {
                return n * 2
            }
            fn twiceAgain(n) { return twice(twice(n)) }
            """;
        Assert.Equal(2, FunctionLocator.FindDefinition(code, "JGS", "twice"));
        Assert.Equal(5, FunctionLocator.FindDefinition(code, "JGS", "twiceAgain"));
        Assert.Null(FunctionLocator.FindDefinition(code, "JGS", "thrice"));
    }

    [Theory]
    [InlineData("function r = area(w, h)", true)]
    [InlineData("function [a, b] = area(w)", true)]
    [InlineData("function area(w) % draws it", true)]
    [InlineData("function area", true)]
    [InlineData("  function r=area(w)", true)]
    [InlineData("r = area(2, 3);", false)]
    [InlineData("function r = areas(w)", false)]
    [InlineData("% function r = area(w)", false)]
    public void Matlab_ReadsEveryFunctionLineShape(string line, bool defines)
    {
        int? found = FunctionLocator.FindDefinition("x = 1;\n" + line + "\nend", "MATLAB", "area");
        Assert.Equal(defines ? 2 : null, found);
    }

    [Fact]
    public void Python_FindsDefsAndClasses()
    {
        const string code = """
            import math

            class Shape:
                def area(self):
                    return 0

            async def fetch(url):
                pass

            print(area(3))
            """;
        Assert.Equal(3, FunctionLocator.FindDefinition(code, "Python", "Shape"));
        Assert.Equal(4, FunctionLocator.FindDefinition(code, "Python", "area"));
        Assert.Equal(7, FunctionLocator.FindDefinition(code, "Python", "fetch"));
        Assert.Null(FunctionLocator.FindDefinition(code, "Python", "print"));
    }

    [Fact]
    public void CSharp_FindsMethodsAndTypes_AndNotCalls()
    {
        const string code = """
            using System;

            public static class Shapes
            {
                public static double Area(double w, double h) => w * h;

                static List<int> Sizes<T>(IEnumerable<T> items) { return Area(1, 2); }
            }

            record Point(int X, int Y);
            var a = Area(2, 3);
            return Area(1, 1);
            """;
        Assert.Equal(3, FunctionLocator.FindDefinition(code, "C#", "Shapes"));
        Assert.Equal(5, FunctionLocator.FindDefinition(code, "C#", "Area"));
        Assert.Equal(7, FunctionLocator.FindDefinition(code, "C#", "Sizes"));
        Assert.Equal(10, FunctionLocator.FindDefinition(code, "C#", "Point"));
        Assert.Null(FunctionLocator.FindDefinition(code, "C#", "Console"));
    }

    [Fact]
    public void Text_AndNonIdentifiers_FindNothing()
    {
        Assert.Null(FunctionLocator.FindDefinition("fn twice(n) {}", "Text", "twice"));
        Assert.Null(FunctionLocator.FindDefinition("fn twice(n) {}", "JGS", "twi ce"));
        Assert.Null(FunctionLocator.FindDefinition("fn twice(n) {}", "JGS", ""));
    }

    [Theory]
    [InlineData(@"C:\work\helpers\area.m", "area", true)]
    [InlineData(@"C:\work\Area.M", "area", true)]
    [InlineData(@"C:\work\area.jgs", "area", true)]
    [InlineData(@"C:\work\area.py", "area", true)]
    [InlineData(@"C:\work\area.txt", "area", false)]
    [InlineData(@"C:\work\areas.m", "area", false)]
    public void AFileNamedForTheFunction_IsItsDefinition(string path, string name, bool matches) =>
        Assert.Equal(matches, FunctionLocator.FileNameMatches(path, name));
}
