/* File: k9t_opt.cs
*
* MATLAB Strongly Typed Interface Version: R2025b
* C# source code generated on: <DATE>
*/
using System;
using MathWorks.MATLAB.Types;
using MathWorks.MATLAB.Exceptions;
namespace K9{

    public static partial class MATLABFunctions { 
    public static void k9t_opt(MATLABProvider _matlab, double a, double b, string c){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9t_opt(new RunOptions(nargout:0),a,b,c);
    }
    public static void k9t_opt(MATLABProvider _matlab, double a, double b, string c,  out dynamic r){
        dynamic _dynMatlab = _matlab;
        r = (MATLABArray)_dynMatlab.k9t_opt(new RunOptions(nargout:1),a,b,c);
    }
    public static void k9t_opt(MATLABProvider _matlab, double a, double b){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9t_opt(new RunOptions(nargout:0),a,b);
    }
    public static void k9t_opt(MATLABProvider _matlab, double a, double b,  out dynamic r){
        dynamic _dynMatlab = _matlab;
        r = (MATLABArray)_dynMatlab.k9t_opt(new RunOptions(nargout:1),a,b);
    }
    public static void k9t_opt(MATLABProvider _matlab, double a){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9t_opt(new RunOptions(nargout:0),a);
    }
    public static void k9t_opt(MATLABProvider _matlab, double a,  out dynamic r){
        dynamic _dynMatlab = _matlab;
        r = (MATLABArray)_dynMatlab.k9t_opt(new RunOptions(nargout:1),a);
    }
    }
}
