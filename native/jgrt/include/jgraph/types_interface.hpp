/*
 * jgraph/types_interface.hpp - what a strongly typed interface (<library>v2.hpp, which
 * compiler.build.cppSharedLibrary and mcc -W cpplib write) is built on (compiler and SDK plan,
 * stage K12): the controller type the generated functions take, the conversions of results to C++
 * values, and the base classes of the generated classes, whose properties are read and set through
 * subsref and subsasgn and whose methods are called through feval, as R2025b's generated code
 * expects of them.
 *
 * MathWorks' names, outside any namespace as the generated code names them; the text is JGraph's own.
 */
#ifndef JGRAPH_TYPES_INTERFACE_HPP
#define JGRAPH_TYPES_INTERFACE_HPP

#include "cpplib.hpp"

#include <stdexcept>

/// What a generated function calls through: a C++ shared library.
typedef jgraph::cpplib::MATLABLibrary MATLABControllerType;

namespace MatlabTypesInterface {

/// A visitor that answers a numeric array as complex: as it is when it is, its elements given a zero imaginary part when not.
class ConvertToComplex {
public:
    typedef jgraph::data::Array result_type;

    template <typename T>
    result_type operator()(const jgraph::data::TypedArray<std::complex<T>>& arr) {
        return arr;
    }

    template <typename T, typename std::enable_if<std::is_arithmetic<T>::value && !std::is_same<T, bool>::value>::type* = nullptr>
    result_type operator()(const jgraph::data::TypedArray<T>& arr) {
        jgraph::data::ArrayFactory f;
        jgraph::data::TypedArray<std::complex<T>> complex = f.createArray<std::complex<T>>(arr.getDimensions());
        auto it = complex.begin();
        for (const auto& element : arr) {
            *it = std::complex<T>(element, 0);
            ++it;
        }
        return complex;
    }

    template <typename T>
    result_type operator()(const T&) {
        throw std::logic_error("Cannot convert this type to complex!");
    }
};

/// An array's elements as a std::vector of T.
template <typename T>
inline std::vector<T> convertMDAtoVector(jgraph::data::Array arr) {
    const jgraph::data::TypedArray<T> typed = arr;
    return std::vector<T>(typed.cbegin(), typed.cend());
}

/// A string array's elements as text, a missing one empty.
template <>
inline std::vector<std::u16string> convertMDAtoVector(jgraph::data::Array strArray) {
    const jgraph::data::TypedArray<jgraph::data::MATLABString> strings(strArray);
    std::vector<std::u16string> out;
    for (jgraph::data::MATLABString s : strings) {
        out.push_back(s.has_value() ? *s : std::u16string());
    }
    return out;
}

/// An array's first element as T.
template <typename T>
inline T convertMDAtoScalar(jgraph::data::Array arr) {
    jgraph::data::TypedArray<T> typed = arr;
    T value = arr[0];
    return value;
}

/// A char array's text, or a string array's first element (empty when missing).
template <>
inline std::u16string convertMDAtoScalar(jgraph::data::Array arr) {
    if (arr.getType() == jgraph::data::ArrayType::CHAR) {
        return jgraph::data::CharArray(arr).toUTF16();
    }
    const jgraph::data::TypedArray<jgraph::data::MATLABString> strings(arr);
    jgraph::data::MATLABString s = strings[0];
    return s.has_value() ? *s : std::u16string();
}

} // namespace MatlabTypesInterface

/// What a generated value class derives from: the MATLAB object it stands for, and the library that made it.
template <typename ControllerType>
class MATLABObject {
public:
    operator jgraph::data::Array() { return m_object; }

    MATLABObject() {}
    MATLABObject(std::shared_ptr<ControllerType> matlabPtr, std::u16string clsName) : m_matlabPtr(matlabPtr) {
        m_object = MATLABCallDefaultConstructor(clsName);
    }
    MATLABObject(std::shared_ptr<ControllerType> matlabPtr, jgraph::data::Array obj) : m_matlabPtr(matlabPtr), m_object(obj) {}

protected:
    std::shared_ptr<ControllerType> m_matlabPtr = nullptr;
    jgraph::data::Array m_object = jgraph::data::Array();

    template <typename T>
    inline T MATLABGetScalarProperty(std::u16string propName) {
        jgraph::data::Array value = MATLABGetArrayProperty(propName);
        return value[0];
    }

    template <typename T>
    inline std::complex<T> MATLABGetComplexScalarProperty(std::u16string propName) {
        jgraph::data::Array value = MATLABGetArrayProperty(propName);
        MatlabTypesInterface::ConvertToComplex toComplex;
        jgraph::data::Array converted = jgraph::data::apply_visitor(value, toComplex);
        jgraph::data::TypedArray<std::complex<T>> typed(converted);
        return typed[0];
    }

    template <typename T>
    inline void MATLABSetScalarProperty(std::u16string propName, T value) {
        jgraph::data::ArrayFactory f;
        MATLABSetArrayProperty(propName, f.createScalar<T>(value));
    }

    template <typename T>
    inline std::vector<T> MATLABGetVectorProperty(std::u16string propName) {
        const jgraph::data::TypedArray<T> arr = MATLABGetArrayProperty(propName);
        return std::vector<T>(arr.cbegin(), arr.cend());
    }

    template <typename T>
    inline void MATLABSetVectorProperty(std::u16string propName, std::vector<T> value) {
        jgraph::data::ArrayFactory f;
        MATLABSetArrayProperty(propName, f.createArray({value.size(), size_t(1)}, value.cbegin(), value.cend()));
    }

    /// A property, as subsref reads it.
    inline jgraph::data::Array MATLABGetArrayProperty(std::u16string propName) {
        return m_matlabPtr->feval(u"subsref", {m_object, Subscript(propName)});
    }

    /// A property set, as subsasgn sets it: a value object's copy is kept as this object.
    inline void MATLABSetArrayProperty(std::u16string propName, jgraph::data::Array value) {
        m_object = m_matlabPtr->feval(u"subsasgn", {m_object, Subscript(propName), value});
    }

    inline void MATLABCallNoOutputMethod(std::u16string fcn, std::vector<jgraph::data::Array> args) { m_matlabPtr->feval(fcn, 0, args); }

    inline jgraph::data::Array MATLABCallOneOutputMethod(std::u16string fcn, std::vector<jgraph::data::Array> args) {
        return m_matlabPtr->feval(fcn, args);
    }

    inline std::vector<jgraph::data::Array> MATLABCallMultiOutputMethod(std::u16string fcn, size_t nOut, std::vector<jgraph::data::Array> args) {
        return m_matlabPtr->feval(fcn, nOut, args);
    }

private:
    inline jgraph::data::Array MATLABCallDefaultConstructor(std::u16string clsName) {
        return m_matlabPtr->feval(clsName, std::vector<jgraph::data::Array>());
    }

    static jgraph::data::Array Subscript(const std::u16string& propName) {
        jgraph::data::ArrayFactory f;
        jgraph::data::StructArray s = f.createStructArray({1, 1}, {"type", "subs"});
        s[0]["type"] = f.createCharArray(".");
        s[0]["subs"] = f.createCharArray(propName);
        return s;
    }
};

/// What a generated handle class derives from: an object that may have been deleted.
template <typename ControllerType>
class MATLABHandleObject : public MATLABObject<ControllerType> {
public:
    MATLABHandleObject() : MATLABObject<ControllerType>() {}
    MATLABHandleObject(std::shared_ptr<ControllerType> matlabPtr, std::u16string clsName) : MATLABObject<ControllerType>(matlabPtr, clsName) {}
    MATLABHandleObject(std::shared_ptr<ControllerType> matlabPtr, jgraph::data::Array obj) : MATLABObject<ControllerType>(matlabPtr, obj) {}

    bool isvalid() {
        jgraph::data::TypedArray<bool> valid = this->m_matlabPtr->feval(u"isvalid", this->m_object);
        return valid[0];
    }
};

template <>
template <>
inline char16_t MATLABObject<MATLABControllerType>::MATLABGetScalarProperty(std::u16string propName) {
    jgraph::data::CharArray chars = MATLABGetArrayProperty(propName);
    return chars[0];
}

template <>
template <>
inline void MATLABObject<MATLABControllerType>::MATLABSetScalarProperty(std::u16string propName, char16_t value) {
    jgraph::data::ArrayFactory f;
    MATLABSetArrayProperty(propName, f.createCharArray(std::u16string(1, value)));
}

template <>
template <>
inline std::u16string MATLABObject<MATLABControllerType>::MATLABGetScalarProperty(std::u16string propName) {
    return MatlabTypesInterface::convertMDAtoScalar<std::u16string>(MATLABGetArrayProperty(propName));
}

template <>
template <>
inline void MATLABObject<MATLABControllerType>::MATLABSetScalarProperty(std::u16string propName, std::u16string value) {
    jgraph::data::ArrayFactory f;
    MATLABSetArrayProperty(propName, f.createScalar(value));
}

template <>
template <>
inline std::vector<std::u16string> MATLABObject<MATLABControllerType>::MATLABGetVectorProperty(std::u16string propName) {
    return MatlabTypesInterface::convertMDAtoVector<std::u16string>(MATLABGetArrayProperty(propName));
}

template <>
template <>
inline std::vector<jgraph::data::Object> MATLABObject<MATLABControllerType>::MATLABGetVectorProperty(std::u16string propName) {
    const jgraph::data::ObjectArray objects = MATLABGetArrayProperty(propName);
    return std::vector<jgraph::data::Object>(objects.cbegin(), objects.cend());
}

template <>
template <>
inline void MATLABObject<MATLABControllerType>::MATLABSetVectorProperty(std::u16string propName, std::vector<std::u16string> value) {
    jgraph::data::ArrayFactory f;
    std::vector<jgraph::data::MATLABString> strings(value.begin(), value.end());
    MATLABSetArrayProperty(propName, f.createArray({value.size(), size_t(1)}, strings.begin(), strings.end()));
}

template <>
template <>
inline void MATLABObject<MATLABControllerType>::MATLABSetVectorProperty(std::u16string propName, std::vector<jgraph::data::Object> value) {
    jgraph::data::ArrayFactory f;
    MATLABSetArrayProperty(propName, f.createArray({value.size(), size_t(1)}, value.begin(), value.end()));
}

template <>
template <>
inline jgraph::data::Array MATLABObject<MATLABControllerType>::MATLABGetScalarProperty(std::u16string propName) {
    return MATLABGetArrayProperty(propName);
}

template <>
template <>
inline void MATLABObject<MATLABControllerType>::MATLABSetScalarProperty(std::u16string propName, jgraph::data::Array value) {
    MATLABSetArrayProperty(propName, value);
}

#endif // JGRAPH_TYPES_INTERFACE_HPP
