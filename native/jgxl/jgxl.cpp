// jgxl: the template a JGraph Excel add-in is made of (compiler and SDK plan, stage K17c; decision KO9).
//
// mcc -W excel copies this DLL to <addin>_<major>_<minor>.dll and stamps it: resource 1 the type
// library JGraph writes for the add-in's functions (TYPELIB), resource 2 the add-in's archive
// (RT_RCDATA) when it is embedded, the version block, and the JGraph Runtime version it needs
// (JGRAPH_RUNTIME), which is R2025b's own resource layout for the add-ins its mbuild compiles.
// Nothing here knows the add-in's functions: the type library does, and the component itself is
// served by the JGraph Runtime's native bridge, jgrt.dll (com.cpp), which this file finds and
// forwards to:
//
//   DllGetClassObject    finds the JGraph Runtime of the stamped version (JGRAPH_RUNTIME_ROOT,
//                        then the registry key the runtime's installer writes, then the add-in's own
//                        folder for a runtime carried along, then a folder on the PATH holding
//                        jgrt.dll), loads its jgrt.dll and asks it for the class factory;
//   DllCanUnloadNow      asks jgrt.dll whether any object of this add-in is alive;
//   DllRegisterServer    writes the registration itself, from the type library alone, so a machine
//   DllUnregisterServer  registers the add-in before the runtime is installed: machine-wide when it
//                        may (an elevated regsvr32), else for the current user, which is what R2025b
//                        gives through MWRegsvr /useronly (the keys as its k17_registry recording
//                        shows them, under JGraph's own component category, decision KO10).
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <oaidl.h>
#include <ocidl.h>
#include <olectl.h>

#include <string>
#include <vector>

namespace
{
const wchar_t* const RuntimeVersionResource = L"JGRAPH_RUNTIME";
const wchar_t* const RootVariable = L"JGRAPH_RUNTIME_ROOT";
const wchar_t* const RegistryKey = L"SOFTWARE\\JGraph\\JGraph Runtime\\";
const wchar_t* const BridgeInRuntime = L"bin\\win64\\jgrt.dll";

// JGraph's component category for its Excel add-ins (decision KO10), where R2025b registers
// {5D003098-3D6B-11D5-B155-00D0B7BA7544} "MATLAB Excel Builder Components".
// {C4E7A6D2-5B0F-4E3A-8D91-7F2B6A1C9E35}
const GUID CategoryId = { 0xC4E7A6D2, 0x5B0F, 0x4E3A, { 0x8D, 0x91, 0x7F, 0x2B, 0x6A, 0x1C, 0x9E, 0x35 } };
const wchar_t* const CategoryName = L"JGraph Excel Add-in Components";

// The AppID every add-in names, as R2025b's name MathWorks' one ({64571771-615C-406E-82F2-1B02209353EE}).
// {9A3D5F71-2C8E-4B06-A1D4-6E0F7B2C3D58}
const wchar_t* const AppId = L"{9A3D5F71-2C8E-4B06-A1D4-6E0F7B2C3D58}";

HINSTANCE Self = nullptr;
HMODULE Bridge = nullptr;
HRESULT (*BridgeGetClassObject)(const wchar_t*, const CLSID*, const IID*, void**) = nullptr;
HRESULT (*BridgeCanUnloadNow)(const wchar_t*) = nullptr;

std::wstring ModulePath()
{
    std::vector<wchar_t> buffer(MAX_PATH);
    for (;;)
    {
        DWORD n = GetModuleFileNameW(Self, buffer.data(), static_cast<DWORD>(buffer.size()));
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
    std::wstring wide(static_cast<size_t>(n), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, utf8.data(), static_cast<int>(utf8.size()), wide.data(), n);
    return wide;
}

// An RCDATA resource's bytes as text.
std::string ResourceText(const wchar_t* name)
{
    HRSRC found = FindResourceW(Self, name, RT_RCDATA);
    HGLOBAL loaded = found == nullptr ? nullptr : LoadResource(Self, found);
    const void* bytes = loaded == nullptr ? nullptr : LockResource(loaded);
    DWORD size = found == nullptr ? 0 : SizeofResource(Self, found);
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
    LARGE_INTEGER size = {};
    if (GetFileSizeEx(file, &size) && size.QuadPart > 0 && size.QuadPart < (1 << 20))
    {
        bytes.resize(static_cast<size_t>(size.QuadPart));
        DWORD read = 0;
        if (!ReadFile(file, bytes.data(), static_cast<DWORD>(bytes.size()), &read, nullptr))
        {
            bytes.clear();
        }
    }

    CloseHandle(file);
    return bytes;
}

// The "version" string of a runtime's marker (jgraph-runtime.json), or empty (as jglaunch.cpp reads it).
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

// jgrt.dll of the JGraph Runtime of `version`, found as the launcher finds the runtime, then in a
// PATH folder (as a Python package's __init__.py looks, stage K13); empty when nowhere.
std::wstring FindBridge(const std::string& version, const std::wstring& ownFolder)
{
    std::vector<wchar_t> named(32768);
    DWORD n = GetEnvironmentVariableW(RootVariable, named.data(), static_cast<DWORD>(named.size()));
    if (n > 0 && n < named.size())
    {
        std::wstring root = TrimSeparators(std::wstring(named.data(), n));
        if (MarkerVersion(root) == version && FileExists(root + L"\\" + BridgeInRuntime))
        {
            return root + L"\\" + BridgeInRuntime;
        }
    }

    for (HKEY hive : { HKEY_LOCAL_MACHINE, HKEY_CURRENT_USER })
    {
        std::wstring root = RegisteredRoot(hive, Widen(version));
        if (!root.empty())
        {
            root = TrimSeparators(root);
            if (MarkerVersion(root) == version && FileExists(root + L"\\" + BridgeInRuntime))
            {
                return root + L"\\" + BridgeInRuntime;
            }
        }
    }

    if (MarkerVersion(ownFolder) == version && FileExists(ownFolder + L"\\" + BridgeInRuntime))
    {
        return ownFolder + L"\\" + BridgeInRuntime;
    }

    std::vector<wchar_t> path(32768);
    n = GetEnvironmentVariableW(L"PATH", path.data(), static_cast<DWORD>(path.size()));
    if (n > 0 && n < path.size())
    {
        std::wstring all(path.data(), n);
        size_t start = 0;
        while (start <= all.size())
        {
            size_t end = all.find(L';', start);
            if (end == std::wstring::npos)
            {
                end = all.size();
            }

            std::wstring folder = TrimSeparators(all.substr(start, end - start));
            if (!folder.empty() && FileExists(folder + L"\\jgrt.dll"))
            {
                return folder + L"\\jgrt.dll";
            }

            start = end + 1;
        }
    }

    return std::wstring();
}

// Loads the bridge once; false, with the reason set as IErrorInfo, when the runtime is not there.
bool LoadBridge()
{
    if (Bridge != nullptr)
    {
        return true;
    }

    std::string version = ResourceText(RuntimeVersionResource);
    std::wstring own = FolderOf(ModulePath());
    std::wstring bridge = version.empty() ? std::wstring() : FindBridge(version, own);
    std::wstring why;
    if (version.empty())
    {
        why = L"This add-in names no JGraph Runtime version: its JGRAPH_RUNTIME resource is missing.";
    }
    else if (bridge.empty())
    {
        why = L"The JGraph Runtime " + Widen(version) + L" this add-in needs was not found: set JGRAPH_RUNTIME_ROOT to its folder, "
            L"install it, or carry it beside the add-in.";
    }
    else
    {
        Bridge = LoadLibraryExW(bridge.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
        if (Bridge == nullptr)
        {
            why = L"The JGraph Runtime's native bridge " + bridge + L" could not be loaded.";
        }
        else
        {
            BridgeGetClassObject = reinterpret_cast<decltype(BridgeGetClassObject)>(GetProcAddress(Bridge, "jgrtXlGetClassObject"));
            BridgeCanUnloadNow = reinterpret_cast<decltype(BridgeCanUnloadNow)>(GetProcAddress(Bridge, "jgrtXlCanUnloadNow"));
            if (BridgeGetClassObject == nullptr || BridgeCanUnloadNow == nullptr)
            {
                why = L"The JGraph Runtime at " + bridge + L" is older than this add-in: it serves no Excel add-ins.";
                FreeLibrary(Bridge);
                Bridge = nullptr;
            }
        }
    }

    if (Bridge == nullptr)
    {
        ICreateErrorInfo* make = nullptr;
        if (SUCCEEDED(CreateErrorInfo(&make)) && make != nullptr)
        {
            BSTR source = SysAllocString(L"JGraph Excel add-in");
            BSTR description = SysAllocStringLen(why.data(), static_cast<UINT>(why.size()));
            make->SetSource(source);
            make->SetDescription(description);
            SysFreeString(source);
            SysFreeString(description);
            IErrorInfo* info = nullptr;
            if (SUCCEEDED(make->QueryInterface(IID_IErrorInfo, reinterpret_cast<void**>(&info))) && info != nullptr)
            {
                SetErrorInfo(0, info);
                info->Release();
            }

            make->Release();
        }

        return false;
    }

    return true;
}

// ---- Registration, from the type library alone -----------------------------------------------

struct Registration
{
    std::wstring path;
    std::wstring library;      // the type library's name, the add-in's
    std::wstring className;    // the coclass's
    std::wstring interfaceName;
    std::wstring version;      // major.minor
    std::wstring progId;       // <library>.<class>
    std::wstring versionedProgId;
    std::wstring clsid;
    std::wstring libid;
    GUID libidGuid = {};
    WORD major = 1;
    WORD minor = 0;
    ITypeLib* lib = nullptr;
};

std::wstring GuidText(const GUID& guid)
{
    wchar_t text[64];
    StringFromGUID2(guid, text, 64);
    return text;
}

std::wstring NameOf(ITypeInfo* info, MEMBERID id)
{
    BSTR name = nullptr;
    std::wstring text;
    if (SUCCEEDED(info->GetDocumentation(id, &name, nullptr, nullptr, nullptr)) && name != nullptr)
    {
        text.assign(name, SysStringLen(name));
    }

    SysFreeString(name);
    return text;
}

std::wstring DocOf(ITypeInfo* info)
{
    BSTR doc = nullptr;
    std::wstring text;
    if (SUCCEEDED(info->GetDocumentation(MEMBERID_NIL, nullptr, &doc, nullptr, nullptr)) && doc != nullptr)
    {
        text.assign(doc, SysStringLen(doc));
    }

    SysFreeString(doc);
    return text;
}

bool EndsWith(const std::wstring& text, const std::wstring& tail)
{
    return text.size() > tail.size() && text.compare(text.size() - tail.size(), tail.size(), tail) == 0;
}

// What the add-in's type library says about it; false when the DLL carries none or no coclass.
bool Describe(Registration& r)
{
    r.path = ModulePath();
    if (FAILED(LoadTypeLibEx(r.path.c_str(), REGKIND_NONE, &r.lib)) || r.lib == nullptr)
    {
        return false;
    }

    BSTR name = nullptr;
    BSTR doc = nullptr;
    if (SUCCEEDED(r.lib->GetDocumentation(-1, &name, &doc, nullptr, nullptr)) && name != nullptr)
    {
        r.library.assign(name, SysStringLen(name));
    }

    TLIBATTR* attr = nullptr;
    if (SUCCEEDED(r.lib->GetLibAttr(&attr)) && attr != nullptr)
    {
        r.libidGuid = attr->guid;
        r.libid = GuidText(attr->guid);
        r.major = attr->wMajorVerNum;
        r.minor = attr->wMinorVerNum;
        r.lib->ReleaseTLibAttr(attr);
    }

    wchar_t version[32];
    swprintf(version, 32, L"%u.%u", static_cast<unsigned>(r.major), static_cast<unsigned>(r.minor));
    r.version = version;

    // The names as given are in the documentation strings: a type library's own names are
    // case-insensitive, so a coclass K17Xl in a library k17xl reads back as k17xl (R2025b's too).
    if (doc != nullptr)
    {
        std::wstring text(doc, SysStringLen(doc));
        std::wstring tail = std::wstring(L" ") + version + L" Type Library";
        if (EndsWith(text, tail))
        {
            r.library = text.substr(0, text.size() - tail.size());
        }
    }

    SysFreeString(name);
    SysFreeString(doc);
    bool found = false;
    UINT count = r.lib->GetTypeInfoCount();
    for (UINT k = 0; k < count && !found; k++)
    {
        ITypeInfo* info = nullptr;
        if (FAILED(r.lib->GetTypeInfo(k, &info)) || info == nullptr)
        {
            continue;
        }

        TYPEATTR* tattr = nullptr;
        if (SUCCEEDED(info->GetTypeAttr(&tattr)) && tattr != nullptr)
        {
            if (tattr->typekind == TKIND_COCLASS)
            {
                r.clsid = GuidText(tattr->guid);
                r.className = NameOf(info, MEMBERID_NIL);
                std::wstring classDoc = DocOf(info);
                if (EndsWith(classDoc, L" Class"))
                {
                    r.className = classDoc.substr(0, classDoc.size() - 6);
                }

                HREFTYPE href = 0;
                ITypeInfo* iface = nullptr;
                if (SUCCEEDED(info->GetRefTypeOfImplType(0, &href)) && SUCCEEDED(info->GetRefTypeInfo(href, &iface)) && iface != nullptr)
                {
                    r.interfaceName = NameOf(iface, MEMBERID_NIL);
                    iface->Release();
                }

                found = true;
            }

            info->ReleaseTypeAttr(tattr);
        }

        info->Release();
    }

    if (!found)
    {
        return false;
    }

    r.progId = r.library + L"." + r.className;
    swprintf(version, 32, L".%u_%u", static_cast<unsigned>(r.major), static_cast<unsigned>(r.minor));
    r.versionedProgId = r.progId + version;
    return true;
}

LSTATUS WriteValue(HKEY classes, const std::wstring& key, const wchar_t* name, const std::wstring& value)
{
    HKEY made = nullptr;
    LSTATUS status = RegCreateKeyExW(classes, key.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &made, nullptr);
    if (status != ERROR_SUCCESS)
    {
        return status;
    }

    status = RegSetValueExW(made, name, 0, REG_SZ, reinterpret_cast<const BYTE*>(value.c_str()), static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t)));
    RegCloseKey(made);
    return status;
}

LSTATUS WriteKey(HKEY classes, const std::wstring& key)
{
    HKEY made = nullptr;
    LSTATUS status = RegCreateKeyExW(classes, key.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &made, nullptr);
    if (status == ERROR_SUCCESS)
    {
        RegCloseKey(made);
    }

    return status;
}

// The keys R2025b's registration writes (k17_registry: regsvr32.registered.*), under JGraph's category.
LSTATUS WriteRegistration(HKEY classes, const Registration& r, bool perUser)
{
    LSTATUS status = WriteValue(classes, r.progId, nullptr, r.className);
    if (status != ERROR_SUCCESS)
    {
        return status;
    }

    WriteValue(classes, r.progId + L"\\CLSID", nullptr, r.clsid);
    WriteValue(classes, r.progId + L"\\CurVer", nullptr, r.versionedProgId);
    WriteValue(classes, r.versionedProgId, nullptr, r.className);
    WriteValue(classes, r.versionedProgId + L"\\CLSID", nullptr, r.clsid);
    std::wstring clsid = L"CLSID\\" + r.clsid;
    WriteValue(classes, clsid, nullptr, r.className);
    WriteValue(classes, clsid, L"AppID", AppId);
    WriteKey(classes, clsid + L"\\Implemented Categories");
    WriteKey(classes, clsid + L"\\Implemented Categories\\" + GuidText(CategoryId));
    WriteValue(classes, clsid + L"\\InprocServer32", nullptr, r.path);
    WriteValue(classes, clsid + L"\\InprocServer32", L"ThreadingModel", L"Both");
    WriteValue(classes, clsid + L"\\ProgID", nullptr, r.versionedProgId);
    WriteValue(classes, clsid + L"\\TypeLib", nullptr, r.libid);
    WriteValue(classes, clsid + L"\\TypeLib", L"Version", r.version);
    WriteValue(classes, clsid + L"\\VersionIndependentProgID", nullptr, r.progId);
    WriteValue(classes, std::wstring(L"Component Categories\\") + GuidText(CategoryId), L"409", CategoryName);

    // The type library and the Interface keys of every interface in it, by the registrar for the hive.
    std::wstring folder = FolderOf(r.path);
    std::vector<wchar_t> path(r.path.begin(), r.path.end());
    path.push_back(L'\0');
    std::vector<wchar_t> help(folder.begin(), folder.end());
    help.push_back(L'\0');
    HRESULT hr = perUser ? RegisterTypeLibForUser(r.lib, path.data(), help.data()) : RegisterTypeLib(r.lib, path.data(), help.data());
    return SUCCEEDED(hr) ? ERROR_SUCCESS : static_cast<LSTATUS>(hr);
}

void RemoveRegistration(HKEY classes, const Registration& r, bool perUser)
{
    RegDeleteTreeW(classes, r.progId.c_str());
    RegDeleteTreeW(classes, r.versionedProgId.c_str());
    RegDeleteTreeW(classes, (L"CLSID\\" + r.clsid).c_str());
    if (perUser)
    {
        UnRegisterTypeLibForUser(r.libidGuid, r.major, r.minor, 0, SYS_WIN64);
    }
    else
    {
        UnRegisterTypeLib(r.libidGuid, r.major, r.minor, 0, SYS_WIN64);
    }
}

// Whether the key is there for this hive's view of HKEY_CLASSES_ROOT.
bool Registered(HKEY classes, const std::wstring& key)
{
    HKEY open = nullptr;
    if (RegOpenKeyExW(classes, key.c_str(), 0, KEY_READ, &open) != ERROR_SUCCESS)
    {
        return false;
    }

    RegCloseKey(open);
    return true;
}
}

extern "C" BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, void* reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
    {
        Self = instance;
        DisableThreadLibraryCalls(instance);
    }

    return TRUE;
}

extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID clsid, REFIID iid, void** ppv)
{
    if (ppv == nullptr)
    {
        return E_POINTER;
    }

    *ppv = nullptr;
    if (!LoadBridge())
    {
        return E_FAIL;
    }

    std::wstring path = ModulePath();
    return BridgeGetClassObject(path.c_str(), &clsid, &iid, ppv);
}

extern "C" HRESULT __stdcall DllCanUnloadNow()
{
    if (Bridge == nullptr || BridgeCanUnloadNow == nullptr)
    {
        return S_OK;
    }

    std::wstring path = ModulePath();
    return BridgeCanUnloadNow(path.c_str());
}

// Registers machine-wide when the caller may (HKEY_LOCAL_MACHINE's classes), else for the current
// user: what regsvr32 gives elevated, and what R2025b's MWRegsvr /useronly gives otherwise.
extern "C" HRESULT __stdcall DllRegisterServer()
{
    Registration r;
    if (!Describe(r))
    {
        if (r.lib != nullptr)
        {
            r.lib->Release();
        }

        return SELFREG_E_TYPELIB;
    }

    HKEY machine = nullptr;
    LSTATUS status = RegOpenKeyExW(HKEY_LOCAL_MACHINE, L"Software\\Classes", 0, KEY_WRITE, &machine);
    bool perUser = status != ERROR_SUCCESS;
    if (!perUser)
    {
        status = WriteRegistration(machine, r, false);
        RegCloseKey(machine);
        perUser = status == ERROR_ACCESS_DENIED;
    }

    if (perUser)
    {
        HKEY user = nullptr;
        status = RegOpenKeyExW(HKEY_CURRENT_USER, L"Software\\Classes", 0, KEY_WRITE, &user);
        if (status == ERROR_SUCCESS)
        {
            status = WriteRegistration(user, r, true);
            RegCloseKey(user);
        }
    }

    r.lib->Release();
    return status == ERROR_SUCCESS ? S_OK : SELFREG_E_CLASS;
}

// Removes the registration from both hives, wherever it is.
extern "C" HRESULT __stdcall DllUnregisterServer()
{
    Registration r;
    if (!Describe(r))
    {
        if (r.lib != nullptr)
        {
            r.lib->Release();
        }

        return SELFREG_E_TYPELIB;
    }

    bool removedAny = false;
    HKEY user = nullptr;
    if (RegOpenKeyExW(HKEY_CURRENT_USER, L"Software\\Classes", 0, KEY_WRITE | KEY_READ, &user) == ERROR_SUCCESS)
    {
        if (Registered(user, L"CLSID\\" + r.clsid) || Registered(user, r.progId))
        {
            RemoveRegistration(user, r, true);
            removedAny = true;
        }

        RegCloseKey(user);
    }

    HKEY machine = nullptr;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, L"Software\\Classes", 0, KEY_WRITE | KEY_READ, &machine) == ERROR_SUCCESS)
    {
        if (Registered(machine, L"CLSID\\" + r.clsid) || Registered(machine, r.progId))
        {
            RemoveRegistration(machine, r, false);
            removedAny = true;
        }

        RegCloseKey(machine);
    }

    r.lib->Release();
    (void)removedAny;
    return S_OK;
}
