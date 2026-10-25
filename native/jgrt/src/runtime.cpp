// runtime.cpp - the mcl API, and the .NET it runs on (compiler and SDK plan, stage K10).
//
// The engine is .NET: the first call that needs it starts the .NET 8 runtime in this process
// through hostfxr, on the JGraph Runtime's jgrt.runtimeconfig.json (the Windows desktop framework,
// so a component can show its figures), loads JGraph.Runtime.dll into the default load context
// and asks its NativeBridge.Start for the entry points the rest of this file calls. The engine is
// found from where this DLL is: beside it when an application carries the engine with it, else
// <root>\bin\win64 for jgrt.dll in the runtime's <root>\runtime\win64. When .NET is already
// running in the process (a .NET program calling a C library), that runtime is joined.
//
// What the functions answer is what record-k10.ps1 measured R2025b's runtime answering
// (k10_app, k10_feval), except where R2025b crashes (a NULL input, mxGetProperty of a missing
// property) or answers a log file name with a stray '>' in front (ADR 0266).
#include "jgrt_internal.h"

#include <atomic>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <vector>

#include "nethost.h"
#include "hostfxr.h"
#include "coreclr_delegates.h"

namespace
{
std::mutex Gate;
bool StartTried = false;
std::atomic<const JgrtManaged*> Engine{ nullptr };
JgrtManaged Table = {};

// Whether the runtime has run in this process: R2025b's mclIsMCRInitialized stays true after
// mclTerminateApplication, and mclInitializeApplication answers false once it has run (k10_app).
std::atomic<bool> Initialized{ false };

std::string LogFile;
std::string LastMessage;

struct Stream
{
    std::wstring Path;
};

HMODULE Self()
{
    HMODULE module = nullptr;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&Self), &module);
    return module;
}

std::wstring ModulePath(HMODULE module)
{
    std::vector<wchar_t> buffer(MAX_PATH);
    for (;;)
    {
        DWORD n = GetModuleFileNameW(module, buffer.data(), static_cast<DWORD>(buffer.size()));
        if (n < buffer.size())
        {
            return std::wstring(buffer.data(), n);
        }

        buffer.resize(buffer.size() * 2);
    }
}

std::wstring FolderOf(const std::wstring& path)
{
    size_t at = path.find_last_of(L"\\/");
    return at == std::wstring::npos ? std::wstring(L".") : path.substr(0, at);
}

std::wstring FullPath(const std::wstring& path)
{
    DWORD n = GetFullPathNameW(path.c_str(), 0, nullptr, nullptr);
    if (n == 0)
    {
        return path;
    }

    std::wstring full(n, L'\0');
    n = GetFullPathNameW(path.c_str(), n, full.data(), nullptr);
    full.resize(n);
    return full;
}

bool FileExists(const std::wstring& path)
{
    DWORD attributes = GetFileAttributesW(path.c_str());
    return attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY) == 0;
}

// The engine's folder: beside this DLL, or the runtime layout's bin\win64; empty when neither has it.
std::wstring BinFolder()
{
    std::wstring here = FolderOf(ModulePath(Self()));
    if (FileExists(here + L"\\JGraph.Runtime.dll"))
    {
        return here;
    }

    std::wstring bin = FullPath(here + L"\\..\\..\\bin\\win64");
    return FileExists(bin + L"\\JGraph.Runtime.dll") ? bin : std::wstring();
}

void WriteStandard(DWORD which, const char* bytes, size_t count)
{
    HANDLE stream = GetStdHandle(which);
    DWORD written = 0;
    if (stream != nullptr && stream != INVALID_HANDLE_VALUE && count > 0)
    {
        WriteFile(stream, bytes, static_cast<DWORD>(count), &written, nullptr);
    }
}

// Says on standard error why the runtime could not start: there is no one else to tell.
void Complain(const std::wstring& text)
{
    std::string bytes = JgrtToAnsi(text.data(), text.size()) + "\n";
    WriteStandard(STD_ERROR_HANDLE, bytes.data(), bytes.size());
}

std::wstring HostErrors;

void HOSTFXR_CALLTYPE CollectHostError(const char_t* message)
{
    HostErrors += message;
    HostErrors += L"\n";
}

const JgrtManaged* StartEngine()
{
    std::wstring bin = BinFolder();
    if (bin.empty())
    {
        Complain(L"The JGraph Runtime's engine was not found: jgrt.dll looks for JGraph.Runtime.dll beside itself, "
            L"then in the runtime's bin\\win64 (" + FullPath(FolderOf(ModulePath(Self())) + L"\\..\\..\\bin\\win64") + L").");
        return nullptr;
    }

    std::wstring engine = bin + L"\\JGraph.Runtime.dll";
    std::wstring config = bin + L"\\jgrt.runtimeconfig.json";
    if (!FileExists(config))
    {
        Complain(L"The JGraph Runtime at " + bin + L" is damaged: jgrt.runtimeconfig.json is missing.");
        return nullptr;
    }

    std::vector<wchar_t> hostfxrPath(MAX_PATH);
    size_t size = hostfxrPath.size();
    get_hostfxr_parameters where = { sizeof(where), engine.c_str(), nullptr };
    int found = get_hostfxr_path(hostfxrPath.data(), &size, &where);
    if (found != 0 && size > hostfxrPath.size())
    {
        hostfxrPath.resize(size);
        found = get_hostfxr_path(hostfxrPath.data(), &size, &where);
    }

    HMODULE hostfxr = found == 0 ? LoadLibraryW(hostfxrPath.data()) : nullptr;
    auto setErrorWriter = hostfxr == nullptr ? nullptr
        : reinterpret_cast<hostfxr_set_error_writer_fn>(GetProcAddress(hostfxr, "hostfxr_set_error_writer"));
    auto initialize = hostfxr == nullptr ? nullptr
        : reinterpret_cast<hostfxr_initialize_for_runtime_config_fn>(GetProcAddress(hostfxr, "hostfxr_initialize_for_runtime_config"));
    auto setProperty = hostfxr == nullptr ? nullptr
        : reinterpret_cast<hostfxr_set_runtime_property_value_fn>(GetProcAddress(hostfxr, "hostfxr_set_runtime_property_value"));
    auto getDelegate = hostfxr == nullptr ? nullptr
        : reinterpret_cast<hostfxr_get_runtime_delegate_fn>(GetProcAddress(hostfxr, "hostfxr_get_runtime_delegate"));
    auto close = hostfxr == nullptr ? nullptr : reinterpret_cast<hostfxr_close_fn>(GetProcAddress(hostfxr, "hostfxr_close"));
    if (setErrorWriter == nullptr || initialize == nullptr || setProperty == nullptr || getDelegate == nullptr || close == nullptr)
    {
        Complain(L"The JGraph Runtime needs the .NET 8 Desktop Runtime (x64), which this computer does not have.");
        return nullptr;
    }

    hostfxr_error_writer_fn previous = setErrorWriter(CollectHostError);
    hostfxr_handle context = nullptr;
    int started = initialize(config.c_str(), nullptr, &context);
    if (started < 0 || context == nullptr)
    {
        setErrorWriter(previous);
        Complain(L"The JGraph Runtime could not start .NET:\n" + HostErrors);
        return nullptr;
    }

    // A runtime this call started looks for its native libraries and answers matlabroot from
    // the engine's folder, as jgraph.exe's does; one already running keeps its own.
    if (started == 0)
    {
        setProperty(context, L"APP_CONTEXT_BASE_DIRECTORY", (bin + L"\\").c_str());
    }

    load_assembly_fn loadAssembly = nullptr;
    get_function_pointer_fn getFunction = nullptr;
    int got = getDelegate(context, hdt_load_assembly, reinterpret_cast<void**>(&loadAssembly));
    if (got == 0)
    {
        got = getDelegate(context, hdt_get_function_pointer, reinterpret_cast<void**>(&getFunction));
    }

    int (CORECLR_DELEGATE_CALLTYPE * start)(JgrtManaged*, const wchar_t*, intptr_t) = nullptr;
    if (got == 0)
    {
        got = loadAssembly(engine.c_str(), nullptr, nullptr);
    }

    if (got == 0)
    {
        got = getFunction(L"JGraph.Runtime.Native.NativeBridge, JGraph.Runtime", L"Start", UNMANAGEDCALLERSONLY_METHOD,
            nullptr, nullptr, reinterpret_cast<void**>(&start));
    }

    setErrorWriter(previous);
    close(context);
    if (got != 0 || start == nullptr)
    {
        wchar_t code[16];
        swprintf(code, 16, L"0x%08X", static_cast<unsigned>(got));
        Complain(L"The JGraph Runtime could not load its engine (" + engine + L", " + code + L"):\n" + HostErrors);
        return nullptr;
    }

    if (start(&Table, bin.c_str(), reinterpret_cast<intptr_t>(Self())) == 0)
    {
        Complain(L"The JGraph Runtime's engine did not start.");
        return nullptr;
    }

    return &Table;
}

// Text a C program gave as a file name: the ANSI code page, as GetModuleFileNameA writes it.
std::wstring FromAnsi(const char* bytes)
{
    if (bytes == nullptr || *bytes == '\0')
    {
        return std::wstring();
    }

    int n = MultiByteToWideChar(CP_ACP, 0, bytes, -1, nullptr, 0);
    std::wstring wide(static_cast<size_t>(n), L'\0');
    MultiByteToWideChar(CP_ACP, 0, bytes, -1, wide.data(), n);
    wide.resize(wcslen(wide.c_str()));
    return wide;
}

// A file carries an archive when a ZIP end-of-directory record sits in its last 64 KB.
bool CarriesArchive(const std::wstring& path)
{
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE)
    {
        return false;
    }

    LARGE_INTEGER size = {};
    bool found = false;
    if (GetFileSizeEx(file, &size) && size.QuadPart >= 22)
    {
        LONGLONG tail = size.QuadPart < 65558 ? size.QuadPart : 65558;
        LARGE_INTEGER at;
        at.QuadPart = size.QuadPart - tail;
        std::vector<char> bytes(static_cast<size_t>(tail));
        DWORD read = 0;
        if (SetFilePointerEx(file, at, nullptr, FILE_BEGIN) && ReadFile(file, bytes.data(), static_cast<DWORD>(tail), &read, nullptr))
        {
            for (long long i = static_cast<long long>(read) - 22; i >= 0 && !found; i--)
            {
                found = memcmp(bytes.data() + i, "PK\x05\x06", 4) == 0;
            }
        }
    }

    CloseHandle(file);
    return found;
}

// Text the engine answers: asked once for its length, then for the text.
std::wstring Ask(int (*answer)(int, wchar_t*, int), int what)
{
    int n = answer(what, nullptr, 0);
    if (n <= 0)
    {
        return std::wstring();
    }

    std::wstring text(static_cast<size_t>(n), L'\0');
    n = answer(what, text.data(), n);
    text.resize(static_cast<size_t>(n > 0 ? n : 0));
    return text;
}

bool OpenInstance(HMCRINSTANCE* inst, mclOutputHandlerFcn error_handler, mclOutputHandlerFcn print_handler, const std::wstring& archive)
{
    if (inst == nullptr || archive.empty())
    {
        return false;
    }

    const JgrtManaged* engine = JgrtEngine();
    if (engine == nullptr)
    {
        return false;
    }

    intptr_t made = engine->OpenInstance(archive.c_str(), reinterpret_cast<intptr_t>(error_handler), reinterpret_cast<intptr_t>(print_handler));
    if (made == 0)
    {
        return false;
    }

    // Opening a component starts the runtime when the program did not.
    Initialized = true;
    *inst = reinterpret_cast<HMCRINSTANCE>(made);
    return true;
}
}

const JgrtManaged* JgrtEngine()
{
    const JgrtManaged* ready = Engine.load();
    if (ready != nullptr)
    {
        return ready;
    }

    std::lock_guard<std::mutex> hold(Gate);
    if (!StartTried)
    {
        StartTried = true;
        Engine = StartEngine();
    }

    return Engine.load();
}

void JgrtReleaseValue(intptr_t value)
{
    if (const JgrtManaged* engine = Engine.load())
    {
        engine->ReleaseValue(value);
    }
}

intptr_t JgrtCopyValue(intptr_t value)
{
    const JgrtManaged* engine = Engine.load();
    return engine == nullptr ? 0 : engine->CopyValue(value);
}

// ---- The application -------------------------------------------------------------------------

// Nothing to prepare: the runtime is found and started by the first call that needs it. Answers
// whether the runtime's engine is where jgrt.dll looks for it.
bool mclmcrInitialize(void) { return !BinFolder().empty(); }

bool mclInitializeApplication(const char** options, size_t count)
{
    if (Initialized)
    {
        return false;
    }

    const JgrtManaged* engine = JgrtEngine();
    if (engine == nullptr)
    {
        return false;
    }

    std::vector<std::wstring> texts;
    for (size_t i = 0; options != nullptr && i < count; i++)
    {
        texts.push_back(options[i] == nullptr ? std::wstring() : JgrtFromBytes(options[i], strlen(options[i])));
    }

    std::vector<const wchar_t*> pointers;
    for (const std::wstring& text : texts)
    {
        pointers.push_back(text.c_str());
    }

    bool ok = engine->InitializeApplication(pointers.empty() ? nullptr : pointers.data(), static_cast<int>(pointers.size())) != 0;
    if (ok)
    {
        Initialized = true;
    }

    return ok;
}

bool mclTerminateApplication(void)
{
    if (const JgrtManaged* engine = Engine.load())
    {
        engine->TerminateApplication();
    }

    return true;
}

bool mclIsMCRInitialized(void) { return Initialized; }

const char* mclGetLogFileName(void)
{
    const JgrtManaged* engine = Engine.load();
    std::wstring name;
    if (engine != nullptr)
    {
        int n = engine->LogFileName(nullptr, 0);
        if (n > 0)
        {
            name.assign(static_cast<size_t>(n), L'\0');
            n = engine->LogFileName(name.data(), n);
            name.resize(static_cast<size_t>(n > 0 ? n : 0));
        }
    }

    std::lock_guard<std::mutex> hold(Gate);
    LogFile = JgrtToAnsi(name.data(), name.size());
    return LogFile.c_str();
}

int mclRunMain(mclMainFcnType run_main, int argc, const char** argv) { return run_main == nullptr ? 0 : run_main(argc, argv); }

int mclGetStackTrace(char*** stack)
{
    if (stack == nullptr)
    {
        return 0;
    }

    *stack = nullptr;
    const JgrtManaged* engine = Engine.load();
    if (engine == nullptr)
    {
        return 0;
    }

    std::wstring text = Ask(engine->LastError, JgrtLastStack);
    std::vector<std::string> lines;
    size_t start = 0;
    while (start < text.size())
    {
        size_t end = text.find(L'\n', start);
        if (end == std::wstring::npos)
        {
            end = text.size();
        }

        lines.push_back(JgrtToAnsi(text.data() + start, end - start));
        start = end + 1;
    }

    if (lines.empty())
    {
        return 0;
    }

    char** made = static_cast<char**>(mxCalloc(lines.size(), sizeof(char*)));
    for (size_t i = 0; i < lines.size(); i++)
    {
        made[i] = static_cast<char*>(mxMalloc(lines[i].size() + 1));
        memcpy(made[i], lines[i].c_str(), lines[i].size() + 1);
    }

    *stack = made;
    return static_cast<int>(lines.size());
}

int mclFreeStackTrace(char*** stack, int nStackDepth)
{
    if (stack == nullptr || *stack == nullptr)
    {
        return 0;
    }

    for (int i = 0; i < nStackDepth; i++)
    {
        mxFree((*stack)[i]);
    }

    mxFree(*stack);
    *stack = nullptr;
    return 0;
}

const char* mclGetLastErrorMessage(void)
{
    const JgrtManaged* engine = Engine.load();
    std::wstring text = engine == nullptr ? std::wstring() : Ask(engine->LastError, JgrtLastMessage);
    std::lock_guard<std::mutex> hold(Gate);
    LastMessage = JgrtToAnsi(text.data(), text.size());
    return LastMessage.c_str();
}

// ---- Instances and calls ---------------------------------------------------------------------

mclCtfStream mclGetEmbeddedCtfStream(void* path)
{
    std::wstring file = FromAnsi(static_cast<const char*>(path));
    if (file.empty() || !CarriesArchive(file))
    {
        return nullptr;
    }

    Stream* stream = new Stream{ file };
    return reinterpret_cast<mclCtfStream>(stream);
}

void mclDestroyStream(mclCtfStream stream) { delete reinterpret_cast<Stream*>(stream); }

bool mclInitializeComponentInstanceEmbedded(HMCRINSTANCE* inst, mclOutputHandlerFcn error_handler, mclOutputHandlerFcn print_handler,
    mclCtfStream ctfStream, const char* session_key)
{
    (void)session_key;
    if (ctfStream == nullptr)
    {
        return false;
    }

    return OpenInstance(inst, error_handler, print_handler, reinterpret_cast<Stream*>(ctfStream)->Path);
}

bool mclInitializeComponentInstanceCtfFileToCache(HMCRINSTANCE* inst, mclOutputHandlerFcn error_handler, mclOutputHandlerFcn print_handler,
    const char* ctfFileLocation)
{
    return OpenInstance(inst, error_handler, print_handler, FullPath(FromAnsi(ctfFileLocation)));
}

// What a library built with mcc -C calls: its archive is <component_name>.ctf beside the library
// (stage K11, k11_build: separate).
bool mclInitializeComponentInstanceNonEmbeddedStandalone(HMCRINSTANCE* inst, const char* path_to_component, const char* component_name,
    mccTargetType ttype, mclOutputHandlerFcn error_handler, mclOutputHandlerFcn print_handler, const char* session_key)
{
    (void)ttype;
    (void)session_key;
    if (path_to_component == nullptr || component_name == nullptr)
    {
        return false;
    }

    return OpenInstance(inst, error_handler, print_handler,
        FullPath(FolderOf(FromAnsi(path_to_component)) + L"\\" + FromAnsi(component_name) + L".ctf"));
}

bool mclTerminateInstance(HMCRINSTANCE* inst)
{
    if (inst == nullptr)
    {
        return false;
    }

    const JgrtManaged* engine = Engine.load();
    if (*inst != nullptr && engine != nullptr)
    {
        engine->CloseInstance(reinterpret_cast<intptr_t>(*inst));
    }

    *inst = nullptr;
    return true;
}

bool mclFeval(HMCRINSTANCE inst, const char* name, int nlhs, mxArray** plhs, int nrhs, mxArray** prhs)
{
    const JgrtManaged* engine = Engine.load();
    if (inst == nullptr || engine == nullptr || name == nullptr)
    {
        return false;
    }

    return engine->Feval(reinterpret_cast<intptr_t>(inst), JgrtFromBytes(name, strlen(name)).c_str(), nlhs, plhs, nrhs, prhs) != 0;
}

bool mclMlfFeval(HMCRINSTANCE inst, const char* name, int nargout, int fnout, int fnin, ...)
{
    va_list ap;
    va_start(ap, fnin);
    bool ok = mclMlfVFeval(inst, name, nargout, fnout, fnin, ap);
    va_end(ap);
    return ok;
}

// The arguments an mlf function passes: fnout output pointers, then fnin inputs, a negative count
// meaning its last argument is varargout's pointer or varargin's cell (k10_out\k10lib.c). A
// varargin that is not a cell is one argument, as R2025b passes it (k10_feval risky 7); a NULL
// element of varargin's cell is []. Outputs not asked for leave their pointers alone (k10_feval
// mlf.two.1s), and an output with no pointer to receive it is destroyed (risky 6).
bool mclMlfVFeval(HMCRINSTANCE inst, const char* name, int nargout, int fnout, int fnin, va_list ap)
{
    int outs = fnout < 0 ? -fnout : fnout;
    int ins = fnin < 0 ? -fnin : fnin;
    std::vector<mxArray**> outputs(static_cast<size_t>(outs));
    for (int i = 0; i < outs; i++)
    {
        outputs[i] = va_arg(ap, mxArray**);
    }

    std::vector<mxArray*> inputs;
    std::vector<mxArray*> made;
    for (int i = 0; i < ins; i++)
    {
        mxArray* value = va_arg(ap, mxArray*);
        if (fnin < 0 && i == ins - 1 && value != nullptr && mxIsCell(value))
        {
            for (size_t k = 0; k < mxGetNumberOfElements(value); k++)
            {
                mxArray* element = mxGetCell(value, k);
                if (element == nullptr)
                {
                    element = mxCreateDoubleMatrix(0, 0, mxREAL);
                    made.push_back(element);
                }

                inputs.push_back(element);
            }
        }
        else
        {
            inputs.push_back(value);
        }
    }

    int asked = nargout > 0 ? nargout : 0;
    std::vector<mxArray*> results(static_cast<size_t>(asked > 0 ? asked : 1), nullptr);
    bool ok = mclFeval(inst, name, nargout, results.data(), static_cast<int>(inputs.size()), inputs.empty() ? nullptr : inputs.data());
    for (mxArray* temporary : made)
    {
        mxDestroyArray(temporary);
    }

    if (!ok)
    {
        return false;
    }

    int named = fnout < 0 ? outs - 1 : outs;
    for (int i = 0; i < asked; i++)
    {
        if (i < named)
        {
            if (outputs[i] != nullptr)
            {
                *outputs[i] = results[i];
            }
            else
            {
                mxDestroyArray(results[i]);
            }

            results[i] = nullptr;
        }
    }

    if (fnout < 0 && asked > named)
    {
        mxArray* rest = mxCreateCellMatrix(1, static_cast<mwSize>(asked - named));
        for (int i = named; i < asked; i++)
        {
            mxSetCell(rest, static_cast<mwIndex>(i - named), results[i]);
            results[i] = nullptr;
        }

        if (outputs[outs - 1] != nullptr)
        {
            *outputs[outs - 1] = rest;
        }
        else
        {
            mxDestroyArray(rest);
        }
    }

    for (mxArray* left : results)
    {
        mxDestroyArray(left);
    }

    return true;
}

int mclWrite(int fd, const void* ptr, size_t count)
{
    if (ptr == nullptr || (fd != 1 && fd != 2))
    {
        return -1;
    }

    HANDLE stream = GetStdHandle(fd == 1 ? STD_OUTPUT_HANDLE : STD_ERROR_HANDLE);
    DWORD written = 0;
    if (stream == nullptr || stream == INVALID_HANDLE_VALUE)
    {
        return 0;
    }

    WriteFile(stream, ptr, static_cast<DWORD>(count), &written, nullptr);
    return static_cast<int>(written);
}

// Text for a C program's output or error handler, in the ANSI code page as mxArrayToString gives
// text; with no handler, standard output or standard error (fd 1 or 2) instead.
int jgrtCallHandler(intptr_t handler, int fd, const wchar_t* text, size_t count)
{
    std::string bytes = JgrtToAnsi(text, count);
    if (handler != 0)
    {
        return reinterpret_cast<mclOutputHandlerFcn>(handler)(bytes.c_str());
    }

    return mclWrite(fd, bytes.data(), bytes.size());
}

void mclWaitForFiguresToDie(HMCRINSTANCE inst)
{
    if (const JgrtManaged* engine = Engine.load(); engine != nullptr && inst != nullptr)
    {
        engine->WaitForFigures(reinterpret_cast<intptr_t>(inst));
    }
}
