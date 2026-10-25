/* File: k9counter.cs
*
* JGraph Strongly Typed Interface Version: 0.2.3 (R2025b)
* C# source code generated on: <DATE>
*/
using System;
using JGraph.MATLAB.Types;
using JGraph.MATLAB.Exceptions;
namespace K9{
[MATLABClass("k9counter")]
public  class k9counter:IEquatable<k9counter> { 
    private dynamic _objrep;
    private dynamic _matlab;
    public  k9counter(MATLABProvider _matlab, dynamic start){
        this._matlab = _matlab;
        _objrep = (MATLABArray)this._matlab.k9counter(new RunOptions(nargout:1),start);
    }
    private k9counter (){}

    public System.Numerics.Complex Count {
        get => _matlab.matlab.@internal.engine.getProperty(_objrep, "Count");
        set => _objrep = _matlab.matlab.@internal.engine.setProperty(_objrep,"Count",value);
    }
    public dynamic Label {
        get => _matlab.matlab.@internal.engine.getProperty(_objrep, "Label");
        set => _objrep = _matlab.matlab.@internal.engine.setProperty(_objrep,"Label",value);
    }
    public void current(){
        _objrep.current(new RunOptions(nargout:0));
    }
    public void current( out dynamic c){
        c = (MATLABArray)_objrep.current(new RunOptions(nargout:1));
    }
    public void increment(double @by){
        _objrep.increment(new RunOptions(nargout:0),@by);
    }
    public void eq(dynamic B){
        _objrep.eq(new RunOptions(nargout:0),B);
    }
    public void eq(dynamic B,  out dynamic TF){
        TF = (MATLABArray)_objrep.eq(new RunOptions(nargout:1),B);
    }
    public void ne(dynamic B){
        _objrep.ne(new RunOptions(nargout:0),B);
    }
    public void ne(dynamic B,  out dynamic TF){
        TF = (MATLABArray)_objrep.ne(new RunOptions(nargout:1),B);
    }
    public static bool operator < (k9counter obj1, k9counter obj2){
        bool ret = obj1._matlab.lt(obj1,obj2);
        return ret;
    }

    public static bool operator > (k9counter obj1, k9counter obj2){
        bool ret = obj1._matlab.gt(obj1,obj2);
        return ret;
    }

    public static bool operator <= (k9counter obj1, k9counter obj2){
        bool ret = obj1._matlab.le(obj1,obj2);
        return ret;
    }

    public static bool operator >= (k9counter obj1, k9counter obj2){
        bool ret = obj1._matlab.ge(obj1,obj2);
        return ret;
    }

    public void delete(){
        _objrep.delete(new RunOptions(nargout:0));
    }
    public void isvalid(){
        _objrep.isvalid(new RunOptions(nargout:0));
    }
    public void isvalid( out dynamic validity){
        validity = (MATLABArray)_objrep.isvalid(new RunOptions(nargout:1));
    }
    public static bool operator == (k9counter obj1, k9counter obj2){
        bool ret = obj1._matlab.eq(obj1,obj2);
        return ret;
    }

    public static bool operator != (k9counter obj1, k9counter obj2){
        bool ret = obj1._matlab.ne(obj1,obj2);
        return ret;
    }

    public override bool Equals (Object obj){
        if (obj == null){
            return false;
        }

        try{

            k9counter _obj = (k9counter)obj;
            return (this == _obj);
        }

        catch(Exception){

            return false;
        }

    }

    public bool Equals(k9counter obj){
        return this == obj;
    }

    public override int GetHashCode(){
        UInt64 hashcode = _matlab.keyHash(_objrep);
        return hashcode.GetHashCode();
    }

    public static implicit operator MATLABObject(k9counter k9counter){
        return k9counter._objrep;
    }
    public static implicit operator k9counter(MATLABObject _obj){
        k9counter k9counter = new k9counter();
        k9counter._objrep = _obj;
#pragma warning disable CS8601, CS8602
        k9counter._matlab = typeof(MATLABObject).GetField("Matlab", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(_obj);
#pragma warning restore
        return k9counter;
    }
}
}
