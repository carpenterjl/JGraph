/* File: k9pkg_k9inner.cs
*
* JGraph Strongly Typed Interface Version: 0.2.3 (R2025b)
* C# source code generated on: <DATE>
*/
using System;
using JGraph.MATLAB.Types;
using JGraph.MATLAB.Exceptions;
namespace K9.k9pkg{

    public static partial class MATLABFunctions { 
    public static void k9inner(MATLABProvider _matlab, double x){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9pkg.k9inner(new RunOptions(nargout:0),x);
    }
    public static void k9inner(MATLABProvider _matlab, double x,  out dynamic r){
        dynamic _dynMatlab = _matlab;
        r = (MATLABArray)_dynMatlab.k9pkg.k9inner(new RunOptions(nargout:1),x);
    }
    }
}
