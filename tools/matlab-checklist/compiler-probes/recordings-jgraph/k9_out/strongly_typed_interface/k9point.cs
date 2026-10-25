/* File: k9point.cs
*
* JGraph Strongly Typed Interface Version: 0.2.3 (R2025b)
* C# source code generated on: <DATE>
*/
using System;
using JGraph.MATLAB.Types;
using JGraph.MATLAB.Exceptions;
namespace K9{
[MATLABClass("k9point")]
public  struct k9point:IEquatable<k9point> { 
    private void ThrowIfDefaultValue()
    {
        if (_matlab == null || _objrep == null)
        {
            throw new UnsupportedTypeException("k9point object is not initialized.");
        }
    }
    private dynamic _objrep;
    private dynamic _matlab;
    public  k9point(MATLABProvider _matlab, dynamic x, dynamic y){
        this._matlab = _matlab;
        _objrep = (MATLABArray)this._matlab.k9point(new RunOptions(nargout:1),x,y);
    }
    public System.Numerics.Complex X {
        get => _matlab.matlab.@internal.engine.getProperty(_objrep, "X");
        set => _objrep = _matlab.matlab.@internal.engine.setProperty(_objrep,"X",value);
    }
    public System.Numerics.Complex Y {
        get => _matlab.matlab.@internal.engine.getProperty(_objrep, "Y");
        set => _objrep = _matlab.matlab.@internal.engine.setProperty(_objrep,"Y",value);
    }
    public string Tag {
        get => _matlab.matlab.@internal.engine.getProperty(_objrep, "Tag");
    }
    public void parts(){
        _objrep.parts(new RunOptions(nargout:0));
    }
    public void parts( out dynamic a){
        a = (MATLABArray)_objrep.parts(new RunOptions(nargout:1));
    }
    public void parts( out dynamic a,  out dynamic b){
        (a,b) = ((MATLABArray,MATLABArray))_objrep.parts(new RunOptions(nargout:2));
    }
    public void scaled(double k){
        _objrep.scaled(new RunOptions(nargout:0),k);
    }
    public void scaled(double k,  out dynamic p){
        p = (MATLABArray)_objrep.scaled(new RunOptions(nargout:1),k);
    }
    public void norm(){
        _objrep.norm(new RunOptions(nargout:0));
    }
    public void norm( out dynamic n){
        n = (MATLABArray)_objrep.norm(new RunOptions(nargout:1));
    }
    public static void origin(MATLABProvider _matlab){
        dynamic _dynMatlab = _matlab;
        _dynMatlab.k9point.origin(new RunOptions(nargout:0));
    }
    public static void origin(MATLABProvider _matlab,  out dynamic p){
        dynamic _dynMatlab = _matlab;
        p = (MATLABArray)_dynMatlab.k9point.origin(new RunOptions(nargout:1));
    }
    public static bool operator == (k9point obj1, k9point obj2){
        bool ret = obj1._matlab.eq(obj1,obj2);
        return ret;
    }

    public static bool operator != (k9point obj1, k9point obj2){
        bool ret = obj1._matlab.ne(obj1,obj2);
        return ret;
    }

    public override bool Equals (Object obj){
        if (obj == null){
            return false;
        }

        try{

            k9point _obj = (k9point)obj;
            return (this == _obj);
        }

        catch(Exception){

            return false;
        }

    }

    public bool Equals(k9point obj){
        return this == obj;
    }

    public override int GetHashCode(){
        UInt64 hashcode = _matlab.keyHash(_objrep);
        return hashcode.GetHashCode();
    }

    public static implicit operator MATLABObject(k9point k9point){
        k9point.ThrowIfDefaultValue();
        return k9point._objrep;
    }
    public static implicit operator k9point(MATLABObject _obj){
        k9point k9point = default;
        k9point._objrep = _obj;
#pragma warning disable CS8601, CS8602
        k9point._matlab = typeof(MATLABObject).GetField("Matlab", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(_obj);
#pragma warning restore
        return k9point;
    }
}
}
