/* File: <W>\b\cpp\k12v\k12vv2.hpp
*
* MATLAB Strongly Typed Interface Version: R2025b
* C++ source code generated on: <DATE>
*/
#include "MatlabTypesInterface.hpp"
#include <map>


template<size_t nargout>
struct return_type_k9nargs { typedef void type; };


template<size_t nargout = 1>
typename return_type_k9nargs<nargout>::type k9nargs(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array varargin) {
    static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
}

template<size_t nargout = 1>
typename return_type_k9nargs<nargout>::type k9nargs(std::shared_ptr<MATLABControllerType> _matlabPtr, std::vector<matlab::data::Array> _args) {
    static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
}
template<>
struct return_type_k9nargs<0> { typedef void type; };

template<>
struct return_type_k9nargs<1> { typedef matlab::data::Array type; };


template<>
matlab::data::Array k9nargs<1>(std::shared_ptr<MATLABControllerType> _matlabPtr, std::vector<matlab::data::Array> _args) { 
    matlab::data::Array _result_mda = _matlabPtr->feval(u"k9nargs", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}




template<size_t nargout>
struct return_type_k9nout { typedef void type; };


template<size_t nargout = 1>
typename return_type_k9nout<nargout>::type k9nout(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array n) {
    static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
}
template<>
struct return_type_k9nout<0> { typedef void type; };

template<>
struct return_type_k9nout<1> { typedef matlab::data::Array type; };

template<>
void k9nout<0>(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array n) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        n };
    _matlabPtr->feval(u"k9nout", 0, _args);
}

template<>
matlab::data::Array k9nout<1>(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array n) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        n };
    matlab::data::Array _result_mda = _matlabPtr->feval(u"k9nout", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}




template<size_t nargout>
struct return_type_k9describe { typedef void type; };


template<size_t nargout = 1>
typename return_type_k9describe<nargout>::type k9describe(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array x) {
    static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
}
template<>
struct return_type_k9describe<0> { typedef void type; };

template<>
struct return_type_k9describe<1> { typedef matlab::data::Array type; };

template<>
void k9describe<0>(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array x) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        x };
    _matlabPtr->feval(u"k9describe", 0, _args);
}

template<>
matlab::data::Array k9describe<1>(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array x) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        x };
    matlab::data::Array _result_mda = _matlabPtr->feval(u"k9describe", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}