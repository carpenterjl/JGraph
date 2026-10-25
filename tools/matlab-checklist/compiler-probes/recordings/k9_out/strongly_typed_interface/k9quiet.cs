/* File: k9quiet.cs
*
* MATLAB Strongly Typed Interface Version: R2025b
* C# source code generated on: <DATE>
*/
using System;
using MathWorks.MATLAB.Types;
using MathWorks.MATLAB.Exceptions;
namespace K9{

    public static partial class MATLABFunctions { 
    public static void k9quiet(MATLABProvider _matlab){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9quiet(new RunOptions(nargout:0));
    }
    public static void k9quiet(MATLABProvider _matlab,  out dynamic old){
        dynamic _dynMatlab = _matlab;
        old = (MATLABArray)_dynMatlab.k9quiet(new RunOptions(nargout:1));
    }
    }
}
