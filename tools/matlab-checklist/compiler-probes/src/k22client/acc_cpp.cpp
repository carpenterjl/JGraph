/*
 * acc_cpp: the acceptance set's C++ client (compiler and SDK plan, stage K22) of the C++ shared
 * library acccpp built from src\k22, through the MATLAB Data API and the strongly typed header
 * acccppv2.hpp generated with it. Built against R2025b's extern\include and its libraries for the
 * recording, and unchanged against the JGraph Runtime's include and jgrt.lib for the comparison.
 * One label<TAB>result line per question. Argument: the folder holding acccpp.ctf.
 */
#include "MatlabCppSharedLib.hpp"
#include "acccppv2.hpp"

#include <cstdio>
#include <exception>
#include <functional>
#include <iostream>
#include <string>
#include <vector>

namespace mc = matlab::cpplib;
namespace md = matlab::data;

static std::string Flat(std::string s)
{
    std::string out;
    for (char c : s)
    {
        if (c == '\n') out += "\\n";
        else if (c == '\r') out += "\\r";
        else if (c == '\t') out += "\\t";
        else out += c;
    }
    return out;
}

static std::string Ascii(const std::u16string &s)
{
    std::string out;
    for (char16_t c : s) out += c < 128 ? static_cast<char>(c) : '?';
    return out;
}

static void Line(const std::string &label, const std::string &text)
{
    std::printf("%s\t%s\n", label.c_str(), text.c_str());
    std::fflush(stdout);
}

static std::string Numbers(const md::Array &a)
{
    md::TypedArray<double> t = a;
    std::string out = "double " + std::to_string(t.getDimensions()[0]) + "x" + std::to_string(t.getDimensions()[1]);
    int shown = 0;
    for (double d : t)
    {
        char buf[64];
        std::snprintf(buf, sizeof buf, " %g", d);
        out += buf;
        if (++shown == 8) break;
    }
    return out;
}

static void Case(const std::string &label, const std::function<std::string()> &f)
{
    std::string text;
    try { text = f(); }
    catch (const std::exception &e) { text = std::string("THREW ") + Flat(e.what()); }
    catch (...) { text = "THREW ?"; }
    Line(label, text);
}

int main(int argc, char **argv)
{
    std::u16string folder;
    if (argc > 1) for (const char *p = argv[1]; *p; p++) folder += static_cast<char16_t>(*p);
    std::u16string ctf = folder.empty() ? u"acccpp.ctf" : folder + u"\\acccpp.ctf";
    md::ArrayFactory f;
    std::shared_ptr<mc::MATLABApplication> app;
    std::shared_ptr<mc::MATLABLibrary> lib;
    Case("start", [&] {
        app = mc::initMATLABApplication(mc::MATLABApplicationMode::IN_PROCESS, std::vector<std::u16string>());
        lib = mc::initMATLABLibrary(app, ctf);
        return std::string("ok");
    });
    if (!lib) return 1;
    Case("quiet", [&] { lib->feval(u"acc_quiet", 0, std::vector<md::Array>()); return std::string("ok"); });
    Case("add", [&] { return Numbers(lib->feval(u"acc_add", std::vector<md::Array>{f.createScalar(2.0), f.createScalar(3.0)})); });
    Case("add.int", [&] { return Numbers(lib->feval(u"acc_add", std::vector<md::Array>{f.createScalar<int32_t>(2), f.createScalar<int32_t>(3)})); });
    Case("stats", [&] {
        md::StructArray s = lib->feval(u"acc_stats", f.createArray<double>({1, 5}, {3, 1, 4, 1, 5}));
        std::string out = "struct";
        for (const auto &name : s.getFieldNames())
        {
            md::TypedArray<double> v = s[0][name];
            double value = v[0];
            char buf[64];
            std::snprintf(buf, sizeof buf, " %s=%g", std::string(name).c_str(), value);
            out += buf;
        }
        return out;
    });
    Case("greet", [&] {
        md::CharArray g = lib->feval(u"acc_greet", f.createCharArray("Ada"));
        return "char " + std::to_string(g.getDimensions()[0]) + "x" + std::to_string(g.getDimensions()[1]) + " <<" + g.toAscii() + ">>";
    });
    Case("scale", [&] { return Numbers(lib->feval(u"acc_scale", std::vector<md::Array>{f.createArray<double>({2, 2}, {1, 3, 2, 4}), f.createScalar(10.0)})); });
    Case("count.1", [&] { return Numbers(lib->feval(u"acc_count", std::vector<md::Array>())); });
    Case("count.2", [&] { return Numbers(lib->feval(u"acc_count", std::vector<md::Array>())); });
    Case("fail", [&] { lib->feval(u"acc_fail", 0, std::vector<md::Array>{f.createCharArray("from C++")}); return std::string("ok"); });
    Case("add.wrongclass", [&] { return Numbers(lib->feval(u"acc_add", std::vector<md::Array>{f.createCharArray("x"), f.createScalar(3.0)})); });
    Case("count.3", [&] { return Numbers(lib->feval(u"acc_count", std::vector<md::Array>())); });

    // The strongly typed header: a double argument is a double or a std::complex<double>, a char one
    // a std::u16string, a matrix a matlab::data::Array, as R2025b's generator writes them.
    Case("typed.add", [&] { return Numbers(acc_add<1>(lib, 2.0, 3.0)); });
    Case("typed.add.complex", [&] { return Numbers(acc_add<1>(lib, std::complex<double>(2, 0), std::complex<double>(3, 0))); });
    Case("typed.stats", [&] {
        md::StructArray s = acc_stats<1>(lib, std::vector<double>{3, 1, 4, 1, 5});
        md::TypedArray<double> n = s[0]["n"];
        double value = n[0];
        return "n=" + std::to_string(static_cast<int>(value));
    });
    Case("typed.greet", [&] { md::CharArray g = acc_greet<1>(lib, u"Ada"); return g.toAscii(); });
    Case("typed.scale", [&] { return Numbers(acc_scale<1>(lib, f.createArray<double>({2, 2}, {1, 3, 2, 4}), 10.0)); });
    Case("typed.count", [&] { return Numbers(acc_count<1>(lib)); });
    Case("typed.quiet", [&] { acc_quiet(lib); return std::string("ok"); });
    Case("typed.fail", [&] { acc_fail(lib, u"typed"); return std::string("ok"); });
    Case("end", [&] { lib.reset(); app.reset(); return std::string("ok"); });
    return 0;
}
