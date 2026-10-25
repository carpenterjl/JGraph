/* File: k9nv.cs
*
* MATLAB Strongly Typed Interface Version: R2025b
* C# source code generated on: <DATE>
*/
using System;
using MathWorks.MATLAB.Types;
using MathWorks.MATLAB.Exceptions;
namespace K9{

    public static partial class MATLABFunctions { 
    public static void k9nv(MATLABProvider _matlab, System.Numerics.Complex x, System.Numerics.Complex Scale, string Label){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9nv(new RunOptions(nargout:0),x,Scale,Label);
    }
    public static void k9nv(MATLABProvider _matlab, System.Numerics.Complex x, System.Numerics.Complex Scale, string Label,  out dynamic r){
        dynamic _dynMatlab = _matlab;
        r = (MATLABArray)_dynMatlab.k9nv(new RunOptions(nargout:1),x,Scale,Label);
    }
    public static void k9nv(MATLABProvider _matlab, System.Numerics.Complex x, System.Numerics.Complex Scale){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9nv(new RunOptions(nargout:0),x,Scale);
    }
    public static void k9nv(MATLABProvider _matlab, System.Numerics.Complex x, System.Numerics.Complex Scale,  out dynamic r){
        dynamic _dynMatlab = _matlab;
        r = (MATLABArray)_dynMatlab.k9nv(new RunOptions(nargout:1),x,Scale);
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
