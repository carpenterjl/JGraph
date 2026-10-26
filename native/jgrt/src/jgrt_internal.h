// jgrt_internal.h - what jgrt's own files share (compiler and SDK plan, stage K10).
//
// jgrt.dll is the JGraph Runtime's native bridge: the C API a C program, a C shared library built
// by mcc -W lib, a Python package and the Excel add-in call. mxArrays live here, in native memory
// the caller owns; everything that runs MATLAB code is the engine's, reached through .NET hosted
// in this process (runtime.cpp), which reads and makes mxArrays through the functions below.
#pragma once

#define JGRT_BUILD
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include <cstddef>
#include <cstdint>
#include <string>

#include "../include/mclmcrrt.h"

// The class an mxArray holding a value the engine keeps has, as R2025b's runtime gives one: an
// object, a string, a function handle, a table and the like come back to C as a reference with
// this class, a 1-by-1 size and their MATLAB class name (record-k10.ps1, k10_feval).
const int JgrtReferenceClass = 48;

struct mxArray_tag
{
    int classId;          // an mxClassID, or JgrtReferenceClass
    bool complex;
    bool separate;        // a complex array's parts in data and imag rather than interleaved in data
    bool sparse;
    bool fromGlobal;
    int userBits;
    mwSize ndims;
    mwSize* dims;
    void* data;           // the elements; a cell's or struct's mxArray pointers (element-major for a struct)
    void* imag;           // the imaginary part of a separate complex array
    mwIndex* ir;
    mwIndex* jc;
    mwSize nzmax;
    int nfields;
    char** fieldNames;
    intptr_t handle;      // a reference's value, held by the engine
    char* className;      // a reference's MATLAB class
};

// What the engine's side reads of an array, without the storage being rearranged.
struct JgrtStorage
{
    int32_t classId;
    int32_t flags;        // 1 complex, 2 separate, 4 sparse
    uint64_t ndims;
    const mwSize* dims;
    void* data;
    void* imag;
    mwIndex* ir;
    mwIndex* jc;
    uint64_t nzmax;
    int32_t nfields;
    int32_t reserved;
    char** fieldNames;
    intptr_t handle;
    const char* className;
};

// The engine's entry points (JGraph.Runtime's NativeBridge), found once .NET is running
// (runtime.cpp). Text crosses as UTF-16; a function answering text writes at most capacity
// characters into buffer and answers how many it has, so a caller can ask again with room.
struct JgrtManaged
{
    int (*InitializeApplication)(const wchar_t** options, int count);
    int (*TerminateApplication)();
    int (*LogFileName)(wchar_t* buffer, int capacity);
    intptr_t (*OpenInstance)(const wchar_t* archive, intptr_t errorHandler, intptr_t printHandler);
    int (*CloseInstance)(intptr_t instance);
    int (*Feval)(intptr_t instance, const wchar_t* name, int nlhs, mxArray** plhs, int nrhs, mxArray** prhs);
    int (*LastError)(int what, wchar_t* buffer, int capacity);
    void (*ReleaseValue)(intptr_t value);
    intptr_t (*CopyValue)(intptr_t value);
    mxArray* (*GetProperty)(intptr_t value, uint64_t index, const wchar_t* name);
    intptr_t (*SetProperty)(intptr_t value, uint64_t index, const wchar_t* name, const mxArray* property);
    void (*WaitForFigures)(intptr_t instance);

    // The C++ shared library API's (stage K12, cpplib.cpp): a library is an instance as above.
    int (*CppStart)(int outOfProcess, const wchar_t** options, int count, wchar_t** error);
    intptr_t (*CppOpen)(const wchar_t* archive, wchar_t** error);
    uint64_t (*CppCall)(intptr_t library, const wchar_t* function, size_t nlhs, const unsigned char* inputs, size_t length,
        void* context, void* write, int output, int error, void* done, wchar_t** refused);
    int (*CppCancel)(uint64_t task, int allowInterrupt);
    void (*CppForget)(uint64_t task);

    // A Python package's instance (stage K13, cpplib.cpp's jgrtPyOpen): a library whose calls take and
    // give datetimes, durations and dictionaries as values.
    intptr_t (*PyOpen)(const wchar_t* archive, wchar_t** error);
};

// What LastError answers.
const int JgrtLastMessage = 0;
const int JgrtLastStack = 1;

// The engine's entry points, or null when .NET could not be started (the reason went to standard error).
const JgrtManaged* JgrtEngine();

// The managed value an array references is released with it, and copied with it.
void JgrtReleaseValue(intptr_t value);
intptr_t JgrtCopyValue(intptr_t value);

// Text in the process's ANSI code page, and from it.
std::string JgrtToAnsi(const wchar_t* text, size_t count);
std::string JgrtToUtf8(const wchar_t* text, size_t count);
std::wstring JgrtFromBytes(const char* bytes, size_t count);
std::wstring JgrtFromUtf8(const char* bytes, size_t count);

// For the engine: what an array holds, references made and updated, and text handed to a C
// program's output and error handlers.
extern "C" __declspec(dllexport) int jgrtStorage(const mxArray* pa, JgrtStorage* storage);
extern "C" __declspec(dllexport) mxArray* jgrtCreateReference(intptr_t value, const char* className);
extern "C" __declspec(dllexport) void jgrtSetReference(mxArray* pa, intptr_t value);
extern "C" __declspec(dllexport) int jgrtCallHandler(intptr_t handler, int fd, const wchar_t* text, size_t count);
