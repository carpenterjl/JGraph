/*
 * jgraph/cpplib.hpp - a C++ shared library's runtime as the JGraph Runtime gives it (compiler and
 * SDK plan, stage K12): the application a program starts once, the libraries it opens from their
 * archives (<name>.ctf), and runMain.
 *
 * MathWorks' names in the namespace jgraph::cpplib (decision KO3); the text is JGraph's own. An
 * archive named without a full path is looked for where R2025b's header looks (record-k12.ps1,
 * recordings\k12_paths.out.txt): the folder CPPSHARED_BASE_CTF_PATH names, the working folder,
 * then the program's own folder. The application's state is the process's, kept in jgrt.dll.
 */
#ifndef JGRAPH_CPPLIB_HPP
#define JGRAPH_CPPLIB_HPP

#include "execution.hpp"

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

#include <iostream>

namespace jgraph {
namespace cpplib {

using namespace jgraph::execution;

/// Whether the runtime runs in the program's process. JGraph's always does.
enum class MATLABApplicationMode { OUT_OF_PROCESS = 0, IN_PROCESS = 1 };

/// What starting the application or opening a library refuses with.
class CppSharedLibException final : public jgraph::execution::Exception {
public:
    CppSharedLibException() {}
    CppSharedLibException(const std::string& msg) : jgraph::execution::Exception(msg) {}
};

class MATLABApplication;
class MATLABLibrary;

std::shared_ptr<MATLABApplication> initMATLABApplication(const MATLABApplicationMode mode = MATLABApplicationMode::IN_PROCESS,
                                                         const std::vector<std::u16string>& options = std::vector<std::u16string>());
std::unique_ptr<MATLABLibrary> initMATLABLibrary(std::shared_ptr<MATLABApplication> application, const std::u16string& ctffilename,
                                                 const std::u16string& session_key = std::u16string());
FutureResult<std::unique_ptr<MATLABLibrary>> initMATLABLibraryAsync(std::shared_ptr<MATLABApplication> application, const std::u16string& ctffilename,
                                                                    const std::u16string& session_key = std::u16string());
int runMain(std::function<int(std::shared_ptr<MATLABApplication>, int, const char**)>, std::shared_ptr<MATLABApplication>&& appsession, int argc,
            const char** argv);

/// The running application: the runtime ends when the last holder of it lets it go.
class MATLABApplication {
public:
    ~MATLABApplication() { jgrtCppStop(); }
    bool isInProc() { return _mode == MATLABApplicationMode::IN_PROCESS; }

private:
    MATLABApplication(const MATLABApplicationMode mode, const std::vector<std::u16string>& options) : _mode(mode), _options(options) {}
    friend std::shared_ptr<MATLABApplication> initMATLABApplication(const MATLABApplicationMode mode, const std::vector<std::u16string>& options);
    MATLABApplicationMode _mode;
    std::vector<std::u16string> _options;
};

/// A library opened from its archive: its functions called through feval and fevalAsync.
class MATLABLibrary : public jgraph::execution::ExecutionInterface {
public:
    ~MATLABLibrary() {
        if (matlabHandle != 0) {
            jgrtCppClose(matlabHandle);
            matlabHandle = 0;
        }
    }

    /// Waits until none of the library's figures is on screen.
    void waitForFiguresToClose() {
        if (matlabHandle != 0) {
            jgrtCppWaitForFigures(matlabHandle);
        }
    }

private:
    friend FutureResult<std::unique_ptr<MATLABLibrary>> initMATLABLibraryAsync(std::shared_ptr<MATLABApplication> application,
                                                                               const std::u16string& ctffilename, const std::u16string& session_key);
    MATLABLibrary(std::shared_ptr<MATLABApplication> application, uint64_t handle)
        : jgraph::execution::ExecutionInterface(handle, application ? application->isInProc() : true), appPtr_(std::move(application)) {}
    std::shared_ptr<MATLABApplication> appPtr_;
};

namespace detail {

inline std::wstring WideOf(const std::u16string& s) { return std::wstring(s.begin(), s.end()); }
inline std::u16string U16Of(const std::wstring& s) { return std::u16string(s.begin(), s.end()); }

inline bool IsFile(const std::wstring& path) {
    DWORD attributes = GetFileAttributesW(path.c_str());
    return attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY) == 0;
}

inline std::wstring Joined(std::wstring folder, const std::wstring& name) {
    if (folder.empty()) {
        return std::wstring();
    }
    if (folder.back() != L'\\' && folder.back() != L'/') {
        folder += L'\\';
    }
    return folder + name;
}

inline std::wstring Variable(const wchar_t* name) {
    DWORD n = GetEnvironmentVariableW(name, nullptr, 0);
    if (n == 0) {
        return std::wstring();
    }
    std::wstring value(n, L'\0');
    n = GetEnvironmentVariableW(name, &value[0], n);
    value.resize(n);
    return value;
}

inline std::wstring WorkingFolder() {
    DWORD n = GetCurrentDirectoryW(0, nullptr);
    std::wstring folder(n, L'\0');
    n = GetCurrentDirectoryW(n, &folder[0]);
    folder.resize(n);
    return folder;
}

inline std::wstring ProgramFolder() {
    std::wstring path(MAX_PATH, L'\0');
    for (;;) {
        DWORD n = GetModuleFileNameW(nullptr, &path[0], static_cast<DWORD>(path.size()));
        if (n < path.size()) {
            path.resize(n);
            break;
        }
        path.resize(path.size() * 2);
    }
    size_t slash = path.find_last_of(L"\\/");
    return slash == std::wstring::npos ? std::wstring() : path.substr(0, slash + 1);
}

/// Where an archive is: a full path as given (when the file is there), else the first of the
/// folder CPPSHARED_BASE_CTF_PATH names, the working folder and the program's folder that has it;
/// empty when none does.
inline std::wstring PathToCtf(const std::u16string& given) {
    std::wstring name = WideOf(given);
    bool full = name.size() > 2 && (name[0] == L'\\' || name[0] == L'/' || name[0] == L'~' || name[1] == L':');
    if (full) {
        return IsFile(name) ? name : std::wstring();
    }
    for (const std::wstring& folder : {Variable(L"CPPSHARED_BASE_CTF_PATH"), WorkingFolder(), ProgramFolder()}) {
        std::wstring candidate = Joined(folder, name);
        if (!candidate.empty() && IsFile(candidate)) {
            return candidate;
        }
    }
    return std::wstring();
}

inline std::wstring FullPathOf(const std::wstring& path) {
    DWORD n = GetFullPathNameW(path.c_str(), 0, nullptr, nullptr);
    if (n == 0) {
        return path;
    }
    std::wstring full(n, L'\0');
    n = GetFullPathNameW(path.c_str(), n, &full[0], nullptr);
    full.resize(n);
    return full;
}

} // namespace detail

/// Starts the application, once in a process: a second start while it runs, or any after it ended,
/// is refused in R2025b's words (twice). JGraph's runtime runs in the program's process in either mode.
inline std::shared_ptr<MATLABApplication> initMATLABApplication(const MATLABApplicationMode mode, const std::vector<std::u16string>& options) {
    std::vector<const char16_t*> texts;
    for (const std::u16string& option : options) {
        texts.push_back(option.c_str());
    }
    char16_t* error = nullptr;
    int started = jgrtCppStart(mode == MATLABApplicationMode::OUT_OF_PROCESS ? 1 : 0, texts.empty() ? nullptr : texts.data(), texts.size(), &error);
    std::u16string reason = jgraph::execution::detail::Taken(error);
    switch (started) {
    case 0:
        return std::shared_ptr<MATLABApplication>(new MATLABApplication(mode, options));
    case 1:
        throw CppSharedLibException("MATLABApplication is already initialized.");
    case 2:
        throw CppSharedLibException("MATLABApplication is already terminated and cannot be reinitialized.");
    default:
        throw CppSharedLibException(data::detail::Utf8Of(reason));
    }
}

inline std::unique_ptr<MATLABLibrary> initMATLABLibrary(std::shared_ptr<MATLABApplication> application, const std::u16string& ctffilename,
                                                        const std::u16string& session_key) {
    return initMATLABLibraryAsync(std::move(application), ctffilename, session_key).get();
}

/// Opens a library from its archive, found as PathToCtf finds it; one not found is refused at
/// once, one that cannot be opened when the future is read (with the reason on standard error).
inline FutureResult<std::unique_ptr<MATLABLibrary>> initMATLABLibraryAsync(std::shared_ptr<MATLABApplication> application,
                                                                           const std::u16string& ctffilename, const std::u16string& session_key) {
    (void)session_key;
    std::wstring path = detail::PathToCtf(ctffilename);
    if (path.empty()) {
        throw CppSharedLibException("Unable to open file '" + data::detail::Utf8Of(ctffilename) + "'.");
    }
    std::u16string full = detail::U16Of(detail::FullPathOf(path));
    std::future<std::unique_ptr<MATLABLibrary>> opened = std::async(std::launch::async, [application, full]() {
        char16_t* error = nullptr;
        uint64_t handle = jgrtCppOpen(full.c_str(), &error);
        std::u16string reason = jgraph::execution::detail::Taken(error);
        if (handle == 0) {
            if (!reason.empty()) {
                std::cerr << data::detail::Utf8Of(reason) << std::endl;
            }
            throw CppSharedLibException("Failed to initialize MATLABLibrary.");
        }
        return std::unique_ptr<MATLABLibrary>(new MATLABLibrary(application, handle));
    });
    return FutureResult<std::unique_ptr<MATLABLibrary>>(std::move(opened));
}

/// Runs the program's main work with the application and answers what it returns.
inline int runMain(std::function<int(std::shared_ptr<MATLABApplication>, int, const char**)> mainFcn, std::shared_ptr<MATLABApplication>&& appsession,
                   int argc, const char** argv) {
    return mainFcn(std::move(appsession), argc, argv);
}

} // namespace cpplib
} // namespace jgraph

#endif // JGRAPH_CPPLIB_HPP
