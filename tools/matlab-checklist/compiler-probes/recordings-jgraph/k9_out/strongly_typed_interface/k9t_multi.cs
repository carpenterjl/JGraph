/* File: k9t_multi.cs
*
* JGraph Strongly Typed Interface Version: 0.2.3 (R2025b)
* C# source code generated on: <DATE>
*/
using System;
using JGraph.MATLAB.Types;
using JGraph.MATLAB.Exceptions;
namespace K9{

    public static partial class MATLABFunctions { 
    public static void k9t_multi(MATLABProvider _matlab, double [] values){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9t_multi(new RunOptions(nargout:0),values);
    }
    public static void k9t_multi(MATLABProvider _matlab, double [] values,  out double total){
        dynamic _dynMatlab = _matlab;
        total = (double)_dynMatlab.k9t_multi(new RunOptions(nargout:1),values);
    }
    public static void k9t_multi(MATLABProvider _matlab, double [] values,  out double total,  out int count){
        dynamic _dynMatlab = _matlab;
        (total,count) = ((double,int))_dynMatlab.k9t_multi(new RunOptions(nargout:2),values);
    }
    public static void k9t_multi(MATLABProvider _matlab, double [] values,  out double total,  out int count,  out string label){
        dynamic _dynMatlab = _matlab;
        (total,count,label) = ((double,int,string))_dynMatlab.k9t_multi(new RunOptions(nargout:3),values);
    }
    }
}
