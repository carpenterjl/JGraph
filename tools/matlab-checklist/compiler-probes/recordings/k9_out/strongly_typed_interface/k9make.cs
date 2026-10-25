/* File: k9make.cs
*
* MATLAB Strongly Typed Interface Version: R2025b
* C# source code generated on: <DATE>
*/
using System;
using MathWorks.MATLAB.Types;
using MathWorks.MATLAB.Exceptions;
namespace K9{

    public static partial class MATLABFunctions { 
    public static void k9make(MATLABProvider _matlab, dynamic kind){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9make(new RunOptions(nargout:0),kind);
    }
    public static void k9make(MATLABProvider _matlab, dynamic kind,  out dynamic v){
        dynamic _dynMatlab = _matlab;
        v = (MATLABArray)_dynMatlab.k9make(new RunOptions(nargout:1),kind);
    }
    }
}
