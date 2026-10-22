// Minimal client of the cppSharedLibrary (matlab-data) generic interface.
#include "MatlabCppSharedLib.hpp"
#include "jgcppv2.hpp"
#include <iostream>
#include <chrono>

int main(int argc, char **argv)
{
    namespace mc = matlab::cpplib;
    namespace md = matlab::data;
    auto t0 = std::chrono::steady_clock::now();
    try {
        auto app = mc::initMATLABApplication(mc::MATLABApplicationMode::IN_PROCESS, std::vector<std::u16string>());
        auto lib = mc::initMATLABLibrary(app, u"jgcpp.ctf");
        auto t1 = std::chrono::steady_clock::now();
        std::cout << "init ms=" << std::chrono::duration_cast<std::chrono::milliseconds>(t1 - t0).count() << std::endl;
        md::Array r = typedadd(lib, 1.5, 2.0);
        std::cout << "typedadd(1.5,2.0) type=" << static_cast<int>(r.getType()) << " value=" << double(md::TypedArray<double>(r)[0]) << std::endl;
        md::ArrayFactory f;
        std::vector<md::Array> args = { f.createScalar<double>(10.0), f.createScalar<double>(20.0) };
        md::Array r2 = lib->feval(u"addtwo", args);
        std::cout << "feval addtwo(10,20) value=" << double(md::TypedArray<double>(r2)[0]) << std::endl;
        try {
            std::vector<md::Array> bad = { f.createArray<double>({1, 2}, {1.0, 2.0}), f.createScalar<double>(3.0) };
            lib->feval(u"typedadd", bad);
        } catch (const matlab::execution::MATLABException &e) {
            std::cout << "typedadd([1 2],3) MATLABException id=" << matlab::engine::convertUTF16StringToUTF8String(e.getMessageID())
                      << " msg=" << e.what() << std::endl;
        }
    } catch (const std::exception &e) {
        std::cout << "std::exception: " << e.what() << std::endl;
        return 2;
    }
    return 0;
}
