/*
 * jgraph/data_array.hpp - the MATLAB Data API for C++ as the JGraph Runtime gives it: the arrays a
 * program makes, reads and changes, and passes to and gets back from a C++ shared library's
 * functions (compiler and SDK plan, stage K12).
 *
 * The class and member names are MathWorks' and the namespace is jgraph (decision KO3), so a
 * program written for MATLAB's API ports by naming jgraph where it named matlab - for example with
 * "namespace matlab = jgraph;" after the includes. The text is JGraph's own. What each operation
 * answers and refuses, with its message, follows R2025b's API as record-k12.ps1 measured it
 * (recordings\k12_client.out.txt, local).
 *
 * An array's elements live in the program's own memory, shared by the copies of the array until
 * one of them changes them (each Array that holds the storage counts; a reference or iterator taken
 * for writing first gives its array storage of its own). They reach the engine only when a function
 * is called (jgraph/execution.hpp).
 */
#ifndef JGRAPH_DATA_ARRAY_HPP
#define JGRAPH_DATA_ARRAY_HPP

#include <algorithm>
#include <atomic>
#include <complex>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <initializer_list>
#include <iterator>
#include <memory>
#include <ostream>
#include <stdexcept>
#include <string>
#include <type_traits>
#include <utility>
#include <vector>

namespace jgraph {

/// What every exception of the API derives from: what() says what went wrong.
class Exception {
public:
    virtual ~Exception() noexcept {}
    virtual const char* what() const noexcept = 0;
};

namespace data {

/// The kind of an array's elements.
enum class ArrayType : int {
    LOGICAL,
    CHAR,
    MATLAB_STRING,
    DOUBLE,
    SINGLE,
    INT8,
    UINT8,
    INT16,
    UINT16,
    INT32,
    UINT32,
    INT64,
    UINT64,
    COMPLEX_DOUBLE,
    COMPLEX_SINGLE,
    COMPLEX_INT8,
    COMPLEX_UINT8,
    COMPLEX_INT16,
    COMPLEX_UINT16,
    COMPLEX_INT32,
    COMPLEX_UINT32,
    COMPLEX_INT64,
    COMPLEX_UINT64,
    CELL,
    STRUCT,
    OBJECT,
    VALUE_OBJECT,
    HANDLE_OBJECT_REF,
    ENUM,
    SPARSE_LOGICAL,
    SPARSE_DOUBLE,
    SPARSE_COMPLEX_DOUBLE,
    UNKNOWN,
    SPARSE_SINGLE,
    SPARSE_COMPLEX_SINGLE,
};

/// How an array's elements are laid out in memory.
enum class MemoryLayout : int { COLUMN_MAJOR = 0, ROW_MAJOR, UNKNOWN };

/// How the data given to the factory is ordered.
enum class InputLayout : int { COLUMN_MAJOR = 0, ROW_MAJOR = 1, UNKNOWN = 2 };

enum class DataOrder : int { COLUMN_MAJOR, ROW_MAJOR };

/// The kinds of exception, one class each.
enum class ExceptionType : int {
    NoException = 0,
    InvalidArrayIndex,
    StringIndexMustBeLast,
    StringIndexNotValid,
    CantAssignArrayToThisArray,
    NotEnoughIndicesProvided,
    TooManyIndicesProvided,
    CanOnlyUseOneStringIndex,
    InvalidDataType,
    InvalidArrayType,
    InvalidFieldName,
    FailedToLoadLibMatlabDataArray,
    FailedToResolveSymbol,
    MustSpecifyClassName,
    WrongNumberOfEnumsSupplied,
    NonAsciiCharInInputData,
    NonAsciiCharInRequestedAsciiOutput,
    InvalidDimensionsInSparseArray,
    DuplicateFieldNameInStructArray,
    OutOfMemory,
    SystemError,
    CantIndexIntoEmptyArray,
    FeatureNotSupported,
    InvalidMemoryLayout,
    InvalidDimensionsInRowMajorArray,
    InvalidTypeInRowMajorArray,
    NumberOfElementsExceedsMaximum,
    InvalidHeterogeneousArray,
    ObjectNotAccessible,
    InvalidNumberOfElementsProvided,
    ObjectNotFound,
    WrongVectorSizeForStruct,
    PropertyExists,
    CouldNotCallConstructor,
    HandleObjectNotFound,
    InvalidHeterogeneousClass,
    InvalidHandleObjectType,
    InvalidClassName,
    RecursionLimitExceeded,
    PropertyNotFound,
    InvalidPropertyValue,
    FailedToSerializeObject,
    InvalidEnumerationName,
    StringIsMissing,
    UnSuppportedOperation,
    ObjectSerializationFailed,
    JavaNotSupported,
    IllegalFieldName,
    ZeroBufferSize,
    BufferSizeTooLarge,
    ArrayDimensionsExceedBufferSize,
    InvalidAssignmentForContainerArray,
    InvalidUTF8Input,
    InvalidUTF16toUTF8Conversion,
};

namespace detail {

/// An exception of one kind: its class says the kind, what() what happened.
template <typename Base, ExceptionType Kind>
class ArrayException : public Base, public std::exception {
public:
    ArrayException(std::string text) : fText(std::move(text)) {}
    const char* what() const noexcept override { return fText.c_str(); }

private:
    std::string fText;
};

} // namespace detail

using InvalidArrayIndexException = detail::ArrayException<jgraph::Exception, ExceptionType::InvalidArrayIndex>;
using TypeMismatchException = detail::ArrayException<jgraph::Exception, ExceptionType::InvalidDataType>;
using InvalidArrayTypeException = detail::ArrayException<jgraph::Exception, ExceptionType::InvalidArrayType>;
using InvalidMemoryLayoutException = detail::ArrayException<jgraph::Exception, ExceptionType::InvalidMemoryLayout>;
using InvalidDimensionsInRowMajorArrayException = detail::ArrayException<jgraph::Exception, ExceptionType::InvalidDimensionsInRowMajorArray>;
using InvalidTypeInRowMajorArrayException = detail::ArrayException<jgraph::Exception, ExceptionType::InvalidTypeInRowMajorArray>;
using SystemErrorException = detail::ArrayException<jgraph::Exception, ExceptionType::SystemError>;
using InvalidFieldNameException = detail::ArrayException<jgraph::Exception, ExceptionType::InvalidFieldName>;
using IllegalFieldNameException = detail::ArrayException<jgraph::Exception, ExceptionType::IllegalFieldName>;
using FailedToLoadLibMatlabDataArrayException = detail::ArrayException<jgraph::Exception, ExceptionType::FailedToLoadLibMatlabDataArray>;
using FailedToResolveSymbolException = detail::ArrayException<jgraph::Exception, ExceptionType::FailedToResolveSymbol>;
using WrongNumberOfEnumsSuppliedException = detail::ArrayException<jgraph::Exception, ExceptionType::WrongNumberOfEnumsSupplied>;
using MustSpecifyClassNameException = detail::ArrayException<jgraph::Exception, ExceptionType::MustSpecifyClassName>;
using TooManyIndicesProvidedException = detail::ArrayException<jgraph::Exception, ExceptionType::TooManyIndicesProvided>;
using NotEnoughIndicesProvidedException = detail::ArrayException<jgraph::Exception, ExceptionType::NotEnoughIndicesProvided>;
using StringIndexMustBeLastException = detail::ArrayException<jgraph::Exception, ExceptionType::StringIndexMustBeLast>;
using StringIndexNotValidException = detail::ArrayException<jgraph::Exception, ExceptionType::StringIndexNotValid>;
using CanOnlyUseOneStringIndexException = detail::ArrayException<jgraph::Exception, ExceptionType::CanOnlyUseOneStringIndex>;
using NonAsciiCharInInputDataException = detail::ArrayException<jgraph::Exception, ExceptionType::NonAsciiCharInInputData>;
using NonAsciiCharInRequestedAsciiOutputException = detail::ArrayException<jgraph::Exception, ExceptionType::NonAsciiCharInRequestedAsciiOutput>;
using InvalidDimensionsInSparseArrayException = detail::ArrayException<jgraph::Exception, ExceptionType::InvalidDimensionsInSparseArray>;
using DuplicateFieldNameInStructArrayException = detail::ArrayException<jgraph::Exception, ExceptionType::DuplicateFieldNameInStructArray>;
using CantIndexIntoEmptyArrayException = detail::ArrayException<jgraph::Exception, ExceptionType::CantIndexIntoEmptyArray>;
using FeatureNotSupportedException = detail::ArrayException<jgraph::Exception, ExceptionType::FeatureNotSupported>;
using NumberOfElementsExceedsMaximumException = detail::ArrayException<jgraph::Exception, ExceptionType::NumberOfElementsExceedsMaximum>;
using ObjectArrayIncompatibleTypesException = detail::ArrayException<jgraph::Exception, ExceptionType::InvalidHeterogeneousArray>;
using AccessingObjectNotSupportedException = detail::ArrayException<jgraph::Exception, ExceptionType::ObjectNotAccessible>;
using InvalidNumberOfElementsProvidedException = detail::ArrayException<jgraph::Exception, ExceptionType::InvalidNumberOfElementsProvided>;
using ObjectNotFoundException = detail::ArrayException<jgraph::Exception, ExceptionType::ObjectNotFound>;
using WrongVectorSizeForStructException = detail::ArrayException<jgraph::Exception, ExceptionType::WrongVectorSizeForStruct>;
using PropertyExistsException = detail::ArrayException<jgraph::Exception, ExceptionType::PropertyExists>;
using CouldNotCallConstructorException = detail::ArrayException<jgraph::Exception, ExceptionType::CouldNotCallConstructor>;
using HandleObjectNotFoundException = detail::ArrayException<jgraph::Exception, ExceptionType::HandleObjectNotFound>;
using InvalidHeterogeneousClassException = detail::ArrayException<jgraph::Exception, ExceptionType::InvalidHeterogeneousClass>;
using InvalidHandleObjectTypeException = detail::ArrayException<jgraph::Exception, ExceptionType::InvalidHandleObjectType>;
using InvalidClassNameException = detail::ArrayException<jgraph::Exception, ExceptionType::InvalidClassName>;
using RecursionLimitExceededException = detail::ArrayException<jgraph::Exception, ExceptionType::RecursionLimitExceeded>;
using PropertyNotFoundException = detail::ArrayException<jgraph::Exception, ExceptionType::PropertyNotFound>;
using InvalidPropertyValueException = detail::ArrayException<jgraph::Exception, ExceptionType::InvalidPropertyValue>;
using FailedToSerializeObjectException = detail::ArrayException<jgraph::Exception, ExceptionType::FailedToSerializeObject>;
using InvalidEnumerationNameException = detail::ArrayException<jgraph::Exception, ExceptionType::InvalidEnumerationName>;
using StringIsMissingException = detail::ArrayException<jgraph::Exception, ExceptionType::StringIsMissing>;
using UnSuppportedOperationException = detail::ArrayException<jgraph::Exception, ExceptionType::UnSuppportedOperation>;
using ObjectSerializationFailedException = detail::ArrayException<jgraph::Exception, ExceptionType::ObjectSerializationFailed>;
using JavaNotSupportedException = detail::ArrayException<jgraph::Exception, ExceptionType::JavaNotSupported>;
using InvalidAssignmentForContainerArrayException = detail::ArrayException<jgraph::Exception, ExceptionType::InvalidAssignmentForContainerArray>;
using InvalidUTF8InputException = detail::ArrayException<jgraph::Exception, ExceptionType::InvalidUTF8Input>;
using InvalidUTF16toUTF8ConversionException = detail::ArrayException<jgraph::Exception, ExceptionType::InvalidUTF16toUTF8Conversion>;

} // namespace data

using OutOfMemoryException = data::detail::ArrayException<Exception, data::ExceptionType::OutOfMemory>;

namespace data {

// --- Values that may be missing --------------------------------------------------------------------

/// The missing value an optional can be given.
struct nullopt_t {
    explicit constexpr nullopt_t(int) {}
};
constexpr nullopt_t nullopt{0};

/// A value, or none: a string array's element, missing when it has none.
template <typename T>
class optional {
public:
    typedef T value_type;

    optional() noexcept : fMissing(true), fData() {}
    optional(nullopt_t) noexcept : fMissing(true), fData() {}
    optional(std::nullptr_t) noexcept : fMissing(true), fData() {}
    optional(const optional& other) = default;
    optional(optional&& other) noexcept : fMissing(other.fMissing), fData(std::move(other.fData)) {}
    optional(T&& value) noexcept : fMissing(false), fData(std::move(value)) {}
    optional(const T& value) : fMissing(false), fData(value) {}

    optional& operator=(const optional& other) = default;
    optional& operator=(optional&& other) noexcept {
        fMissing = other.fMissing;
        fData = std::move(other.fData);
        return *this;
    }
    optional& operator=(T&& value) noexcept {
        fMissing = false;
        fData = std::move(value);
        return *this;
    }
    optional& operator=(const T& value) {
        fMissing = false;
        fData = value;
        return *this;
    }
    optional& operator=(nullopt_t) noexcept {
        reset();
        return *this;
    }

    const T* operator->() const { return &fData; }
    T* operator->() { return &fData; }
    const T& operator*() const { return fData; }
    T& operator*() { return fData; }
    explicit operator bool() const noexcept { return !fMissing; }
    operator T() const { return fData; }
    bool has_value() const noexcept { return !fMissing; }

    void swap(optional& other) noexcept {
        std::swap(fMissing, other.fMissing);
        std::swap(fData, other.fData);
    }
    void reset() noexcept {
        fMissing = true;
        fData = T();
    }

private:
    bool fMissing;
    T fData;
};

// Two missing values are not equal, as R2025b's API answers (local: optional).
template <class T>
bool operator==(const optional<T>& lhs, const optional<T>& rhs) noexcept {
    return lhs.has_value() && rhs.has_value() && *lhs == *rhs;
}
template <class T>
bool operator!=(const optional<T>& lhs, const optional<T>& rhs) noexcept {
    return !(lhs == rhs);
}
template <class T>
bool operator==(const optional<T>& opt, nullopt_t) noexcept {
    return !opt.has_value();
}
template <class T>
bool operator!=(const optional<T>& opt, nullopt_t) noexcept {
    return opt.has_value();
}
template <class T>
bool operator==(const optional<T>& opt, const T& value) noexcept {
    return opt.has_value() && *opt == value;
}
template <class T>
bool operator!=(const optional<T>& opt, const T& value) noexcept {
    return !(opt == value);
}

// --- Basic types ---------------------------------------------------------------------------------

typedef char16_t CHAR16_T;
using String = std::basic_string<char16_t>;
using MATLABString = optional<String>;
using ArrayDimensions = std::vector<size_t>;

/// The number of elements an array of these dimensions has: 1 for none at all.
inline size_t getNumElements(const ArrayDimensions& dims) noexcept {
    size_t n = 1;
    for (size_t d : dims) {
        n *= d;
    }
    return n;
}

template <typename T = void>
using buffer_deleter_t = void (*)(T*);
template <typename T>
using buffer_ptr_t = std::unique_ptr<T[], buffer_deleter_t<T>>;
template <typename T>
using buffer_ptr = std::unique_ptr<T[], buffer_deleter_t<>>;
using void_buffer_ptr_t = std::unique_ptr<void, buffer_deleter_t<>>;

template <typename T>
struct is_complex : std::false_type {};
template <typename T>
struct is_complex<std::complex<T>> : std::true_type {};
template <typename T>
struct is_complex<std::complex<T> const> : std::true_type {};
template <typename T>
struct is_const_complex : std::false_type {};
template <typename T>
struct is_const_complex<std::complex<T> const> : std::true_type {};
template <typename T>
struct is_optional : std::false_type {};
template <typename T>
struct is_optional<optional<T>> : std::true_type {};
template <typename T>
struct is_optional<optional<T> const> : std::true_type {};
template <typename T>
struct is_const_optional : std::false_type {};
template <typename T>
struct is_const_optional<optional<T> const> : std::true_type {};

// --- Declarations --------------------------------------------------------------------------------

class Array;
template <typename T>
class TypedArray;
class CharArray;
class StructArray;
class EnumArray;
template <typename T>
class SparseArray;
class Struct;
class Enumeration;
class Object;
class MATLABFieldIdentifier;
class ArrayFactory;
template <typename T>
class Reference;
template <bool is_const_ref>
class ArrayElementRef;
template <typename T, bool is_const_ref>
class ArrayElementTypedRef;
template <typename T>
class TypedIterator;
template <typename T>
class ForwardIterator;
template <template <class> class IteratorType, class ElementType>
class Range;
template <bool is_const_ref>
class IndexableArrayRef;

namespace impl {
class ArrayImpl;
class ObjectImpl;
} // namespace impl

namespace detail {
struct RefImpl;
class Access;
} // namespace detail

/// What an enumeration array's elements are: each one's member name is read as a std::string.
class Enumeration {};

// --- Element types -------------------------------------------------------------------------------

template <typename T>
struct GetArrayType;
template <>
struct GetArrayType<bool> { static const ArrayType type = ArrayType::LOGICAL; };
template <>
struct GetArrayType<char16_t> { static const ArrayType type = ArrayType::CHAR; };
template <>
struct GetArrayType<MATLABString> { static const ArrayType type = ArrayType::MATLAB_STRING; };
template <>
struct GetArrayType<double> { static const ArrayType type = ArrayType::DOUBLE; };
template <>
struct GetArrayType<float> { static const ArrayType type = ArrayType::SINGLE; };
template <>
struct GetArrayType<int8_t> { static const ArrayType type = ArrayType::INT8; };
template <>
struct GetArrayType<int16_t> { static const ArrayType type = ArrayType::INT16; };
template <>
struct GetArrayType<int32_t> { static const ArrayType type = ArrayType::INT32; };
template <>
struct GetArrayType<int64_t> { static const ArrayType type = ArrayType::INT64; };
template <>
struct GetArrayType<uint8_t> { static const ArrayType type = ArrayType::UINT8; };
template <>
struct GetArrayType<uint16_t> { static const ArrayType type = ArrayType::UINT16; };
template <>
struct GetArrayType<uint32_t> { static const ArrayType type = ArrayType::UINT32; };
template <>
struct GetArrayType<uint64_t> { static const ArrayType type = ArrayType::UINT64; };
template <>
struct GetArrayType<std::complex<int8_t>> { static const ArrayType type = ArrayType::COMPLEX_INT8; };
template <>
struct GetArrayType<std::complex<int16_t>> { static const ArrayType type = ArrayType::COMPLEX_INT16; };
template <>
struct GetArrayType<std::complex<int32_t>> { static const ArrayType type = ArrayType::COMPLEX_INT32; };
template <>
struct GetArrayType<std::complex<int64_t>> { static const ArrayType type = ArrayType::COMPLEX_INT64; };
template <>
struct GetArrayType<std::complex<uint8_t>> { static const ArrayType type = ArrayType::COMPLEX_UINT8; };
template <>
struct GetArrayType<std::complex<uint16_t>> { static const ArrayType type = ArrayType::COMPLEX_UINT16; };
template <>
struct GetArrayType<std::complex<uint32_t>> { static const ArrayType type = ArrayType::COMPLEX_UINT32; };
template <>
struct GetArrayType<std::complex<uint64_t>> { static const ArrayType type = ArrayType::COMPLEX_UINT64; };
template <>
struct GetArrayType<std::complex<double>> { static const ArrayType type = ArrayType::COMPLEX_DOUBLE; };
template <>
struct GetArrayType<std::complex<float>> { static const ArrayType type = ArrayType::COMPLEX_SINGLE; };
template <>
struct GetArrayType<Array> { static const ArrayType type = ArrayType::CELL; };
template <>
struct GetArrayType<Struct> { static const ArrayType type = ArrayType::STRUCT; };
template <>
struct GetArrayType<Enumeration> { static const ArrayType type = ArrayType::ENUM; };
template <>
struct GetArrayType<Object> { static const ArrayType type = ArrayType::OBJECT; };
template <>
struct GetArrayType<SparseArray<bool>> { static const ArrayType type = ArrayType::SPARSE_LOGICAL; };
template <>
struct GetArrayType<SparseArray<double>> { static const ArrayType type = ArrayType::SPARSE_DOUBLE; };
template <>
struct GetArrayType<SparseArray<float>> { static const ArrayType type = ArrayType::SPARSE_SINGLE; };
template <>
struct GetArrayType<SparseArray<std::complex<double>>> { static const ArrayType type = ArrayType::SPARSE_COMPLEX_DOUBLE; };
template <>
struct GetArrayType<SparseArray<std::complex<float>>> { static const ArrayType type = ArrayType::SPARSE_COMPLEX_SINGLE; };

template <typename T>
struct GetSparseArrayType;
template <>
struct GetSparseArrayType<bool> { static const ArrayType type = ArrayType::SPARSE_LOGICAL; };
template <>
struct GetSparseArrayType<double> { static const ArrayType type = ArrayType::SPARSE_DOUBLE; };
template <>
struct GetSparseArrayType<float> { static const ArrayType type = ArrayType::SPARSE_SINGLE; };
template <>
struct GetSparseArrayType<std::complex<double>> { static const ArrayType type = ArrayType::SPARSE_COMPLEX_DOUBLE; };
template <>
struct GetSparseArrayType<std::complex<float>> { static const ArrayType type = ArrayType::SPARSE_COMPLEX_SINGLE; };

/// The element type an array made from values of type T holds.
template <typename T>
struct GetReturnType { typedef T type; };
template <>
struct GetReturnType<String> { typedef MATLABString type; };
template <>
struct GetReturnType<std::string> { typedef MATLABString type; };
template <>
struct GetReturnType<const char*> { typedef MATLABString type; };
template <>
struct GetReturnType<const char16_t*> { typedef MATLABString type; };
template <>
struct GetReturnType<char> { typedef char16_t type; };

namespace detail {

/// Whether T is kept as numbers in memory of its own type (a number, a logical, a character, or a complex number).
template <typename T>
struct IsDense
    : std::integral_constant<bool, std::is_arithmetic<typename std::remove_const<T>::type>::value ||
                                       is_complex<typename std::remove_const<T>::type>::value> {};

/// What a const iterator's element is read as: an enumeration's member as its name.
template <typename T>
struct GetConstRefType { typedef T type; };
template <>
struct GetConstRefType<Enumeration> { typedef std::string type; };

template <typename T>
inline void DeleteArray(T* data) {
    delete[] data;
}

} // namespace detail

// --- Objects -------------------------------------------------------------------------------------

namespace impl {

/// A value the engine keeps for the program: released when the last array holding it lets it go.
class EngineValue {
public:
    EngineValue(uint64_t handle, void (*release)(uint64_t)) noexcept : fHandle(handle), fRelease(release) {}
    ~EngineValue() {
        if (fRelease != nullptr && fHandle != 0) {
            fRelease(fHandle);
        }
    }
    EngineValue(const EngineValue&) = delete;
    EngineValue& operator=(const EngineValue&) = delete;
    uint64_t handle() const noexcept { return fHandle; }

private:
    uint64_t fHandle;
    void (*fRelease)(uint64_t);
};

/// One element of a MATLAB object array the engine holds: the array, and the element's place in it.
class ObjectImpl {
public:
    ObjectImpl(std::shared_ptr<EngineValue> value, size_t index, ArrayType type, std::string className)
        : value(std::move(value)), index(index), type(type), className(std::move(className)) {}
    std::shared_ptr<EngineValue> value;
    size_t index;
    ArrayType type;
    std::string className;
};

} // namespace impl

/// One element of an object array: a MATLAB object the engine keeps, which the program passes back.
class Object {
public:
    virtual ~Object() noexcept {}
    Object(Object&& rhs) noexcept : pImpl(std::move(rhs.pImpl)) {}
    Object& operator=(Object&& rhs) noexcept {
        pImpl = std::move(rhs.pImpl);
        return *this;
    }
    Object(const Object& rhs) noexcept : pImpl(rhs.pImpl) {}
    Object& operator=(const Object& rhs) noexcept {
        pImpl = rhs.pImpl;
        return *this;
    }

protected:
    friend class detail::Access;
    std::shared_ptr<impl::ObjectImpl> pImpl;
    Object() noexcept {}
    Object(std::shared_ptr<impl::ObjectImpl> impl) noexcept : pImpl(std::move(impl)) {}
};

// --- Array ---------------------------------------------------------------------------------------

/// An array of any type: what a function takes and gives back. Copies share the elements until one changes them.
class Array {
public:
    static const ArrayType type = ArrayType::UNKNOWN;

    Array() noexcept {}
    virtual ~Array() noexcept { Leave(); }
    Array(Array&& rhs) noexcept : pImpl(std::move(rhs.pImpl)) {}
    Array& operator=(Array&& rhs) noexcept {
        if (this != &rhs) {
            Leave();
            pImpl = std::move(rhs.pImpl);
        }
        return *this;
    }
    Array(const Array& rhs) noexcept : pImpl(rhs.pImpl) { Join(); }
    Array& operator=(const Array& rhs) noexcept {
        if (this != &rhs) {
            std::shared_ptr<impl::ArrayImpl> held = rhs.pImpl;
            Leave();
            pImpl = std::move(held);
            Join();
        }
        return *this;
    }

    ArrayType getType() const;
    MemoryLayout getMemoryLayout() const;
    ArrayDimensions getDimensions() const noexcept;
    size_t getNumberOfElements() const noexcept;
    bool isEmpty() const noexcept;
    ArrayElementRef<false> operator[](size_t idx);
    ArrayElementRef<true> operator[](size_t idx) const;

protected:
    explicit Array(std::shared_ptr<impl::ArrayImpl> impl) noexcept : pImpl(std::move(impl)) { Join(); }
    std::shared_ptr<impl::ArrayImpl> pImpl;
    friend class detail::Access;
    template <typename T>
    friend Range<TypedIterator, T> getWritableElements(Array& a);

private:
    void Join() noexcept;
    void Leave() noexcept;
};

namespace impl {

/// How the elements of one numeric type are made, copied and freed.
struct DenseKind {
    size_t size;
    void* (*make)(size_t count);
    void (*destroy)(void* data, void (*deleter)());
    void (*deleteArray)();
};

template <typename T>
inline void* MakeDense(size_t count) {
    return count == 0 ? nullptr : new T[count]();
}

template <typename T>
inline void DestroyDense(void* data, void (*deleter)()) {
    if (data != nullptr && deleter != nullptr) {
        reinterpret_cast<void (*)(T*)>(deleter)(static_cast<T*>(data));
    }
}

template <typename T>
inline const DenseKind* KindOf() {
    static const DenseKind kind = {sizeof(T), &MakeDense<T>, &DestroyDense<T>,
                                   reinterpret_cast<void (*)()>(&detail::DeleteArray<T>)};
    return &kind;
}

/// An array's storage: its type and size, and its elements as the type keeps them.
class ArrayImpl {
public:
    ArrayType type = ArrayType::UNKNOWN;
    ArrayDimensions dims;
    MemoryLayout layout = MemoryLayout::COLUMN_MAJOR;

    /// How many Array objects hold this storage; one that changes it first takes a copy when it is shared.
    std::atomic<long> arrays{0};

    // Numbers, characters and logicals, and a sparse array's stored values: count elements of one
    // type, freed by the deleter of the buffer they came in.
    const DenseKind* dense = nullptr;
    void* data = nullptr;
    size_t count = 0;
    void (*deleter)() = nullptr;

    std::vector<MATLABString> strings;      // a string array's elements
    std::vector<Array> elements;            // a cell's elements, or each struct element's fields in turn
    std::vector<std::string> fields;        // a struct array's field names
    std::string className;                  // an enumeration's class
    std::vector<std::string> members;       // an enumeration's member names, element by element
    std::vector<Object> objects;            // an object array's elements
    std::vector<size_t> rows;               // a sparse array's: each stored value's row
    std::vector<size_t> columnStarts;       // and where each column's values start, with one more at the end

    ArrayImpl() = default;
    ArrayImpl(const ArrayImpl&) = delete;
    ArrayImpl& operator=(const ArrayImpl&) = delete;
    ~ArrayImpl() {
        if (dense != nullptr) {
            dense->destroy(data, deleter);
        }
    }

    /// Storage of its own with the same elements.
    std::shared_ptr<ArrayImpl> Clone() const {
        std::shared_ptr<ArrayImpl> copy = std::make_shared<ArrayImpl>();
        copy->type = type;
        copy->dims = dims;
        copy->layout = layout;
        copy->dense = dense;
        copy->count = count;
        if (dense != nullptr) {
            copy->data = dense->make(count);
            copy->deleter = dense->deleteArray;
            if (count != 0) {
                std::memcpy(copy->data, data, count * dense->size);
            }
        }
        copy->strings = strings;
        copy->elements = elements;
        copy->fields = fields;
        copy->className = className;
        copy->members = members;
        copy->objects = objects;
        copy->rows = rows;
        copy->columnStarts = columnStarts;
        return copy;
    }
};

} // namespace impl

inline void Array::Join() noexcept {
    if (pImpl) {
        ++pImpl->arrays;
    }
}

inline void Array::Leave() noexcept {
    if (pImpl) {
        --pImpl->arrays;
    }
}

namespace detail {

/// Where a reference points: an element of the array whose storage owner is, named by its
/// subscripts (or by its place in storage, for an iterator's), and for a struct element a field;
/// or, for an iterator over one struct element's fields, a place in the owner's elements itself.
struct RefImpl {
    std::shared_ptr<impl::ArrayImpl> owner;
    std::vector<size_t> indices;
    bool position = false;
    bool slot = false;
    bool hasField = false;
    std::string field;
};

/// The way in to what the API's classes keep from their users.
class Access {
public:
    template <typename T>
    static T createObj(std::shared_ptr<impl::ArrayImpl> impl) {
        return T(std::move(impl));
    }
    static const std::shared_ptr<impl::ArrayImpl>& getImpl(const Array& a) noexcept { return a.pImpl; }

    /// The array's storage, its own: copied first when other arrays share it.
    static impl::ArrayImpl& writable(Array& a) {
        if (!a.pImpl) {
            throw InvalidArrayTypeException("Can't convert the Array to this TypedArray");
        }
        if (a.pImpl->arrays > 1) {
            std::shared_ptr<impl::ArrayImpl> copy = a.pImpl->Clone();
            a.Leave();
            a.pImpl = std::move(copy);
            a.Join();
        }
        return *a.pImpl;
    }

    template <typename T>
    static T createRef(std::shared_ptr<RefImpl> ref) {
        return T(std::move(ref));
    }
    template <typename T>
    static TypedIterator<T> createIterator(std::shared_ptr<impl::ArrayImpl> owner, size_t place) {
        return TypedIterator<T>(std::move(owner), place);
    }
    template <typename T>
    static const std::shared_ptr<RefImpl>& getRef(const T& ref) noexcept {
        return ref.pImpl;
    }

    static const std::shared_ptr<impl::ObjectImpl>& getImpl(const Object& o) noexcept { return o.pImpl; }
    static Object createObject(std::shared_ptr<impl::ObjectImpl> impl) { return Object(std::move(impl)); }
};

// --- Messages --------------------------------------------------------------------------------------

[[noreturn]] inline void ThrowTypedArray() { throw InvalidArrayTypeException("Can't convert the Array to this TypedArray"); }
[[noreturn]] inline void ThrowMismatch() { throw TypeMismatchException("Data type mismatch."); }
[[noreturn]] inline void ThrowNotAscii() { throw NonAsciiCharInInputDataException("Input data can only contain ASCII characters"); }

// --- Storage -------------------------------------------------------------------------------------

inline const impl::DenseKind* DenseFor(ArrayType type) {
    switch (type) {
    case ArrayType::LOGICAL:
    case ArrayType::SPARSE_LOGICAL:
        return impl::KindOf<bool>();
    case ArrayType::CHAR:
        return impl::KindOf<char16_t>();
    case ArrayType::DOUBLE:
    case ArrayType::SPARSE_DOUBLE:
        return impl::KindOf<double>();
    case ArrayType::SINGLE:
    case ArrayType::SPARSE_SINGLE:
        return impl::KindOf<float>();
    case ArrayType::INT8: return impl::KindOf<int8_t>();
    case ArrayType::UINT8: return impl::KindOf<uint8_t>();
    case ArrayType::INT16: return impl::KindOf<int16_t>();
    case ArrayType::UINT16: return impl::KindOf<uint16_t>();
    case ArrayType::INT32: return impl::KindOf<int32_t>();
    case ArrayType::UINT32: return impl::KindOf<uint32_t>();
    case ArrayType::INT64: return impl::KindOf<int64_t>();
    case ArrayType::UINT64: return impl::KindOf<uint64_t>();
    case ArrayType::COMPLEX_DOUBLE:
    case ArrayType::SPARSE_COMPLEX_DOUBLE:
        return impl::KindOf<std::complex<double>>();
    case ArrayType::COMPLEX_SINGLE:
    case ArrayType::SPARSE_COMPLEX_SINGLE:
        return impl::KindOf<std::complex<float>>();
    case ArrayType::COMPLEX_INT8: return impl::KindOf<std::complex<int8_t>>();
    case ArrayType::COMPLEX_UINT8: return impl::KindOf<std::complex<uint8_t>>();
    case ArrayType::COMPLEX_INT16: return impl::KindOf<std::complex<int16_t>>();
    case ArrayType::COMPLEX_UINT16: return impl::KindOf<std::complex<uint16_t>>();
    case ArrayType::COMPLEX_INT32: return impl::KindOf<std::complex<int32_t>>();
    case ArrayType::COMPLEX_UINT32: return impl::KindOf<std::complex<uint32_t>>();
    case ArrayType::COMPLEX_INT64: return impl::KindOf<std::complex<int64_t>>();
    case ArrayType::COMPLEX_UINT64: return impl::KindOf<std::complex<uint64_t>>();
    default:
        return nullptr;
    }
}

inline bool IsSparse(ArrayType type) {
    return type == ArrayType::SPARSE_LOGICAL || type == ArrayType::SPARSE_DOUBLE || type == ArrayType::SPARSE_COMPLEX_DOUBLE ||
           type == ArrayType::SPARSE_SINGLE || type == ArrayType::SPARSE_COMPLEX_SINGLE;
}

inline bool IsObject(ArrayType type) {
    return type == ArrayType::OBJECT || type == ArrayType::VALUE_OBJECT || type == ArrayType::HANDLE_OBJECT_REF;
}

/// Dimensions as the factory keeps them: one size is a column of that many, anything else as given.
inline ArrayDimensions Shape(ArrayDimensions dims) {
    if (dims.size() == 1) {
        dims.push_back(1);
    }
    return dims;
}

/// The empty double, [], which a new cell's elements and struct's fields hold.
inline Array EmptyDouble();

/// New storage of a type and size, every element its type's default: zero, false, a NUL, a
/// missing string, [] for a cell element or struct field, an empty member name.
inline std::shared_ptr<impl::ArrayImpl> NewStorage(ArrayType type, ArrayDimensions dims, size_t fieldCount = 0) {
    std::shared_ptr<impl::ArrayImpl> a = std::make_shared<impl::ArrayImpl>();
    a->type = type;
    a->dims = std::move(dims);
    size_t n = getNumElements(a->dims);
    if (const impl::DenseKind* kind = IsSparse(type) ? nullptr : DenseFor(type)) {
        a->dense = kind;
        a->count = n;
        a->data = kind->make(n);
        a->deleter = kind->deleteArray;
        return a;
    }
    switch (type) {
    case ArrayType::MATLAB_STRING:
        a->strings.resize(n);
        break;
    case ArrayType::CELL:
        a->elements.reserve(n);
        for (size_t k = 0; k < n; k++) {
            a->elements.push_back(EmptyDouble());
        }
        break;
    case ArrayType::STRUCT:
        a->elements.reserve(n * fieldCount);
        for (size_t k = 0; k < n * fieldCount; k++) {
            a->elements.push_back(EmptyDouble());
        }
        break;
    case ArrayType::ENUM:
        a->members.resize(n);
        break;
    case ArrayType::OBJECT:
    case ArrayType::VALUE_OBJECT:
    case ArrayType::HANDLE_OBJECT_REF:
        break;
    default:
        if (IsSparse(type)) {
            a->dense = DenseFor(type);
            a->deleter = a->dense->deleteArray;
            a->columnStarts.assign(a->dims.size() > 1 ? a->dims[1] + 1 : 1, 0);
        }
        break;
    }
    return a;
}

inline Array EmptyDouble() {
    return Access::createObj<Array>(NewStorage(ArrayType::DOUBLE, ArrayDimensions{0, 0}));
}

template <typename T>
inline T* Elements(const impl::ArrayImpl& a) {
    return static_cast<T*>(a.data);
}

/// Whether at most one dimension is other than 1, so a single index counts along it.
inline bool IsVectorShape(const ArrayDimensions& dims) {
    size_t longer = 0;
    for (size_t d : dims) {
        if (d != 1) {
            longer++;
        }
    }
    return longer <= 1;
}

/// The place in storage of the element these subscripts name, as R2025b's API reads them (local:
/// index.*): one index for a vector or scalar, at least one per dimension that is not a singleton
/// otherwise, extra trailing subscripts 0, row-major storage read by row.
inline size_t Place(const impl::ArrayImpl& a, const std::vector<size_t>& indices) {
    size_t n = getNumElements(a.dims);
    if (n == 0) {
        throw CantIndexIntoEmptyArrayException("Can't index into an empty array.");
    }
    if (indices.size() == 1) {
        if (!IsVectorShape(a.dims)) {
            throw NotEnoughIndicesProvidedException("Not enough indices provided.");
        }
        if (indices[0] >= n) {
            throw InvalidArrayIndexException("Array index is invalid.");
        }
        return indices[0];
    }
    size_t nd = a.dims.size();
    for (size_t k = indices.size(); k < nd; k++) {
        if (a.dims[k] != 1) {
            throw NotEnoughIndicesProvidedException("Not enough indices provided.");
        }
    }
    for (size_t k = 0; k < indices.size(); k++) {
        if (k < nd ? indices[k] >= a.dims[k] : indices[k] != 0) {
            if (k < nd) {
                throw InvalidArrayIndexException("Array index is invalid.");
            }
            throw TooManyIndicesProvidedException("Too many indices provided.");
        }
    }
    size_t place = 0;
    size_t stride = 1;
    if (a.layout == MemoryLayout::ROW_MAJOR) {
        for (size_t k = nd; k-- > 0;) {
            if (k < indices.size()) {
                place += indices[k] * stride;
            }
            stride *= a.dims[k];
        }
    } else {
        for (size_t k = 0; k < nd; k++) {
            if (k < indices.size()) {
                place += indices[k] * stride;
            }
            stride *= a.dims[k];
        }
    }
    return place;
}

inline size_t Place(const RefImpl& r) {
    return r.position ? r.indices[0] : Place(*r.owner, r.indices);
}

inline size_t FieldIndex(const impl::ArrayImpl& a, const std::string& name) {
    for (size_t f = 0; f < a.fields.size(); f++) {
        if (a.fields[f] == name) {
            return f;
        }
    }
    throw InvalidFieldNameException("Field does not exist in this struct.");
}

/// The Array a reference names: a cell's element, or a struct element's field.
inline Array& Slot(const RefImpl& r) {
    impl::ArrayImpl& a = *r.owner;
    if (r.slot) {
        return a.elements[r.indices[0]];
    }
    if (a.type == ArrayType::CELL && !r.hasField) {
        return a.elements[Place(r)];
    }
    if (a.type == ArrayType::STRUCT && r.hasField) {
        size_t place = Place(r);
        return a.elements[place * a.fields.size() + FieldIndex(a, r.field)];
    }
    if (r.hasField) {
        throw StringIndexNotValidException("String index not valid for this array.");
    }
    ThrowMismatch();
}

/// The storage of the Array a reference names, its own when the reference writes through it.
inline std::shared_ptr<impl::ArrayImpl> SlotStorage(const RefImpl& r, bool writable) {
    Array& slot = Slot(r);
    if (writable) {
        Access::writable(slot);
    }
    return Access::getImpl(slot);
}

inline std::shared_ptr<RefImpl> NewRef(std::shared_ptr<impl::ArrayImpl> owner, size_t index) {
    std::shared_ptr<RefImpl> r = std::make_shared<RefImpl>();
    r->owner = std::move(owner);
    r->indices.push_back(index);
    return r;
}

inline std::shared_ptr<RefImpl> PlaceRef(std::shared_ptr<impl::ArrayImpl> owner, size_t place) {
    std::shared_ptr<RefImpl> r = NewRef(std::move(owner), place);
    r->position = true;
    return r;
}

/// A reference one index further on: the next subscript, after a field name refused.
inline std::shared_ptr<RefImpl> Then(const RefImpl& r, size_t index) {
    if (r.hasField) {
        throw StringIndexMustBeLastException("String index must be last.");
    }
    std::shared_ptr<RefImpl> next = std::make_shared<RefImpl>(r);
    next->indices.push_back(index);
    return next;
}

inline std::shared_ptr<RefImpl> Then(const RefImpl& r, std::string field) {
    if (r.hasField) {
        throw CanOnlyUseOneStringIndexException("Can only provide one string index.");
    }
    if (r.owner->type != ArrayType::STRUCT) {
        throw StringIndexNotValidException("String index not valid for this array.");
    }
    std::shared_ptr<RefImpl> next = std::make_shared<RefImpl>(r);
    next->hasField = true;
    next->field = std::move(field);
    return next;
}

// --- Text ----------------------------------------------------------------------------------------

inline std::string AsciiOf(const String& text) {
    std::string out;
    out.reserve(text.size());
    for (char16_t c : text) {
        if (c > 127) {
            throw NonAsciiCharInRequestedAsciiOutputException("Can't create a std::string from this data.");
        }
        out.push_back(static_cast<char>(c));
    }
    return out;
}

/// A string element as std::string: ASCII only, as R2025b's API reads one (local: string.std.*).
inline std::string AsciiElement(const String& text) {
    std::string out;
    out.reserve(text.size());
    for (char16_t c : text) {
        if (c > 127) {
            ThrowNotAscii();
        }
        out.push_back(static_cast<char>(c));
    }
    return out;
}

inline String FromAscii(const std::string& text) {
    String out;
    out.reserve(text.size());
    for (char c : text) {
        if (static_cast<unsigned char>(c) > 127) {
            ThrowNotAscii();
        }
        out.push_back(static_cast<char16_t>(static_cast<unsigned char>(c)));
    }
    return out;
}

inline std::string Utf8Of(const String& text) {
    std::string out;
    for (size_t i = 0; i < text.size(); i++) {
        uint32_t c = text[i];
        if (c >= 0xD800 && c < 0xDC00 && i + 1 < text.size() && text[i + 1] >= 0xDC00 && text[i + 1] < 0xE000) {
            c = 0x10000 + ((c - 0xD800) << 10) + (text[i + 1] - 0xDC00);
            i++;
        }
        if (c < 0x80) {
            out.push_back(static_cast<char>(c));
        } else if (c < 0x800) {
            out.push_back(static_cast<char>(0xC0 | (c >> 6)));
            out.push_back(static_cast<char>(0x80 | (c & 0x3F)));
        } else if (c < 0x10000) {
            out.push_back(static_cast<char>(0xE0 | (c >> 12)));
            out.push_back(static_cast<char>(0x80 | ((c >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (c & 0x3F)));
        } else {
            out.push_back(static_cast<char>(0xF0 | (c >> 18)));
            out.push_back(static_cast<char>(0x80 | ((c >> 12) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | ((c >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (c & 0x3F)));
        }
    }
    return out;
}

/// UTF-8 read as UTF-16; a sequence that does not decode is dropped, as R2025b's factory drops it (local: char.utf8.bad).
inline String FromUtf8(const std::string& text) {
    String out;
    size_t i = 0;
    while (i < text.size()) {
        unsigned char b = static_cast<unsigned char>(text[i]);
        uint32_t c;
        size_t more;
        if (b < 0x80) {
            c = b;
            more = 0;
        } else if ((b & 0xE0) == 0xC0) {
            c = b & 0x1F;
            more = 1;
        } else if ((b & 0xF0) == 0xE0) {
            c = b & 0x0F;
            more = 2;
        } else if ((b & 0xF8) == 0xF0) {
            c = b & 0x07;
            more = 3;
        } else {
            i++;
            continue;
        }
        if (i + more >= text.size() + (more == 0 ? 1 : 0) && more != 0 && i + more > text.size() - 1) {
            break;
        }
        bool ok = true;
        for (size_t k = 1; k <= more; k++) {
            unsigned char t = static_cast<unsigned char>(text[i + k]);
            if ((t & 0xC0) != 0x80) {
                ok = false;
                break;
            }
            c = (c << 6) | (t & 0x3F);
        }
        if (!ok) {
            i++;
            continue;
        }
        i += more + 1;
        if (c >= 0x10000) {
            c -= 0x10000;
            out.push_back(static_cast<char16_t>(0xD800 + (c >> 10)));
            out.push_back(static_cast<char16_t>(0xDC00 + (c & 0x3FF)));
        } else {
            out.push_back(static_cast<char16_t>(c));
        }
    }
    return out;
}

// --- Reading and writing one element ----------------------------------------------------------------

/// The element a reference names, converted to T: a number of any numeric type read as T (local:
/// index.generic.mismatch), text, a cell's or field's Array, a struct element, an object.
template <typename T>
struct Reader;

template <typename T, typename U>
inline T NumberAs(const U& value, std::false_type /*complex target*/) {
    return static_cast<T>(value);
}

template <typename T, typename U>
inline T NumberAs(const U& value, std::true_type /*complex target*/) {
    return T(static_cast<typename T::value_type>(value), 0);
}

template <typename T, typename U>
inline T ComplexAs(const std::complex<U>& value, std::false_type /*complex target*/) {
    (void)value;
    ThrowMismatch();
}

template <typename T, typename U>
inline T ComplexAs(const std::complex<U>& value, std::true_type /*complex target*/) {
    return T(static_cast<typename T::value_type>(value.real()), static_cast<typename T::value_type>(value.imag()));
}

template <typename T>
inline T ReadNumber(const impl::ArrayImpl& a, size_t place) {
    typedef typename is_complex<T>::type Complex;
    switch (a.type) {
    case ArrayType::LOGICAL: return NumberAs<T>(Elements<bool>(a)[place], Complex());
    case ArrayType::CHAR: return NumberAs<T>(Elements<char16_t>(a)[place], Complex());
    case ArrayType::DOUBLE: return NumberAs<T>(Elements<double>(a)[place], Complex());
    case ArrayType::SINGLE: return NumberAs<T>(Elements<float>(a)[place], Complex());
    case ArrayType::INT8: return NumberAs<T>(Elements<int8_t>(a)[place], Complex());
    case ArrayType::UINT8: return NumberAs<T>(Elements<uint8_t>(a)[place], Complex());
    case ArrayType::INT16: return NumberAs<T>(Elements<int16_t>(a)[place], Complex());
    case ArrayType::UINT16: return NumberAs<T>(Elements<uint16_t>(a)[place], Complex());
    case ArrayType::INT32: return NumberAs<T>(Elements<int32_t>(a)[place], Complex());
    case ArrayType::UINT32: return NumberAs<T>(Elements<uint32_t>(a)[place], Complex());
    case ArrayType::INT64: return NumberAs<T>(Elements<int64_t>(a)[place], Complex());
    case ArrayType::UINT64: return NumberAs<T>(Elements<uint64_t>(a)[place], Complex());
    case ArrayType::COMPLEX_DOUBLE: return ComplexAs<T>(Elements<std::complex<double>>(a)[place], Complex());
    case ArrayType::COMPLEX_SINGLE: return ComplexAs<T>(Elements<std::complex<float>>(a)[place], Complex());
    case ArrayType::COMPLEX_INT8: return ComplexAs<T>(Elements<std::complex<int8_t>>(a)[place], Complex());
    case ArrayType::COMPLEX_UINT8: return ComplexAs<T>(Elements<std::complex<uint8_t>>(a)[place], Complex());
    case ArrayType::COMPLEX_INT16: return ComplexAs<T>(Elements<std::complex<int16_t>>(a)[place], Complex());
    case ArrayType::COMPLEX_UINT16: return ComplexAs<T>(Elements<std::complex<uint16_t>>(a)[place], Complex());
    case ArrayType::COMPLEX_INT32: return ComplexAs<T>(Elements<std::complex<int32_t>>(a)[place], Complex());
    case ArrayType::COMPLEX_UINT32: return ComplexAs<T>(Elements<std::complex<uint32_t>>(a)[place], Complex());
    case ArrayType::COMPLEX_INT64: return ComplexAs<T>(Elements<std::complex<int64_t>>(a)[place], Complex());
    case ArrayType::COMPLEX_UINT64: return ComplexAs<T>(Elements<std::complex<uint64_t>>(a)[place], Complex());
    default:
        ThrowMismatch();
    }
}

template <typename T, bool IsArray = std::is_base_of<Array, T>::value>
struct ArrayReader {
    static T Read(const RefImpl& r) { return T(Slot(r)); }
};

template <typename T>
struct ArrayReader<T, false> {
    static T Read(const RefImpl& r) {
        const impl::ArrayImpl& a = *r.owner;
        if (r.hasField) {
            ThrowMismatch();
        }
        return ReadNumber<T>(a, Place(r));
    }
};

template <typename T>
struct Reader {
    static T Read(const RefImpl& r) { return ArrayReader<T>::Read(r); }
};

template <>
struct Reader<std::string> {
    static std::string Read(const RefImpl& r) {
        const impl::ArrayImpl& a = *r.owner;
        if (a.type == ArrayType::MATLAB_STRING) {
            const MATLABString& s = a.strings[Place(r)];
            if (!s.has_value()) {
                throw std::runtime_error("Missing string");
            }
            return AsciiElement(*s);
        }
        if (a.type == ArrayType::ENUM) {
            return a.members[Place(r)];
        }
        ThrowMismatch();
    }
};

template <>
struct Reader<String> {
    static String Read(const RefImpl& r) {
        const impl::ArrayImpl& a = *r.owner;
        if (a.type != ArrayType::MATLAB_STRING) {
            ThrowMismatch();
        }
        const MATLABString& s = a.strings[Place(r)];
        if (!s.has_value()) {
            throw std::runtime_error("Missing string value");
        }
        return *s;
    }
};

template <>
struct Reader<MATLABString> {
    static MATLABString Read(const RefImpl& r) {
        const impl::ArrayImpl& a = *r.owner;
        if (a.type != ArrayType::MATLAB_STRING) {
            ThrowMismatch();
        }
        return a.strings[Place(r)];
    }
};

template <>
struct Reader<Object> {
    static Object Read(const RefImpl& r) {
        const impl::ArrayImpl& a = *r.owner;
        if (!IsObject(a.type)) {
            ThrowMismatch();
        }
        return a.objects[Place(r)];
    }
};

template <>
struct Reader<Enumeration> {
    static Enumeration Read(const RefImpl& r) {
        if (r.owner->type != ArrayType::ENUM) {
            ThrowMismatch();
        }
        Place(r);
        return Enumeration();
    }
};

template <>
struct Reader<Struct> {
    static Struct Read(const RefImpl& r);
};

template <typename T>
inline T Read(const RefImpl& r) {
    return Reader<typename std::remove_cv<T>::type>::Read(r);
}

/// Writes a number into an array of exactly its type: an int into a double array is refused (local: index.generic.write.mismatch).
template <typename T>
inline void WriteNumber(const RefImpl& r, const T& value) {
    impl::ArrayImpl& a = *r.owner;
    if (a.type != GetArrayType<T>::type || r.hasField) {
        ThrowMismatch();
    }
    Elements<T>(a)[Place(r)] = value;
}

template <typename T, bool IsArray = std::is_base_of<Array, T>::value, bool IsNumber = IsDense<T>::value>
struct Writer {
    static void Write(const RefImpl& r, const T& value) {
        (void)r;
        (void)value;
        ThrowMismatch();
    }
};

template <typename T>
struct Writer<T, false, true> {
    static void Write(const RefImpl& r, const T& value) { WriteNumber<T>(r, value); }
};

template <typename T>
struct Writer<T, true, false> {
    static void Write(const RefImpl& r, const T& value) { Slot(r) = value; }
};

inline void WriteString(const RefImpl& r, MATLABString value) {
    impl::ArrayImpl& a = *r.owner;
    if (a.type != ArrayType::MATLAB_STRING || r.hasField) {
        ThrowMismatch();
    }
    a.strings[Place(r)] = std::move(value);
}

template <>
struct Writer<std::string, false, false> {
    static void Write(const RefImpl& r, const std::string& value) {
        if (r.owner->type == ArrayType::ENUM) {
            r.owner->members[Place(r)] = value;
            return;
        }
        WriteString(r, MATLABString(FromAscii(value)));
    }
};

template <>
struct Writer<String, false, false> {
    static void Write(const RefImpl& r, const String& value) { WriteString(r, MATLABString(value)); }
};

template <>
struct Writer<MATLABString, false, false> {
    static void Write(const RefImpl& r, const MATLABString& value) { WriteString(r, value); }
};

template <>
struct Writer<Object, false, false> {
    static void Write(const RefImpl& r, const Object& value) {
        impl::ArrayImpl& a = *r.owner;
        if (!IsObject(a.type)) {
            ThrowMismatch();
        }
        a.objects[Place(r)] = value;
    }
};

template <typename T>
inline void Write(const RefImpl& r, const T& value) {
    Writer<typename std::remove_cv<T>::type>::Write(r, value);
}

inline void Write(const RefImpl& r, const char* value) {
    Write(r, std::string(value));
}

inline void Write(const RefImpl& r, const char16_t* value) {
    Write(r, String(value));
}

} // namespace detail

// --- References ----------------------------------------------------------------------------------

/// What every reference holds: where it points.
class ReferenceHolder {
protected:
    std::shared_ptr<detail::RefImpl> pImpl;
    friend class detail::Access;
    ReferenceHolder(std::shared_ptr<detail::RefImpl> impl) noexcept : pImpl(std::move(impl)) {}
    ReferenceHolder(ReferenceHolder&& rhs) noexcept : pImpl(std::move(rhs.pImpl)) {}
    ReferenceHolder& operator=(ReferenceHolder&& rhs) noexcept {
        pImpl = std::move(rhs.pImpl);
        return *this;
    }
    ReferenceHolder(const ReferenceHolder& rhs) noexcept : pImpl(rhs.pImpl) {}
    ReferenceHolder& operator=(const ReferenceHolder& rhs) noexcept {
        pImpl = rhs.pImpl;
        return *this;
    }
};

/// A reference to an Array held in a cell or a struct field: its type and size read through it.
class ArrayReferenceExt : public ReferenceHolder {
public:
    ArrayType getType() const;
    ArrayDimensions getDimensions() const;
    size_t getNumberOfElements() const;
    bool isEmpty() const;

protected:
    friend class detail::Access;
    ArrayReferenceExt(std::shared_ptr<detail::RefImpl> impl) noexcept : ReferenceHolder(std::move(impl)) {}
    ArrayReferenceExt(ArrayReferenceExt&& rhs) noexcept : ReferenceHolder(std::move(rhs)) {}
    ArrayReferenceExt& operator=(ArrayReferenceExt&& rhs) noexcept {
        ReferenceHolder::operator=(std::move(rhs));
        return *this;
    }
    ArrayReferenceExt(const ArrayReferenceExt& rhs) noexcept : ReferenceHolder(rhs) {}
    ArrayReferenceExt& operator=(const ArrayReferenceExt& rhs) noexcept {
        ReferenceHolder::operator=(rhs);
        return *this;
    }
};

/// A reference to an Array held in a cell or a struct field, indexable in turn.
template <bool is_const_ref>
class IndexableArrayRef : public ArrayReferenceExt {
public:
    ArrayElementRef<is_const_ref> operator[](size_t idx);
    ArrayElementRef<true> operator[](size_t idx) const;

protected:
    IndexableArrayRef(std::shared_ptr<detail::RefImpl> impl) noexcept : ArrayReferenceExt(std::move(impl)) {}
    IndexableArrayRef(IndexableArrayRef&& rhs) noexcept : ArrayReferenceExt(std::move(rhs)) {}
    IndexableArrayRef& operator=(IndexableArrayRef&& rhs) noexcept {
        ArrayReferenceExt::operator=(std::move(rhs));
        return *this;
    }
    IndexableArrayRef(const IndexableArrayRef& rhs) noexcept : ArrayReferenceExt(rhs) {}
    IndexableArrayRef& operator=(const IndexableArrayRef& rhs) noexcept {
        ArrayReferenceExt::operator=(rhs);
        return *this;
    }
};

/// A reference to a string array's element: whether it has a value, and the value.
class MATLABStringReferenceExt : public ReferenceHolder {
public:
    bool has_value() const { return detail::Read<MATLABString>(*pImpl).has_value(); }
    explicit operator bool() const { return has_value(); }
    operator String() const { return detail::Read<String>(*pImpl); }

protected:
    friend class detail::Access;
    MATLABStringReferenceExt(std::shared_ptr<detail::RefImpl> impl) noexcept : ReferenceHolder(std::move(impl)) {}
    MATLABStringReferenceExt(MATLABStringReferenceExt&& rhs) noexcept : ReferenceHolder(std::move(rhs)) {}
    MATLABStringReferenceExt& operator=(MATLABStringReferenceExt&& rhs) noexcept {
        ReferenceHolder::operator=(std::move(rhs));
        return *this;
    }
    MATLABStringReferenceExt(const MATLABStringReferenceExt& rhs) noexcept : ReferenceHolder(rhs) {}
    MATLABStringReferenceExt& operator=(const MATLABStringReferenceExt& rhs) noexcept {
        ReferenceHolder::operator=(rhs);
        return *this;
    }
};

template <typename T>
struct GetReferenceExt { using type = ReferenceHolder; };
template <>
struct GetReferenceExt<Array> { using type = IndexableArrayRef<false>; };
template <>
struct GetReferenceExt<MATLABString> { using type = MATLABStringReferenceExt; };

/// A writable reference to one element of an array: read as its type, assigned a new value.
template <typename T>
class Reference : public GetReferenceExt<T>::type {
public:
    typedef typename std::remove_const<T>::type ref_type;
    using Parent = typename GetReferenceExt<T>::type;
    using Parent::pImpl;

    operator ref_type() const { return detail::Read<ref_type>(*pImpl); }
    Reference<T>& operator=(T rhs) {
        detail::Write(*pImpl, rhs);
        return *this;
    }
    operator std::string() const { return detail::Read<std::string>(*pImpl); }
    Reference<T>& operator=(std::string rhs) {
        detail::Write(*pImpl, rhs);
        return *this;
    }
    Reference<T>& operator=(String rhs) {
        detail::Write(*pImpl, rhs);
        return *this;
    }

    Reference(Reference<T>&& rhs) noexcept : Parent(std::move(rhs)) {}
    Reference<T>& operator=(Reference<T>&& rhs) noexcept {
        Parent::operator=(std::move(rhs));
        return *this;
    }
    Reference(const Reference<T>& rhs) noexcept : Parent(rhs) {}
    Reference<T>& operator=(const Reference<T>& rhs) noexcept {
        Parent::operator=(rhs);
        return *this;
    }

protected:
    friend class detail::Access;
    Reference(std::shared_ptr<detail::RefImpl> impl) noexcept : Parent(std::move(impl)) {}

private:
    Reference() = delete;
};

using ArrayRef = Reference<Array>;

/// A reference to one element of an array of any type (Array::operator[]): indexed further, read as any type its elements convert to, assigned a value of its own type.
template <bool is_const_ref>
class ArrayElementRef {
public:
    ArrayElementRef<is_const_ref> operator[](size_t idx) { return ArrayElementRef<is_const_ref>(detail::Then(*pImpl, idx)); }
    ArrayElementRef<is_const_ref> operator[](std::string idx) { return ArrayElementRef<is_const_ref>(detail::Then(*pImpl, std::move(idx))); }
    template <typename T>
    operator T() const {
        return detail::Read<T>(*pImpl);
    }
    template <typename T>
    ArrayElementRef<is_const_ref>& operator=(T rhs) {
        static_assert(!is_const_ref, "Can't assign to a const reference.");
        detail::Write(*pImpl, rhs);
        return *this;
    }

private:
    std::shared_ptr<detail::RefImpl> pImpl;
    ArrayElementRef(ArrayElementRef<is_const_ref>&& rhs) noexcept : pImpl(std::move(rhs.pImpl)) {}
    ArrayElementRef(std::shared_ptr<detail::RefImpl> impl) noexcept : pImpl(std::move(impl)) {}
    friend class detail::Access;
    friend class Array;
    friend class IndexableArrayRef<true>;
    friend class IndexableArrayRef<false>;
    template <bool>
    friend class ArrayElementRef;
    ArrayElementRef() = delete;
    ArrayElementRef(const ArrayElementRef<is_const_ref>& rhs) = delete;
    ArrayElementRef<is_const_ref>& operator=(const ArrayElementRef<is_const_ref>& rhs) = delete;
    ArrayElementRef<is_const_ref>& operator=(ArrayElementRef<is_const_ref>&& rhs) = delete;
};

template <typename T>
struct GetElementReferenceType { using type = ReferenceHolder; };
template <>
struct GetElementReferenceType<Array> { using type = ArrayReferenceExt; };
template <>
struct GetElementReferenceType<MATLABString> { using type = MATLABStringReferenceExt; };

/// A reference to one element of a typed array (TypedArray<T>::operator[]): a number read as T& to
/// change in place, anything else read and assigned by value; a struct element indexed by field.
template <typename T, bool is_const_ref>
class ArrayElementTypedRef : public GetElementReferenceType<T>::type {
public:
    using Parent = typename GetElementReferenceType<T>::type;
    using Parent::pImpl;
    typedef typename std::conditional<std::is_arithmetic<T>::value && !is_const_ref, T&, T>::type reference;

    ArrayElementTypedRef<T, is_const_ref> operator[](size_t idx) { return ArrayElementTypedRef<T, is_const_ref>(detail::Then(*pImpl, idx)); }
    ArrayElementTypedRef<Array, is_const_ref> operator[](std::string idx) {
        return detail::Access::createRef<ArrayElementTypedRef<Array, is_const_ref>>(detail::Then(*pImpl, std::move(idx)));
    }
    operator reference() const { return Value(std::integral_constant<bool, std::is_arithmetic<T>::value && !is_const_ref>()); }
    operator std::string() const { return detail::Read<std::string>(*pImpl); }
    ArrayElementTypedRef<T, is_const_ref>& operator=(T rhs) {
        detail::Write(*pImpl, rhs);
        return *this;
    }
    ArrayElementTypedRef<T, is_const_ref>& operator=(String rhs) {
        detail::Write(*pImpl, rhs);
        return *this;
    }
    ArrayElementTypedRef<T, is_const_ref>& operator=(std::string rhs) {
        detail::Write(*pImpl, rhs);
        return *this;
    }
    template <typename U>
    operator Reference<U>() const {
        return detail::Access::createRef<Reference<U>>(pImpl);
    }
    template <typename U, typename Type = T, typename = typename std::enable_if<std::is_base_of<Array, Type>::value>::type>
    operator U() const {
        static_assert(std::is_base_of<Array, U>::value, "Can only be used to cast to an Array");
        return U(detail::Slot(*pImpl));
    }
    template <bool is_rhs_const>
    ArrayElementTypedRef<T, is_const_ref>& operator=(ArrayElementTypedRef<T, is_rhs_const>&& rhs) {
        T value = rhs;
        detail::Write(*pImpl, value);
        return *this;
    }

private:
    ArrayElementTypedRef(ArrayElementTypedRef<T, is_const_ref>&& rhs) noexcept : Parent(std::move(rhs.pImpl)) {}
    ArrayElementTypedRef(std::shared_ptr<detail::RefImpl> impl) noexcept : Parent(std::move(impl)) {}
    reference Value(std::true_type) const { return detail::Elements<T>(*pImpl->owner)[detail::Place(*pImpl)]; }
    reference Value(std::false_type) const { return detail::Read<T>(*pImpl); }
    friend class detail::Access;
    friend class TypedArray<T>;
    friend class Reference<TypedArray<T>>;
    template <typename U, bool C>
    friend class ArrayElementTypedRef;
    ArrayElementTypedRef() = delete;
    ArrayElementTypedRef(const ArrayElementTypedRef<T, is_const_ref>& rhs) = delete;
    ArrayElementTypedRef<T, is_const_ref>& operator=(const ArrayElementTypedRef<T, is_const_ref>& rhs) = delete;
};

namespace detail {

/// A comparison of an element: false when the element cannot be read, as R2025b answers for a
/// string element past the end (k12 eq probe; its numeric comparisons end the process there).
template <typename F>
inline bool Compared(F compare) noexcept {
    try {
        return compare();
    } catch (...) {
        return false;
    }
}

} // namespace detail

template <typename T, bool C>
bool operator==(ArrayElementTypedRef<T, C> const& lhs, T const& rhs) noexcept {
    return detail::Compared([&] { return static_cast<T>(lhs) == rhs; });
}
template <typename T, bool C>
bool operator==(T const& lhs, ArrayElementTypedRef<T, C> const& rhs) noexcept {
    return rhs == lhs;
}
template <typename T, bool C>
bool operator==(ArrayElementTypedRef<T, C> const& lhs, ArrayElementTypedRef<T, C> const& rhs) noexcept {
    return detail::Compared([&] { return static_cast<T>(lhs) == static_cast<T>(rhs); });
}
template <bool C>
bool operator==(ArrayElementTypedRef<MATLABString, C> const& lhs, String const& rhs) noexcept {
    return detail::Compared([&] {
        MATLABString s = lhs;
        return s.has_value() && *s == rhs;
    });
}
template <bool C>
bool operator==(String const& lhs, ArrayElementTypedRef<MATLABString, C> const& rhs) noexcept {
    return rhs == lhs;
}
template <bool C>
bool operator==(ArrayElementTypedRef<MATLABString, C> const& lhs, MATLABString const& rhs) noexcept {
    return detail::Compared([&] {
        MATLABString s = lhs;
        return s == rhs;
    });
}
template <bool C>
bool operator==(MATLABString const& lhs, ArrayElementTypedRef<MATLABString, C> const& rhs) noexcept {
    return rhs == lhs;
}
template <bool C>
bool operator==(ArrayElementTypedRef<MATLABString, C> const& lhs, std::string const& rhs) noexcept {
    return detail::Compared([&] {
        MATLABString s = lhs;
        if (!s.has_value() || s->size() != rhs.size()) {
            return false;
        }
        for (size_t k = 0; k < rhs.size(); k++) {
            if ((*s)[k] != static_cast<char16_t>(static_cast<unsigned char>(rhs[k]))) {
                return false;
            }
        }
        return true;
    });
}
template <bool C>
bool operator==(std::string const& lhs, ArrayElementTypedRef<MATLABString, C> const& rhs) noexcept {
    return rhs == lhs;
}
template <typename T1, typename T2>
inline bool operator!=(ArrayElementTypedRef<T1, false> const& lhs, T2 const& rhs) noexcept {
    return !(lhs == rhs);
}
template <typename T1, typename T2>
inline bool operator!=(ArrayElementTypedRef<T1, true> const& lhs, T2 const& rhs) noexcept {
    return !(lhs == rhs);
}

// --- Struct elements -----------------------------------------------------------------------------

/// One element of a struct array, read: its fields by name, and in order.
class Struct {
public:
    typedef TypedIterator<Array const> const_iterator;
    Array operator[](std::string idx) const;
    const_iterator begin() const noexcept;
    const_iterator cbegin() const noexcept;
    const_iterator end() const noexcept;
    const_iterator cend() const noexcept;

private:
    Struct() = delete;
    Struct(std::shared_ptr<detail::RefImpl> impl) noexcept : pImpl(std::move(impl)) {}
    std::shared_ptr<detail::RefImpl> pImpl;
    friend class detail::Access;
    friend struct detail::Reader<Struct>;
    friend class Reference<Struct>;
};

/// One element of a struct array, writable: a field by name is a reference to assign through.
template <>
class Reference<Struct> : public ReferenceHolder {
public:
    typedef TypedIterator<Array> iterator;
    typedef TypedIterator<Array const> const_iterator;
    Reference<Array> operator[](std::string idx);
    Array operator[](std::string idx) const;
    iterator begin() noexcept;
    iterator end() noexcept;
    const_iterator begin() const noexcept;
    const_iterator cbegin() const noexcept;
    const_iterator end() const noexcept;
    const_iterator cend() const noexcept;
    operator Struct() const noexcept;

private:
    friend class detail::Access;
    Reference(std::shared_ptr<detail::RefImpl> impl) noexcept : ReferenceHolder(std::move(impl)) {}
    Reference() = delete;
};

using StructRef = Reference<Struct>;

// --- Iterators -----------------------------------------------------------------------------------

namespace detail {

template <typename T, bool Dense = IsDense<T>::value>
struct IteratorKinds {
    typedef typename std::remove_const<T>::type impl_type;
    typedef T& reference;
    typedef T* pointer;
};

template <typename T>
struct IteratorKinds<T, false> {
    typedef typename std::remove_const<T>::type impl_type;
    typedef typename GetConstRefType<impl_type>::type const_reference;
    typedef typename std::conditional<std::is_const<T>::value, const_reference, Reference<impl_type>>::type reference;
    typedef reference* pointer;
};

} // namespace detail

/// An iterator over an array's elements in storage order: a number's as T&, anything else's as a reference (or, read only, a value).
template <typename T>
class TypedIterator {
public:
    typedef typename detail::IteratorKinds<T>::impl_type impl_type;
    typedef typename detail::IteratorKinds<T>::reference reference;
    typedef typename detail::IteratorKinds<T>::pointer pointer;
    typedef std::random_access_iterator_tag iterator_category;
    typedef T value_type;
    typedef std::ptrdiff_t difference_type;

    TypedIterator() noexcept : fPlace(0) {}
    TypedIterator(const TypedIterator<T>& rhs) noexcept = default;
    TypedIterator<T>& operator=(const TypedIterator<T>& rhs) noexcept = default;
    TypedIterator(TypedIterator<T>&& rhs) noexcept = default;
    TypedIterator<T>& operator=(TypedIterator<T>&& rhs) noexcept = default;

    TypedIterator<T>& operator++() noexcept {
        ++fPlace;
        return *this;
    }
    TypedIterator<T>& operator--() noexcept {
        --fPlace;
        return *this;
    }
    TypedIterator<T> operator++(int) noexcept {
        TypedIterator<T> before(*this);
        ++fPlace;
        return before;
    }
    TypedIterator<T> operator--(int) noexcept {
        TypedIterator<T> before(*this);
        --fPlace;
        return before;
    }
    TypedIterator<T>& operator+=(difference_type d) noexcept {
        fPlace = static_cast<size_t>(static_cast<difference_type>(fPlace) + d);
        return *this;
    }
    TypedIterator<T>& operator-=(difference_type d) noexcept {
        fPlace = static_cast<size_t>(static_cast<difference_type>(fPlace) - d);
        return *this;
    }
    bool operator==(const TypedIterator<T>& rhs) const noexcept { return fPlace == rhs.fPlace; }
    bool operator!=(const TypedIterator<T>& rhs) const noexcept { return fPlace != rhs.fPlace; }
    bool operator<(const TypedIterator<T>& rhs) const noexcept { return fPlace < rhs.fPlace; }
    bool operator>(const TypedIterator<T>& rhs) const noexcept { return fPlace > rhs.fPlace; }
    bool operator<=(const TypedIterator<T>& rhs) const noexcept { return fPlace <= rhs.fPlace; }
    bool operator>=(const TypedIterator<T>& rhs) const noexcept { return fPlace >= rhs.fPlace; }
    TypedIterator<T> operator+(difference_type d) const noexcept {
        TypedIterator<T> moved(*this);
        moved += d;
        return moved;
    }
    TypedIterator<T> operator-(difference_type d) const noexcept {
        TypedIterator<T> moved(*this);
        moved -= d;
        return moved;
    }
    difference_type operator-(const TypedIterator<T>& rhs) const noexcept {
        return static_cast<difference_type>(fPlace) - static_cast<difference_type>(rhs.fPlace);
    }
    reference operator*() const { return At(fPlace, typename detail::IsDense<T>::type()); }
    template <typename U = T, typename = typename std::enable_if<detail::IsDense<U>::value>::type>
    pointer operator->() const noexcept {
        return &static_cast<T*>(fOwner->data)[fPlace];
    }
    reference operator[](const size_t& rhs) const { return At(fPlace + rhs, typename detail::IsDense<T>::type()); }

private:
    friend class detail::Access;
    template <typename U>
    friend class TypedArray;
    template <typename U>
    friend class SparseArray;
    template <typename U>
    friend class Reference;
    friend class Struct;
    TypedIterator(std::shared_ptr<impl::ArrayImpl> owner, size_t place, size_t offset = 0, bool slots = false) noexcept
        : fOwner(std::move(owner)), fPlace(place), fOffset(offset), fSlots(slots) {}
    reference At(size_t place, std::true_type) const { return static_cast<T*>(fOwner->data)[place]; }
    reference At(size_t place, std::false_type) const { return Element(place, std::is_const<T>()); }
    std::shared_ptr<detail::RefImpl> RefAt(size_t place) const {
        std::shared_ptr<detail::RefImpl> r = detail::PlaceRef(fOwner, fOffset + place);
        r->slot = fSlots;
        return r;
    }
    reference Element(size_t place, std::true_type) const { return detail::Read<reference>(*RefAt(place)); }
    reference Element(size_t place, std::false_type) const { return detail::Access::createRef<reference>(RefAt(place)); }
    std::shared_ptr<impl::ArrayImpl> fOwner;
    size_t fPlace;
    size_t fOffset = 0;    // where the elements iterated start in storage: a struct element's first field
    bool fSlots = false;   // the elements are the owner's own Arrays, one struct element's fields
};

/// An iterator that goes forward only: a struct array's field names.
template <typename T>
class ForwardIterator {
public:
    typedef typename std::remove_const<T>::type impl_type;
    typedef impl_type reference;
    typedef impl_type* pointer;
    typedef std::forward_iterator_tag iterator_category;
    typedef T value_type;
    typedef std::ptrdiff_t difference_type;

    ForwardIterator(const ForwardIterator<T>& rhs) noexcept = default;
    ForwardIterator<T>& operator=(const ForwardIterator<T>& rhs) noexcept = default;
    ForwardIterator(ForwardIterator<T>&& rhs) noexcept = default;
    ForwardIterator<T>& operator=(ForwardIterator<T>&& rhs) noexcept = default;
    ForwardIterator<T>& operator++() noexcept {
        ++fPlace;
        return *this;
    }
    ForwardIterator<T> operator++(int) noexcept {
        ForwardIterator<T> before(*this);
        ++fPlace;
        return before;
    }
    bool operator==(const ForwardIterator<T>& rhs) const noexcept { return fPlace == rhs.fPlace; }
    bool operator!=(const ForwardIterator<T>& rhs) const noexcept { return fPlace != rhs.fPlace; }
    reference operator*() const;

private:
    friend class detail::Access;
    friend class StructArray;
    friend class StructArrayExt;
    template <typename U>
    friend class Reference;
    ForwardIterator(std::shared_ptr<impl::ArrayImpl> owner, size_t place) noexcept : fOwner(std::move(owner)), fPlace(place) {}
    std::shared_ptr<impl::ArrayImpl> fOwner;
    size_t fPlace;
};

/// A begin and an end, for range-based for.
template <template <class> class IteratorType, class ElementType>
class Range {
public:
    IteratorType<ElementType>& begin() noexcept { return fBegin; }
    IteratorType<ElementType>& end() noexcept { return fEnd; }
    Range(IteratorType<ElementType> begin, IteratorType<ElementType> end) noexcept : fBegin(std::move(begin)), fEnd(std::move(end)) {}
    Range(Range&& rhs) noexcept : fBegin(std::move(rhs.fBegin)), fEnd(std::move(rhs.fEnd)) {}
    Range& operator=(Range&& rhs) noexcept {
        fBegin = std::move(rhs.fBegin);
        fEnd = std::move(rhs.fEnd);
        return *this;
    }

private:
    IteratorType<ElementType> fBegin;
    IteratorType<ElementType> fEnd;
    Range& operator=(Range const& rhs) = delete;
    Range(const Range& rhs) = delete;
};

/// A struct array's field name, compared by its text.
class MATLABFieldIdentifier {
public:
    MATLABFieldIdentifier() noexcept : pImpl(std::make_shared<std::string>()) {}
    MATLABFieldIdentifier(std::string str) noexcept : pImpl(std::make_shared<std::string>(std::move(str))) {}
    MATLABFieldIdentifier& operator=(MATLABFieldIdentifier&& rhs) noexcept {
        pImpl = std::move(rhs.pImpl);
        return *this;
    }
    MATLABFieldIdentifier(MATLABFieldIdentifier&& rhs) noexcept : pImpl(std::move(rhs.pImpl)) {}
    MATLABFieldIdentifier& operator=(MATLABFieldIdentifier const& rhs) noexcept {
        pImpl = rhs.pImpl;
        return *this;
    }
    MATLABFieldIdentifier(const MATLABFieldIdentifier& rhs) noexcept : pImpl(rhs.pImpl) {}
    bool operator==(const MATLABFieldIdentifier& rhs) const noexcept { return *pImpl == *rhs.pImpl; }
    operator std::string() const noexcept { return *pImpl; }

private:
    std::shared_ptr<std::string> pImpl;
    friend class detail::Access;
};

template <typename T>
inline typename ForwardIterator<T>::reference ForwardIterator<T>::operator*() const {
    return MATLABFieldIdentifier(fOwner->fields[fPlace]);
}

// --- Typed arrays --------------------------------------------------------------------------------

namespace detail {

/// Whether storage of this type can be read as a TypedArray<T>.
template <typename T>
inline bool Holds(ArrayType type) {
    return type == GetArrayType<typename std::remove_const<T>::type>::type;
}
template <>
inline bool Holds<Object>(ArrayType type) {
    return IsObject(type);
}
template <>
inline bool Holds<const Object>(ArrayType type) {
    return IsObject(type);
}

} // namespace detail

/// An array whose elements are all of type T: numbers, characters, logicals, strings
/// (MATLABString), a cell's Arrays, a struct array's elements, an enumeration's members, objects.
template <typename T>
class TypedArray : public Array {
public:
    using iterator = TypedIterator<T>;
    using const_iterator = TypedIterator<typename std::add_const<T>::type>;
    using reference = typename iterator::reference;
    using const_reference = typename const_iterator::reference;
    using data_type = T;
    static const ArrayType type = GetArrayType<typename std::remove_const<T>::type>::type;

    virtual ~TypedArray() noexcept {}
    TypedArray<T>& operator=(TypedArray<T>&& rhs) noexcept {
        Array::operator=(std::move(rhs));
        return *this;
    }
    TypedArray(TypedArray<T>&& rhs) noexcept : Array(std::move(rhs)) {}
    TypedArray(Array&& rhs) : Array(std::move(rhs)) { Check(); }
    TypedArray<T>& operator=(Array&& rhs) {
        Array::operator=(std::move(rhs));
        Check();
        return *this;
    }
    TypedArray(const TypedArray<T>& rhs) noexcept : Array(rhs) {}
    TypedArray(const Array& rhs) : Array(rhs) { Check(); }
    TypedArray<T>& operator=(const Array& rhs) {
        Array::operator=(rhs);
        Check();
        return *this;
    }
    TypedArray<T>& operator=(const TypedArray<T>& rhs) noexcept {
        Array::operator=(rhs);
        return *this;
    }

    iterator begin() noexcept { return iterator(Owned(), 0); }
    const_iterator begin() const noexcept { return const_iterator(pImpl, 0); }
    const_iterator cbegin() const noexcept { return const_iterator(pImpl, 0); }
    iterator end() noexcept { return iterator(Owned(), getNumberOfElements()); }
    const_iterator end() const noexcept { return const_iterator(pImpl, getNumberOfElements()); }
    const_iterator cend() const noexcept { return const_iterator(pImpl, getNumberOfElements()); }

    ArrayElementTypedRef<T, std::is_const<T>::value> operator[](size_t idx) {
        return detail::Access::createRef<ArrayElementTypedRef<T, std::is_const<T>::value>>(detail::NewRef(Owned(), idx));
    }
    ArrayElementTypedRef<T, true> operator[](size_t idx) const {
        return detail::Access::createRef<ArrayElementTypedRef<T, true>>(detail::NewRef(pImpl, idx));
    }

    /// The elements' memory, which the caller then owns; the array is left empty (0-by-0). Shared elements are copied first.
    buffer_ptr_t<T> release() {
        static_assert(detail::IsDense<T>::value, "Only an array of numbers, characters or logicals can release its memory.");
        impl::ArrayImpl& a = detail::Access::writable(*this);
        buffer_ptr_t<T> buffer(static_cast<T*>(a.data), reinterpret_cast<buffer_deleter_t<T>>(a.deleter));
        a.data = nullptr;
        a.count = 0;
        a.deleter = a.dense->deleteArray;
        a.dims = ArrayDimensions{0, 0};
        return buffer;
    }

protected:
    friend class detail::Access;
    TypedArray(std::shared_ptr<impl::ArrayImpl> impl) noexcept : Array(std::move(impl)) {}
    TypedArray() = delete;

private:
    void Check() const {
        if (!pImpl || !detail::Holds<T>(pImpl->type)) {
            detail::ThrowTypedArray();
        }
    }
    std::shared_ptr<impl::ArrayImpl> Owned() {
        detail::Access::writable(*this);
        return pImpl;
    }
};

using CellArray = TypedArray<Array>;
using StringArray = TypedArray<MATLABString>;
using ObjectArray = TypedArray<Object>;

/// A char array: its characters as UTF-16, UTF-8, or ASCII.
class CharArray : public TypedArray<char16_t> {
public:
    static const ArrayType type = ArrayType::CHAR;
    CharArray& operator=(CharArray&& rhs) noexcept {
        TypedArray<char16_t>::operator=(std::move(rhs));
        return *this;
    }
    CharArray(CharArray&& rhs) noexcept : TypedArray<char16_t>(std::move(rhs)) {}
    CharArray& operator=(CharArray const& rhs) noexcept {
        TypedArray<char16_t>::operator=(rhs);
        return *this;
    }
    CharArray(const CharArray& rhs) noexcept : TypedArray<char16_t>(rhs) {}
    CharArray(Array&& rhs) : TypedArray<char16_t>(std::move(rhs)) {}
    CharArray& operator=(Array&& rhs) {
        TypedArray<char16_t>::operator=(std::move(rhs));
        return *this;
    }
    CharArray(const Array& rhs) : TypedArray<char16_t>(rhs) {}
    CharArray& operator=(const Array& rhs) {
        TypedArray<char16_t>::operator=(rhs);
        return *this;
    }

    String toUTF16() const noexcept {
        const char16_t* chars = static_cast<const char16_t*>(pImpl->data);
        return chars == nullptr ? String() : String(chars, chars + pImpl->count);
    }
    std::string toUTF8() const { return detail::Utf8Of(toUTF16()); }
    std::string toAscii() const { return detail::AsciiOf(toUTF16()); }

private:
    friend class detail::Access;
    CharArray(std::shared_ptr<impl::ArrayImpl> impl) noexcept : TypedArray<char16_t>(std::move(impl)) {}
    CharArray() = delete;
};

using CharArrayRef = Reference<TypedArray<char16_t>>;

/// A struct array: its field names, and its elements.
class StructArray : public TypedArray<Struct> {
public:
    static const ArrayType type = ArrayType::STRUCT;
    ~StructArray() noexcept {}
    StructArray(StructArray&& rhs) noexcept : TypedArray<Struct>(std::move(rhs)) {}
    StructArray& operator=(StructArray&& rhs) noexcept {
        TypedArray<Struct>::operator=(std::move(rhs));
        return *this;
    }
    StructArray& operator=(StructArray const& rhs) noexcept {
        TypedArray<Struct>::operator=(rhs);
        return *this;
    }
    StructArray(const StructArray& rhs) noexcept : TypedArray<Struct>(rhs) {}
    StructArray(Array&& rhs) : TypedArray<Struct>(std::move(rhs)) {}
    StructArray(const Array& rhs) : TypedArray<Struct>(rhs) {}
    StructArray& operator=(const Array& rhs) {
        TypedArray<Struct>::operator=(rhs);
        return *this;
    }
    StructArray& operator=(Array&& rhs) {
        TypedArray<Struct>::operator=(std::move(rhs));
        return *this;
    }

    Range<ForwardIterator, MATLABFieldIdentifier const> getFieldNames() const noexcept {
        return Range<ForwardIterator, MATLABFieldIdentifier const>(ForwardIterator<MATLABFieldIdentifier const>(pImpl, 0),
                                                                    ForwardIterator<MATLABFieldIdentifier const>(pImpl, pImpl->fields.size()));
    }
    size_t getNumberOfFields() const noexcept { return pImpl->fields.size(); }

private:
    friend class detail::Access;
    StructArray() = delete;
    StructArray(std::shared_ptr<impl::ArrayImpl> impl) noexcept : TypedArray<Struct>(std::move(impl)) {}
};

/// An enumeration array: its class, and each element's member name.
class EnumArray : public TypedArray<Enumeration> {
public:
    static const ArrayType type = ArrayType::ENUM;
    EnumArray(EnumArray&& rhs) noexcept : TypedArray<Enumeration>(std::move(rhs)) {}
    EnumArray& operator=(EnumArray&& rhs) noexcept {
        TypedArray<Enumeration>::operator=(std::move(rhs));
        return *this;
    }
    EnumArray(const EnumArray& rhs) noexcept : TypedArray<Enumeration>(rhs) {}
    EnumArray& operator=(const EnumArray& rhs) noexcept {
        TypedArray<Enumeration>::operator=(rhs);
        return *this;
    }
    EnumArray(const Array& rhs) : TypedArray<Enumeration>(rhs) {}
    EnumArray& operator=(const Array& rhs) {
        TypedArray<Enumeration>::operator=(rhs);
        return *this;
    }
    EnumArray(Array&& rhs) : TypedArray<Enumeration>(std::move(rhs)) {}
    EnumArray& operator=(Array&& rhs) {
        TypedArray<Enumeration>::operator=(std::move(rhs));
        return *this;
    }
    std::string getClassName() const noexcept { return pImpl->className; }

private:
    friend class detail::Access;
    EnumArray(std::shared_ptr<impl::ArrayImpl> impl) noexcept : TypedArray<Enumeration>(std::move(impl)) {}
    EnumArray() = delete;
};

using EnumArrayRef = Reference<TypedArray<Enumeration>>;

// --- Sparse arrays -------------------------------------------------------------------------------

typedef std::pair<size_t, size_t> SparseIndex;

/// A sparse matrix: its stored values in column order, each one's row and column.
template <typename T>
class SparseArray : public Array {
public:
    using iterator = TypedIterator<T>;
    using const_iterator = TypedIterator<typename std::add_const<T>::type>;
    static const ArrayType type = GetSparseArrayType<T>::type;

    SparseArray(const SparseArray<T>& rhs) noexcept : Array(rhs) {}
    SparseArray& operator=(const SparseArray<T>& rhs) noexcept {
        Array::operator=(rhs);
        return *this;
    }
    SparseArray(SparseArray&& rhs) noexcept : Array(std::move(rhs)) {}
    SparseArray& operator=(SparseArray<T>&& rhs) noexcept {
        Array::operator=(std::move(rhs));
        return *this;
    }
    SparseArray(const Array& rhs) : Array(rhs) { Check(); }
    SparseArray& operator=(const Array& rhs) {
        Array::operator=(rhs);
        Check();
        return *this;
    }
    SparseArray(Array&& rhs) : Array(std::move(rhs)) { Check(); }
    SparseArray& operator=(Array&& rhs) {
        Array::operator=(std::move(rhs));
        Check();
        return *this;
    }

    iterator begin() noexcept { return iterator(Owned(), 0); }
    const_iterator begin() const noexcept { return const_iterator(pImpl, 0); }
    const_iterator cbegin() const noexcept { return const_iterator(pImpl, 0); }
    iterator end() noexcept { return iterator(Owned(), pImpl->count); }
    const_iterator end() const noexcept { return const_iterator(pImpl, pImpl->count); }
    const_iterator cend() const noexcept { return const_iterator(pImpl, pImpl->count); }
    size_t getNumberOfNonZeroElements() const noexcept { return pImpl->count; }

    SparseIndex getIndex(const TypedIterator<T>& it) { return IndexAt(static_cast<size_t>(it - iterator(pImpl, 0))); }
    SparseIndex getIndex(const TypedIterator<T const>& it) const { return IndexAt(static_cast<size_t>(it - const_iterator(pImpl, 0))); }

private:
    friend class detail::Access;
    SparseArray(std::shared_ptr<impl::ArrayImpl> impl) noexcept : Array(std::move(impl)) {}
    SparseArray() = delete;
    void Check() const {
        if (!pImpl || pImpl->type != GetSparseArrayType<T>::type) {
            throw InvalidArrayTypeException("Can't convert the Array to this SparseArray");
        }
    }
    std::shared_ptr<impl::ArrayImpl> Owned() {
        detail::Access::writable(*this);
        return pImpl;
    }
    SparseIndex IndexAt(size_t place) const {
        const std::vector<size_t>& starts = pImpl->columnStarts;
        size_t column = static_cast<size_t>(std::upper_bound(starts.begin(), starts.end(), place) - starts.begin()) - 1;
        return SparseIndex(pImpl->rows[place], column);
    }
};

// --- References to arrays held in cells and fields -----------------------------------------------

/// A reference to a char array held in a cell or field: its text.
class CharArrayExt : public Reference<Array> {
public:
    String toUTF16() const noexcept { return CharArray(static_cast<Array>(*this)).toUTF16(); }
    std::string toAscii() const { return CharArray(static_cast<Array>(*this)).toAscii(); }
    std::string toUTF8() const noexcept { return CharArray(static_cast<Array>(*this)).toUTF8(); }

protected:
    CharArrayExt(std::shared_ptr<detail::RefImpl> impl) noexcept : Reference<Array>(std::move(impl)) {}
    CharArrayExt(const Reference<Array>& rhs) : Reference<Array>(rhs) {}

private:
    CharArrayExt() = delete;
};

/// A reference to an enumeration array held in a cell or field: its class.
class EnumArrayExt : public Reference<Array> {
public:
    std::string getClassName() const noexcept { return EnumArray(static_cast<Array>(*this)).getClassName(); }

protected:
    EnumArrayExt(std::shared_ptr<detail::RefImpl> impl) noexcept : Reference<Array>(std::move(impl)) {}
    EnumArrayExt(const Reference<Array>& rhs) : Reference<Array>(rhs) {}

private:
    EnumArrayExt() = delete;
};

/// A reference to a struct array held in a cell or field: its field names.
class StructArrayExt : public Reference<Array> {
public:
    Range<ForwardIterator, MATLABFieldIdentifier const> getFieldNames() const noexcept {
        std::shared_ptr<impl::ArrayImpl> held = detail::SlotStorage(*pImpl, false);
        return Range<ForwardIterator, MATLABFieldIdentifier const>(ForwardIterator<MATLABFieldIdentifier const>(held, 0),
                                                                    ForwardIterator<MATLABFieldIdentifier const>(held, held->fields.size()));
    }
    size_t getNumberOfFields() const noexcept { return detail::SlotStorage(*pImpl, false)->fields.size(); }

protected:
    StructArrayExt(std::shared_ptr<detail::RefImpl> impl) noexcept : Reference<Array>(std::move(impl)) {}
    StructArrayExt(const Reference<Array>& rhs) : Reference<Array>(rhs) {}

private:
    StructArrayExt() = delete;
};

template <typename T>
struct GetReferenceArrayExt { using type = Reference<Array>; };
template <>
struct GetReferenceArrayExt<char16_t> { using type = CharArrayExt; };
template <>
struct GetReferenceArrayExt<Enumeration> { using type = EnumArrayExt; };
template <>
struct GetReferenceArrayExt<Struct> { using type = StructArrayExt; };

/// A reference to a typed array held in a cell or field: indexed, iterated and assigned through.
template <typename T>
class Reference<TypedArray<T>> : public GetReferenceArrayExt<T>::type {
public:
    using Parent = typename GetReferenceArrayExt<T>::type;
    using Parent::pImpl;
    typedef TypedIterator<T> iterator;
    typedef TypedIterator<typename std::add_const<T>::type> const_iterator;
    typedef typename std::remove_const<T>::type arr_elem_type;
    typedef TypedArray<T> ref_type;

    Reference(const Reference<Array>& rhs) : Parent(rhs) { Check(); }
    ArrayElementTypedRef<arr_elem_type, std::is_const<T>::value> operator[](size_t idx) {
        return detail::Access::createRef<ArrayElementTypedRef<arr_elem_type, std::is_const<T>::value>>(detail::NewRef(detail::SlotStorage(*pImpl, true), idx));
    }
    Reference<TypedArray<T>>& operator=(TypedArray<T> rhs) noexcept {
        detail::Slot(*pImpl) = rhs;
        return *this;
    }
    ArrayElementTypedRef<arr_elem_type, true> operator[](size_t idx) const {
        return detail::Access::createRef<ArrayElementTypedRef<arr_elem_type, true>>(detail::NewRef(detail::SlotStorage(*pImpl, false), idx));
    }
    iterator begin() noexcept { return detail::Access::template createIterator<T>(detail::SlotStorage(*pImpl, true), 0); }
    const_iterator begin() const noexcept {
        return detail::Access::template createIterator<typename std::add_const<T>::type>(detail::SlotStorage(*pImpl, false), 0);
    }
    const_iterator cbegin() const noexcept { return begin(); }
    iterator end() noexcept {
        std::shared_ptr<impl::ArrayImpl> held = detail::SlotStorage(*pImpl, true);
        size_t n = getNumElements(held->dims);
        return detail::Access::template createIterator<T>(std::move(held), n);
    }
    const_iterator end() const noexcept {
        std::shared_ptr<impl::ArrayImpl> held = detail::SlotStorage(*pImpl, false);
        size_t n = getNumElements(held->dims);
        return detail::Access::template createIterator<typename std::add_const<T>::type>(std::move(held), n);
    }
    const_iterator cend() const noexcept { return end(); }

protected:
    friend class detail::Access;
    Reference(std::shared_ptr<detail::RefImpl> impl) : Parent(std::move(impl)) { Check(); }
    Reference() = delete;

private:
    void Check() const {
        if (!detail::Holds<T>(detail::SlotStorage(*pImpl, false)->type)) {
            detail::ThrowTypedArray();
        }
    }
};

template <typename T>
using TypedArrayRef = Reference<TypedArray<T>>;
using CellArrayRef = Reference<TypedArray<Array>>;
using StructArrayRef = Reference<TypedArray<Struct>>;

/// A reference to a sparse array held in a cell or field.
template <typename T>
class Reference<SparseArray<T>> : public ArrayReferenceExt {
public:
    typedef TypedIterator<T> iterator;
    typedef TypedIterator<typename std::add_const<T>::type> const_iterator;
    Reference(const Reference<Array>& rhs) : ArrayReferenceExt(rhs) { Held(); }
    iterator begin() noexcept { return iterator(detail::SlotStorage(*pImpl, true), 0); }
    const_iterator begin() const noexcept { return const_iterator(detail::SlotStorage(*pImpl, false), 0); }
    const_iterator cbegin() const noexcept { return begin(); }
    iterator end() noexcept {
        std::shared_ptr<impl::ArrayImpl> held = detail::SlotStorage(*pImpl, true);
        size_t n = held->count;
        return iterator(std::move(held), n);
    }
    const_iterator end() const noexcept {
        std::shared_ptr<impl::ArrayImpl> held = detail::SlotStorage(*pImpl, false);
        size_t n = held->count;
        return const_iterator(std::move(held), n);
    }
    const_iterator cend() const noexcept { return end(); }
    size_t getNumberOfNonZeroElements() const noexcept { return detail::SlotStorage(*pImpl, false)->count; }

protected:
    friend class detail::Access;
    Reference(std::shared_ptr<detail::RefImpl> impl) : ArrayReferenceExt(std::move(impl)) { Held(); }
    Reference() = delete;

private:
    const SparseArray<T> Held() const { return SparseArray<T>(detail::Slot(*pImpl)); }
};

template <typename T>
using SparseArrayRef = Reference<SparseArray<T>>;

} // namespace data
} // namespace jgraph

#include "data_array_impl.hpp"

#endif // JGRAPH_DATA_ARRAY_HPP
