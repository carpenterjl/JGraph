/* File: k9echo.cs
*
* MATLAB Strongly Typed Interface Version: R2025b
* C# source code generated on: <DATE>
*/
using System;
using MathWorks.MATLAB.Types;
using MathWorks.MATLAB.Exceptions;
namespace K9{

    public static partial class MATLABFunctions { 
    public static void k9echo(MATLABProvider _matlab, dynamic @in){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9echo(new RunOptions(nargout:0),@in);
    }
    public static void k9echo(MATLABProvider _matlab, dynamic @in,  out dynamic @out){
        dynamic _dynMatlab = _matlab;
        @out = (MATLABArray)_dynMatlab.k9echo(new RunOptions(nargout:1),@in);
    }
    }
}
