/* File: k9t_ref.cs
*
* JGraph Strongly Typed Interface Version: 0.2.3 (R2025b)
* C# source code generated on: <DATE>
*/
using System;
using JGraph.MATLAB.Types;
using JGraph.MATLAB.Exceptions;
namespace K9{

    public static partial class MATLABFunctions { 
    public static void k9t_ref(MATLABProvider _matlab, double [] x, double n){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9t_ref(new RunOptions(nargout:0),x,n);
    }
    public static void k9t_ref(MATLABProvider _matlab, double [] x, double n,  out dynamic _x){
        dynamic _dynMatlab = _matlab;
        _x = (MATLABArray)_dynMatlab.k9t_ref(new RunOptions(nargout:1),x,n);
    }
    public static void k9t_ref(MATLABProvider _matlab, double [] x, double n,  out dynamic _x,  out dynamic y){
        dynamic _dynMatlab = _matlab;
        (_x,y) = ((MATLABArray,MATLABArray))_dynMatlab.k9t_ref(new RunOptions(nargout:2),x,n);
    }
    }
}
