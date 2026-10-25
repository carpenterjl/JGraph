// Stage K9's client: the strongly typed interface generated with the K9 archive (K9.dll), called as a
// client written against R2025b's generated classes would call it.
using System;
using System.Collections.Generic;
using System.Numerics;
using MathWorks.MATLAB.Types;

static class TypedCases
{
    public static void Run(object provider, Action<string, Func<object>> Case, Action<string, Action> Do, Action<string, string> Line)
    {
        dynamic matlab = provider;
        MATLABProvider m = (MATLABProvider)matlab;
        Case("typed.echo", () => { K9.MATLABFunctions.k9echo(m, 2.0, out dynamic o); return o; });
        Case("typed.echo.double", () => { K9.MATLABFunctions.k9echo(m, 2.0, out dynamic o); return (double)o; });
        Do("typed.echo.void", () => K9.MATLABFunctions.k9echo(m, 2.0));
        Case("typed.real", () => { K9.MATLABFunctions.k9t_real(m, 1.0, new double[] { 1, 2 }, new double[,] { { 1, 2 }, { 3, 4 } }, new double[2, 3, 4], out dynamic r); return (double)r; });
        Case("typed.real.badsize", () => { K9.MATLABFunctions.k9t_real(m, 1.0, new double[] { 1 }, new double[,] { { 1 } }, new double[3, 3, 3], out dynamic r); return (double)r; });
        Case("typed.real.emptyrow", () => { K9.MATLABFunctions.k9t_real(m, 1.0, new double[0], new double[0, 0], new double[2, 3, 4], out dynamic r); return (double)r; });
        Case("typed.cplx", () => { K9.MATLABFunctions.k9t_cplx(m, new Complex(1, 1), new Complex[] { 1, 2 }, 3.0f, out dynamic r); return r; });
        Case("typed.ints", () => { K9.MATLABFunctions.k9t_ints(m, (sbyte)1, new byte[] { 1, 2 }, (short)3, (ushort)4, 5, new uint[,] { { 6 } }, 7L, 8UL, 9, out dynamic r); return (double)r; });
        Case("typed.misc", () =>
        {
            var p = new K9.k9point(m, 1.0, 2.0);
            K9.MATLABFunctions.k9t_misc(m, true, new bool[] { true }, new char[] { 'a', 'b' }, 'c', "e", new string[] { "f" },
                new object[] { 1.0 }, new object[] { 2.0 }, new MATLABStruct(("a", 1.0)), new MATLABStruct[] { new MATLABStruct(("a", 1.0)) },
                K9.k9color.red, p, new K9.k9point[] { p, p }, new Dictionary<string, double> { ["a"] = 1 }, 1.0, 2.0, out dynamic r);
            return (double)r;
        });
        Case("typed.untyped", () => { K9.MATLABFunctions.k9t_untyped(m, 1.0, 2.0, out dynamic r); return (double)r; });
        Case("typed.opt.1", () => { K9.MATLABFunctions.k9t_opt(m, 1.0, out dynamic r); return (string)r; });
        Case("typed.opt.2", () => { K9.MATLABFunctions.k9t_opt(m, 1.0, 2.0, out dynamic r); return (string)r; });
        Case("typed.opt.3", () => { K9.MATLABFunctions.k9t_opt(m, 1.0, 2.0, "z", out dynamic r); return (string)r; });
        Case("typed.ref", () => { var x = new double[] { 1, 2 }; K9.MATLABFunctions.k9t_ref(m, x, 3.0, out dynamic x2, out dynamic y); return string.Join(",", (double[])x2) + "|" + (double)y + "|" + string.Join(",", x); });
        Case("typed.keyword", () => { K9.MATLABFunctions.k9t_keyword(m, 1.0, 2.0, 3.0, out dynamic o); return (double)o; });
        Case("typed.multi.1", () => { K9.MATLABFunctions.k9t_multi(m, new double[] { 1, 2, 3 }, out double total); return total; });
        Case("typed.multi.2", () => { K9.MATLABFunctions.k9t_multi(m, new double[] { 1, 2, 3 }, out double total, out int count); return total + "|" + count; });
        Case("typed.multi.3", () => { K9.MATLABFunctions.k9t_multi(m, new double[] { 1, 2, 3 }, out double total, out int count, out string label); return total + "|" + count + "|" + label; });
        Do("typed.multi.0", () => K9.MATLABFunctions.k9t_multi(m, new double[] { 1, 2, 3 }));
        Do("typed.none", () => K9.MATLABFunctions.k9t_none(m));
        Case("typed.objects.1", () => { K9.MATLABFunctions.k9t_objects(m, K9.k9color.blue, out K9.k9point p); return p.X; });
        Case("typed.objects.2", () => { K9.MATLABFunctions.k9t_objects(m, K9.k9color.blue, out K9.k9point p, out K9.k9color c); return p.Y + "|" + c; });
        Case("typed.nv.1", () => { K9.MATLABFunctions.k9nv(m, new Complex(2, 0), out dynamic r); return (string)r; });
        Case("typed.nv.2", () => { K9.MATLABFunctions.k9nv(m, new Complex(2, 0), new Complex(3, 0), out dynamic r); return (string)r; });
        Case("typed.nv.3", () => { K9.MATLABFunctions.k9nv(m, new Complex(2, 0), new Complex(3, 0), "x", out dynamic r); return (string)r; });
        Case("typed.pkg", () => { K9.k9pkg.MATLABFunctions.k9inner(m, 1.0, out dynamic r); return (double)r; });
        Case("typed.make", () => { K9.MATLABFunctions.k9make(m, "row", out dynamic r); return (double[])r; });
        Case("typed.describe", () => { K9.MATLABFunctions.k9describe(m, new double[] { 1 }, out dynamic d); return (string)d; });
        Case("typed.multi.dyn", () => { K9.MATLABFunctions.k9multi(m, 2.0, out dynamic a, out dynamic b, out dynamic c); return (double)a + "|" + (double)b + "|" + (string)c; });
        Case("typed.err", () => { K9.MATLABFunctions.k9err(m, "id", out dynamic r); return r; });
        Do("typed.noout", () => K9.MATLABFunctions.k9noout(m, 3.0));
        Case("typed.slow", () => { K9.MATLABFunctions.k9slow(m, 0.0, out dynamic t); return (double)t; });

        // The value class: a struct over the MATLAB object.
        Case("typed.point.new", () => new K9.k9point(m, 3.0, 4.0).X);
        Case("typed.point.Y", () => new K9.k9point(m, 3.0, 4.0).Y);
        Case("typed.point.Tag", () => new K9.k9point(m, 3.0, 4.0).Tag);
        Case("typed.point.norm", () => { new K9.k9point(m, 3.0, 4.0).norm(out dynamic n); return (double)n; });
        Do("typed.point.norm.void", () => new K9.k9point(m, 3.0, 4.0).norm());
        Case("typed.point.scaled", () => { new K9.k9point(m, 3.0, 4.0).scaled(2.0, out dynamic p); return (double)p.X; });
        Case("typed.point.parts", () => { new K9.k9point(m, 3.0, 4.0).parts(out dynamic a, out dynamic b); return (double)a + "|" + (double)b; });
        Case("typed.point.origin", () => { K9.k9point.origin(m, out dynamic p); return (double)p.Y; });
        Case("typed.point.set", () => { var p = new K9.k9point(m, 3.0, 4.0); p.X = 9; return p.X; });
        Case("typed.point.eq", () => new K9.k9point(m, 3.0, 4.0) == new K9.k9point(m, 3.0, 4.0));
        Case("typed.point.ne", () => new K9.k9point(m, 3.0, 4.0) != new K9.k9point(m, 1.0, 4.0));
        Case("typed.point.Equals", () => new K9.k9point(m, 3.0, 4.0).Equals(new K9.k9point(m, 3.0, 4.0)));
        Case("typed.point.Equals.object", () => new K9.k9point(m, 3.0, 4.0).Equals((object)"x"));
        Case("typed.point.hash", () => new K9.k9point(m, 3.0, 4.0).GetHashCode() == new K9.k9point(m, 3.0, 4.0).GetHashCode());
        Case("typed.point.toobject", () => { MATLABObject o = new K9.k9point(m, 3.0, 4.0); return (string)matlab.k9describe(o); });
        Case("typed.point.fromobject", () => { K9.k9point p = (MATLABObject)matlab.k9make("point"); return p.Y; });
        Case("typed.point.default", () => { MATLABObject o = default(K9.k9point); return o; });
        Case("typed.point.pass", () => (string)matlab.k9describe(new K9.k9point(m, 5.0, 6.0)));
        Case("typed.point.array.pass", () => (string)matlab.k9describe(new K9.k9point[] { new K9.k9point(m, 5.0, 6.0) }));
        Case("typed.point.cast", () => { K9.k9point p = (K9.k9point)matlab.k9make("point"); return p.X; });

        // The handle class.
        Case("typed.counter.new", () => new K9.k9counter(m, 1.0).Count);
        Case("typed.counter.increment", () => { var c = new K9.k9counter(m, 1.0); c.increment(2.0); return c.Count; });
        Case("typed.counter.current", () => { var c = new K9.k9counter(m, 1.0); c.current(out dynamic v); return (double)v; });
        Case("typed.counter.set", () => { var c = new K9.k9counter(m, 1.0); c.Count = 7; return c.Count; });
        Case("typed.counter.Label", () => new K9.k9counter(m, 1.0).Label);
        Case("typed.counter.Label.set", () => { var c = new K9.k9counter(m, 1.0); c.Label = "x"; return (string)c.Label; });
        Case("typed.counter.eq", () => { var c = new K9.k9counter(m, 1.0); c.eq(c, out dynamic tf); return (bool)tf; });
        Case("typed.counter.ne", () => { var c = new K9.k9counter(m, 1.0); c.ne(new K9.k9counter(m, 1.0), out dynamic tf); return (bool)tf; });
        Case("typed.counter.op.eq", () => { var c = new K9.k9counter(m, 1.0); return c == c; });
        Case("typed.counter.op.eq.other", () => new K9.k9counter(m, 1.0) == new K9.k9counter(m, 1.0));
        Case("typed.counter.op.lt", () => new K9.k9counter(m, 1.0) < new K9.k9counter(m, 2.0));
        Case("typed.counter.isvalid", () => { var c = new K9.k9counter(m, 1.0); c.isvalid(out dynamic v); return (bool)v; });
        Case("typed.counter.delete", () => { var c = new K9.k9counter(m, 1.0); c.delete(); c.isvalid(out dynamic v); return (bool)v; });
        Case("typed.counter.after.delete", () => { var c = new K9.k9counter(m, 1.0); c.delete(); return c.Count; });
        Case("typed.counter.Equals", () => { var c = new K9.k9counter(m, 1.0); return c.Equals(c); });
        Case("typed.counter.hash", () => { var c = new K9.k9counter(m, 1.0); return c.GetHashCode() == c.GetHashCode(); });
        Case("typed.counter.shared", () => { var c = new K9.k9counter(m, 1.0); MATLABObject o = c; K9.k9counter back = o; back.Count = 5; return c.Count; });
        Case("typed.counter.pass", () => (string)matlab.k9describe(new K9.k9counter(m, 4.0)));

        // The enumeration.
        Case("typed.color.pass", () => (string)matlab.k9describe(K9.k9color.green));
        Case("typed.color.out", () => (K9.k9color)matlab.k9make("enum"));
        Case("typed.color.attribute", () => ((MATLABEnumAttribute)Attribute.GetCustomAttribute(typeof(K9.k9color), typeof(MATLABEnumAttribute))).Name);
        Case("typed.point.attribute", () => ((MATLABClassAttribute)Attribute.GetCustomAttribute(typeof(K9.k9point), typeof(MATLABClassAttribute))).Name);
    }
}
