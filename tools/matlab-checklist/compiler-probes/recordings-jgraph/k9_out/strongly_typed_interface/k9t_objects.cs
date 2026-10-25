/* File: k9t_objects.cs
*
* JGraph Strongly Typed Interface Version: 0.2.3 (R2025b)
* C# source code generated on: <DATE>
*/
using System;
using JGraph.MATLAB.Types;
using JGraph.MATLAB.Exceptions;
namespace K9{

    public static partial class MATLABFunctions { 
    public static void k9t_objects(MATLABProvider _matlab, k9color color){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9t_objects(new RunOptions(nargout:0),color);
    }
    public static void k9t_objects(MATLABProvider _matlab, k9color color,  out k9point p){
        dynamic _dynMatlab = _matlab;
        p = (k9point)_dynMatlab.k9t_objects(new RunOptions(nargout:1),color);
    }
    public static void k9t_objects(MATLABProvider _matlab, k9color color,  out k9point p,  out k9color c){
        dynamic _dynMatlab = _matlab;
        (p,c) = ((k9point,k9color))_dynMatlab.k9t_objects(new RunOptions(nargout:2),color);
    }
    }
}
