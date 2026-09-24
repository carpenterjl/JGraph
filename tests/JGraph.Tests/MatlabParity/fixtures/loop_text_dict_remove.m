% loop_text_dict_remove.m -- V6 (ADR 0167), the three forms the generated value-isolation fixtures
% found missing: a for loop over a char row, a char matrix, a string array and a string matrix (each
% pass binds a column, a 1-by-1 char or a 1-by-1 string, as over a numeric array); removing a
% dictionary entry by d(key) = [], one key, several, a missing one, through an alias, a struct
% field, a cell slot and a global, and what an empty of another shape does; and the class of a
% containers.Map's Count. Recorded from R2025b.

run_case('lt01_char_row', @lt01);
run_case('lt02_char_empty', @lt02);
run_case('lt03_char_matrix', @lt03);
run_case('lt04_string_row', @lt04);
run_case('lt05_string_matrix', @lt05);
run_case('lt06_string_scalar', @lt06);
run_case('lt07_string_empty_row', @lt07);
run_case('lt08_string_no_rows', @lt08);
run_case('lt09_double_no_rows', @lt09);
run_case('lt10_char_source_held', @lt10);
run_case('lt11_char_column', @lt11);
run_case('lt12_char_var_rebound', @lt12);
run_case('lt13_int8_codes', @lt13);
run_case('lt14_logical', @lt14);
run_case('lt15_string_source_held', @lt15);
run_case('lt16_char_break', @lt16);
run_case('dr01_remove_one', @dr01);
run_case('dr02_remove_missing', @dr02);
run_case('dr03_remove_several', @dr03);
run_case('dr04_remove_by_empty_variable', @dr04);
run_case('dr05_empty_row_rhs', @dr05);
run_case('dr06_string_keys', @dr06);
run_case('dr07_cell_values', @dr07);
run_case('dr08_cell_values_empty_cell_rhs', @dr08);
run_case('dr09_alias_isolated', @dr09);
run_case('dr10_in_struct_field', @dr10);
run_case('dr11_in_cell_slot', @dr11);
run_case('dr12_global_in_function', @dr12);
run_case('dr13_remove_then_add', @dr13);
run_case('dr14_remove_all', @dr14);
run_case('dr15_numentries_class', @dr15);
run_case('dr16_string_values', @dr16);
run_case('dr17_remove_verb_alias', @dr17);
run_case('dr18_count_property', @dr18);
run_case('dr19_numentries_dot', @dr19);
run_case('dr20_keys_dot', @dr20);
run_case('dr21_keytype_dot', @dr21);
run_case('mc01_count_class', @mc01);
run_case('mc02_empty_value_stays', @mc02);
run_case('mc03_count_after_remove', @mc03);
run_case('mc04_empty_map_count', @mc04);
run_case('mc05_count_arithmetic', @mc05);
run_case('mc06_length', @mc06);
run_case('mc07_count_in_range', @mc07);

function run_case(name, fn)
try
    fprintf('CHK|%s|%s|exact\n', name, clean(fn()));
catch err
    fprintf('CHK|%s|ERR %s|exact\n', name, clean(err.message));
end
end

function s = clean(s)
if isnumeric(s) || islogical(s)
    s = mat2str(s);
end
s = char(s);
s = strrep(s, char(13), '');
s = strrep(s, char(10), ' ');
s = strrep(s, '|', '/');
end

function s = sh(v)
if iscell(v)
    parts = cellfun(@sh, v, 'UniformOutput', false);
    s = ['{' strjoin(parts(:)', ' ; ') '}'];
elseif ischar(v)
    s = ['char ' mat2str(size(v)) ' ' strjoin(cellstr(v)', '/')];
elseif isstring(v)
    s = ['string ' mat2str(size(v)) ' ' strjoin(cellstr(v(:)'), ',')];
else
    s = [class(v) ' ' mat2str(size(v)) ' ' mat2str(v)];
end
end

function s = dk(d)
s = [sh(keys(d)') ' / ' sh(values(d)') ' / ' num2str(numEntries(d))];
end

function s = lt01()
r = {}; for c = 'abz', r{end + 1} = c; end
s = sh(r);
end
function s = lt02()
r = {}; n = 0; for c = '', r{end + 1} = c; n = n + 1; end
s = [sh(r) ' ' num2str(n)];
end
function s = lt03()
r = {}; for c = ['ab'; 'cd'], r{end + 1} = c; end
s = sh(r);
end
function s = lt04()
r = {}; for t = ["x" "yy"], r{end + 1} = t; end
s = sh(r);
end
function s = lt05()
r = {}; for t = ["a" "b"; "c" "d"], r{end + 1} = t; end
s = sh(r);
end
function s = lt06()
r = {}; for t = "abc", r{end + 1} = t; end
s = sh(r);
end
function s = lt07()
r = {}; n = 0; for t = strings(1, 0), r{end + 1} = t; n = n + 1; end
s = [sh(r) ' ' num2str(n)];
end
function s = lt08()
r = {}; n = 0; for t = strings(0, 3), r{end + 1} = t; n = n + 1; end
s = [sh(r) ' ' num2str(n)];
end
function s = lt09()
r = {}; n = 0; for x = zeros(0, 3), r{end + 1} = x; n = n + 1; end
s = [sh(r) ' ' num2str(n)];
end
function s = lt10()
t = 'abc'; r = {};
for c = t
    t(1) = 'z';
    r{end + 1} = c;
end
s = [sh(r) ' ' sh(t)];
end
function s = lt11()
r = {}; for c = ['a'; 'b'; 'c'], r{end + 1} = c; end
s = sh(r);
end
function s = lt12()
r = {}; for c = 'ab', c = [c 'x']; r{end + 1} = c; end %#ok<FXSET>
s = sh(r);
end
function s = lt13()
r = {}; for c = int8('ab'), r{end + 1} = c; end
s = sh(r);
end
function s = lt14()
r = {}; for b = [true false], r{end + 1} = b; end
s = sh(r);
end
function s = lt15()
t = ["p" "q" "r"]; r = {};
for e = t
    t(1) = "z";
    r{end + 1} = e;
end
s = [sh(r) ' ' sh(t)];
end
function s = lt16()
r = {}; for c = 'abc', r{end + 1} = c; if c == 'b', break; end, end
s = sh(r);
end

function s = dr01()
d = dictionary([1 2 3], [10 20 30]);
d(2) = [];
s = dk(d);
end
function s = dr02()
d = dictionary([1 2 3], [10 20 30]);
d(9) = [];
s = dk(d);
end
function s = dr03()
d = dictionary([1 2 3], [10 20 30]);
d([1 3]) = [];
s = dk(d);
end
function s = dr04()
d = dictionary([1 2 3], [10 20 30]);
x = [];
d(2) = x;
s = dk(d);
end
function s = dr05()
d = dictionary([1 2 3], [10 20 30]);
d(2) = zeros(1, 0);
s = dk(d);
end
function s = dr06()
d = dictionary(["a" "b" "c"], [1 2 3]);
d("b") = [];
s = dk(d);
end
function s = dr07()
d = dictionary([1 2], {5, 'x'});
d(1) = [];
s = dk(d);
end
function s = dr08()
d = dictionary([1 2], {5, 'x'});
d(2) = {};
s = dk(d);
end
function s = dr09()
d = dictionary([1 2], [10 20]); e = d;
e(1) = [];
s = [dk(d) ' // ' dk(e)];
end
function s = dr10()
st.d = dictionary([1 2], [10 20]);
st.d(2) = [];
s = dk(st.d);
end
function s = dr11()
c = {dictionary([1 2], [10 20])};
c{1}(2) = [];
s = dk(c{1});
end
function s = dr12()
global gd
gd = dictionary([1 2], [10 20]);
remove_global_key();
s = dk(gd);
end
function remove_global_key()
global gd
gd(1) = [];
end
function s = dr13()
d = dictionary([1 2 3], [10 20 30]);
d(2) = [];
d(2) = 99;
s = dk(d);
end
function s = dr14()
d = dictionary([1 2], [10 20]);
d(1) = []; d(2) = [];
s = [dk(d) ' ' num2str(isConfigured(d))];
end
function s = dr15()
d = dictionary([1 2], [10 20]);
s = class(numEntries(d));
end
function s = dr16()
d = dictionary([1 2], ["a" "b"]);
d(1) = [];
s = dk(d);
end
function s = dr17()
d = dictionary([1 2], [10 20]);
e = remove(d, 1);
s = [dk(d) ' // ' dk(e)];
end
function s = dr18()
d = dictionary([1 2], [10 20]);
s = sh(d.Count);
end
function s = dr19()
d = dictionary([1 2], [10 20]);
s = sh(d.numEntries);
end
function s = dr20()
d = dictionary([1 2], [10 20]);
s = sh(d.keys);
end
function s = dr21()
d = dictionary([1 2], [10 20]);
s = sh(d.KeyType);
end

function s = mc01()
m = containers.Map({'a', 'b'}, {1, 2});
s = sh(m.Count);
end
function s = mc02()
m = containers.Map('KeyType', 'char', 'ValueType', 'any');
m('a') = 1; m('b') = 2;
m('a') = [];
s = [sh(m.Count) ' ' sh(m('a'))];
end
function s = mc03()
m = containers.Map({'a', 'b'}, {1, 2});
remove(m, 'a');
s = sh(m.Count);
end
function s = mc04()
m = containers.Map();
s = sh(m.Count);
end
function s = mc05()
m = containers.Map({'a', 'b'}, {1, 2});
s = sh(m.Count + 1);
end
function s = mc06()
m = containers.Map({'a', 'b'}, {1, 2});
s = sh(length(m));
end
function s = mc07()
m = containers.Map({'a', 'b'}, {1, 2});
r = {}; for k = 1:m.Count, r{end + 1} = k; end
s = sh(r);
end
