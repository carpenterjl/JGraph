/* File: k9multi.cs
*
* JGraph Strongly Typed Interface Version: 0.2.3 (R2025b)
* C# source code generated on: <DATE>
*/
using System;
using JGraph.MATLAB.Types;
using JGraph.MATLAB.Exceptions;
namespace K9{

    public static partial class MATLABFunctions { 
    public static void k9multi(MATLABProvider _matlab, dynamic x){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9multi(new RunOptions(nargout:0),x);
    }
    public static void k9multi(MATLABProvider _matlab, dynamic x,  out dynamic a){
        dynamic _dynMatlab = _matlab;
        a = (MATLABArray)_dynMatlab.k9multi(new RunOptions(nargout:1),x);
    }
    public static void k9multi(MATLABProvider _matlab, dynamic x,  out dynamic a,  out dynamic b){
        dynamic _dynMatlab = _matlab;
        (a,b) = ((MATLABArray,MATLABArray))_dynMatlab.k9multi(new RunOptions(nargout:2),x);
    }
    public static void k9multi(MATLABProvider _matlab, dynamic x,  out dynamic a,  out dynamic b,  out dynamic c){
        dynamic _dynMatlab = _matlab;
        (a,b,c) = ((MATLABArray,MATLABArray,MATLABArray))_dynMatlab.k9multi(new RunOptions(nargout:3),x);
    }
    }
}
