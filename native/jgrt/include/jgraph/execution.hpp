/*
 * jgraph/execution.hpp - calling a C++ shared library's functions as the JGraph Runtime does it
 * (compiler and SDK plan, stage K12): feval and fevalAsync with the arrays of jgraph/data_array.hpp
 * or with C++'s own values, the futures they answer, and the exceptions they raise.
 *
 * MathWorks' names in the namespace jgraph::execution (decision KO3); the text is JGraph's own.
 * What a call answers, refuses and writes follows R2025b's runtime as record-k12.ps1 measured it
 * (recordings\k12_client.out.txt, calls). A call goes to the engine in jgrt.dll and runs after the
 * calls started before it; its arrays cross in JGraph's own binary form, written and read here.
 */
#ifndef JGRAPH_EXECUTION_HPP
#define JGRAPH_EXECUTION_HPP

#include "data_array.hpp"
#include "jgrt_cpp.h"

#include <chrono>
#include <functional>
#include <future>
#include <map>
#include <streambuf>
#include <tuple>

namespace jgraph {
namespace execution {

/// Where a call's output or error text goes.
typedef std::basic_streambuf<char16_t> StreamBuffer;

/// What every exception a call raises derives from.
class Exception : public std::exception, public jgraph::Exception {
public:
    Exception() = default;
    Exception(const Exception&) = default;
    Exception(Exception&&) = default;
    Exception& operator=(const Exception&) = default;
    Exception& operator=(Exception&&) = default;
    virtual ~Exception() = default;
    Exception(const std::string& msg) : message(msg) {}
    virtual const char* what() const noexcept override { return message.c_str(); }

private:
    std::string message;
};

/// The runtime has ended.
class MATLABNotAvailableException final : public Exception {
public:
    MATLABNotAvailableException() {}
    MATLABNotAvailableException(const std::string& msg) : Exception(msg) {}
};

/// The call was cancelled before it started.
class CancelledException final : public Exception {
public:
    CancelledException() {}
    CancelledException(const std::string& msg) : Exception(msg) {}
};

/// The call was interrupted while it ran.
class InterruptedException final : public Exception {
public:
    InterruptedException() {}
    InterruptedException(const std::string& msg) : Exception(msg) {}
};

/// One frame of a MATLAB error's stack: its file, function and line.
class StackFrame final {
public:
    StackFrame() : lineNumber(0) {}
    StackFrame(const std::u16string& file, const std::u16string& func, uint64_t line) : fileName(file), funcName(func), lineNumber(line) {}
    std::u16string getFileName() const { return fileName; }
    std::u16string getFunctionName() const { return funcName; }
    uint64_t getLineNumber() const { return lineNumber; }

private:
    std::u16string fileName;
    std::u16string funcName;
    uint64_t lineNumber;
};

/// A MATLAB error: its identifier and message.
class MATLABException : public Exception {
public:
    MATLABException() {}
    MATLABException(const std::string& msg) : Exception(msg) {}
    MATLABException(const std::string& id, const std::u16string& txt) : Exception(data::detail::Utf8Of(txt)), errorID(id), errorText(txt) {}
    std::string getMessageID() const { return errorID; }
    std::u16string getMessageText() const { return errorText; }
    virtual ~MATLABException() {}
    MATLABException(const MATLABException& rhs) = default;
    MATLABException& operator=(const MATLABException& rhs) = default;

private:
    std::string errorID;
    std::u16string errorText;
};

/// A syntax error in the code a call ran.
class MATLABSyntaxException final : public MATLABException {
public:
    MATLABSyntaxException(const std::string& msg) : MATLABException(msg) {}
    MATLABSyntaxException(const std::string& id, const std::u16string& txt) : MATLABException(id, txt) {}
};

/// An error a called function raised: its stack, and the errors that caused it.
class MATLABExecutionException final : public MATLABException {
public:
    MATLABExecutionException() {}
    MATLABExecutionException(const std::string& msg, const std::vector<StackFrame>& trace) : MATLABException(msg), stackTrace(trace) {}
    MATLABExecutionException(const std::string& id, const std::u16string& txt, const std::vector<StackFrame>& trace,
                             const std::vector<MATLABExecutionException>& cause)
        : MATLABException(id, txt), stackTrace(trace), errorCause(cause) {}
    std::vector<StackFrame> getStackTrace() const { return stackTrace; }
    std::vector<MATLABExecutionException> getCause() const { return errorCause; }
    void setCause(const std::vector<MATLABExecutionException>& cause) { errorCause = cause; }

private:
    std::vector<StackFrame> stackTrace;
    std::vector<MATLABExecutionException> errorCause;
};

/// A result that is not the C++ type it was asked for.
class TypeConversionException final : public Exception {
public:
    TypeConversionException() {}
    TypeConversionException(const std::string& msg) : Exception(msg) {}
};

/// UTF-8 as UTF-16; text that does not decode is refused.
inline std::u16string convertUTF8StringToUTF16String(const std::string& utf8string) {
    std::u16string out = data::detail::FromUtf8(utf8string);
    if (data::detail::Utf8Of(out) != utf8string) {
        throw TypeConversionException("The input cannot be converted to the requested type.");
    }
    return out;
}

/// UTF-16 as UTF-8; a lone surrogate is refused.
inline std::string convertUTF16StringToUTF8String(const std::u16string& utf16string) {
    for (size_t i = 0; i < utf16string.size(); i++) {
        char16_t c = utf16string[i];
        if (c >= 0xD800 && c < 0xDC00) {
            if (i + 1 >= utf16string.size() || utf16string[i + 1] < 0xDC00 || utf16string[i + 1] >= 0xE000) {
                throw TypeConversionException("The input cannot be converted to the requested type.");
            }
            i++;
        } else if (c >= 0xDC00 && c < 0xE000) {
            throw TypeConversionException("The input cannot be converted to the requested type.");
        }
    }
    return data::detail::Utf8Of(utf16string);
}

// --- Tasks and futures -----------------------------------------------------------------------------

/// A call's task: cancelled through it, forgotten when the last future on it goes.
class TaskReference {
public:
    TaskReference() : handle(0) {}
    TaskReference(std::function<bool(uintptr_t, bool)>&& cancel) : handle(0), cancelImpl(std::move(cancel)) {}
    TaskReference(uintptr_t aHandle, std::function<bool(uintptr_t, bool)>&& cancel) : handle(aHandle), cancelImpl(std::move(cancel)) {}
    uintptr_t getHandle() const { return handle; }
    virtual ~TaskReference() {
        if (handle != 0) {
            jgrtCppForget(handle);
        }
    }
    virtual bool cancel(bool allowInterrupt) { return cancelImpl ? cancelImpl(handle, allowInterrupt) : false; }

private:
    TaskReference(TaskReference& handle) = delete;
    TaskReference& operator=(TaskReference&) = delete;
    uintptr_t handle;
    std::function<bool(uintptr_t, bool)> cancelImpl;
};

template <class T>
class SharedFutureResult;

/// What fevalAsync answers: the call's result when it is done, and the way to cancel it.
template <class T>
class FutureResult : public std::future<T> {
public:
    FutureResult() {}
    ~FutureResult() {}
    void swap(FutureResult<T>& a_futureresult) {
        std::swap(future, a_futureresult.future);
        taskReference.swap(a_futureresult.taskReference);
        std::swap(output, a_futureresult.output);
        std::swap(error, a_futureresult.error);
    }
    FutureResult(std::future<T>&& a_future) : future(std::move(a_future)) {}
    FutureResult(std::future<T>&& a_future, std::shared_ptr<TaskReference> a_taskreference)
        : future(std::move(a_future)), taskReference(std::move(a_taskreference)) {}
    FutureResult(std::future<T>&& a_future, const std::shared_ptr<TaskReference>& a_taskreference, const std::shared_ptr<StreamBuffer>& outputBuffer,
                 const std::shared_ptr<StreamBuffer>& errorBuffer)
        : future(std::move(a_future)), taskReference(a_taskreference), output(outputBuffer), error(errorBuffer) {}
    FutureResult(FutureResult<T>&& a_futureresult) noexcept { swap(a_futureresult); }
    FutureResult<T>& operator=(FutureResult<T>&& rhs) noexcept {
        swap(rhs);
        return *this;
    }
    SharedFutureResult<T> share() { return SharedFutureResult<T>(std::move(*this)); }
    T get() { return future.get(); }
    bool valid() const { return future.valid(); }
    void wait() const { future.wait(); }
    template <class Clock, class Duration>
    std::future_status wait_until(const std::chrono::time_point<Clock, Duration>& abs_time) const {
        return future.wait_until(abs_time);
    }
    template <class Rep, class Period>
    std::future_status wait_for(const std::chrono::duration<Rep, Period>& rel_time) const {
        return future.wait_for(rel_time);
    }
    bool cancel(bool allowInterrupt = true) { return taskReference ? taskReference->cancel(allowInterrupt) : false; }
    std::shared_ptr<TaskReference> getTaskReference() { return taskReference; }

private:
    FutureResult(std::future<T>&) = delete;
    FutureResult(const FutureResult&) = delete;
    FutureResult& operator=(FutureResult&) = delete;
    std::future<T> future;
    std::shared_ptr<TaskReference> taskReference;
    std::weak_ptr<StreamBuffer> output;
    std::weak_ptr<StreamBuffer> error;
    friend class SharedFutureResult<T>;
};

/// A future several holders can wait on and read.
template <class T>
class SharedFutureResult : public std::shared_future<T> {
public:
    SharedFutureResult() {}
    ~SharedFutureResult() {}
    void swap(SharedFutureResult<T>& a_sharedfuture) {
        std::swap(sharedFuture, a_sharedfuture.sharedFuture);
        taskReference.swap(a_sharedfuture.taskReference);
    }
    SharedFutureResult(const SharedFutureResult& a_sharedfuture) : sharedFuture(a_sharedfuture.sharedFuture), taskReference(a_sharedfuture.taskReference) {}
    SharedFutureResult(SharedFutureResult&& a_sharedfuture) { swap(a_sharedfuture); }
    SharedFutureResult(FutureResult<T>&& a_futureresult) : sharedFuture(std::move(a_futureresult.future)), taskReference(std::move(a_futureresult.taskReference)) {}
    SharedFutureResult<T>& operator=(SharedFutureResult<T>&& rhs) {
        swap(rhs);
        return *this;
    }
    SharedFutureResult<T>& operator=(const SharedFutureResult<T>& rhs) {
        sharedFuture = rhs.sharedFuture;
        taskReference = rhs.taskReference;
        return *this;
    }
    decltype(std::declval<std::shared_future<T>>().get()) get() const { return sharedFuture.get(); }
    bool valid() const { return sharedFuture.valid(); }
    void wait() const { sharedFuture.wait(); }
    template <class Clock, class Duration>
    std::future_status wait_until(const std::chrono::time_point<Clock, Duration>& abs_time) const {
        return sharedFuture.wait_until(abs_time);
    }
    template <class Rep, class Period>
    std::future_status wait_for(const std::chrono::duration<Rep, Period>& rel_time) const {
        return sharedFuture.wait_for(rel_time);
    }
    bool cancel(bool allowInterrupt = true) { return taskReference ? taskReference->cancel(allowInterrupt) : false; }

private:
    std::shared_future<T> sharedFuture;
    std::shared_ptr<TaskReference> taskReference;
};

// --- Arrays in the engine's binary form --------------------------------------------------------------

namespace detail {

using data::Array;
using data::ArrayType;
namespace dd = data::detail;

/// Writes arrays in the form the engine reads: each its type, dimensions and elements in
/// column-major order (a row-major array's read out by column), recursively for cells and structs.
class Writer {
public:
    std::vector<unsigned char> bytes;

    void Write(const Array& a) {
        const std::shared_ptr<data::impl::ArrayImpl>& held = dd::Access::getImpl(a);
        if (!held) {
            Write(dd::EmptyDouble());
            return;
        }
        const data::impl::ArrayImpl& s = *held;
        Byte(static_cast<unsigned char>(s.type));
        U32(static_cast<uint32_t>(s.dims.size()));
        for (size_t d : s.dims) {
            U64(d);
        }
        size_t n = data::getNumElements(s.dims);
        if (dd::IsSparse(s.type)) {
            U64(s.count);
            for (size_t r : s.rows) {
                U64(r);
            }
            for (size_t c : s.columnStarts) {
                U64(c);
            }
            Raw(s.data, s.count * s.dense->size);
            return;
        }
        if (s.dense != nullptr) {
            if (s.layout == data::MemoryLayout::ROW_MAJOR && n > 1) {
                const unsigned char* from = static_cast<const unsigned char*>(s.data);
                for (size_t k = 0; k < n; k++) {
                    Raw(from + dd::RowPlaceOfColumnOrder(s.dims, k) * s.dense->size, s.dense->size);
                }
            } else {
                Raw(s.data, n * s.dense->size);
            }
            return;
        }
        switch (s.type) {
        case ArrayType::MATLAB_STRING:
            for (const data::MATLABString& e : s.strings) {
                Byte(e.has_value() ? 1 : 0);
                if (e.has_value()) {
                    Text16(*e);
                }
            }
            break;
        case ArrayType::CELL:
            for (const Array& e : s.elements) {
                Write(e);
            }
            break;
        case ArrayType::STRUCT:
            U32(static_cast<uint32_t>(s.fields.size()));
            for (const std::string& f : s.fields) {
                Text8(f);
            }
            for (const Array& e : s.elements) {
                Write(e);
            }
            break;
        case ArrayType::ENUM:
            Text8(s.className);
            for (const std::string& m : s.members) {
                Text8(m);
            }
            break;
        default:
            if (dd::IsObject(s.type)) {
                Text8(s.className);
                for (const data::Object& o : s.objects) {
                    const std::shared_ptr<data::impl::ObjectImpl>& oi = dd::Access::getImpl(o);
                    U64(oi && oi->value ? oi->value->handle() : 0);
                    U64(oi ? oi->index : 0);
                }
            }
            break;
        }
    }

    void Byte(unsigned char b) { bytes.push_back(b); }
    void U32(uint32_t v) { Raw(&v, sizeof v); }
    void U64(uint64_t v) { Raw(&v, sizeof v); }
    void Raw(const void* p, size_t n) {
        const unsigned char* b = static_cast<const unsigned char*>(p);
        bytes.insert(bytes.end(), b, b + n);
    }
    void Text8(const std::string& s) {
        U32(static_cast<uint32_t>(s.size()));
        Raw(s.data(), s.size());
    }
    void Text16(const std::u16string& s) {
        U32(static_cast<uint32_t>(s.size()));
        Raw(s.data(), s.size() * sizeof(char16_t));
    }
};

/// Reads what the engine wrote: arrays, and error records.
class Reader {
public:
    Reader(const unsigned char* data, size_t length) : p(data), end(data + length) {}

    Array Read() {
        ArrayType type = static_cast<ArrayType>(Byte());
        uint32_t nd = U32();
        data::ArrayDimensions dims(nd);
        for (uint32_t k = 0; k < nd; k++) {
            dims[k] = static_cast<size_t>(U64());
        }
        size_t n = data::getNumElements(dims);
        if (dd::IsSparse(type)) {
            std::shared_ptr<data::impl::ArrayImpl> a = dd::NewStorage(type, dims);
            size_t nnz = static_cast<size_t>(U64());
            a->rows.resize(nnz);
            for (size_t k = 0; k < nnz; k++) {
                a->rows[k] = static_cast<size_t>(U64());
            }
            for (size_t& c : a->columnStarts) {
                c = static_cast<size_t>(U64());
            }
            a->count = nnz;
            a->data = a->dense->make(nnz);
            RawInto(a->data, nnz * a->dense->size);
            return dd::Access::createObj<Array>(std::move(a));
        }
        if (type == ArrayType::STRUCT) {
            uint32_t nf = U32();
            std::vector<std::string> fields(nf);
            for (std::string& f : fields) {
                f = Text8();
            }
            std::shared_ptr<data::impl::ArrayImpl> a = dd::NewStorage(type, dims, nf);
            a->fields = std::move(fields);
            for (Array& e : a->elements) {
                e = Read();
            }
            return dd::Access::createObj<Array>(std::move(a));
        }
        std::shared_ptr<data::impl::ArrayImpl> a = dd::NewStorage(dd::IsObject(type) ? ArrayType::OBJECT : type, dims);
        a->type = type;
        if (a->dense != nullptr) {
            RawInto(a->data, n * a->dense->size);
            return dd::Access::createObj<Array>(std::move(a));
        }
        switch (type) {
        case ArrayType::MATLAB_STRING:
            for (data::MATLABString& e : a->strings) {
                if (Byte() != 0) {
                    e = Text16();
                }
            }
            break;
        case ArrayType::CELL:
            for (Array& e : a->elements) {
                e = Read();
            }
            break;
        case ArrayType::ENUM:
            a->className = Text8();
            for (std::string& m : a->members) {
                m = Text8();
            }
            break;
        default:
            if (dd::IsObject(type)) {
                a->className = Text8();
                a->objects.reserve(n);
                for (size_t k = 0; k < n; k++) {
                    uint64_t handle = U64();
                    uint64_t index = U64();
                    std::shared_ptr<data::impl::EngineValue>& value = values[handle];
                    if (!value) {
                        value = std::make_shared<data::impl::EngineValue>(handle, &jgrtCppRelease);
                    }
                    a->objects.push_back(dd::Access::createObject(
                        std::make_shared<data::impl::ObjectImpl>(value, static_cast<size_t>(index), type, a->className)));
                }
            }
            break;
        }
        return dd::Access::createObj<Array>(std::move(a));
    }

    /// A MATLAB error: identifier, message, stack and causes.
    MATLABExecutionException Error() {
        std::string id = Text8();
        std::u16string message = Text16();
        uint32_t frames = U32();
        std::vector<StackFrame> stack;
        for (uint32_t k = 0; k < frames; k++) {
            std::u16string file = Text16();
            std::u16string name = Text16();
            uint64_t line = U64();
            stack.push_back(StackFrame(file, name, line));
        }
        uint32_t causes = U32();
        std::vector<MATLABExecutionException> cause;
        for (uint32_t k = 0; k < causes; k++) {
            cause.push_back(Error());
        }
        return MATLABExecutionException(id, message, stack, cause);
    }

    unsigned char Byte() {
        Need(1);
        return *p++;
    }
    uint32_t U32() {
        uint32_t v;
        RawInto(&v, sizeof v);
        return v;
    }
    uint64_t U64() {
        uint64_t v;
        RawInto(&v, sizeof v);
        return v;
    }
    void RawInto(void* to, size_t n) {
        Need(n);
        if (n != 0) {
            std::memcpy(to, p, n);
        }
        p += n;
    }
    std::string Text8() {
        uint32_t n = U32();
        Need(n);
        std::string s(reinterpret_cast<const char*>(p), n);
        p += n;
        return s;
    }
    std::u16string Text16() {
        uint32_t n = U32();
        std::u16string s(n, u'\0');
        RawInto(&s[0], n * sizeof(char16_t));
        return s;
    }

private:
    void Need(size_t n) const {
        if (static_cast<size_t>(end - p) < n) {
            throw Exception("The JGraph Runtime answered a call with a damaged result.");
        }
    }
    const unsigned char* p;
    const unsigned char* end;
    std::map<uint64_t, std::shared_ptr<data::impl::EngineValue>> values;
};

/// Text the runtime gave, taken and freed.
inline std::u16string Taken(char16_t* text) {
    std::u16string s = text == nullptr ? std::u16string() : std::u16string(text);
    if (text != nullptr) {
        jgrtCppFree(text);
    }
    return s;
}

/// The exception a call that did not finish ends with.
inline std::exception_ptr Failure(int status, const unsigned char* data, size_t length) {
    Reader r(data, length);
    switch (status) {
    case 1: return std::make_exception_ptr(CancelledException(r.Text8()));
    case 2: return std::make_exception_ptr(InterruptedException(r.Text8()));
    case 3: return std::make_exception_ptr(r.Error());
    case 4: {
        MATLABExecutionException e = r.Error();
        return std::make_exception_ptr(MATLABSyntaxException(e.getMessageID(), e.getMessageText()));
    }
    case 6: return std::make_exception_ptr(MATLABNotAvailableException(r.Text8()));
    case 7: return std::make_exception_ptr(std::runtime_error(r.Text8())); // a name refused once the call ran, as one refused before it
    default: return std::make_exception_ptr(Exception(r.Text8()));
    }
}

/// One call in flight: where its text goes, and what to do when it ends.
struct Call {
    std::shared_ptr<StreamBuffer> output;
    std::shared_ptr<StreamBuffer> error;
    std::function<void(int, const unsigned char*, size_t)> finish;
};

inline void WriteText(void* context, int stream, const char16_t* text, size_t length) {
    Call* call = static_cast<Call*>(context);
    const std::shared_ptr<StreamBuffer>& to = stream == 1 ? call->output : call->error;
    if (to) {
        to->sputn(text, static_cast<std::streamsize>(length));
    }
}

inline void Finished(void* context, int status, const unsigned char* data, size_t length) {
    std::unique_ptr<Call> call(static_cast<Call*>(context));
    try {
        call->finish(status, data, length);
    } catch (...) {
        // Nothing may leave a callback into C; finish sets the promise, and a damaged result already did.
    }
}

inline bool Cancel(uintptr_t task, bool allowInterrupt) { return jgrtCppCancel(task, allowInterrupt ? 1 : 0) != 0; }

/// Starts a call; the promise is kept until it ends. A name the library cannot call is refused here,
/// with R2025b's words (calls: builtin.sqrt), before anything runs.
template <typename Promise, typename Make>
inline std::shared_ptr<TaskReference> Start(uint64_t library, const std::u16string& function, size_t nlhs, const std::vector<Array>& args,
                                            const std::shared_ptr<StreamBuffer>& output, const std::shared_ptr<StreamBuffer>& error,
                                            std::shared_ptr<Promise> promise, Make make) {
    Writer w;
    for (const Array& a : args) {
        w.Write(a);
    }
    std::unique_ptr<Call> call(new Call());
    call->output = output;
    call->error = error;
    call->finish = [promise, make, nlhs](int status, const unsigned char* data, size_t length) {
        if (status != 0) {
            promise->set_exception(Failure(status, data, length));
            return;
        }
        try {
            Reader r(data, length);
            std::vector<Array> results;
            for (size_t k = 0; k < nlhs; k++) {
                results.push_back(r.Read());
            }
            make(*promise, std::move(results));
        } catch (...) {
            promise->set_exception(std::current_exception());
        }
    };
    char16_t* refused = nullptr;
    uint64_t task = jgrtCppCall(library, function.c_str(), nlhs, w.bytes.empty() ? nullptr : w.bytes.data(), w.bytes.size(), call.get(),
                                &WriteText, output ? 1 : 0, error ? 1 : 0, &Finished, &refused);
    if (task == 0) {
        throw std::runtime_error(data::detail::Utf8Of(Taken(refused)));
    }
    call.release();
    return std::make_shared<TaskReference>(static_cast<uintptr_t>(task), &Cancel);
}

} // namespace detail

/// A library's functions, called with arrays or with C++'s own values.
class ExecutionInterface {
public:
    std::vector<data::Array> feval(const std::u16string& function, const size_t nlhs, const std::vector<data::Array>& args,
                                   const std::shared_ptr<StreamBuffer>& output = std::shared_ptr<StreamBuffer>(),
                                   const std::shared_ptr<StreamBuffer>& error = std::shared_ptr<StreamBuffer>()) {
        return fevalAsync(function, nlhs, args, output, error).get();
    }
    std::vector<data::Array> feval(const std::string& function, const size_t nlhs, const std::vector<data::Array>& args,
                                   const std::shared_ptr<StreamBuffer>& output = std::shared_ptr<StreamBuffer>(),
                                   const std::shared_ptr<StreamBuffer>& error = std::shared_ptr<StreamBuffer>()) {
        return feval(Wide(function), nlhs, args, output, error);
    }
    data::Array feval(const std::u16string& function, const std::vector<data::Array>& args,
                      const std::shared_ptr<StreamBuffer>& output = std::shared_ptr<StreamBuffer>(),
                      const std::shared_ptr<StreamBuffer>& error = std::shared_ptr<StreamBuffer>()) {
        return fevalAsync(function, args, output, error).get();
    }
    data::Array feval(const std::string& function, const std::vector<data::Array>& args,
                      const std::shared_ptr<StreamBuffer>& output = std::shared_ptr<StreamBuffer>(),
                      const std::shared_ptr<StreamBuffer>& error = std::shared_ptr<StreamBuffer>()) {
        return feval(Wide(function), args, output, error);
    }
    data::Array feval(const std::u16string& function, const data::Array& arg, const std::shared_ptr<StreamBuffer>& output = std::shared_ptr<StreamBuffer>(),
                      const std::shared_ptr<StreamBuffer>& error = std::shared_ptr<StreamBuffer>()) {
        return feval(function, std::vector<data::Array>({arg}), output, error);
    }
    data::Array feval(const std::string& function, const data::Array& arg, const std::shared_ptr<StreamBuffer>& output = std::shared_ptr<StreamBuffer>(),
                      const std::shared_ptr<StreamBuffer>& error = std::shared_ptr<StreamBuffer>()) {
        return feval(Wide(function), arg, output, error);
    }

    template <class ReturnType, typename... RhsArgs>
    ReturnType feval(const std::u16string& function, const std::shared_ptr<StreamBuffer>& output, const std::shared_ptr<StreamBuffer>& error,
                     RhsArgs&&... rhsArgs);
    template <class ReturnType, typename... RhsArgs>
    ReturnType feval(const std::string& function, const std::shared_ptr<StreamBuffer>& output, const std::shared_ptr<StreamBuffer>& error,
                     RhsArgs&&... rhsArgs);
    template <class ReturnType, typename... RhsArgs>
    ReturnType feval(const std::u16string& function, RhsArgs&&... rhsArgs);
    template <class ReturnType, typename... RhsArgs>
    ReturnType feval(const std::string& function, RhsArgs&&... rhsArgs);

    FutureResult<std::vector<data::Array>> fevalAsync(const std::u16string& function, const size_t nlhs, const std::vector<data::Array>& args,
                                                      const std::shared_ptr<StreamBuffer>& output = std::shared_ptr<StreamBuffer>(),
                                                      const std::shared_ptr<StreamBuffer>& error = std::shared_ptr<StreamBuffer>()) {
        typedef std::promise<std::vector<data::Array>> Promise;
        std::shared_ptr<Promise> promise = std::make_shared<Promise>();
        std::future<std::vector<data::Array>> f = promise->get_future();
        std::shared_ptr<TaskReference> task = detail::Start(matlabHandle, function, nlhs, args, output, error, promise,
                                                            [](Promise& p, std::vector<data::Array>&& results) { p.set_value(std::move(results)); });
        return FutureResult<std::vector<data::Array>>(std::move(f), task, output, error);
    }
    FutureResult<std::vector<data::Array>> fevalAsync(const std::string& function, const size_t nlhs, const std::vector<data::Array>& args,
                                                      const std::shared_ptr<StreamBuffer>& output = std::shared_ptr<StreamBuffer>(),
                                                      const std::shared_ptr<StreamBuffer>& error = std::shared_ptr<StreamBuffer>()) {
        return fevalAsync(Wide(function), nlhs, args, output, error);
    }
    FutureResult<data::Array> fevalAsync(const std::u16string& function, const std::vector<data::Array>& args,
                                         const std::shared_ptr<StreamBuffer>& output = std::shared_ptr<StreamBuffer>(),
                                         const std::shared_ptr<StreamBuffer>& error = std::shared_ptr<StreamBuffer>()) {
        typedef std::promise<data::Array> Promise;
        std::shared_ptr<Promise> promise = std::make_shared<Promise>();
        std::future<data::Array> f = promise->get_future();
        std::shared_ptr<TaskReference> task = detail::Start(matlabHandle, function, 1, args, output, error, promise,
                                                            [](Promise& p, std::vector<data::Array>&& results) { p.set_value(results.at(0)); });
        return FutureResult<data::Array>(std::move(f), task, output, error);
    }
    FutureResult<data::Array> fevalAsync(const std::string& function, const std::vector<data::Array>& args,
                                         const std::shared_ptr<StreamBuffer>& output = std::shared_ptr<StreamBuffer>(),
                                         const std::shared_ptr<StreamBuffer>& error = std::shared_ptr<StreamBuffer>()) {
        return fevalAsync(Wide(function), args, output, error);
    }
    FutureResult<data::Array> fevalAsync(const std::u16string& function, const data::Array& arg,
                                         const std::shared_ptr<StreamBuffer>& output = std::shared_ptr<StreamBuffer>(),
                                         const std::shared_ptr<StreamBuffer>& error = std::shared_ptr<StreamBuffer>()) {
        return fevalAsync(function, std::vector<data::Array>({arg}), output, error);
    }
    FutureResult<data::Array> fevalAsync(const std::string& function, const data::Array& arg,
                                         const std::shared_ptr<StreamBuffer>& output = std::shared_ptr<StreamBuffer>(),
                                         const std::shared_ptr<StreamBuffer>& error = std::shared_ptr<StreamBuffer>()) {
        return fevalAsync(Wide(function), arg, output, error);
    }

    template <class ReturnType, typename... RhsArgs>
    FutureResult<ReturnType> fevalAsync(const std::u16string& function, const std::shared_ptr<StreamBuffer>& output,
                                        const std::shared_ptr<StreamBuffer>& error, RhsArgs&&... rhsArgs);
    template <class ReturnType, typename... RhsArgs>
    FutureResult<ReturnType> fevalAsync(const std::string& function, const std::shared_ptr<StreamBuffer>& output,
                                        const std::shared_ptr<StreamBuffer>& error, RhsArgs&&... rhsArgs);
    template <class ReturnType, typename... RhsArgs>
    FutureResult<ReturnType> fevalAsync(const std::u16string& function, RhsArgs&&... rhsArgs);
    template <class ReturnType, typename... RhsArgs>
    FutureResult<ReturnType> fevalAsync(const std::string& function, RhsArgs&&... rhsArgs);

    ExecutionInterface(uint64_t handle, bool inProc = false) : matlabHandle(handle), isInProc(inProc) {}
    ~ExecutionInterface() { matlabHandle = 0; }

protected:
    uint64_t matlabHandle;

private:
    static std::u16string Wide(const std::string& s) { return std::u16string(s.begin(), s.end()); }
    bool isInProc;
};

// --- C++'s own values in and out ------------------------------------------------------------------

namespace detail {

template <typename T>
struct is_vector_t : std::false_type {};
template <typename T, typename A>
struct is_vector_t<std::vector<T, A>> : std::true_type {};

/// The C++ values feval takes and answers besides arrays: numbers, complex numbers, logicals,
/// characters, text, and vectors of them.
template <typename T>
inline constexpr bool isSupportedStandardCPPType() {
    typedef typename std::remove_cv<typename std::remove_reference<T>::type>::type U;
    return std::is_same<U, bool>::value || std::is_same<U, int>::value || std::is_same<U, int8_t>::value || std::is_same<U, int16_t>::value ||
           std::is_same<U, int32_t>::value || std::is_same<U, int64_t>::value || std::is_same<U, uint8_t>::value ||
           std::is_same<U, uint16_t>::value || std::is_same<U, uint32_t>::value || std::is_same<U, uint64_t>::value ||
           std::is_same<U, char>::value || std::is_convertible<U, std::basic_string<char>>::value ||
           std::is_convertible<U, std::basic_string<char16_t>>::value || std::is_same<U, float>::value || std::is_same<U, double>::value ||
           data::is_complex<U>::value || is_vector_t<U>::value;
}

/// An input as the array MATLAB gets: a number as its 1-by-1 array, text as a string scalar, a
/// vector as a row, an array as it is.
inline data::Array CreateRhs(data::ArrayFactory& f, const data::Array& value) { return value; }
inline data::Array CreateRhs(data::ArrayFactory& f, const std::string& value) { return f.createScalar(value); }
inline data::Array CreateRhs(data::ArrayFactory& f, const char* value) { return f.createScalar(std::string(value)); }
inline data::Array CreateRhs(data::ArrayFactory& f, const std::u16string& value) { return f.createScalar(value); }
inline data::Array CreateRhs(data::ArrayFactory& f, const char16_t* value) { return f.createScalar(std::u16string(value)); }
inline data::Array CreateRhs(data::ArrayFactory& f, char value) { return f.createCharArray(std::string(1, value)); }

template <typename T>
inline typename std::enable_if<std::is_arithmetic<T>::value || data::is_complex<T>::value, data::Array>::type CreateRhs(data::ArrayFactory& f,
                                                                                                                      const T& value) {
    return f.createArray<T>({1, 1}, {value});
}

template <typename T, typename A>
inline data::Array CreateRhs(data::ArrayFactory& f, const std::vector<T, A>& value) {
    return f.createArray({1, value.size()}, value.begin(), value.end());
}

/// A result as the C++ value asked for (createLhs): the first element of the array; text from a
/// string or char array; a vector of the elements; a tuple of several results each as its own type.
template <typename T>
struct Lhs {
    static const size_t nlhs = 1;
    static T From(std::vector<data::Array>&& lhs) {
        if (lhs.empty()) {
            throw TypeConversionException("The result is empty.");
        }
        return One(std::move(lhs.front()));
    }
    static T One(data::Array&& value) {
        try {
            const data::TypedArray<T> typed(std::move(value));
            if (typed.begin() == typed.end()) {
                throw TypeConversionException("The result is empty.");
            }
            return *typed.begin();
        } catch (const TypeConversionException&) {
            throw;
        } catch (const std::exception& e) {
            throw TypeConversionException(e.what());
        }
    }
};

template <>
struct Lhs<void> {
    static const size_t nlhs = 0;
    static void From(std::vector<data::Array>&&) {}
};

template <>
struct Lhs<data::Array> {
    static const size_t nlhs = 1;
    static data::Array From(std::vector<data::Array>&& lhs) {
        if (lhs.empty()) {
            throw TypeConversionException("The result is empty.");
        }
        return lhs[0];
    }
    static data::Array One(data::Array&& value) { return value; }
};

/// A string element or a char array as std::string (ASCII) or std::u16string.
template <typename S>
struct TextOf;

template <>
struct TextOf<std::string> {
    static std::string Element(const data::TypedArray<data::MATLABString>& strings, size_t k) {
        std::string s = strings[k];
        return s;
    }
    static std::string Chars(const data::CharArray& c) { return c.toAscii(); }
};

template <>
struct TextOf<std::u16string> {
    static std::u16string Element(const data::TypedArray<data::MATLABString>& strings, size_t k) {
        data::String s = strings[k];
        return s;
    }
    static std::u16string Chars(const data::CharArray& c) { return c.toUTF16(); }
};

/// Text from a string array's first element or a char array's characters; anything else refused.
template <typename S>
struct TextLhs {
    static const size_t nlhs = 1;
    static S From(std::vector<data::Array>&& lhs) {
        if (lhs.empty()) {
            throw TypeConversionException("The result is empty.");
        }
        return One(std::move(lhs.front()));
    }
    static S One(data::Array&& value) {
        try {
            if (value.getType() == data::ArrayType::MATLAB_STRING) {
                const data::TypedArray<data::MATLABString> strings(std::move(value));
                if (strings.getNumberOfElements() == 0) {
                    throw TypeConversionException("The result is empty.");
                }
                return TextOf<S>::Element(strings, 0);
            }
            if (value.getType() == data::ArrayType::CHAR) {
                return TextOf<S>::Chars(data::CharArray(std::move(value)));
            }
            throw data::InvalidArrayTypeException("The return value is not a string (or) char.");
        } catch (const TypeConversionException&) {
            throw;
        } catch (const std::exception& e) {
            throw TypeConversionException(e.what());
        }
    }
};

template <>
struct Lhs<std::string> : TextLhs<std::string> {};
template <>
struct Lhs<std::u16string> : TextLhs<std::u16string> {};

/// Text from every element of string arrays, or each char array whole.
template <typename S>
struct TextsLhs {
    static const size_t nlhs = 1;
    static std::vector<S> From(std::vector<data::Array>&& lhs) {
        if (lhs.empty()) {
            throw TypeConversionException("The result is empty.");
        }
        std::vector<S> out;
        try {
            for (data::Array& a : lhs) {
                if (a.getType() == data::ArrayType::MATLAB_STRING) {
                    const data::TypedArray<data::MATLABString> strings(a);
                    for (size_t k = 0; k < strings.getNumberOfElements(); k++) {
                        out.push_back(TextOf<S>::Element(strings, k));
                    }
                } else if (a.getType() == data::ArrayType::CHAR) {
                    out.push_back(TextOf<S>::Chars(data::CharArray(a)));
                } else {
                    throw data::InvalidArrayTypeException("The return value is not a string (or) char.");
                }
            }
        } catch (const std::exception& e) {
            throw TypeConversionException(e.what());
        }
        return out;
    }
};

template <>
struct Lhs<std::vector<std::string>> : TextsLhs<std::string> {};
template <>
struct Lhs<std::vector<std::u16string>> : TextsLhs<std::u16string> {};

template <typename T, typename A>
struct Lhs<std::vector<T, A>> {
    static const size_t nlhs = 1;
    static std::vector<T, A> From(std::vector<data::Array>&& lhs) {
        if (lhs.empty()) {
            throw TypeConversionException("The result is empty.");
        }
        return One(std::move(lhs.front()));
    }
    static std::vector<T, A> One(data::Array&& value) {
        try {
            data::TypedArray<T> typed(std::move(value));
            std::vector<T, A> out;
            for (auto e : typed) {
                out.push_back(e);
            }
            return out;
        } catch (const std::exception& e) {
            throw TypeConversionException(e.what());
        }
    }
};

/// Several results, each converted as a single result of its own type is (a char array read as
/// text in a tuple too, where R2025b's refuses it: calls, native.tuple).
template <typename... Types>
struct Lhs<std::tuple<Types...>> {
    static const size_t nlhs = sizeof...(Types);
    static std::tuple<Types...> From(std::vector<data::Array>&& lhs) {
        if (lhs.size() < sizeof...(Types)) {
            throw TypeConversionException("The result is empty.");
        }
        return Make(lhs, std::index_sequence_for<Types...>());
    }

private:
    template <size_t... Index>
    static std::tuple<Types...> Make(std::vector<data::Array>& lhs, std::index_sequence<Index...>) {
        return std::tuple<Types...>(Lhs<typename std::remove_cv<typename std::remove_reference<Types>::type>::type>::One(std::move(lhs[Index]))...);
    }
};

/// Sets a call's promise from its results, converted.
template <typename R>
struct Settler {
    void operator()(std::promise<R>& p, std::vector<data::Array>&& results) const { p.set_value(Lhs<R>::From(std::move(results))); }
};

template <>
struct Settler<void> {
    void operator()(std::promise<void>& p, std::vector<data::Array>&&) const { p.set_value(); }
};

} // namespace detail

template <class ReturnType, typename... RhsArgs>
inline FutureResult<ReturnType> ExecutionInterface::fevalAsync(const std::u16string& function, const std::shared_ptr<StreamBuffer>& output,
                                                              const std::shared_ptr<StreamBuffer>& error, RhsArgs&&... rhsArgs) {
    data::ArrayFactory factory;
    std::vector<data::Array> args({detail::CreateRhs(factory, std::forward<RhsArgs>(rhsArgs))...});
    typedef std::promise<ReturnType> Promise;
    std::shared_ptr<Promise> promise = std::make_shared<Promise>();
    std::future<ReturnType> f = promise->get_future();
    std::shared_ptr<TaskReference> task =
        detail::Start(matlabHandle, function, detail::Lhs<ReturnType>::nlhs, args, output, error, promise, detail::Settler<ReturnType>());
    return FutureResult<ReturnType>(std::move(f), task, output, error);
}

template <class ReturnType, typename... RhsArgs>
inline FutureResult<ReturnType> ExecutionInterface::fevalAsync(const std::string& function, const std::shared_ptr<StreamBuffer>& output,
                                                              const std::shared_ptr<StreamBuffer>& error, RhsArgs&&... rhsArgs) {
    return fevalAsync<ReturnType>(Wide(function), output, error, std::forward<RhsArgs>(rhsArgs)...);
}

template <class ReturnType, typename... RhsArgs>
inline FutureResult<ReturnType> ExecutionInterface::fevalAsync(const std::u16string& function, RhsArgs&&... rhsArgs) {
    const std::shared_ptr<StreamBuffer> none;
    return fevalAsync<ReturnType>(function, none, none, std::forward<RhsArgs>(rhsArgs)...);
}

template <class ReturnType, typename... RhsArgs>
inline FutureResult<ReturnType> ExecutionInterface::fevalAsync(const std::string& function, RhsArgs&&... rhsArgs) {
    return fevalAsync<ReturnType>(Wide(function), std::forward<RhsArgs>(rhsArgs)...);
}

template <class ReturnType, typename... RhsArgs>
inline ReturnType ExecutionInterface::feval(const std::u16string& function, const std::shared_ptr<StreamBuffer>& output,
                                            const std::shared_ptr<StreamBuffer>& error, RhsArgs&&... rhsArgs) {
    return fevalAsync<ReturnType>(function, output, error, std::forward<RhsArgs>(rhsArgs)...).get();
}

template <class ReturnType, typename... RhsArgs>
inline ReturnType ExecutionInterface::feval(const std::string& function, const std::shared_ptr<StreamBuffer>& output,
                                            const std::shared_ptr<StreamBuffer>& error, RhsArgs&&... rhsArgs) {
    return feval<ReturnType>(Wide(function), output, error, std::forward<RhsArgs>(rhsArgs)...);
}

template <class ReturnType, typename... RhsArgs>
inline ReturnType ExecutionInterface::feval(const std::u16string& function, RhsArgs&&... rhsArgs) {
    return fevalAsync<ReturnType>(function, std::forward<RhsArgs>(rhsArgs)...).get();
}

template <class ReturnType, typename... RhsArgs>
inline ReturnType ExecutionInterface::feval(const std::string& function, RhsArgs&&... rhsArgs) {
    return feval<ReturnType>(Wide(function), std::forward<RhsArgs>(rhsArgs)...);
}

} // namespace execution
} // namespace jgraph

#endif // JGRAPH_EXECUTION_HPP
