/*
 * k12client: a client of the C++ shared libraries probe_k12_build.m builds from src\k9 (compiler and
 * SDK plan, stage K12), through the MATLAB Data API. Built against R2025b's extern\include and its
 * libraries for the recording, and unchanged against the JGraph Runtime's include and jgrt.lib, whose
 * MathWorks-named headers call their jgraph namespace matlab too, for the comparison. One key<TAB>value
 * line per question, so the two transcripts compare line by line; a question that throws prints ERR,
 * the exception's class and its what().
 *
 *   k12client local      the Data API alone: arrays made, read, changed and refused, no runtime
 *   k12client calls      the runtime: k12lib.ctf's and k12v.ctf's functions called every way
 *   k12client typed      the strongly typed interface, k12libv2.hpp
 *   k12client paths      where initMATLABLibrary finds an archive named pick.ctf
 *   k12client twice      the application started while it runs, and again after it ended
 *   k12client outproc    the application out of process
 *   k12client runmain    the application's main run by runMain
 */
#include "MatlabCppSharedLib.hpp"
#include "k12libv2.hpp"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <functional>
#include <iostream>
#include <numeric>
#include <string>
#include <thread>
#include <vector>

namespace mc = matlab::cpplib;
namespace md = matlab::data;
namespace me = matlab::execution;

// --- Printing ------------------------------------------------------------------------------------

static std::string Utf8(const std::u16string &s)
{
    std::string out;
    for (size_t i = 0; i < s.size(); i++)
    {
        unsigned long c = s[i];
        if (c >= 0xD800 && c < 0xDC00 && i + 1 < s.size() && s[i + 1] >= 0xDC00 && s[i + 1] < 0xE000)
        {
            c = 0x10000 + ((c - 0xD800) << 10) + (s[i + 1] - 0xDC00);
            i++;
        }
        if (c < 0x80)
            out += static_cast<char>(c);
        else if (c < 0x800)
        {
            out += static_cast<char>(0xC0 | (c >> 6));
            out += static_cast<char>(0x80 | (c & 0x3F));
        }
        else if (c < 0x10000)
        {
            out += static_cast<char>(0xE0 | (c >> 12));
            out += static_cast<char>(0x80 | ((c >> 6) & 0x3F));
            out += static_cast<char>(0x80 | (c & 0x3F));
        }
        else
        {
            out += static_cast<char>(0xF0 | (c >> 18));
            out += static_cast<char>(0x80 | ((c >> 12) & 0x3F));
            out += static_cast<char>(0x80 | ((c >> 6) & 0x3F));
            out += static_cast<char>(0x80 | (c & 0x3F));
        }
    }
    return out;
}

// Line ends, tabs, NULs and every byte past ASCII shown escaped, so every answer stays on its line
// and survives the console's code page on its way into the transcript.
static std::string Shown(const std::string &s)
{
    std::string out;
    for (char c : s)
    {
        unsigned char b = static_cast<unsigned char>(c);
        if (c == '\n')
            out += "\\n";
        else if (c == '\r')
            out += "\\r";
        else if (c == '\t')
            out += "\\t";
        else if (b == 0 || b >= 0x80)
        {
            char hex[8];
            snprintf(hex, sizeof hex, "\\x%02X", b);
            out += hex;
        }
        else
            out += c;
    }
    return out;
}

static void Line(const std::string &key, const std::string &value)
{
    std::cout << key << '\t' << Shown(value) << '\n';
    std::cout.flush();
}

static std::string Text(double v)
{
    char b[64];
    snprintf(b, sizeof b, "%.17g", v);
    return b;
}
static std::string Text(float v)
{
    char b[64];
    snprintf(b, sizeof b, "%.9g", static_cast<double>(v));
    return b;
}
static std::string Text(bool v) { return v ? "1" : "0"; }
static std::string Text(int8_t v) { return std::to_string(static_cast<int>(v)); }
static std::string Text(uint8_t v) { return std::to_string(static_cast<unsigned>(v)); }
static std::string Text(int16_t v) { return std::to_string(v); }
static std::string Text(uint16_t v) { return std::to_string(v); }
static std::string Text(int32_t v) { return std::to_string(v); }
static std::string Text(uint32_t v) { return std::to_string(v); }
static std::string Text(int64_t v) { return std::to_string(v); }
static std::string Text(uint64_t v) { return std::to_string(v); }
static std::string Text(char16_t v) { return Utf8(std::u16string(1, v)); }
template <typename T>
static std::string Text(const std::complex<T> &v)
{
    std::string im = Text(v.imag());
    return Text(v.real()) + (im[0] == '-' ? "" : "+") + im + "i";
}

static std::string Dims(const md::ArrayDimensions &d)
{
    std::string s = "[";
    for (size_t k = 0; k < d.size(); k++)
        s += (k ? "," : "") + std::to_string(d[k]);
    return s + "]";
}

static const char *TypeName(md::ArrayType t)
{
    switch (t)
    {
    case md::ArrayType::LOGICAL: return "LOGICAL";
    case md::ArrayType::CHAR: return "CHAR";
    case md::ArrayType::MATLAB_STRING: return "MATLAB_STRING";
    case md::ArrayType::DOUBLE: return "DOUBLE";
    case md::ArrayType::SINGLE: return "SINGLE";
    case md::ArrayType::INT8: return "INT8";
    case md::ArrayType::UINT8: return "UINT8";
    case md::ArrayType::INT16: return "INT16";
    case md::ArrayType::UINT16: return "UINT16";
    case md::ArrayType::INT32: return "INT32";
    case md::ArrayType::UINT32: return "UINT32";
    case md::ArrayType::INT64: return "INT64";
    case md::ArrayType::UINT64: return "UINT64";
    case md::ArrayType::COMPLEX_DOUBLE: return "COMPLEX_DOUBLE";
    case md::ArrayType::COMPLEX_SINGLE: return "COMPLEX_SINGLE";
    case md::ArrayType::COMPLEX_INT8: return "COMPLEX_INT8";
    case md::ArrayType::COMPLEX_UINT8: return "COMPLEX_UINT8";
    case md::ArrayType::COMPLEX_INT16: return "COMPLEX_INT16";
    case md::ArrayType::COMPLEX_UINT16: return "COMPLEX_UINT16";
    case md::ArrayType::COMPLEX_INT32: return "COMPLEX_INT32";
    case md::ArrayType::COMPLEX_UINT32: return "COMPLEX_UINT32";
    case md::ArrayType::COMPLEX_INT64: return "COMPLEX_INT64";
    case md::ArrayType::COMPLEX_UINT64: return "COMPLEX_UINT64";
    case md::ArrayType::CELL: return "CELL";
    case md::ArrayType::STRUCT: return "STRUCT";
    case md::ArrayType::OBJECT: return "OBJECT";
    case md::ArrayType::VALUE_OBJECT: return "VALUE_OBJECT";
    case md::ArrayType::HANDLE_OBJECT_REF: return "HANDLE_OBJECT_REF";
    case md::ArrayType::ENUM: return "ENUM";
    case md::ArrayType::SPARSE_LOGICAL: return "SPARSE_LOGICAL";
    case md::ArrayType::SPARSE_DOUBLE: return "SPARSE_DOUBLE";
    case md::ArrayType::SPARSE_COMPLEX_DOUBLE: return "SPARSE_COMPLEX_DOUBLE";
    case md::ArrayType::UNKNOWN: return "UNKNOWN";
    case md::ArrayType::SPARSE_SINGLE: return "SPARSE_SINGLE";
    case md::ArrayType::SPARSE_COMPLEX_SINGLE: return "SPARSE_COMPLEX_SINGLE";
    }
    return "?";
}

template <typename T>
static std::string Join(const md::Array &a)
{
    const md::TypedArray<T> t(a);
    std::string s = "[";
    bool first = true;
    for (auto v : t)
    {
        s += (first ? "" : " ") + Text(static_cast<T>(v));
        first = false;
    }
    return s + "]";
}

template <typename T>
static std::string SparseText(const md::Array &a)
{
    const md::SparseArray<T> sp(a);
    std::string s = "nnz=" + std::to_string(sp.getNumberOfNonZeroElements());
    for (auto it = sp.begin(); it != sp.end(); ++it)
    {
        md::SparseIndex ix = sp.getIndex(it);
        s += " (" + std::to_string(ix.first) + "," + std::to_string(ix.second) + ")=" + Text(static_cast<T>(*it));
    }
    return s;
}

static std::string Describe(const md::Array &a);

static std::string Elements(const md::Array &a)
{
    switch (a.getType())
    {
    case md::ArrayType::LOGICAL: return Join<bool>(a);
    case md::ArrayType::DOUBLE: return Join<double>(a);
    case md::ArrayType::SINGLE: return Join<float>(a);
    case md::ArrayType::INT8: return Join<int8_t>(a);
    case md::ArrayType::UINT8: return Join<uint8_t>(a);
    case md::ArrayType::INT16: return Join<int16_t>(a);
    case md::ArrayType::UINT16: return Join<uint16_t>(a);
    case md::ArrayType::INT32: return Join<int32_t>(a);
    case md::ArrayType::UINT32: return Join<uint32_t>(a);
    case md::ArrayType::INT64: return Join<int64_t>(a);
    case md::ArrayType::UINT64: return Join<uint64_t>(a);
    case md::ArrayType::COMPLEX_DOUBLE: return Join<std::complex<double>>(a);
    case md::ArrayType::COMPLEX_SINGLE: return Join<std::complex<float>>(a);
    case md::ArrayType::COMPLEX_INT8: return Join<std::complex<int8_t>>(a);
    case md::ArrayType::COMPLEX_UINT8: return Join<std::complex<uint8_t>>(a);
    case md::ArrayType::COMPLEX_INT16: return Join<std::complex<int16_t>>(a);
    case md::ArrayType::COMPLEX_UINT16: return Join<std::complex<uint16_t>>(a);
    case md::ArrayType::COMPLEX_INT32: return Join<std::complex<int32_t>>(a);
    case md::ArrayType::COMPLEX_UINT32: return Join<std::complex<uint32_t>>(a);
    case md::ArrayType::COMPLEX_INT64: return Join<std::complex<int64_t>>(a);
    case md::ArrayType::COMPLEX_UINT64: return Join<std::complex<uint64_t>>(a);
    case md::ArrayType::CHAR:
    {
        const md::CharArray c(a);
        return "'" + Utf8(c.toUTF16()) + "'";
    }
    case md::ArrayType::MATLAB_STRING:
    {
        const md::TypedArray<md::MATLABString> t(a);
        std::string s = "[";
        bool first = true;
        for (auto v : t)
        {
            md::MATLABString m = v;
            s += (first ? "" : " ") + (m.has_value() ? "\"" + Utf8(*m) + "\"" : std::string("<missing>"));
            first = false;
        }
        return s + "]";
    }
    case md::ArrayType::CELL:
    {
        // By iterator, in storage order: a single index is refused for a matrix.
        const md::CellArray c(a);
        std::string s = "{";
        bool first = true;
        for (md::Array e : c)
        {
            s += (first ? "" : "; ") + Describe(e);
            first = false;
        }
        return s + "}";
    }
    case md::ArrayType::STRUCT:
    {
        const md::StructArray st(a);
        std::vector<std::string> names;
        for (const auto &n : st.getFieldNames())
            names.push_back(std::string(n));
        std::string s = "fields [";
        for (size_t k = 0; k < names.size(); k++)
            s += (k ? "," : "") + names[k];
        s += "]";
        for (md::Struct e : st)
        {
            s += " (";
            for (size_t k = 0; k < names.size(); k++)
            {
                md::Array v = e[names[k]];
                s += (k ? ", " : "") + names[k] + "=" + Describe(v);
            }
            s += ")";
        }
        return s;
    }
    case md::ArrayType::ENUM:
    {
        const md::EnumArray en(a);
        std::string s = en.getClassName() + " {";
        for (size_t k = 0; k < en.getNumberOfElements(); k++)
        {
            std::string name = en[k];
            s += (k ? " " : "") + name;
        }
        return s + "}";
    }
    case md::ArrayType::SPARSE_DOUBLE: return SparseText<double>(a);
    case md::ArrayType::SPARSE_LOGICAL: return SparseText<bool>(a);
    case md::ArrayType::SPARSE_COMPLEX_DOUBLE: return SparseText<std::complex<double>>(a);
    case md::ArrayType::OBJECT:
    case md::ArrayType::VALUE_OBJECT:
    case md::ArrayType::HANDLE_OBJECT_REF:
        return "object";
    default:
        return "?";
    }
}

static std::string Describe(const md::Array &a)
{
    std::string s = std::string(TypeName(a.getType())) + " " + Dims(a.getDimensions());
    try
    {
        if (a.getMemoryLayout() == md::MemoryLayout::ROW_MAJOR)
            s += " rowmajor";
    }
    catch (...)
    {
        s += " layout?";
    }
    return s + " " + Elements(a);
}

// --- Failures ------------------------------------------------------------------------------------

static std::string Frames(const me::MATLABExecutionException &e)
{
    std::string s;
    for (const me::StackFrame &f : e.getStackTrace())
    {
        std::string file = Utf8(f.getFileName());
        size_t slash = file.find_last_of("\\/");
        if (slash != std::string::npos)
            file = file.substr(slash + 1);
        s += " [" + file + ":" + Utf8(f.getFunctionName()) + ":" + std::to_string(f.getLineNumber()) + "]";
    }
    return s;
}

static std::string Causes(const me::MATLABExecutionException &e)
{
    std::string s;
    for (const me::MATLABExecutionException &c : e.getCause())
        s += " {" + c.getMessageID() + " | " + Utf8(c.getMessageText()) + Causes(c) + "}";
    return s;
}

#define K12_DATA_EXCEPTIONS(X) \
    X(InvalidArrayIndexException) X(TypeMismatchException) X(InvalidArrayTypeException) \
    X(InvalidMemoryLayoutException) X(InvalidDimensionsInRowMajorArrayException) X(InvalidTypeInRowMajorArrayException) \
    X(SystemErrorException) X(InvalidFieldNameException) X(IllegalFieldNameException) \
    X(FailedToLoadLibMatlabDataArrayException) X(FailedToResolveSymbolException) X(WrongNumberOfEnumsSuppliedException) \
    X(MustSpecifyClassNameException) X(TooManyIndicesProvidedException) X(NotEnoughIndicesProvidedException) \
    X(StringIndexMustBeLastException) X(StringIndexNotValidException) X(CanOnlyUseOneStringIndexException) \
    X(NonAsciiCharInInputDataException) X(NonAsciiCharInRequestedAsciiOutputException) X(InvalidDimensionsInSparseArrayException) \
    X(DuplicateFieldNameInStructArrayException) X(CantIndexIntoEmptyArrayException) X(FeatureNotSupportedException) \
    X(NumberOfElementsExceedsMaximumException) X(ObjectArrayIncompatibleTypesException) X(AccessingObjectNotSupportedException) \
    X(InvalidNumberOfElementsProvidedException) X(ObjectNotFoundException) X(WrongVectorSizeForStructException) \
    X(PropertyExistsException) X(CouldNotCallConstructorException) X(HandleObjectNotFoundException) \
    X(InvalidHeterogeneousClassException) X(InvalidHandleObjectTypeException) X(InvalidClassNameException) \
    X(RecursionLimitExceededException) X(PropertyNotFoundException) X(InvalidPropertyValueException) \
    X(FailedToSerializeObjectException) X(InvalidEnumerationNameException) X(StringIsMissingException) \
    X(UnSuppportedOperationException) X(ObjectSerializationFailedException) X(JavaNotSupportedException) \
    X(InvalidAssignmentForContainerArrayException) X(InvalidUTF8InputException) X(InvalidUTF16toUTF8ConversionException)

#define K12_CATCH(name) \
    catch (const md::name &e) { return std::string(#name " | ") + e.what(); }

// The exception in flight, by its class: every catch below names a class both APIs have.
static std::string Failure()
{
    try
    {
        throw;
    }
    catch (const me::MATLABExecutionException &e)
    {
        return "MATLABExecutionException | " + e.getMessageID() + " | " + Utf8(e.getMessageText()) + " | what " + e.what() + " |" +
               Frames(e) + " |" + Causes(e);
    }
    catch (const me::MATLABSyntaxException &e)
    {
        return "MATLABSyntaxException | " + e.getMessageID() + " | " + Utf8(e.getMessageText()) + " | what " + e.what();
    }
    catch (const me::MATLABException &e)
    {
        return "MATLABException | " + e.getMessageID() + " | " + Utf8(e.getMessageText()) + " | what " + e.what();
    }
    catch (const me::CancelledException &e) { return std::string("CancelledException | ") + e.what(); }
    catch (const me::InterruptedException &e) { return std::string("InterruptedException | ") + e.what(); }
    catch (const me::MATLABNotAvailableException &e) { return std::string("MATLABNotAvailableException | ") + e.what(); }
    catch (const me::TypeConversionException &e) { return std::string("TypeConversionException | ") + e.what(); }
    catch (const mc::CppSharedLibException &e) { return std::string("CppSharedLibException | ") + e.what(); }
    catch (const me::Exception &e) { return std::string("execution::Exception | ") + e.what(); }
    K12_DATA_EXCEPTIONS(K12_CATCH)
    catch (const matlab::OutOfMemoryException &e) { return std::string("OutOfMemoryException | ") + e.what(); }
    catch (const matlab::Exception &e) { return std::string("matlab::Exception | ") + e.what(); }
    catch (const std::exception &e) { return std::string("std::exception | ") + e.what(); }
    catch (...) { return "unknown"; }
}

template <typename F>
static void Case(const std::string &key, F f)
{
    std::string value;
    try
    {
        value = f();
    }
    catch (...)
    {
        value = "ERR " + Failure();
    }
    Line(key, value);
}

// --- Local: the Data API alone -------------------------------------------------------------------

struct Visitor
{
    std::string operator()(const md::TypedArray<double> &) { return "double"; }
    std::string operator()(const md::CharArray &) { return "char"; }
    std::string operator()(const md::StructArray &) { return "struct"; }
    template <typename T>
    std::string operator()(const T &) { return "other"; }
};

template <typename T>
static void Kind(md::ArrayFactory &f, const std::string &name, T one)
{
    Case("type." + name, [&] { return Describe(f.createArray<T>({2, 3})); });
    Case("scalar." + name, [&] { return Describe(f.createScalar<T>(one)); });
    Case("list." + name, [&] { return Describe(f.createArray<T>({1, 2}, {one, one})); });
    Case("typed." + name, [&] {
        md::Array a = f.createScalar<T>(one);
        md::TypedArray<T> t(a);
        return std::string("ok ") + TypeName(t.getType());
    });
}

static void Local()
{
    md::ArrayFactory f;
    Kind<bool>(f, "bool", true);
    Kind<char16_t>(f, "char16", u'x');
    Kind<double>(f, "double", 2.5);
    Kind<float>(f, "float", 1.5f);
    Kind<int8_t>(f, "int8", -5);
    Kind<uint8_t>(f, "uint8", 200);
    Kind<int16_t>(f, "int16", -7);
    Kind<uint16_t>(f, "uint16", 7);
    Kind<int32_t>(f, "int32", -100000);
    Kind<uint32_t>(f, "uint32", 4000000000u);
    Kind<int64_t>(f, "int64", -9007199254740993LL);
    Kind<uint64_t>(f, "uint64", 18446744073709551615ULL);
    Kind<std::complex<double>>(f, "cdouble", std::complex<double>(1, -1));
    Kind<std::complex<float>>(f, "cfloat", std::complex<float>(2, 3));
    Kind<std::complex<int8_t>>(f, "cint8", std::complex<int8_t>(1, 2));
    Kind<std::complex<uint16_t>>(f, "cuint16", std::complex<uint16_t>(3, 4));
    Kind<std::complex<int64_t>>(f, "cint64", std::complex<int64_t>(-5, 6));
    Case("type.string", [&] { return Describe(f.createArray<md::MATLABString>({1, 2})); });
    Case("type.cell", [&] { return Describe(f.createArray<md::Array>({1, 2})); });
    Case("type.cellarray", [&] { return Describe(f.createCellArray({2, 1})); });

    // Dimensions.
    for (const md::ArrayDimensions &d : std::vector<md::ArrayDimensions>{
             {2, 3, 1}, {2, 1, 1}, {1}, {5}, {}, {0, 3}, {2, 3, 1, 1}, {1, 1, 2}, {3, 0, 2}, {1, 1}, {0}, {2, 3, 4}})
    {
        Case("dims." + Dims(d), [&] {
            md::TypedArray<double> a = f.createArray<double>(d);
            return Dims(a.getDimensions()) + " n=" + std::to_string(a.getNumberOfElements()) + " empty=" + Text(a.isEmpty());
        });
    }
    Case("dims.getNumElements", [&] { return std::to_string(md::getNumElements({2, 3, 4})); });
    Case("dims.getNumElements0", [&] { return std::to_string(md::getNumElements({})); });

    // Scalars and text.
    Case("scalar.string16", [&] { return Describe(f.createScalar(md::String(u"alpha"))); });
    Case("scalar.std", [&] { return Describe(f.createScalar(std::string("abc"))); });
    Case("scalar.std.nonascii", [&] { return Describe(f.createScalar(std::string("h\xC3\xA9llo"))); });
    Case("scalar.missing", [&] { return Describe(f.createScalar(md::MATLABString())); });
    Case("scalar.emptystring", [&] { return Describe(f.createScalar(md::String(u""))); });
    Case("char.hello", [&] { return Describe(f.createCharArray("hello")); });
    Case("char.empty", [&] { return Describe(f.createCharArray("")); });
    Case("char.u16", [&] { return Describe(f.createCharArray(md::String(u"λ€A"))); });
    Case("char.std.nonascii", [&] { return Describe(f.createCharArray(std::string("h\xC3\xA9llo"))); });
    Case("char.utf8", [&] { return Describe(f.createCharArrayFromUTF8("h\xC3\xA9llo \xCE\xBB")); });
    Case("char.utf8.bad", [&] { return Describe(f.createCharArrayFromUTF8("bad \xC3")); });
    Case("char.toAscii", [&] { return f.createCharArray("abc").toAscii(); });
    Case("char.toAscii.nonascii", [&] { return f.createCharArray(md::String(u"λ")).toAscii(); });
    Case("char.toUTF8", [&] { return f.createCharArray(md::String(u"λ€A")).toUTF8(); });
    Case("char.toUTF16", [&] { return Utf8(f.createCharArray("xyz").toUTF16()); });
    Case("char.matrix", [&] {
        md::CharArray c = f.createArray<char16_t>({2, 3}, {u'a', u'd', u'b', u'e', u'c', u'f'});
        return Describe(c) + " ascii " + c.toAscii();
    });
    Case("char.element", [&] {
        md::CharArray c = f.createCharArray("hey");
        char16_t e = c[1];
        c[2] = u'Y';
        return Text(e) + " " + c.toAscii();
    });
    Case("char.from.double", [&] {
        md::CharArray c = f.createScalar(1.0);
        return Describe(c);
    });

    // Filling.
    Case("init.exact", [&] { return Describe(f.createArray<double>({2, 2}, {1, 2, 3, 4})); });
    Case("init.fewer", [&] { return Describe(f.createArray<double>({2, 2}, {1, 2, 3})); });
    Case("init.more", [&] { return Describe(f.createArray<double>({2, 2}, {1, 2, 3, 4, 5})); });
    Case("init.iter", [&] {
        std::vector<double> v{1, 2, 3, 4, 5, 6};
        return Describe(f.createArray({2, 3}, v.begin(), v.end()));
    });
    Case("init.iter.rowmajor", [&] {
        std::vector<double> v{1, 2, 3, 4, 5, 6};
        return Describe(f.createArray({2, 3}, v.begin(), v.end(), md::InputLayout::ROW_MAJOR));
    });
    Case("init.iter.fewer", [&] {
        std::vector<double> v{1, 2, 3};
        return Describe(f.createArray({2, 2}, v.begin(), v.end()));
    });
    Case("init.iter.more", [&] {
        std::vector<double> v{1, 2, 3, 4, 5};
        return Describe(f.createArray({2, 2}, v.begin(), v.end()));
    });
    Case("init.iter.int", [&] {
        std::vector<int> v{1, 2};
        return Describe(f.createArray({1, 2}, v.begin(), v.end()));
    });
    Case("init.pointers", [&] {
        double v[] = {7, 8, 9};
        return Describe(f.createArray<double>({1, 3}, v, v + 3));
    });
    Case("init.strings", [&] {
        return Describe(f.createArray<md::MATLABString>({1, 3}, {md::MATLABString(md::String(u"a")), md::MATLABString(), md::MATLABString(md::String(u"c"))}));
    });
    Case("init.stdstrings", [&] {
        std::vector<std::string> v{"x", "yy"};
        return Describe(f.createArray({1, 2}, v.begin(), v.end()));
    });
    Case("init.cellvalues", [&] { return Describe(f.createCellArray({1, 3}, 1.0, std::string("two"), f.createScalar<int16_t>(3))); });
    Case("init.cellvalues.u16", [&] { return Describe(f.createCellArray({1, 2}, md::String(u"w"), true)); });

    // Indexing.
    md::TypedArray<double> m = f.createArray<double>({2, 3}, {1, 2, 3, 4, 5, 6});
    Case("index.linear", [&] { double v = m[3]; return Text(v); });
    Case("index.pair", [&] { double v = m[1][2]; return Text(v); });
    Case("index.pair.first", [&] { double v = m[0][1]; return Text(v); });
    Case("index.past", [&] { double v = m[6]; return Text(v); });
    Case("index.pair.past", [&] { double v = m[2][0]; return Text(v); });
    Case("index.three", [&] { double v = m[0][0][0]; return Text(v); });
    Case("index.empty", [&] {
        md::TypedArray<double> e = f.createArray<double>({0, 0});
        double v = e[0];
        return Text(v);
    });
    Case("index.write", [&] {
        md::TypedArray<double> w = f.createArray<double>({2, 2}, {1, 2, 3, 4});
        w[1][0] = 20;
        w[3] = 40;
        return Describe(w);
    });
    Case("index.generic", [&] {
        md::Array a = f.createArray<int32_t>({1, 2}, {5, 6});
        int32_t v = a[1];
        return Text(v);
    });
    Case("index.generic.mismatch", [&] {
        md::Array a = f.createArray<int32_t>({1, 2}, {5, 6});
        double v = a[1];
        return Text(v);
    });
    Case("index.generic.write", [&] {
        md::Array a = f.createArray<double>({1, 2}, {5, 6});
        a[0] = 9.0;
        return Describe(a);
    });
    Case("index.generic.write.mismatch", [&] {
        md::Array a = f.createArray<double>({1, 2}, {5, 6});
        a[0] = 9;
        return Describe(a);
    });
    Case("index.typed.read.int", [&] {
        md::TypedArray<int16_t> t = f.createArray<int16_t>({1, 2}, {5, 6});
        int v = t[1];
        return std::to_string(v);
    });

    // Array type checks.
    Case("cast.double.from.int32", [&] { md::TypedArray<double> t = f.createScalar<int32_t>(5); return Describe(t); });
    Case("cast.struct.from.cell", [&] { md::StructArray s = f.createCellArray({1, 1}); return Describe(s); });
    Case("cast.cell.from.struct", [&] { md::CellArray c = f.createStructArray({1, 1}, {"a"}); return Describe(c); });
    Case("cast.enum.from.double", [&] { md::EnumArray e = f.createScalar(1.0); return Describe(e); });
    Case("cast.sparse.from.double", [&] { md::SparseArray<double> s = f.createScalar(1.0); return Describe(s); });
    Case("cast.string.from.char", [&] { md::TypedArray<md::MATLABString> s = f.createCharArray("a"); return Describe(s); });
    Case("cast.complex.from.double", [&] { md::TypedArray<std::complex<double>> c = f.createScalar(1.0); return Describe(c); });

    // Copies share until written.
    Case("cow.typed", [&] {
        md::TypedArray<double> a = f.createArray<double>({1, 3}, {1, 2, 3});
        md::TypedArray<double> b = a;
        b[0] = 10;
        return "a=" + Join<double>(a) + " b=" + Join<double>(b);
    });
    Case("cow.generic", [&] {
        md::TypedArray<double> a = f.createArray<double>({1, 3}, {1, 2, 3});
        md::Array c = a;
        md::TypedArray<double> d(c);
        d[1] = 20;
        return "a=" + Join<double>(a) + " c=" + Join<double>(c) + " d=" + Join<double>(d);
    });
    Case("cow.cell", [&] {
        md::CellArray c1 = f.createCellArray({1, 1});
        c1[0] = f.createScalar(1.0);
        md::CellArray c2 = c1;
        c2[0] = f.createScalar(2.0);
        return Describe(c1) + " / " + Describe(c2);
    });
    Case("cow.struct.ref", [&] {
        md::StructArray s = f.createStructArray({1, 1}, {"v"});
        s[0]["v"] = f.createArray<double>({1, 2}, {1, 2});
        md::StructArray s2 = s;
        md::TypedArrayRef<double> r = s2[0]["v"];
        r[0] = 99;
        return Describe(s) + " / " + Describe(s2);
    });
    Case("cow.cell.nested", [&] {
        md::CellArray c = f.createCellArray({1, 1});
        c[0] = f.createArray<double>({1, 2}, {1, 2});
        md::CellArray c2 = c;
        md::TypedArrayRef<double> r = c2[0];
        r[1] = 7;
        return Describe(c) + " / " + Describe(c2);
    });
    Case("cow.element.taken", [&] {
        md::CellArray c = f.createCellArray({1, 1});
        c[0] = f.createArray<double>({1, 2}, {1, 2});
        md::TypedArray<double> inner = c[0];
        inner[0] = 50;
        return Describe(c) + " / " + Describe(inner);
    });

    // Iterators.
    Case("iter.sum", [&] {
        double s = 0;
        for (auto v : m)
            s += v;
        return Text(s);
    });
    Case("iter.write", [&] {
        md::TypedArray<double> w = f.createArray<double>({1, 3}, {1, 2, 3});
        for (auto &v : w)
            v *= 2;
        return Describe(w);
    });
    Case("iter.arith", [&] {
        auto it = m.begin();
        it += 2;
        auto it2 = it + 1;
        return Text(*it) + " " + Text(*it2) + " " + Text(it[1]) + " d=" + std::to_string(m.end() - m.begin()) + " lt=" + Text(it < it2);
    });
    Case("iter.sort", [&] {
        md::TypedArray<double> w = f.createArray<double>({1, 4}, {3, 1, 4, 2});
        std::sort(w.begin(), w.end());
        return Describe(w);
    });
    Case("iter.empty", [&] {
        md::TypedArray<double> e = f.createArray<double>({0, 3});
        return Text(e.begin() == e.end());
    });
    Case("iter.strings", [&] {
        md::TypedArray<md::MATLABString> s = f.createArray<md::MATLABString>({1, 2}, {md::MATLABString(md::String(u"a")), md::MATLABString(md::String(u"b"))});
        std::string out;
        for (auto e : s)
        {
            std::string t = e;
            out += t + ";";
        }
        return out;
    });
    Case("iter.cell.write", [&] {
        md::CellArray c = f.createCellArray({1, 2});
        for (auto e : c)
            e = f.createScalar(4.0);
        return Describe(c);
    });
    Case("range.readonly", [&] {
        double s = 0;
        for (const auto &v : md::getReadOnlyElements<double>(m))
            s += v;
        return Text(s);
    });
    Case("range.readonly.mismatch", [&] {
        double s = 0;
        md::Array a = f.createScalar<int32_t>(1);
        for (const auto &v : md::getReadOnlyElements<double>(a))
            s += v;
        return Text(s);
    });
    Case("range.writable", [&] {
        md::Array a = f.createArray<double>({1, 2}, {1, 2});
        for (auto &v : md::getWritableElements<double>(a))
            v += 10;
        return Describe(a);
    });

    // Buffers and memory layout.
    Case("buffer.column", [&] {
        md::buffer_ptr_t<double> b = f.createBuffer<double>(6);
        for (int k = 0; k < 6; k++)
            b[k] = k + 1;
        md::TypedArray<double> a = f.createArrayFromBuffer<double>({2, 3}, std::move(b));
        double v = a[0][1];
        return Describe(a) + " [0][1]=" + Text(v);
    });
    md::buffer_ptr_t<double> rb = f.createBuffer<double>(6);
    for (int k = 0; k < 6; k++)
        rb[k] = k + 1;
    md::TypedArray<double> rm = f.createArrayFromBuffer<double>({2, 3}, std::move(rb), md::MemoryLayout::ROW_MAJOR);
    Case("rowmajor.describe", [&] { return Describe(rm); });
    Case("rowmajor.pair", [&] { double v = rm[0][1]; double w = rm[1][0]; return Text(v) + " " + Text(w); });
    Case("rowmajor.linear", [&] { double v = rm[1]; return Text(v); });
    Case("rowmajor.column.order", [&] {
        std::string s;
        for (const auto &v : md::ColumnMajor::readOnlyElements<double>(rm))
            s += Text(v) + " ";
        return s;
    });
    Case("rowmajor.row.order", [&] {
        std::string s;
        for (const auto &v : md::RowMajor::readOnlyElements<double>(rm))
            s += Text(v) + " ";
        return s;
    });
    Case("columnmajor.row.order", [&] {
        std::string s;
        for (const auto &v : md::RowMajor::readOnlyElements<double>(m))
            s += Text(v) + " ";
        return s;
    });
    Case("rowmajor.nd", [&] {
        md::buffer_ptr_t<double> b = f.createBuffer<double>(8);
        for (int k = 0; k < 8; k++)
            b[k] = k + 1;
        return Describe(f.createArrayFromBuffer<double>({2, 2, 2}, std::move(b), md::MemoryLayout::ROW_MAJOR));
    });
    Case("buffer.cell", [&] {
        md::buffer_ptr_t<md::Array> b = f.createBuffer<md::Array>(2);
        return std::string("made");
    });
    Case("buffer.zero", [&] {
        md::buffer_ptr_t<double> b = f.createBuffer<double>(0);
        return Describe(f.createArrayFromBuffer<double>({0, 0}, std::move(b)));
    });
    Case("buffer.short", [&] {
        md::buffer_ptr_t<double> b = f.createBuffer<double>(2);
        b[0] = 1;
        b[1] = 2;
        return Describe(f.createArrayFromBuffer<double>({1, 2}, std::move(b)));
    });
    Case("release", [&] {
        md::TypedArray<double> a = f.createArray<double>({1, 3}, {1, 2, 3});
        md::buffer_ptr_t<double> b = a.release();
        return Text(b[0]) + " " + Text(b[2]) + " after " + Dims(a.getDimensions());
    });
    Case("release.shared", [&] {
        md::TypedArray<double> a = f.createArray<double>({1, 3}, {1, 2, 3});
        md::TypedArray<double> keep = a;
        md::buffer_ptr_t<double> b = a.release();
        return Text(b[0]) + " keep " + Join<double>(keep);
    });

    // Strings.
    md::TypedArray<md::MATLABString> sa =
        f.createArray<md::MATLABString>({1, 3}, {md::MATLABString(md::String(u"one")), md::MATLABString(), md::MATLABString(md::String(u"λ"))});
    Case("string.std", [&] { std::string s = sa[0]; return s; });
    Case("string.std.missing", [&] { std::string s = sa[1]; return s; });
    Case("string.std.nonascii", [&] { std::string s = sa[2]; return s; });
    Case("string.u16", [&] { md::String s = sa[0]; return Utf8(s); });
    Case("string.u16.missing", [&] { md::String s = sa[1]; return Utf8(s); });
    Case("string.has_value", [&] { return Text(sa[0].has_value()) + Text(sa[1].has_value()); });
    Case("string.bool", [&] { return Text(bool(sa[0])) + Text(bool(sa[1])); });
    Case("string.optional", [&] { md::MATLABString o = sa[1]; return Text(o.has_value()); });
    Case("string.assign", [&] {
        md::TypedArray<md::MATLABString> t = sa;
        t[1] = std::string("two");
        t[0] = md::String(u"uno");
        return Describe(t) + " / " + Describe(sa);
    });
    Case("string.compare", [&] {
        return Text(sa[0] == std::string("one")) + Text(sa[0] == md::String(u"one")) + Text(sa[1] == md::MATLABString()) + Text(sa[0] != std::string("x"));
    });

    // Cells.
    Case("cell.default", [&] { return Describe(f.createCellArray({1, 2})); });
    Case("cell.set", [&] {
        md::CellArray c = f.createCellArray({1, 2});
        c[0] = f.createScalar(1.0);
        c[1] = f.createCharArray("two");
        md::Array e = c[1];
        return Describe(c) + " e=" + TypeName(e.getType()) + " ref=" + TypeName(c[1].getType());
    });
    Case("cell.nested.index", [&] {
        md::CellArray c = f.createCellArray({1, 1});
        c[0] = f.createArray<double>({1, 3}, {4, 5, 6});
        md::Array v = c[0][1];
        return Describe(v);
    });
    Case("cell.pair.index", [&] {
        md::CellArray c = f.createCellArray({2, 2});
        c[1][0] = f.createScalar(10.0);
        c[0][1] = f.createScalar(20.0);
        return Describe(c);
    });
    Case("cell.inner.ref", [&] {
        md::CellArray c = f.createCellArray({1, 1});
        c[0] = f.createArray<double>({1, 3}, {4, 5, 6});
        md::TypedArrayRef<double> r = c[0];
        double v = r[1];
        r[2] = 60;
        return Text(v) + " " + Describe(c);
    });
    Case("cell.ref.dims", [&] {
        md::CellArray c = f.createCellArray({1, 1});
        c[0] = f.createArray<double>({2, 2});
        return Dims(c[0].getDimensions()) + " n=" + std::to_string(c[0].getNumberOfElements()) + " empty=" + Text(c[0].isEmpty());
    });

    // Structs.
    Case("struct.default", [&] { return Describe(f.createStructArray({1, 2}, {"b", "a"})); });
    Case("struct.set", [&] {
        md::StructArray s = f.createStructArray({1, 1}, {"name", "value"});
        s[0]["name"] = f.createCharArray("probe");
        s[0]["value"] = f.createArray<double>({1, 3}, {1, 2, 3});
        return Describe(s) + " fields=" + std::to_string(s.getNumberOfFields());
    });
    Case("struct.missing.field", [&] {
        md::StructArray s = f.createStructArray({1, 1}, {"a"});
        md::Array v = s[0]["nosuch"];
        return Describe(v);
    });
    Case("struct.missing.field.set", [&] {
        md::StructArray s = f.createStructArray({1, 1}, {"a"});
        s[0]["nosuch"] = f.createScalar(1.0);
        return Describe(s);
    });
    Case("struct.duplicate", [&] { return Describe(f.createStructArray({1, 1}, {"a", "a"})); });
    Case("struct.illegal.digit", [&] { return Describe(f.createStructArray({1, 1}, {"1a"})); });
    Case("struct.illegal.space", [&] { return Describe(f.createStructArray({1, 1}, {"a b"})); });
    Case("struct.illegal.empty", [&] { return Describe(f.createStructArray({1, 1}, {""})); });
    Case("struct.long.63", [&] { return Describe(f.createStructArray({1, 1}, {std::string(63, 'a')})).substr(0, 30); });
    Case("struct.long.64", [&] { return Describe(f.createStructArray({1, 1}, {std::string(64, 'a')})).substr(0, 30); });
    Case("struct.nofields", [&] { return Describe(f.createStructArray({1, 1}, {})); });
    Case("struct.index.past", [&] {
        md::StructArray s = f.createStructArray({1, 1}, {"a"});
        md::Array v = s[3]["a"];
        return Describe(v);
    });
    Case("struct.element", [&] {
        md::StructArray s = f.createStructArray({1, 2}, {"a"});
        s[1]["a"] = f.createScalar(5.0);
        md::Struct e = s[1];
        md::Array v = e["a"];
        std::string out = Describe(v);
        for (auto x : e)
            out += " it:" + std::string(TypeName(x.getType()));
        return out;
    });
    Case("struct.fieldid", [&] {
        md::MATLABFieldIdentifier a("x"), b("x"), c("y");
        return Text(a == b) + Text(a == c) + " " + std::string(a);
    });

    // Enumerations.
    Case("enum.make", [&] { return Describe(f.createEnumArray({1, 2}, "k9color", {"red", "blue"})); });
    Case("enum.noname", [&] { return Describe(f.createEnumArray({1, 2}, "k9color")); });
    Case("enum.wrongcount", [&] { return Describe(f.createEnumArray({1, 2}, "k9color", {"red"})); });
    Case("enum.noclass", [&] { return Describe(f.createEnumArray({1, 1}, "", {"red"})); });
    Case("enum.element", [&] {
        md::EnumArray e = f.createEnumArray({1, 2}, "k9color", {"red", "blue"});
        std::string n = e[1];
        return n + " " + e.getClassName();
    });

    // Sparse.
    Case("sparse.make", [&] {
        auto data = f.createBuffer<double>(2);
        data[0] = 5;
        data[1] = 6;
        auto rows = f.createBuffer<size_t>(2);
        rows[0] = 0;
        rows[1] = 2;
        auto cols = f.createBuffer<size_t>(2);
        cols[0] = 0;
        cols[1] = 1;
        return Describe(f.createSparseArray<double>({3, 3}, 2, std::move(data), std::move(rows), std::move(cols)));
    });
    Case("sparse.unsorted", [&] {
        auto data = f.createBuffer<double>(3);
        data[0] = 1;
        data[1] = 2;
        data[2] = 3;
        auto rows = f.createBuffer<size_t>(3);
        rows[0] = 2;
        rows[1] = 0;
        rows[2] = 0;
        auto cols = f.createBuffer<size_t>(3);
        cols[0] = 0;
        cols[1] = 1;
        cols[2] = 0;
        return Describe(f.createSparseArray<double>({3, 2}, 3, std::move(data), std::move(rows), std::move(cols)));
    });
    Case("sparse.logical", [&] {
        auto data = f.createBuffer<bool>(1);
        data[0] = true;
        auto rows = f.createBuffer<size_t>(1);
        rows[0] = 1;
        auto cols = f.createBuffer<size_t>(1);
        cols[0] = 1;
        return Describe(f.createSparseArray<bool>({2, 2}, 1, std::move(data), std::move(rows), std::move(cols)));
    });
    Case("sparse.complex", [&] {
        auto data = f.createBuffer<std::complex<double>>(1);
        data[0] = std::complex<double>(1, 1);
        auto rows = f.createBuffer<size_t>(1);
        rows[0] = 0;
        auto cols = f.createBuffer<size_t>(1);
        cols[0] = 0;
        return Describe(f.createSparseArray<std::complex<double>>({2, 2}, 1, std::move(data), std::move(rows), std::move(cols)));
    });
    Case("sparse.nd", [&] {
        auto data = f.createBuffer<double>(1);
        data[0] = 1;
        auto rows = f.createBuffer<size_t>(1);
        rows[0] = 0;
        auto cols = f.createBuffer<size_t>(1);
        cols[0] = 0;
        return Describe(f.createSparseArray<double>({2, 2, 2}, 1, std::move(data), std::move(rows), std::move(cols)));
    });
    Case("sparse.past", [&] {
        auto data = f.createBuffer<double>(1);
        data[0] = 1;
        auto rows = f.createBuffer<size_t>(1);
        rows[0] = 5;
        auto cols = f.createBuffer<size_t>(1);
        cols[0] = 0;
        return Describe(f.createSparseArray<double>({2, 2}, 1, std::move(data), std::move(rows), std::move(cols)));
    });
    Case("sparse.write", [&] {
        auto data = f.createBuffer<double>(1);
        data[0] = 1;
        auto rows = f.createBuffer<size_t>(1);
        rows[0] = 0;
        auto cols = f.createBuffer<size_t>(1);
        cols[0] = 0;
        md::SparseArray<double> s = f.createSparseArray<double>({2, 2}, 1, std::move(data), std::move(rows), std::move(cols));
        *s.begin() = 8;
        return Describe(s);
    });

    // Optional, empty, visitors.
    Case("optional", [&] {
        md::MATLABString o;
        std::string s = Text(o.has_value()) + Text(bool(o));
        o = md::String(u"x");
        s += Text(o.has_value()) + Utf8(*o);
        o.reset();
        s += Text(o.has_value());
        return s + Text(md::MATLABString(md::String(u"a")) == md::MATLABString(md::String(u"a"))) + Text(md::MATLABString() == md::MATLABString());
    });
    Case("empty", [&] { return Describe(f.createEmptyArray()); });
    Case("visit.double", [&] { return md::apply_visitor(md::Array(m), Visitor()); });
    Case("visit.char", [&] { return md::apply_visitor(md::Array(f.createCharArray("a")), Visitor()); });
    Case("visit.struct", [&] { return md::apply_visitor(md::Array(f.createStructArray({1, 1}, {"a"})), Visitor()); });
    Case("visit.int8", [&] { return md::apply_visitor(md::Array(f.createScalar<int8_t>(1)), Visitor()); });
    Case("visit.string", [&] { return md::apply_visitor(md::Array(f.createScalar(md::String(u"s"))), Visitor()); });

    // Conversion helpers.
    Case("utf.8to16", [&] { return Utf8(me::convertUTF8StringToUTF16String("h\xC3\xA9 \xCE\xBB")); });
    Case("utf.16to8", [&] { return me::convertUTF16StringToUTF8String(u"hé λ"); });

    // Last: a default Array has no storage, which R2025b's API may not survive being asked about.
    Case("empty.default", [&] {
        md::Array a;
        return Text(a.isEmpty()) + " n=" + std::to_string(a.getNumberOfElements()) + " " + TypeName(a.getType());
    });
}

// --- Calls: the runtime --------------------------------------------------------------------------

class Capture : public std::basic_streambuf<char16_t>
{
public:
    std::u16string text;

protected:
    int_type overflow(int_type c) override
    {
        if (!traits_type::eq_int_type(c, traits_type::eof()))
            text.push_back(traits_type::to_char_type(c));
        return traits_type::not_eof(c);
    }
    std::streamsize xsputn(const char16_t *s, std::streamsize n) override
    {
        text.append(s, static_cast<size_t>(n));
        return n;
    }
};

// One factory, made when first needed (R2025b's resolves its library then).
static md::ArrayFactory &Made()
{
    static md::ArrayFactory factory;
    return factory;
}
#define Factory Made()

static md::Array Chars(const std::string &s) { return Factory.createCharArray(s); }

// The inputs passed in and echoed back: every kind of array the API makes.
static std::vector<std::pair<std::string, md::Array>> Inputs()
{
    md::ArrayFactory &f = Factory;
    std::vector<std::pair<std::string, md::Array>> v;
    v.push_back({"double", f.createScalar(2.5)});
    v.push_back({"negzero", f.createScalar(-0.0)});
    v.push_back({"row", f.createArray<double>({1, 3}, {1, 2, 3})});
    v.push_back({"column", f.createArray<double>({3, 1}, {1, 2, 3})});
    v.push_back({"matrix", f.createArray<double>({2, 3}, {1, 4, 2, 5, 3, 6})});
    {
        std::vector<double> d(24);
        std::iota(d.begin(), d.end(), 1.0);
        v.push_back({"nd", f.createArray({2, 3, 4}, d.begin(), d.end())});
    }
    v.push_back({"trailing", f.createArray<double>({2, 3, 1})});
    v.push_back({"empty", f.createArray<double>({0, 0})});
    v.push_back({"empty03", f.createArray<double>({0, 3})});
    v.push_back({"emptyarray", f.createEmptyArray()});
    v.push_back({"single", f.createScalar(1.5f)});
    v.push_back({"int8", f.createArray<int8_t>({1, 3}, {-128, 0, 127})});
    v.push_back({"uint8", f.createScalar<uint8_t>(200)});
    v.push_back({"int16", f.createScalar<int16_t>(-7)});
    v.push_back({"uint16", f.createScalar<uint16_t>(7)});
    v.push_back({"int32", f.createScalar<int32_t>(-100000)});
    v.push_back({"uint32", f.createScalar<uint32_t>(4000000000u)});
    v.push_back({"int64", f.createScalar<int64_t>(-9007199254740993LL)});
    v.push_back({"int64small", f.createScalar<int64_t>(-42)});
    v.push_back({"uint64", f.createScalar<uint64_t>(18446744073709551615ULL)});
    v.push_back({"uint64small", f.createScalar<uint64_t>(42)});
    v.push_back({"complex", f.createScalar(std::complex<double>(1, -1))});
    v.push_back({"complexrow", f.createArray<std::complex<double>>({1, 2}, {{1, 2}, {3, -4}})});
    v.push_back({"complexzero", f.createScalar(std::complex<double>(3, 0))});
    v.push_back({"complexsingle", f.createScalar(std::complex<float>(2, 3))});
    v.push_back({"complexint8", f.createScalar(std::complex<int8_t>(1, 2))});
    v.push_back({"logical", f.createScalar(true)});
    v.push_back({"logicalm", f.createArray<bool>({2, 2}, {true, false, false, true})});
    v.push_back({"char", f.createCharArray("hello")});
    v.push_back({"charmatrix", f.createArray<char16_t>({2, 3}, {u'a', u'd', u'b', u'e', u'c', u'f'})});
    v.push_back({"unicode", f.createCharArray(md::String(u"λ€A"))});
    v.push_back({"emptychar", f.createCharArray("")});
    v.push_back({"string", f.createScalar(md::String(u"alpha"))});
    v.push_back({"strings", f.createArray<md::MATLABString>({1, 2}, {md::MATLABString(md::String(u"alpha")), md::MATLABString(md::String(u"beta"))})});
    v.push_back({"stringm", f.createArray<md::MATLABString>({2, 2}, {md::MATLABString(md::String(u"a")), md::MATLABString(md::String(u"c")), md::MATLABString(md::String(u"b")), md::MATLABString(md::String(u"d"))})});
    v.push_back({"stringmissing", f.createScalar(md::MATLABString())});
    v.push_back({"stringsmissing", f.createArray<md::MATLABString>({1, 2}, {md::MATLABString(), md::MATLABString(md::String(u"x"))})});
    v.push_back({"emptystring", f.createScalar(md::String(u""))});
    v.push_back({"stringempty", f.createArray<md::MATLABString>({1, 0})});
    {
        md::CellArray c = f.createCellArray({1, 3});
        c[0] = f.createScalar(1.0);
        c[1] = f.createCharArray("two");
        c[2] = f.createArray<double>({2, 2}, {3, 5, 4, 6});
        v.push_back({"cell", c});
        md::CellArray col = f.createCellArray({2, 1});
        col[0] = f.createScalar(1.0);
        col[1] = f.createCharArray("two");
        v.push_back({"cellcol", col});
        md::CellArray nested = f.createCellArray({1, 2});
        md::CellArray inner = f.createCellArray({1, 1});
        inner[0] = f.createScalar(1.0);
        nested[0] = inner;
        nested[1] = f.createCellArray({0, 0});
        v.push_back({"nestedcell", nested});
        v.push_back({"emptycell", f.createCellArray({0, 0})});
        v.push_back({"defaultcell", f.createCellArray({1, 2})});
    }
    {
        md::StructArray s = f.createStructArray({1, 1}, {"name", "value"});
        s[0]["name"] = f.createCharArray("probe");
        s[0]["value"] = f.createArray<double>({1, 3}, {1, 2, 3});
        v.push_back({"struct", s});
        md::StructArray arr = f.createStructArray({1, 3}, {"a"});
        for (size_t k = 0; k < 3; k++)
            arr[k]["a"] = f.createScalar(static_cast<double>(k + 1));
        v.push_back({"structarray", arr});
        md::StructArray inner = f.createStructArray({1, 1}, {"x"});
        inner[0]["x"] = f.createScalar(1.0);
        md::StructArray outer = f.createStructArray({1, 1}, {"inner"});
        outer[0]["inner"] = inner;
        v.push_back({"structnested", outer});
        v.push_back({"emptystruct", f.createStructArray({0, 0}, {"a"})});
        v.push_back({"nofields", f.createStructArray({1, 1}, {})});
        v.push_back({"defaultstruct", f.createStructArray({1, 1}, {"p", "q"})});
    }
    {
        auto data = f.createBuffer<double>(2);
        data[0] = 5;
        data[1] = 6;
        auto rows = f.createBuffer<size_t>(2);
        rows[0] = 0;
        rows[1] = 2;
        auto cols = f.createBuffer<size_t>(2);
        cols[0] = 0;
        cols[1] = 1;
        v.push_back({"sparse", f.createSparseArray<double>({3, 3}, 2, std::move(data), std::move(rows), std::move(cols))});
        auto ld = f.createBuffer<bool>(2);
        ld[0] = true;
        ld[1] = true;
        auto lr = f.createBuffer<size_t>(2);
        lr[0] = 0;
        lr[1] = 1;
        auto lc = f.createBuffer<size_t>(2);
        lc[0] = 0;
        lc[1] = 1;
        v.push_back({"sparselogical", f.createSparseArray<bool>({2, 2}, 2, std::move(ld), std::move(lr), std::move(lc))});
        auto cd = f.createBuffer<std::complex<double>>(1);
        cd[0] = std::complex<double>(1, 1);
        auto cr = f.createBuffer<size_t>(1);
        cr[0] = 0;
        auto cc = f.createBuffer<size_t>(1);
        cc[0] = 0;
        v.push_back({"sparsecomplex", f.createSparseArray<std::complex<double>>({2, 2}, 1, std::move(cd), std::move(cr), std::move(cc))});
    }
    v.push_back({"enum", f.createEnumArray({1, 1}, "k9color", {"green"})});
    v.push_back({"enums", f.createEnumArray({1, 2}, "k9color", {"red", "blue"})});
    v.push_back({"enumbad", f.createEnumArray({1, 1}, "k9color", {"purple"})});
    v.push_back({"enumnoclass", f.createEnumArray({1, 1}, "k9nosuchclass", {"red"})});
    {
        md::buffer_ptr_t<double> b = f.createBuffer<double>(6);
        for (int k = 0; k < 6; k++)
            b[k] = k + 1;
        v.push_back({"rowmajor", f.createArrayFromBuffer<double>({2, 3}, std::move(b), md::MemoryLayout::ROW_MAJOR)});
        md::buffer_ptr_t<int16_t> c = f.createBuffer<int16_t>(4);
        for (int k = 0; k < 4; k++)
            c[k] = static_cast<int16_t>(k + 1);
        v.push_back({"rowmajorint16", f.createArrayFromBuffer<int16_t>({2, 2}, std::move(c), md::MemoryLayout::ROW_MAJOR)});
        md::buffer_ptr_t<double> d = f.createBuffer<double>(8);
        for (int k = 0; k < 8; k++)
            d[k] = k + 1;
        v.push_back({"rowmajornd", f.createArrayFromBuffer<double>({2, 2, 2}, std::move(d), md::MemoryLayout::ROW_MAJOR)});
    }
    v.push_back({"inf", f.createArray<double>({1, 3}, {INFINITY, -INFINITY, NAN})});
    return v;
}

static const char *const Kinds[] = {"double", "negzero", "row", "column", "matrix", "nd", "nd4", "trailing", "empty", "empty03", "single",
                                    "singles", "int8", "int8s", "uint8", "uint8m", "int16", "uint16", "int32", "int32s", "uint32", "int64",
                                    "uint64", "complex", "complexrow", "complexsingle", "complexint8", "logical", "logicals", "logicalm",
                                    "char", "char1", "charmatrix", "emptychar", "unicode", "string", "strings", "stringm", "stringmissing",
                                    "stringsmissing", "emptystring", "stringempty", "cell", "cellcol", "cellm", "cellstr", "cellstrcol",
                                    "nestedcell", "emptycell", "struct", "structnested", "structarray", "structm", "emptystruct", "nofields",
                                    "sparse", "sparselogical", "sparsecomplex", "inf", "nan", "pi", "enum", "enums", "point", "points",
                                    "counter", "dict", "dictnum", "dictcell", "dictempty", "dictconfigured", "handle", "datetime", "table",
                                    "map", "categorical", "duration"};

static std::string Result(const std::vector<md::Array> &r)
{
    std::string s = std::to_string(r.size()) + ":";
    for (const md::Array &a : r)
        s += " <" + Describe(a) + ">";
    return s;
}

static std::string CharText(const md::Array &a) { return Utf8(md::CharArray(a).toUTF16()); }

static int Calls()
{
    md::ArrayFactory &f = Factory;
    std::shared_ptr<mc::MATLABApplication> app;
    Case("app.start", [&] {
        app = mc::initMATLABApplication(mc::MATLABApplicationMode::IN_PROCESS, std::vector<std::u16string>());
        return std::string("ok inproc=") + Text(app->isInProc());
    });
    if (!app)
        return 1;
    std::shared_ptr<mc::MATLABLibrary> lib, var;
    Case("lib.open", [&] {
        lib = mc::initMATLABLibrary(app, u"k12lib.ctf");
        return std::string("ok");
    });
    Case("lib.open.variadic", [&] {
        var = mc::initMATLABLibrary(app, u"k12v.ctf");
        return std::string("ok");
    });
    if (!lib || !var)
        return 1;
    Case("quiet", [&] { return Describe(lib->feval(u"k9quiet", std::vector<md::Array>())); });
    Case("lib.missing", [&] { mc::initMATLABLibrary(app, u"nosuch.ctf"); return std::string("ok"); });
    Case("lib.async", [&] {
        auto fut = mc::initMATLABLibraryAsync(app, u"k12v.ctf");
        std::unique_ptr<mc::MATLABLibrary> third = fut.get();
        return CharText(third->feval(u"k9describe", f.createScalar(1.0)));
    });

    for (const char *kind : Kinds)
        Case(std::string("make.") + kind, [&] { return Describe(lib->feval(u"k9make", Chars(kind))); });

    std::vector<std::pair<std::string, md::Array>> inputs = Inputs();
    for (const auto &in : inputs)
        Case("in." + in.first, [&] { return CharText(lib->feval(u"k9describe", in.second)); });
    for (const auto &in : inputs)
        Case("echo." + in.first, [&] { return Describe(lib->feval(u"k9echo", in.second)); });

    // Objects made by MATLAB, passed back.
    Case("obj.point", [&] {
        md::Array p = lib->feval(u"k9make", Chars("point"));
        return CharText(lib->feval(u"k9describe", p)) + " norm=" + Describe(lib->feval(u"norm", p));
    });
    Case("obj.point.scaled", [&] {
        md::Array p = lib->feval(u"k9make", Chars("point"));
        md::Array q = lib->feval(u"scaled", std::vector<md::Array>{p, f.createScalar(2.0)});
        return Describe(q) + " " + CharText(lib->feval(u"k9describe", q));
    });
    Case("obj.points", [&] {
        md::Array p = lib->feval(u"k9make", Chars("points"));
        return Describe(p) + " " + CharText(lib->feval(u"k9describe", p));
    });
    Case("obj.counter", [&] {
        md::Array c = lib->feval(u"k9make", Chars("counter"));
        lib->feval(u"increment", 0, std::vector<md::Array>{c, f.createScalar(2.0)});
        md::Array copy = c;
        lib->feval(u"increment", 0, std::vector<md::Array>{copy, f.createScalar(1.0)});
        return Describe(lib->feval(u"current", c)) + " " + CharText(lib->feval(u"k9describe", c));
    });
    Case("obj.counter.isvalid", [&] {
        md::Array c = lib->feval(u"k9make", Chars("counter"));
        md::Array before = lib->feval(u"isvalid", c);
        lib->feval(u"delete", 0, std::vector<md::Array>{c});
        return Describe(before) + " " + Describe(lib->feval(u"isvalid", c));
    });
    for (const char *kind : {"enum", "enums", "dict", "handle", "datetime", "table", "map", "categorical", "duration"})
        Case(std::string("obj.back.") + kind, [&] {
            md::Array v = lib->feval(u"k9make", Chars(kind));
            return CharText(lib->feval(u"k9describe", v));
        });
    Case("obj.handle.call", [&] {
        md::Array h = lib->feval(u"k9make", Chars("handle"));
        return Describe(lib->feval(u"feval", std::vector<md::Array>{h, f.createScalar(0.0)}));
    });
    Case("obj.across.libraries", [&] {
        md::Array p = lib->feval(u"k9make", Chars("point"));
        return CharText(var->feval(u"k9describe", p));
    });

    // Functions outside the archive.
    Case("builtin.sqrt", [&] { return Describe(lib->feval(u"sqrt", f.createScalar(16.0))); });
    Case("builtin.class", [&] { return Describe(lib->feval(u"class", f.createScalar<int8_t>(1))); });
    Case("builtin.plus", [&] { return Describe(lib->feval(u"plus", std::vector<md::Array>{f.createScalar(1.0), f.createScalar(2.0)})); });
    Case("builtin.disp.nlhs1", [&] { return Describe(lib->feval(u"disp", f.createScalar(1.0))); });
    Case("unknown", [&] { return Describe(lib->feval(u"k9nosuchfunction", f.createScalar(1.0))); });
    Case("unknown.utf8name", [&] { return Describe(lib->feval(std::string("k9echo"), f.createScalar(1.0))); });
    Case("variadic.from.main", [&] { return Describe(lib->feval(u"k9nargs", std::vector<md::Array>())); });

    // Errors.
    for (const char *kind : {"plain", "id", "nested", "cause", "builtin", "undefined", "none", "bogus"})
        Case(std::string("err.") + kind, [&] { return Describe(lib->feval(u"k9err", Chars(kind))); });
    Case("err.toomany.inputs", [&] { return Describe(lib->feval(u"k9echo", std::vector<md::Array>{f.createScalar(1.0), f.createScalar(2.0)})); });
    Case("err.toomany.outputs", [&] { return Result(lib->feval(u"k9echo", 2, std::vector<md::Array>{f.createScalar(1.0)})); });
    Case("err.noinput", [&] { return Describe(lib->feval(u"k9echo", std::vector<md::Array>())); });
    Case("err.typed.reject", [&] { return Describe(lib->feval(u"k9t_real", std::vector<md::Array>{f.createCharArray("x"), f.createScalar(1.0), f.createScalar(1.0), f.createScalar(1.0)})); });
    Case("err.nv.reject", [&] { return Describe(lib->feval(u"k9nv", std::vector<md::Array>{f.createScalar(1.0), f.createCharArray("Nope"), f.createScalar(1.0)})); });

    // Output counts.
    for (size_t n : {0, 1, 2, 3, 4})
        Case("nlhs.multi." + std::to_string(n), [&] { return Result(lib->feval(u"k9multi", n, std::vector<md::Array>{f.createScalar(2.0)})); });
    Case("nlhs.noout.0", [&] { return Result(lib->feval(u"k9noout", 0, std::vector<md::Array>{f.createScalar(1.0)})); });
    Case("nlhs.noout.single", [&] { return Describe(lib->feval(u"k9noout", f.createScalar(1.0))); });
    Case("nlhs.none.0", [&] { return Result(lib->feval(u"k9t_none", 0, std::vector<md::Array>())); });
    Case("nlhs.echo.0", [&] { return Result(lib->feval(u"k9echo", 0, std::vector<md::Array>{f.createScalar(1.0)})); });
    for (size_t n : {0, 1, 3})
        Case("nargs." + std::to_string(n), [&] {
            std::vector<md::Array> args;
            for (size_t k = 0; k < n; k++)
                args.push_back(f.createScalar(static_cast<double>(k + 1)));
            return Result(var->feval(u"k9nargs", 1, args));
        });
    for (size_t n : {0, 1, 3})
        Case("nout." + std::to_string(n), [&] { return Result(var->feval(u"k9nout", n, std::vector<md::Array>{f.createScalar(2.0)})); });
    Case("nv.default", [&] { return CharText(lib->feval(u"k9nv", f.createScalar(3.0))); });
    Case("nv.given", [&] {
        return CharText(lib->feval(u"k9nv", std::vector<md::Array>{f.createScalar(3.0), f.createCharArray("Label"), f.createScalar(md::String(u"lbl")),
                                                                   f.createCharArray("scale"), f.createScalar(2.0)}));
    });

    // Text written by the functions.
    Case("print.buffers", [&] {
        auto out = std::make_shared<Capture>();
        auto err = std::make_shared<Capture>();
        md::Array r = lib->feval(u"k9print", f.createScalar(2.0), out, err);
        return Describe(r) + " out=" + Utf8(out->text) + " err=" + Utf8(err->text);
    });
    Case("print.output.only", [&] {
        auto out = std::make_shared<Capture>();
        md::Array r = lib->feval(u"k9print", f.createScalar(1.0), out);
        return Describe(r) + " out=" + Utf8(out->text);
    });
    Case("print.error.only", [&] {
        auto err = std::make_shared<Capture>();
        std::shared_ptr<me::StreamBuffer> none;
        md::Array r = lib->feval(u"k9print", f.createScalar(1.0), none, err);
        return Describe(r) + " err=" + Utf8(err->text);
    });
    Case("print.error.buffers", [&] {
        auto out = std::make_shared<Capture>();
        auto err = std::make_shared<Capture>();
        try
        {
            lib->feval(u"k9err", Chars("id"), out, err);
        }
        catch (...)
        {
            return "threw out=" + Utf8(out->text) + " err=" + Utf8(err->text);
        }
        return std::string("no error");
    });
    std::cout << "print.none.start" << std::endl;
    Case("print.none", [&] { return Describe(lib->feval(u"k9print", f.createScalar(1.0))); });
    std::cout << "print.none.end" << std::endl;

    // Asynchronous calls.
    Case("async.get", [&] {
        auto fut = lib->fevalAsync(u"k9slow", f.createScalar(1.0));
        bool waiting = fut.wait_for(std::chrono::milliseconds(0)) == std::future_status::timeout;
        md::Array r = fut.get();
        return "waiting=" + Text(waiting) + " " + Describe(r) + " valid=" + Text(fut.valid());
    });
    Case("async.multi", [&] {
        auto fut = lib->fevalAsync(u"k9multi", 3, std::vector<md::Array>{f.createScalar(2.0)});
        return Result(fut.get());
    });
    Case("async.error", [&] {
        auto fut = lib->fevalAsync(u"k9err", Chars("id"));
        return Describe(fut.get());
    });
    Case("async.share", [&] {
        auto fut = lib->fevalAsync(u"k9echo", f.createScalar(4.0));
        auto shared = fut.share();
        md::Array a = shared.get();
        md::Array b = shared.get();
        return Describe(a) + " " + Describe(b) + " valid=" + Text(fut.valid()) + Text(shared.valid());
    });
    Case("async.cancel.queued", [&] {
        auto slow = lib->fevalAsync(u"k9slow", f.createScalar(2.0));
        auto quick = lib->fevalAsync(u"k9echo", f.createScalar(5.0));
        bool cancelled = quick.cancel();
        std::string s = "cancel=" + Text(cancelled);
        try
        {
            s += " quick " + Describe(quick.get());
        }
        catch (...)
        {
            s += " quick ERR " + Failure();
        }
        s += " slow " + Describe(slow.get());
        return s;
    });
    Case("async.cancel.running.keep", [&] {
        auto slow = lib->fevalAsync(u"k9slow", f.createScalar(2.0));
        std::this_thread::sleep_for(std::chrono::milliseconds(500));
        bool cancelled = slow.cancel(false);
        std::string s = "cancel=" + Text(cancelled);
        try
        {
            s += " " + Describe(slow.get());
        }
        catch (...)
        {
            s += " ERR " + Failure();
        }
        return s;
    });
    Case("async.cancel.running.interrupt", [&] {
        auto slow = lib->fevalAsync(u"k9slow", f.createScalar(3.0));
        std::this_thread::sleep_for(std::chrono::milliseconds(500));
        bool cancelled = slow.cancel(true);
        std::string s = "cancel=" + Text(cancelled);
        try
        {
            s += " " + Describe(slow.get());
        }
        catch (...)
        {
            s += " ERR " + Failure();
        }
        return s;
    });
    Case("async.cancel.done", [&] {
        auto fut = lib->fevalAsync(u"k9echo", f.createScalar(1.0));
        fut.wait();
        bool cancelled = fut.cancel();
        return "cancel=" + Text(cancelled) + " " + Describe(fut.get());
    });

    // feval's native forms.
    Case("native.double", [&] { return Text(lib->feval<double>(u"k9echo", 2.5)); });
    Case("native.int", [&] { return Text(lib->feval<int32_t>(u"k9echo", 7)); });
    Case("native.bool", [&] { return Text(lib->feval<bool>(u"k9echo", true)); });
    Case("native.complex", [&] { return Text(lib->feval<std::complex<double>>(u"k9echo", std::complex<double>(1, 2))); });
    Case("native.string", [&] { return lib->feval<std::string>(u"k9echo", std::string("abc")); });
    Case("native.u16", [&] { return Utf8(lib->feval<std::u16string>(u"k9echo", std::u16string(u"λx"))); });
    Case("native.char.result", [&] { return lib->feval<std::string>(u"k9make", std::string("char")); });
    Case("native.vector", [&] {
        std::vector<double> v = lib->feval<std::vector<double>>(u"k9make", std::string("row"));
        std::string s;
        for (double d : v)
            s += Text(d) + " ";
        return s;
    });
    Case("native.vector.in", [&] {
        std::vector<double> in{1, 2, 3};
        return CharText(lib->feval<md::Array>(u"k9describe", in));
    });
    Case("native.strings", [&] {
        std::vector<std::string> v = lib->feval<std::vector<std::string>>(u"k9make", std::string("strings"));
        std::string s;
        for (const std::string &x : v)
            s += x + ";";
        return s;
    });
    Case("native.tuple", [&] {
        std::tuple<double, double, std::string> t = lib->feval<std::tuple<double, double, std::string>>(u"k9multi", 2.0);
        return Text(std::get<0>(t)) + " " + Text(std::get<1>(t)) + " " + std::get<2>(t);
    });
    Case("native.void", [&] {
        lib->feval<void>(u"k9noout", 1.0);
        return std::string("ok");
    });
    Case("native.array", [&] {
        md::Array three = f.createScalar(3.0);
        return Describe(lib->feval<md::Array>(u"k9echo", three));
    });
    Case("native.mismatch", [&] { return Text(lib->feval<double>(u"k9make", std::string("int32"))); });
    Case("native.empty", [&] { return Text(lib->feval<double>(u"k9make", std::string("empty"))); });
    Case("native.string.mismatch", [&] { return lib->feval<std::string>(u"k9make", std::string("double")); });
    Case("native.error", [&] { return Text(lib->feval<double>(u"k9err", std::string("id"))); });
    Case("native.async", [&] {
        auto fut = lib->fevalAsync<double>(u"k9echo", 6.0);
        return Text(fut.get());
    });
    Case("native.buffers", [&] {
        auto out = std::make_shared<Capture>();
        auto err = std::make_shared<Capture>();
        const std::shared_ptr<me::StreamBuffer> o = out, e = err;
        double r = lib->feval<double>(u"k9print", o, e, 1.0);
        return Text(r) + " out=" + Utf8(out->text) + " err=" + Utf8(err->text);
    });

    Case("figures", [&] {
        lib->waitForFiguresToClose();
        return std::string("returned");
    });
    Case("lib.close", [&] {
        var.reset();
        return std::string("closed");
    });
    Case("lib.after.other.closed", [&] { return Describe(lib->feval(u"k9echo", f.createScalar(1.0))); });
    return 0;
}

// --- Paths: where an archive is found ------------------------------------------------------------

static int Paths(int argc, char **argv)
{
    auto app = mc::initMATLABApplication(mc::MATLABApplicationMode::IN_PROCESS, std::vector<std::u16string>());
    for (int k = 2; k < argc; k++)
    {
        std::string name = argv[k];
        std::u16string u16(name.begin(), name.end());
        Case("open." + name, [&] {
            std::shared_ptr<mc::MATLABLibrary> lib = mc::initMATLABLibrary(app, u16);
            // Which archive was found: each candidate holds a different set of functions. A name
            // the archive lacks is refused before anything runs, so no MATLAB error (and no beep).
            std::string has;
            for (const char16_t *fn : {u"k9make", u"k9nargs", u"k9echo"})
            {
                try
                {
                    lib->feval(fn, Factory.createCharArray("double"));
                    has += Utf8(fn) + " ";
                }
                catch (...)
                {
                }
            }
            return "has " + has;
        });
    }
    return 0;
}

// --- Twice: one application per process ----------------------------------------------------------

static int Twice()
{
    {
        std::shared_ptr<mc::MATLABApplication> app;
        Case("first", [&] {
            app = mc::initMATLABApplication(mc::MATLABApplicationMode::IN_PROCESS, std::vector<std::u16string>());
            return std::string("ok");
        });
        Case("second.while.running", [&] {
            mc::initMATLABApplication(mc::MATLABApplicationMode::IN_PROCESS, std::vector<std::u16string>());
            return std::string("ok");
        });
        Case("second.outproc.while.running", [&] {
            mc::initMATLABApplication(mc::MATLABApplicationMode::OUT_OF_PROCESS, std::vector<std::u16string>());
            return std::string("ok");
        });
        Case("call", [&] {
            std::shared_ptr<mc::MATLABLibrary> lib = mc::initMATLABLibrary(app, u"k12lib.ctf");
            lib->feval(u"k9quiet", std::vector<md::Array>());
            return Describe(lib->feval(u"k9echo", Factory.createScalar(1.0)));
        });
        Case("lib.two.of.one", [&] {
            std::shared_ptr<mc::MATLABLibrary> a = mc::initMATLABLibrary(app, u"k12lib.ctf");
            std::shared_ptr<mc::MATLABLibrary> b = mc::initMATLABLibrary(app, u"k12lib.ctf");
            md::Array c = a->feval(u"k9make", Chars("counter"));
            return CharText(b->feval(u"k9describe", c));
        });
    }
    Case("after.end", [&] {
        mc::initMATLABApplication(mc::MATLABApplicationMode::IN_PROCESS, std::vector<std::u16string>());
        return std::string("ok");
    });
    return 0;
}

// --- Out of process ------------------------------------------------------------------------------

static int OutOfProcess()
{
    std::shared_ptr<mc::MATLABApplication> app;
    Case("app.start", [&] {
        app = mc::initMATLABApplication(mc::MATLABApplicationMode::OUT_OF_PROCESS, std::vector<std::u16string>());
        return std::string("ok inproc=") + Text(app->isInProc());
    });
    if (!app)
        return 1;
    std::shared_ptr<mc::MATLABLibrary> lib;
    Case("lib.open", [&] {
        lib = mc::initMATLABLibrary(app, u"k12lib.ctf");
        return std::string("ok");
    });
    if (!lib)
        return 1;
    Case("quiet", [&] { return Describe(lib->feval(u"k9quiet", std::vector<md::Array>())); });
    Case("echo", [&] { return Describe(lib->feval(u"k9echo", Factory.createArray<double>({1, 2}, {1, 2}))); });
    Case("describe", [&] { return CharText(lib->feval(u"k9describe", Factory.createScalar(md::String(u"x")))); });
    Case("error", [&] { return Describe(lib->feval(u"k9err", Chars("id"))); });
    Case("print", [&] {
        auto out = std::make_shared<Capture>();
        md::Array r = lib->feval(u"k9print", Factory.createScalar(1.0), out);
        return Describe(r) + " out=" + Utf8(out->text);
    });
    return 0;
}

// --- runMain -------------------------------------------------------------------------------------

static int Main(std::shared_ptr<mc::MATLABApplication> app, int argc, const char **argv)
{
    Line("main.argc", std::to_string(argc));
    Line("main.argv1", argc > 1 ? argv[1] : "");
    Case("main.call", [&] {
        std::shared_ptr<mc::MATLABLibrary> lib = mc::initMATLABLibrary(app, u"k12lib.ctf");
        lib->feval(u"k9quiet", std::vector<md::Array>());
        return Describe(lib->feval(u"k9echo", Factory.createScalar(1.0)));
    });
    return 7;
}

static int RunMain(int argc, char **argv)
{
    auto app = mc::initMATLABApplication(mc::MATLABApplicationMode::IN_PROCESS, std::vector<std::u16string>());
    int code = mc::runMain(Main, std::move(app), argc, const_cast<const char **>(argv));
    Line("runmain.returned", std::to_string(code));
    return 0;
}

int Typed();

int main(int argc, char **argv)
{
    std::string mode = argc > 1 ? argv[1] : "local";
    int code = 0;
    try
    {
        if (mode == "local")
            Local();
        else if (mode == "calls")
            code = Calls();
        else if (mode == "typed")
            code = Typed();
        else if (mode == "paths")
            code = Paths(argc, argv);
        else if (mode == "twice")
            code = Twice();
        else if (mode == "outproc")
            code = OutOfProcess();
        else if (mode == "runmain")
            code = RunMain(argc, argv);
        else
        {
            Line("mode", "unknown " + mode);
            code = 2;
        }
    }
    catch (...)
    {
        Line("uncaught", Failure());
        code = 3;
    }
    Line("end", mode);
    return code;
}

// --- Typed: the strongly typed interface ---------------------------------------------------------

int Typed()
{
    md::ArrayFactory &f = Factory;
    auto app = mc::initMATLABApplication(mc::MATLABApplicationMode::IN_PROCESS, std::vector<std::u16string>());
    std::shared_ptr<mc::MATLABLibrary> lib = mc::initMATLABLibrary(app, u"k12lib.ctf");
    k9quiet(lib);

    // Functions.
    Case("t.describe", [&] { return CharText(k9describe(lib, f.createScalar(1.0))); });
    Case("t.make", [&] { return Describe(k9make(lib, Chars("row"))); });
    Case("t.echo.0", [&] {
        k9echo<0>(lib, f.createScalar(1.0));
        return std::string("ok");
    });
    Case("t.multi.1", [&] { return Describe(k9multi(lib, f.createScalar(2.0))); });
    Case("t.multi.2", [&] {
        auto t = k9multi<2>(lib, f.createScalar(2.0));
        return Describe(std::get<0>(t)) + " " + Describe(std::get<1>(t));
    });
    Case("t.multi.3", [&] {
        auto t = k9multi<3>(lib, f.createScalar(2.0));
        return Describe(std::get<2>(t));
    });
    Case("t.nv", [&] { return CharText(k9nv(lib, 3.0, 2.0, u"lbl")); });
    Case("t.nv.complex", [&] { return CharText(k9nv(lib, std::complex<double>(3, 0), std::complex<double>(2, 0), u"lbl")); });
    Case("t.slow", [&] { return Describe(k9slow(lib, f.createScalar(0.0))); });
    Case("t.err", [&] { return Describe(k9err(lib, Chars("id"))); });
    Case("t.err.0", [&] {
        k9err<0>(lib, Chars("none"));
        return std::string("ok");
    });
    std::cout << "t.noout.start" << std::endl;
    Case("t.noout", [&] {
        k9noout(lib, f.createScalar(1.0));
        return std::string("ok");
    });
    Case("t.none", [&] {
        k9t_none(lib);
        return std::string("ok");
    });
    std::cout << "t.none.end" << std::endl;
    Case("t.real", [&] {
        md::Array c = f.createArray<double>({2, 2}, {1, 2, 3, 4});
        md::Array d = f.createArray<double>({2, 3, 4});
        return Describe(k9t_real(lib, 1.0, std::vector<double>{1, 2}, c, d));
    });
    Case("t.real.badsize", [&] {
        md::Array c = f.createArray<double>({2, 2}, {1, 2, 3, 4});
        md::Array d = f.createArray<double>({2, 3});
        return Describe(k9t_real(lib, 1.0, std::vector<double>{1, 2}, c, d));
    });
    Case("t.cplx", [&] { return Describe(k9t_cplx(lib, 1.0, std::vector<double>{1, 2}, 2.0f)); });
    Case("t.cplx.complex", [&] {
        return Describe(k9t_cplx(lib, std::complex<double>(1, 1), std::vector<std::complex<double>>{{1, 0}, {0, 1}}, std::complex<float>(2, 0)));
    });
    Case("t.ints", [&] {
        return Describe(k9t_ints(lib, static_cast<int8_t>(1), std::vector<uint8_t>{2, 3}, static_cast<int16_t>(4), static_cast<uint16_t>(5),
                                 static_cast<int32_t>(6), f.createArray<uint32_t>({1, 1}, {7}), static_cast<int64_t>(8), static_cast<uint64_t>(9),
                                 static_cast<int32_t>(10)));
    });
    Case("t.ints.complex", [&] {
        return Describe(k9t_ints(lib, static_cast<int8_t>(1), std::vector<uint8_t>{2, 3}, static_cast<int16_t>(4), static_cast<uint16_t>(5),
                                 static_cast<int32_t>(6), f.createArray<uint32_t>({1, 1}, {7}), static_cast<int64_t>(8), static_cast<uint64_t>(9),
                                 std::complex<int32_t>(10, 0)));
    });
    Case("t.misc", [&] {
        k9point p(lib, f.createScalar(1.0), f.createScalar(2.0));
        std::vector<k9point> q{p, p};
        md::CellArray g = f.createCellArray({1, 1});
        g[0] = f.createScalar(1.0);
        md::StructArray k = f.createStructArray({1, 1}, {"a"});
        k[0]["a"] = f.createScalar(1.0);
        md::Array dict = lib->feval(u"k9make", Chars("dict"));
        return Describe(k9t_misc(lib, true, std::vector<bool>{true, false}, u"chars", u'c', u"str", std::vector<std::u16string>{u"a", u"b"}, g, g, k,
                                 k, k9color::blue, p, q, dict, f.createScalar(1.0), f.createScalar(2.0)));
    });
    Case("t.untyped", [&] { return Describe(k9t_untyped(lib, f.createScalar(1.0), f.createScalar(2.0))); });
    Case("t.opt", [&] { return CharText(k9t_opt(lib, 1.0, 2.0, u"z")); });
    Case("t.ref.1", [&] { return Describe(k9t_ref(lib, std::vector<double>{1, 2}, 3.0)); });
    Case("t.ref.2", [&] {
        auto t = k9t_ref<2>(lib, std::vector<double>{1, 2}, 3.0);
        return Describe(std::get<0>(t)) + " " + Describe(std::get<1>(t));
    });
    Case("t.keyword", [&] { return Describe(k9t_keyword(lib, f.createScalar(1.0), f.createScalar(2.0), f.createScalar(3.0))); });
    Case("t.typed.multi.1", [&] { return Text(k9t_multi(lib, std::vector<double>{1, 2, 3})); });
    Case("t.typed.multi.2", [&] {
        auto t = k9t_multi<2>(lib, std::vector<double>{1, 2, 3});
        return Text(std::get<0>(t)) + " " + Text(std::get<1>(t));
    });
    Case("t.typed.multi.3", [&] {
        auto t = k9t_multi<3>(lib, std::vector<double>{1, 2, 3});
        return Text(std::get<0>(t)) + " " + Text(std::get<1>(t)) + " " + Utf8(std::get<2>(t));
    });
    Case("t.objects.1", [&] {
        k9point p = k9t_objects<1>(lib, k9color::green);
        return Text(p.getX()) + " " + Text(p.getY());
    });
    Case("t.objects.2", [&] {
        auto t = k9t_objects<2>(lib, k9color::blue);
        return Text(std::get<0>(t).getX()) + " " + getk9colorString(std::get<1>(t));
    });
    Case("t.inner", [&] { return Describe(k9pkg::k9inner(lib, 5.0)); });

    // Classes.
    Case("c.point", [&] {
        k9point p(lib, f.createScalar(3.0), f.createScalar(4.0));
        return Text(p.getX()) + " " + Text(p.getY()) + " " + Utf8(p.getTag()) + " norm=" + Describe(p.norm());
    });
    Case("c.point.set", [&] {
        k9point p(lib, f.createScalar(3.0), f.createScalar(4.0));
        p.setX(10.0);
        p.setY(std::complex<double>(1, 2));
        return Text(p.getX()) + " " + Text(p.getY());
    });
    Case("c.point.scaled", [&] {
        k9point p(lib, f.createScalar(3.0), f.createScalar(4.0));
        md::Array q = p.scaled(2.0);
        return Describe(q) + " " + CharText(lib->feval(u"k9describe", q));
    });
    Case("c.point.parts", [&] {
        k9point p(lib, f.createScalar(3.0), f.createScalar(4.0));
        auto t = p.parts<2>();
        return Describe(std::get<0>(t)) + " " + Describe(std::get<1>(t)) + " one=" + Describe(p.parts());
    });
    Case("c.point.array", [&] {
        k9point p(lib, f.createScalar(3.0), f.createScalar(4.0));
        md::Array a = p;
        return Describe(a);
    });
    Case("c.counter", [&] {
        k9counter c(lib, f.createScalar(5.0));
        c.increment(2.0);
        return Text(c.getCount()) + " " + Describe(c.current()) + " valid=" + Text(c.isvalid());
    });
    Case("c.counter.set", [&] {
        k9counter c(lib, f.createScalar(5.0));
        c.setCount(9.0);
        c.setLabel(Chars("lbl"));
        return Text(c.getCount()) + " " + Describe(c.getLabel());
    });
    Case("c.counter.compare", [&] {
        k9counter a(lib, f.createScalar(1.0));
        k9counter b(lib, f.createScalar(1.0));
        k9counter a2 = a;
        return Text(a == a2) + Text(a == b) + Text(a != b);
    });
    Case("c.counter.shared", [&] {
        k9counter a(lib, f.createScalar(1.0));
        k9counter a2 = a;
        a2.increment(5.0);
        return Text(a.getCount());
    });
    Case("c.opt", [&] {
        k9opt o(lib);
        o.bump(3.0);
        o.setTotal(o.getTotal() + 1);
        return Text(o.getTotal()) + " twice=" + Describe(o.getTwice());
    });
    Case("c.opt.lt", [&] {
        k9opt a(lib), b(lib);
        b.bump(1.0);
        return Text(a < b) + Text(b < a) + " " + Describe(a.lt(b));
    });
    Case("c.opt.ge", [&] {
        k9opt a(lib), b(lib);
        return Text(a >= b) + Text(a <= b);
    });
    Case("e.color", [&] { return getk9colorString(k9color::red) + " " + std::to_string(static_cast<int>(getk9colorEnum("blue"))); });
    return 0;
}
