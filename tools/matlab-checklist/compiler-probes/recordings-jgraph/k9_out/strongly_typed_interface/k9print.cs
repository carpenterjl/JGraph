/* File: k9print.cs
*
* JGraph Strongly Typed Interface Version: 0.2.3 (R2025b)
* C# source code generated on: <DATE>
*/
using System;
using JGraph.MATLAB.Types;
using JGraph.MATLAB.Exceptions;
namespace K9{

    public static partial class MATLABFunctions { 
    public static void k9print(MATLABProvider _matlab, dynamic n){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9print(new RunOptions(nargout:0),n);
    }
    public static void k9print(MATLABProvider _matlab, ref dynamic n){
        dynamic _dynMatlab = _matlab;
        n = (MATLABArray)_dynMatlab.k9print(new RunOptions(nargout:1),n);
    }
    }
}
