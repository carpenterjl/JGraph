/* File: k9nv.cs
*
* JGraph Strongly Typed Interface Version: 0.2.3 (R2025b)
* C# source code generated on: <DATE>
*/
using System;
using JGraph.MATLAB.Types;
using JGraph.MATLAB.Exceptions;
namespace K9{

    public static partial class MATLABFunctions { 
    public static void k9nv(MATLABProvider _matlab, System.Numerics.Complex x, System.Numerics.Complex Scale, string Label){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9nv(new RunOptions(nargout:0),x,"Scale",Scale,"Label",Label);
    }
    public static void k9nv(MATLABProvider _matlab, System.Numerics.Complex x, System.Numerics.Complex Scale, string Label,  out dynamic r){
        dynamic _dynMatlab = _matlab;
        r = (MATLABArray)_dynMatlab.k9nv(new RunOptions(nargout:1),x,"Scale",Scale,"Label",Label);
    }
    public static void k9nv(MATLABProvider _matlab, System.Numerics.Complex x, System.Numerics.Complex Scale){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9nv(new RunOptions(nargout:0),x,"Scale",Scale);
    }
    public static void k9nv(MATLABProvider _matlab, System.Numerics.Complex x, System.Numerics.Complex Scale,  out dynamic r){
        dynamic _dynMatlab = _matlab;
        r = (MATLABArray)_dynMatlab.k9nv(new RunOptions(nargout:1),x,"Scale",Scale);
    }
    public static void k9nv(MATLABProvider _matlab, System.Numerics.Complex x){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9nv(new RunOptions(nargout:0),x);
    }
    public static void k9nv(MATLABProvider _matlab, System.Numerics.Complex x,  out dynamic r){
        dynamic _dynMatlab = _matlab;
        r = (MATLABArray)_dynMatlab.k9nv(new RunOptions(nargout:1),x);
    }
    }
}
