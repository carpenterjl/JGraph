/* File: k9noout.cs
*
* JGraph Strongly Typed Interface Version: 0.2.3 (R2025b)
* C# source code generated on: <DATE>
*/
using System;
using JGraph.MATLAB.Types;
using JGraph.MATLAB.Exceptions;
namespace K9{

    public static partial class MATLABFunctions { 
    public static void k9noout(MATLABProvider _matlab, dynamic x){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9noout(new RunOptions(nargout:0),x);
    }
    }
}
