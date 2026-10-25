/* File: k9describe.cs
*
* JGraph Strongly Typed Interface Version: 0.2.3 (R2025b)
* C# source code generated on: <DATE>
*/
using System;
using JGraph.MATLAB.Types;
using JGraph.MATLAB.Exceptions;
namespace K9{

    public static partial class MATLABFunctions { 
    public static void k9describe(MATLABProvider _matlab, dynamic x){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9describe(new RunOptions(nargout:0),x);
    }
    public static void k9describe(MATLABProvider _matlab, dynamic x,  out dynamic d){
        dynamic _dynMatlab = _matlab;
        d = (MATLABArray)_dynMatlab.k9describe(new RunOptions(nargout:1),x);
    }
    }
}
