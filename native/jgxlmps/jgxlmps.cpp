// jgxlmps: the template an Excel client for the Production Server is made of (compiler and SDK plan,
// stage K17f; decision KO9).
//
// mcc -W mpsxl copies this DLL to <addin>.dll and stamps it: resource 1 the type library JGraph
// writes for the client's functions (TYPELIB: a dual interface I<Class> with one method per
// function, HRESULT f([in] long nargout, [in, out] VARIANT* out..., [in] VARIANT in...), and no
// nargout for a function without outputs), the configuration text JGRAPH_EXCEL_CLIENT (RT_RCDATA:
// AddIn=, Class=, Version= and one Function=<name>:<switches> line per function) and the version
// block. R2025b generates a C# source over MathWorks' .NET client assembly and compiles it with the
// Framework's csc.exe; this DLL is the native counterpart, needing no runtime of any kind on the
// client machine: every worksheet function call is converted to MATLAB Production Server's protobuf
// request (k17_mps: the wire log of R2025b's client, matched byte for byte for the values Excel
// sends), posted over WinHTTP to <server URL>/<function> and the reply converted back into the
// caller's [in, out] VARIANTs - the outputs, or the error as text in the first of them, as R2025b's
// client answers (MATLABException:<id>, WebException:<status>, Exception:<message>).
//
// The object is generic, as jgrt's COM objects are (native\jgrt\src\com.cpp): a vtable of IUnknown,
// IDispatch and one thunk per further slot, so the dual interface the type library declares is
// served whole; IDispatch::Invoke is ITypeInfo::Invoke driving that same vtable. The thunks read
// their arguments as pointer-sized slots, which on x64 every argument of this interface is (a long,
// a VARIANT* and a VARIANT by value, passed as a pointer to the caller's copy).
//
// Where the server is: HKEY_CURRENT_USER\Software\JGraph\Excel Client\<addin> (then the machine's
// key), the values ServerURL (http://host:port/<archive>), timeoutSeconds, responseSizeLimit
// (megabytes) and certificateFile (a PEM the server's HTTPS certificate must equal); written by
// JGraph's server configuration add-in (jgxlconfig.dll) or the client's installer (jgxlsetup.exe),
// where R2025b's ServerConfig.dll keeps MathWorks' under Software\MathWorks\MATLAB Builder EX\2.5.
// Logging: the key's Logging subkey (Enabled, LoggingLevel 0 errors / 1 information / 2 everything,
// LogFileFolder), one line per event in <folder>\<addin>-<date>.log, as MathWorks' "Enable Client
// Logging" page describes R2025b's.
//
// DllRegisterServer writes the registration from the type library alone: the version-independent
// ProgID <addin>.<Class> alone (R2025b's regasm registers no versioned one), the CLSID with its
// InprocServer32, ProgID, TypeLib and JGraph's Excel component category, the type library; machine-
// wide when the caller may, else for the current user.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <oaidl.h>
#include <ocidl.h>
#include <olectl.h>
#include <winhttp.h>
#include <wincrypt.h>
#include <intrin.h>
#include <shlobj.h>

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <cwchar>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

namespace
{
const int FirstMemberSlot = 7;
const int SlotCount = 520;
const wchar_t* const ConfigurationResource = L"JGRAPH_EXCEL_CLIENT";
const wchar_t* const SettingsKey = L"Software\\JGraph\\Excel Client\\";
const wchar_t* const ErrorSource = L"JGraph Excel client";
const int DefaultTimeoutSeconds = 120;
const int DefaultResponseSizeLimitMb = 64;
const double DateBias = 693960.0;

// JGraph's component category for its Excel add-ins (decision KO10), the add-in template's.
const GUID CategoryId = { 0xC4E7A6D2, 0x5B0F, 0x4E3A, { 0x8D, 0x91, 0x7F, 0x2B, 0x6A, 0x1C, 0x9E, 0x35 } };
const wchar_t* const CategoryName = L"JGraph Excel Add-in Components";

HINSTANCE Self = nullptr;
LONG Objects = 0;
LONG Locks = 0;
std::mutex Gate;

// ---- Text helpers ----------------------------------------------------------------------------

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

std::string Narrow(const std::wstring& wide)
{
    if (wide.empty())
    {
        return std::string();
    }

    int n = WideCharToMultiByte(CP_UTF8, 0, wide.data(), static_cast<int>(wide.size()), nullptr, 0, nullptr, nullptr);
    std::string utf8(static_cast<size_t>(n), '\0');
    WideCharToMultiByte(CP_UTF8, 0, wide.data(), static_cast<int>(wide.size()), utf8.data(), n, nullptr, nullptr);
    return utf8;
}

std::wstring Trim(const std::wstring& text)
{
    size_t start = text.find_first_not_of(L" \t\r\n");
    size_t end = text.find_last_not_of(L" \t\r\n");
    return start == std::wstring::npos ? std::wstring() : text.substr(start, end - start + 1);
}

std::wstring GuidText(const GUID& guid)
{
    wchar_t text[64];
    StringFromGUID2(guid, text, 64);
    return text;
}

std::wstring NumberText(double value)
{
    if (std::isnan(value))
    {
        return L"NaN";
    }

    if (std::isinf(value))
    {
        return value > 0 ? L"Inf" : L"-Inf";
    }

    wchar_t buffer[64];
    swprintf(buffer, 64, L"%.15g", value);
    return buffer;
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
    if (GetFileSizeEx(file, &size) && size.QuadPart > 0 && size.QuadPart < (16 << 20))
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

// ---- The settings and the log ----------------------------------------------------------------

struct Settings
{
    std::wstring url;
    int timeoutSeconds = DefaultTimeoutSeconds;
    int responseSizeLimitMb = DefaultResponseSizeLimitMb;
    std::wstring certificateFile;
};

// The first hive holding the key: the user's, then the machine's (R2025b's getAddinRegistryKey).
HKEY OpenSettings(const std::wstring& subkey)
{
    for (HKEY hive : { HKEY_CURRENT_USER, HKEY_LOCAL_MACHINE })
    {
        HKEY key = nullptr;
        if (RegOpenKeyExW(hive, (std::wstring(SettingsKey) + subkey).c_str(), 0, KEY_READ, &key) == ERROR_SUCCESS)
        {
            return key;
        }
    }

    return nullptr;
}

std::wstring StringValue(HKEY key, const wchar_t* name)
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
    if (RegQueryValueExW(key, name, nullptr, nullptr, reinterpret_cast<BYTE*>(value.data()), &size) != ERROR_SUCCESS)
    {
        return std::wstring();
    }

    value.resize(wcslen(value.c_str()));
    return value;
}

bool IntValue(HKEY key, const wchar_t* name, int& value)
{
    DWORD type = 0;
    DWORD size = 0;
    if (key == nullptr || RegQueryValueExW(key, name, nullptr, &type, nullptr, &size) != ERROR_SUCCESS)
    {
        return false;
    }

    if (type == REG_DWORD)
    {
        DWORD raw = 0;
        size = sizeof(raw);
        RegQueryValueExW(key, name, nullptr, nullptr, reinterpret_cast<BYTE*>(&raw), &size);
        value = static_cast<int>(raw);
        return true;
    }

    std::wstring text = StringValue(key, name);
    if (text.empty())
    {
        return false;
    }

    wchar_t* end = nullptr;
    long parsed = wcstol(text.c_str(), &end, 10);
    if (end == text.c_str())
    {
        return false;
    }

    value = static_cast<int>(parsed);
    return true;
}

Settings ReadSettings(const std::wstring& addIn)
{
    Settings s;
    HKEY key = OpenSettings(addIn);
    if (key != nullptr)
    {
        s.url = Trim(StringValue(key, L"ServerURL"));
        // A ".ctf" suffix dropped and "http://" supplied, as R2025b's client reads its URL (getServerConfig).
        if (s.url.size() > 4 && s.url.compare(s.url.size() - 4, 4, L".ctf") == 0)
        {
            s.url.resize(s.url.size() - 4);
        }

        if (!s.url.empty() && s.url.compare(0, 7, L"http://") != 0 && s.url.compare(0, 8, L"https://") != 0)
        {
            s.url = L"http://" + s.url;
        }

        int value = 0;
        if (IntValue(key, L"timeoutSeconds", value) && value > 0)
        {
            s.timeoutSeconds = value;
        }

        if (IntValue(key, L"responseSizeLimit", value) && value > 0)
        {
            s.responseSizeLimitMb = value;
        }

        s.certificateFile = StringValue(key, L"certificateFile");
        RegCloseKey(key);
    }

    return s;
}

// The client's own log (Logging: Enabled, LoggingLevel, LogFileFolder), one line per event in the
// shape of R2025b's FileLogTraceListener: "<addin> Add-in  <level>  <id>  <message>  <utc>Z  <pid>".
struct Logger
{
    std::wstring addIn;
    bool enabled = false;
    int level = 0;
    std::wstring folder;

    void Read()
    {
        HKEY key = OpenSettings(L"Logging");
        enabled = false;
        if (key == nullptr)
        {
            return;
        }

        int value = 0;
        enabled = IntValue(key, L"Enabled", value) && value != 0;
        level = IntValue(key, L"LoggingLevel", value) ? value : 0;
        folder = StringValue(key, L"LogFileFolder");
        RegCloseKey(key);
        if (folder.empty())
        {
            wchar_t* local = nullptr;
            if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &local)) && local != nullptr)
            {
                folder = std::wstring(local) + L"\\JGraph\\Excel Client";
                CoTaskMemFree(local);
            }
        }
    }

    // Levels as R2025b's filter takes them: 0 errors alone, 1 information too, 2 everything.
    void Write(int severity, const wchar_t* levelName, int id, const std::wstring& message)
    {
        if (!enabled || folder.empty() || severity > level)
        {
            return;
        }

        SHCreateDirectoryExW(nullptr, folder.c_str(), nullptr);
        SYSTEMTIME now = {};
        GetSystemTime(&now);
        wchar_t stamp[64];
        swprintf(stamp, 64, L"%04u-%02u-%02u %02u:%02u:%02uZ", now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute, now.wSecond);
        wchar_t day[32];
        swprintf(day, 32, L"%04u-%02u-%02u", now.wYear, now.wMonth, now.wDay);
        std::wstring line = addIn + L" Add-in  " + levelName + L"  " + std::to_wstring(id) + L"  " + message + L"  " + stamp + L"  " + std::to_wstring(GetCurrentProcessId()) + L"\r\n";
        std::string bytes = Narrow(line);
        HANDLE file = CreateFileW((folder + L"\\" + addIn + L"-" + day + L".log").c_str(), FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file != INVALID_HANDLE_VALUE)
        {
            DWORD written = 0;
            WriteFile(file, bytes.data(), static_cast<DWORD>(bytes.size()), &written, nullptr);
            CloseHandle(file);
        }
    }

    void Verbose(const std::wstring& message) { Write(2, L"Verbose", 16, message); }
    void Information(const std::wstring& message) { Write(1, L"Information", 8, message); }
    void Critical(const std::wstring& message) { Write(0, L"Critical", 1, message); }
};

// ---- Protobuf ----------------------------------------------------------------------------------

// MATLAB_Array.MWType and the field numbers of MathWorks' schema (JGraph's JgsMpsProtobuf, stage K16).
enum MwType { TUint8 = 0, TInt8, TUint16, TInt16, TUint32, TInt32, TUint64, TInt64, TSingle, TDouble, TLogical, TChar, TStruct, TCell, TSparse, TObject, THandleRef, TArrayRef, TEnumeration, TString };
const int FType = 1, FDimension = 2, FMemoryLayout = 6, FIsComplex = 7, FUint8 = 16, FChar = 27, FCell = 28, FStruct = 29, FStringElements = 38;

struct Writer
{
    std::string bytes;

    void Varint(uint64_t value)
    {
        while (value >= 0x80)
        {
            bytes.push_back(static_cast<char>((value & 0x7F) | 0x80));
            value >>= 7;
        }

        bytes.push_back(static_cast<char>(value));
    }

    void Tag(int field, int wire) { Varint((static_cast<uint64_t>(field) << 3) | static_cast<uint64_t>(wire)); }
    void VarintField(int field, uint64_t value) { Tag(field, 0); Varint(value); }
    void Fixed32(uint32_t value) { bytes.append(reinterpret_cast<const char*>(&value), 4); }
    void Fixed64(uint64_t value) { bytes.append(reinterpret_cast<const char*>(&value), 8); }
    void Double(double value) { uint64_t bits; memcpy(&bits, &value, 8); Fixed64(bits); }
    void Float(float value) { uint32_t bits; memcpy(&bits, &value, 4); Fixed32(bits); }
    void Bytes(int field, const std::string& data) { Tag(field, 2); Varint(data.size()); bytes.append(data); }
    void Message(int field, const Writer& message) { Bytes(field, message.bytes); }
};

struct Reader
{
    const uint8_t* at;
    const uint8_t* end;
    bool failed = false;

    Reader(const std::string& data) : at(reinterpret_cast<const uint8_t*>(data.data())), end(at + data.size()) {}
    Reader(const uint8_t* start, size_t size) : at(start), end(start + size) {}

    bool AtEnd() const { return at >= end; }

    uint64_t Varint()
    {
        uint64_t value = 0;
        int shift = 0;
        while (at < end && shift < 64)
        {
            uint8_t b = *at++;
            value |= static_cast<uint64_t>(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return value;
            }

            shift += 7;
        }

        failed = true;
        return value;
    }

    bool Next(int& field, int& wire)
    {
        if (at >= end)
        {
            return false;
        }

        uint64_t tag = Varint();
        if (failed)
        {
            return false;
        }

        field = static_cast<int>(tag >> 3);
        wire = static_cast<int>(tag & 7);
        return true;
    }

    uint32_t Fixed32()
    {
        if (end - at < 4)
        {
            failed = true;
            at = end;
            return 0;
        }

        uint32_t value;
        memcpy(&value, at, 4);
        at += 4;
        return value;
    }

    uint64_t Fixed64()
    {
        if (end - at < 8)
        {
            failed = true;
            at = end;
            return 0;
        }

        uint64_t value;
        memcpy(&value, at, 8);
        at += 8;
        return value;
    }

    // A length-delimited field's bytes.
    Reader Bytes()
    {
        uint64_t size = Varint();
        if (failed || size > static_cast<uint64_t>(end - at))
        {
            failed = true;
            at = end;
            return Reader(end, 0);
        }

        Reader inner(at, static_cast<size_t>(size));
        at += size;
        return inner;
    }

    std::string Text()
    {
        Reader inner = Bytes();
        return std::string(reinterpret_cast<const char*>(inner.at), static_cast<size_t>(inner.end - inner.at));
    }

    void Skip(int wire)
    {
        switch (wire)
        {
        case 0: Varint(); break;
        case 1: Fixed64(); break;
        case 2: Bytes(); break;
        case 5: Fixed32(); break;
        default: failed = true; at = end; break;
        }
    }

    // Packed or single varints of one field.
    void Varints(int wire, std::vector<uint64_t>& into)
    {
        if (wire == 2)
        {
            Reader inner = Bytes();
            while (!inner.AtEnd() && !inner.failed)
            {
                into.push_back(inner.Varint());
            }
        }
        else
        {
            into.push_back(Varint());
        }
    }

    void Fixed32s(int wire, std::vector<uint32_t>& into)
    {
        if (wire == 2)
        {
            Reader inner = Bytes();
            while (!inner.AtEnd() && !inner.failed)
            {
                into.push_back(inner.Fixed32());
            }
        }
        else
        {
            into.push_back(Fixed32());
        }
    }

    void Fixed64s(int wire, std::vector<uint64_t>& into)
    {
        if (wire == 2)
        {
            Reader inner = Bytes();
            while (!inner.AtEnd() && !inner.failed)
            {
                into.push_back(inner.Fixed64());
            }
        }
        else
        {
            into.push_back(Fixed64());
        }
    }
};

// A value on its way to or from the server: what a MATLAB_Array holds that an Excel cell can take.
struct Value
{
    MwType type = TDouble;
    std::vector<int> dims = { 1, 1 };
    std::vector<double> numbers;          // numeric types and logical, column-major
    std::vector<double> imaginary;        // when complex
    std::wstring text;                    // char: the code units column-major; string: unused
    std::vector<std::wstring> strings;    // a string array's elements, column-major
    std::vector<Value> cells;             // a cell array's elements, column-major
    std::vector<std::wstring> fieldNames; // a struct's
    std::wstring className;               // an object's, for the message
    bool complex = false;
    bool unsupported = false;             // a type no cell can take (object, handle, sparse, ...)

    size_t Count() const
    {
        size_t n = 1;
        for (int d : dims)
        {
            n *= static_cast<size_t>(d);
        }

        return n;
    }
};

Value Scalar(MwType type, double number)
{
    Value v;
    v.type = type;
    v.numbers.push_back(number);
    return v;
}

Value Text(const std::wstring& text)
{
    Value v;
    v.type = TChar;
    v.text = text;
    v.dims = text.empty() ? std::vector<int>{ 0, 0 } : std::vector<int>{ 1, static_cast<int>(text.size()) };
    return v;
}

// ---- Writing a value as MATLAB_Array, as R2025b's .NET client writes it (k17_mps) ----------------

int PayloadField(MwType type)
{
    switch (type)
    {
    case TLogical: return 26;
    case TChar: return 27;
    case TCell: return 28;
    case TStruct: return 29;
    case TString: return 38;
    default: return FUint8 + static_cast<int>(type);
    }
}

Writer WriteArray(const Value& v);

Writer NumericPayload(const Value& v)
{
    Writer payload;
    auto pack = [&](int field, const std::vector<double>& values)
    {
        if (v.type == TUint8 || v.type == TInt8)
        {
            std::string bytes;
            for (double d : values)
            {
                bytes.push_back(static_cast<char>(static_cast<int64_t>(d)));
            }

            payload.Bytes(field, bytes);
            return;
        }

        Writer packed;
        for (double d : values)
        {
            switch (v.type)
            {
            case TUint16: case TUint64:
                packed.Varint(d >= 18446744073709551615.0 ? UINT64_MAX : d <= 0 ? 0 : static_cast<uint64_t>(d));
                break;
            case TInt16: case TInt64:
                packed.Varint(static_cast<uint64_t>(d >= 9223372036854775807.0 ? INT64_MAX : d <= -9223372036854775808.0 ? INT64_MIN : static_cast<int64_t>(d)));
                break;
            case TUint32:
                packed.Fixed32(static_cast<uint32_t>(d));
                break;
            case TInt32:
                packed.Fixed32(static_cast<uint32_t>(static_cast<int32_t>(d)));
                break;
            case TSingle:
                packed.Float(static_cast<float>(d));
                break;
            case TLogical:
                packed.bytes.push_back(static_cast<char>(d != 0 ? 1 : 0));
                break;
            default:
                packed.Double(d);
                break;
            }
        }

        payload.Bytes(field, packed.bytes);
    };
    pack(1, v.numbers);
    if (v.complex)
    {
        pack(2, v.imaginary);
    }

    return payload;
}

// type, the dimensions packed, then the payload: no memory layout and no isComplex, as the .NET
// client's requests omit them (k17_mps: request 3, "08 09 12 02 01 01 ca 01 ...").
Writer WriteArray(const Value& v)
{
    Writer array;
    array.VarintField(FType, static_cast<uint64_t>(v.type));
    Writer dims;
    for (int d : v.dims)
    {
        dims.Varint(static_cast<uint64_t>(d));
    }

    array.Bytes(FDimension, dims.bytes);
    if (v.complex)
    {
        array.VarintField(FIsComplex, 1);
    }

    Writer payload;
    switch (v.type)
    {
    case TChar:
    {
        Writer packed;
        for (wchar_t c : v.text)
        {
            packed.Varint(static_cast<uint16_t>(c));
        }

        payload.Bytes(1, packed.bytes);
        break;
    }

    case TCell:
        for (const Value& cell : v.cells)
        {
            payload.Message(1, WriteArray(cell));
        }

        break;
    case TStruct:
        for (const std::wstring& name : v.fieldNames)
        {
            payload.Bytes(1, Narrow(name));
        }

        for (const Value& cell : v.cells)
        {
            payload.Message(2, WriteArray(cell));
        }

        break;
    default:
        payload = NumericPayload(v);
        break;
    }

    array.Message(PayloadField(v.type), payload);
    return array;
}

// MATLAB_Params: nargout (1), then each rhs (2).
std::string WriteRequest(int nargout, const std::vector<Value>& inputs)
{
    Writer params;
    params.VarintField(1, static_cast<uint64_t>(nargout));
    for (const Value& v : inputs)
    {
        params.Message(2, WriteArray(v));
    }

    return params.bytes;
}

// ---- Reading a MATLAB_Array -------------------------------------------------------------------------

bool ReadArray(Reader& reader, Value& v);

void ReadNumbers(MwType type, Reader payload, std::vector<double>& real, std::vector<double>& imag)
{
    int field, wire;
    while (payload.Next(field, wire) && !payload.failed)
    {
        if (field != 1 && field != 2)
        {
            payload.Skip(wire);
            continue;
        }

        std::vector<double>& into = field == 1 ? real : imag;
        switch (type)
        {
        case TUint8: case TInt8:
        {
            Reader bytes = payload.Bytes();
            for (const uint8_t* p = bytes.at; p < bytes.end; p++)
            {
                into.push_back(type == TInt8 ? static_cast<double>(static_cast<int8_t>(*p)) : static_cast<double>(*p));
            }

            break;
        }

        case TUint16: case TInt16: case TUint64: case TInt64:
        {
            std::vector<uint64_t> values;
            payload.Varints(wire, values);
            for (uint64_t u : values)
            {
                into.push_back(type == TUint16 ? static_cast<double>(static_cast<uint16_t>(u))
                    : type == TInt16 ? static_cast<double>(static_cast<int16_t>(static_cast<int32_t>(u)))
                    : type == TUint64 ? static_cast<double>(u) : static_cast<double>(static_cast<int64_t>(u)));
            }

            break;
        }

        case TUint32: case TInt32: case TSingle:
        {
            std::vector<uint32_t> values;
            payload.Fixed32s(wire, values);
            for (uint32_t u : values)
            {
                if (type == TSingle)
                {
                    float f;
                    memcpy(&f, &u, 4);
                    into.push_back(f);
                }
                else
                {
                    into.push_back(type == TUint32 ? static_cast<double>(u) : static_cast<double>(static_cast<int32_t>(u)));
                }
            }

            break;
        }

        case TLogical:
        {
            std::vector<uint64_t> values;
            payload.Varints(wire, values);
            for (uint64_t u : values)
            {
                into.push_back(u != 0 ? 1.0 : 0.0);
            }

            break;
        }

        default:
        {
            std::vector<uint64_t> values;
            payload.Fixed64s(wire, values);
            for (uint64_t u : values)
            {
                double d;
                memcpy(&d, &u, 8);
                into.push_back(d);
            }

            break;
        }
        }
    }
}

// Row-major elements into column-major order.
template <typename T>
void ToColumnMajor(std::vector<T>& values, const std::vector<int>& dims)
{
    size_t count = 1;
    for (int d : dims)
    {
        count *= static_cast<size_t>(d);
    }

    if (values.size() < count || count < 2)
    {
        return;
    }

    std::vector<T> result(count);
    std::vector<size_t> index(dims.size());
    for (size_t k = 0; k < count; k++)
    {
        size_t rest = k;
        for (size_t d = dims.size(); d-- > 0;)
        {
            index[d] = rest % static_cast<size_t>(dims[d]);
            rest /= static_cast<size_t>(dims[d]);
        }

        size_t at = 0;
        size_t stride = 1;
        for (size_t d = 0; d < dims.size(); d++)
        {
            at += index[d] * stride;
            stride *= static_cast<size_t>(dims[d]);
        }

        result[at] = values[k];
    }

    values.swap(result);
}

bool ReadArray(Reader& reader, Value& v)
{
    int type = -1;
    std::vector<uint64_t> dims;
    bool rowMajor = false;
    int payloadField = 0;
    Reader payload(nullptr, 0);
    std::vector<std::string> sharedStrings;
    int field, wire;
    while (reader.Next(field, wire) && !reader.failed)
    {
        if (field == FType && wire == 0)
        {
            type = static_cast<int>(reader.Varint());
        }
        else if (field == FDimension)
        {
            reader.Varints(wire, dims);
        }
        else if (field == FMemoryLayout && wire == 0)
        {
            rowMajor = reader.Varint() == 1;
        }
        else if (field == FIsComplex && wire == 0)
        {
            v.complex = reader.Varint() != 0;
        }
        else if (((field >= FUint8 && field <= 30) || field == FStringElements) && wire == 2)
        {
            payloadField = field;
            payload = reader.Bytes();
        }
        else if (field == 5 && wire == 2)
        {
            // sharedData.stringTable, which names an enumeration's class; read for the message alone.
            Reader shared = reader.Bytes();
            int f, w;
            while (shared.Next(f, w) && !shared.failed)
            {
                if (f == 1 && w == 2)
                {
                    Reader table = shared.Bytes();
                    int tf, tw;
                    while (table.Next(tf, tw) && !table.failed)
                    {
                        if (tf == 1 && tw == 2)
                        {
                            sharedStrings.push_back(table.Text());
                        }
                        else
                        {
                            table.Skip(tw);
                        }
                    }
                }
                else
                {
                    shared.Skip(w);
                }
            }
        }
        else if (field == 36 && wire == 0)
        {
            uint64_t index = reader.Varint();
            v.className = index < sharedStrings.size() ? Widen(sharedStrings[static_cast<size_t>(index)]) : L"enumeration";
        }
        else if (field == 31 && wire == 0)
        {
            uint64_t index = reader.Varint();
            v.className = index < sharedStrings.size() ? Widen(sharedStrings[static_cast<size_t>(index)]) : L"object";
        }
        else
        {
            reader.Skip(wire);
        }
    }

    if (reader.failed)
    {
        return false;
    }

    if (type < 0)
    {
        type = payloadField == 0 ? TUint8 : payloadField == 27 ? TChar : payloadField == 28 ? TCell : payloadField == 29 ? TStruct : payloadField == 38 ? TString : payloadField - FUint8;
    }

    v.type = static_cast<MwType>(type);
    v.dims.clear();
    if (dims.empty())
    {
        v.dims = { 0, 0 };
    }
    else
    {
        for (uint64_t d : dims)
        {
            v.dims.push_back(static_cast<int>(d));
        }

        if (v.dims.size() == 1)
        {
            v.dims.push_back(1);
        }

        while (v.dims.size() > 2 && v.dims.back() == 1)
        {
            v.dims.pop_back();
        }
    }

    size_t count = v.Count();
    switch (v.type)
    {
    case TUint8: case TInt8: case TUint16: case TInt16: case TUint32: case TInt32: case TUint64: case TInt64: case TSingle: case TDouble: case TLogical:
        if (payloadField != 0)
        {
            ReadNumbers(v.type, payload, v.numbers, v.imaginary);
        }

        if (rowMajor)
        {
            ToColumnMajor(v.numbers, v.dims);
            ToColumnMajor(v.imaginary, v.dims);
        }

        v.complex = !v.imaginary.empty();
        return v.numbers.size() >= count;
    case TChar:
    {
        std::vector<uint64_t> units;
        if (payloadField != 0)
        {
            int f, w;
            while (payload.Next(f, w) && !payload.failed)
            {
                if (f == 1)
                {
                    payload.Varints(w, units);
                }
                else
                {
                    payload.Skip(w);
                }
            }
        }

        if (rowMajor)
        {
            ToColumnMajor(units, v.dims);
        }

        for (uint64_t u : units)
        {
            v.text.push_back(static_cast<wchar_t>(static_cast<uint16_t>(u)));
        }

        return v.text.size() >= count;
    }

    case TCell: case TStruct:
    {
        if (payloadField != 0)
        {
            int f, w;
            while (payload.Next(f, w) && !payload.failed)
            {
                if (v.type == TStruct && f == 1 && w == 2)
                {
                    v.fieldNames.push_back(Widen(payload.Text()));
                }
                else if ((v.type == TCell && f == 1 && w == 2) || (v.type == TStruct && f == 2 && w == 2))
                {
                    Reader element = payload.Bytes();
                    Value inner;
                    if (!ReadArray(element, inner))
                    {
                        return false;
                    }

                    v.cells.push_back(inner);
                }
                else
                {
                    payload.Skip(w);
                }
            }
        }

        if (rowMajor && v.type == TCell)
        {
            ToColumnMajor(v.cells, v.dims);
        }

        return v.type == TStruct || v.cells.size() >= count;
    }

    case TString:
    {
        std::vector<uint64_t> units;
        std::vector<uint64_t> sizes;
        if (payloadField != 0)
        {
            int f, w;
            while (payload.Next(f, w) && !payload.failed)
            {
                if (f == 1)
                {
                    payload.Varints(w, units);
                }
                else if (f == 2)
                {
                    payload.Varints(w, sizes);
                }
                else
                {
                    payload.Skip(w);
                }
            }
        }

        size_t at = 0;
        for (size_t i = 0; i < sizes.size() && i < count; i++)
        {
            std::wstring s;
            for (uint64_t k = 0; k < sizes[i] && at < units.size(); k++)
            {
                s.push_back(static_cast<wchar_t>(static_cast<uint16_t>(units[at++])));
            }

            v.strings.push_back(s);
        }

        return v.strings.size() >= count;
    }

    default:
        // Named by its MWType, as R2025b's client names it ("unsupported type : object" for a datetime; k17_mps: make.datetime).
        v.unsupported = true;
        v.className = v.type == TSparse ? L"sparse" : v.type == TEnumeration ? L"enumeration" : L"object";
        return true;
    }
}

// MATLAB_Result: the outputs (1) or the error (2: message 1, identifier 2).
struct Reply
{
    std::vector<Value> outputs;
    bool isError = false;
    std::wstring message;
    std::wstring identifier;
    bool malformed = false;
};

Reply ReadReply(const std::string& body)
{
    Reply reply;
    Reader reader(body);
    int field, wire;
    while (reader.Next(field, wire) && !reader.failed)
    {
        if (field == 1 && wire == 2)
        {
            Reader element = reader.Bytes();
            Value v;
            if (!ReadArray(element, v))
            {
                reply.malformed = true;
                return reply;
            }

            reply.outputs.push_back(v);
        }
        else if (field == 2 && wire == 2)
        {
            reply.isError = true;
            Reader error = reader.Bytes();
            int f, w;
            while (error.Next(f, w) && !error.failed)
            {
                if (f == 1 && w == 2)
                {
                    reply.message = Widen(error.Text());
                }
                else if (f == 2 && w == 2)
                {
                    reply.identifier = Widen(error.Text());
                }
                else
                {
                    error.Skip(w);
                }
            }
        }
        else
        {
            reader.Skip(wire);
        }
    }

    reply.malformed = reader.failed;
    return reply;
}

// ---- The switches and the conversions from Excel ---------------------------------------------------

struct Switches
{
    bool blankToNaN = false;
    bool dateToString = false;
    bool nanToZero = false;
    bool numericToDate = false;
};

struct Conversion
{
    std::wstring failure; // what cannot be sent, in the words the output cell gets
};

std::wstring DateText(DATE date)
{
    BSTR text = nullptr;
    std::wstring result;
    if (SUCCEEDED(VarBstrFromDate(date, LOCALE_USER_DEFAULT, 0, &text)) && text != nullptr)
    {
        result.assign(text, SysStringLen(text));
    }

    SysFreeString(text);
    return result;
}

Value Blank(const Switches& s)
{
    return Scalar(TDouble, s.blankToNaN ? std::nan("") : 0.0);
}

bool FromVariant(const VARIANT& given, const Switches& s, Value& out, Conversion& c);

// A SAFEARRAY's dimensions as MATLAB's: one dimension a row, two as they are (the first varying fastest, as Excel's Value gives them).
std::vector<int> DimsOf(SAFEARRAY* array)
{
    std::vector<int> dims;
    if (array->cDims == 1)
    {
        dims = { 1, static_cast<int>(array->rgsabound[0].cElements) };
    }
    else
    {
        for (USHORT d = 0; d < array->cDims; d++)
        {
            dims.push_back(static_cast<int>(array->rgsabound[d].cElements));
        }
    }

    return dims;
}

// An array of Variants (a Range's Value, a VBA array): homogeneous numbers a double matrix, booleans a
// logical one, else a cell; blanks replaced first, as the .NET client's MWReplaceInputNullWithZero and
// MWCoerceObjectToPrimitiveArrayIfHomogeneous do it (k17_mps: echo.var2d, echo.arr_var_with_empty).
bool FromVariantArray(SAFEARRAY* array, const Switches& s, Value& out, Conversion& c)
{
    std::vector<int> dims = DimsOf(array);
    size_t count = 1;
    for (int d : dims)
    {
        count *= static_cast<size_t>(d);
    }

    VARIANT* data = nullptr;
    if (FAILED(SafeArrayAccessData(array, reinterpret_cast<void**>(&data))))
    {
        c.failure = L"Exception:An array argument could not be read.";
        return false;
    }

    std::vector<Value> elements;
    bool allNumbers = true;
    bool allBooleans = count > 0;
    for (size_t i = 0; i < count; i++)
    {
        Value e;
        if (!FromVariant(data[i], s, e, c))
        {
            SafeArrayUnaccessData(array);
            return false;
        }

        VARTYPE vt = data[i].vt;
        bool number = e.type == TDouble && e.dims.size() == 2 && e.dims[0] == 1 && e.dims[1] == 1 && !e.complex;
        allNumbers = allNumbers && number;
        allBooleans = allBooleans && vt == VT_BOOL;
        elements.push_back(e);
    }

    SafeArrayUnaccessData(array);
    out = Value();
    out.dims = dims;
    if (count == 1)
    {
        out = elements[0];
        return true;
    }

    if (allNumbers || allBooleans)
    {
        out.type = allBooleans ? TLogical : TDouble;
        for (const Value& e : elements)
        {
            out.numbers.push_back(e.numbers.empty() ? 0.0 : e.numbers[0]);
        }

        return true;
    }

    out.type = TCell;
    out.cells = elements;
    return true;
}

template <typename T>
void TypedElements(SAFEARRAY* array, std::vector<double>& into, double (*convert)(const T&))
{
    T* data = nullptr;
    if (FAILED(SafeArrayAccessData(array, reinterpret_cast<void**>(&data))))
    {
        return;
    }

    size_t count = 1;
    for (USHORT d = 0; d < array->cDims; d++)
    {
        count *= array->rgsabound[d].cElements;
    }

    for (size_t i = 0; i < count; i++)
    {
        into.push_back(convert(data[i]));
    }

    SafeArrayUnaccessData(array);
}

bool FromVariant(const VARIANT& given, const Switches& s, Value& out, Conversion& c)
{
    const VARIANT* v = &given;
    while (v->vt == (VT_BYREF | VT_VARIANT) && v->pvarVal != nullptr)
    {
        v = v->pvarVal;
    }

    VARTYPE vt = v->vt;
    if ((vt & VT_ARRAY) != 0)
    {
        SAFEARRAY* array = (vt & VT_BYREF) != 0 ? *v->pparray : v->parray;
        if (array == nullptr)
        {
            out = Blank(s);
            return true;
        }

        VARTYPE element = vt & VT_TYPEMASK;
        if (element == VT_VARIANT)
        {
            return FromVariantArray(array, s, out, c);
        }

        out = Value();
        out.dims = DimsOf(array);
        switch (element)
        {
        case VT_R8: out.type = TDouble; TypedElements<double>(array, out.numbers, [](const double& d) { return d; }); return true;
        case VT_R4: out.type = TSingle; TypedElements<float>(array, out.numbers, [](const float& d) { return static_cast<double>(d); }); return true;
        case VT_I4: case VT_INT: out.type = TInt32; TypedElements<int32_t>(array, out.numbers, [](const int32_t& d) { return static_cast<double>(d); }); return true;
        case VT_I2: out.type = TInt16; TypedElements<int16_t>(array, out.numbers, [](const int16_t& d) { return static_cast<double>(d); }); return true;
        case VT_UI1: out.type = TUint8; TypedElements<uint8_t>(array, out.numbers, [](const uint8_t& d) { return static_cast<double>(d); }); return true;
        case VT_BOOL: out.type = TLogical; TypedElements<VARIANT_BOOL>(array, out.numbers, [](const VARIANT_BOOL& d) { return d != 0 ? 1.0 : 0.0; }); return true;
        case VT_DATE:
        {
            std::vector<double> dates;
            TypedElements<DATE>(array, dates, [](const DATE& d) { return static_cast<double>(d); });
            if (s.dateToString)
            {
                out.type = TCell;
                for (double d : dates)
                {
                    out.cells.push_back(Text(DateText(d)));
                }
            }
            else
            {
                out.type = TDouble;
                for (double d : dates)
                {
                    out.numbers.push_back(d + DateBias);
                }
            }

            return true;
        }

        case VT_BSTR:
        {
            BSTR* data = nullptr;
            out.type = TCell;
            if (SUCCEEDED(SafeArrayAccessData(array, reinterpret_cast<void**>(&data))))
            {
                for (size_t i = 0; i < out.Count(); i++)
                {
                    out.cells.push_back(Text(data[i] == nullptr ? std::wstring() : std::wstring(data[i], SysStringLen(data[i]))));
                }

                SafeArrayUnaccessData(array);
            }

            return true;
        }

        default:
            c.failure = L"Exception:An array of this type cannot be sent to the server.";
            return false;
        }
    }

    switch (vt)
    {
    case VT_EMPTY: case VT_NULL:
        out = Blank(s);
        return true;
    case VT_R8: out = Scalar(TDouble, v->dblVal); return true;
    case VT_R4: out = Scalar(TSingle, v->fltVal); return true;
    case VT_I4: case VT_INT: out = Scalar(TInt32, v->lVal); return true;
    case VT_I2: out = Scalar(TInt16, v->iVal); return true;
    case VT_I1: out = Scalar(TInt8, v->cVal); return true;
    case VT_UI1: out = Scalar(TUint8, v->bVal); return true;
    case VT_UI2: out = Scalar(TUint16, v->uiVal); return true;
    case VT_UI4: case VT_UINT: out = Scalar(TUint32, v->ulVal); return true;
    case VT_I8: out = Scalar(TInt64, static_cast<double>(v->llVal)); return true;
    case VT_UI8: out = Scalar(TUint64, static_cast<double>(v->ullVal)); return true;
    case VT_BOOL: out = Scalar(TLogical, v->boolVal != 0 ? 1.0 : 0.0); return true;
    case VT_CY:
    {
        double d = 0;
        VarR8FromCy(v->cyVal, &d);
        out = Scalar(TDouble, d);
        return true;
    }

    case VT_DECIMAL:
    {
        double d = 0;
        DECIMAL dec = v->decVal;
        VarR8FromDec(&dec, &d);
        out = Scalar(TDouble, d);
        return true;
    }

    case VT_DATE:
        out = s.dateToString ? Text(DateText(v->date)) : Scalar(TDouble, v->date + DateBias);
        return true;
    case VT_BSTR:
        out = Text(v->bstrVal == nullptr ? std::wstring() : std::wstring(v->bstrVal, SysStringLen(v->bstrVal)));
        return true;
    case VT_ERROR:
        // An Excel error value (#N/A, #VALUE!): the .NET client refuses the whole call ("Data of following
        // type cannot be marshalled into MATLAB : ... ErrorWrapper"); so does this one, in its own words.
        c.failure = L"Exception:An Excel error value cannot be sent to the server.";
        return false;
    case VT_DISPATCH: case VT_UNKNOWN:
    {
        // A Range: its Value, as the .NET client reads range.Value (a DATE for a date cell, which the
        // Value2 form would give as a number).
        IDispatch* dispatch = nullptr;
        if (vt == VT_DISPATCH)
        {
            dispatch = v->pdispVal;
            if (dispatch != nullptr)
            {
                dispatch->AddRef();
            }
        }
        else if (v->punkVal != nullptr)
        {
            v->punkVal->QueryInterface(IID_IDispatch, reinterpret_cast<void**>(&dispatch));
        }

        if (dispatch == nullptr)
        {
            out = Blank(s);
            return true;
        }

        wchar_t valueName[] = L"Value";
        LPOLESTR names[] = { valueName };
        DISPID id = 0;
        HRESULT hr = dispatch->GetIDsOfNames(IID_NULL, names, 1, LOCALE_USER_DEFAULT, &id);
        VARIANT value;
        VariantInit(&value);
        if (SUCCEEDED(hr))
        {
            DISPPARAMS none = {};
            hr = dispatch->Invoke(id, IID_NULL, LOCALE_USER_DEFAULT, DISPATCH_PROPERTYGET, &none, &value, nullptr, nullptr);
        }

        dispatch->Release();
        if (FAILED(hr))
        {
            c.failure = L"Exception:An object argument has no Value to send to the server.";
            return false;
        }

        bool ok = FromVariant(value, s, out, c);
        VariantClear(&value);
        return ok;
    }

    default:
        c.failure = L"Exception:An argument of this type cannot be sent to the server.";
        return false;
    }
}

// ---- The conversions to Excel ------------------------------------------------------------------------

VARTYPE VariantTypeOf(MwType type)
{
    switch (type)
    {
    case TUint8: return VT_UI1;
    case TInt8: return VT_I1;
    case TUint16: return VT_UI2;
    case TInt16: return VT_I2;
    case TUint32: return VT_UI4;
    case TInt32: return VT_I4;
    case TUint64: return VT_UI8;
    case TInt64: return VT_I8;
    case TSingle: return VT_R4;
    case TLogical: return VT_BOOL;
    default: return VT_R8;
    }
}

void SetNumber(VARIANT& target, MwType type, double d, const Switches& s)
{
    VariantClear(&target);
    if (type == TDouble && std::isnan(d) && s.nanToZero)
    {
        d = 0;
    }

    // Under NumericToDate a double is a DATE when the value less the bias is one OLE holds (the add-in's
    // OutputAsDate rule, stage K17d); any other stays the number.
    if (type == TDouble && s.numericToDate && !std::isnan(d) && d - DateBias >= -657434.0 && d - DateBias < 2958466.0)
    {
        target.vt = VT_DATE;
        target.date = d - DateBias;
        return;
    }

    target.vt = VariantTypeOf(type);
    switch (type)
    {
    case TUint8: target.bVal = static_cast<BYTE>(d); break;
    case TInt8: target.cVal = static_cast<CHAR>(static_cast<int>(d)); break;
    case TUint16: target.uiVal = static_cast<USHORT>(d); break;
    case TInt16: target.iVal = static_cast<SHORT>(d); break;
    case TUint32: target.ulVal = static_cast<ULONG>(d); break;
    case TInt32: target.lVal = static_cast<LONG>(d); break;
    case TUint64: target.ullVal = static_cast<ULONGLONG>(d); break;
    case TInt64: target.llVal = static_cast<LONGLONG>(d); break;
    case TSingle: target.fltVal = static_cast<float>(d); break;
    case TLogical: target.boolVal = d != 0 ? VARIANT_TRUE : VARIANT_FALSE; break;
    default: target.dblVal = d; break;
    }
}

// A two-dimensional, zero-based SAFEARRAY, as a .NET array marshals (k17_mps: make.doublemat [0..1,0..1]).
SAFEARRAY* NewArray(VARTYPE vt, const std::vector<int>& dims)
{
    SAFEARRAYBOUND bounds[2];
    bounds[0].lLbound = 0;
    bounds[0].cElements = static_cast<ULONG>(dims[0]);
    bounds[1].lLbound = 0;
    size_t rest = 1;
    for (size_t d = 1; d < dims.size(); d++)
    {
        rest *= static_cast<size_t>(dims[d]);
    }

    bounds[1].cElements = static_cast<ULONG>(rest);
    return SafeArrayCreate(vt, 2, bounds);
}

bool ToVariant(const Value& v, const Switches& s, VARIANT& target, std::wstring& failure);

// A char matrix: one string per row (where the .NET client flattens the rows column-major into one text, k17_mps: make.charmat 'acbd').
void CharRows(const Value& v, std::vector<std::wstring>& rows)
{
    int height = v.dims[0];
    int width = v.dims.size() > 1 ? v.dims[1] : 1;
    rows.assign(static_cast<size_t>(height), std::wstring());
    for (int col = 0; col < width; col++)
    {
        for (int row = 0; row < height; row++)
        {
            size_t at = static_cast<size_t>(col) * static_cast<size_t>(height) + static_cast<size_t>(row);
            if (at < v.text.size())
            {
                rows[static_cast<size_t>(row)].push_back(v.text[at]);
            }
        }
    }
}

bool ToVariant(const Value& v, const Switches& s, VARIANT& target, std::wstring& failure)
{
    VariantClear(&target);
    size_t count = v.Count();
    if (v.unsupported)
    {
        failure = L"Exception:MATLAB response contained unsupported type : " + v.className;
        return false;
    }

    switch (v.type)
    {
    case TStruct:
    {
        std::wstring names;
        for (size_t i = 0; i < v.fieldNames.size(); i++)
        {
            names += (i > 0 ? L"," : L"") + v.fieldNames[i];
        }

        failure = L"Exception:A MATLAB structure with fields [" + names + L"] cannot be shown in a cell";
        return false;
    }

    case TChar:
        if (v.dims.size() == 2 && v.dims[0] > 1)
        {
            std::vector<std::wstring> rows;
            CharRows(v, rows);
            SAFEARRAY* array = NewArray(VT_BSTR, { v.dims[0], 1 });
            BSTR* data = nullptr;
            SafeArrayAccessData(array, reinterpret_cast<void**>(&data));
            for (size_t i = 0; i < rows.size(); i++)
            {
                data[i] = SysAllocStringLen(rows[i].data(), static_cast<UINT>(rows[i].size()));
            }

            SafeArrayUnaccessData(array);
            target.vt = VT_ARRAY | VT_BSTR;
            target.parray = array;
            return true;
        }

        target.vt = VT_BSTR;
        target.bstrVal = SysAllocStringLen(v.text.data(), static_cast<UINT>(v.text.size()));
        return true;
    case TString:
        if (count == 1)
        {
            target.vt = VT_BSTR;
            target.bstrVal = SysAllocStringLen(v.strings[0].data(), static_cast<UINT>(v.strings[0].size()));
            return true;
        }
        else
        {
            SAFEARRAY* array = NewArray(VT_BSTR, v.dims);
            BSTR* data = nullptr;
            SafeArrayAccessData(array, reinterpret_cast<void**>(&data));
            for (size_t i = 0; i < count && i < v.strings.size(); i++)
            {
                data[i] = SysAllocStringLen(v.strings[i].data(), static_cast<UINT>(v.strings[i].size()));
            }

            SafeArrayUnaccessData(array);
            target.vt = VT_ARRAY | VT_BSTR;
            target.parray = array;
            return true;
        }

    case TCell:
    {
        SAFEARRAY* array = NewArray(VT_VARIANT, v.dims);
        VARIANT* data = nullptr;
        SafeArrayAccessData(array, reinterpret_cast<void**>(&data));
        bool ok = true;
        for (size_t i = 0; i < count && i < v.cells.size() && ok; i++)
        {
            ok = ToVariant(v.cells[i], s, data[i], failure);
        }

        SafeArrayUnaccessData(array);
        if (!ok)
        {
            SafeArrayDestroy(array);
            return false;
        }

        target.vt = VT_ARRAY | VT_VARIANT;
        target.parray = array;
        return true;
    }

    default:
        break;
    }

    if (v.complex)
    {
        failure = L"Exception:Complex data received from MATLAB is not supported";
        return false;
    }

    // Numbers and logicals: an empty answers Empty (where the .NET client fails on a null reference);
    // a scalar its value; an array a two-dimensional one of its type.
    if (count == 0)
    {
        return true;
    }

    if (count == 1 && v.dims.size() == 2 && v.dims[0] == 1 && v.dims[1] == 1)
    {
        SetNumber(target, v.type, v.numbers[0], s);
        return true;
    }

    // Under NumericToDate a double array answers Variants, each a date or the number; the others their own type.
    VARTYPE vt = v.type == TDouble && s.numericToDate ? VT_VARIANT : VariantTypeOf(v.type);
    SAFEARRAY* array = NewArray(vt, v.dims);
    if (array == nullptr)
    {
        failure = L"Exception:The result is too large for a cell range.";
        return false;
    }

    void* data = nullptr;
    SafeArrayAccessData(array, &data);
    for (size_t i = 0; i < count && i < v.numbers.size(); i++)
    {
        VARIANT one;
        VariantInit(&one);
        SetNumber(one, v.type, v.numbers[i], s);
        switch (vt)
        {
        case VT_VARIANT: static_cast<VARIANT*>(data)[i] = one; break;
        case VT_R8: static_cast<double*>(data)[i] = one.dblVal; break;
        case VT_R4: static_cast<float*>(data)[i] = one.fltVal; break;
        case VT_BOOL: static_cast<VARIANT_BOOL*>(data)[i] = one.boolVal; break;
        case VT_UI1: static_cast<BYTE*>(data)[i] = one.bVal; break;
        case VT_I1: static_cast<CHAR*>(data)[i] = one.cVal; break;
        case VT_UI2: static_cast<USHORT*>(data)[i] = one.uiVal; break;
        case VT_I2: static_cast<SHORT*>(data)[i] = one.iVal; break;
        case VT_UI4: static_cast<ULONG*>(data)[i] = one.ulVal; break;
        case VT_I4: static_cast<LONG*>(data)[i] = one.lVal; break;
        case VT_UI8: static_cast<ULONGLONG*>(data)[i] = one.ullVal; break;
        case VT_I8: static_cast<LONGLONG*>(data)[i] = one.llVal; break;
        default: break;
        }
    }

    SafeArrayUnaccessData(array);
    target.vt = VT_ARRAY | vt;
    target.parray = array;
    return true;
}

// ---- The call to the server ------------------------------------------------------------------------

struct HttpResult
{
    bool sent = false;             // false: no response (connect, resolve, timeout); `failure` names it as .NET's WebExceptionStatus does
    std::wstring failure;
    DWORD status = 0;
    std::wstring reason;
    std::string body;
};

// The failure's name in the words of .NET's WebExceptionStatus, which R2025b's client answers after "WebException:".
std::wstring WinHttpFailure(DWORD error)
{
    switch (error)
    {
    case ERROR_WINHTTP_TIMEOUT: return L"Timeout";
    case ERROR_WINHTTP_NAME_NOT_RESOLVED: return L"NameResolutionFailure";
    case ERROR_WINHTTP_CANNOT_CONNECT: return L"ConnectFailure";
    case ERROR_WINHTTP_CONNECTION_ERROR: return L"ConnectionClosed";
    case ERROR_WINHTTP_SECURE_FAILURE: return L"TrustFailure";
    case ERROR_WINHTTP_INVALID_URL: return L"RequestProhibitedByProxy";
    default: return L"UnknownError (" + std::to_wstring(error) + L")";
    }
}

// The DER bytes of the one certificate in a PEM file (or the file's bytes when it is DER already).
std::string CertificateDer(const std::wstring& path)
{
    std::string text = ReadFileBytes(path);
    if (text.empty())
    {
        return std::string();
    }

    size_t begin = text.find("-----BEGIN CERTIFICATE-----");
    if (begin == std::string::npos)
    {
        return text;
    }

    size_t start = text.find('\n', begin);
    size_t end = text.find("-----END CERTIFICATE-----", begin);
    if (start == std::string::npos || end == std::string::npos)
    {
        return std::string();
    }

    std::string base64 = text.substr(start, end - start);
    DWORD size = 0;
    if (!CryptStringToBinaryA(base64.c_str(), static_cast<DWORD>(base64.size()), CRYPT_STRING_BASE64, nullptr, &size, nullptr, nullptr))
    {
        return std::string();
    }

    std::string der(size, '\0');
    CryptStringToBinaryA(base64.c_str(), static_cast<DWORD>(base64.size()), CRYPT_STRING_BASE64, reinterpret_cast<BYTE*>(der.data()), &size, nullptr, nullptr);
    der.resize(size);
    return der;
}

HttpResult Post(const std::wstring& url, const std::string& body, const Settings& settings, const std::wstring& userAgent)
{
    HttpResult result;
    URL_COMPONENTS parts = {};
    parts.dwStructSize = sizeof(parts);
    wchar_t host[256] = {};
    wchar_t path[4096] = {};
    parts.lpszHostName = host;
    parts.dwHostNameLength = 256;
    parts.lpszUrlPath = path;
    parts.dwUrlPathLength = 4096;
    if (!WinHttpCrackUrl(url.c_str(), static_cast<DWORD>(url.size()), 0, &parts))
    {
        result.failure = L"RequestProhibitedByProxy";
        return result;
    }

    bool secure = parts.nScheme == INTERNET_SCHEME_HTTPS;
    HINTERNET session = WinHttpOpen(userAgent.c_str(), WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY, WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
    if (session == nullptr)
    {
        result.failure = WinHttpFailure(GetLastError());
        return result;
    }

    int timeout = settings.timeoutSeconds * 1000;
    WinHttpSetTimeouts(session, timeout, timeout, timeout, timeout);
    HINTERNET connection = WinHttpConnect(session, host, parts.nPort, 0);
    HINTERNET request = connection == nullptr ? nullptr
        : WinHttpOpenRequest(connection, L"POST", path, nullptr, WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, secure ? WINHTTP_FLAG_SECURE : 0);
    std::string pinned = secure && !settings.certificateFile.empty() ? CertificateDer(settings.certificateFile) : std::string();
    if (request != nullptr && !pinned.empty())
    {
        // A configured certificate is trusted by itself: the chain's authority is not asked about, and
        // the server's certificate is compared with it before the request's body goes out.
        DWORD flags = SECURITY_FLAG_IGNORE_UNKNOWN_CA | SECURITY_FLAG_IGNORE_CERT_CN_INVALID | SECURITY_FLAG_IGNORE_CERT_DATE_INVALID;
        WinHttpSetOption(request, WINHTTP_OPTION_SECURITY_FLAGS, &flags, sizeof(flags));
    }

    bool ok = request != nullptr
        && WinHttpSendRequest(request, L"Content-Type: application/x-google-protobuf\r\n", static_cast<DWORD>(-1), WINHTTP_NO_REQUEST_DATA, 0, static_cast<DWORD>(body.size()), 0);
    if (ok && !pinned.empty())
    {
        PCCERT_CONTEXT served = nullptr;
        DWORD size = sizeof(served);
        bool same = false;
        if (WinHttpQueryOption(request, WINHTTP_OPTION_SERVER_CERT_CONTEXT, &served, &size) && served != nullptr)
        {
            same = served->cbCertEncoded == pinned.size() && memcmp(served->pbCertEncoded, pinned.data(), pinned.size()) == 0;
            CertFreeCertificateContext(served);
        }

        if (!same)
        {
            result.failure = L"TrustFailure";
            WinHttpCloseHandle(request);
            WinHttpCloseHandle(connection);
            WinHttpCloseHandle(session);
            return result;
        }
    }

    if (ok && !body.empty())
    {
        DWORD written = 0;
        ok = WinHttpWriteData(request, body.data(), static_cast<DWORD>(body.size()), &written) && written == body.size();
    }

    ok = ok && WinHttpReceiveResponse(request, nullptr);
    if (!ok)
    {
        result.failure = WinHttpFailure(GetLastError());
    }
    else
    {
        result.sent = true;
        DWORD status = 0;
        DWORD size = sizeof(status);
        WinHttpQueryHeaders(request, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER, WINHTTP_HEADER_NAME_BY_INDEX, &status, &size, WINHTTP_NO_HEADER_INDEX);
        result.status = status;
        wchar_t reason[256] = {};
        size = sizeof(reason);
        if (WinHttpQueryHeaders(request, WINHTTP_QUERY_STATUS_TEXT, WINHTTP_HEADER_NAME_BY_INDEX, reason, &size, WINHTTP_NO_HEADER_INDEX))
        {
            result.reason = reason;
        }

        size_t limit = static_cast<size_t>(settings.responseSizeLimitMb) * 1024 * 1024;
        for (;;)
        {
            DWORD available = 0;
            if (!WinHttpQueryDataAvailable(request, &available) || available == 0)
            {
                break;
            }

            if (result.body.size() + available > limit)
            {
                result.sent = false;
                result.failure = L"ResponseSizeLimit";
                break;
            }

            size_t at = result.body.size();
            result.body.resize(at + available);
            DWORD read = 0;
            if (!WinHttpReadData(request, result.body.data() + at, available, &read))
            {
                break;
            }

            result.body.resize(at + read);
        }
    }

    if (request != nullptr)
    {
        WinHttpCloseHandle(request);
    }

    if (connection != nullptr)
    {
        WinHttpCloseHandle(connection);
    }

    WinHttpCloseHandle(session);
    return result;
}

// ---- The module: the type library, the configuration, the members by slot ------------------------------

struct Param
{
    VARTYPE vt = VT_EMPTY;
    bool byRef = false;
};

struct Member
{
    std::wstring name;
    std::vector<Param> params;
    int outputs = 0; // the [in, out] VARIANT* slots
};

struct Module
{
    std::wstring path;
    std::wstring addIn;
    std::wstring className;
    std::wstring version;
    std::wstring libid;
    GUID libidGuid = {};
    WORD major = 1;
    WORD minor = 0;
    CLSID clsid = {};
    IID iid = {};
    std::wstring progId;
    ITypeLib* lib = nullptr;
    ITypeInfo* info = nullptr;      // the interface's vtable view, which Invoke drives
    ITypeInfo* dispatch = nullptr;  // its dispatch view, which a client reads members from
    std::vector<Member> slots;
    std::map<std::wstring, Switches> switches;
    bool loaded = false;
    std::wstring complaint;
};

Module TheModule;

std::wstring NameOf(ITypeInfo* info, MEMBERID id)
{
    BSTR name = nullptr;
    std::wstring text;
    if (info != nullptr && SUCCEEDED(info->GetDocumentation(id, &name, nullptr, nullptr, nullptr)) && name != nullptr)
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
    if (info != nullptr && SUCCEEDED(info->GetDocumentation(MEMBERID_NIL, nullptr, &doc, nullptr, nullptr)) && doc != nullptr)
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

Switches SwitchesOf(const std::wstring& words)
{
    Switches s;
    size_t start = 0;
    while (start <= words.size())
    {
        size_t end = words.find(L',', start);
        if (end == std::wstring::npos)
        {
            end = words.size();
        }

        std::wstring word = Trim(words.substr(start, end - start));
        if (word == L"blank") s.blankToNaN = true;
        else if (word == L"date") s.dateToString = true;
        else if (word == L"nan") s.nanToZero = true;
        else if (word == L"numdate") s.numericToDate = true;
        start = end + 1;
    }

    return s;
}

// The stamped configuration: AddIn=, Class=, Version=, Function=<name>:<words>.
void ReadConfiguration(Module& m)
{
    std::wstring text = Widen(ResourceText(ConfigurationResource));
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
        if (equals == std::wstring::npos)
        {
            continue;
        }

        std::wstring key = line.substr(0, equals);
        std::wstring value = line.substr(equals + 1);
        if (key == L"AddIn") m.addIn = value;
        else if (key == L"Class") m.className = value;
        else if (key == L"Version") m.version = value;
        else if (key == L"Function")
        {
            size_t colon = value.find(L':');
            std::wstring name = colon == std::wstring::npos ? value : value.substr(0, colon);
            m.switches[name] = SwitchesOf(colon == std::wstring::npos ? std::wstring() : value.substr(colon + 1));
        }
    }
}

// What the DLL's type library says: the one coclass, its interface, the members by vtable slot.
bool Describe(Module& m)
{
    m.path = ModulePath();
    if (FAILED(LoadTypeLibEx(m.path.c_str(), REGKIND_NONE, &m.lib)) || m.lib == nullptr)
    {
        m.complaint = L"The client carries no type library.";
        return false;
    }

    BSTR name = nullptr;
    BSTR doc = nullptr;
    std::wstring library;
    if (SUCCEEDED(m.lib->GetDocumentation(-1, &name, &doc, nullptr, nullptr)) && name != nullptr)
    {
        library.assign(name, SysStringLen(name));
    }

    TLIBATTR* attr = nullptr;
    if (SUCCEEDED(m.lib->GetLibAttr(&attr)) && attr != nullptr)
    {
        m.libidGuid = attr->guid;
        m.libid = GuidText(attr->guid);
        m.major = attr->wMajorVerNum;
        m.minor = attr->wMinorVerNum;
        m.lib->ReleaseTLibAttr(attr);
    }

    wchar_t version[32];
    swprintf(version, 32, L"%u.%u", static_cast<unsigned>(m.major), static_cast<unsigned>(m.minor));
    if (doc != nullptr)
    {
        std::wstring text(doc, SysStringLen(doc));
        std::wstring tail = std::wstring(L" ") + version + L" Type Library";
        if (EndsWith(text, tail))
        {
            library = text.substr(0, text.size() - tail.size());
        }
    }

    SysFreeString(name);
    SysFreeString(doc);
    if (m.addIn.empty())
    {
        m.addIn = library;
    }

    UINT count = m.lib->GetTypeInfoCount();
    for (UINT k = 0; k < count && m.info == nullptr; k++)
    {
        ITypeInfo* info = nullptr;
        if (FAILED(m.lib->GetTypeInfo(k, &info)) || info == nullptr)
        {
            continue;
        }

        TYPEATTR* tattr = nullptr;
        if (SUCCEEDED(info->GetTypeAttr(&tattr)) && tattr != nullptr)
        {
            if (tattr->typekind == TKIND_COCLASS)
            {
                m.clsid = tattr->guid;
                std::wstring className = NameOf(info, MEMBERID_NIL);
                std::wstring classDoc = DocOf(info);
                if (EndsWith(classDoc, L" Class"))
                {
                    className = classDoc.substr(0, classDoc.size() - 6);
                }

                if (m.className.empty())
                {
                    m.className = className;
                }

                HREFTYPE href = 0;
                ITypeInfo* iface = nullptr;
                if (SUCCEEDED(info->GetRefTypeOfImplType(0, &href)) && SUCCEEDED(info->GetRefTypeInfo(href, &iface)) && iface != nullptr)
                {
                    TYPEATTR* iattr = nullptr;
                    if (SUCCEEDED(iface->GetTypeAttr(&iattr)) && iattr != nullptr)
                    {
                        m.iid = iattr->guid;
                        if (iattr->typekind == TKIND_DISPATCH)
                        {
                            HREFTYPE vhref = 0;
                            ITypeInfo* vtable = nullptr;
                            if (SUCCEEDED(iface->GetRefTypeOfImplType(static_cast<UINT>(-1), &vhref)) && SUCCEEDED(iface->GetRefTypeInfo(vhref, &vtable)) && vtable != nullptr)
                            {
                                m.dispatch = iface;
                                m.info = vtable;
                            }
                        }
                        else
                        {
                            m.info = iface;
                            HREFTYPE dhref = 0;
                            if ((iattr->wTypeFlags & TYPEFLAG_FDUAL) != 0 && SUCCEEDED(iface->GetRefTypeOfImplType(static_cast<UINT>(-1), &dhref)))
                            {
                                iface->GetRefTypeInfo(dhref, &m.dispatch);
                            }
                        }

                        iface->ReleaseTypeAttr(iattr);
                    }

                    if (m.info != iface && m.dispatch != iface)
                    {
                        iface->Release();
                    }
                }
            }

            info->ReleaseTypeAttr(tattr);
        }

        info->Release();
    }

    if (m.info == nullptr)
    {
        m.complaint = L"The client's type library declares no coclass with an interface.";
        return false;
    }

    m.progId = m.addIn + L"." + m.className;
    m.slots.resize(SlotCount);
    TYPEATTR* iattr = nullptr;
    if (SUCCEEDED(m.info->GetTypeAttr(&iattr)) && iattr != nullptr)
    {
        for (WORD f = 0; f < iattr->cFuncs; f++)
        {
            FUNCDESC* fd = nullptr;
            if (FAILED(m.info->GetFuncDesc(f, &fd)) || fd == nullptr)
            {
                continue;
            }

            int slot = static_cast<int>(fd->oVft / sizeof(void*));
            if (slot >= FirstMemberSlot && slot < SlotCount && fd->invkind == INVOKE_FUNC)
            {
                Member& member = m.slots[static_cast<size_t>(slot)];
                member.name = NameOf(m.info, fd->memid);
                for (SHORT p = 0; p < fd->cParams; p++)
                {
                    Param param;
                    const TYPEDESC* t = &fd->lprgelemdescParam[p].tdesc;
                    int pointers = 0;
                    while (t->vt == VT_PTR && t->lptdesc != nullptr)
                    {
                        t = t->lptdesc;
                        pointers++;
                    }

                    param.vt = t->vt;
                    param.byRef = pointers > 0;
                    if (param.byRef && param.vt == VT_VARIANT)
                    {
                        member.outputs++;
                    }

                    member.params.push_back(param);
                }
            }

            m.info->ReleaseFuncDesc(fd);
        }

        m.info->ReleaseTypeAttr(iattr);
    }

    return true;
}

bool LoadModule()
{
    std::lock_guard<std::mutex> hold(Gate);
    if (TheModule.loaded)
    {
        return TheModule.info != nullptr;
    }

    TheModule.loaded = true;
    ReadConfiguration(TheModule);
    return Describe(TheModule);
}

// ---- The object ----------------------------------------------------------------------------------------

struct ClientObject
{
    const void* const* vtbl;
    LONG refs;
    Logger log;

    static ClientObject* From(void* self) { return static_cast<ClientObject*>(self); }

    HRESULT Query(REFIID riid, void** ppv)
    {
        if (ppv == nullptr)
        {
            return E_POINTER;
        }

        *ppv = nullptr;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IDispatch) || IsEqualIID(riid, TheModule.iid))
        {
            *ppv = this;
            InterlockedIncrement(&refs);
            return S_OK;
        }

        return E_NOINTERFACE;
    }

    ULONG Release()
    {
        LONG left = InterlockedDecrement(&refs);
        if (left == 0)
        {
            InterlockedDecrement(&Objects);
            delete this;
        }

        return static_cast<ULONG>(left);
    }

    // The outputs of one call, or its failure as text in the first output (R2025b's client puts every
    // failure there: MATLABException:<identifier>, WebException:<status>, Exception:<message>).
    void Answer(const Member& m, const std::vector<VARIANT*>& outs, const std::wstring& text)
    {
        if (outs.empty())
        {
            return;
        }

        VariantClear(outs[0]);
        outs[0]->vt = VT_BSTR;
        outs[0]->bstrVal = SysAllocStringLen(text.data(), static_cast<UINT>(text.size()));
        log.Critical(L"Call to " + m.name + L" function failed.");
        log.Critical(text);
    }

    HRESULT CallSlot(int slot, intptr_t a1, intptr_t a2, intptr_t a3, const intptr_t* stack)
    {
        const Member& m = TheModule.slots[static_cast<size_t>(slot)];
        if (m.name.empty())
        {
            return E_NOTIMPL;
        }

        auto arg = [&](size_t i) -> intptr_t { return i == 0 ? a1 : i == 1 ? a2 : i == 2 ? a3 : stack[i - 3]; };
        log.Read();
        log.Verbose(L"Entered " + m.name + L"() function.");
        std::vector<VARIANT*> outs;
        std::vector<Value> inputs;
        Switches s = TheModule.switches.count(m.name) != 0 ? TheModule.switches[m.name] : Switches();
        Conversion c;
        bool stopped = false;
        int index = 1;
        for (size_t i = 0; i < m.params.size(); i++)
        {
            const Param& p = m.params[i];
            intptr_t raw = arg(i);
            if (p.byRef && p.vt == VT_VARIANT)
            {
                outs.push_back(reinterpret_cast<VARIANT*>(raw));
                continue;
            }

            if (p.vt != VT_VARIANT || raw == 0 || stopped)
            {
                continue; // nargout (a long), which the .NET client ignores: it asks for every declared output
            }

            const VARIANT* given = reinterpret_cast<const VARIANT*>(raw);
            const VARIANT* real = given;
            while (real->vt == (VT_BYREF | VT_VARIANT) && real->pvarVal != nullptr)
            {
                real = real->pvarVal;
            }

            if (real->vt == VT_ERROR && real->scode == DISP_E_PARAMNOTFOUND)
            {
                // An argument left out (Optional in VBA, Type.Missing in .NET) ends the list, as the .NET client's loop does.
                stopped = true;
                continue;
            }

            Value v;
            if (!FromVariant(*given, s, v, c))
            {
                Answer(m, outs, c.failure);
                return S_OK;
            }

            log.Information(L"Input " + std::to_wstring(index++) + L" is given.");
            inputs.push_back(v);
        }

        Settings settings = ReadSettings(TheModule.addIn);
        log.Information(L"MPS server URL is " + settings.url + L".");
        if (settings.url.empty())
        {
            log.Critical(L"MPS server url is not provided for " + TheModule.addIn);
            Answer(m, outs, L"MATLAB error thrown : MPS server url is not provided for " + TheModule.addIn);
            return S_OK;
        }

        std::wstring url = settings.url;
        if (!url.empty() && url.back() == L'/')
        {
            url.pop_back();
        }

        url += L"/" + m.name;
        HttpResult http = Post(url, WriteRequest(m.outputs, inputs), settings, L"JGraph Excel client " + TheModule.version);
        if (!http.sent)
        {
            Answer(m, outs, L"WebException:" + http.failure);
            return S_OK;
        }

        if (http.status != 200)
        {
            Answer(m, outs, L"WebException:" + http.reason);
            return S_OK;
        }

        Reply reply = ReadReply(http.body);
        if (reply.malformed)
        {
            Answer(m, outs, L"Exception:The server's reply could not be read.");
            return S_OK;
        }

        if (reply.isError)
        {
            log.Critical(L"Catch MATLAB Exception: " + reply.message);
            Answer(m, outs, L"MATLABException:" + reply.identifier);
            return S_OK;
        }

        log.Verbose(L"Call to " + m.name + L" function returned.");
        std::wstring failure;
        for (size_t i = 0; i < outs.size(); i++)
        {
            if (i >= reply.outputs.size())
            {
                VariantClear(outs[i]);
                continue;
            }

            if (!ToVariant(reply.outputs[i], s, *outs[i], failure))
            {
                for (size_t k = 0; k <= i && k < outs.size(); k++)
                {
                    VariantClear(outs[k]);
                }

                Answer(m, outs, failure);
                return S_OK;
            }
        }

        return S_OK;
    }
};

HRESULT __stdcall ObjectQueryInterface(void* self, REFIID riid, void** ppv) { return ClientObject::From(self)->Query(riid, ppv); }
ULONG __stdcall ObjectAddRef(void* self) { return static_cast<ULONG>(InterlockedIncrement(&ClientObject::From(self)->refs)); }
ULONG __stdcall ObjectRelease(void* self) { return ClientObject::From(self)->Release(); }

HRESULT __stdcall ObjectGetTypeInfoCount(void* self, UINT* count)
{
    (void)self;
    if (count == nullptr)
    {
        return E_POINTER;
    }

    *count = 1;
    return S_OK;
}

HRESULT __stdcall ObjectGetTypeInfo(void* self, UINT index, LCID lcid, ITypeInfo** info)
{
    (void)self;
    (void)lcid;
    if (info == nullptr)
    {
        return E_POINTER;
    }

    *info = nullptr;
    if (index != 0)
    {
        return DISP_E_BADINDEX;
    }

    ITypeInfo* given = TheModule.dispatch != nullptr ? TheModule.dispatch : TheModule.info;
    given->AddRef();
    *info = given;
    return S_OK;
}

HRESULT __stdcall ObjectGetIDsOfNames(void* self, REFIID riid, LPOLESTR* names, UINT count, LCID lcid, DISPID* ids)
{
    (void)self;
    (void)riid;
    (void)lcid;
    return TheModule.info->GetIDsOfNames(names, count, ids);
}

HRESULT __stdcall ObjectInvoke(void* self, DISPID member, REFIID riid, LCID lcid, WORD flags, DISPPARAMS* params, VARIANT* result, EXCEPINFO* excepInfo, UINT* argErr)
{
    (void)riid;
    (void)lcid;
    // ITypeInfo::Invoke coerces the arguments as the type library declares them and calls through this
    // object's vtable: a late-bound caller (VBA's CreateObject) reaches the same thunks an early-bound one does.
    return TheModule.info->Invoke(static_cast<IDispatch*>(static_cast<void*>(self)), member, flags, params, result, excepInfo, argErr);
}

#define JGXL_SLOT(b, d) \
    __declspec(noinline) HRESULT __stdcall Slot_##b##d(void* self, intptr_t a1, intptr_t a2, intptr_t a3) \
    { \
        return ClientObject::From(self)->CallSlot(b * 10 + d, a1, a2, a3, static_cast<const intptr_t*>(_AddressOfReturnAddress()) + 5); \
    }
#define JGXL_SLOT_ENTRY(b, d) reinterpret_cast<const void*>(&Slot_##b##d),
#define JGXL_TEN(X, b) X(b, 0) X(b, 1) X(b, 2) X(b, 3) X(b, 4) X(b, 5) X(b, 6) X(b, 7) X(b, 8) X(b, 9)
#define JGXL_SLOTS(X) \
    X(0, 7) X(0, 8) X(0, 9) \
    JGXL_TEN(X, 1) JGXL_TEN(X, 2) JGXL_TEN(X, 3) JGXL_TEN(X, 4) JGXL_TEN(X, 5) JGXL_TEN(X, 6) JGXL_TEN(X, 7) JGXL_TEN(X, 8) JGXL_TEN(X, 9) \
    JGXL_TEN(X, 10) JGXL_TEN(X, 11) JGXL_TEN(X, 12) JGXL_TEN(X, 13) JGXL_TEN(X, 14) JGXL_TEN(X, 15) JGXL_TEN(X, 16) JGXL_TEN(X, 17) JGXL_TEN(X, 18) JGXL_TEN(X, 19) \
    JGXL_TEN(X, 20) JGXL_TEN(X, 21) JGXL_TEN(X, 22) JGXL_TEN(X, 23) JGXL_TEN(X, 24) JGXL_TEN(X, 25) JGXL_TEN(X, 26) JGXL_TEN(X, 27) JGXL_TEN(X, 28) JGXL_TEN(X, 29) \
    JGXL_TEN(X, 30) JGXL_TEN(X, 31) JGXL_TEN(X, 32) JGXL_TEN(X, 33) JGXL_TEN(X, 34) JGXL_TEN(X, 35) JGXL_TEN(X, 36) JGXL_TEN(X, 37) JGXL_TEN(X, 38) JGXL_TEN(X, 39) \
    JGXL_TEN(X, 40) JGXL_TEN(X, 41) JGXL_TEN(X, 42) JGXL_TEN(X, 43) JGXL_TEN(X, 44) JGXL_TEN(X, 45) JGXL_TEN(X, 46) JGXL_TEN(X, 47) JGXL_TEN(X, 48) JGXL_TEN(X, 49) \
    JGXL_TEN(X, 50) JGXL_TEN(X, 51)

JGXL_SLOTS(JGXL_SLOT)

const void* const ObjectVtbl[SlotCount] =
{
    reinterpret_cast<const void*>(&ObjectQueryInterface),
    reinterpret_cast<const void*>(&ObjectAddRef),
    reinterpret_cast<const void*>(&ObjectRelease),
    reinterpret_cast<const void*>(&ObjectGetTypeInfoCount),
    reinterpret_cast<const void*>(&ObjectGetTypeInfo),
    reinterpret_cast<const void*>(&ObjectGetIDsOfNames),
    reinterpret_cast<const void*>(&ObjectInvoke),
    JGXL_SLOTS(JGXL_SLOT_ENTRY)
};

static_assert(sizeof(ObjectVtbl) / sizeof(ObjectVtbl[0]) == SlotCount, "the vtable has a thunk per slot");

// ---- The class factory -------------------------------------------------------------------------------

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

    auto* object = new ClientObject();
    object->vtbl = ObjectVtbl;
    object->refs = 1;
    object->log.addIn = TheModule.addIn;
    InterlockedIncrement(&Objects);
    HRESULT hr = object->Query(riid, ppv);
    object->Release();
    return hr;
}

HRESULT __stdcall FactoryLockServer(void* self, BOOL lock)
{
    (void)self;
    if (lock)
    {
        InterlockedIncrement(&Locks);
    }
    else
    {
        InterlockedDecrement(&Locks);
    }

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

// ---- Registration ----------------------------------------------------------------------------------------

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

// The keys R2025b's regasm /regfile writes for the generated class (k17_excel: fromresults.registry):
// the ProgID with its CLSID, the CLSID with InprocServer32, ProgId and Implemented Categories; plus the
// type library and the VersionIndependentProgID JGraph's native client has.
LSTATUS WriteRegistration(HKEY classes, const Module& m, bool perUser)
{
    std::wstring clsid = L"CLSID\\" + GuidText(m.clsid);
    LSTATUS status = WriteValue(classes, m.progId, nullptr, m.progId);
    if (status != ERROR_SUCCESS)
    {
        return status;
    }

    WriteValue(classes, m.progId + L"\\CLSID", nullptr, GuidText(m.clsid));
    WriteValue(classes, clsid, nullptr, m.progId);
    WriteKey(classes, clsid + L"\\Implemented Categories");
    WriteKey(classes, clsid + L"\\Implemented Categories\\" + GuidText(CategoryId));
    WriteValue(classes, clsid + L"\\InprocServer32", nullptr, m.path);
    WriteValue(classes, clsid + L"\\InprocServer32", L"ThreadingModel", L"Both");
    WriteValue(classes, clsid + L"\\ProgID", nullptr, m.progId);
    WriteValue(classes, clsid + L"\\VersionIndependentProgID", nullptr, m.progId);
    WriteValue(classes, clsid + L"\\TypeLib", nullptr, m.libid);
    wchar_t version[32];
    swprintf(version, 32, L"%u.%u", static_cast<unsigned>(m.major), static_cast<unsigned>(m.minor));
    WriteValue(classes, clsid + L"\\TypeLib", L"Version", version);
    WriteValue(classes, std::wstring(L"Component Categories\\") + GuidText(CategoryId), L"409", CategoryName);
    std::wstring folder = FolderOf(m.path);
    std::vector<wchar_t> path(m.path.begin(), m.path.end());
    path.push_back(L'\0');
    std::vector<wchar_t> help(folder.begin(), folder.end());
    help.push_back(L'\0');
    HRESULT hr = perUser ? RegisterTypeLibForUser(m.lib, path.data(), help.data()) : RegisterTypeLib(m.lib, path.data(), help.data());
    return SUCCEEDED(hr) ? ERROR_SUCCESS : static_cast<LSTATUS>(hr);
}

void RemoveRegistration(HKEY classes, const Module& m, bool perUser)
{
    RegDeleteTreeW(classes, m.progId.c_str());
    RegDeleteTreeW(classes, (L"CLSID\\" + GuidText(m.clsid)).c_str());
    if (perUser)
    {
        UnRegisterTypeLibForUser(m.libidGuid, m.major, m.minor, 0, SYS_WIN64);
    }
    else
    {
        UnRegisterTypeLib(m.libidGuid, m.major, m.minor, 0, SYS_WIN64);
    }
}

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
    if (!LoadModule())
    {
        return E_FAIL;
    }

    if (!IsEqualCLSID(clsid, TheModule.clsid))
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

// Registers machine-wide when the caller may, else for the current user (what regsvr32 gives elevated
// and what R2025b's reg import of regasm's script gives a user).
extern "C" HRESULT __stdcall DllRegisterServer()
{
    if (!LoadModule())
    {
        return SELFREG_E_TYPELIB;
    }

    HKEY machine = nullptr;
    LSTATUS status = RegOpenKeyExW(HKEY_LOCAL_MACHINE, L"Software\\Classes", 0, KEY_WRITE, &machine);
    bool perUser = status != ERROR_SUCCESS;
    if (!perUser)
    {
        status = WriteRegistration(machine, TheModule, false);
        RegCloseKey(machine);
        perUser = status == ERROR_ACCESS_DENIED;
    }

    if (perUser)
    {
        HKEY user = nullptr;
        status = RegOpenKeyExW(HKEY_CURRENT_USER, L"Software\\Classes", 0, KEY_WRITE, &user);
        if (status == ERROR_SUCCESS)
        {
            status = WriteRegistration(user, TheModule, true);
            RegCloseKey(user);
        }
    }

    return status == ERROR_SUCCESS ? S_OK : SELFREG_E_CLASS;
}

extern "C" HRESULT __stdcall DllUnregisterServer()
{
    if (!LoadModule())
    {
        return SELFREG_E_TYPELIB;
    }

    HKEY user = nullptr;
    if (RegOpenKeyExW(HKEY_CURRENT_USER, L"Software\\Classes", 0, KEY_WRITE | KEY_READ, &user) == ERROR_SUCCESS)
    {
        if (Registered(user, L"CLSID\\" + GuidText(TheModule.clsid)) || Registered(user, TheModule.progId))
        {
            RemoveRegistration(user, TheModule, true);
        }

        RegCloseKey(user);
    }

    HKEY machine = nullptr;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, L"Software\\Classes", 0, KEY_WRITE | KEY_READ, &machine) == ERROR_SUCCESS)
    {
        if (Registered(machine, L"CLSID\\" + GuidText(TheModule.clsid)) || Registered(machine, TheModule.progId))
        {
            RemoveRegistration(machine, TheModule, false);
        }

        RegCloseKey(machine);
    }

    return S_OK;
}
