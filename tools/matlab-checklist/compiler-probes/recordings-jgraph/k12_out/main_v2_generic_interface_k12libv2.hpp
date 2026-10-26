/* File: <W>\b\cpp\k12lib\k12libv2.hpp
*
* JGraph Strongly Typed Interface Version: 0.2.3 (R2025b)
* C++ source code generated on: <DATE>
*/
#include "MatlabTypesInterface.hpp"
#include <map>

    class k9opt : public MATLABHandleObject<MATLABControllerType> { 
    public:

    friend bool operator==(const k9opt& A, const k9opt& B);
    friend bool operator!=(const k9opt& A, const k9opt& B);
    friend bool operator<(const k9opt& a, const k9opt& b);
    friend bool operator>(const k9opt& A, const k9opt& B);
    friend bool operator<=(const k9opt& A, const k9opt& B);
    friend bool operator>=(const k9opt& A, const k9opt& B);
        // constructors
        k9opt() : MATLABHandleObject() {}

        k9opt(std::shared_ptr<MATLABControllerType> matlabPtr) :
            MATLABHandleObject(matlabPtr, u"k9opt")
        {}
                k9opt(std::shared_ptr<MATLABControllerType> matlabPtr, matlab::data::Array obj) :
            MATLABHandleObject(matlabPtr, obj)
        {}

        // properties
        double getTotal() { return MATLABGetScalarProperty<double>(u"Total"); }
        void setTotal(double value) { return MATLABSetScalarProperty(u"Total", value); }
        matlab::data::Array getTwice() {
            matlab::data::Array obj = MATLABGetScalarProperty<matlab::data::Array>(u"Twice");
            return obj;
        }

        // methods
        private:
        template<size_t nargout>
        struct return_type_lt { typedef void type; };
        
        public:
        template<size_t nargout = 1>
        typename return_type_lt<nargout>::type lt(matlab::data::Array b) {
            static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
        }

        void bump(double by) { 
            matlab::data::ArrayFactory _arrayFactory;
            std::vector<matlab::data::Array> _args = {
                m_object,
                _arrayFactory.createArray<double>({1,1}, {by}) };
            m_matlabPtr->feval(u"bump", 0, _args);
        }

        private:
        template<size_t nargout>
        struct return_type_eq { typedef void type; };
        
        public:
        template<size_t nargout = 1>
        typename return_type_eq<nargout>::type eq(matlab::data::Array B) {
            static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
        }

        private:
        template<size_t nargout>
        struct return_type_ne { typedef void type; };
        
        public:
        template<size_t nargout = 1>
        typename return_type_ne<nargout>::type ne(matlab::data::Array B) {
            static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
        }

        private:
        template<size_t nargout>
        struct return_type_gt { typedef void type; };
        
        public:
        template<size_t nargout = 1>
        typename return_type_gt<nargout>::type gt(matlab::data::Array B) {
            static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
        }

        private:
        template<size_t nargout>
        struct return_type_le { typedef void type; };
        
        public:
        template<size_t nargout = 1>
        typename return_type_le<nargout>::type le(matlab::data::Array B) {
            static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
        }

        private:
        template<size_t nargout>
        struct return_type_ge { typedef void type; };
        
        public:
        template<size_t nargout = 1>
        typename return_type_ge<nargout>::type ge(matlab::data::Array B) {
            static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
        }

    };
template<>
struct k9opt::return_type_lt<0> { typedef void type; };

template<>
struct k9opt::return_type_lt<1> { typedef matlab::data::Array type; };

template<>
void k9opt::lt<0>(matlab::data::Array b) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        b };
    m_matlabPtr->feval(u"lt", 0, _args);
}

template<>
matlab::data::Array k9opt::lt<1>(matlab::data::Array b) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        b };
    matlab::data::Array _result_mda = m_matlabPtr->feval(u"lt", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}



template<>
struct k9opt::return_type_eq<0> { typedef void type; };

template<>
struct k9opt::return_type_eq<1> { typedef matlab::data::Array type; };

template<>
void k9opt::eq<0>(matlab::data::Array B) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        B };
    m_matlabPtr->feval(u"eq", 0, _args);
}

template<>
matlab::data::Array k9opt::eq<1>(matlab::data::Array B) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        B };
    matlab::data::Array _result_mda = m_matlabPtr->feval(u"eq", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}


template<>
struct k9opt::return_type_ne<0> { typedef void type; };

template<>
struct k9opt::return_type_ne<1> { typedef matlab::data::Array type; };

template<>
void k9opt::ne<0>(matlab::data::Array B) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        B };
    m_matlabPtr->feval(u"ne", 0, _args);
}

template<>
matlab::data::Array k9opt::ne<1>(matlab::data::Array B) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        B };
    matlab::data::Array _result_mda = m_matlabPtr->feval(u"ne", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}


template<>
struct k9opt::return_type_gt<0> { typedef void type; };

template<>
struct k9opt::return_type_gt<1> { typedef matlab::data::Array type; };

template<>
void k9opt::gt<0>(matlab::data::Array B) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        B };
    m_matlabPtr->feval(u"gt", 0, _args);
}

template<>
matlab::data::Array k9opt::gt<1>(matlab::data::Array B) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        B };
    matlab::data::Array _result_mda = m_matlabPtr->feval(u"gt", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}


template<>
struct k9opt::return_type_le<0> { typedef void type; };

template<>
struct k9opt::return_type_le<1> { typedef matlab::data::Array type; };

template<>
void k9opt::le<0>(matlab::data::Array B) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        B };
    m_matlabPtr->feval(u"le", 0, _args);
}

template<>
matlab::data::Array k9opt::le<1>(matlab::data::Array B) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        B };
    matlab::data::Array _result_mda = m_matlabPtr->feval(u"le", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}


template<>
struct k9opt::return_type_ge<0> { typedef void type; };

template<>
struct k9opt::return_type_ge<1> { typedef matlab::data::Array type; };

template<>
void k9opt::ge<0>(matlab::data::Array B) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        B };
    m_matlabPtr->feval(u"ge", 0, _args);
}

template<>
matlab::data::Array k9opt::ge<1>(matlab::data::Array B) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        B };
    matlab::data::Array _result_mda = m_matlabPtr->feval(u"ge", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}



bool operator==(const k9opt& A, const k9opt& B) {
    std::vector<matlab::data::Array> _args = { A.m_object, B.m_object };
    matlab::data::TypedArray<bool> _result_mda = A.m_matlabPtr->feval(u"eq", _args);
    bool _result = _result_mda[0];
    return _result;
}


bool operator!=(const k9opt& A, const k9opt& B) {
    std::vector<matlab::data::Array> _args = { A.m_object, B.m_object };
    matlab::data::TypedArray<bool> _result_mda = A.m_matlabPtr->feval(u"ne", _args);
    bool _result = _result_mda[0];
    return _result;
}


bool operator<(const k9opt& a, const k9opt& b) {
    std::vector<matlab::data::Array> _args = { a.m_object, b.m_object };
    matlab::data::TypedArray<bool> _result_mda = a.m_matlabPtr->feval(u"lt", _args);
    bool _result = _result_mda[0];
    return _result;
}


bool operator>(const k9opt& A, const k9opt& B) {
    std::vector<matlab::data::Array> _args = { A.m_object, B.m_object };
    matlab::data::TypedArray<bool> _result_mda = A.m_matlabPtr->feval(u"gt", _args);
    bool _result = _result_mda[0];
    return _result;
}


bool operator<=(const k9opt& A, const k9opt& B) {
    std::vector<matlab::data::Array> _args = { A.m_object, B.m_object };
    matlab::data::TypedArray<bool> _result_mda = A.m_matlabPtr->feval(u"le", _args);
    bool _result = _result_mda[0];
    return _result;
}


bool operator>=(const k9opt& A, const k9opt& B) {
    std::vector<matlab::data::Array> _args = { A.m_object, B.m_object };
    matlab::data::TypedArray<bool> _result_mda = A.m_matlabPtr->feval(u"ge", _args);
    bool _result = _result_mda[0];
    return _result;
}




    enum class k9color { 
        // enumerations
        red,
        green,
        blue
    };

namespace {
    std::string getk9colorString(const k9color& _enum) {
        static const std::map<k9color, std::string> map = { {k9color::red, "red"}, {k9color::green, "green"}, {k9color::blue, "blue"} };
        return (map.find(_enum))->second;
    }
    k9color getk9colorEnum(const std::string& _str) {
        static const std::map<std::string, k9color> map = { {"red", k9color::red}, {"green", k9color::green}, {"blue", k9color::blue} };
        return (map.find(_str))->second;
    }
}


    class k9counter : public MATLABHandleObject<MATLABControllerType> { 
    public:

    friend bool operator==(const k9counter& A, const k9counter& B);
    friend bool operator!=(const k9counter& A, const k9counter& B);
    friend bool operator<(const k9counter& A, const k9counter& B);
    friend bool operator>(const k9counter& A, const k9counter& B);
    friend bool operator<=(const k9counter& A, const k9counter& B);
    friend bool operator>=(const k9counter& A, const k9counter& B);
        // constructors
        k9counter() : MATLABHandleObject() {}

        k9counter(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array start) : 
            MATLABHandleObject()
        { 
            m_matlabPtr = _matlabPtr;
            matlab::data::ArrayFactory _arrayFactory;
            std::vector<matlab::data::Array> _args = {
                start };
            matlab::data::Array _result = m_matlabPtr->feval(u"k9counter", _args);
            m_object = _result;
        }


                // properties
        std::complex<double> getCount() { return MATLABGetComplexScalarProperty<double>(u"Count"); }
        void setCount(double value) { return MATLABSetScalarProperty(u"Count", value); }
        void setCount(std::complex<double> value) { return MATLABSetScalarProperty(u"Count", value); }
        matlab::data::Array getLabel() {
            matlab::data::Array obj = MATLABGetScalarProperty<matlab::data::Array>(u"Label");
            return obj;
        }
        void setLabel(matlab::data::Array obj) {
            MATLABSetScalarProperty(u"Label", obj);
        }

        // methods
        private:
        template<size_t nargout>
        struct return_type_current { typedef void type; };
        
        public:
        template<size_t nargout = 1>
        typename return_type_current<nargout>::type current() {
            static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
        }

        void increment(double by) { 
            matlab::data::ArrayFactory _arrayFactory;
            std::vector<matlab::data::Array> _args = {
                m_object,
                _arrayFactory.createArray<double>({1,1}, {by}) };
            m_matlabPtr->feval(u"increment", 0, _args);
        }

        private:
        template<size_t nargout>
        struct return_type_eq { typedef void type; };
        
        public:
        template<size_t nargout = 1>
        typename return_type_eq<nargout>::type eq(matlab::data::Array B) {
            static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
        }

        private:
        template<size_t nargout>
        struct return_type_ne { typedef void type; };
        
        public:
        template<size_t nargout = 1>
        typename return_type_ne<nargout>::type ne(matlab::data::Array B) {
            static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
        }

        private:
        template<size_t nargout>
        struct return_type_lt { typedef void type; };
        
        public:
        template<size_t nargout = 1>
        typename return_type_lt<nargout>::type lt(matlab::data::Array B) {
            static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
        }

        private:
        template<size_t nargout>
        struct return_type_gt { typedef void type; };
        
        public:
        template<size_t nargout = 1>
        typename return_type_gt<nargout>::type gt(matlab::data::Array B) {
            static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
        }

        private:
        template<size_t nargout>
        struct return_type_le { typedef void type; };
        
        public:
        template<size_t nargout = 1>
        typename return_type_le<nargout>::type le(matlab::data::Array B) {
            static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
        }

        private:
        template<size_t nargout>
        struct return_type_ge { typedef void type; };
        
        public:
        template<size_t nargout = 1>
        typename return_type_ge<nargout>::type ge(matlab::data::Array B) {
            static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
        }

    };
template<>
struct k9counter::return_type_current<0> { typedef void type; };

template<>
struct k9counter::return_type_current<1> { typedef matlab::data::Array type; };

template<>
void k9counter::current<0>() { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object };
    m_matlabPtr->feval(u"current", 0, _args);
}

template<>
matlab::data::Array k9counter::current<1>() { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object };
    matlab::data::Array _result_mda = m_matlabPtr->feval(u"current", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}



template<>
struct k9counter::return_type_eq<0> { typedef void type; };

template<>
struct k9counter::return_type_eq<1> { typedef matlab::data::Array type; };

template<>
void k9counter::eq<0>(matlab::data::Array B) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        B };
    m_matlabPtr->feval(u"eq", 0, _args);
}

template<>
matlab::data::Array k9counter::eq<1>(matlab::data::Array B) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        B };
    matlab::data::Array _result_mda = m_matlabPtr->feval(u"eq", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}


template<>
struct k9counter::return_type_ne<0> { typedef void type; };

template<>
struct k9counter::return_type_ne<1> { typedef matlab::data::Array type; };

template<>
void k9counter::ne<0>(matlab::data::Array B) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        B };
    m_matlabPtr->feval(u"ne", 0, _args);
}

template<>
matlab::data::Array k9counter::ne<1>(matlab::data::Array B) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        B };
    matlab::data::Array _result_mda = m_matlabPtr->feval(u"ne", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}


template<>
struct k9counter::return_type_lt<0> { typedef void type; };

template<>
struct k9counter::return_type_lt<1> { typedef matlab::data::Array type; };

template<>
void k9counter::lt<0>(matlab::data::Array B) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        B };
    m_matlabPtr->feval(u"lt", 0, _args);
}

template<>
matlab::data::Array k9counter::lt<1>(matlab::data::Array B) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        B };
    matlab::data::Array _result_mda = m_matlabPtr->feval(u"lt", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}


template<>
struct k9counter::return_type_gt<0> { typedef void type; };

template<>
struct k9counter::return_type_gt<1> { typedef matlab::data::Array type; };

template<>
void k9counter::gt<0>(matlab::data::Array B) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        B };
    m_matlabPtr->feval(u"gt", 0, _args);
}

template<>
matlab::data::Array k9counter::gt<1>(matlab::data::Array B) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        B };
    matlab::data::Array _result_mda = m_matlabPtr->feval(u"gt", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}


template<>
struct k9counter::return_type_le<0> { typedef void type; };

template<>
struct k9counter::return_type_le<1> { typedef matlab::data::Array type; };

template<>
void k9counter::le<0>(matlab::data::Array B) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        B };
    m_matlabPtr->feval(u"le", 0, _args);
}

template<>
matlab::data::Array k9counter::le<1>(matlab::data::Array B) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        B };
    matlab::data::Array _result_mda = m_matlabPtr->feval(u"le", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}


template<>
struct k9counter::return_type_ge<0> { typedef void type; };

template<>
struct k9counter::return_type_ge<1> { typedef matlab::data::Array type; };

template<>
void k9counter::ge<0>(matlab::data::Array B) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        B };
    m_matlabPtr->feval(u"ge", 0, _args);
}

template<>
matlab::data::Array k9counter::ge<1>(matlab::data::Array B) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        B };
    matlab::data::Array _result_mda = m_matlabPtr->feval(u"ge", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}



bool operator==(const k9counter& A, const k9counter& B) {
    std::vector<matlab::data::Array> _args = { A.m_object, B.m_object };
    matlab::data::TypedArray<bool> _result_mda = A.m_matlabPtr->feval(u"eq", _args);
    bool _result = _result_mda[0];
    return _result;
}


bool operator!=(const k9counter& A, const k9counter& B) {
    std::vector<matlab::data::Array> _args = { A.m_object, B.m_object };
    matlab::data::TypedArray<bool> _result_mda = A.m_matlabPtr->feval(u"ne", _args);
    bool _result = _result_mda[0];
    return _result;
}


bool operator<(const k9counter& A, const k9counter& B) {
    std::vector<matlab::data::Array> _args = { A.m_object, B.m_object };
    matlab::data::TypedArray<bool> _result_mda = A.m_matlabPtr->feval(u"lt", _args);
    bool _result = _result_mda[0];
    return _result;
}


bool operator>(const k9counter& A, const k9counter& B) {
    std::vector<matlab::data::Array> _args = { A.m_object, B.m_object };
    matlab::data::TypedArray<bool> _result_mda = A.m_matlabPtr->feval(u"gt", _args);
    bool _result = _result_mda[0];
    return _result;
}


bool operator<=(const k9counter& A, const k9counter& B) {
    std::vector<matlab::data::Array> _args = { A.m_object, B.m_object };
    matlab::data::TypedArray<bool> _result_mda = A.m_matlabPtr->feval(u"le", _args);
    bool _result = _result_mda[0];
    return _result;
}


bool operator>=(const k9counter& A, const k9counter& B) {
    std::vector<matlab::data::Array> _args = { A.m_object, B.m_object };
    matlab::data::TypedArray<bool> _result_mda = A.m_matlabPtr->feval(u"ge", _args);
    bool _result = _result_mda[0];
    return _result;
}




    class k9point : public MATLABObject<MATLABControllerType> { 
    public:

        // constructors
        k9point() : MATLABObject() {}

        k9point(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array x, matlab::data::Array y) : 
            MATLABObject()
        { 
            m_matlabPtr = _matlabPtr;
            matlab::data::ArrayFactory _arrayFactory;
            std::vector<matlab::data::Array> _args = {
                x,
                y };
            matlab::data::Array _result = m_matlabPtr->feval(u"k9point", _args);
            m_object = _result;
        }


                k9point(std::shared_ptr<MATLABControllerType> matlabPtr, matlab::data::Array obj) :
            MATLABObject(matlabPtr, obj)
        {}

        // properties
        std::complex<double> getX() { return MATLABGetComplexScalarProperty<double>(u"X"); }
        void setX(double value) { return MATLABSetScalarProperty(u"X", value); }
        void setX(std::complex<double> value) { return MATLABSetScalarProperty(u"X", value); }
        std::complex<double> getY() { return MATLABGetComplexScalarProperty<double>(u"Y"); }
        void setY(double value) { return MATLABSetScalarProperty(u"Y", value); }
        void setY(std::complex<double> value) { return MATLABSetScalarProperty(u"Y", value); }
        std::u16string getTag() { return MATLABGetScalarProperty<std::u16string>(u"Tag"); }

        // methods
        private:
        template<size_t nargout>
        struct return_type_parts { typedef void type; };
        
        public:
        template<size_t nargout = 1>
        typename return_type_parts<nargout>::type parts() {
            static_assert(nargout<=2, "Too many outputs specified. Maximum outputs is 2.");
        }

        private:
        template<size_t nargout>
        struct return_type_scaled { typedef void type; };
        
        public:
        template<size_t nargout = 1>
        typename return_type_scaled<nargout>::type scaled(double k) {
            static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
        }

        private:
        template<size_t nargout>
        struct return_type_norm { typedef void type; };
        
        public:
        template<size_t nargout = 1>
        typename return_type_norm<nargout>::type norm() {
            static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
        }

    };
template<>
struct k9point::return_type_parts<0> { typedef void type; };

template<>
struct k9point::return_type_parts<1> { typedef matlab::data::Array type; };

template<>
struct k9point::return_type_parts<2> { typedef std::tuple<matlab::data::Array, matlab::data::Array> type; };

template<>
void k9point::parts<0>() { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object };
    m_matlabPtr->feval(u"parts", 0, _args);
}

template<>
matlab::data::Array k9point::parts<1>() { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object };
    matlab::data::Array _result_mda = m_matlabPtr->feval(u"parts", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}

template<>
std::tuple<matlab::data::Array, matlab::data::Array> k9point::parts<2>() { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object };
    std::vector<matlab::data::Array> _result_mda = m_matlabPtr->feval(u"parts",2, _args);
    std::tuple<matlab::data::Array, matlab::data::Array> _result;
    std::get<0>(_result) = _result_mda.at(0);
    std::get<1>(_result) = _result_mda.at(1);
    return _result;
}


template<>
struct k9point::return_type_scaled<0> { typedef void type; };

template<>
struct k9point::return_type_scaled<1> { typedef matlab::data::Array type; };

template<>
void k9point::scaled<0>(double k) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        _arrayFactory.createArray<double>({1,1}, {k}) };
    m_matlabPtr->feval(u"scaled", 0, _args);
}

template<>
matlab::data::Array k9point::scaled<1>(double k) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object,
        _arrayFactory.createArray<double>({1,1}, {k}) };
    matlab::data::Array _result_mda = m_matlabPtr->feval(u"scaled", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}


template<>
struct k9point::return_type_norm<0> { typedef void type; };

template<>
struct k9point::return_type_norm<1> { typedef matlab::data::Array type; };

template<>
void k9point::norm<0>() { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object };
    m_matlabPtr->feval(u"norm", 0, _args);
}

template<>
matlab::data::Array k9point::norm<1>() { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        m_object };
    matlab::data::Array _result_mda = m_matlabPtr->feval(u"norm", _args);
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




template<size_t nargout>
struct return_type_k9make { typedef void type; };


template<size_t nargout = 1>
typename return_type_k9make<nargout>::type k9make(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array kind) {
    static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
}
template<>
struct return_type_k9make<0> { typedef void type; };

template<>
struct return_type_k9make<1> { typedef matlab::data::Array type; };

template<>
void k9make<0>(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array kind) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        kind };
    _matlabPtr->feval(u"k9make", 0, _args);
}

template<>
matlab::data::Array k9make<1>(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array kind) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        kind };
    matlab::data::Array _result_mda = _matlabPtr->feval(u"k9make", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}




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




template<size_t nargout>
struct return_type_k9multi { typedef void type; };


template<size_t nargout = 1>
typename return_type_k9multi<nargout>::type k9multi(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array x) {
    static_assert(nargout<=3, "Too many outputs specified. Maximum outputs is 3.");
}
template<>
struct return_type_k9multi<0> { typedef void type; };

template<>
struct return_type_k9multi<1> { typedef matlab::data::Array type; };

template<>
struct return_type_k9multi<2> { typedef std::tuple<matlab::data::Array, matlab::data::Array> type; };

template<>
struct return_type_k9multi<3> { typedef std::tuple<matlab::data::Array, matlab::data::Array, matlab::data::Array> type; };

template<>
void k9multi<0>(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array x) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        x };
    _matlabPtr->feval(u"k9multi", 0, _args);
}

template<>
matlab::data::Array k9multi<1>(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array x) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        x };
    matlab::data::Array _result_mda = _matlabPtr->feval(u"k9multi", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}

template<>
std::tuple<matlab::data::Array, matlab::data::Array> k9multi<2>(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array x) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        x };
    std::vector<matlab::data::Array> _result_mda = _matlabPtr->feval(u"k9multi",2, _args);
    std::tuple<matlab::data::Array, matlab::data::Array> _result;
    std::get<0>(_result) = _result_mda.at(0);
    std::get<1>(_result) = _result_mda.at(1);
    return _result;
}

template<>
std::tuple<matlab::data::Array, matlab::data::Array, matlab::data::Array> k9multi<3>(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array x) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        x };
    std::vector<matlab::data::Array> _result_mda = _matlabPtr->feval(u"k9multi",3, _args);
    std::tuple<matlab::data::Array, matlab::data::Array, matlab::data::Array> _result;
    std::get<0>(_result) = _result_mda.at(0);
    std::get<1>(_result) = _result_mda.at(1);
    std::get<2>(_result) = _result_mda.at(2);
    return _result;
}




template<size_t nargout>
struct return_type_k9nv { typedef void type; };


template<size_t nargout = 1>
typename return_type_k9nv<nargout>::type k9nv(std::shared_ptr<MATLABControllerType> _matlabPtr, std::complex<double> x, std::complex<double> Scale, std::u16string Label) {
    static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
}

template<size_t nargout = 1>
typename return_type_k9nv<nargout>::type k9nv(std::shared_ptr<MATLABControllerType> _matlabPtr, double x, double Scale, std::u16string Label) {
    static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
}
template<>
struct return_type_k9nv<0> { typedef void type; };

template<>
struct return_type_k9nv<1> { typedef matlab::data::Array type; };

template<>
void k9nv<0>(std::shared_ptr<MATLABControllerType> _matlabPtr, double x, double Scale, std::u16string Label) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray<double>({1,1}, {x}),
        _arrayFactory.createCharArray("Scale"),
        _arrayFactory.createArray<double>({1,1}, {Scale}),
        _arrayFactory.createCharArray("Label"),
        _arrayFactory.createArray<std::u16string>({1,1}, {Label}) };
    _matlabPtr->feval(u"k9nv", 0, _args);
}

template<>
matlab::data::Array k9nv<1>(std::shared_ptr<MATLABControllerType> _matlabPtr, double x, double Scale, std::u16string Label) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray<double>({1,1}, {x}),
        _arrayFactory.createCharArray("Scale"),
        _arrayFactory.createArray<double>({1,1}, {Scale}),
        _arrayFactory.createCharArray("Label"),
        _arrayFactory.createArray<std::u16string>({1,1}, {Label}) };
    matlab::data::Array _result_mda = _matlabPtr->feval(u"k9nv", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}
template<>
void k9nv<0>(std::shared_ptr<MATLABControllerType> _matlabPtr, std::complex<double> x, std::complex<double> Scale, std::u16string Label) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray<std::complex<double>>({1,1}, {x}),
        _arrayFactory.createCharArray("Scale"),
        _arrayFactory.createArray<std::complex<double>>({1,1}, {Scale}),
        _arrayFactory.createCharArray("Label"),
        _arrayFactory.createArray<std::u16string>({1,1}, {Label}) };
    _matlabPtr->feval(u"k9nv", 0, _args);
}

template<>
matlab::data::Array k9nv<1>(std::shared_ptr<MATLABControllerType> _matlabPtr, std::complex<double> x, std::complex<double> Scale, std::u16string Label) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray<std::complex<double>>({1,1}, {x}),
        _arrayFactory.createCharArray("Scale"),
        _arrayFactory.createArray<std::complex<double>>({1,1}, {Scale}),
        _arrayFactory.createCharArray("Label"),
        _arrayFactory.createArray<std::u16string>({1,1}, {Label}) };
    matlab::data::Array _result_mda = _matlabPtr->feval(u"k9nv", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}




template<size_t nargout>
struct return_type_k9print { typedef void type; };


template<size_t nargout = 1>
typename return_type_k9print<nargout>::type k9print(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array n) {
    static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
}
template<>
struct return_type_k9print<0> { typedef void type; };

template<>
struct return_type_k9print<1> { typedef matlab::data::Array type; };

template<>
void k9print<0>(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array n) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        n };
    _matlabPtr->feval(u"k9print", 0, _args);
}

template<>
matlab::data::Array k9print<1>(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array n) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        n };
    matlab::data::Array _result_mda = _matlabPtr->feval(u"k9print", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}



void k9noout(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array x) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        x };
    _matlabPtr->feval(u"k9noout", 0, _args);
}




template<size_t nargout>
struct return_type_k9slow { typedef void type; };


template<size_t nargout = 1>
typename return_type_k9slow<nargout>::type k9slow(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array seconds) {
    static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
}
template<>
struct return_type_k9slow<0> { typedef void type; };

template<>
struct return_type_k9slow<1> { typedef matlab::data::Array type; };

template<>
void k9slow<0>(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array seconds) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        seconds };
    _matlabPtr->feval(u"k9slow", 0, _args);
}

template<>
matlab::data::Array k9slow<1>(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array seconds) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        seconds };
    matlab::data::Array _result_mda = _matlabPtr->feval(u"k9slow", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}




template<size_t nargout>
struct return_type_k9err { typedef void type; };


template<size_t nargout = 1>
typename return_type_k9err<nargout>::type k9err(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array kind) {
    static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
}
template<>
struct return_type_k9err<0> { typedef void type; };

template<>
struct return_type_k9err<1> { typedef matlab::data::Array type; };

template<>
void k9err<0>(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array kind) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        kind };
    _matlabPtr->feval(u"k9err", 0, _args);
}

template<>
matlab::data::Array k9err<1>(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array kind) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        kind };
    matlab::data::Array _result_mda = _matlabPtr->feval(u"k9err", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}




template<size_t nargout>
struct return_type_k9quiet { typedef void type; };


template<size_t nargout = 1>
typename return_type_k9quiet<nargout>::type k9quiet(std::shared_ptr<MATLABControllerType> _matlabPtr) {
    static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
}
template<>
struct return_type_k9quiet<0> { typedef void type; };

template<>
struct return_type_k9quiet<1> { typedef matlab::data::Array type; };

template<>
void k9quiet<0>(std::shared_ptr<MATLABControllerType> _matlabPtr) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = { };
    _matlabPtr->feval(u"k9quiet", 0, _args);
}

template<>
matlab::data::Array k9quiet<1>(std::shared_ptr<MATLABControllerType> _matlabPtr) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = { };
    matlab::data::Array _result_mda = _matlabPtr->feval(u"k9quiet", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}




template<size_t nargout>
struct return_type_k9t_real { typedef void type; };


template<size_t nargout = 1>
typename return_type_k9t_real<nargout>::type k9t_real(std::shared_ptr<MATLABControllerType> _matlabPtr, double a, std::vector<double> b, matlab::data::Array c, matlab::data::Array d) {
    static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
}
template<>
struct return_type_k9t_real<0> { typedef void type; };

template<>
struct return_type_k9t_real<1> { typedef matlab::data::Array type; };

template<>
void k9t_real<0>(std::shared_ptr<MATLABControllerType> _matlabPtr, double a, std::vector<double> b, matlab::data::Array c, matlab::data::Array d) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray<double>({1,1}, {a}),
        _arrayFactory.createArray({1,b.size()}, b.begin(), b.end()),
        c,
        d };
    _matlabPtr->feval(u"k9t_real", 0, _args);
}

template<>
matlab::data::Array k9t_real<1>(std::shared_ptr<MATLABControllerType> _matlabPtr, double a, std::vector<double> b, matlab::data::Array c, matlab::data::Array d) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray<double>({1,1}, {a}),
        _arrayFactory.createArray({1,b.size()}, b.begin(), b.end()),
        c,
        d };
    matlab::data::Array _result_mda = _matlabPtr->feval(u"k9t_real", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}




template<size_t nargout>
struct return_type_k9t_cplx { typedef void type; };


template<size_t nargout = 1>
typename return_type_k9t_cplx<nargout>::type k9t_cplx(std::shared_ptr<MATLABControllerType> _matlabPtr, std::complex<double> a, std::vector<std::complex<double>> b, std::complex<float> c) {
    static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
}

template<size_t nargout = 1>
typename return_type_k9t_cplx<nargout>::type k9t_cplx(std::shared_ptr<MATLABControllerType> _matlabPtr, double a, std::vector<double> b, float c) {
    static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
}
template<>
struct return_type_k9t_cplx<0> { typedef void type; };

template<>
struct return_type_k9t_cplx<1> { typedef matlab::data::Array type; };

template<>
void k9t_cplx<0>(std::shared_ptr<MATLABControllerType> _matlabPtr, double a, std::vector<double> b, float c) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray<double>({1,1}, {a}),
        _arrayFactory.createArray({1,b.size()}, b.begin(), b.end()),
        _arrayFactory.createArray<float>({1,1}, {c}) };
    _matlabPtr->feval(u"k9t_cplx", 0, _args);
}

template<>
matlab::data::Array k9t_cplx<1>(std::shared_ptr<MATLABControllerType> _matlabPtr, double a, std::vector<double> b, float c) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray<double>({1,1}, {a}),
        _arrayFactory.createArray({1,b.size()}, b.begin(), b.end()),
        _arrayFactory.createArray<float>({1,1}, {c}) };
    matlab::data::Array _result_mda = _matlabPtr->feval(u"k9t_cplx", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}
template<>
void k9t_cplx<0>(std::shared_ptr<MATLABControllerType> _matlabPtr, std::complex<double> a, std::vector<std::complex<double>> b, std::complex<float> c) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray<std::complex<double>>({1,1}, {a}),
        _arrayFactory.createArray({1,b.size()}, b.begin(), b.end()),
        _arrayFactory.createArray<std::complex<float>>({1,1}, {c}) };
    _matlabPtr->feval(u"k9t_cplx", 0, _args);
}

template<>
matlab::data::Array k9t_cplx<1>(std::shared_ptr<MATLABControllerType> _matlabPtr, std::complex<double> a, std::vector<std::complex<double>> b, std::complex<float> c) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray<std::complex<double>>({1,1}, {a}),
        _arrayFactory.createArray({1,b.size()}, b.begin(), b.end()),
        _arrayFactory.createArray<std::complex<float>>({1,1}, {c}) };
    matlab::data::Array _result_mda = _matlabPtr->feval(u"k9t_cplx", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}




template<size_t nargout>
struct return_type_k9t_ints { typedef void type; };


template<size_t nargout = 1>
typename return_type_k9t_ints<nargout>::type k9t_ints(std::shared_ptr<MATLABControllerType> _matlabPtr, int8_t a, std::vector<uint8_t> b, int16_t c, uint16_t d, int32_t e, matlab::data::Array f, int64_t g, uint64_t h, std::complex<int32_t> i) {
    static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
}

template<size_t nargout = 1>
typename return_type_k9t_ints<nargout>::type k9t_ints(std::shared_ptr<MATLABControllerType> _matlabPtr, int8_t a, std::vector<uint8_t> b, int16_t c, uint16_t d, int32_t e, matlab::data::Array f, int64_t g, uint64_t h, int32_t i) {
    static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
}
template<>
struct return_type_k9t_ints<0> { typedef void type; };

template<>
struct return_type_k9t_ints<1> { typedef matlab::data::Array type; };

template<>
void k9t_ints<0>(std::shared_ptr<MATLABControllerType> _matlabPtr, int8_t a, std::vector<uint8_t> b, int16_t c, uint16_t d, int32_t e, matlab::data::Array f, int64_t g, uint64_t h, int32_t i) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray<int8_t>({1,1}, {a}),
        _arrayFactory.createArray({1,b.size()}, b.begin(), b.end()),
        _arrayFactory.createArray<int16_t>({1,1}, {c}),
        _arrayFactory.createArray<uint16_t>({1,1}, {d}),
        _arrayFactory.createArray<int32_t>({1,1}, {e}),
        f,
        _arrayFactory.createArray<int64_t>({1,1}, {g}),
        _arrayFactory.createArray<uint64_t>({1,1}, {h}),
        _arrayFactory.createArray<int32_t>({1,1}, {i}) };
    _matlabPtr->feval(u"k9t_ints", 0, _args);
}

template<>
matlab::data::Array k9t_ints<1>(std::shared_ptr<MATLABControllerType> _matlabPtr, int8_t a, std::vector<uint8_t> b, int16_t c, uint16_t d, int32_t e, matlab::data::Array f, int64_t g, uint64_t h, int32_t i) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray<int8_t>({1,1}, {a}),
        _arrayFactory.createArray({1,b.size()}, b.begin(), b.end()),
        _arrayFactory.createArray<int16_t>({1,1}, {c}),
        _arrayFactory.createArray<uint16_t>({1,1}, {d}),
        _arrayFactory.createArray<int32_t>({1,1}, {e}),
        f,
        _arrayFactory.createArray<int64_t>({1,1}, {g}),
        _arrayFactory.createArray<uint64_t>({1,1}, {h}),
        _arrayFactory.createArray<int32_t>({1,1}, {i}) };
    matlab::data::Array _result_mda = _matlabPtr->feval(u"k9t_ints", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}
template<>
void k9t_ints<0>(std::shared_ptr<MATLABControllerType> _matlabPtr, int8_t a, std::vector<uint8_t> b, int16_t c, uint16_t d, int32_t e, matlab::data::Array f, int64_t g, uint64_t h, std::complex<int32_t> i) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray<int8_t>({1,1}, {a}),
        _arrayFactory.createArray({1,b.size()}, b.begin(), b.end()),
        _arrayFactory.createArray<int16_t>({1,1}, {c}),
        _arrayFactory.createArray<uint16_t>({1,1}, {d}),
        _arrayFactory.createArray<int32_t>({1,1}, {e}),
        f,
        _arrayFactory.createArray<int64_t>({1,1}, {g}),
        _arrayFactory.createArray<uint64_t>({1,1}, {h}),
        _arrayFactory.createArray<std::complex<int32_t>>({1,1}, {i}) };
    _matlabPtr->feval(u"k9t_ints", 0, _args);
}

template<>
matlab::data::Array k9t_ints<1>(std::shared_ptr<MATLABControllerType> _matlabPtr, int8_t a, std::vector<uint8_t> b, int16_t c, uint16_t d, int32_t e, matlab::data::Array f, int64_t g, uint64_t h, std::complex<int32_t> i) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray<int8_t>({1,1}, {a}),
        _arrayFactory.createArray({1,b.size()}, b.begin(), b.end()),
        _arrayFactory.createArray<int16_t>({1,1}, {c}),
        _arrayFactory.createArray<uint16_t>({1,1}, {d}),
        _arrayFactory.createArray<int32_t>({1,1}, {e}),
        f,
        _arrayFactory.createArray<int64_t>({1,1}, {g}),
        _arrayFactory.createArray<uint64_t>({1,1}, {h}),
        _arrayFactory.createArray<std::complex<int32_t>>({1,1}, {i}) };
    matlab::data::Array _result_mda = _matlabPtr->feval(u"k9t_ints", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}




template<size_t nargout>
struct return_type_k9t_misc { typedef void type; };


template<size_t nargout = 1>
typename return_type_k9t_misc<nargout>::type k9t_misc(std::shared_ptr<MATLABControllerType> _matlabPtr, bool a, std::vector<bool> b, std::u16string c, char16_t d, std::u16string e, std::vector<std::u16string> f, matlab::data::Array g, matlab::data::Array h, matlab::data::Array k, matlab::data::Array m, k9color n, k9point p, std::vector<k9point> q, matlab::data::Array s, matlab::data::Array t, matlab::data::Array u) {
    static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
}
template<>
struct return_type_k9t_misc<0> { typedef void type; };

template<>
struct return_type_k9t_misc<1> { typedef matlab::data::Array type; };

template<>
void k9t_misc<0>(std::shared_ptr<MATLABControllerType> _matlabPtr, bool a, std::vector<bool> b, std::u16string c, char16_t d, std::u16string e, std::vector<std::u16string> f, matlab::data::Array g, matlab::data::Array h, matlab::data::Array k, matlab::data::Array m, k9color n, k9point p, std::vector<k9point> q, matlab::data::Array s, matlab::data::Array t, matlab::data::Array u) { 
    matlab::data::ArrayFactory _arrayFactory;
    matlab::data::Array _n_Enum = _arrayFactory.createEnumArray({1,1}, "k9color", {getk9colorString(n)});
    std::vector<matlab::data::Object> _objHolder;
    matlab::data::Array _mda;
    size_t _numobj;

    _numobj = q.size();
    _objHolder.clear();
    for(size_t _i=0; _i<_numobj; _i++) {
        _mda = q[_i];
        _objHolder.push_back(((matlab::data::ObjectArray)_mda)[0]);
    }
    matlab::data::ObjectArray _q_ConcatArray = _arrayFactory.createArray({1,_numobj}, _objHolder.begin(), _objHolder.end());

    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray<bool>({1,1}, {a}),
        _arrayFactory.createArray({1,b.size()}, b.begin(), b.end()),
        _arrayFactory.createArray({1,c.size()}, c.begin(), c.end()),
        _arrayFactory.createArray<char16_t>({1,1}, {d}),
        _arrayFactory.createArray<std::u16string>({1,1}, {e}),
        _arrayFactory.createArray({1,f.size()}, f.begin(), f.end()),
        g,
        h,
        k,
        m,
        _n_Enum,
        p,
        _q_ConcatArray,
        _arrayFactory.createArray<matlab::data::Array>({1,1}, {s}),
        t,
        u };
    _matlabPtr->feval(u"k9t_misc", 0, _args);
}

template<>
matlab::data::Array k9t_misc<1>(std::shared_ptr<MATLABControllerType> _matlabPtr, bool a, std::vector<bool> b, std::u16string c, char16_t d, std::u16string e, std::vector<std::u16string> f, matlab::data::Array g, matlab::data::Array h, matlab::data::Array k, matlab::data::Array m, k9color n, k9point p, std::vector<k9point> q, matlab::data::Array s, matlab::data::Array t, matlab::data::Array u) { 
    matlab::data::ArrayFactory _arrayFactory;
    matlab::data::Array _n_Enum = _arrayFactory.createEnumArray({1,1}, "k9color", {getk9colorString(n)});
    std::vector<matlab::data::Object> _objHolder;
    matlab::data::Array _mda;
    size_t _numobj;

    _numobj = q.size();
    _objHolder.clear();
    for(size_t _i=0; _i<_numobj; _i++) {
        _mda = q[_i];
        _objHolder.push_back(((matlab::data::ObjectArray)_mda)[0]);
    }
    matlab::data::ObjectArray _q_ConcatArray = _arrayFactory.createArray({1,_numobj}, _objHolder.begin(), _objHolder.end());

    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray<bool>({1,1}, {a}),
        _arrayFactory.createArray({1,b.size()}, b.begin(), b.end()),
        _arrayFactory.createArray({1,c.size()}, c.begin(), c.end()),
        _arrayFactory.createArray<char16_t>({1,1}, {d}),
        _arrayFactory.createArray<std::u16string>({1,1}, {e}),
        _arrayFactory.createArray({1,f.size()}, f.begin(), f.end()),
        g,
        h,
        k,
        m,
        _n_Enum,
        p,
        _q_ConcatArray,
        _arrayFactory.createArray<matlab::data::Array>({1,1}, {s}),
        t,
        u };
    matlab::data::Array _result_mda = _matlabPtr->feval(u"k9t_misc", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}




template<size_t nargout>
struct return_type_k9t_untyped { typedef void type; };


template<size_t nargout = 1>
typename return_type_k9t_untyped<nargout>::type k9t_untyped(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array a, matlab::data::Array b) {
    static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
}
template<>
struct return_type_k9t_untyped<0> { typedef void type; };

template<>
struct return_type_k9t_untyped<1> { typedef matlab::data::Array type; };

template<>
void k9t_untyped<0>(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array a, matlab::data::Array b) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        a,
        b };
    _matlabPtr->feval(u"k9t_untyped", 0, _args);
}

template<>
matlab::data::Array k9t_untyped<1>(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array a, matlab::data::Array b) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        a,
        b };
    matlab::data::Array _result_mda = _matlabPtr->feval(u"k9t_untyped", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}




template<size_t nargout>
struct return_type_k9t_opt { typedef void type; };


template<size_t nargout = 1>
typename return_type_k9t_opt<nargout>::type k9t_opt(std::shared_ptr<MATLABControllerType> _matlabPtr, double a, double b, std::u16string c) {
    static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
}
template<>
struct return_type_k9t_opt<0> { typedef void type; };

template<>
struct return_type_k9t_opt<1> { typedef matlab::data::Array type; };

template<>
void k9t_opt<0>(std::shared_ptr<MATLABControllerType> _matlabPtr, double a, double b, std::u16string c) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray<double>({1,1}, {a}),
        _arrayFactory.createArray<double>({1,1}, {b}),
        _arrayFactory.createArray<std::u16string>({1,1}, {c}) };
    _matlabPtr->feval(u"k9t_opt", 0, _args);
}

template<>
matlab::data::Array k9t_opt<1>(std::shared_ptr<MATLABControllerType> _matlabPtr, double a, double b, std::u16string c) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray<double>({1,1}, {a}),
        _arrayFactory.createArray<double>({1,1}, {b}),
        _arrayFactory.createArray<std::u16string>({1,1}, {c}) };
    matlab::data::Array _result_mda = _matlabPtr->feval(u"k9t_opt", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}




template<size_t nargout>
struct return_type_k9t_ref { typedef void type; };


template<size_t nargout = 1>
typename return_type_k9t_ref<nargout>::type k9t_ref(std::shared_ptr<MATLABControllerType> _matlabPtr, std::vector<double> x, double n) {
    static_assert(nargout<=2, "Too many outputs specified. Maximum outputs is 2.");
}
template<>
struct return_type_k9t_ref<0> { typedef void type; };

template<>
struct return_type_k9t_ref<1> { typedef matlab::data::Array type; };

template<>
struct return_type_k9t_ref<2> { typedef std::tuple<matlab::data::Array, matlab::data::Array> type; };

template<>
void k9t_ref<0>(std::shared_ptr<MATLABControllerType> _matlabPtr, std::vector<double> x, double n) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray({1,x.size()}, x.begin(), x.end()),
        _arrayFactory.createArray<double>({1,1}, {n}) };
    _matlabPtr->feval(u"k9t_ref", 0, _args);
}

template<>
matlab::data::Array k9t_ref<1>(std::shared_ptr<MATLABControllerType> _matlabPtr, std::vector<double> x, double n) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray({1,x.size()}, x.begin(), x.end()),
        _arrayFactory.createArray<double>({1,1}, {n}) };
    matlab::data::Array _result_mda = _matlabPtr->feval(u"k9t_ref", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}

template<>
std::tuple<matlab::data::Array, matlab::data::Array> k9t_ref<2>(std::shared_ptr<MATLABControllerType> _matlabPtr, std::vector<double> x, double n) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray({1,x.size()}, x.begin(), x.end()),
        _arrayFactory.createArray<double>({1,1}, {n}) };
    std::vector<matlab::data::Array> _result_mda = _matlabPtr->feval(u"k9t_ref",2, _args);
    std::tuple<matlab::data::Array, matlab::data::Array> _result;
    std::get<0>(_result) = _result_mda.at(0);
    std::get<1>(_result) = _result_mda.at(1);
    return _result;
}




template<size_t nargout>
struct return_type_k9t_keyword { typedef void type; };


template<size_t nargout = 1>
typename return_type_k9t_keyword<nargout>::type k9t_keyword(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array _int, matlab::data::Array string, matlab::data::Array base) {
    static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
}
template<>
struct return_type_k9t_keyword<0> { typedef void type; };

template<>
struct return_type_k9t_keyword<1> { typedef matlab::data::Array type; };

template<>
void k9t_keyword<0>(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array _int, matlab::data::Array string, matlab::data::Array base) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _int,
        string,
        base };
    _matlabPtr->feval(u"k9t_keyword", 0, _args);
}

template<>
matlab::data::Array k9t_keyword<1>(std::shared_ptr<MATLABControllerType> _matlabPtr, matlab::data::Array _int, matlab::data::Array string, matlab::data::Array base) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _int,
        string,
        base };
    matlab::data::Array _result_mda = _matlabPtr->feval(u"k9t_keyword", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}




template<size_t nargout>
struct return_type_k9t_multi { typedef void type; };


template<size_t nargout = 1>
typename return_type_k9t_multi<nargout>::type k9t_multi(std::shared_ptr<MATLABControllerType> _matlabPtr, std::vector<double> values) {
    static_assert(nargout<=3, "Too many outputs specified. Maximum outputs is 3.");
}
template<>
struct return_type_k9t_multi<0> { typedef void type; };

template<>
struct return_type_k9t_multi<1> { typedef double type; };

template<>
struct return_type_k9t_multi<2> { typedef std::tuple<double, int32_t> type; };

template<>
struct return_type_k9t_multi<3> { typedef std::tuple<double, int32_t, std::u16string> type; };

template<>
void k9t_multi<0>(std::shared_ptr<MATLABControllerType> _matlabPtr, std::vector<double> values) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray({1,values.size()}, values.begin(), values.end()) };
    _matlabPtr->feval(u"k9t_multi", 0, _args);
}

template<>
double k9t_multi<1>(std::shared_ptr<MATLABControllerType> _matlabPtr, std::vector<double> values) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray({1,values.size()}, values.begin(), values.end()) };
    matlab::data::Array _result_mda = _matlabPtr->feval(u"k9t_multi", _args);
    double _result;
    _result = MatlabTypesInterface::convertMDAtoScalar<double>(_result_mda);
    return _result;
}

template<>
std::tuple<double, int32_t> k9t_multi<2>(std::shared_ptr<MATLABControllerType> _matlabPtr, std::vector<double> values) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray({1,values.size()}, values.begin(), values.end()) };
    std::vector<matlab::data::Array> _result_mda = _matlabPtr->feval(u"k9t_multi",2, _args);
    std::tuple<double, int32_t> _result;
    std::get<0>(_result) = MatlabTypesInterface::convertMDAtoScalar<double>(_result_mda.at(0));
    std::get<1>(_result) = MatlabTypesInterface::convertMDAtoScalar<int32_t>(_result_mda.at(1));
    return _result;
}

template<>
std::tuple<double, int32_t, std::u16string> k9t_multi<3>(std::shared_ptr<MATLABControllerType> _matlabPtr, std::vector<double> values) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray({1,values.size()}, values.begin(), values.end()) };
    std::vector<matlab::data::Array> _result_mda = _matlabPtr->feval(u"k9t_multi",3, _args);
    std::tuple<double, int32_t, std::u16string> _result;
    std::get<0>(_result) = MatlabTypesInterface::convertMDAtoScalar<double>(_result_mda.at(0));
    std::get<1>(_result) = MatlabTypesInterface::convertMDAtoScalar<int32_t>(_result_mda.at(1));
    std::get<2>(_result) = MatlabTypesInterface::convertMDAtoScalar<std::u16string>(_result_mda.at(2));
    return _result;
}



void k9t_none(std::shared_ptr<MATLABControllerType> _matlabPtr) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = { };
    _matlabPtr->feval(u"k9t_none", 0, _args);
}




template<size_t nargout>
struct return_type_k9t_objects { typedef void type; };


template<size_t nargout = 1>
typename return_type_k9t_objects<nargout>::type k9t_objects(std::shared_ptr<MATLABControllerType> _matlabPtr, k9color color) {
    static_assert(nargout<=2, "Too many outputs specified. Maximum outputs is 2.");
}
template<>
struct return_type_k9t_objects<0> { typedef void type; };

template<>
struct return_type_k9t_objects<1> { typedef k9point type; };

template<>
struct return_type_k9t_objects<2> { typedef std::tuple<k9point, k9color> type; };

template<>
void k9t_objects<0>(std::shared_ptr<MATLABControllerType> _matlabPtr, k9color color) { 
    matlab::data::ArrayFactory _arrayFactory;
    matlab::data::Array _color_Enum = _arrayFactory.createEnumArray({1,1}, "k9color", {getk9colorString(color)});
    std::vector<matlab::data::Array> _args = {
        _color_Enum };
    _matlabPtr->feval(u"k9t_objects", 0, _args);
}

template<>
k9point k9t_objects<1>(std::shared_ptr<MATLABControllerType> _matlabPtr, k9color color) { 
    matlab::data::ArrayFactory _arrayFactory;
    matlab::data::Array _color_Enum = _arrayFactory.createEnumArray({1,1}, "k9color", {getk9colorString(color)});
    std::vector<matlab::data::Array> _args = {
        _color_Enum };
    matlab::data::Array _result_mda = _matlabPtr->feval(u"k9t_objects", _args);
    k9point _object(_matlabPtr, _result_mda);
    return _object;
}

template<>
std::tuple<k9point, k9color> k9t_objects<2>(std::shared_ptr<MATLABControllerType> _matlabPtr, k9color color) { 
    matlab::data::ArrayFactory _arrayFactory;
    matlab::data::Array _color_Enum = _arrayFactory.createEnumArray({1,1}, "k9color", {getk9colorString(color)});
    std::vector<matlab::data::Array> _args = {
        _color_Enum };
    std::vector<matlab::data::Array> _result_mda = _matlabPtr->feval(u"k9t_objects",2, _args);
    std::tuple<k9point, k9color> _result;
    k9point _obj1 (_matlabPtr, _result_mda[0]);
    std::get<0>(_result) = _obj1;
    const std::string& _str2 = _result_mda.at(1)[0];
    std::get<1>(_result) = getk9colorEnum(_str2);
    return _result;
}


namespace k9pkg {

template<size_t nargout>
struct return_type_k9pkg_k9inner { typedef void type; };


template<size_t nargout = 1>
typename return_type_k9pkg_k9inner<nargout>::type k9inner(std::shared_ptr<MATLABControllerType> _matlabPtr, double x) {
    static_assert(nargout<=1, "Too many outputs specified. Maximum outputs is 1.");
}
template<>
struct return_type_k9pkg_k9inner<0> { typedef void type; };

template<>
struct return_type_k9pkg_k9inner<1> { typedef matlab::data::Array type; };

template<>
void k9inner<0>(std::shared_ptr<MATLABControllerType> _matlabPtr, double x) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray<double>({1,1}, {x}) };
    _matlabPtr->feval(u"k9pkg.k9inner", 0, _args);
}

template<>
matlab::data::Array k9inner<1>(std::shared_ptr<MATLABControllerType> _matlabPtr, double x) { 
    matlab::data::ArrayFactory _arrayFactory;
    std::vector<matlab::data::Array> _args = {
        _arrayFactory.createArray<double>({1,1}, {x}) };
    matlab::data::Array _result_mda = _matlabPtr->feval(u"k9pkg.k9inner", _args);
    matlab::data::Array _result;
    _result = _result_mda;
    return _result;
}


}