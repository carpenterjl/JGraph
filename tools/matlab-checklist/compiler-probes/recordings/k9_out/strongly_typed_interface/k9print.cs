/* File: k9print.cs
*
* MATLAB Strongly Typed Interface Version: R2025b
* C# source code generated on: <DATE>
*/
using System;
using MathWorks.MATLAB.Types;
using MathWorks.MATLAB.Exceptions;
namespace K9{

    public static partial class MATLABFunctions { 
    public static void k9print(MATLABProvider _matlab, dynamic n){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9print(new RunOptions(nargout:0),n);
    }
    public static void k9print(MATLABProvider _matlab, ref dynamic n){
        dynamic _dynMatlab = _matlab;
        n = (MATLABArray)_dynMatlab.k9print(new RunOptions(nargout:1),n);
    }
    }
}
