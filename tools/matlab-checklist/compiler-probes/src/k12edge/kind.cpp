/*
 * kind: the MATLAB Data API read past the end of cells, structs, numbers and strings (compiler and
 * SDK plan, stage K12), naming the exception each read throws and the documented class that catches
 * the first. Built by record-k12-edge.ps1 against R2025b's extern\include and libMatlabDataArray.lib,
 * and unchanged against JGraph's native\jgrt\include, whose MathWorks-named headers call their
 * jgraph namespace matlab too. One name<TAB>outcome line per read.
 */
#include "MatlabDataArray.hpp"
#include <functional>
#include <iostream>
#include <typeinfo>

namespace md = matlab::data;

static void Try(const char* name, const std::function<void()>& body)
{
    try { body(); std::cout << name << "\tok\n"; }
    catch (const std::exception& e) { std::cout << name << "\t" << typeid(e).name() << " | " << e.what() << "\n"; }
}

int main()
{
    std::cout << std::unitbuf;
    md::ArrayFactory f;
    md::CellArray c = f.createCellArray({1, 1});
    c[0] = f.createArray<double>({1, 3}, {4, 5, 6});
    Try("c01.read", [&] { md::Array v = c[0][1]; });
    try { md::Array v = c[0][1]; }
    catch (const std::out_of_range&) { std::cout << "base\tstd::out_of_range\n"; }
    catch (const std::invalid_argument&) { std::cout << "base\tstd::invalid_argument\n"; }
    catch (const std::logic_error&) { std::cout << "base\tstd::logic_error\n"; }
    catch (const std::range_error&) { std::cout << "base\tstd::range_error\n"; }
    catch (const std::runtime_error&) { std::cout << "base\tstd::runtime_error\n"; }
    catch (const matlab::Exception&) { std::cout << "base\tmatlab::Exception\n"; }
    catch (const std::exception&) { std::cout << "base\tstd::exception\n"; }
    try { md::Array v = c[0][1]; }
    catch (const md::InvalidArrayIndexException&) { std::cout << "data\tInvalidArrayIndexException\n"; }
    catch (const std::exception&) { std::cout << "data\tnone\n"; }
    md::Array plain = c;
    Try("plain.c1", [&] { md::Array e = static_cast<const md::CellArray&>(md::CellArray(plain))[1]; });
    Try("const.c1", [&] { const md::CellArray k = c; md::Array e = k[1]; });
    Try("const.c01", [&] { const md::CellArray k = c; md::Array e = k[0][1]; });
    Try("iter.ok", [&] { for (auto e : c) { md::Array a = e; (void)a; } });
    md::CellArray e0 = f.createCellArray({0, 0});
    Try("e00.read", [&] { md::Array v = e0[0]; });
    Try("e00.write", [&] { e0[0] = f.createScalar(1.0); });
    md::CellArray e1 = f.createCellArray({1, 0});
    Try("e10.read", [&] { md::Array v = e1[0][0]; });
    Try("ref.read", [&] { md::Reference<md::Array> ref = c[0][1]; md::Array v = ref; });
    Try("typed.read", [&] { md::TypedArray<double> v = c[1]; });
    Try("typed.ok", [&] { md::TypedArray<double> v = c[0]; });
    Try("inner.ref", [&] { md::TypedArrayRef<double> t = c[0]; double v = t[5]; (void)v; });
    Try("inner.ref.past", [&] { md::TypedArrayRef<double> t = c[2]; double v = t[0]; (void)v; });
    Try("c000.read", [&] { md::Array v = c[0][0][0]; });
    Try("c001.read", [&] { md::Array v = c[0][0][1]; });
    Try("c1.read", [&] { md::Array v = c[1]; });
    Try("c02.read", [&] { md::Array v = c[0][2]; });
    Try("c01.write", [&] { c[0][1] = f.createScalar(1.0); });
    Try("c01.type", [&] { (void)c[0][1].getType(); });
    md::CellArray d = f.createCellArray({2, 2});
    Try("d02.read", [&] { md::Array v = d[0][2]; });
    Try("d20.read", [&] { md::Array v = d[2][0]; });
    Try("d02.write", [&] { d[0][2] = f.createScalar(1.0); });
    Try("d20.write", [&] { d[2][0] = f.createScalar(1.0); });
    Try("d010.read", [&] { md::Array v = d[0][1][0]; });
    Try("d011.read", [&] { md::Array v = d[0][1][1]; });
    Try("d3.read", [&] { md::Array v = d[3]; });
    md::CellArray r = f.createCellArray({1, 4});
    Try("r4.read", [&] { md::Array v = r[4]; });
    Try("r4.write", [&] { r[4] = f.createScalar(1.0); });
    Try("r04.read", [&] { md::Array v = r[0][4]; });
    md::StructArray s = f.createStructArray({1, 2}, {"a"});
    Try("s2.read", [&] { md::Array v = s[2]["a"]; });
    Try("s02.read", [&] { md::Array v = s[0][2]["a"]; });
    Try("s10.read", [&] { md::Array v = s[1][0]["a"]; });
    md::TypedArray<double> m = f.createArray<double>({2, 2}, {1, 2, 3, 4});
    Try("m02.read", [&] { double v = m[0][2]; (void)v; });
    Try("m02.write", [&] { m[0][2] = 5; });
    md::TypedArray<md::MATLABString> t = f.createArray<md::MATLABString>({1, 2});
    Try("t2.read", [&] { md::MATLABString v = t[2]; (void)v; });
    Try("t02.read", [&] { md::MATLABString v = t[0][2]; (void)v; });
    return 0;
}
