% property_accessors.m -- V6 (ADR 0167), property accessors and Dependent properties (appendix A
% #27, #28, #147): when a get or set method runs and when it is bypassed (inside its own body
% alone), the order of getter, setter, subscripts, end and the right-hand side in a read and in a
% composite write, on a value class and a handle class, standing alone and held in a struct or a
% cell; what a Dependent property may and may not do; the display, properties, fieldnames, isprop
% and struct of an object with accessors; and what each refusal says. Recorded from R2025b. The
% classes are in helpers/ (AccBox, AccHBox, AccGetWrites, AccSetReads, AccBadSet, AccHBadSet,
% AccDepDefault, AccDepNoGet, AccGetNoProp, AccRefuse, AccHRefuse, and value_isolation_accessors'
% DepBox and SetBox).

run_case('pa01_default_no_set', @pa01);
run_case('pa02_ctor_set', @pa02);
run_case('pa03_read', @pa03);
run_case('pa04_read_in_method', @pa04);
run_case('pa05_whole_set', @pa05);
run_case('pa06_indexed_set', @pa06);
run_case('pa07_end_set', @pa07);
run_case('pa08_delete_elem', @pa08);
run_case('pa09_dep_read', @pa09);
run_case('pa10_dep_set', @pa10);
run_case('pa11_dep_indexed_set', @pa11);
run_case('pa12_struct_prop_nested', @pa12);
run_case('pa13_struct_prop_field_whole', @pa13);
run_case('pa14_validate_before_set', @pa14);
run_case('pa15_validate_class_before_set', @pa15);
run_case('pa16_set_refuses_value_stays', @pa16);
run_case('pa17_set_refuses_handle_stays', @pa17);
run_case('pa18_copy_semantics', @pa18);
run_case('pa19_share_into_set', @pa19);
run_case('pa20_handle_alias', @pa20);
run_case('pa21_handle_struct_held_whole', @pa21);
run_case('pa22_value_in_struct_end', @pa22);
run_case('pa23_value_in_cell_indexed', @pa23);
run_case('pa24_get_writes_same_prop', @pa24);
run_case('pa25_set_reads_same_prop', @pa25);
run_case('pa26_bad_value_set', @pa26);
run_case('pa27_bad_handle_set', @pa27);
run_case('pa28_dep_default', @pa28);
run_case('pa29_dep_no_get_read', @pa29);
run_case('pa30_dep_no_get_write', @pa30);
run_case('pa31_dep_no_set_write', @pa31);
run_case('pa32_get_no_prop', @pa32);
run_case('pa33_properties_list', @pa33);
run_case('pa34_isprop', @pa34);
run_case('pa35_disp_logs', @pa35);
run_case('pa36_compound', @pa36);
run_case('pa37_growth', @pa37);
run_case('pa38_read_elem', @pa38);
run_case('pa39_read_end', @pa39);
run_case('pa40_handle_dep_set', @pa40);
run_case('pa41_handle_dep_no_set', @pa41);
run_case('pa42_handle_hits', @pa42);
run_case('pa43_struct_prop_read_nested', @pa43);
run_case('pa44_dep_in_script', @pa44);
run_case('pa45_fieldnames', @pa45);
run_case('pa46_struct_of', @pa46);
run_case('pa47_set_whole_empty', @pa47);
run_case('pa48_value_in_cell_whole', @pa48);
run_case('pa49_set_in_method', @pa49);
run_case('pa50_dep_end_set', @pa50);
run_case('pa51_numel', @pa51);
run_case('pa52_methods_list', @pa52);
run_case('pa53_handle_in_cell_dep', @pa53);
run_case('pa54_two_writes_one_statement_each', @pa54);

function run_case(name, fn)
global vlog_text
vlog_text = '';
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
    s = ['{' strjoin(parts(:)', ',') '}'];
elseif ischar(v)
    s = v;
elseif isstruct(v)
    s = ['struct ' strjoin(fieldnames(v)', ',')];
else
    s = [mat2str(v) '/' class(v)];
end
end

function s = lg(v)
global vlog_text
if ~ischar(v), v = sh(v); end
s = sprintf('%s / %s', vlog_text, v);
end

function k = idx_l()
vlog('idx');
k = 2;
end

function v = rhs_l()
vlog('rhs');
v = 9;
end

function s = pa01()
o = AccBox(); %#ok<NASGU>
s = lg('');
end
function s = pa02()
o = AccBox(5);
s = lg(o.peek());
end
function s = pa03()
o = AccBox(); vlog('start');
x = o.p;
s = lg(x);
end
function s = pa04()
o = AccBox(); vlog('start');
x = o.peek();
s = lg(x);
end
function s = pa05()
o = AccBox(); vlog('start');
o.p = 9;
s = lg(o.peek());
end
function s = pa06()
o = AccBox(); vlog('start');
o.p(idx_l()) = rhs_l();
s = lg(o.peek());
end
function s = pa07()
o = AccBox(); vlog('start');
o.p(end) = rhs_l();
s = lg(o.peek());
end
function s = pa08()
o = AccBox(); vlog('start');
o.p(2) = [];
s = lg(o.peek());
end
function s = pa09()
o = AccBox(); vlog('start');
x = o.q;
s = lg(x);
end
function s = pa10()
o = AccBox(); vlog('start');
o.q = [8 8 8];
s = lg(o.peek());
end
function s = pa11()
o = AccBox(); vlog('start');
o.q(2) = 8;
s = lg(o.peek());
end
function s = pa12()
o = AccBox(); vlog('start');
o.s.f(2) = rhs_l();
s = lg(o.s.f);
end
function s = pa13()
o = AccBox(); vlog('start');
o.s.f = 5;
s = lg(o.s.f);
end
function s = pa14()
o = AccBox(); vlog('start');
try
    o.n = -1;
catch
    vlog('E');
end
s = lg(o.n);
end
function s = pa15()
o = AccBox(); vlog('start');
try
    o.n = 'a';
catch
    vlog('E');
end
s = lg(o.n);
end
function s = pa16()
o = AccRefuse();
try
    o.p(2) = -1;
catch err
    vlog(['E:' err.message]);
end
s = lg(o.p);
end
function s = pa17()
h = AccHRefuse();
try
    h.p(2) = -1;
catch err
    vlog(['E:' err.message]);
end
s = lg(h.p);
end
function s = pa18()
a = AccBox(); b = a; vlog('start');
b.p(1) = 9;
s = lg([a.peek() b.peek()]);
end
function s = pa19()
v = ones(1, 3); b = SetBox(); b.p = v; v(1) = 7;
s = lg(b.p);
end
function s = pa20()
h = AccHBox(); g = h; vlog('start');
h.p(2) = 9;
s = lg(g.p);
end
function s = pa21()
st.h = AccHBox(); vlog('start');
st.h.p = 7;
s = lg(st.h.p);
end
function s = pa22()
st.b = AccBox(); vlog('start');
st.b.p(end) = rhs_l();
s = lg(st.b.peek());
end
function s = pa23()
c = {AccBox()}; vlog('start');
c{1}.p(idx_l()) = rhs_l();
s = lg(c{1}.peek());
end
function s = pa24()
g = AccGetWrites(); vlog('start');
x = g.p;
s = lg(x);
end
function s = pa25()
o = AccSetReads(); vlog('start');
o.p = 5;
s = lg(o.p);
end
function s = pa26()
b = AccBadSet(); vlog('start');
b.p = 5;
s = lg(b.p);
end
function s = pa27()
b = AccHBadSet(); vlog('start');
b.p = 5;
s = lg(b.p);
end
function s = pa28()
d = AccDepDefault();
s = lg(d.q);
end
function s = pa29()
d = AccDepNoGet();
x = d.r;
s = lg(x);
end
function s = pa30()
d = AccDepNoGet();
d.r = 1;
s = lg(d.p);
end
function s = pa31()
b = DepBox();
b.q = 1;
s = lg(b.p);
end
function s = pa32()
g = AccGetNoProp();
s = lg(g.p);
end
function s = pa33()
s = strjoin(properties(DepBox())', ',');
end
function s = pa34()
b = DepBox();
s = sprintf('%d %d %d', isprop(b, 'q'), isprop(b, 'p'), isprop(b, 'zz'));
end
function s = pa35()
o = AccBox(); vlog('start');
disp(o);
s = lg('');
end
function s = pa36()
o = AccBox(); vlog('start');
o.p = o.p + rhs_l();
s = lg(o.peek());
end
function s = pa37()
o = AccBox(); vlog('start');
o.p(end + 1) = rhs_l();
s = lg(o.peek());
end
function s = pa38()
o = AccBox(); vlog('start');
x = o.p(2);
s = lg(x);
end
function s = pa39()
o = AccBox(); vlog('start');
x = o.p(end);
s = lg(x);
end
function s = pa40()
h = AccHBox(); vlog('start');
h.q(2) = 8;
s = lg(h.p);
end
function s = pa41()
b = DepBox();
try
    b.q(2) = 8;
catch err
    vlog(['E:' err.message]);
end
s = lg(b.p);
end
function s = pa42()
h = AccHBox(); vlog('start');
x = h.p; y = h.p(1);
s = lg([x y h.hits]);
end
function s = pa43()
o = AccBox(); vlog('start');
x = o.s.f(2);
s = lg(x);
end
function s = pa44()
o = AccBox(); vlog('start');
o.q = [2 4 6];
s = lg(o.q);
end
function s = pa45()
o = AccBox(); vlog('start');
s = lg(strjoin(fieldnames(o)', ','));
end
function s = pa46()
o = AccBox(); vlog('start');
w = warning('off', 'all');
st = struct(o);
warning(w);
s = lg([strjoin(fieldnames(st)', ',') ' ' mat2str(st.q)]);
end
function s = pa47()
o = AccBox(); vlog('start');
o.p = [];
s = lg(size(o.peek()));
end
function s = pa48()
c = {AccBox()}; vlog('start');
c{1}.p = 4;
s = lg(c{1}.peek());
end
function s = pa49()
o = AccHBox(); vlog('start');
o.p = o.p;
s = lg(o.hits);
end
function s = pa50()
o = AccBox(); vlog('start');
o.q(end) = 8;
s = lg(o.peek());
end
function s = pa51()
o = AccBox(); vlog('start');
n = numel(o.p);
s = lg(n);
end
function s = pa52()
o = AccBox();
s = strjoin(methods(o)', ',');
end
function s = pa53()
c = {AccHBox()}; vlog('start');
c{1}.q(1) = 20;
s = lg(c{1}.p);
end
function s = pa54()
o = AccBox(); vlog('start');
o.p(1) = 7; o.p(3) = 8;
s = lg(o.peek());
end
