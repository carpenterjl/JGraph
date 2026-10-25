/* File: k9slow.cs
*
* JGraph Strongly Typed Interface Version: 0.2.3 (R2025b)
* C# source code generated on: <DATE>
*/
using System;
using JGraph.MATLAB.Types;
using JGraph.MATLAB.Exceptions;
namespace K9{

    public static partial class MATLABFunctions { 
    public static void k9slow(MATLABProvider _matlab, dynamic seconds){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9slow(new RunOptions(nargout:0),seconds);
    }
    public static void k9slow(MATLABProvider _matlab, dynamic seconds,  out dynamic t){
        dynamic _dynMatlab = _matlab;
        t = (MATLABArray)_dynMatlab.k9slow(new RunOptions(nargout:1),seconds);
    }
    }
}
