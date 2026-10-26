/*
 * eq: one reference, comparison or struct element of the MATLAB Data API made past the end of an
 * array per run (compiler and SDK plan, stage K12), because R2025b's library ends the process for
 * some: its element comparisons are noexcept, and a struct element taken past the end is refused only
 * when it is read. Built by record-k12-edge.ps1 against R2025b's and, unchanged, against JGraph's
 * headers; eq <case> prints "made" once the value exists and its outcome after.
 *
 *   num     a number past the end compared        str     a string past the end compared
 *   ref     a typed reference made past the end   refok   a typed reference inside the array
 *   st      a struct element past the end iterated
 *   stf     a struct element past the end, a field read
 *   streq   a number past the end compared with !=
 *   refeq   an element past the end compared with one inside
 */
#include "MatlabDataArray.hpp"
#include <iostream>
#include <string>
#include <typeinfo>

namespace md = matlab::data;

int main(int argc, char** argv)
{
    std::cout << std::unitbuf;
    std::string which = argc > 1 ? argv[1] : "";
    md::ArrayFactory f;
    md::TypedArray<double> m = f.createArray<double>({2, 2}, {1, 2, 3, 4});
    md::TypedArray<md::MATLABString> t = f.createArray<md::MATLABString>({1, 2});
    md::CellArray c = f.createCellArray({1, 1});
    c[0] = f.createArray<double>({1, 3}, {4, 5, 6});
    try {
        if (which == "num") { bool b = m[5][0] == 3.0; std::cout << which << "\tok " << b << "\n"; }
        if (which == "str") { bool b = t[5] == std::string("x"); std::cout << which << "\tok " << b << "\n"; }
        if (which == "ref") { md::TypedArrayRef<double> r = c[2]; std::cout << which << "\tmade\n"; double v = r[0]; std::cout << which << "\tok " << v << "\n"; }
        md::StructArray s = f.createStructArray({1, 2}, {"a"});
        if (which == "st") { md::Struct e = s[5]; std::cout << which << "\tmade\n"; size_t n = 0; for (const md::Array& x : e) { (void)x; n++; } std::cout << which << "\tok " << n << "\n"; }
        if (which == "stf") { md::Struct e = s[5]; std::cout << which << "\tmade\n"; md::Array a = e["a"]; std::cout << which << "\tok\n"; }
        if (which == "streq") { bool b = m[0][5] != 3.0; std::cout << which << "\tok " << b << "\n"; }
        if (which == "refeq") { bool b = m[0][5] == m[0][0]; std::cout << which << "\tok " << b << "\n"; }
        if (which == "refok") { md::TypedArrayRef<double> r = c[0]; double v = r[2]; std::cout << which << "\tok " << v << "\n"; }
    }
    catch (const std::exception& e) { std::cout << which << "\t" << typeid(e).name() << " | " << e.what() << "\n"; }
    return 0;
}
