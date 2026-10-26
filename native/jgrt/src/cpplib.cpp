// cpplib.cpp - the C++ shared library API's way into the engine (compiler and SDK plan, stage K12):
// the jgrtCpp functions jgraph/cpplib.hpp and jgraph/execution.hpp call, which hand their work to
// the engine's NativeCppCalls (JGraph.Runtime) and keep the one thing the engine does not: whether
// the C++ application has started or ended in this process, so a second start while it runs and
// any start after it ended are refused as R2025b refuses them (record-k12.ps1, k12_client twice).
#include "jgrt_internal.h"
#include "../include/jgraph/jgrt_cpp.h"

#include <mutex>
#include <vector>

#include <objbase.h>

namespace
{
std::mutex PhaseGate;
int Phase = 0; // 0 not started, 1 running, 2 ended

char16_t* Copy(const wchar_t* text)
{
    size_t n = wcslen(text);
    auto* copy = static_cast<char16_t*>(CoTaskMemAlloc((n + 1) * sizeof(char16_t)));
    if (copy != nullptr)
    {
        memcpy(copy, text, (n + 1) * sizeof(char16_t));
    }

    return copy;
}

const wchar_t* Wide(const char16_t* text) { return reinterpret_cast<const wchar_t*>(text); }
}

int jgrtCppStart(int outOfProcess, const char16_t* const* options, size_t count, char16_t** error)
{
    if (error != nullptr)
    {
        *error = nullptr;
    }

    std::lock_guard<std::mutex> hold(PhaseGate);
    if (Phase != 0)
    {
        return Phase;
    }

    const JgrtManaged* engine = JgrtEngine();
    if (engine == nullptr)
    {
        if (error != nullptr)
        {
            *error = Copy(L"The JGraph Runtime's engine could not start; standard error says why.");
        }

        return 3;
    }

    std::vector<const wchar_t*> texts;
    for (size_t i = 0; options != nullptr && i < count; i++)
    {
        texts.push_back(options[i] == nullptr ? L"" : Wide(options[i]));
    }

    wchar_t* reason = nullptr;
    int started = engine->CppStart(outOfProcess, texts.empty() ? nullptr : texts.data(), static_cast<int>(texts.size()), &reason);
    if (started != 0)
    {
        if (error != nullptr)
        {
            *error = reinterpret_cast<char16_t*>(reason);
        }
        else
        {
            CoTaskMemFree(reason);
        }

        return 3;
    }

    Phase = 1;
    return 0;
}

void jgrtCppStop(void)
{
    std::lock_guard<std::mutex> hold(PhaseGate);
    if (Phase == 1)
    {
        if (const JgrtManaged* engine = JgrtEngine())
        {
            engine->TerminateApplication();
        }

        Phase = 2;
    }
}

namespace
{
uint64_t OpenLibrary(intptr_t (*open)(const wchar_t*, wchar_t**), const char16_t* path, char16_t** error)
{
    wchar_t* reason = nullptr;
    intptr_t library = open(Wide(path), &reason);
    if (error != nullptr)
    {
        *error = reinterpret_cast<char16_t*>(reason);
    }
    else
    {
        CoTaskMemFree(reason);
    }

    return static_cast<uint64_t>(library);
}
}

uint64_t jgrtCppOpen(const char16_t* path, char16_t** error)
{
    if (error != nullptr)
    {
        *error = nullptr;
    }

    const JgrtManaged* engine = JgrtEngine();
    if (engine == nullptr || path == nullptr)
    {
        return 0;
    }

    return OpenLibrary(engine->CppOpen, path, error);
}

uint64_t jgrtPyOpen(const char16_t* path, char16_t** error)
{
    if (error != nullptr)
    {
        *error = nullptr;
    }

    const JgrtManaged* engine = JgrtEngine();
    if (engine == nullptr || path == nullptr)
    {
        return 0;
    }

    return OpenLibrary(engine->PyOpen, path, error);
}

void jgrtCppClose(uint64_t library)
{
    if (const JgrtManaged* engine = JgrtEngine(); engine != nullptr && library != 0)
    {
        engine->CloseInstance(static_cast<intptr_t>(library));
    }
}

void jgrtCppWaitForFigures(uint64_t library)
{
    if (const JgrtManaged* engine = JgrtEngine(); engine != nullptr && library != 0)
    {
        engine->WaitForFigures(static_cast<intptr_t>(library));
    }
}

uint64_t jgrtCppCall(uint64_t library, const char16_t* function, size_t nlhs, const unsigned char* inputs, size_t length,
    void* context, jgrtCppWriteFcn write, int output, int error, jgrtCppDoneFcn done, char16_t** refused)
{
    if (refused != nullptr)
    {
        *refused = nullptr;
    }

    const JgrtManaged* engine = JgrtEngine();
    if (engine == nullptr)
    {
        if (refused != nullptr)
        {
            *refused = Copy(L"The JGraph Runtime is not running.");
        }

        return 0;
    }

    wchar_t* reason = nullptr;
    uint64_t task = engine->CppCall(static_cast<intptr_t>(library), Wide(function), nlhs, inputs, length, context,
        reinterpret_cast<void*>(write), output, error, reinterpret_cast<void*>(done), &reason);
    if (refused != nullptr)
    {
        *refused = reinterpret_cast<char16_t*>(reason);
    }
    else
    {
        CoTaskMemFree(reason);
    }

    return task;
}

int jgrtCppCancel(uint64_t task, int allowInterrupt)
{
    const JgrtManaged* engine = JgrtEngine();
    return engine == nullptr ? 0 : engine->CppCancel(task, allowInterrupt);
}

void jgrtCppForget(uint64_t task)
{
    if (const JgrtManaged* engine = JgrtEngine())
    {
        engine->CppForget(task);
    }
}

void jgrtCppRelease(uint64_t value) { JgrtReleaseValue(static_cast<intptr_t>(value)); }

void jgrtCppFree(void* memory) { CoTaskMemFree(memory); }
