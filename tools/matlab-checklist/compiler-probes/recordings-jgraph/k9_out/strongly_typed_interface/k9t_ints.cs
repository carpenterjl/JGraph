/* File: k9t_ints.cs
*
* JGraph Strongly Typed Interface Version: 0.2.3 (R2025b)
* C# source code generated on: <DATE>
*/
using System;
using JGraph.MATLAB.Types;
using JGraph.MATLAB.Exceptions;
namespace K9{

    public static partial class MATLABFunctions { 
    public static void k9t_ints(MATLABProvider _matlab, sbyte a, byte [] b, Int16 c, UInt16 d, int e, uint [,] f, Int64 g, UInt64 h, dynamic i){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9t_ints(new RunOptions(nargout:0),a,b,c,d,e,f,g,h,i);
    }
    public static void k9t_ints(MATLABProvider _matlab, sbyte a, byte [] b, Int16 c, UInt16 d, int e, uint [,] f, Int64 g, UInt64 h, dynamic i,  out dynamic r){
        dynamic _dynMatlab = _matlab;
        r = (MATLABArray)_dynMatlab.k9t_ints(new RunOptions(nargout:1),a,b,c,d,e,f,g,h,i);
    }
    }
}
