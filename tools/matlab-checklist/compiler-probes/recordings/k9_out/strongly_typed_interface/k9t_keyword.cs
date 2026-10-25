/* File: k9t_keyword.cs
*
* MATLAB Strongly Typed Interface Version: R2025b
* C# source code generated on: <DATE>
*/
using System;
using MathWorks.MATLAB.Types;
using MathWorks.MATLAB.Exceptions;
namespace K9{

    public static partial class MATLABFunctions { 
    public static void k9t_keyword(MATLABProvider _matlab, dynamic @int, dynamic @string, dynamic @base){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9t_keyword(new RunOptions(nargout:0),@int,@string,@base);
    }
    public static void k9t_keyword(MATLABProvider _matlab, dynamic @int, dynamic @string, dynamic @base,  out dynamic @out){
        dynamic _dynMatlab = _matlab;
        @out = (MATLABArray)_dynMatlab.k9t_keyword(new RunOptions(nargout:1),@int,@string,@base);
    }
    }
}
