/* File: k9t_cplx.cs
*
* MATLAB Strongly Typed Interface Version: R2025b
* C# source code generated on: <DATE>
*/
using System;
using MathWorks.MATLAB.Types;
using MathWorks.MATLAB.Exceptions;
namespace K9{

    public static partial class MATLABFunctions { 
    public static void k9t_cplx(MATLABProvider _matlab, System.Numerics.Complex a, System.Numerics.Complex [] b, dynamic c){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9t_cplx(new RunOptions(nargout:0),a,b,c);
    }
    public static void k9t_cplx(MATLABProvider _matlab, System.Numerics.Complex a, System.Numerics.Complex [] b, dynamic c,  out dynamic r){
        dynamic _dynMatlab = _matlab;
        r = (MATLABArray)_dynMatlab.k9t_cplx(new RunOptions(nargout:1),a,b,c);
    }
    }
}
