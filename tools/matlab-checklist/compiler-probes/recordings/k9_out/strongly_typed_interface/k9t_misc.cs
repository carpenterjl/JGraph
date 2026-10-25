/* File: k9t_misc.cs
*
* MATLAB Strongly Typed Interface Version: R2025b
* C# source code generated on: <DATE>
*/
using System;
using MathWorks.MATLAB.Types;
using MathWorks.MATLAB.Exceptions;
namespace K9{

    public static partial class MATLABFunctions { 
    public static void k9t_misc(MATLABProvider _matlab, bool a, bool[] b, char [] c, char d, string e, string [] f, dynamic g, object [] h, MATLABStruct k, MATLABStruct[] m, k9color n, k9point p, k9point[] q, dynamic s, dynamic t, dynamic u){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9t_misc(new RunOptions(nargout:0),a,b,c,d,e,f,g,h,k,m,n,p,q,s,t,u);
    }
    public static void k9t_misc(MATLABProvider _matlab, bool a, bool[] b, char [] c, char d, string e, string [] f, dynamic g, object [] h, MATLABStruct k, MATLABStruct[] m, k9color n, k9point p, k9point[] q, dynamic s, dynamic t, dynamic u,  out dynamic r){
        dynamic _dynMatlab = _matlab;
        r = (MATLABArray)_dynMatlab.k9t_misc(new RunOptions(nargout:1),a,b,c,d,e,f,g,h,k,m,n,p,q,s,t,u);
    }
    }
}
