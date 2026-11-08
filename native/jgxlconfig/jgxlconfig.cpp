// jgxlconfig: JGraph's server configuration add-in for Excel (compiler and SDK plan, stage K17f), the
// counterpart of the ServerConfig.dll R2025b's compiler.package.excelClientForProductionServer carries
// in every installer.
//
// A COM add-in for Excel (IDTExtensibility2) with a ribbon part (IRibbonExtensibility): its one button,
// "Configure Production Server" in the Add-ins tab, opens a dialog that writes where an Excel client
// for the Production Server finds its server: HKEY_CURRENT_USER\Software\JGraph\Excel Client\<client>
// with ServerURL (http://host:port/<archive>), timeoutSeconds, responseSizeLimit (megabytes) and
// certificateFile, and the Logging subkey (Enabled, LoggingLevel, LogFileFolder) every client reads
// (jgxlmps.cpp). R2025b's keeps MathWorks' under Software\MathWorks\MATLAB Builder EX\2.5, the URL as
// a value name with data 1. The same dialog opens with no Excel through the exported JgxlConfigDialog
// (rundll32 jgxlconfig.dll,JgxlConfigDialog [client]), which the installer and a user without the
// ribbon can use.
//
// DllRegisterServer writes the class (ProgID JGraph.ServerConfig) and Excel's add-in entry
// (Software\Microsoft\Office\Excel\Addins\JGraph.ServerConfig: FriendlyName, Description,
// LoadBehavior 3) machine-wide when the caller may, else for the current user.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <windowsx.h>
#include <commdlg.h>
#include <shlobj.h>
#include <oaidl.h>
#include <olectl.h>

#include <string>
#include <vector>

#include "resource.h"

namespace
{
// {7C2E9A41-5D63-4F8B-9A17-3B6E0D4C8F52}
const CLSID AddInClsid = { 0x7C2E9A41, 0x5D63, 0x4F8B, { 0x9A, 0x17, 0x3B, 0x6E, 0x0D, 0x4C, 0x8F, 0x52 } };
// IDTExtensibility2 {B65AD801-ABAF-11D0-BB8B-00A0C90F2744}
const IID IID_IDTExtensibility2 = { 0xB65AD801, 0xABAF, 0x11D0, { 0xBB, 0x8B, 0x00, 0xA0, 0xC9, 0x0F, 0x27, 0x44 } };
// IRibbonExtensibility {000C0396-0000-0000-C000-000000000046}
const IID IID_IRibbonExtensibility = { 0x000C0396, 0x0000, 0x0000, { 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46 } };

const wchar_t* const ProgId = L"JGraph.ServerConfig";
const wchar_t* const FriendlyName = L"JGraph Server Configuration";
const wchar_t* const Description = L"Configures the Production Server address for JGraph Excel clients";
const wchar_t* const SettingsKey = L"Software\\JGraph\\Excel Client";
const wchar_t* const OfficeAddInKey = L"Software\\Microsoft\\Office\\Excel\\Addins\\JGraph.ServerConfig";
const DISPID ConfigureId = 1;

const wchar_t* const RibbonXml =
    L"<customUI xmlns=\"http://schemas.microsoft.com/office/2006/01/customui\">"
    L"<ribbon><tabs><tab idMso=\"TabAddIns\"><group id=\"jgraphServer\" label=\"JGraph\">"
    L"<button id=\"jgraphConfigure\" label=\"Configure Production Server\" size=\"large\" imageMso=\"ServerProperties\" "
    L"screentip=\"Configure Production Server\" supertip=\"Sets the server address, limits and logging of the JGraph Excel clients installed for you.\" "
    L"onAction=\"OnConfigure\"/></group></tab></tabs></ribbon></customUI>";

HINSTANCE Self = nullptr;
LONG Objects = 0;
LONG Locks = 0;

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

std::wstring GuidText(const GUID& guid)
{
    wchar_t text[64];
    StringFromGUID2(guid, text, 64);
    return text;
}

std::wstring Trim(const std::wstring& text)
{
    size_t start = text.find_first_not_of(L" \t\r\n");
    size_t end = text.find_last_not_of(L" \t\r\n");
    return start == std::wstring::npos ? std::wstring() : text.substr(start, end - start + 1);
}

// ---- The settings ----------------------------------------------------------------------------------

std::wstring ReadString(HKEY key, const wchar_t* name)
{
    DWORD type = 0;
    DWORD size = 0;
    if (key == nullptr || RegQueryValueExW(key, name, nullptr, &type, nullptr, &size) != ERROR_SUCCESS || size == 0)
    {
        return std::wstring();
    }

    if (type == REG_DWORD)
    {
        DWORD value = 0;
        size = sizeof(value);
        RegQueryValueExW(key, name, nullptr, nullptr, reinterpret_cast<BYTE*>(&value), &size);
        return std::to_wstring(value);
    }

    std::wstring value(size / sizeof(wchar_t), L'\0');
    RegQueryValueExW(key, name, nullptr, nullptr, reinterpret_cast<BYTE*>(value.data()), &size);
    value.resize(wcslen(value.c_str()));
    return value;
}

HKEY OpenRead(HKEY hive, const std::wstring& path)
{
    HKEY key = nullptr;
    return RegOpenKeyExW(hive, path.c_str(), 0, KEY_READ, &key) == ERROR_SUCCESS ? key : nullptr;
}

struct ClientSettings
{
    std::wstring url;
    std::wstring timeout = L"120";
    std::wstring limit = L"64";
    std::wstring certificate;
};

struct LogSettings
{
    bool enabled = false;
    int level = 0;
    std::wstring folder;
};

// The clients configured so far: the subkeys of the settings key in either hive, Logging aside.
std::vector<std::wstring> KnownClients()
{
    std::vector<std::wstring> names;
    for (HKEY hive : { HKEY_CURRENT_USER, HKEY_LOCAL_MACHINE })
    {
        HKEY key = OpenRead(hive, SettingsKey);
        if (key == nullptr)
        {
            continue;
        }

        for (DWORD i = 0;; i++)
        {
            wchar_t name[256];
            DWORD size = 256;
            if (RegEnumKeyExW(key, i, name, &size, nullptr, nullptr, nullptr, nullptr) != ERROR_SUCCESS)
            {
                break;
            }

            if (_wcsicmp(name, L"Logging") == 0)
            {
                continue;
            }

            bool seen = false;
            for (const std::wstring& n : names)
            {
                seen = seen || _wcsicmp(n.c_str(), name) == 0;
            }

            if (!seen)
            {
                names.push_back(name);
            }
        }

        RegCloseKey(key);
    }

    return names;
}

ClientSettings ReadClient(const std::wstring& client)
{
    ClientSettings s;
    for (HKEY hive : { HKEY_CURRENT_USER, HKEY_LOCAL_MACHINE })
    {
        HKEY key = OpenRead(hive, std::wstring(SettingsKey) + L"\\" + client);
        if (key == nullptr)
        {
            continue;
        }

        s.url = ReadString(key, L"ServerURL");
        std::wstring t = ReadString(key, L"timeoutSeconds");
        std::wstring l = ReadString(key, L"responseSizeLimit");
        if (!t.empty()) s.timeout = t;
        if (!l.empty()) s.limit = l;
        s.certificate = ReadString(key, L"certificateFile");
        RegCloseKey(key);
        break;
    }

    return s;
}

LogSettings ReadLog()
{
    LogSettings s;
    for (HKEY hive : { HKEY_CURRENT_USER, HKEY_LOCAL_MACHINE })
    {
        HKEY key = OpenRead(hive, std::wstring(SettingsKey) + L"\\Logging");
        if (key == nullptr)
        {
            continue;
        }

        s.enabled = ReadString(key, L"Enabled") == L"1";
        std::wstring level = ReadString(key, L"LoggingLevel");
        s.level = level.empty() ? 0 : _wtoi(level.c_str());
        s.folder = ReadString(key, L"LogFileFolder");
        RegCloseKey(key);
        break;
    }

    return s;
}

bool WriteSettings(const std::wstring& client, const ClientSettings& s, const LogSettings& log, std::wstring& why)
{
    HKEY key = nullptr;
    if (RegCreateKeyExW(HKEY_CURRENT_USER, (std::wstring(SettingsKey) + L"\\" + client).c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &key, nullptr) != ERROR_SUCCESS)
    {
        why = L"The settings key could not be written.";
        return false;
    }

    auto text = [&](HKEY k, const wchar_t* name, const std::wstring& value)
    {
        RegSetValueExW(k, name, 0, REG_SZ, reinterpret_cast<const BYTE*>(value.c_str()), static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t)));
    };
    auto number = [&](HKEY k, const wchar_t* name, DWORD value)
    {
        RegSetValueExW(k, name, 0, REG_DWORD, reinterpret_cast<const BYTE*>(&value), sizeof(value));
    };
    text(key, L"ServerURL", s.url);
    number(key, L"timeoutSeconds", static_cast<DWORD>(_wtoi(s.timeout.c_str())));
    number(key, L"responseSizeLimit", static_cast<DWORD>(_wtoi(s.limit.c_str())));
    if (s.certificate.empty())
    {
        RegDeleteValueW(key, L"certificateFile");
    }
    else
    {
        text(key, L"certificateFile", s.certificate);
    }

    RegCloseKey(key);
    if (RegCreateKeyExW(HKEY_CURRENT_USER, (std::wstring(SettingsKey) + L"\\Logging").c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &key, nullptr) == ERROR_SUCCESS)
    {
        number(key, L"Enabled", log.enabled ? 1 : 0);
        number(key, L"LoggingLevel", static_cast<DWORD>(log.level));
        text(key, L"LogFileFolder", log.folder);
        RegCloseKey(key);
    }

    return true;
}

// ---- The dialog -----------------------------------------------------------------------------------------

struct DialogState
{
    std::wstring client; // the client to show first, or empty
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

void ShowClient(HWND dialog, const std::wstring& client)
{
    ClientSettings s = ReadClient(client);
    SetDlgItemTextW(dialog, IDC_URL, s.url.c_str());
    SetDlgItemTextW(dialog, IDC_TIMEOUT, s.timeout.c_str());
    SetDlgItemTextW(dialog, IDC_LIMIT, s.limit.c_str());
    SetDlgItemTextW(dialog, IDC_CERT, s.certificate.c_str());
}

void BrowseFile(HWND dialog, int id, const wchar_t* filter)
{
    wchar_t file[MAX_PATH] = {};
    std::wstring current = ControlText(dialog, id);
    wcsncpy_s(file, current.c_str(), _TRUNCATE);
    OPENFILENAMEW ofn = {};
    ofn.lStructSize = sizeof(ofn);
    ofn.hwndOwner = dialog;
    ofn.lpstrFilter = filter;
    ofn.lpstrFile = file;
    ofn.nMaxFile = MAX_PATH;
    ofn.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_HIDEREADONLY;
    if (GetOpenFileNameW(&ofn))
    {
        SetDlgItemTextW(dialog, id, file);
    }
}

void BrowseFolder(HWND dialog, int id)
{
    BROWSEINFOW info = {};
    info.hwndOwner = dialog;
    info.lpszTitle = L"The folder the client's log files go in";
    info.ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE;
    PIDLIST_ABSOLUTE chosen = SHBrowseForFolderW(&info);
    if (chosen != nullptr)
    {
        wchar_t path[MAX_PATH] = {};
        if (SHGetPathFromIDListW(chosen, path))
        {
            SetDlgItemTextW(dialog, id, path);
        }

        CoTaskMemFree(chosen);
    }
}

INT_PTR CALLBACK ConfigDialogProc(HWND dialog, UINT message, WPARAM wParam, LPARAM lParam)
{
    switch (message)
    {
    case WM_INITDIALOG:
    {
        auto* state = reinterpret_cast<DialogState*>(lParam);
        SetWindowLongPtrW(dialog, GWLP_USERDATA, lParam);
        HWND combo = GetDlgItem(dialog, IDC_ADDIN);
        std::vector<std::wstring> clients = KnownClients();
        for (const std::wstring& c : clients)
        {
            ComboBox_AddString(combo, c.c_str());
        }

        std::wstring first = state != nullptr && !state->client.empty() ? state->client : clients.empty() ? std::wstring() : clients.front();
        SetWindowTextW(combo, first.c_str());
        ShowClient(dialog, first);
        HWND levels = GetDlgItem(dialog, IDC_LOGLEVEL);
        ComboBox_AddString(levels, L"Errors");
        ComboBox_AddString(levels, L"Errors and information");
        ComboBox_AddString(levels, L"Everything");
        LogSettings log = ReadLog();
        CheckDlgButton(dialog, IDC_LOGENABLED, log.enabled ? BST_CHECKED : BST_UNCHECKED);
        ComboBox_SetCurSel(levels, log.level < 0 ? 0 : log.level > 2 ? 2 : log.level);
        SetDlgItemTextW(dialog, IDC_LOGFOLDER, log.folder.c_str());
        return TRUE;
    }

    case WM_COMMAND:
        switch (LOWORD(wParam))
        {
        case IDC_ADDIN:
            if (HIWORD(wParam) == CBN_SELCHANGE)
            {
                HWND combo = GetDlgItem(dialog, IDC_ADDIN);
                int at = ComboBox_GetCurSel(combo);
                if (at >= 0)
                {
                    wchar_t name[256] = {};
                    ComboBox_GetLBText(combo, at, name);
                    ShowClient(dialog, name);
                }
            }

            return TRUE;
        case IDC_BROWSE:
            BrowseFile(dialog, IDC_CERT, L"Certificates (*.pem;*.crt;*.cer)\0*.pem;*.crt;*.cer\0All files\0*.*\0");
            return TRUE;
        case IDC_LOGBROWSE:
            BrowseFolder(dialog, IDC_LOGFOLDER);
            return TRUE;
        case IDOK:
        {
            std::wstring client = Trim(ControlText(dialog, IDC_ADDIN));
            if (client.empty())
            {
                MessageBoxW(dialog, L"Name the Excel client to configure: the add-in's name, the stem of its DLL.", FriendlyName, MB_ICONINFORMATION);
                return TRUE;
            }

            ClientSettings s;
            s.url = Trim(ControlText(dialog, IDC_URL));
            if (!s.url.empty() && s.url.compare(0, 7, L"http://") != 0 && s.url.compare(0, 8, L"https://") != 0)
            {
                s.url = L"http://" + s.url;
            }

            s.timeout = Trim(ControlText(dialog, IDC_TIMEOUT));
            s.limit = Trim(ControlText(dialog, IDC_LIMIT));
            if (_wtoi(s.timeout.c_str()) <= 0 || _wtoi(s.limit.c_str()) <= 0)
            {
                MessageBoxW(dialog, L"The timeout and the response limit are whole numbers above zero.", FriendlyName, MB_ICONINFORMATION);
                return TRUE;
            }

            s.certificate = Trim(ControlText(dialog, IDC_CERT));
            LogSettings log;
            log.enabled = IsDlgButtonChecked(dialog, IDC_LOGENABLED) == BST_CHECKED;
            log.level = ComboBox_GetCurSel(GetDlgItem(dialog, IDC_LOGLEVEL));
            log.folder = Trim(ControlText(dialog, IDC_LOGFOLDER));
            std::wstring why;
            if (!WriteSettings(client, s, log, why))
            {
                MessageBoxW(dialog, why.c_str(), FriendlyName, MB_ICONERROR);
                return TRUE;
            }

            EndDialog(dialog, IDOK);
            return TRUE;
        }

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

INT_PTR ShowDialog(HWND owner, const std::wstring& client)
{
    DialogState state;
    state.client = client;
    return DialogBoxParamW(Self, MAKEINTRESOURCEW(IDD_CONFIG), owner, ConfigDialogProc, reinterpret_cast<LPARAM>(&state));
}

// Excel's window, from its Application object's Hwnd property, for the dialog's owner.
HWND OwnerOf(IDispatch* application)
{
    if (application == nullptr)
    {
        return nullptr;
    }

    wchar_t hwndName[] = L"Hwnd";
    LPOLESTR names[] = { hwndName };
    DISPID id = 0;
    if (FAILED(application->GetIDsOfNames(IID_NULL, names, 1, LOCALE_USER_DEFAULT, &id)))
    {
        return nullptr;
    }

    DISPPARAMS none = {};
    VARIANT value;
    VariantInit(&value);
    HWND owner = nullptr;
    if (SUCCEEDED(application->Invoke(id, IID_NULL, LOCALE_USER_DEFAULT, DISPATCH_PROPERTYGET, &none, &value, nullptr, nullptr)))
    {
        if (SUCCEEDED(VariantChangeType(&value, &value, 0, VT_I8)))
        {
            owner = reinterpret_cast<HWND>(static_cast<intptr_t>(value.llVal));
        }
    }

    VariantClear(&value);
    return owner;
}

// ---- The add-in object ------------------------------------------------------------------------------------

struct AddIn;

struct RibbonPart
{
    const void* const* vtbl;
};

struct AddIn
{
    const void* const* vtbl;   // IUnknown, IDispatch, IDTExtensibility2
    RibbonPart ribbon;         // IUnknown, IDispatch, IRibbonExtensibility
    LONG refs;
    IDispatch* application;

    static AddIn* From(void* self) { return static_cast<AddIn*>(self); }
    static AddIn* FromRibbon(void* self) { return reinterpret_cast<AddIn*>(static_cast<char*>(self) - offsetof(AddIn, ribbon)); }

    HRESULT Query(REFIID riid, void** ppv)
    {
        if (ppv == nullptr)
        {
            return E_POINTER;
        }

        *ppv = nullptr;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IDispatch) || IsEqualIID(riid, IID_IDTExtensibility2))
        {
            *ppv = this;
        }
        else if (IsEqualIID(riid, IID_IRibbonExtensibility))
        {
            *ppv = &ribbon;
        }
        else
        {
            return E_NOINTERFACE;
        }

        InterlockedIncrement(&refs);
        return S_OK;
    }

    ULONG Release()
    {
        LONG left = InterlockedDecrement(&refs);
        if (left == 0)
        {
            if (application != nullptr)
            {
                application->Release();
            }

            InterlockedDecrement(&Objects);
            delete this;
        }

        return static_cast<ULONG>(left);
    }

    // The ribbon's callbacks, which Excel reaches through IDispatch: OnConfigure(control).
    HRESULT Names(LPOLESTR* names, UINT count, DISPID* ids)
    {
        HRESULT hr = S_OK;
        for (UINT i = 0; i < count; i++)
        {
            if (i == 0 && _wcsicmp(names[i], L"OnConfigure") == 0)
            {
                ids[i] = ConfigureId;
            }
            else
            {
                ids[i] = DISPID_UNKNOWN;
                hr = DISP_E_UNKNOWNNAME;
            }
        }

        return hr;
    }

    HRESULT Invoke(DISPID member, WORD flags)
    {
        if (member != ConfigureId || (flags & DISPATCH_METHOD) == 0)
        {
            return DISP_E_MEMBERNOTFOUND;
        }

        ShowDialog(OwnerOf(application), std::wstring());
        return S_OK;
    }
};

HRESULT __stdcall AddInQueryInterface(void* self, REFIID riid, void** ppv) { return AddIn::From(self)->Query(riid, ppv); }
ULONG __stdcall AddInAddRef(void* self) { return static_cast<ULONG>(InterlockedIncrement(&AddIn::From(self)->refs)); }
ULONG __stdcall AddInRelease(void* self) { return AddIn::From(self)->Release(); }
HRESULT __stdcall AddInGetTypeInfoCount(void* self, UINT* count) { (void)self; if (count != nullptr) { *count = 0; } return S_OK; }
HRESULT __stdcall AddInGetTypeInfo(void* self, UINT index, LCID lcid, ITypeInfo** info) { (void)self; (void)index; (void)lcid; if (info != nullptr) { *info = nullptr; } return E_NOTIMPL; }
HRESULT __stdcall AddInGetIDsOfNames(void* self, REFIID riid, LPOLESTR* names, UINT count, LCID lcid, DISPID* ids) { (void)riid; (void)lcid; return AddIn::From(self)->Names(names, count, ids); }
HRESULT __stdcall AddInInvoke(void* self, DISPID member, REFIID riid, LCID lcid, WORD flags, DISPPARAMS* params, VARIANT* result, EXCEPINFO* excepInfo, UINT* argErr)
{
    (void)riid; (void)lcid; (void)params; (void)result; (void)excepInfo; (void)argErr;
    return AddIn::From(self)->Invoke(member, flags);
}

// IDTExtensibility2
HRESULT __stdcall AddInOnConnection(void* self, IDispatch* application, int connectMode, IDispatch* addInInst, SAFEARRAY** custom)
{
    (void)connectMode; (void)addInInst; (void)custom;
    AddIn* addIn = AddIn::From(self);
    if (addIn->application != nullptr)
    {
        addIn->application->Release();
    }

    addIn->application = application;
    if (application != nullptr)
    {
        application->AddRef();
    }

    return S_OK;
}

HRESULT __stdcall AddInOnDisconnection(void* self, int removeMode, SAFEARRAY** custom)
{
    (void)removeMode; (void)custom;
    AddIn* addIn = AddIn::From(self);
    if (addIn->application != nullptr)
    {
        addIn->application->Release();
        addIn->application = nullptr;
    }

    return S_OK;
}

HRESULT __stdcall AddInOnAddInsUpdate(void* self, SAFEARRAY** custom) { (void)self; (void)custom; return S_OK; }
HRESULT __stdcall AddInOnStartupComplete(void* self, SAFEARRAY** custom) { (void)self; (void)custom; return S_OK; }
HRESULT __stdcall AddInOnBeginShutdown(void* self, SAFEARRAY** custom) { (void)self; (void)custom; return S_OK; }

const void* const AddInVtbl[] =
{
    reinterpret_cast<const void*>(&AddInQueryInterface),
    reinterpret_cast<const void*>(&AddInAddRef),
    reinterpret_cast<const void*>(&AddInRelease),
    reinterpret_cast<const void*>(&AddInGetTypeInfoCount),
    reinterpret_cast<const void*>(&AddInGetTypeInfo),
    reinterpret_cast<const void*>(&AddInGetIDsOfNames),
    reinterpret_cast<const void*>(&AddInInvoke),
    reinterpret_cast<const void*>(&AddInOnConnection),
    reinterpret_cast<const void*>(&AddInOnDisconnection),
    reinterpret_cast<const void*>(&AddInOnAddInsUpdate),
    reinterpret_cast<const void*>(&AddInOnStartupComplete),
    reinterpret_cast<const void*>(&AddInOnBeginShutdown),
};

// IRibbonExtensibility, a tear-off at a fixed offset.
HRESULT __stdcall RibbonQueryInterface(void* self, REFIID riid, void** ppv) { return AddIn::FromRibbon(self)->Query(riid, ppv); }
ULONG __stdcall RibbonAddRef(void* self) { return static_cast<ULONG>(InterlockedIncrement(&AddIn::FromRibbon(self)->refs)); }
ULONG __stdcall RibbonRelease(void* self) { return AddIn::FromRibbon(self)->Release(); }
HRESULT __stdcall RibbonGetTypeInfoCount(void* self, UINT* count) { (void)self; if (count != nullptr) { *count = 0; } return S_OK; }
HRESULT __stdcall RibbonGetTypeInfo(void* self, UINT index, LCID lcid, ITypeInfo** info) { (void)self; (void)index; (void)lcid; if (info != nullptr) { *info = nullptr; } return E_NOTIMPL; }
HRESULT __stdcall RibbonGetIDsOfNames(void* self, REFIID riid, LPOLESTR* names, UINT count, LCID lcid, DISPID* ids) { (void)riid; (void)lcid; return AddIn::FromRibbon(self)->Names(names, count, ids); }
HRESULT __stdcall RibbonInvoke(void* self, DISPID member, REFIID riid, LCID lcid, WORD flags, DISPPARAMS* params, VARIANT* result, EXCEPINFO* excepInfo, UINT* argErr)
{
    (void)riid; (void)lcid; (void)params; (void)result; (void)excepInfo; (void)argErr;
    return AddIn::FromRibbon(self)->Invoke(member, flags);
}

HRESULT __stdcall RibbonGetCustomUI(void* self, BSTR ribbonId, BSTR* xml)
{
    (void)self; (void)ribbonId;
    if (xml == nullptr)
    {
        return E_POINTER;
    }

    *xml = SysAllocString(RibbonXml);
    return S_OK;
}

const void* const RibbonVtbl[] =
{
    reinterpret_cast<const void*>(&RibbonQueryInterface),
    reinterpret_cast<const void*>(&RibbonAddRef),
    reinterpret_cast<const void*>(&RibbonRelease),
    reinterpret_cast<const void*>(&RibbonGetTypeInfoCount),
    reinterpret_cast<const void*>(&RibbonGetTypeInfo),
    reinterpret_cast<const void*>(&RibbonGetIDsOfNames),
    reinterpret_cast<const void*>(&RibbonInvoke),
    reinterpret_cast<const void*>(&RibbonGetCustomUI),
};

// ---- The class factory --------------------------------------------------------------------------------------

struct ClassFactory
{
    const void* const* vtbl;
    LONG refs;
};

HRESULT __stdcall FactoryQueryInterface(void* self, REFIID riid, void** ppv)
{
    if (ppv == nullptr)
    {
        return E_POINTER;
    }

    if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IClassFactory))
    {
        *ppv = self;
        InterlockedIncrement(&static_cast<ClassFactory*>(self)->refs);
        return S_OK;
    }

    *ppv = nullptr;
    return E_NOINTERFACE;
}

ULONG __stdcall FactoryAddRef(void* self) { return static_cast<ULONG>(InterlockedIncrement(&static_cast<ClassFactory*>(self)->refs)); }

ULONG __stdcall FactoryRelease(void* self)
{
    auto* factory = static_cast<ClassFactory*>(self);
    LONG left = InterlockedDecrement(&factory->refs);
    if (left == 0)
    {
        delete factory;
    }

    return static_cast<ULONG>(left);
}

HRESULT __stdcall FactoryCreateInstance(void* self, IUnknown* outer, REFIID riid, void** ppv)
{
    (void)self;
    if (ppv == nullptr)
    {
        return E_POINTER;
    }

    *ppv = nullptr;
    if (outer != nullptr)
    {
        return CLASS_E_NOAGGREGATION;
    }

    auto* addIn = new AddIn();
    addIn->vtbl = AddInVtbl;
    addIn->ribbon.vtbl = RibbonVtbl;
    addIn->refs = 1;
    addIn->application = nullptr;
    InterlockedIncrement(&Objects);
    HRESULT hr = addIn->Query(riid, ppv);
    addIn->Release();
    return hr;
}

HRESULT __stdcall FactoryLockServer(void* self, BOOL lock)
{
    (void)self;
    if (lock) InterlockedIncrement(&Locks); else InterlockedDecrement(&Locks);
    return S_OK;
}

const void* const FactoryVtbl[] =
{
    reinterpret_cast<const void*>(&FactoryQueryInterface),
    reinterpret_cast<const void*>(&FactoryAddRef),
    reinterpret_cast<const void*>(&FactoryRelease),
    reinterpret_cast<const void*>(&FactoryCreateInstance),
    reinterpret_cast<const void*>(&FactoryLockServer),
};

// ---- Registration ----------------------------------------------------------------------------------------------

LSTATUS WriteText(HKEY root, const std::wstring& key, const wchar_t* name, const std::wstring& value)
{
    HKEY made = nullptr;
    LSTATUS status = RegCreateKeyExW(root, key.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &made, nullptr);
    if (status != ERROR_SUCCESS)
    {
        return status;
    }

    status = RegSetValueExW(made, name, 0, REG_SZ, reinterpret_cast<const BYTE*>(value.c_str()), static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t)));
    RegCloseKey(made);
    return status;
}

LSTATUS WriteNumber(HKEY root, const std::wstring& key, const wchar_t* name, DWORD value)
{
    HKEY made = nullptr;
    LSTATUS status = RegCreateKeyExW(root, key.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &made, nullptr);
    if (status != ERROR_SUCCESS)
    {
        return status;
    }

    status = RegSetValueExW(made, name, 0, REG_DWORD, reinterpret_cast<const BYTE*>(&value), sizeof(value));
    RegCloseKey(made);
    return status;
}

LSTATUS Register(HKEY hive)
{
    std::wstring path = ModulePath();
    std::wstring clsid = L"Software\\Classes\\CLSID\\" + GuidText(AddInClsid);
    LSTATUS status = WriteText(hive, std::wstring(L"Software\\Classes\\") + ProgId, nullptr, FriendlyName);
    if (status != ERROR_SUCCESS)
    {
        return status;
    }

    WriteText(hive, std::wstring(L"Software\\Classes\\") + ProgId + L"\\CLSID", nullptr, GuidText(AddInClsid));
    WriteText(hive, clsid, nullptr, FriendlyName);
    WriteText(hive, clsid + L"\\InprocServer32", nullptr, path);
    WriteText(hive, clsid + L"\\InprocServer32", L"ThreadingModel", L"Apartment");
    WriteText(hive, clsid + L"\\ProgID", nullptr, ProgId);
    WriteText(hive, clsid + L"\\Programmable", nullptr, L"");
    WriteText(hive, OfficeAddInKey, L"FriendlyName", FriendlyName);
    WriteText(hive, OfficeAddInKey, L"Description", Description);
    return WriteNumber(hive, OfficeAddInKey, L"LoadBehavior", 3);
}

void Unregister(HKEY hive)
{
    RegDeleteTreeW(hive, (std::wstring(L"Software\\Classes\\") + ProgId).c_str());
    RegDeleteTreeW(hive, (L"Software\\Classes\\CLSID\\" + GuidText(AddInClsid)).c_str());
    RegDeleteTreeW(hive, OfficeAddInKey);
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
    if (!IsEqualCLSID(clsid, AddInClsid))
    {
        return CLASS_E_CLASSNOTAVAILABLE;
    }

    auto* factory = new ClassFactory();
    factory->vtbl = FactoryVtbl;
    factory->refs = 1;
    HRESULT hr = FactoryQueryInterface(factory, iid, ppv);
    FactoryRelease(factory);
    return hr;
}

extern "C" HRESULT __stdcall DllCanUnloadNow()
{
    return Objects == 0 && Locks == 0 ? S_OK : S_FALSE;
}

extern "C" HRESULT __stdcall DllRegisterServer()
{
    LSTATUS status = Register(HKEY_LOCAL_MACHINE);
    if (status == ERROR_ACCESS_DENIED)
    {
        status = Register(HKEY_CURRENT_USER);
    }

    return status == ERROR_SUCCESS ? S_OK : SELFREG_E_CLASS;
}

extern "C" HRESULT __stdcall DllUnregisterServer()
{
    Unregister(HKEY_CURRENT_USER);
    Unregister(HKEY_LOCAL_MACHINE);
    return S_OK;
}

// The dialog with no Excel: rundll32 jgxlconfig.dll,JgxlConfigDialog [client name].
extern "C" void __stdcall JgxlConfigDialogW(HWND owner, HINSTANCE instance, LPWSTR commandLine, int show)
{
    (void)instance; (void)show;
    ShowDialog(owner, commandLine == nullptr ? std::wstring() : Trim(commandLine));
}

extern "C" void __stdcall JgxlConfigDialog(HWND owner, HINSTANCE instance, LPSTR commandLine, int show)
{
    (void)instance; (void)show;
    std::wstring client;
    if (commandLine != nullptr && commandLine[0] != '\0')
    {
        int n = MultiByteToWideChar(CP_ACP, 0, commandLine, -1, nullptr, 0);
        client.resize(static_cast<size_t>(n > 0 ? n - 1 : 0));
        MultiByteToWideChar(CP_ACP, 0, commandLine, -1, client.data(), n);
    }

    ShowDialog(owner, Trim(client));
}
