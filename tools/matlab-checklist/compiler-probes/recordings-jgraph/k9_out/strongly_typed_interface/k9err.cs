/* File: k9err.cs
*
* JGraph Strongly Typed Interface Version: 0.2.3 (R2025b)
* C# source code generated on: <DATE>
*/
using System;
using JGraph.MATLAB.Types;
using JGraph.MATLAB.Exceptions;
namespace K9{

    public static partial class MATLABFunctions { 
    public static void k9err(MATLABProvider _matlab, dynamic kind){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9err(new RunOptions(nargout:0),kind);
    }
    public static void k9err(MATLABProvider _matlab, dynamic kind,  out dynamic r){
        dynamic _dynMatlab = _matlab;
        r = (MATLABArray)_dynMatlab.k9err(new RunOptions(nargout:1),kind);
    }
    }
}
