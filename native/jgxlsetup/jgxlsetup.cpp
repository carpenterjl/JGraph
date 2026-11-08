// jgxlsetup: the template an Excel client's installer is made of (compiler and SDK plan, stage K17f).
//
// compiler.package.excelClientForProductionServer copies this program to <InstallerName>.exe and stamps
// it with the files to install as a zip with stored entries (RCDATA JGRAPH_INSTALL_BUNDLE), the
// configuration text R2025b's writes as _config.txt (RCDATA JGRAPH_EXCEL_CLIENT_CONFIG: CTFFile,
// CTFArchive, AutomationAddinDll, responseSizeLimit, timeoutSeconds, AutomationAddin, ServerInfo,
// certificateFile), the icon and the version block. R2025b's mps_excel_addin_installer.exe carries
// the same payload with the unzip tools it runs; this one unpacks the zip itself.
//
// Run, it asks where to install (the user's local application data, or Program Files for every user
// when it may), the server's address (ServerInfo, with the archive's name after it, which is how an
// Excel client's URL names the archive it calls) and the limits, then: unpacks the files, registers the
// client library and JGraph's server configuration add-in through their own DllRegisterServer (for the
// current user, or machine-wide when elevated and asked), writes the client's settings where it reads
// them (Software\JGraph\Excel Client\<client>), tells Excel to open the add-in workbook when there is
// one (the OPEN entry of its Options key, as Excel's own Add-Ins dialog writes it), and leaves an entry
// in Apps & Features whose uninstall string runs this program with /uninstall. Switches: /quiet (no
// dialog, the defaults), /dir <folder>, /url <server URL>, /allusers, /uninstall.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <windowsx.h>
#include <shlobj.h>
#include <shellapi.h>

#include <cstdint>
#include <cstring>
#include <map>
#include <string>
#include <vector>

#include "resource.h"

namespace
{
const wchar_t* const BundleResource = L"JGRAPH_INSTALL_BUNDLE";
const wchar_t* const ConfigResource = L"JGRAPH_EXCEL_CLIENT_CONFIG";
const wchar_t* const SettingsKey = L"Software\\JGraph\\Excel Client";
const wchar_t* const ConfigurationAddIn = L"jgxlconfig.dll";
const wchar_t* const Title = L"JGraph Excel client";

HINSTANCE Self = nullptr;

struct Options
{
    bool quiet = false;
    bool uninstall = false;
    bool allUsers = false;
    std::wstring folder;
    std::wstring url;
};

struct Config
{
    std::map<std::wstring, std::wstring> values;
    std::wstring Get(const wchar_t* key) const
    {
        auto found = values.find(key);
        return found == values.end() ? std::wstring() : found->second;
    }
};

// ---- Helpers -------------------------------------------------------------------------------------------------

std::wstring ModulePath()
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

std::wstring FileNameOf(const std::wstring& path)
{
    size_t at = path.find_last_of(L"\\/");
    return at == std::wstring::npos ? path : path.substr(at + 1);
}

std::wstring StemOf(const std::wstring& name)
{
    size_t dot = name.find_last_of(L'.');
    return dot == std::wstring::npos ? name : name.substr(0, dot);
}

std::wstring Widen(const std::string& utf8)
{
    if (utf8.empty())
    {
        return std::wstring();
    }

    int n = MultiByteToWideChar(CP_UTF8, 0, utf8.data(), static_cast<int>(utf8.size()), nullptr, 0);
    std::wstring wide(static_cast<size_t>(n), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, utf8.data(), static_cast<int>(utf8.size()), wide.data(), n);
    return wide;
}

std::wstring Trim(const std::wstring& text)
{
    size_t start = text.find_first_not_of(L" \t\r\n");
    size_t end = text.find_last_not_of(L" \t\r\n");
    return start == std::wstring::npos ? std::wstring() : text.substr(start, end - start + 1);
}

bool EndsWithNoCase(const std::wstring& text, const wchar_t* tail)
{
    size_t n = wcslen(tail);
    return text.size() >= n && _wcsicmp(text.c_str() + text.size() - n, tail) == 0;
}

std::string Resource(const wchar_t* name)
{
    HRSRC found = FindResourceW(Self, name, RT_RCDATA);
    HGLOBAL loaded = found == nullptr ? nullptr : LoadResource(Self, found);
    const void* bytes = loaded == nullptr ? nullptr : LockResource(loaded);
    DWORD size = found == nullptr ? 0 : SizeofResource(Self, found);
    return bytes == nullptr ? std::string() : std::string(static_cast<const char*>(bytes), size);
}

Config ReadConfig()
{
    Config c;
    std::wstring text = Widen(Resource(ConfigResource));
    size_t start = 0;
    while (start < text.size())
    {
        size_t end = text.find(L'\n', start);
        if (end == std::wstring::npos)
        {
            end = text.size();
        }

        std::wstring line = Trim(text.substr(start, end - start));
        start = end + 1;
        size_t equals = line.find(L'=');
        if (equals != std::wstring::npos)
        {
            c.values[line.substr(0, equals)] = line.substr(equals + 1);
        }
    }

    return c;
}

bool IsElevated()
{
    HANDLE token = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token))
    {
        return false;
    }

    TOKEN_ELEVATION elevation = {};
    DWORD size = sizeof(elevation);
    bool elevated = GetTokenInformation(token, TokenElevation, &elevation, size, &size) && elevation.TokenIsElevated != 0;
    CloseHandle(token);
    return elevated;
}

std::wstring KnownFolder(REFKNOWNFOLDERID id)
{
    wchar_t* path = nullptr;
    std::wstring result;
    if (SUCCEEDED(SHGetKnownFolderPath(id, 0, nullptr, &path)) && path != nullptr)
    {
        result = path;
    }

    CoTaskMemFree(path);
    return result;
}

std::wstring Version()
{
    std::wstring path = ModulePath();
    DWORD handle = 0;
    DWORD size = GetFileVersionInfoSizeW(path.c_str(), &handle);
    if (size == 0)
    {
        return L"1.0.0.0";
    }

    std::vector<BYTE> data(size);
    VS_FIXEDFILEINFO* info = nullptr;
    UINT length = 0;
    if (!GetFileVersionInfoW(path.c_str(), 0, size, data.data()) || !VerQueryValueW(data.data(), L"\\", reinterpret_cast<void**>(&info), &length) || info == nullptr)
    {
        return L"1.0.0.0";
    }

    wchar_t text[64];
    swprintf(text, 64, L"%u.%u.%u.%u", HIWORD(info->dwFileVersionMS), LOWORD(info->dwFileVersionMS), HIWORD(info->dwFileVersionLS), LOWORD(info->dwFileVersionLS));
    return text;
}

void Say(const Options& o, const std::wstring& text, bool error = false)
{
    if (!o.quiet)
    {
        MessageBoxW(nullptr, text.c_str(), Title, error ? MB_ICONERROR : MB_ICONINFORMATION);
    }
}

// ---- The zip (stored entries) --------------------------------------------------------------------------------

struct Entry
{
    std::wstring name;
    uint32_t offset = 0;      // the local header's
    uint32_t size = 0;
    uint16_t method = 0;
};

uint16_t Read16(const std::string& d, size_t at) { return static_cast<uint16_t>(static_cast<uint8_t>(d[at]) | (static_cast<uint8_t>(d[at + 1]) << 8)); }
uint32_t Read32(const std::string& d, size_t at) { return static_cast<uint32_t>(Read16(d, at)) | (static_cast<uint32_t>(Read16(d, at + 2)) << 16); }

bool ReadEntries(const std::string& zip, std::vector<Entry>& entries, std::wstring& why)
{
    if (zip.size() < 22)
    {
        why = L"The installer carries no files.";
        return false;
    }

    size_t eocd = std::string::npos;
    for (size_t at = zip.size() - 22; at + 1 > 0 && zip.size() - at <= 22 + 65535; at--)
    {
        if (Read32(zip, at) == 0x06054b50)
        {
            eocd = at;
            break;
        }

        if (at == 0)
        {
            break;
        }
    }

    if (eocd == std::string::npos)
    {
        why = L"The installer's payload is not a zip.";
        return false;
    }

    uint16_t count = Read16(zip, eocd + 10);
    uint32_t directory = Read32(zip, eocd + 16);
    size_t at = directory;
    for (uint16_t i = 0; i < count; i++)
    {
        if (at + 46 > zip.size() || Read32(zip, at) != 0x02014b50)
        {
            why = L"The installer's payload is damaged.";
            return false;
        }

        Entry e;
        e.method = Read16(zip, at + 10);
        e.size = Read32(zip, at + 24);
        uint16_t nameLength = Read16(zip, at + 28);
        uint16_t extraLength = Read16(zip, at + 30);
        uint16_t commentLength = Read16(zip, at + 32);
        e.offset = Read32(zip, at + 42);
        e.name = Widen(zip.substr(at + 46, nameLength));
        entries.push_back(e);
        at += 46u + nameLength + extraLength + commentLength;
    }

    return true;
}

bool WriteFileBytes(const std::wstring& path, const char* data, size_t size)
{
    HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE)
    {
        return false;
    }

    DWORD written = 0;
    bool ok = size == 0 || (WriteFile(file, data, static_cast<DWORD>(size), &written, nullptr) && written == size);
    CloseHandle(file);
    return ok;
}

bool Unpack(const std::string& zip, const std::wstring& folder, std::vector<std::wstring>& written, std::wstring& why)
{
    std::vector<Entry> entries;
    if (!ReadEntries(zip, entries, why))
    {
        return false;
    }

    for (const Entry& e : entries)
    {
        if (e.name.empty() || e.name.back() == L'/')
        {
            continue;
        }

        if (e.method != 0)
        {
            why = L"The installer's payload holds a compressed entry (" + e.name + L"), which this installer does not read.";
            return false;
        }

        if (e.offset + 30 > zip.size() || Read32(zip, e.offset) != 0x04034b50)
        {
            why = L"The installer's payload is damaged at " + e.name + L".";
            return false;
        }

        size_t data = e.offset + 30u + Read16(zip, e.offset + 26) + Read16(zip, e.offset + 28);
        if (data + e.size > zip.size())
        {
            why = L"The installer's payload is short at " + e.name + L".";
            return false;
        }

        std::wstring relative = e.name;
        for (wchar_t& c : relative)
        {
            if (c == L'/')
            {
                c = L'\\';
            }
        }

        if (relative.find(L"..") != std::wstring::npos || (relative.size() > 1 && relative[1] == L':'))
        {
            why = L"The installer's payload names a file outside the folder (" + e.name + L").";
            return false;
        }

        std::wstring target = folder + L"\\" + relative;
        SHCreateDirectoryExW(nullptr, FolderOf(target).c_str(), nullptr);
        if (!WriteFileBytes(target, zip.data() + data, e.size))
        {
            why = L"The file " + target + L" could not be written.";
            return false;
        }

        written.push_back(target);
    }

    return true;
}

// ---- Registration and settings ---------------------------------------------------------------------------------

typedef HRESULT(__stdcall* SelfRegistration)();

bool CallExport(const std::wstring& dll, const char* name, std::wstring& why)
{
    HMODULE module = LoadLibraryExW(dll.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
    if (module == nullptr)
    {
        why = L"The library " + dll + L" could not be loaded.";
        return false;
    }

    auto entry = reinterpret_cast<SelfRegistration>(GetProcAddress(module, name));
    HRESULT hr = entry == nullptr ? E_NOTIMPL : entry();
    FreeLibrary(module);
    if (FAILED(hr))
    {
        wchar_t code[32];
        swprintf(code, 32, L"0x%08X", static_cast<unsigned>(hr));
        why = L"The library " + FileNameOf(dll) + L" could not be registered (" + code + L").";
        return false;
    }

    return true;
}

void WriteText(HKEY hive, const std::wstring& key, const wchar_t* name, const std::wstring& value)
{
    HKEY made = nullptr;
    if (RegCreateKeyExW(hive, key.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &made, nullptr) == ERROR_SUCCESS)
    {
        RegSetValueExW(made, name, 0, REG_SZ, reinterpret_cast<const BYTE*>(value.c_str()), static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t)));
        RegCloseKey(made);
    }
}

void WriteNumber(HKEY hive, const std::wstring& key, const wchar_t* name, DWORD value)
{
    HKEY made = nullptr;
    if (RegCreateKeyExW(hive, key.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &made, nullptr) == ERROR_SUCCESS)
    {
        RegSetValueExW(made, name, 0, REG_DWORD, reinterpret_cast<const BYTE*>(&value), sizeof(value));
        RegCloseKey(made);
    }
}

std::wstring ReadText(HKEY hive, const std::wstring& key, const wchar_t* name)
{
    HKEY open = nullptr;
    if (RegOpenKeyExW(hive, key.c_str(), 0, KEY_READ, &open) != ERROR_SUCCESS)
    {
        return std::wstring();
    }

    DWORD size = 0;
    std::wstring value;
    if (RegQueryValueExW(open, name, nullptr, nullptr, nullptr, &size) == ERROR_SUCCESS && size > 0)
    {
        value.resize(size / sizeof(wchar_t));
        RegQueryValueExW(open, name, nullptr, nullptr, reinterpret_cast<BYTE*>(value.data()), &size);
        value.resize(wcslen(value.c_str()));
    }

    RegCloseKey(open);
    return value;
}

std::wstring UninstallKey(const std::wstring& client) { return L"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\JGraphExcelClient_" + client; }

// Excel's Options keys (every Office version installed for the user): where an OPEN entry tells Excel to open a workbook at start.
std::vector<std::wstring> ExcelOptionsKeys(HKEY hive)
{
    std::vector<std::wstring> keys;
    HKEY office = nullptr;
    if (RegOpenKeyExW(hive, L"Software\\Microsoft\\Office", 0, KEY_READ, &office) != ERROR_SUCCESS)
    {
        return keys;
    }

    for (DWORD i = 0;; i++)
    {
        wchar_t name[64];
        DWORD size = 64;
        if (RegEnumKeyExW(office, i, name, &size, nullptr, nullptr, nullptr, nullptr) != ERROR_SUCCESS)
        {
            break;
        }

        std::wstring candidate = std::wstring(L"Software\\Microsoft\\Office\\") + name + L"\\Excel\\Options";
        HKEY options = nullptr;
        if (iswdigit(name[0]) && RegOpenKeyExW(hive, candidate.c_str(), 0, KEY_READ, &options) == ERROR_SUCCESS)
        {
            keys.push_back(candidate);
            RegCloseKey(options);
        }
    }

    RegCloseKey(office);
    return keys;
}

// The OPEN entries, as Excel's Add-Ins dialog writes them: OPEN, OPEN1, OPEN2, ... each "/R "<path>"".
void OpenWorkbookAtStart(HKEY hive, const std::wstring& workbook, bool add)
{
    std::wstring wanted = L"/R \"" + workbook + L"\"";
    for (const std::wstring& key : ExcelOptionsKeys(hive))
    {
        HKEY options = nullptr;
        if (RegOpenKeyExW(hive, key.c_str(), 0, KEY_READ | KEY_WRITE, &options) != ERROR_SUCCESS)
        {
            continue;
        }

        int firstFree = -1;
        bool present = false;
        for (int n = 0; n < 200; n++)
        {
            std::wstring name = n == 0 ? L"OPEN" : L"OPEN" + std::to_wstring(n);
            DWORD size = 0;
            if (RegQueryValueExW(options, name.c_str(), nullptr, nullptr, nullptr, &size) != ERROR_SUCCESS)
            {
                if (firstFree < 0)
                {
                    firstFree = n;
                }

                continue;
            }

            std::wstring value(size / sizeof(wchar_t), L'\0');
            RegQueryValueExW(options, name.c_str(), nullptr, nullptr, reinterpret_cast<BYTE*>(value.data()), &size);
            value.resize(wcslen(value.c_str()));
            if (_wcsicmp(value.c_str(), wanted.c_str()) == 0)
            {
                present = true;
                if (!add)
                {
                    RegDeleteValueW(options, name.c_str());
                }
            }
        }

        if (add && !present && firstFree >= 0)
        {
            std::wstring name = firstFree == 0 ? L"OPEN" : L"OPEN" + std::to_wstring(firstFree);
            RegSetValueExW(options, name.c_str(), 0, REG_SZ, reinterpret_cast<const BYTE*>(wanted.c_str()), static_cast<DWORD>((wanted.size() + 1) * sizeof(wchar_t)));
        }

        RegCloseKey(options);
    }
}

// ---- Install and uninstall ----------------------------------------------------------------------------------------

struct Plan
{
    Config config;
    std::wstring client;         // the add-in's name: the client library's stem
    std::wstring library;        // AutomationAddinDll
    std::wstring archive;        // AutomationAddin
    std::wstring defaultFolder;
    std::wstring defaultUrl;     // ServerInfo/AutomationAddin
    int timeout = 60;
    int limit = 750;
    std::vector<std::wstring> files;
};

Plan MakePlan(const Options& o)
{
    Plan p;
    p.config = ReadConfig();
    p.library = p.config.Get(L"AutomationAddinDll");
    p.archive = p.config.Get(L"AutomationAddin");
    p.client = StemOf(p.library);
    if (p.client.empty())
    {
        p.client = p.archive.empty() ? L"client" : p.archive;
    }

    std::wstring base = o.allUsers ? KnownFolder(FOLDERID_ProgramFiles) + L"\\JGraph Excel Clients" : KnownFolder(FOLDERID_LocalAppData) + L"\\JGraph\\Excel Clients";
    p.defaultFolder = base + L"\\" + p.client;
    std::wstring info = p.config.Get(L"ServerInfo");
    if (!info.empty())
    {
        while (!info.empty() && info.back() == L'/')
        {
            info.pop_back();
        }

        p.defaultUrl = p.archive.empty() ? info : info + L"/" + p.archive;
    }

    std::wstring t = p.config.Get(L"timeoutSeconds");
    std::wstring l = p.config.Get(L"responseSizeLimit");
    if (!t.empty() && _wtoi(t.c_str()) > 0) p.timeout = _wtoi(t.c_str());
    if (!l.empty() && _wtoi(l.c_str()) > 0) p.limit = _wtoi(l.c_str());
    std::vector<Entry> entries;
    std::wstring why;
    if (ReadEntries(Resource(BundleResource), entries, why))
    {
        for (const Entry& e : entries)
        {
            if (!e.name.empty() && e.name.back() != L'/')
            {
                p.files.push_back(e.name);
            }
        }
    }

    return p;
}

bool Install(const Options& o, const Plan& p, const std::wstring& folder, const std::wstring& url, int timeout, int limit, std::wstring& report, std::wstring& why)
{
    HKEY hive = o.allUsers ? HKEY_LOCAL_MACHINE : HKEY_CURRENT_USER;
    if (SHCreateDirectoryExW(nullptr, folder.c_str(), nullptr) != ERROR_SUCCESS && GetLastError() != ERROR_ALREADY_EXISTS && GetLastError() != ERROR_FILE_EXISTS)
    {
        why = L"The folder " + folder + L" could not be made.";
        return false;
    }

    std::vector<std::wstring> written;
    if (!Unpack(Resource(BundleResource), folder, written, why))
    {
        return false;
    }

    // The client library and the configuration add-in registered through their own DllRegisterServer:
    // machine-wide when this process may (elevated, for every user), else for the current user.
    std::wstring library;
    std::wstring workbook;
    std::wstring configAddIn;
    std::wstring certificate;
    for (const std::wstring& file : written)
    {
        std::wstring name = FileNameOf(file);
        if (_wcsicmp(name.c_str(), p.library.c_str()) == 0) library = file;
        else if (_wcsicmp(name.c_str(), ConfigurationAddIn) == 0) configAddIn = file;
        else if (EndsWithNoCase(name, L".xlam") || EndsWithNoCase(name, L".xla")) workbook = file;
        else if (EndsWithNoCase(name, L".pem") || EndsWithNoCase(name, L".crt") || EndsWithNoCase(name, L".cer")) certificate = file;
    }

    std::wstring registered;
    if (!library.empty())
    {
        if (!CallExport(library, "DllRegisterServer", why))
        {
            return false;
        }

        registered += L"\n  " + FileNameOf(library);
    }

    if (!configAddIn.empty())
    {
        std::wstring ignored;
        if (CallExport(configAddIn, "DllRegisterServer", ignored))
        {
            registered += L"\n  " + FileNameOf(configAddIn) + L" (the Configure Production Server button on Excel's Add-ins tab)";
        }
    }

    // The client's settings, where it reads them.
    std::wstring key = std::wstring(SettingsKey) + L"\\" + p.client;
    WriteText(hive, key, L"ServerURL", url);
    WriteNumber(hive, key, L"timeoutSeconds", static_cast<DWORD>(timeout));
    WriteNumber(hive, key, L"responseSizeLimit", static_cast<DWORD>(limit));
    std::wstring configured = p.config.Get(L"certificateFile");
    if (!configured.empty())
    {
        // The certificate as packaged when it is not where the building machine had it.
        DWORD attributes = GetFileAttributesW(configured.c_str());
        WriteText(hive, key, L"certificateFile", attributes != INVALID_FILE_ATTRIBUTES || certificate.empty() ? configured : certificate);
    }

    if (!workbook.empty())
    {
        OpenWorkbookAtStart(HKEY_CURRENT_USER, workbook, true);
    }

    // This program kept beside the files for the uninstall, and the entry in Apps & Features.
    std::wstring self = ModulePath();
    std::wstring kept = folder + L"\\" + FileNameOf(self);
    if (_wcsicmp(self.c_str(), kept.c_str()) != 0)
    {
        CopyFileW(self.c_str(), kept.c_str(), FALSE);
    }

    std::wstring uninstall = UninstallKey(p.client);
    WriteText(hive, uninstall, L"DisplayName", p.client + L" Excel client (JGraph)");
    WriteText(hive, uninstall, L"DisplayVersion", Version());
    WriteText(hive, uninstall, L"Publisher", L"JGraph");
    WriteText(hive, uninstall, L"InstallLocation", folder);
    WriteText(hive, uninstall, L"DisplayIcon", kept);
    WriteText(hive, uninstall, L"UninstallString", L"\"" + kept + L"\" /uninstall" + (o.allUsers ? L" /allusers" : L""));
    WriteNumber(hive, uninstall, L"NoModify", 1);
    WriteNumber(hive, uninstall, L"NoRepair", 1);
    report = L"The " + p.client + L" Excel client is installed in\n  " + folder + L"\n\nServer: " + url + L"\nRegistered:" + registered
        + (workbook.empty() ? L"" : L"\n\nExcel opens " + FileNameOf(workbook) + L" when it starts; its functions can be used in any workbook's cells.");
    return true;
}

bool Uninstall(const Options& o, const Plan& p, std::wstring& report, std::wstring& why)
{
    HKEY hive = o.allUsers ? HKEY_LOCAL_MACHINE : HKEY_CURRENT_USER;
    std::wstring folder = ReadText(hive, UninstallKey(p.client), L"InstallLocation");
    if (folder.empty())
    {
        folder = FolderOf(ModulePath());
    }

    std::wstring self = ModulePath();
    std::vector<std::wstring> files;
    WIN32_FIND_DATAW found;
    HANDLE search = FindFirstFileW((folder + L"\\*").c_str(), &found);
    if (search != INVALID_HANDLE_VALUE)
    {
        do
        {
            if ((found.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0)
            {
                files.push_back(folder + L"\\" + found.cFileName);
            }
        } while (FindNextFileW(search, &found));
        FindClose(search);
    }

    for (const std::wstring& file : files)
    {
        std::wstring name = FileNameOf(file);
        if (_wcsicmp(name.c_str(), p.library.c_str()) == 0 || _wcsicmp(name.c_str(), ConfigurationAddIn) == 0)
        {
            std::wstring ignored;
            CallExport(file, "DllUnregisterServer", ignored);
        }

        if (EndsWithNoCase(name, L".xlam") || EndsWithNoCase(name, L".xla"))
        {
            OpenWorkbookAtStart(HKEY_CURRENT_USER, file, false);
        }
    }

    RegDeleteTreeW(hive, (std::wstring(SettingsKey) + L"\\" + p.client).c_str());
    RegDeleteTreeW(hive, UninstallKey(p.client).c_str());
    bool leftover = false;
    for (const std::wstring& file : files)
    {
        if (_wcsicmp(file.c_str(), self.c_str()) == 0)
        {
            continue;
        }

        leftover = !DeleteFileW(file.c_str()) || leftover;
    }

    // This program and its folder go once it has exited.
    std::wstring command = L"cmd.exe /c ping -n 3 127.0.0.1 >nul & del /q \"" + self + L"\" & rmdir \"" + folder + L"\"";
    STARTUPINFOW startup = {};
    startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESHOWWINDOW;
    startup.wShowWindow = SW_HIDE;
    PROCESS_INFORMATION process = {};
    std::vector<wchar_t> line(command.begin(), command.end());
    line.push_back(L'\0');
    if (CreateProcessW(nullptr, line.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &startup, &process))
    {
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
    }

    (void)why;
    report = L"The " + p.client + L" Excel client is removed" + (leftover ? L"; a file in use is left in " + folder : L"") + L".";
    return true;
}

// ---- The dialog ---------------------------------------------------------------------------------------------------

struct DialogState
{
    const Plan* plan;
    Options* options;
    std::wstring folder;
    std::wstring url;
    int timeout = 60;
    int limit = 750;
};

std::wstring ControlText(HWND dialog, int id)
{
    HWND control = GetDlgItem(dialog, id);
    int n = GetWindowTextLengthW(control);
    std::wstring text(static_cast<size_t>(n) + 1, L'\0');
    GetWindowTextW(control, text.data(), n + 1);
    text.resize(static_cast<size_t>(n));
    return text;
}

INT_PTR CALLBACK InstallDialogProc(HWND dialog, UINT message, WPARAM wParam, LPARAM lParam)
{
    auto* state = reinterpret_cast<DialogState*>(GetWindowLongPtrW(dialog, GWLP_USERDATA));
    switch (message)
    {
    case WM_INITDIALOG:
    {
        state = reinterpret_cast<DialogState*>(lParam);
        SetWindowLongPtrW(dialog, GWLP_USERDATA, lParam);
        const Plan& p = *state->plan;
        SetWindowTextW(dialog, (L"Install the " + p.client + L" Excel client").c_str());
        SetDlgItemTextW(dialog, IDC_INTRO, (L"Installs the " + p.client + L" Excel client for the Production Server: its worksheet functions call the archive " + p.archive + L" on the server named below.").c_str());
        SetDlgItemTextW(dialog, IDC_FOLDER, state->folder.c_str());
        SetDlgItemTextW(dialog, IDC_URL, state->url.c_str());
        SetDlgItemTextW(dialog, IDC_TIMEOUT, std::to_wstring(state->timeout).c_str());
        SetDlgItemTextW(dialog, IDC_LIMIT, std::to_wstring(state->limit).c_str());
        bool elevated = IsElevated();
        EnableWindow(GetDlgItem(dialog, IDC_ALLUSERS), elevated);
        CheckDlgButton(dialog, IDC_ALLUSERS, state->options->allUsers && elevated ? BST_CHECKED : BST_UNCHECKED);
        HWND files = GetDlgItem(dialog, IDC_FILES);
        for (const std::wstring& f : p.files)
        {
            ListBox_AddString(files, f.c_str());
        }

        return TRUE;
    }

    case WM_COMMAND:
        switch (LOWORD(wParam))
        {
        case IDC_ALLUSERS:
        {
            bool all = IsDlgButtonChecked(dialog, IDC_ALLUSERS) == BST_CHECKED;
            std::wstring base = all ? KnownFolder(FOLDERID_ProgramFiles) + L"\\JGraph Excel Clients" : KnownFolder(FOLDERID_LocalAppData) + L"\\JGraph\\Excel Clients";
            SetDlgItemTextW(dialog, IDC_FOLDER, (base + L"\\" + state->plan->client).c_str());
            return TRUE;
        }

        case IDC_FOLDERBROWSE:
        {
            BROWSEINFOW info = {};
            info.hwndOwner = dialog;
            info.lpszTitle = L"The folder the client's files go in";
            info.ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE;
            PIDLIST_ABSOLUTE chosen = SHBrowseForFolderW(&info);
            if (chosen != nullptr)
            {
                wchar_t path[MAX_PATH] = {};
                if (SHGetPathFromIDListW(chosen, path))
                {
                    SetDlgItemTextW(dialog, IDC_FOLDER, path);
                }

                CoTaskMemFree(chosen);
            }

            return TRUE;
        }

        case IDOK:
            state->folder = Trim(ControlText(dialog, IDC_FOLDER));
            state->url = Trim(ControlText(dialog, IDC_URL));
            state->timeout = _wtoi(ControlText(dialog, IDC_TIMEOUT).c_str());
            state->limit = _wtoi(ControlText(dialog, IDC_LIMIT).c_str());
            state->options->allUsers = IsDlgButtonChecked(dialog, IDC_ALLUSERS) == BST_CHECKED;
            if (state->folder.empty())
            {
                MessageBoxW(dialog, L"Name the folder to install into.", Title, MB_ICONINFORMATION);
                return TRUE;
            }

            if (state->timeout <= 0 || state->limit <= 0)
            {
                MessageBoxW(dialog, L"The timeout and the response limit are whole numbers above zero.", Title, MB_ICONINFORMATION);
                return TRUE;
            }

            EndDialog(dialog, IDOK);
            return TRUE;
        case IDCANCEL:
            EndDialog(dialog, IDCANCEL);
            return TRUE;
        default:
            break;
        }

        break;
    default:
        break;
    }

    return FALSE;
}

Options ReadOptions()
{
    Options o;
    int count = 0;
    LPWSTR* args = CommandLineToArgvW(GetCommandLineW(), &count);
    for (int i = 1; args != nullptr && i < count; i++)
    {
        std::wstring a = args[i];
        if (_wcsicmp(a.c_str(), L"/quiet") == 0 || _wcsicmp(a.c_str(), L"/q") == 0 || _wcsicmp(a.c_str(), L"/s") == 0) o.quiet = true;
        else if (_wcsicmp(a.c_str(), L"/uninstall") == 0) o.uninstall = true;
        else if (_wcsicmp(a.c_str(), L"/allusers") == 0) o.allUsers = true;
        else if (_wcsicmp(a.c_str(), L"/dir") == 0 && i + 1 < count) o.folder = args[++i];
        else if (_wcsicmp(a.c_str(), L"/url") == 0 && i + 1 < count) o.url = args[++i];
    }

    LocalFree(args);
    return o;
}
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE previous, LPWSTR commandLine, int show)
{
    (void)previous; (void)commandLine; (void)show;
    Self = instance;
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    Options o = ReadOptions();
    Plan p = MakePlan(o);
    if (o.allUsers && !IsElevated())
    {
        Say(o, L"Installing for every user needs administrator rights: run the installer as an administrator, or without /allusers.", true);
        return 3;
    }

    std::wstring report;
    std::wstring why;
    if (o.uninstall)
    {
        if (!o.quiet && MessageBoxW(nullptr, (L"Remove the " + p.client + L" Excel client?").c_str(), Title, MB_ICONQUESTION | MB_OKCANCEL) != IDOK)
        {
            return 1;
        }

        Uninstall(o, p, report, why);
        Say(o, report);
        return 0;
    }

    DialogState state;
    state.plan = &p;
    state.options = &o;
    state.folder = o.folder.empty() ? p.defaultFolder : o.folder;
    state.url = o.url.empty() ? p.defaultUrl : o.url;
    state.timeout = p.timeout;
    state.limit = p.limit;
    if (!o.quiet)
    {
        INT_PTR answer = DialogBoxParamW(Self, MAKEINTRESOURCEW(IDD_INSTALL), nullptr, InstallDialogProc, reinterpret_cast<LPARAM>(&state));
        if (answer != IDOK)
        {
            return 1;
        }

        if (o.allUsers && !IsElevated())
        {
            Say(o, L"Installing for every user needs administrator rights.", true);
            return 3;
        }
    }

    if (!state.url.empty() && state.url.compare(0, 7, L"http://") != 0 && state.url.compare(0, 8, L"https://") != 0)
    {
        state.url = L"http://" + state.url;
    }

    if (!Install(o, p, state.folder, state.url, state.timeout, state.limit, report, why))
    {
        Say(o, why, true);
        return 2;
    }

    Say(o, report);
    return 0;
}
