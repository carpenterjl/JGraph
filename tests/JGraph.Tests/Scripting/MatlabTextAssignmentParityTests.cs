using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Indexed writes into string arrays, char rows and cells, and <c>+</c> on strings, against MATLAB
/// R2025b. Every expected value here was produced by running the same statements in MATLAB.
/// </summary>
/// <remarks>
/// The rules these pin: <c>x(2) = "c"</c> stores one string and not a nested array (audit 6.1); a slot
/// a string array grows into is the missing string; a number written into a string array is spelled
/// as <c>string</c> spells it and NaN is missing; a string written into a double array is read as a
/// number, and into an integer, single or logical array is refused; a char row takes writes by
/// position and grows with <c>char(0)</c>; a cell takes <c>c(k) = {v}</c> and grows with <c>[]</c>;
/// <c>+</c> with a string answers a string whatever class the other side wears (audit 6.2), and every
/// other arithmetic operator on a string is refused.
/// </remarks>
[Collection("JG facade")]
public class MatlabTextAssignmentParityTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabTextAssignmentParityTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

    private async Task RunAsserting(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    // --- string arrays -----------------------------------------------------------------------------

    [Fact]
    public Task AStringArrayElementWriteStoresOneString() => RunAsserting("""
        x = ["a" "b"];
        x(2) = "c";
        assert(isequal(x, ["a" "c"]));
        assert(isequal(strlength(x), [1 1]));
        assert(strcmp(class(x), 'string') && isequal(size(x), [1 2]));
        assert(isequal(x(2), "c") && strcmp(class(x{2}), 'char'));
        x(4) = "d";
        assert(isequal(ismissing(x), [false false true false]));
        assert(isequaln(strlength(x), [1 1 NaN 1]));
        assert(isequaln(x + "!", ["a!" "c!" string(missing) "d!"]));
        x(2) = x(2) + x(1);
        assert(isequal(x(2), "ca"));
        y = ["a" "b"; "c" "d"];
        y(2, 1) = "z"; y(1, :) = "q";
        assert(isequal(y, ["q" "q"; "z" "d"]));
        y(:) = "w";
        assert(isequal(y, ["w" "w"; "w" "w"]));
        """);

    [Fact]
    public Task AStringArrayTakesEveryRightHandSideMatlabConverts() => RunAsserting("""
        w = ["a" "b"]; w(2) = 'ch';   assert(isequal(w, ["a" "ch"]) && strcmp(class(w), 'string'));
        w = ["a" "b"]; w(2) = 5;      assert(isequal(w, ["a" "5"]));
        w = ["a" "b"]; w(2) = true;   assert(isequal(w, ["a" "true"]));
        w = ["a" "b"]; w(2) = int8(7); assert(isequal(w, ["a" "7"]));
        w = ["a" "b"]; w(2) = pi;     assert(isequal(w, ["a" "3.1416"]));
        w = ["a" "b"]; w(2) = 1e16;   assert(isequal(w, ["a" "1e+16"]));
        w = ["a" "b"]; w(2) = 1+2i;   assert(isequal(w, ["a" "1+2i"]));
        w = ["a" "b"]; w(2) = NaN;    assert(isequal(ismissing(w), [false true]));
        w = ["a" "b"]; w(2) = {'s'};  assert(isequal(w, ["a" "s"]));
        w = ["a" "b"]; w(2) = {1};    assert(isequal(w, ["a" "1"]));
        w = ["a" "b"]; w(2) = '';     assert(isequal(w, ["a" ""]));
        w = ["a" "b"]; w(2) = missing; assert(isequal(ismissing(w), [false true]));
        w = ["a" "b"]; w(1:2) = "p";  assert(isequal(w, ["p" "p"]));
        w = ["a" "b"]; w(1:2) = 'pq'; assert(isequal(w, ["pq" "pq"]));
        w = ["a" "b"]; w(1:2) = [1 2]; assert(isequal(w, ["1" "2"]));
        w = ["a" "b"]; w(1:2) = {'p', 'q'}; assert(isequal(w, ["p" "q"]));
        w = ["a" "b"]; w(1:2) = ["p"; "q"]; assert(isequal(w, ["p" "q"]));
        w = ["a" "b"]; w(w == "a") = "q"; assert(isequal(w, ["q" "b"]));
        w = ["a" "b"]; w{2} = 'p';    assert(isequal(w, ["a" "p"]));
        x = ["a" "b"; "c" "d"]; x(:, 1) = ['p'; 'q']; assert(isequal(x, ["p" "b"; "q" "d"]));
        x = ["a" "b"; "c" "d"]; x(:, 1) = [1; 2];     assert(isequal(x, ["1" "b"; "2" "d"]));
        x = ["a" "b"; "c" "d"]; x(:, end+1) = 7;      assert(isequal(x, ["a" "b" "7"; "c" "d" "7"]));
        """);

    [Fact]
    public Task AStringArrayRefusesWhatMatlabRefuses() => RunAsserting("""
        assert(fails(@() write(['ab'; 'cd'])));
        assert(fails(@() write(["p" "q"])));
        assert(fails(@() write({[1 2]})));
        assert(fails(@() write(struct('a', 1))));
        assert(fails(@() write(strings(0))));
        assert(fails(@() write({})));
        assert(fails(@() write(zeros(1, 0))));
        assert(fails(@braces));
        assert(fails(@twoIntoOne));
        function ok = fails(f)
          ok = false;
          try, f(); catch, ok = true; end
        end
        function w = write(rhs)
          w = ["a" "b"]; w(2) = rhs;
        end
        function w = braces()
          w = ["a" "b"]; w{2} = "p";
        end
        function w = twoIntoOne()
          w = ["a" "b"]; w(1:2) = ["p" "q" "r"];
        end
        """);

    [Fact]
    public Task AStringArrayGrowsWithMissingAndDeletesLikeAnyArray() => RunAsserting("""
        y = ["a" "b"; "c" "d"]; y(3, 3) = "e";
        assert(isequal(ismissing(y), [false false true; false false true; true true false]));
        z = "s"; z(3) = "t";
        assert(isequal(size(z), [1 3]) && isequal(ismissing(z), [false true false]));
        z = "s"; z(2, 2) = "t";
        assert(isequal(size(z), [2 2]) && isequal(ismissing(z), [false true; true false]));
        x = ["a"; "b"]; x(3) = "c";
        assert(isequal(size(x), [3 1]) && isequal(x, ["a"; "b"; "c"]));
        x = strings(1, 3); x(2) = "m";
        assert(isequal(x, ["" "m" ""]) && isequal(strlength(x), [0 1 0]));
        x = strings(0); x(1) = "m";
        assert(isequal(size(x), [1 1]) && isstring(x));
        w = ["a" "b"]; w(end+1) = "n";
        assert(isequal(w, ["a" "b" "n"]));
        w = ["a" "b"]; w(1) = [];
        assert(isequal(w, "b") && isequal(size(w), [1 1]));
        w = ["a" "b"]; w([1 2]) = [];
        assert(isstring(w) && isequal(size(w), [1 0]));
        x = ["a" "b"; "c" "d"]; x(x == "a") = [];
        assert(isequal(x, ["c" "b" "d"]));
        x = ["a" "b"; "c" "d"]; x(2, :) = [];
        assert(isequal(x, ["a" "b"]));
        x = ["a" "b"; "c" "d"]; x(end+1, :) = "z";
        assert(isequal(x, ["a" "b"; "c" "d"; "z" "z"]));
        x = ["a" "b"]; x(2, 1) = "c";
        assert(isequal(ismissing(x), [false false; false true]));
        """);

    [Fact]
    public Task AConjuredVariableTakesTheKindThatIsWrittenIntoIt() => RunAsserting("""
        q(1) = "a";
        assert(isstring(q) && isequal(q, "a"));
        q(3) = "c";
        assert(isequal(ismissing(q), [false true false]));
        p(1) = {5};
        assert(iscell(p) && isequal(p, {5}));
        r(3) = 7;
        assert(isequal(r, [0 0 7]));
        n = []; n(1) = "a";
        assert(strcmp(class(n), 'double') && isnan(n));
        """);

    // --- numeric arrays --------------------------------------------------------------------------

    [Fact]
    public Task AStringWrittenIntoADoubleArrayIsReadAsANumber() => RunAsserting("""
        n = [1 2]; n(2) = "5";    assert(isequal(n, [1 5]) && strcmp(class(n), 'double'));
        n = [1 2]; n(2) = "1e3";  assert(isequal(n, [1 1000]));
        n = [1 2]; n(2) = "a";    assert(isequaln(n, [1 NaN]));
        n = [1 2]; n(4) = "a";    assert(isequaln(n, [1 2 0 NaN]));
        n = [1 2]; n(1:2) = ["5" "x"]; assert(isequaln(n, [5 NaN]));
        n = [1 2]; n(2) = string(missing); assert(isequaln(n, [1 NaN]));
        n = [1 2]; n(2) = '5';    assert(isequal(n, [1 53]));
        x = [1 2; 3 4]; x(3, 3) = "9"; assert(isequal(x, [1 2 0; 3 4 0; 0 0 9]));
        assert(fails(@intoInt8) && fails(@intoSingle) && fails(@intoLogical));
        assert(fails(@cellIntoDouble) && fails(@twoCharsIntoOne) && fails(@arrayIntoOne));
        function ok = fails(f)
          ok = false;
          try, f(); catch, ok = true; end
        end
        function n = intoInt8()
          n = int8([1 2]); n(2) = "a";
        end
        function n = intoSingle()
          n = single([1 2]); n(2) = "a";
        end
        function l = intoLogical()
          l = [true false]; l(1) = "a";
        end
        function n = cellIntoDouble()
          n = [1 2]; n(2) = {5};
        end
        function n = twoCharsIntoOne()
          n = [1 2]; n(2) = 'ab';
        end
        function n = arrayIntoOne()
          n = [1 2]; n(2) = [3 4];
        end
        """);

    // --- char rows -------------------------------------------------------------------------------

    [Fact]
    public Task ACharRowTakesWritesByPosition() => RunAsserting("""
        c = 'abc'; c(2) = 'x';       assert(strcmp(c, 'axc') && ischar(c));
        c = 'abc'; c(2) = "x";       assert(strcmp(c, 'axc') && ischar(c));
        c = 'abc'; c(1, 2) = 'x';    assert(strcmp(c, 'axc'));
        c = 'abc'; c([1 3]) = 'xy';  assert(strcmp(c, 'xby'));
        c = 'abc'; c(1:2) = 'q';     assert(strcmp(c, 'qqc'));
        c = 'abc'; c(:) = 'z';       assert(strcmp(c, 'zzz'));
        c = 'abc'; c(2) = 65;        assert(strcmp(c, 'aAc'));
        c = 'abc'; c(2) = 65.5;      assert(strcmp(c, 'aAc'));
        c = 'abc'; c(2:3) = "xy";    assert(strcmp(c, 'axy'));
        c = 'abc'; c(logical([1 0 1])) = 'XY'; assert(strcmp(c, 'XbY'));
        c = 'abc'; c(end+1) = '!';   assert(strcmp(c, 'abc!'));
        c = 'ab'; c(4) = 'x';        assert(isequal(double(c), [97 98 0 120]));
        c = ''; c(3) = 'x';          assert(isequal(double(c), [0 0 120]));
        c = 'abc'; c(2) = [];        assert(strcmp(c, 'ac'));
        c = 'abc'; c(2) = '';        assert(strcmp(c, 'ac'));
        s = 'hello'; s(1) = upper(s(1)); assert(strcmp(s, 'Hello'));
        assert(fails(@twoIntoOne) && fails(@logicalIn) && fails(@cellIn) && fails(@missingIn));
        function ok = fails(f)
          ok = false;
          try, f(); catch, ok = true; end
        end
        function c = twoIntoOne()
          c = 'abc'; c(2) = "xy";
        end
        function c = logicalIn()
          c = 'abc'; c(2) = true;
        end
        function c = cellIn()
          c = 'abc'; c(2) = {'x'};
        end
        function c = missingIn()
          c = 'abc'; c(2) = string(missing);
        end
        """);

    // --- cells -----------------------------------------------------------------------------------

    [Fact]
    public Task ACellTakesParenWritesAndGrowsWithEmpties() => RunAsserting("""
        ce = {'a'}; ce(2) = {'x'};
        assert(isequal(ce, {'a', 'x'}));
        ce(end+1) = {5};
        assert(isequal(ce, {'a', 'x', 5}));
        ce(1) = [];
        assert(isequal(ce, {'x', 5}));
        ce(1:2) = {0};
        assert(isequal(ce, {0, 0}));
        ce = {'a'}; ce(2) = {"x"};
        assert(strcmp(class(ce{2}), 'string'));
        c2 = {1 2; 3 4};
        c2(2, 2) = {9};
        assert(isequal(c2, {1 2; 3 9}));
        c2(3, 1) = {7};
        assert(isequal(size(c2), [3 2]) && isequal(c2{3, 1}, 7) && isempty(c2{3, 2}));
        c2(1, :) = [];
        assert(isequal(c2, {3 9; 7 []}));
        col = {1; 2}; col(3) = {3};
        assert(isequal(size(col), [3 1]));
        acc = {};
        for k = 1:3, acc(end+1) = {k}; end
        assert(isequal(acc, {1, 2, 3}));
        assert(fails(@stringIn) && fails(@numberIn) && fails(@twoIntoOne));
        function ok = fails(f)
          ok = false;
          try, f(); catch, ok = true; end
        end
        function ce = stringIn()
          ce = {'a'}; ce(2) = "x";
        end
        function ce = numberIn()
          ce = {'a'}; ce(2) = 5;
        end
        function ce = twoIntoOne()
          ce = {'a'}; ce(1) = {1, 2};
        end
        """);

    // --- operators -------------------------------------------------------------------------------

    [Fact]
    public Task PlusOnAStringConcatenatesWithoutANumericClass() => RunAsserting("""
        assert(isequal(1 + "abc", "1abc"));
        assert(isequal("abc" + int8(5), "abc5") && strcmp(class("abc" + int8(5)), 'string'));
        assert(isequal(int8(5) + "abc", "5abc"));
        assert(isequal("abc" + single(1.5), "abc1.5"));
        assert(isequal("abc" + uint8(200), "abc200"));
        assert(isequal("abc" + int32(-7), "abc-7"));
        assert(isequal("abc" + true, "abctrue"));
        assert(isequal("abc" + logical([1 0]), ["abctrue" "abcfalse"]));
        assert(isequal(["a" "b"] + ["1"; "2"], ["a1" "b1"; "a2" "b2"]));
        assert(isequal(["a"; "b"] + [1 2], ["a1" "a2"; "b1" "b2"]));
        assert(isequal(["a" "b"; "c" "d"] + [1; 2], ["a1" "b1"; "c2" "d2"]));
        assert(isequal("abc" + ['ab'; 'cd'], ["abcab"; "abccd"]));
        assert(isequal("abc" + 1i, "abc0+1i"));
        assert(isequal("abc" + [1+2i 3], ["abc1+2i" "abc3+0i"]));
        assert(isequal("abc" + {1; 2}, ["abc1"; "abc2"]));
        assert(isequal("abc" + {'q'}, "abcq"));
        assert(isequal("abc" + 1e16, "abc1e+16"));
        assert(isequal("abc" + 0.1 + 0.2, "abc0.10.2"));
        assert(isequal("abc" + (0.1 + 0.2), "abc0.3"));
        assert(isequal("abc" + 1/3, "abc0.33333"));
        assert(isequal("abc" + -0, "abc0"));
        assert(isequal("abc" + Inf, "abcInf"));
        assert(ismissing("abc" + NaN) && ismissing(string(missing) + 1) && ismissing(1 + string(missing)));
        assert(isstring("abc" + []) && isequal(size("abc" + []), [0 0]));
        assert(isequal(size("abc" + zeros(1, 0)), [1 0]));
        assert(isequal(plus("abc", 5), "abc5") && isstring(plus("abc", 5)));
        assert(isequal(plus(5, "abc"), "5abc"));
        assert(isequal(string(2.5i), "0+2.5i") && isequal(string([1+2i 3]), ["1+2i" "3+0i"]));
        assert(fails(@() "abc" - 1) && fails(@() "abc" * 2) && fails(@() "abc" .* 2) && fails(@() -"abc"));
        assert(fails(@() "abc" + {[1 2]}) && fails(@() "abc" + struct('a', 1)) && fails(@() "abc" + @sin));
        assert(fails(@() ["a" "b"] + [5 6 7]) && fails(@() ["a" "b"] + []));
        function ok = fails(f)
          ok = false;
          try, f(); catch, ok = true; end
        end
        """);
}
