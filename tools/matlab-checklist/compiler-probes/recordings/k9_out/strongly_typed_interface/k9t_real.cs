/* File: k9t_real.cs
*
* MATLAB Strongly Typed Interface Version: R2025b
* C# source code generated on: <DATE>
*/
using System;
using MathWorks.MATLAB.Types;
using MathWorks.MATLAB.Exceptions;
namespace K9{

    public static partial class MATLABFunctions { 
    public static void k9t_real(MATLABProvider _matlab, double a, double [] b, double [,] c, double [,,] d){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9t_real(new RunOptions(nargout:0),a,b,c,d);
    }
    public static void k9t_real(MATLABProvider _matlab, double a, double [] b, double [,] c, double [,,] d,  out dynamic r){
        dynamic _dynMatlab = _matlab;
        r = (MATLABArray)_dynMatlab.k9t_real(new RunOptions(nargout:1),a,b,c,d);
    }
    }
}
