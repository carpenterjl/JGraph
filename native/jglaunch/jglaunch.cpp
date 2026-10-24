// jglaunch: the launcher a JGraph standalone program is made of (compiler and SDK plan, stage K6).
//
// mcc -m copies the console build of this file (jglaunch-console.exe) and mcc -e the windowed one
// (jglaunch-gui.exe, built with JGLAUNCH_GUI); the build then stamps the program's icon, version,
// help text and the JGraph Runtime version it needs into its resources, and appends the program's
// archive after the last byte, as R2025b's mcc does with its own launcher. At start the launcher:
//
//   1. prints the help and stops for a first argument of -? or /? (R2025b's launcher does that
//      without starting its runtime: the -h file's text, or "Usage:<program> <optional arguments>");
//   2. finds the JGraph Runtime of the version it was built for: JGRAPH_RUNTIME_ROOT, then the
//      registry key the runtime's installer writes (the machine's, then the user's), then the
//      program's own folder, which holds the runtime when the program carries it;
//   3. shows splash.png from beside the program while the runtime starts (windowed build only, as
//      R2025b's -e program does; the runtime closes it);
//   4. starts .NET through hostfxr on the runtime's jglaunch.dll, handing it the program's path and
//      its arguments, and exits with the code the program's run ends with.
//
// Nothing here knows the archive's format: the runtime reads it from the program.

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <shellapi.h>
#ifdef JGLAUNCH_GUI
#include <objidl.h>
#include <gdiplus.h>
#endif

#include <string>
#include <vector>

#include "nethost.h"
#include "hostfxr.h"

namespace
{
const wchar_t* const RuntimeVersionResource = L"JGRAPH_RUNTIME";
const wchar_t* const HelpResource = L"JGRAPH_HELP";
const wchar_t* const RootVariable = L"JGRAPH_RUNTIME_ROOT";
const wchar_t* const RegistryKey = L"SOFTWARE\\JGraph\\JGraph Runtime\\";
const wchar_t* const ManagedEntry = L"bin\\win64\\jglaunch.dll";

// The exit code of a program the launcher could not start: the runtime is missing, or .NET is.
const int CannotStart = 1;

std::wstring ExePath()
{
    std::vector<wchar_t> buffer(MAX_PATH);
    for (;;)
    {
        DWORD n = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
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

std::wstring TrimSeparators(std::wstring path)
{
    while (path.size() > 3 && (path.back() == L'\\' || path.back() == L'/'))
    {
        path.pop_back();
    }

    return path;
}

bool FileExists(const std::wstring& path)
{
    DWORD attributes = GetFileAttributesW(path.c_str());
    return attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY) == 0;
}

std::wstring Widen(const std::string& utf8)
{
    if (utf8.empty())
    {
        return std::wstring();
    }

    int n = MultiByteToWideChar(CP_UTF8, 0, utf8.data(), static_cast<int>(utf8.size()), nullptr, 0);
    std::wstring wide(n, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, utf8.data(), static_cast<int>(utf8.size()), wide.data(), n);
    return wide;
}

std::string Narrow(const std::wstring& wide)
{
    if (wide.empty())
    {
        return std::string();
    }

    int n = WideCharToMultiByte(CP_UTF8, 0, wide.data(), static_cast<int>(wide.size()), nullptr, 0, nullptr, nullptr);
    std::string utf8(n, '\0');
    WideCharToMultiByte(CP_UTF8, 0, wide.data(), static_cast<int>(wide.size()), utf8.data(), n, nullptr, nullptr);
    return utf8;
}

// An RCDATA resource of this program, as bytes; empty when there is none.
std::string ReadResource(const wchar_t* name)
{
    HRSRC found = FindResourceW(nullptr, name, MAKEINTRESOURCEW(10));
    if (found == nullptr)
    {
        return std::string();
    }

    HGLOBAL loaded = LoadResource(nullptr, found);
    DWORD size = SizeofResource(nullptr, found);
    const void* bytes = loaded == nullptr ? nullptr : LockResource(loaded);
    return bytes == nullptr ? std::string() : std::string(static_cast<const char*>(bytes), size);
}

std::string ReadFileBytes(const std::wstring& path)
{
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE)
    {
        return std::string();
    }

    std::string bytes;
    LARGE_INTEGER size;
    if (GetFileSizeEx(file, &size) && size.QuadPart > 0 && size.QuadPart < (1 << 20))
    {
        bytes.resize(static_cast<size_t>(size.QuadPart));
        DWORD read = 0;
        if (!ReadFile(file, bytes.data(), static_cast<DWORD>(bytes.size()), &read, nullptr))
        {
            read = 0;
        }

        bytes.resize(read);
    }

    CloseHandle(file);
    return bytes;
}

// The "version" string of a runtime's marker (jgraph-runtime.json), or empty.
std::string MarkerVersion(const std::wstring& root)
{
    std::string text = ReadFileBytes(root + L"\\jgraph-runtime.json");
    size_t key = text.find("\"version\"");
    if (key == std::string::npos)
    {
        return std::string();
    }

    size_t colon = text.find(':', key + 9);
    size_t open = colon == std::string::npos ? std::string::npos : text.find('"', colon + 1);
    size_t close = open == std::string::npos ? std::string::npos : text.find('"', open + 1);
    return close == std::string::npos ? std::string() : text.substr(open + 1, close - open - 1);
}

std::wstring RegisteredRoot(HKEY hive, const std::wstring& version)
{
    std::wstring key = std::wstring(RegistryKey) + version;
    DWORD size = 0;
    if (RegGetValueW(hive, key.c_str(), L"Location", RRF_RT_REG_SZ, nullptr, nullptr, &size) != ERROR_SUCCESS || size == 0)
    {
        return std::wstring();
    }

    std::wstring value(size / sizeof(wchar_t), L'\0');
    if (RegGetValueW(hive, key.c_str(), L"Location", RRF_RT_REG_SZ, nullptr, value.data(), &size) != ERROR_SUCCESS)
    {
        return std::wstring();
    }

    value.resize(wcslen(value.c_str()));
    return value;
}

// The root of the JGraph Runtime of `version`, found as RuntimeLayout.Find finds it; empty when none.
std::wstring FindRuntime(const std::string& version, const std::wstring& programFolder)
{
    std::vector<wchar_t> named(32768);
    DWORD n = GetEnvironmentVariableW(RootVariable, named.data(), static_cast<DWORD>(named.size()));
    if (n > 0 && n < named.size())
    {
        std::wstring root = TrimSeparators(std::wstring(named.data(), n));
        if (MarkerVersion(root) == version)
        {
            return root;
        }
    }

    for (HKEY hive : { HKEY_LOCAL_MACHINE, HKEY_CURRENT_USER })
    {
        std::wstring root = RegisteredRoot(hive, Widen(version));
        if (!root.empty())
        {
            root = TrimSeparators(root);
            if (MarkerVersion(root) == version)
            {
                return root;
            }
        }
    }

    return MarkerVersion(programFolder) == version ? programFolder : std::wstring();
}

// Writes UTF-8 text to a standard stream; false when the program has none (a windowed program
// started from the shell).
bool WriteStream(DWORD which, const std::wstring& text)
{
    HANDLE stream = GetStdHandle(which);
    if (stream == nullptr || stream == INVALID_HANDLE_VALUE)
    {
        return false;
    }

    DWORD written = 0;
    DWORD mode = 0;
    if (GetConsoleMode(stream, &mode))
    {
        return WriteConsoleW(stream, text.data(), static_cast<DWORD>(text.size()), &written, nullptr) != 0;
    }

    std::string bytes = Narrow(text);
    return WriteFile(stream, bytes.data(), static_cast<DWORD>(bytes.size()), &written, nullptr) != 0;
}

std::wstring ProgramName(const std::wstring& path)
{
    size_t at = path.find_last_of(L"\\/");
    return at == std::wstring::npos ? path : path.substr(at + 1);
}

// Says why the program cannot run: on standard error, or in a message box for a windowed program
// that has no standard error.
void Report(const std::wstring& program, const std::wstring& text)
{
#ifdef JGLAUNCH_GUI
    if (!WriteStream(STD_ERROR_HANDLE, text + L"\n"))
    {
        MessageBoxW(nullptr, text.c_str(), ProgramName(program).c_str(), MB_OK | MB_ICONERROR);
    }
#else
    (void)program;
    WriteStream(STD_ERROR_HANDLE, text + L"\n");
#endif
}

void ShowHelp(const std::wstring& program)
{
    std::wstring text = Widen(ReadResource(HelpResource));
    if (text.empty())
    {
        text = L"Usage:" + program + L" <optional arguments>";
    }

    if (text.back() != L'\n')
    {
        text += L"\n";
    }

#ifdef JGLAUNCH_GUI
    if (!WriteStream(STD_OUTPUT_HANDLE, text))
    {
        MessageBoxW(nullptr, text.c_str(), ProgramName(program).c_str(), MB_OK | MB_ICONINFORMATION);
    }
#else
    WriteStream(STD_OUTPUT_HANDLE, text);
#endif
}

std::wstring hostErrors;

void HOSTFXR_CALLTYPE CollectHostError(const char_t* message)
{
    hostErrors += message;
    hostErrors += L"\n";
}

#ifdef JGLAUNCH_GUI
// The splash: splash.png from beside the program, drawn with its transparency in a borderless
// window centred on the screen, on a thread of its own while the main thread starts the runtime.
// The runtime closes it (WM_CLOSE) when the program's main is about to run.
struct Splash
{
    std::wstring Image;
    HWND Window = nullptr;
    HANDLE Ready = nullptr;
};

LRESULT CALLBACK SplashProc(HWND window, UINT message, WPARAM w, LPARAM l)
{
    if (message == WM_CLOSE)
    {
        DestroyWindow(window);
        return 0;
    }

    if (message == WM_DESTROY)
    {
        PostQuitMessage(0);
        return 0;
    }

    return DefWindowProcW(window, message, w, l);
}

DWORD WINAPI SplashThread(void* parameter)
{
    auto* splash = static_cast<Splash*>(parameter);
    Gdiplus::GdiplusStartupInput input;
    ULONG_PTR token = 0;
    if (Gdiplus::GdiplusStartup(&token, &input, nullptr) != Gdiplus::Ok)
    {
        SetEvent(splash->Ready);
        return 0;
    }

    {
        Gdiplus::Bitmap image(splash->Image.c_str());
        UINT width = image.GetWidth();
        UINT height = image.GetHeight();
        Gdiplus::Rect all(0, 0, static_cast<INT>(width), static_cast<INT>(height));
        Gdiplus::BitmapData pixels;
        if (image.GetLastStatus() == Gdiplus::Ok && width > 0 && height > 0
            && image.LockBits(&all, Gdiplus::ImageLockModeRead, PixelFormat32bppPARGB, &pixels) == Gdiplus::Ok)
        {
            WNDCLASSEXW type = { sizeof(type) };
            type.lpfnWndProc = SplashProc;
            type.hInstance = GetModuleHandleW(nullptr);
            type.hCursor = LoadCursorW(nullptr, IDC_APPSTARTING);
            type.lpszClassName = L"JGraphSplash";
            RegisterClassExW(&type);

            RECT work;
            SystemParametersInfoW(SPI_GETWORKAREA, 0, &work, 0);
            int x = work.left + ((work.right - work.left) - static_cast<int>(width)) / 2;
            int y = work.top + ((work.bottom - work.top) - static_cast<int>(height)) / 2;
            HWND window = CreateWindowExW(WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST, type.lpszClassName, L"", WS_POPUP,
                x, y, static_cast<int>(width), static_cast<int>(height), nullptr, nullptr, type.hInstance, nullptr);

            BITMAPINFO format = {};
            format.bmiHeader.biSize = sizeof(format.bmiHeader);
            format.bmiHeader.biWidth = static_cast<LONG>(width);
            format.bmiHeader.biHeight = -static_cast<LONG>(height);
            format.bmiHeader.biPlanes = 1;
            format.bmiHeader.biBitCount = 32;
            format.bmiHeader.biCompression = BI_RGB;
            void* bits = nullptr;
            HDC screen = GetDC(nullptr);
            HDC memory = CreateCompatibleDC(screen);
            HBITMAP section = CreateDIBSection(screen, &format, DIB_RGB_COLORS, &bits, nullptr, 0);
            if (window != nullptr && section != nullptr)
            {
                for (UINT row = 0; row < height; row++)
                {
                    memcpy(static_cast<BYTE*>(bits) + static_cast<size_t>(row) * width * 4,
                        static_cast<BYTE*>(pixels.Scan0) + static_cast<ptrdiff_t>(row) * pixels.Stride, static_cast<size_t>(width) * 4);
                }

                HGDIOBJ previous = SelectObject(memory, section);
                POINT origin = { 0, 0 };
                POINT at = { x, y };
                SIZE extent = { static_cast<LONG>(width), static_cast<LONG>(height) };
                BLENDFUNCTION blend = { AC_SRC_OVER, 0, 255, AC_SRC_ALPHA };
                UpdateLayeredWindow(window, screen, &at, &extent, memory, &origin, 0, &blend, ULW_ALPHA);
                SelectObject(memory, previous);
                ShowWindow(window, SW_SHOWNOACTIVATE);
                splash->Window = window;
            }

            if (section != nullptr)
            {
                DeleteObject(section);
            }

            DeleteDC(memory);
            ReleaseDC(nullptr, screen);
            image.UnlockBits(&pixels);
        }

        SetEvent(splash->Ready);
        if (splash->Window != nullptr)
        {
            MSG message;
            while (GetMessageW(&message, nullptr, 0, 0) > 0)
            {
                TranslateMessage(&message);
                DispatchMessageW(&message);
            }
        }
    }

    Gdiplus::GdiplusShutdown(token);
    return 0;
}
#endif

int Run()
{
    std::wstring program = ExePath();
    std::wstring folder = FolderOf(program);

    int argc = 0;
    wchar_t** argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    std::vector<std::wstring> arguments;
    for (int i = 1; argv != nullptr && i < argc; i++)
    {
        arguments.emplace_back(argv[i]);
    }

    if (argv != nullptr)
    {
        LocalFree(argv);
    }

    if (!arguments.empty() && (arguments[0] == L"-?" || arguments[0] == L"/?"))
    {
        ShowHelp(program);
        return 0;
    }

    std::wstring version = Widen(ReadResource(RuntimeVersionResource));
    if (version.empty())
    {
        Report(program, L"This program was not built by JGraph's mcc: it names no JGraph Runtime.");
        return CannotStart;
    }

    std::wstring root = FindRuntime(Narrow(version), folder);
    if (root.empty())
    {
        Report(program, L"Could not find version " + version + L" of the JGraph Runtime.\n"
            L"Install the JGraph Runtime " + version + L", or set " + RootVariable + L" to the folder of one.");
        return CannotStart;
    }

    std::wstring managed = root + L"\\" + ManagedEntry;
    if (!FileExists(managed))
    {
        Report(program, L"The JGraph Runtime at " + root + L" is damaged: " + ManagedEntry + L" is missing.");
        return CannotStart;
    }

#ifdef JGLAUNCH_GUI
    Splash splash;
    HANDLE splashThread = nullptr;
    splash.Image = folder + L"\\splash.png";
    if (FileExists(splash.Image))
    {
        splash.Ready = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        splashThread = CreateThread(nullptr, 0, SplashThread, &splash, 0, nullptr);
        if (splashThread != nullptr)
        {
            WaitForSingleObject(splash.Ready, 5000);
        }
    }
#endif

    std::vector<wchar_t> hostfxrPath(MAX_PATH);
    size_t size = hostfxrPath.size();
    get_hostfxr_parameters where = { sizeof(where), managed.c_str(), nullptr };
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
        : reinterpret_cast<hostfxr_initialize_for_dotnet_command_line_fn>(GetProcAddress(hostfxr, "hostfxr_initialize_for_dotnet_command_line"));
    auto run = hostfxr == nullptr ? nullptr : reinterpret_cast<hostfxr_run_app_fn>(GetProcAddress(hostfxr, "hostfxr_run_app"));
    auto close = hostfxr == nullptr ? nullptr : reinterpret_cast<hostfxr_close_fn>(GetProcAddress(hostfxr, "hostfxr_close"));

    int code = CannotStart;
    if (setErrorWriter == nullptr || initialize == nullptr || run == nullptr || close == nullptr)
    {
        Report(program, L"The JGraph Runtime needs the .NET 8 Desktop Runtime (x64), which this computer does not have.");
    }
    else
    {
        // The managed entry's command line: the program, the splash to close, then the program's own.
        std::vector<std::wstring> line = { managed, L"--program", program };
#ifdef JGLAUNCH_GUI
        if (splash.Window != nullptr)
        {
            line.push_back(L"--splash");
            line.push_back(std::to_wstring(reinterpret_cast<UINT_PTR>(splash.Window)));
        }
#endif
        line.push_back(L"--");
        line.insert(line.end(), arguments.begin(), arguments.end());
        std::vector<const char_t*> pointers;
        for (const std::wstring& item : line)
        {
            pointers.push_back(item.c_str());
        }

        setErrorWriter(CollectHostError);
        hostfxr_initialize_parameters parameters = { sizeof(parameters), program.c_str(), nullptr };
        hostfxr_handle context = nullptr;
        int started = initialize(static_cast<int>(pointers.size()), pointers.data(), &parameters, &context);
        if (started < 0 || context == nullptr)
        {
            Report(program, L"The JGraph Runtime could not start .NET:\n" + hostErrors);
        }
        else
        {
            setErrorWriter(nullptr);
            code = run(context);
        }

        if (context != nullptr)
        {
            close(context);
        }
    }

#ifdef JGLAUNCH_GUI
    if (splashThread != nullptr)
    {
        if (splash.Window != nullptr)
        {
            PostMessageW(splash.Window, WM_CLOSE, 0, 0);
        }

        WaitForSingleObject(splashThread, 2000);
        CloseHandle(splashThread);
    }

    if (splash.Ready != nullptr)
    {
        CloseHandle(splash.Ready);
    }
#endif

    return code;
}
}

#ifdef JGLAUNCH_GUI
int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    return Run();
}
#else
int wmain()
{
    return Run();
}
#endif
