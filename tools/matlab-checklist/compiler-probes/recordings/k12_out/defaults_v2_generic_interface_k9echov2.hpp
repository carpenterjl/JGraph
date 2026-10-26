/* File: <W>\b\defaults\k9echov2.hpp
*
* MATLAB Strongly Typed Interface Version: R2025b
* C++ source code generated on: <DATE>
*/
#include "MatlabTypesInterface.hpp"
#include <map>


template<size_t nargout>
struct return_type_k9echo { typedef void type; };


template<size_t nargout = 1>
typename return_type_k9echo<nargout>::type k9echo(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array in) {
    static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
}
template<>
struct return_type_k9echo<0> { typedef void type; };

template<>
struct return_type_k9echo<1> { typedef matlab::data::Array type; };

template<>
void k9echo<0>(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array in) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        in };
    _matlabPtr->feval(u"k9echo", 0, _args);
}

template<>
matlab::data::Array k9echo<1>(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array in) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        in };
    matlab::data::Array _result_mda = _matlabPtr->feval(u"k9echo", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}