// mxarray.cpp - the mxArray C API (compiler and SDK plan, stage K10).
//
// Every behaviour here is the one record-k10.ps1 measured R2025b's runtime giving a C program
// (recordings k10_mx_separate and k10_mx_interleaved), except where R2025b crashes or answers
// against its own documentation, where the right answer is given instead (ADR 0266):
//   - mxGetPr of a complex array in the interleaved model answers NULL (R2025b's throws and ends
//     the process);
//   - mxIsClass compares a reference's class name too (R2025b's answers false for an object of
//     the very class it names);
//   - mxGetProperty of a property the object does not have answers NULL (R2025b's crashes).
// R2025b's answers to misuse that harms nothing are kept: mxCreateNumericMatrix of a cell, char
// or logical class makes one, field names are taken as given, an index past the end answers NULL
// or does nothing, and mxGetPr, mxGetChars and mxGetLogicals answer the data whatever the class.
#include "jgrt_internal.h"

#include <cfloat>
#include <cmath>
#include <cstdlib>
#include <cstring>
#include <limits>
#include <vector>

// The separate complex model's functions, which matrix.h declares only in that model.
extern "C" {
JGRT_API void* mxGetData_730(const mxArray* pa);
JGRT_API void mxSetData_730(mxArray* pa, void* newdata);
JGRT_API double* mxGetPr_730(const mxArray* pa);
JGRT_API void mxSetPr_730(mxArray* pa, double* newdata);
JGRT_API size_t mxGetElementSize_730(const mxArray* pa);
JGRT_API double* mxGetPi_730(const mxArray* pa);
JGRT_API void mxSetPi_730(mxArray* pa, double* pi);
JGRT_API void* mxGetImagData_730(const mxArray* pa);
JGRT_API void mxSetImagData_730(mxArray* pa, void* newdata);
}

namespace
{
const int ReferenceClass = JgrtReferenceClass;

bool IsNumericClass(int c) { return c >= mxDOUBLE_CLASS && c <= mxUINT64_CLASS; }

// Whether the class keeps its elements in data, as numbers or code units.
bool HoldsElements(int c) { return IsNumericClass(c) || c == mxLOGICAL_CLASS || c == mxCHAR_CLASS; }

size_t BaseSize(int c)
{
    switch (c)
    {
    case mxDOUBLE_CLASS: case mxINT64_CLASS: case mxUINT64_CLASS: return 8;
    case mxSINGLE_CLASS: case mxINT32_CLASS: case mxUINT32_CLASS: return 4;
    case mxINT16_CLASS: case mxUINT16_CLASS: case mxCHAR_CLASS: return 2;
    case mxINT8_CLASS: case mxUINT8_CLASS: case mxLOGICAL_CLASS: return 1;
    case mxCELL_CLASS: case mxSTRUCT_CLASS: return sizeof(void*);
    default: return 0;
    }
}

size_t Count(const mxArray* a)
{
    size_t n = 1;
    for (mwSize k = 0; k < a->ndims; k++)
    {
        n *= a->dims[k];
    }

    return n;
}

// How many stored elements data holds: a sparse array's capacity, else every element.
size_t Stored(const mxArray* a) { return a->sparse ? a->nzmax : Count(a); }

// A size as R2025b keeps it: fewer than two dimensions made two (one n as n-by-1, none as
// 0-by-0), and trailing singleton dimensions past the second dropped.
void SetDims(mxArray* a, mwSize ndim, const mwSize* dims)
{
    std::vector<mwSize> shape;
    if (ndim == 0 || dims == nullptr)
    {
        shape = { 0, 0 };
    }
    else if (ndim == 1)
    {
        shape = { dims[0], 1 };
    }
    else
    {
        shape.assign(dims, dims + ndim);
        while (shape.size() > 2 && shape.back() == 1)
        {
            shape.pop_back();
        }
    }

    mwSize* made = static_cast<mwSize*>(malloc(shape.size() * sizeof(mwSize)));
    memcpy(made, shape.data(), shape.size() * sizeof(mwSize));
    free(a->dims);
    a->dims = made;
    a->ndims = shape.size();
}

mxArray* NewArray(int classId, mwSize ndim, const mwSize* dims)
{
    mxArray* a = static_cast<mxArray*>(calloc(1, sizeof(mxArray)));
    if (a == nullptr)
    {
        return nullptr;
    }

    a->classId = classId;
    SetDims(a, ndim, dims);
    return a;
}

// Allocates an array's storage, zeroed: its elements (two parts side by side for a complex one),
// a cell's or struct's pointers, a sparse array's indices. Nothing is allocated for no elements.
void Allocate(mxArray* a)
{
    size_t stored = Stored(a);
    if (a->classId == mxSTRUCT_CLASS)
    {
        size_t slots = stored * static_cast<size_t>(a->nfields);
        a->data = slots > 0 ? calloc(slots, sizeof(mxArray*)) : nullptr;
    }
    else if (a->classId == mxCELL_CLASS || HoldsElements(a->classId))
    {
        size_t width = BaseSize(a->classId) * (a->complex ? 2 : 1);
        a->data = stored > 0 ? calloc(stored, width) : nullptr;
    }

    if (a->sparse)
    {
        a->ir = static_cast<mwIndex*>(calloc(a->nzmax, sizeof(mwIndex)));
        a->jc = static_cast<mwIndex*>(calloc(a->dims[1] + 1, sizeof(mwIndex)));
    }
}

// A complex array's storage in the interleaved layout, rearranged from the separate one.
void ToInterleaved(mxArray* a)
{
    if (!a->complex || !a->separate)
    {
        return;
    }

    size_t n = Stored(a), b = BaseSize(a->classId);
    char* both = n > 0 ? static_cast<char*>(calloc(n, 2 * b)) : nullptr;
    const char* re = static_cast<const char*>(a->data);
    const char* im = static_cast<const char*>(a->imag);
    for (size_t i = 0; both != nullptr && i < n; i++)
    {
        if (re != nullptr)
        {
            memcpy(both + (2 * i * b), re + (i * b), b);
        }

        if (im != nullptr)
        {
            memcpy(both + ((2 * i + 1) * b), im + (i * b), b);
        }
    }

    free(a->data);
    free(a->imag);
    a->data = both;
    a->imag = nullptr;
    a->separate = false;
}

// A complex array's storage in the separate layout, rearranged from the interleaved one.
void ToSeparate(mxArray* a)
{
    if (!a->complex || a->separate)
    {
        return;
    }

    size_t n = Stored(a), b = BaseSize(a->classId);
    char* re = n > 0 ? static_cast<char*>(calloc(n, b)) : nullptr;
    char* im = n > 0 ? static_cast<char*>(calloc(n, b)) : nullptr;
    const char* both = static_cast<const char*>(a->data);
    for (size_t i = 0; both != nullptr && re != nullptr && im != nullptr && i < n; i++)
    {
        memcpy(re + (i * b), both + (2 * i * b), b);
        memcpy(im + (i * b), both + ((2 * i + 1) * b), b);
    }

    free(a->data);
    a->data = re;
    a->imag = im;
    a->separate = true;
}

char* CopyText(const char* text)
{
    size_t n = strlen(text) + 1;
    char* made = static_cast<char*>(malloc(n));
    memcpy(made, text, n);
    return made;
}

mxArray** Slots(const mxArray* a) { return static_cast<mxArray**>(a->data); }

mxArray* NumericArray(mwSize ndim, const mwSize* dims, mxClassID classId, mxComplexity complexity);

mxArray* Characters(const std::wstring& text)
{
    mwSize dims[] = { text.empty() ? 0u : 1u, text.size() };
    mxArray* a = NewArray(mxCHAR_CLASS, 2, dims);
    if (a == nullptr)
    {
        return nullptr;
    }

    Allocate(a);
    if (!text.empty())
    {
        memcpy(a->data, text.data(), text.size() * sizeof(mxChar));
    }

    return a;
}

mxArray* StructArray(mwSize ndim, const mwSize* dims, int nfields, const char** names)
{
    mxArray* a = NewArray(mxSTRUCT_CLASS, ndim, dims);
    if (a == nullptr)
    {
        return nullptr;
    }

    a->nfields = nfields > 0 ? nfields : 0;
    a->fieldNames = a->nfields > 0 ? static_cast<char**>(calloc(a->nfields, sizeof(char*))) : nullptr;
    for (int f = 0; f < a->nfields; f++)
    {
        a->fieldNames[f] = CopyText(names != nullptr && names[f] != nullptr ? names[f] : "");
    }

    Allocate(a);
    return a;
}

mxArray* NumericArray(mwSize ndim, const mwSize* dims, mxClassID classId, mxComplexity complexity)
{
    // R2025b makes what the class names, numeric or not (k10_mx risky 8 to 10).
    if (classId == mxCELL_CLASS || classId == mxCHAR_CLASS || classId == mxLOGICAL_CLASS)
    {
        mxArray* other = NewArray(classId, ndim, dims);
        if (other != nullptr)
        {
            Allocate(other);
        }

        return other;
    }

    if (classId == mxSTRUCT_CLASS)
    {
        return StructArray(ndim, dims, 0, nullptr);
    }

    if (!IsNumericClass(classId))
    {
        return nullptr;
    }

    mxArray* a = NewArray(classId, ndim, dims);
    if (a == nullptr)
    {
        return nullptr;
    }

    a->complex = complexity == mxCOMPLEX;
    Allocate(a);
    return a;
}

mxArray* SparseArray(mwSize m, mwSize n, mwSize nzmax, int classId, bool complex)
{
    mwSize dims[] = { m, n };
    mxArray* a = NewArray(classId, 2, dims);
    if (a == nullptr)
    {
        return nullptr;
    }

    a->sparse = true;
    a->complex = complex;
    a->nzmax = nzmax > 0 ? nzmax : 1;
    Allocate(a);
    return a;
}

// The text of a char array, every code unit in column-major order; empty for anything else.
bool TextOf(const mxArray* a, std::wstring& text)
{
    if (a == nullptr || a->classId != mxCHAR_CLASS)
    {
        return false;
    }

    size_t n = Count(a);
    text.assign(static_cast<const wchar_t*>(a->data), a->data == nullptr ? 0 : n);
    return true;
}

char* Owned(const std::string& bytes)
{
    char* made = static_cast<char*>(malloc(bytes.size() + 1));
    memcpy(made, bytes.data(), bytes.size());
    made[bytes.size()] = '\0';
    return made;
}

int FieldIndex(const mxArray* a, const char* name)
{
    if (a == nullptr || a->classId != mxSTRUCT_CLASS || name == nullptr)
    {
        return -1;
    }

    for (int f = 0; f < a->nfields; f++)
    {
        if (strcmp(a->fieldNames[f], name) == 0)
        {
            return f;
        }
    }

    return -1;
}

const char* ClassName(int c)
{
    switch (c)
    {
    case mxCELL_CLASS: return "cell";
    case mxSTRUCT_CLASS: return "struct";
    case mxLOGICAL_CLASS: return "logical";
    case mxCHAR_CLASS: return "char";
    case mxVOID_CLASS: return "void";
    case mxDOUBLE_CLASS: return "double";
    case mxSINGLE_CLASS: return "single";
    case mxINT8_CLASS: return "int8";
    case mxUINT8_CLASS: return "uint8";
    case mxINT16_CLASS: return "int16";
    case mxUINT16_CLASS: return "uint16";
    case mxINT32_CLASS: return "int32";
    case mxUINT32_CLASS: return "uint32";
    case mxINT64_CLASS: return "int64";
    case mxUINT64_CLASS: return "uint64";
    case mxFUNCTION_CLASS: return "function_handle";
    case mxOPAQUE_CLASS: return "opaque";
    case mxOBJECT_CLASS: return "object";
    default: return "unknown";
    }
}

double ReadReal(int c, const void* data, size_t i)
{
    switch (c)
    {
    case mxDOUBLE_CLASS: return static_cast<const double*>(data)[i];
    case mxSINGLE_CLASS: return static_cast<const float*>(data)[i];
    case mxINT8_CLASS: return static_cast<const int8_t*>(data)[i];
    case mxUINT8_CLASS: return static_cast<const uint8_t*>(data)[i];
    case mxINT16_CLASS: return static_cast<const int16_t*>(data)[i];
    case mxUINT16_CLASS: return static_cast<const uint16_t*>(data)[i];
    case mxINT32_CLASS: return static_cast<const int32_t*>(data)[i];
    case mxUINT32_CLASS: return static_cast<const uint32_t*>(data)[i];
    case mxINT64_CLASS: return static_cast<double>(static_cast<const int64_t*>(data)[i]);
    case mxUINT64_CLASS: return static_cast<double>(static_cast<const uint64_t*>(data)[i]);
    case mxLOGICAL_CLASS: return static_cast<const uint8_t*>(data)[i] != 0 ? 1.0 : 0.0;
    case mxCHAR_CLASS: return static_cast<const uint16_t*>(data)[i];
    default: return 0;
    }
}
}

// ---- Text ------------------------------------------------------------------------------------

std::wstring JgrtFromUtf8(const char* bytes, size_t count)
{
    if (count == 0)
    {
        return std::wstring();
    }

    int n = MultiByteToWideChar(CP_UTF8, 0, bytes, static_cast<int>(count), nullptr, 0);
    std::wstring wide(static_cast<size_t>(n), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, bytes, static_cast<int>(count), wide.data(), n);
    return wide;
}

// Bytes a C program gave as text: UTF-8 when they are valid UTF-8, else the ANSI code page's, as
// R2025b's mxCreateString reads "caf\xC3\xA9" and "caf\xE9" alike (k10_mx create.utf8, create.latin1).
std::wstring JgrtFromBytes(const char* bytes, size_t count)
{
    if (count == 0)
    {
        return std::wstring();
    }

    int n = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, bytes, static_cast<int>(count), nullptr, 0);
    UINT page = n > 0 ? CP_UTF8 : CP_ACP;
    DWORD flags = n > 0 ? MB_ERR_INVALID_CHARS : 0;
    if (n <= 0)
    {
        n = MultiByteToWideChar(CP_ACP, 0, bytes, static_cast<int>(count), nullptr, 0);
    }

    std::wstring wide(static_cast<size_t>(n), L'\0');
    MultiByteToWideChar(page, flags, bytes, static_cast<int>(count), wide.data(), n);
    return wide;
}

// Text in the ANSI code page, a character it cannot hold as one 0x1A, as R2025b's mxArrayToString
// writes "c\xE9\x80\x1A" for 'cé€' and an emoji (k10_mx string.wide.tostring): a character beyond
// the Basic Multilingual Plane, two UTF-16 units, is one character, so one 0x1A. A system whose
// ANSI code page is UTF-8 gets UTF-8.
std::string JgrtToAnsi(const wchar_t* text, size_t count)
{
    if (count == 0)
    {
        return std::string();
    }

    if (GetACP() == CP_UTF8)
    {
        return JgrtToUtf8(text, count);
    }

    std::wstring units(text, count);
    for (size_t i = 0; i + 1 < units.size(); i++)
    {
        if (IS_HIGH_SURROGATE(units[i]) && IS_LOW_SURROGATE(units[i + 1]))
        {
            units.replace(i, 2, 1, L'\xFFFD');
        }
    }

    BOOL used = FALSE;
    int n = WideCharToMultiByte(CP_ACP, 0, units.data(), static_cast<int>(units.size()), nullptr, 0, "\x1A", &used);
    std::string bytes(static_cast<size_t>(n), '\0');
    WideCharToMultiByte(CP_ACP, 0, units.data(), static_cast<int>(units.size()), bytes.data(), n, "\x1A", &used);
    return bytes;
}

std::string JgrtToUtf8(const wchar_t* text, size_t count)
{
    if (count == 0)
    {
        return std::string();
    }

    int n = WideCharToMultiByte(CP_UTF8, 0, text, static_cast<int>(count), nullptr, 0, nullptr, nullptr);
    std::string bytes(static_cast<size_t>(n), '\0');
    WideCharToMultiByte(CP_UTF8, 0, text, static_cast<int>(count), bytes.data(), n, nullptr, nullptr);
    return bytes;
}

// ---- Memory ----------------------------------------------------------------------------------

void* mxMalloc(size_t n) { return malloc(n > 0 ? n : 1); }

void* mxCalloc(size_t n, size_t size) { return calloc(n > 0 ? n : 1, size > 0 ? size : 1); }

void* mxRealloc(void* ptr, size_t size) { return realloc(ptr, size > 0 ? size : 1); }

void mxFree(void* ptr) { free(ptr); }

// ---- Making and destroying arrays ------------------------------------------------------------

mxArray* mxCreateDoubleMatrix(mwSize m, mwSize n, mxComplexity complexity)
{
    mwSize dims[] = { m, n };
    return NumericArray(2, dims, mxDOUBLE_CLASS, complexity);
}

mxArray* mxCreateDoubleScalar(double value)
{
    mxArray* a = mxCreateDoubleMatrix(1, 1, mxREAL);
    if (a != nullptr)
    {
        static_cast<double*>(a->data)[0] = value;
    }

    return a;
}

mxArray* mxCreateNumericMatrix(mwSize m, mwSize n, mxClassID classid, mxComplexity complexity)
{
    mwSize dims[] = { m, n };
    return NumericArray(2, dims, classid, complexity);
}

mxArray* mxCreateNumericArray(mwSize ndim, const mwSize* dims, mxClassID classid, mxComplexity complexity)
{
    return NumericArray(ndim, dims, classid, complexity);
}

mxArray* mxCreateUninitNumericMatrix(size_t m, size_t n, mxClassID classid, mxComplexity complexity)
{
    mwSize dims[] = { m, n };
    return NumericArray(2, dims, classid, complexity);
}

mxArray* mxCreateUninitNumericArray(size_t ndim, size_t* dims, mxClassID classid, mxComplexity complexity)
{
    return NumericArray(ndim, dims, classid, complexity);
}

mxArray* mxCreateLogicalScalar(bool value)
{
    mxArray* a = mxCreateLogicalMatrix(1, 1);
    if (a != nullptr)
    {
        static_cast<uint8_t*>(a->data)[0] = value ? 1 : 0;
    }

    return a;
}

mxArray* mxCreateLogicalMatrix(mwSize m, mwSize n)
{
    mwSize dims[] = { m, n };
    return NumericArray(2, dims, mxLOGICAL_CLASS, mxREAL);
}

mxArray* mxCreateLogicalArray(mwSize ndim, const mwSize* dims) { return NumericArray(ndim, dims, mxLOGICAL_CLASS, mxREAL); }

mxArray* mxCreateString(const char* str)
{
    return str == nullptr ? nullptr : Characters(JgrtFromBytes(str, strlen(str)));
}

mxArray* mxCreateStringFromNChars(const char* str, mwSize n)
{
    if (str == nullptr)
    {
        return nullptr;
    }

    size_t length = 0;
    while (length < n && str[length] != '\0')
    {
        length++;
    }

    return Characters(JgrtFromBytes(str, length));
}

// Rows padded with NUL, not blanks, as R2025b pads them (k10_mx create.charmatrix).
mxArray* mxCreateCharMatrixFromStrings(mwSize m, const char** str)
{
    std::vector<std::wstring> rows;
    size_t cols = 0;
    for (mwSize r = 0; str != nullptr && r < m; r++)
    {
        rows.push_back(str[r] == nullptr ? std::wstring() : JgrtFromBytes(str[r], strlen(str[r])));
        cols = rows.back().size() > cols ? rows.back().size() : cols;
    }

    mwSize dims[] = { rows.size(), cols };
    mxArray* a = NewArray(mxCHAR_CLASS, 2, dims);
    if (a == nullptr)
    {
        return nullptr;
    }

    Allocate(a);
    mxChar* chars = static_cast<mxChar*>(a->data);
    for (size_t r = 0; r < rows.size(); r++)
    {
        for (size_t c = 0; c < rows[r].size(); c++)
        {
            chars[r + (c * rows.size())] = static_cast<mxChar>(rows[r][c]);
        }
    }

    return a;
}

mxArray* mxCreateCharArray(mwSize ndim, const mwSize* dims) { return NumericArray(ndim, dims, mxCHAR_CLASS, mxREAL); }

mxArray* mxCreateCellMatrix(mwSize m, mwSize n)
{
    mwSize dims[] = { m, n };
    return NumericArray(2, dims, mxCELL_CLASS, mxREAL);
}

mxArray* mxCreateCellArray(mwSize ndim, const mwSize* dims) { return NumericArray(ndim, dims, mxCELL_CLASS, mxREAL); }

mxArray* mxCreateStructMatrix(mwSize m, mwSize n, int nfields, const char** fieldnames)
{
    mwSize dims[] = { m, n };
    return StructArray(2, dims, nfields, fieldnames);
}

mxArray* mxCreateStructArray(mwSize ndim, const mwSize* dims, int nfields, const char** fieldnames)
{
    return StructArray(ndim, dims, nfields, fieldnames);
}

mxArray* mxCreateSparse(mwSize m, mwSize n, mwSize nzmax, mxComplexity complexity)
{
    return SparseArray(m, n, nzmax, mxDOUBLE_CLASS, complexity == mxCOMPLEX);
}

mxArray* mxCreateSparseLogicalMatrix(mwSize m, mwSize n, mwSize nzmax) { return SparseArray(m, n, nzmax, mxLOGICAL_CLASS, false); }

mxArray* mxDuplicateArray(const mxArray* in)
{
    if (in == nullptr)
    {
        return nullptr;
    }

    mxArray* a = static_cast<mxArray*>(calloc(1, sizeof(mxArray)));
    if (a == nullptr)
    {
        return nullptr;
    }

    *a = *in;
    a->dims = nullptr;
    a->data = nullptr;
    a->imag = nullptr;
    a->ir = nullptr;
    a->jc = nullptr;
    a->fieldNames = nullptr;
    a->className = nullptr;
    a->handle = 0;
    SetDims(a, in->ndims, in->dims);

    size_t stored = Stored(in);
    if (in->classId == ReferenceClass)
    {
        a->handle = JgrtCopyValue(in->handle);
        a->className = CopyText(in->className != nullptr ? in->className : "");
        return a;
    }

    if (in->classId == mxCELL_CLASS)
    {
        a->data = stored > 0 ? calloc(stored, sizeof(mxArray*)) : nullptr;
        for (size_t i = 0; in->data != nullptr && i < stored; i++)
        {
            Slots(a)[i] = mxDuplicateArray(Slots(in)[i]);
        }

        return a;
    }

    if (in->classId == mxSTRUCT_CLASS)
    {
        a->fieldNames = in->nfields > 0 ? static_cast<char**>(calloc(in->nfields, sizeof(char*))) : nullptr;
        for (int f = 0; f < in->nfields; f++)
        {
            a->fieldNames[f] = CopyText(in->fieldNames[f]);
        }

        size_t slots = stored * static_cast<size_t>(in->nfields);
        a->data = slots > 0 ? calloc(slots, sizeof(mxArray*)) : nullptr;
        for (size_t i = 0; in->data != nullptr && i < slots; i++)
        {
            Slots(a)[i] = mxDuplicateArray(Slots(in)[i]);
        }

        return a;
    }

    size_t width = BaseSize(in->classId) * (in->complex && !in->separate ? 2 : 1);
    if (in->data != nullptr && stored > 0)
    {
        a->data = malloc(stored * width);
        memcpy(a->data, in->data, stored * width);
    }

    if (in->imag != nullptr && stored > 0)
    {
        a->imag = malloc(stored * BaseSize(in->classId));
        memcpy(a->imag, in->imag, stored * BaseSize(in->classId));
    }

    if (in->sparse)
    {
        if (in->ir != nullptr)
        {
            a->ir = static_cast<mwIndex*>(malloc(in->nzmax * sizeof(mwIndex)));
            memcpy(a->ir, in->ir, in->nzmax * sizeof(mwIndex));
        }

        if (in->jc != nullptr)
        {
            size_t columns = in->ndims > 1 ? in->dims[1] + 1 : 1;
            a->jc = static_cast<mwIndex*>(malloc(columns * sizeof(mwIndex)));
            memcpy(a->jc, in->jc, columns * sizeof(mwIndex));
        }
    }

    return a;
}

void mxDestroyArray(mxArray* pa)
{
    if (pa == nullptr)
    {
        return;
    }

    if (pa->classId == mxCELL_CLASS || pa->classId == mxSTRUCT_CLASS)
    {
        size_t slots = Stored(pa) * (pa->classId == mxSTRUCT_CLASS ? static_cast<size_t>(pa->nfields) : 1);
        for (size_t i = 0; pa->data != nullptr && i < slots; i++)
        {
            mxDestroyArray(Slots(pa)[i]);
        }
    }

    for (int f = 0; f < pa->nfields; f++)
    {
        free(pa->fieldNames[f]);
    }

    if (pa->classId == ReferenceClass && pa->handle != 0)
    {
        JgrtReleaseValue(pa->handle);
    }

    free(pa->fieldNames);
    free(pa->className);
    free(pa->data);
    free(pa->imag);
    free(pa->ir);
    free(pa->jc);
    free(pa->dims);
    free(pa);
}

// ---- Class and shape -------------------------------------------------------------------------

mxClassID mxGetClassID(const mxArray* pa) { return pa == nullptr ? mxUNKNOWN_CLASS : static_cast<mxClassID>(pa->classId); }

const char* mxGetClassName(const mxArray* pa)
{
    if (pa == nullptr)
    {
        return "unknown";
    }

    return pa->classId == ReferenceClass && pa->className != nullptr ? pa->className : ClassName(pa->classId);
}

bool mxIsClass(const mxArray* pa, const char* name)
{
    return pa != nullptr && name != nullptr && strcmp(mxGetClassName(pa), name) == 0;
}

mwSize mxGetNumberOfDimensions(const mxArray* pa) { return pa == nullptr ? 0 : pa->ndims; }

const mwSize* mxGetDimensions(const mxArray* pa) { return pa == nullptr ? nullptr : pa->dims; }

size_t mxGetM(const mxArray* pa) { return pa == nullptr ? 0 : pa->dims[0]; }

size_t mxGetN(const mxArray* pa)
{
    if (pa == nullptr)
    {
        return 0;
    }

    size_t n = 1;
    for (mwSize k = 1; k < pa->ndims; k++)
    {
        n *= pa->dims[k];
    }

    return n;
}

size_t mxGetNumberOfElements(const mxArray* pa) { return pa == nullptr ? 0 : Count(pa); }

// The interleaved model's element: both parts of a complex one (k10_mx es=16 for complex double).
size_t mxGetElementSize(const mxArray* pa)
{
    if (pa == nullptr)
    {
        return 0;
    }

    return BaseSize(pa->classId) * (pa->complex ? 2 : 1);
}

size_t mxGetElementSize_730(const mxArray* pa) { return pa == nullptr ? 0 : BaseSize(pa->classId); }

mwIndex mxCalcSingleSubscript(const mxArray* pa, mwSize nsubs, const mwIndex* subs)
{
    if (pa == nullptr || subs == nullptr)
    {
        return 0;
    }

    mwIndex index = 0;
    mwIndex stride = 1;
    for (mwSize k = 0; k < nsubs; k++)
    {
        index += subs[k] * stride;
        stride *= k < pa->ndims ? pa->dims[k] : 1;
    }

    return index;
}

void mxSetM(mxArray* pa, mwSize m)
{
    if (pa != nullptr)
    {
        pa->dims[0] = m;
    }
}

// The size becomes m-by-n, every dimension past the first folded into n (k10_mx reshape.setn.nd).
void mxSetN(mxArray* pa, mwSize n)
{
    if (pa != nullptr)
    {
        mwSize dims[] = { pa->dims[0], n };
        SetDims(pa, 2, dims);
    }
}

int mxSetDimensions(mxArray* pa, const mwSize* pdims, mwSize ndims)
{
    if (pa == nullptr)
    {
        return 1;
    }

    SetDims(pa, ndims, pdims);
    return 0;
}

bool mxIsNumeric(const mxArray* pa) { return pa != nullptr && IsNumericClass(pa->classId); }
bool mxIsCell(const mxArray* pa) { return pa != nullptr && pa->classId == mxCELL_CLASS; }
bool mxIsLogical(const mxArray* pa) { return pa != nullptr && pa->classId == mxLOGICAL_CLASS; }
bool mxIsChar(const mxArray* pa) { return pa != nullptr && pa->classId == mxCHAR_CLASS; }
bool mxIsStruct(const mxArray* pa) { return pa != nullptr && pa->classId == mxSTRUCT_CLASS; }
bool mxIsSparse(const mxArray* pa) { return pa != nullptr && pa->sparse; }
bool mxIsComplex(const mxArray* pa) { return pa != nullptr && pa->complex; }
bool mxIsDouble(const mxArray* pa) { return pa != nullptr && pa->classId == mxDOUBLE_CLASS; }
bool mxIsSingle(const mxArray* pa) { return pa != nullptr && pa->classId == mxSINGLE_CLASS; }
bool mxIsInt8(const mxArray* pa) { return pa != nullptr && pa->classId == mxINT8_CLASS; }
bool mxIsUint8(const mxArray* pa) { return pa != nullptr && pa->classId == mxUINT8_CLASS; }
bool mxIsInt16(const mxArray* pa) { return pa != nullptr && pa->classId == mxINT16_CLASS; }
bool mxIsUint16(const mxArray* pa) { return pa != nullptr && pa->classId == mxUINT16_CLASS; }
bool mxIsInt32(const mxArray* pa) { return pa != nullptr && pa->classId == mxINT32_CLASS; }
bool mxIsUint32(const mxArray* pa) { return pa != nullptr && pa->classId == mxUINT32_CLASS; }
bool mxIsInt64(const mxArray* pa) { return pa != nullptr && pa->classId == mxINT64_CLASS; }
bool mxIsUint64(const mxArray* pa) { return pa != nullptr && pa->classId == mxUINT64_CLASS; }
bool mxIsEmpty(const mxArray* pa) { return pa != nullptr && Count(pa) == 0; }
bool mxIsScalar(const mxArray* pa) { return pa != nullptr && Count(pa) == 1; }
bool mxIsOpaque(const mxArray* pa) { return pa != nullptr && (pa->classId == ReferenceClass || pa->classId == mxOPAQUE_CLASS); }
bool mxIsFunctionHandle(const mxArray* pa) { return pa != nullptr && pa->classId == mxFUNCTION_CLASS; }
bool mxIsObject(const mxArray* pa) { return pa != nullptr && pa->classId == mxOBJECT_CLASS; }
bool mxIsFromGlobalWS(const mxArray* pa) { return pa != nullptr && pa->fromGlobal; }

bool mxIsLogicalScalar(const mxArray* pa) { return mxIsLogical(pa) && Count(pa) == 1; }

bool mxIsLogicalScalarTrue(const mxArray* pa)
{
    return mxIsLogicalScalar(pa) && pa->data != nullptr && static_cast<const uint8_t*>(pa->data)[0] != 0;
}

void mxSetFromGlobalWS(mxArray* pa, bool global)
{
    if (pa != nullptr)
    {
        pa->fromGlobal = global;
    }
}

int mxGetUserBits(const mxArray* pa) { return pa == nullptr ? 0 : pa->userBits; }

void mxSetUserBits(mxArray* pa, int value)
{
    if (pa != nullptr)
    {
        pa->userBits = value;
    }
}

// ---- Data ----------------------------------------------------------------------------------

// The first stored element's real part: a sparse array's first nonzero, not its (1,1) element,
// and 0 for an empty array, a cell, a struct or a reference (k10_mx scalar.*).
double mxGetScalar(const mxArray* pa)
{
    if (pa == nullptr || pa->data == nullptr || !HoldsElements(pa->classId) || Stored(pa) == 0)
    {
        return 0;
    }

    return ReadReal(pa->classId, pa->data, 0);
}

mxLogical* mxGetLogicals(const mxArray* pa)
{
    return pa != nullptr && HoldsElements(pa->classId) ? static_cast<mxLogical*>(pa->data) : nullptr;
}

mxChar* mxGetChars(const mxArray* pa)
{
    return pa != nullptr && HoldsElements(pa->classId) ? static_cast<mxChar*>(pa->data) : nullptr;
}

void* mxGetData(const mxArray* pa)
{
    if (pa == nullptr || pa->classId == ReferenceClass)
    {
        return nullptr;
    }

    ToInterleaved(const_cast<mxArray*>(pa));
    return pa->data;
}

void mxSetData(mxArray* pa, void* newdata)
{
    if (pa == nullptr || pa->classId == ReferenceClass)
    {
        return;
    }

    pa->data = newdata;
    if (pa->complex && pa->separate)
    {
        // The new data is both parts, side by side; the old imaginary part stays the caller's.
        pa->imag = nullptr;
        pa->separate = false;
    }
}

// The real elements in the interleaved model; NULL for a complex array, where R2025b's throws
// and ends the process (k10_mx_interleaved risky 12, ADR 0266).
double* mxGetPr(const mxArray* pa)
{
    if (pa == nullptr || !HoldsElements(pa->classId) || pa->complex)
    {
        return nullptr;
    }

    return static_cast<double*>(pa->data);
}

void mxSetPr(mxArray* pa, double* newdata)
{
    if (pa != nullptr && HoldsElements(pa->classId) && !pa->complex)
    {
        pa->data = newdata;
    }
}

void* mxGetData_730(const mxArray* pa)
{
    if (pa == nullptr || pa->classId == ReferenceClass)
    {
        return nullptr;
    }

    ToSeparate(const_cast<mxArray*>(pa));
    return pa->data;
}

void mxSetData_730(mxArray* pa, void* newdata)
{
    if (pa == nullptr || pa->classId == ReferenceClass)
    {
        return;
    }

    ToSeparate(pa);
    pa->data = newdata;
}

double* mxGetPr_730(const mxArray* pa)
{
    return pa != nullptr && HoldsElements(pa->classId) ? static_cast<double*>(mxGetData_730(pa)) : nullptr;
}

void mxSetPr_730(mxArray* pa, double* newdata)
{
    if (pa != nullptr && HoldsElements(pa->classId))
    {
        mxSetData_730(pa, newdata);
    }
}

double* mxGetPi_730(const mxArray* pa) { return static_cast<double*>(mxGetImagData_730(pa)); }

void* mxGetImagData_730(const mxArray* pa)
{
    if (pa == nullptr || !pa->complex)
    {
        return nullptr;
    }

    ToSeparate(const_cast<mxArray*>(pa));
    return pa->imag;
}

void mxSetPi_730(mxArray* pa, double* pi) { mxSetImagData_730(pa, pi); }

// A real numeric array becomes complex with the given imaginary part, and a complex one real
// again with NULL (k10_mx data.setpi, data.setpi.null); the part it had stays the caller's.
void mxSetImagData_730(mxArray* pa, void* newdata)
{
    if (pa == nullptr || !IsNumericClass(pa->classId))
    {
        return;
    }

    if (pa->complex && !pa->separate)
    {
        ToSeparate(pa);
        free(pa->imag);
    }

    pa->imag = newdata;
    pa->complex = newdata != nullptr;
    pa->separate = newdata != nullptr;
}

#define JGRT_TYPED_ACCESSORS(Name, ClassId)                                       \
    mx##Name* mxGet##Name##s(const mxArray* pa)                                   \
    {                                                                             \
        return pa != nullptr && pa->classId == ClassId && !pa->complex            \
            ? static_cast<mx##Name*>(pa->data) : nullptr;                         \
    }                                                                             \
    int mxSet##Name##s(mxArray* pa, mx##Name* data)                               \
    {                                                                             \
        if (pa == nullptr || pa->classId != ClassId || pa->complex)               \
        {                                                                         \
            return 0;                                                             \
        }                                                                         \
        pa->data = data;                                                          \
        return 1;                                                                 \
    }                                                                             \
    mxComplex##Name* mxGetComplex##Name##s(const mxArray* pa)                     \
    {                                                                             \
        if (pa == nullptr || pa->classId != ClassId || !pa->complex)              \
        {                                                                         \
            return nullptr;                                                       \
        }                                                                         \
        ToInterleaved(const_cast<mxArray*>(pa));                                  \
        return static_cast<mxComplex##Name*>(pa->data);                           \
    }                                                                             \
    int mxSetComplex##Name##s(mxArray* pa, mxComplex##Name* data)                 \
    {                                                                             \
        if (pa == nullptr || pa->classId != ClassId || !pa->complex)              \
        {                                                                         \
            return 0;                                                             \
        }                                                                         \
        pa->data = data;                                                          \
        pa->imag = nullptr;                                                       \
        pa->separate = false;                                                     \
        return 1;                                                                 \
    }

JGRT_TYPED_ACCESSORS(Double, mxDOUBLE_CLASS)
JGRT_TYPED_ACCESSORS(Single, mxSINGLE_CLASS)
JGRT_TYPED_ACCESSORS(Int8, mxINT8_CLASS)
JGRT_TYPED_ACCESSORS(Uint8, mxUINT8_CLASS)
JGRT_TYPED_ACCESSORS(Int16, mxINT16_CLASS)
JGRT_TYPED_ACCESSORS(Uint16, mxUINT16_CLASS)
JGRT_TYPED_ACCESSORS(Int32, mxINT32_CLASS)
JGRT_TYPED_ACCESSORS(Uint32, mxUINT32_CLASS)
JGRT_TYPED_ACCESSORS(Int64, mxINT64_CLASS)
JGRT_TYPED_ACCESSORS(Uint64, mxUINT64_CLASS)
#undef JGRT_TYPED_ACCESSORS

// A numeric array keeps its real parts and drops the imaginary ones; 1 for one already real,
// 0 for anything not numeric (k10_mx_interleaved data.makereal*).
int mxMakeArrayReal(mxArray* pa)
{
    if (pa == nullptr || !IsNumericClass(pa->classId))
    {
        return 0;
    }

    if (!pa->complex)
    {
        return 1;
    }

    ToSeparate(pa);
    free(pa->imag);
    pa->imag = nullptr;
    pa->complex = false;
    pa->separate = false;
    return 1;
}

// A numeric array gains imaginary parts of zero; 1 for one already complex, 0 for anything not
// numeric, char included (k10_mx_interleaved data.makecomplex*).
int mxMakeArrayComplex(mxArray* pa)
{
    if (pa == nullptr || !IsNumericClass(pa->classId))
    {
        return 0;
    }

    if (pa->complex)
    {
        return 1;
    }

    size_t n = Stored(pa), b = BaseSize(pa->classId);
    char* both = n > 0 ? static_cast<char*>(calloc(n, 2 * b)) : nullptr;
    const char* re = static_cast<const char*>(pa->data);
    for (size_t i = 0; both != nullptr && re != nullptr && i < n; i++)
    {
        memcpy(both + (2 * i * b), re + (i * b), b);
    }

    free(pa->data);
    pa->data = both;
    pa->complex = true;
    pa->separate = false;
    return 1;
}

// ---- Sparse arrays ---------------------------------------------------------------------------

mwIndex* mxGetIr(const mxArray* pa) { return pa != nullptr && pa->sparse ? pa->ir : nullptr; }

void mxSetIr(mxArray* pa, mwIndex* newir)
{
    if (pa != nullptr && pa->sparse)
    {
        pa->ir = newir;
    }
}

mwIndex* mxGetJc(const mxArray* pa) { return pa != nullptr && pa->sparse ? pa->jc : nullptr; }

void mxSetJc(mxArray* pa, mwIndex* newjc)
{
    if (pa != nullptr && pa->sparse)
    {
        pa->jc = newjc;
    }
}

mwSize mxGetNzmax(const mxArray* pa) { return pa != nullptr && pa->sparse ? pa->nzmax : 0; }

void mxSetNzmax(mxArray* pa, mwSize nzmax)
{
    if (pa != nullptr && pa->sparse)
    {
        pa->nzmax = nzmax > 0 ? nzmax : 1;
    }
}

// ---- Text ----------------------------------------------------------------------------------

// The text in the ANSI code page into buf, NUL-terminated: 0 when it all fit, 1 when it was cut
// short or pa is not char (and then buf is left alone) (k10_mx string.get*).
int mxGetString(const mxArray* pa, char* buf, mwSize buflen)
{
    std::wstring text;
    if (!TextOf(pa, text) || buf == nullptr || buflen == 0)
    {
        return 1;
    }

    std::string bytes = JgrtToAnsi(text.data(), text.size());
    size_t fits = bytes.size() < buflen ? bytes.size() : buflen - 1;
    memcpy(buf, bytes.data(), fits);
    buf[fits] = '\0';
    return fits == bytes.size() ? 0 : 1;
}

// At most nChars bytes of the text and its NUL: a text shorter than nChars ends with one, a
// longer one is cut off without (k10_mx string.nchars3, string.nchars8).
void mxGetNChars(const mxArray* pa, char* buf, mwSize nChars)
{
    std::wstring text;
    if (!TextOf(pa, text) || buf == nullptr)
    {
        return;
    }

    std::string bytes = JgrtToAnsi(text.data(), text.size());
    size_t n = bytes.size() + 1 < nChars ? bytes.size() + 1 : nChars;
    memcpy(buf, bytes.c_str(), n);
}

char* mxArrayToString(const mxArray* pa)
{
    std::wstring text;
    return TextOf(pa, text) ? Owned(JgrtToAnsi(text.data(), text.size())) : nullptr;
}

char* mxArrayToUTF8String(const mxArray* pa)
{
    std::wstring text;
    return TextOf(pa, text) ? Owned(JgrtToUtf8(text.data(), text.size())) : nullptr;
}

// ---- Cells ---------------------------------------------------------------------------------

mxArray* mxGetCell(const mxArray* pa, mwIndex i)
{
    return pa != nullptr && pa->classId == mxCELL_CLASS && pa->data != nullptr && i < Count(pa) ? Slots(pa)[i] : nullptr;
}

// The element is replaced, not destroyed: the old one stays the caller's.
void mxSetCell(mxArray* pa, mwIndex i, mxArray* value)
{
    if (pa != nullptr && pa->classId == mxCELL_CLASS && pa->data != nullptr && i < Count(pa))
    {
        Slots(pa)[i] = value;
    }
}

// ---- Structs -------------------------------------------------------------------------------

int mxGetNumberOfFields(const mxArray* pa) { return pa != nullptr && pa->classId == mxSTRUCT_CLASS ? pa->nfields : 0; }

const char* mxGetFieldNameByNumber(const mxArray* pa, int n)
{
    return pa != nullptr && pa->classId == mxSTRUCT_CLASS && n >= 0 && n < pa->nfields ? pa->fieldNames[n] : nullptr;
}

int mxGetFieldNumber(const mxArray* pa, const char* name) { return FieldIndex(pa, name); }

mxArray* mxGetFieldByNumber(const mxArray* pa, mwIndex i, int fieldnum)
{
    if (pa == nullptr || pa->classId != mxSTRUCT_CLASS || pa->data == nullptr || i >= Count(pa) || fieldnum < 0 || fieldnum >= pa->nfields)
    {
        return nullptr;
    }

    return Slots(pa)[(i * pa->nfields) + fieldnum];
}

void mxSetFieldByNumber(mxArray* pa, mwIndex i, int fieldnum, mxArray* value)
{
    if (pa == nullptr || pa->classId != mxSTRUCT_CLASS || pa->data == nullptr || i >= Count(pa) || fieldnum < 0 || fieldnum >= pa->nfields)
    {
        return;
    }

    Slots(pa)[(i * pa->nfields) + fieldnum] = value;
}

mxArray* mxGetField(const mxArray* pa, mwIndex i, const char* fieldname)
{
    return mxGetFieldByNumber(pa, i, FieldIndex(pa, fieldname));
}

void mxSetField(mxArray* pa, mwIndex i, const char* fieldname, mxArray* value)
{
    mxSetFieldByNumber(pa, i, FieldIndex(pa, fieldname), value);
}

// The field's number: the one it already has, or a new last one. R2025b takes any name, even one
// MATLAB could not write (k10_mx risky 7); so does this.
int mxAddField(mxArray* pa, const char* fieldname)
{
    if (pa == nullptr || pa->classId != mxSTRUCT_CLASS || fieldname == nullptr)
    {
        return -1;
    }

    int existing = FieldIndex(pa, fieldname);
    if (existing >= 0)
    {
        return existing;
    }

    size_t n = Count(pa);
    int fields = pa->nfields + 1;
    mxArray** slots = n > 0 ? static_cast<mxArray**>(calloc(n * fields, sizeof(mxArray*))) : nullptr;
    for (size_t i = 0; slots != nullptr && i < n; i++)
    {
        for (int f = 0; f < pa->nfields; f++)
        {
            slots[(i * fields) + f] = Slots(pa)[(i * pa->nfields) + f];
        }
    }

    char** names = static_cast<char**>(realloc(pa->fieldNames, fields * sizeof(char*)));
    names[pa->nfields] = CopyText(fieldname);
    free(pa->data);
    pa->data = slots;
    pa->fieldNames = names;
    pa->nfields = fields;
    return fields - 1;
}

// The field goes; the values it held stay the caller's. A number out of range does nothing.
void mxRemoveField(mxArray* pa, int field)
{
    if (pa == nullptr || pa->classId != mxSTRUCT_CLASS || field < 0 || field >= pa->nfields)
    {
        return;
    }

    size_t n = Count(pa);
    int fields = pa->nfields - 1;
    mxArray** slots = n > 0 && fields > 0 ? static_cast<mxArray**>(calloc(n * fields, sizeof(mxArray*))) : nullptr;
    for (size_t i = 0; slots != nullptr && i < n; i++)
    {
        for (int f = 0, to = 0; f < pa->nfields; f++)
        {
            if (f != field)
            {
                slots[(i * fields) + to++] = Slots(pa)[(i * pa->nfields) + f];
            }
        }
    }

    free(pa->fieldNames[field]);
    for (int f = field; f < fields; f++)
    {
        pa->fieldNames[f] = pa->fieldNames[f + 1];
    }

    free(pa->data);
    pa->data = slots;
    pa->nfields = fields;
}

// ---- Objects a component returned ------------------------------------------------------------

mxArray* mxGetProperty(const mxArray* pa, const mwIndex i, const char* propname)
{
    const JgrtManaged* engine = pa != nullptr && pa->classId == ReferenceClass && propname != nullptr ? JgrtEngine() : nullptr;
    return engine == nullptr ? nullptr : engine->GetProperty(pa->handle, i, JgrtFromBytes(propname, strlen(propname)).c_str());
}

// A handle object's property is set on the object itself; a value object's on this array's copy.
void mxSetProperty(mxArray* pa, mwIndex i, const char* propname, const mxArray* value)
{
    const JgrtManaged* engine = pa != nullptr && pa->classId == ReferenceClass && propname != nullptr && value != nullptr ? JgrtEngine() : nullptr;
    if (engine == nullptr)
    {
        return;
    }

    intptr_t changed = engine->SetProperty(pa->handle, i, JgrtFromBytes(propname, strlen(propname)).c_str(), value);
    if (changed != 0)
    {
        jgrtSetReference(pa, changed);
    }
}

// ---- IEEE values ---------------------------------------------------------------------------

double mxGetEps(void) { return DBL_EPSILON; }

double mxGetInf(void) { return std::numeric_limits<double>::infinity(); }

double mxGetNaN(void) { return std::numeric_limits<double>::quiet_NaN(); }

bool mxIsFinite(double x) { return std::isfinite(x); }

bool mxIsInf(double x) { return std::isinf(x); }

bool mxIsNaN(double x) { return std::isnan(x); }

// ---- For the engine ------------------------------------------------------------------------

int jgrtStorage(const mxArray* pa, JgrtStorage* storage)
{
    if (pa == nullptr || storage == nullptr)
    {
        return 0;
    }

    storage->classId = pa->classId;
    storage->flags = (pa->complex ? 1 : 0) | (pa->separate ? 2 : 0) | (pa->sparse ? 4 : 0);
    storage->ndims = pa->ndims;
    storage->dims = pa->dims;
    storage->data = pa->data;
    storage->imag = pa->imag;
    storage->ir = pa->ir;
    storage->jc = pa->jc;
    storage->nzmax = pa->nzmax;
    storage->nfields = pa->nfields;
    storage->reserved = 0;
    storage->fieldNames = pa->fieldNames;
    storage->handle = pa->handle;
    storage->className = pa->className;
    return 1;
}

mxArray* jgrtCreateReference(intptr_t value, const char* className)
{
    mwSize dims[] = { 1, 1 };
    mxArray* a = NewArray(ReferenceClass, 2, dims);
    if (a == nullptr)
    {
        return nullptr;
    }

    a->handle = value;
    a->className = CopyText(className != nullptr ? className : "");
    return a;
}

void jgrtSetReference(mxArray* pa, intptr_t value)
{
    if (pa == nullptr || pa->classId != ReferenceClass || pa->handle == value)
    {
        return;
    }

    if (pa->handle != 0)
    {
        JgrtReleaseValue(pa->handle);
    }

    pa->handle = value;
}
