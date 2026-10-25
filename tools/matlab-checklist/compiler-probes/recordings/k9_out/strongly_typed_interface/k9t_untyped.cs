/* File: k9t_untyped.cs
*
* MATLAB Strongly Typed Interface Version: R2025b
* C# source code generated on: <DATE>
*/
using System;
using MathWorks.MATLAB.Types;
using MathWorks.MATLAB.Exceptions;
namespace K9{

    public static partial class MATLABFunctions { 
    public static void k9t_untyped(MATLABProvider _matlab, dynamic a, dynamic b){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9t_untyped(new RunOptions(nargout:0),a,b);
    }
    public static void k9t_untyped(MATLABProvider _matlab, dynamic a, dynamic b,  out dynamic r){
        dynamic _dynMatlab = _matlab;
        r = (MATLABArray)_dynMatlab.k9t_untyped(new RunOptions(nargout:1),a,b);
    }
    }
}
