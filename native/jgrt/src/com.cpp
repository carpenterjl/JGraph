// com.cpp - the COM objects JGraph's Excel add-in is served by (compiler and SDK plan, stage K17c;
// decisions KO9 and KO10).
//
// An add-in's DLL is a copy of the template jgxl.dll stamped with its type library (resource 1,
// TYPELIB) and its archive (resource 2, RT_RCDATA); the template holds no code for the component
// and hands DllGetClassObject to jgrtXlGetClassObject here. This file reads the type library, gives
// out a class factory for its coclass and makes each object generically: a vtable of IUnknown,
// IDispatch and one thunk per further slot, so the dual interface the type library declares is
// served whole (a late-bound client through Invoke, which ITypeInfo::Invoke drives through that
// same vtable as ATL's IDispatchImpl does; an early-bound one straight through it). Every call is
// forwarded by member name to the engine (NativeBridge's ComInvoke) with its arguments as VARIANTs,
// the [in, out] slots pointing at the caller's own; the engine converts, calls the component and
// writes the outputs back. An object the engine hands out in turn (the flags) is wrapped the same
// way under its own interface (jgrtComWrap), and one that comes back in is recognized
// (jgrtComUnwrap). An error the engine reports becomes an IErrorInfo with R2025b's shape: the
// versioned ProgID as its source and the message as its description.
//
// The thunks read their arguments as pointer-sized slots, which on x64 every argument of these
// interfaces is: a long, a VARIANT_BOOL, a BSTR, an interface pointer, a VARIANT* and a VARIANT by
// value (passed as a pointer to the caller's copy). A floating-point argument by value would come
// in an XMM register instead, so the type libraries JGraph writes never declare one.
#include "jgrt_internal.h"

#include <oaidl.h>
#include <ocidl.h>
#include <intrin.h>

#include <cstddef>
#include <cstdio>
#include <cstring>
#include <map>
#include <mutex>
#include <vector>

namespace
{
// The slots the vtable has: IUnknown's three, IDispatch's four, and the members after them.
const int FirstMemberSlot = 7;
const int SlotCount = 520;

// The interface a managed handle is read back through (jgrtComUnwrap): JGraph's own, served by every object here.
// {7B5E2D2C-3A41-4C9B-9E2F-1D6A0C4F8E21}
const IID IID_JgrtManagedObject = { 0x7B5E2D2C, 0x3A41, 0x4C9B, { 0x9E, 0x2F, 0x1D, 0x6A, 0x0C, 0x4F, 0x8E, 0x21 } };

struct GuidLess
{
    bool operator()(const GUID& a, const GUID& b) const { return memcmp(&a, &b, sizeof(GUID)) < 0; }
};

// One parameter of a member, as the type library declares it.
struct Param
{
    VARTYPE vt = VT_EMPTY;   // the type pointed to, an interface as VT_DISPATCH, an enumeration as VT_I4
    bool byRef = false;      // a pointer (VARIANT*, BSTR*, IDispatch**), [in, out] or [out]
    bool retval = false;
};

struct Member
{
    MEMBERID id = 0;
    INVOKEKIND kind = INVOKE_FUNC;
    std::wstring name;
    std::vector<Param> params;
};

// One interface of a type library, with its members by vtable slot.
struct Interface
{
    IID iid = {};
    std::wstring name;
    ITypeInfo* info = nullptr;       // the TKIND_INTERFACE view, which Invoke and GetIDsOfNames drive
    ITypeInfo* dispatch = nullptr;   // the TKIND_DISPATCH view a client reads members from, when there is one
    std::vector<Member> slots;       // indexed by vtable slot; an empty name means no member there
};

// One add-in DLL: its type library, its coclass, and the interfaces served so far.
struct Module
{
    std::wstring path;
    ITypeLib* lib = nullptr;
    CLSID clsid = {};
    IID iid = {};
    std::wstring libraryName;
    std::wstring className;
    std::wstring progId;             // <library>.<class>.<major>_<minor>, the error source
    std::map<IID, Interface*, GuidLess> interfaces;
    LONG objects = 0;
    LONG locks = 0;
};

std::mutex ComGate;
std::map<std::wstring, Module*> Modules;

std::wstring Lower(std::wstring text)
{
    for (wchar_t& c : text)
    {
        c = static_cast<wchar_t>(towlower(c));
    }

    return text;
}

std::wstring Name(ITypeInfo* info, MEMBERID id)
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

// A type's documentation string.
std::wstring Doc(ITypeInfo* info)
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

std::wstring ToText(const wchar_t* text) { return text == nullptr ? std::wstring() : std::wstring(text); }

// Says what went wrong to whoever asks through IErrorInfo, as the add-in's own error: the versioned
// ProgID as the source and the text as the description (R2025b: src=[k17xl.K17Xl.1_0]).
void Report(const std::wstring& source, const std::wstring& description, const IID& iid)
{
    ICreateErrorInfo* make = nullptr;
    if (FAILED(CreateErrorInfo(&make)) || make == nullptr)
    {
        return;
    }

    make->SetGUID(iid);
    BSTR s = SysAllocStringLen(source.data(), static_cast<UINT>(source.size()));
    BSTR d = SysAllocStringLen(description.data(), static_cast<UINT>(description.size()));
    make->SetSource(s);
    make->SetDescription(d);
    SysFreeString(s);
    SysFreeString(d);
    IErrorInfo* info = nullptr;
    if (SUCCEEDED(make->QueryInterface(IID_IErrorInfo, reinterpret_cast<void**>(&info))) && info != nullptr)
    {
        SetErrorInfo(0, info);
        info->Release();
    }

    make->Release();
}

// A parameter's shape: the pointer levels peeled, an interface (any TKIND but an enumeration) read
// as VT_DISPATCH and an enumeration as a long.
Param ParamOf(ITypeInfo* info, const ELEMDESC& elem)
{
    Param p;
    const TYPEDESC* t = &elem.tdesc;
    int pointers = 0;
    while (t->vt == VT_PTR && t->lptdesc != nullptr)
    {
        t = t->lptdesc;
        pointers++;
    }

    p.vt = t->vt;
    if (t->vt == VT_USERDEFINED)
    {
        p.vt = VT_DISPATCH;
        ITypeInfo* referenced = nullptr;
        if (SUCCEEDED(info->GetRefTypeInfo(t->hreftype, &referenced)) && referenced != nullptr)
        {
            TYPEATTR* attr = nullptr;
            if (SUCCEEDED(referenced->GetTypeAttr(&attr)) && attr != nullptr)
            {
                if (attr->typekind == TKIND_ENUM || attr->typekind == TKIND_ALIAS)
                {
                    p.vt = VT_I4;
                }
                else
                {
                    // An interface pointer by value is one pointer level; a long is none.
                    pointers--;
                }

                referenced->ReleaseTypeAttr(attr);
            }

            referenced->Release();
        }
        else
        {
            pointers--;
        }
    }

    p.byRef = pointers > 0;
    USHORT flags = elem.paramdesc.wParamFlags;
    p.retval = (flags & PARAMFLAG_FRETVAL) != 0;
    return p;
}

Interface* LoadInterface(Module* module, const IID& iid)
{
    auto found = module->interfaces.find(iid);
    if (found != module->interfaces.end())
    {
        return found->second;
    }

    ITypeInfo* info = nullptr;
    if (module->lib == nullptr || FAILED(module->lib->GetTypeInfoOfGuid(iid, &info)) || info == nullptr)
    {
        return nullptr;
    }

    // A dual interface has two views of one GUID; Invoke drives the vtable one.
    ITypeInfo* dispatch = nullptr;
    TYPEATTR* attr = nullptr;
    if (SUCCEEDED(info->GetTypeAttr(&attr)) && attr != nullptr)
    {
        if (attr->typekind == TKIND_DISPATCH)
        {
            HREFTYPE href = 0;
            ITypeInfo* other = nullptr;
            if (SUCCEEDED(info->GetRefTypeOfImplType(static_cast<UINT>(-1), &href)) && SUCCEEDED(info->GetRefTypeInfo(href, &other)) && other != nullptr)
            {
                dispatch = info;
                info = other;
            }
        }
        else if ((attr->wTypeFlags & TYPEFLAG_FDUAL) != 0)
        {
            HREFTYPE href = 0;
            if (SUCCEEDED(info->GetRefTypeOfImplType(static_cast<UINT>(-1), &href)))
            {
                info->GetRefTypeInfo(href, &dispatch);
            }
        }

        info->ReleaseTypeAttr(attr);
    }

    auto* made = new Interface();
    made->iid = iid;
    made->info = info;
    made->dispatch = dispatch;
    made->name = Name(info, MEMBERID_NIL);
    made->slots.resize(SlotCount);
    attr = nullptr;
    if (SUCCEEDED(info->GetTypeAttr(&attr)) && attr != nullptr)
    {
        for (UINT k = 0; k < attr->cFuncs; k++)
        {
            FUNCDESC* fd = nullptr;
            if (FAILED(info->GetFuncDesc(k, &fd)) || fd == nullptr)
            {
                continue;
            }

            int slot = static_cast<int>(fd->oVft / sizeof(void*));
            if (slot >= FirstMemberSlot && slot < SlotCount)
            {
                Member& m = made->slots[static_cast<size_t>(slot)];
                m.id = fd->memid;
                m.kind = fd->invkind;
                m.name = Name(info, fd->memid);
                for (SHORT p = 0; p < fd->cParams; p++)
                {
                    m.params.push_back(ParamOf(info, fd->lprgelemdescParam[p]));
                }
            }

            info->ReleaseFuncDesc(fd);
        }

        info->ReleaseTypeAttr(attr);
    }

    module->interfaces[iid] = made;
    return made;
}

// The module of an add-in DLL, made once per path: its type library read from the DLL's own
// resource, the one coclass in it, and that coclass's default interface.
Module* LoadModule(const std::wstring& path, std::wstring* why)
{
    std::wstring key = Lower(path);
    auto found = Modules.find(key);
    if (found != Modules.end())
    {
        return found->second;
    }

    ITypeLib* lib = nullptr;
    HRESULT hr = LoadTypeLibEx(path.c_str(), REGKIND_NONE, &lib);
    if (FAILED(hr) || lib == nullptr)
    {
        if (why != nullptr)
        {
            wchar_t code[16];
            swprintf(code, 16, L"0x%08X", static_cast<unsigned>(hr));
            *why = L"The add-in " + path + L" carries no type library (" + code + L").";
        }

        return nullptr;
    }

    auto* module = new Module();
    module->path = path;
    module->lib = lib;
    BSTR libName = nullptr;
    BSTR libDoc = nullptr;
    if (SUCCEEDED(lib->GetDocumentation(-1, &libName, &libDoc, nullptr, nullptr)) && libName != nullptr)
    {
        module->libraryName.assign(libName, SysStringLen(libName));
    }

    TLIBATTR* libAttr = nullptr;
    WORD major = 1;
    WORD minor = 0;
    if (SUCCEEDED(lib->GetLibAttr(&libAttr)) && libAttr != nullptr)
    {
        major = libAttr->wMajorVerNum;
        minor = libAttr->wMinorVerNum;
        lib->ReleaseTLibAttr(libAttr);
    }

    // A type library's names are case-insensitive (a library k17xl makes its coclass K17Xl read back
    // as k17xl, in R2025b's libraries too); the names as given are in the documentation strings,
    // "<addin> <major>.<minor> Type Library" and "<Class> Class".
    wchar_t tail[64];
    swprintf(tail, 64, L" %u.%u Type Library", static_cast<unsigned>(major), static_cast<unsigned>(minor));
    if (libDoc != nullptr)
    {
        std::wstring doc(libDoc, SysStringLen(libDoc));
        if (EndsWith(doc, tail))
        {
            module->libraryName = doc.substr(0, doc.size() - wcslen(tail));
        }
    }

    SysFreeString(libName);
    SysFreeString(libDoc);

    UINT count = lib->GetTypeInfoCount();
    bool coclassFound = false;
    for (UINT k = 0; k < count && !coclassFound; k++)
    {
        ITypeInfo* info = nullptr;
        if (FAILED(lib->GetTypeInfo(k, &info)) || info == nullptr)
        {
            continue;
        }

        TYPEATTR* attr = nullptr;
        if (SUCCEEDED(info->GetTypeAttr(&attr)) && attr != nullptr)
        {
            if (attr->typekind == TKIND_COCLASS)
            {
                module->clsid = attr->guid;
                module->className = Name(info, MEMBERID_NIL);
                std::wstring doc = Doc(info);
                if (EndsWith(doc, L" Class"))
                {
                    module->className = doc.substr(0, doc.size() - 6);
                }

                HREFTYPE href = 0;
                ITypeInfo* defaultInterface = nullptr;
                if (SUCCEEDED(info->GetRefTypeOfImplType(0, &href)) && SUCCEEDED(info->GetRefTypeInfo(href, &defaultInterface)) && defaultInterface != nullptr)
                {
                    TYPEATTR* iattr = nullptr;
                    if (SUCCEEDED(defaultInterface->GetTypeAttr(&iattr)) && iattr != nullptr)
                    {
                        module->iid = iattr->guid;
                        coclassFound = true;
                        defaultInterface->ReleaseTypeAttr(iattr);
                    }

                    defaultInterface->Release();
                }
            }

            info->ReleaseTypeAttr(attr);
        }

        info->Release();
    }

    if (!coclassFound)
    {
        if (why != nullptr)
        {
            *why = L"The add-in " + path + L"'s type library declares no coclass.";
        }

        lib->Release();
        delete module;
        return nullptr;
    }

    wchar_t version[32];
    swprintf(version, 32, L".%u_%u", static_cast<unsigned>(major), static_cast<unsigned>(minor));
    module->progId = module->libraryName + L"." + module->className + version;
    Modules[key] = module;
    return module;
}

// ---- The object ------------------------------------------------------------------------------

struct ComObject;

// ISupportErrorInfo, a second vtable inside the object (a C-style tear-off at a fixed offset).
struct SupportErrorInfoPart
{
    const void* const* vtbl;
};

struct ComObject
{
    const void* const* vtbl;         // IUnknown + IDispatch + the member thunks
    SupportErrorInfoPart errorInfo;  // ISupportErrorInfo
    LONG refs;
    Module* module;
    Interface* iface;
    intptr_t handle;                 // the engine's object

    static ComObject* From(void* self) { return static_cast<ComObject*>(self); }
    static ComObject* FromErrorInfo(void* self)
    {
        return reinterpret_cast<ComObject*>(static_cast<char*>(self) - offsetof(ComObject, errorInfo));
    }

    HRESULT Query(REFIID riid, void** ppv)
    {
        if (ppv == nullptr)
        {
            return E_POINTER;
        }

        *ppv = nullptr;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IDispatch) || IsEqualIID(riid, iface->iid) || IsEqualIID(riid, IID_JgrtManagedObject))
        {
            *ppv = this;
        }
        else if (IsEqualIID(riid, IID_ISupportErrorInfo))
        {
            *ppv = &errorInfo;
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
            if (const JgrtManaged* engine = JgrtEngine(); engine != nullptr && engine->ComRelease != nullptr)
            {
                engine->ComRelease(handle);
            }

            InterlockedDecrement(&module->objects);
            delete this;
        }

        return static_cast<ULONG>(left);
    }

    // The thunks' common body: the member of `slot`, its arguments read from the register and stack
    // slots as the type library's parameters say, the call forwarded to the engine, the result
    // written to the [out, retval] parameter.
    HRESULT CallSlot(int slot, intptr_t a1, intptr_t a2, intptr_t a3, const intptr_t* stack)
    {
        const Member& m = iface->slots[static_cast<size_t>(slot)];
        if (m.name.empty())
        {
            return E_NOTIMPL;
        }

        auto arg = [&](size_t i) -> intptr_t { return i == 0 ? a1 : i == 1 ? a2 : i == 2 ? a3 : stack[i - 3]; };
        std::vector<VARIANT> temps(m.params.size());
        std::vector<VARIANT*> argv;
        void* retvalAt = nullptr;
        const Param* retval = nullptr;
        for (size_t i = 0; i < m.params.size(); i++)
        {
            const Param& p = m.params[i];
            VariantInit(&temps[i]);
            intptr_t raw = arg(i);
            if (p.byRef && (p.retval || p.vt != VT_VARIANT))
            {
                // The one [out, retval] (or a plain [out]) parameter receives the result.
                if (retvalAt == nullptr)
                {
                    retvalAt = reinterpret_cast<void*>(raw);
                    retval = &p;
                }

                continue;
            }

            if (p.byRef)
            {
                temps[i].vt = VT_BYREF | VT_VARIANT;
                temps[i].pvarVal = reinterpret_cast<VARIANT*>(raw);
            }
            else
            {
                switch (p.vt)
                {
                case VT_I4: case VT_INT: case VT_UI4: case VT_UINT: case VT_I2: case VT_UI2: case VT_I1: case VT_UI1:
                    temps[i].vt = VT_I4;
                    temps[i].lVal = static_cast<LONG>(raw);
                    break;
                case VT_BOOL:
                    temps[i].vt = VT_BOOL;
                    temps[i].boolVal = static_cast<VARIANT_BOOL>(raw);
                    break;
                case VT_BSTR:
                    temps[i].vt = VT_BSTR;
                    temps[i].bstrVal = reinterpret_cast<BSTR>(raw);
                    break;
                case VT_DISPATCH: case VT_UNKNOWN:
                    temps[i].vt = VT_DISPATCH;
                    temps[i].pdispVal = reinterpret_cast<IDispatch*>(raw);
                    break;
                case VT_VARIANT:
                    if (raw != 0)
                    {
                        temps[i] = *reinterpret_cast<const VARIANT*>(raw);
                    }

                    break;
                default:
                    Report(module->progId, L"The member " + m.name + L" takes a parameter JGraph's COM objects cannot read.", iface->iid);
                    return E_NOTIMPL;
                }
            }

            argv.push_back(&temps[i]);
        }

        const JgrtManaged* engine = JgrtEngine();
        if (engine == nullptr || engine->ComInvoke == nullptr)
        {
            Report(module->progId, L"The JGraph Runtime's engine did not start:\n" + JgrtLastComplaint(), iface->iid);
            return E_FAIL;
        }

        VARIANT result;
        VariantInit(&result);
        wchar_t* source = nullptr;
        wchar_t* description = nullptr;
        int hr = engine->ComInvoke(handle, m.name.c_str(), static_cast<int>(m.kind), static_cast<int>(argv.size()),
            argv.empty() ? nullptr : argv.data(), &result, &source, &description);
        if (FAILED(hr))
        {
            Report(source != nullptr ? ToText(source) : module->progId, ToText(description), iface->iid);
            CoTaskMemFree(source);
            CoTaskMemFree(description);
            VariantClear(&result);
            return hr;
        }

        CoTaskMemFree(source);
        CoTaskMemFree(description);
        HRESULT written = S_OK;
        if (retvalAt != nullptr)
        {
            switch (retval->vt)
            {
            case VT_BSTR:
                if (result.vt != VT_BSTR && result.vt != VT_EMPTY)
                {
                    written = VariantChangeType(&result, &result, 0, VT_BSTR);
                }

                *static_cast<BSTR*>(retvalAt) = result.vt == VT_BSTR ? result.bstrVal : SysAllocString(L"");
                result.vt = VT_EMPTY;
                break;
            case VT_DISPATCH: case VT_UNKNOWN:
                *static_cast<IDispatch**>(retvalAt) = result.vt == VT_DISPATCH ? result.pdispVal : nullptr;
                result.vt = VT_EMPTY;
                break;
            case VT_BOOL:
                written = VariantChangeType(&result, &result, 0, VT_BOOL);
                *static_cast<VARIANT_BOOL*>(retvalAt) = SUCCEEDED(written) ? result.boolVal : VARIANT_FALSE;
                break;
            case VT_VARIANT:
                VariantInit(static_cast<VARIANT*>(retvalAt));
                *static_cast<VARIANT*>(retvalAt) = result;
                result.vt = VT_EMPTY;
                break;
            default:
                written = VariantChangeType(&result, &result, 0, VT_I4);
                *static_cast<LONG*>(retvalAt) = SUCCEEDED(written) ? result.lVal : 0;
                break;
            }
        }

        VariantClear(&result);
        return written;
    }
};

// ---- IUnknown and IDispatch ------------------------------------------------------------------

HRESULT __stdcall ObjectQueryInterface(void* self, REFIID riid, void** ppv) { return ComObject::From(self)->Query(riid, ppv); }
ULONG __stdcall ObjectAddRef(void* self) { return static_cast<ULONG>(InterlockedIncrement(&ComObject::From(self)->refs)); }
ULONG __stdcall ObjectRelease(void* self) { return ComObject::From(self)->Release(); }

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

    Interface* iface = ComObject::From(self)->iface;
    ITypeInfo* given = iface->dispatch != nullptr ? iface->dispatch : iface->info;
    given->AddRef();
    *info = given;
    return S_OK;
}

HRESULT __stdcall ObjectGetIDsOfNames(void* self, REFIID riid, LPOLESTR* names, UINT count, LCID lcid, DISPID* ids)
{
    (void)riid;
    (void)lcid;
    return ComObject::From(self)->iface->info->GetIDsOfNames(names, count, ids);
}

HRESULT __stdcall ObjectInvoke(void* self, DISPID member, REFIID riid, LCID lcid, WORD flags, DISPPARAMS* params, VARIANT* result,
    EXCEPINFO* excepInfo, UINT* argErr)
{
    (void)riid;
    (void)lcid;
    ComObject* object = ComObject::From(self);
    // ITypeInfo::Invoke coerces the arguments as the type library declares them and calls through
    // this object's vtable; a failed call's IErrorInfo becomes its EXCEPINFO, as ATL's objects do it.
    return object->iface->info->Invoke(static_cast<IDispatch*>(static_cast<void*>(object)), member, flags, params, result, excepInfo, argErr);
}

// ---- ISupportErrorInfo -----------------------------------------------------------------------

HRESULT __stdcall ErrorInfoQueryInterface(void* self, REFIID riid, void** ppv) { return ComObject::FromErrorInfo(self)->Query(riid, ppv); }
ULONG __stdcall ErrorInfoAddRef(void* self) { return static_cast<ULONG>(InterlockedIncrement(&ComObject::FromErrorInfo(self)->refs)); }
ULONG __stdcall ErrorInfoRelease(void* self) { return ComObject::FromErrorInfo(self)->Release(); }
HRESULT __stdcall ErrorInfoInterfaceSupportsErrorInfo(void* self, REFIID riid)
{
    ComObject* object = ComObject::FromErrorInfo(self);
    return IsEqualIID(riid, object->iface->iid) || IsEqualIID(riid, IID_IDispatch) ? S_OK : S_FALSE;
}

const void* const ErrorInfoVtbl[] =
{
    reinterpret_cast<const void*>(&ErrorInfoQueryInterface),
    reinterpret_cast<const void*>(&ErrorInfoAddRef),
    reinterpret_cast<const void*>(&ErrorInfoRelease),
    reinterpret_cast<const void*>(&ErrorInfoInterfaceSupportsErrorInfo),
};

// ---- The member thunks -----------------------------------------------------------------------

// Each thunk knows its slot and reads the arguments past the three register ones from the caller's
// stack: the return address, then the 32-byte shadow space, then the fourth argument on.
#define JGRT_SLOT(b, d) \
    __declspec(noinline) HRESULT __stdcall Slot_##b##d(void* self, intptr_t a1, intptr_t a2, intptr_t a3) \
    { \
        return ComObject::From(self)->CallSlot(b * 10 + d, a1, a2, a3, static_cast<const intptr_t*>(_AddressOfReturnAddress()) + 5); \
    }
#define JGRT_SLOT_ENTRY(b, d) reinterpret_cast<const void*>(&Slot_##b##d),

// Every slot from 7 to 519, applied to X(tens, digit).
#define JGRT_TEN(X, b) X(b, 0) X(b, 1) X(b, 2) X(b, 3) X(b, 4) X(b, 5) X(b, 6) X(b, 7) X(b, 8) X(b, 9)
#define JGRT_SLOTS(X) \
    X(0, 7) X(0, 8) X(0, 9) \
    JGRT_TEN(X, 1) JGRT_TEN(X, 2) JGRT_TEN(X, 3) JGRT_TEN(X, 4) JGRT_TEN(X, 5) JGRT_TEN(X, 6) JGRT_TEN(X, 7) JGRT_TEN(X, 8) JGRT_TEN(X, 9) \
    JGRT_TEN(X, 10) JGRT_TEN(X, 11) JGRT_TEN(X, 12) JGRT_TEN(X, 13) JGRT_TEN(X, 14) JGRT_TEN(X, 15) JGRT_TEN(X, 16) JGRT_TEN(X, 17) JGRT_TEN(X, 18) JGRT_TEN(X, 19) \
    JGRT_TEN(X, 20) JGRT_TEN(X, 21) JGRT_TEN(X, 22) JGRT_TEN(X, 23) JGRT_TEN(X, 24) JGRT_TEN(X, 25) JGRT_TEN(X, 26) JGRT_TEN(X, 27) JGRT_TEN(X, 28) JGRT_TEN(X, 29) \
    JGRT_TEN(X, 30) JGRT_TEN(X, 31) JGRT_TEN(X, 32) JGRT_TEN(X, 33) JGRT_TEN(X, 34) JGRT_TEN(X, 35) JGRT_TEN(X, 36) JGRT_TEN(X, 37) JGRT_TEN(X, 38) JGRT_TEN(X, 39) \
    JGRT_TEN(X, 40) JGRT_TEN(X, 41) JGRT_TEN(X, 42) JGRT_TEN(X, 43) JGRT_TEN(X, 44) JGRT_TEN(X, 45) JGRT_TEN(X, 46) JGRT_TEN(X, 47) JGRT_TEN(X, 48) JGRT_TEN(X, 49) \
    JGRT_TEN(X, 50) JGRT_TEN(X, 51)

JGRT_SLOTS(JGRT_SLOT)

const void* const ObjectVtbl[SlotCount] =
{
    reinterpret_cast<const void*>(&ObjectQueryInterface),
    reinterpret_cast<const void*>(&ObjectAddRef),
    reinterpret_cast<const void*>(&ObjectRelease),
    reinterpret_cast<const void*>(&ObjectGetTypeInfoCount),
    reinterpret_cast<const void*>(&ObjectGetTypeInfo),
    reinterpret_cast<const void*>(&ObjectGetIDsOfNames),
    reinterpret_cast<const void*>(&ObjectInvoke),
    JGRT_SLOTS(JGRT_SLOT_ENTRY)
};

static_assert(sizeof(ObjectVtbl) / sizeof(ObjectVtbl[0]) == SlotCount, "the vtable has a thunk per slot");

ComObject* NewObject(Module* module, Interface* iface, intptr_t handle)
{
    auto* made = new ComObject();
    made->vtbl = ObjectVtbl;
    made->errorInfo.vtbl = ErrorInfoVtbl;
    made->refs = 1;
    made->module = module;
    made->iface = iface;
    made->handle = handle;
    InterlockedIncrement(&module->objects);
    return made;
}

// ---- The class factory -----------------------------------------------------------------------

struct ClassFactory
{
    const void* const* vtbl;
    LONG refs;
    Module* module;
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
    if (ppv == nullptr)
    {
        return E_POINTER;
    }

    *ppv = nullptr;
    if (outer != nullptr)
    {
        return CLASS_E_NOAGGREGATION;
    }

    Module* module = static_cast<ClassFactory*>(self)->module;
    const JgrtManaged* engine = JgrtEngine();
    if (engine == nullptr || engine->ComCreate == nullptr)
    {
        Report(module->progId, L"The JGraph Runtime's engine did not start:\n" + JgrtLastComplaint(), module->iid);
        return E_FAIL;
    }

    Interface* iface;
    {
        std::lock_guard<std::mutex> hold(ComGate);
        iface = LoadInterface(module, module->iid);
    }

    if (iface == nullptr)
    {
        Report(module->progId, L"The add-in's type library has no interface for its class.", module->iid);
        return E_FAIL;
    }

    wchar_t* why = nullptr;
    intptr_t handle = engine->ComCreate(module->path.c_str(), reinterpret_cast<intptr_t>(module), &why);
    if (handle == 0)
    {
        Report(module->progId, why != nullptr ? ToText(why) : L"The add-in could not be opened.", module->iid);
        CoTaskMemFree(why);
        return E_FAIL;
    }

    CoTaskMemFree(why);
    ComObject* object = NewObject(module, iface, handle);
    HRESULT hr = object->Query(riid, ppv);
    object->Release();
    return hr;
}

HRESULT __stdcall FactoryLockServer(void* self, BOOL lock)
{
    Module* module = static_cast<ClassFactory*>(self)->module;
    if (lock)
    {
        InterlockedIncrement(&module->locks);
    }
    else
    {
        InterlockedDecrement(&module->locks);
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
}

// ---- What the template calls ------------------------------------------------------------------

// The template's DllGetClassObject: the class factory of the coclass the DLL at `path` declares.
extern "C" __declspec(dllexport) HRESULT jgrtXlGetClassObject(const wchar_t* path, const CLSID* clsid, const IID* iid, void** ppv)
{
    if (ppv == nullptr)
    {
        return E_POINTER;
    }

    *ppv = nullptr;
    if (path == nullptr || clsid == nullptr || iid == nullptr)
    {
        return E_INVALIDARG;
    }

    Module* module;
    std::wstring why;
    {
        std::lock_guard<std::mutex> hold(ComGate);
        module = LoadModule(path, &why);
    }

    if (module == nullptr)
    {
        Report(L"JGraph Excel add-in", why, IID_IClassFactory);
        return E_FAIL;
    }

    if (!IsEqualCLSID(*clsid, module->clsid))
    {
        return CLASS_E_CLASSNOTAVAILABLE;
    }

    auto* factory = new ClassFactory{ FactoryVtbl, 1, module };
    HRESULT hr = FactoryQueryInterface(factory, *iid, ppv);
    FactoryRelease(factory);
    return hr;
}

// The template's DllCanUnloadNow: S_OK while no object of the DLL's class is alive and no client
// locked its factory (a DLL never loaded here can always go); jgrt.dll itself stays.
extern "C" __declspec(dllexport) HRESULT jgrtXlCanUnloadNow(const wchar_t* path)
{
    if (path == nullptr)
    {
        return S_OK;
    }

    std::lock_guard<std::mutex> hold(ComGate);
    auto found = Modules.find(Lower(path));
    if (found == Modules.end())
    {
        return S_OK;
    }

    return found->second->objects == 0 && found->second->locks == 0 ? S_OK : S_FALSE;
}

// ---- What the engine calls --------------------------------------------------------------------

// Wraps the engine's object `handle` as a COM object serving the interface `iid` of `module`'s type
// library (the component's own, or the flags'); null when the type library has no such interface.
// The caller gets one reference.
extern "C" __declspec(dllexport) IDispatch* jgrtComWrap(intptr_t handle, intptr_t module, const GUID* iid)
{
    if (handle == 0 || module == 0 || iid == nullptr)
    {
        return nullptr;
    }

    Interface* iface;
    {
        std::lock_guard<std::mutex> hold(ComGate);
        iface = LoadInterface(reinterpret_cast<Module*>(module), *iid);
    }

    if (iface == nullptr)
    {
        return nullptr;
    }

    ComObject* object = NewObject(reinterpret_cast<Module*>(module), iface, handle);
    return static_cast<IDispatch*>(static_cast<void*>(object));
}

// The engine's handle behind a COM object made here, or 0 for any other object.
extern "C" __declspec(dllexport) intptr_t jgrtComUnwrap(IUnknown* object)
{
    if (object == nullptr)
    {
        return 0;
    }

    void* ours = nullptr;
    if (FAILED(object->QueryInterface(IID_JgrtManagedObject, &ours)) || ours == nullptr)
    {
        return 0;
    }

    intptr_t handle = ComObject::From(ours)->handle;
    ComObject::From(ours)->Release();
    return handle;
}

// The versioned ProgID of the component served from `module`, for the engine's error reports.
extern "C" __declspec(dllexport) int jgrtComProgId(intptr_t module, wchar_t* buffer, int capacity)
{
    if (module == 0)
    {
        return 0;
    }

    const std::wstring& progId = reinterpret_cast<Module*>(module)->progId;
    if (buffer == nullptr || capacity <= 0)
    {
        return static_cast<int>(progId.size());
    }

    int n = static_cast<int>(progId.size()) < capacity ? static_cast<int>(progId.size()) : capacity;
    memcpy(buffer, progId.data(), static_cast<size_t>(n) * sizeof(wchar_t));
    return n;
}
