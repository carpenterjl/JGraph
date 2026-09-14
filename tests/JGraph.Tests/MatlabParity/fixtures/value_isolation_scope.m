% value_isolation_scope.m -- appendix A of the value-ownership plan: an evaluated value the
% interpreter holds while more script code runs (rule M5) -- an operand, a bracket or cell literal,
% a builtin's or a user function's argument, a loop's source, a method's receiver, an argument read
% out of a handle, evalin/evalc writing the caller -- and the builtins that mutate their argument
% (M8). A case named aNNN is appendix row NNN; g_ cases agree on both engines today. The cases that
% clear inside a function come last, so whatever they clear cannot reach an earlier line.

run_case('a005_binary_left_elem', @a005_binary_left_elem);
run_case('a006_bracket_elem', @a006_bracket_elem);
run_case('a007_call_arg_elem', @a007_call_arg_elem);
run_case('a008_cell_literal_elem', @a008_cell_literal_elem);
run_case('g_index_left_elem', @g_index_left_elem);
run_case('a009_for_matrix_source', @a009_for_matrix_source);
run_case('a010_cellfun_cell_arg', @a010_cellfun_cell_arg);
run_case('a011_feval_args', @a011_feval_args);
run_case('g_arrayfun_array_arg', @g_arrayfun_array_arg);
run_case('g_struct_index_left', @g_struct_index_left);
run_case('g_index_target_then_sub', @g_index_target_then_sub);
run_case('a012_struct_selection_arg', @a012_struct_selection_arg);
run_case('a013_cell_literal_child_arg', @a013_cell_literal_child_arg);
run_case('a014_value_object_nested', @a014_value_object_nested);
run_case('a016_value_object_global_prop', @a016_value_object_global_prop);
run_case('g_value_object_alias', @g_value_object_alias);
run_case('a019_method_receiver', @a019_method_receiver);
run_case('a020_dictionary_insert_source', @a020_dictionary_insert_source);
run_case('a021_dictionary_remove_source', @a021_dictionary_remove_source);
run_case('g_regexprep_cell_callback', @g_regexprep_cell_callback);
run_case('g_setfield_source', @g_setfield_source);
run_case('g_rmfield_source', @g_rmfield_source);
run_case('g_orderfields_source', @g_orderfields_source);
run_case('g_map_remove_handle', @g_map_remove_handle);
run_case('g_dictionary_alias_write', @g_dictionary_alias_write);
run_case('g_cell_alias_delete', @g_cell_alias_delete);
run_case('g_handle_prop_read_then_write', @g_handle_prop_read_then_write);
run_case('a022_handle_prop_operand_method_write', @a022_handle_prop_operand_method_write);
run_case('g_handle_holds_value_object', @g_handle_holds_value_object);
run_case('g_struct_holds_value_object', @g_struct_holds_value_object);
run_case('g_object_holds_array', @g_object_holds_array);
run_case('g_object_holds_value_object', @g_object_holds_value_object);
run_case('g_nested_cell_alias', @g_nested_cell_alias);
run_case('g_nested_struct_alias', @g_nested_struct_alias);
run_case('g_string_array_alias', @g_string_array_alias);
run_case('g_char_matrix_alias', @g_char_matrix_alias);
run_case('g_datetime_alias', @g_datetime_alias);
run_case('g_table_alias_dot', @g_table_alias_dot);
run_case('g_logical_alias', @g_logical_alias);
run_case('g_int_alias', @g_int_alias);
run_case('g_loop_alias_snapshot', @g_loop_alias_snapshot);
run_case('a023_deal_multi_assign', @a023_deal_multi_assign);
run_case('a057_eval_writes_mid_expression', @a057_eval_writes_mid_expression);
run_case('a058_evalc_writes_mid_argument', @a058_evalc_writes_mid_argument);
run_case('a015_clear_in_cell_loop', @a015_clear_in_cell_loop);

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

% --- an operand, a literal, an argument written by a later operand's call (borrow_probe.m) ---------

function z = bump_elem()
global gv
gv(1) = 7;
z = 0;
end

function s = a005_binary_left_elem()
global gv
gv = ones(1, 5);
out = gv + bump_elem();
s = num2str(out(1));
end

function s = a006_bracket_elem()
global gv
gv = ones(1, 5);
out = [gv, bump_elem()];
s = num2str(out(1));
end

function s = a007_call_arg_elem()
global gv
gv = ones(1, 5);
out = plus(gv, bump_elem());
s = num2str(out(1));
end

function s = a008_cell_literal_elem()
global gv
gv = ones(1, 5);
c = {gv, bump_elem()};
s = num2str(c{1}(1));
end

function s = g_index_left_elem()
global gv
gv = ones(1, 5);
out = gv(1:5) + bump_elem();
s = num2str(out(1));
end

% --- a loop's source and a higher-order builtin's arguments (borrow_probe2.m) ----------------------

function z = write_gc(x)
global gc
gc{3} = 7;
z = x;
end

function z = write_gm(x)
global gm
gm(3) = 7;
z = x;
end

function z = write_gs(k)
global gs
gs(k).f = 7;
z = 0;
end

function s = a009_for_matrix_source()
global gm
gm = ones(2, 3);
acc = 0;
for col = gm
    acc = acc + col(1);
    gm(1, :) = 7;
end
s = num2str(acc);
end

function s = a010_cellfun_cell_arg()
global gc
gc = {1, 1, 1};
r = cellfun(@write_gc, gc);
s = mat2str(r);
end

function s = a011_feval_args()
global gm
gm = ones(1, 3);
r = feval(@(x, y) x + y, gm, write_gm(1));
s = mat2str(r);
end

function s = g_arrayfun_array_arg()
global gm
gm = ones(1, 3);
r = arrayfun(@write_gm, gm);
s = mat2str(r);
end

function s = g_struct_index_left()
global gs
gs = struct('f', {1, 1, 1});
acc = 0;
for k = 1:3
    acc = acc + gs(k).f + write_gs(k);
end
s = num2str(acc);
end

function s = g_index_target_then_sub()
global gm
gm = ones(1, 3);
r = gm(write_gm(1));
s = num2str(r);
end

% --- a struct selection, a cell literal's child, a value object (borrow_probe3.m, 3b) --------------

function a = pass_first(a, ~)
end

function z = write_gS()
global gS
gS(1).f = 7;
z = 0;
end

function z = write_gC()
global gC
gC{1} = 7;
z = 0;
end

function z = write_gV()
global gV
gV.p = 7;
z = 0;
end

function s = a012_struct_selection_arg()
global gS
gS = struct('f', {1, 1});
r = pass_first(gS(1), write_gS());
s = num2str(r.f);
end

function s = a013_cell_literal_child_arg()
global gC
gC = {1, 1};
r = pass_first({gC{1}, gC}, write_gC());
s = num2str(r{2}{1});
end

function s = a014_value_object_nested()
v = ValueBox();
r = pass_first(v, bump());
s = num2str(r.p);
    function z = bump()
        v.p = 7;
        z = 0;
    end
end

function s = a016_value_object_global_prop()
global gV
gV = ValueBox();
r = pass_first(gV, write_gV());
s = sprintf('%d %d', r.p, gV.p);
end

function s = g_value_object_alias()
v = ValueBox();
w = v; v.p = 7;
s = num2str(w.p);
end

% --- a bound method's receiver; builtins that mutate their argument (borrow_probe4.m, 5) -----------

function s = a019_method_receiver()
v = ValueReader();
r = v.read(bump());
s = num2str(r);
    function z = bump()
        v.p = 7;
        z = 0;
    end
end

function s = a020_dictionary_insert_source()
d = dictionary(1, 10);
e = insert(d, 1, 20); %#ok<NASGU>
s = num2str(d(1));
end

function s = a021_dictionary_remove_source()
d = dictionary([1 2], [10 20]);
e = remove(d, 2); %#ok<NASGU>
s = num2str(numEntries(d));
end

function s = g_regexprep_cell_callback()
global gc
gc = {'a', 'a'};
out = regexprep(gc, 'a', '${bump_gc()}');
s = sprintf('%s %s', strjoin(out, ','), gc{2});
end

function s = g_setfield_source()
st = struct('f', 1, 'g', 2);
t = setfield(st, 'f', 7); %#ok<SFLD,NASGU>
s = num2str(st.f);
end

function s = g_rmfield_source()
st = struct('f', 1, 'g', 2);
t = rmfield(st, 'g'); %#ok<NASGU>
s = num2str(isfield(st, 'g'));
end

function s = g_orderfields_source()
st = struct('g', 2, 'f', 1);
t = orderfields(st); %#ok<NASGU>
fn = fieldnames(st);
s = fn{1};
end

function s = g_map_remove_handle()
m = containers.Map({'a', 'b'}, {1, 2});
remove(m, 'b');
s = num2str(m.Count);
end

function s = g_dictionary_alias_write()
d = dictionary(1, 10);
d2 = d; d2(1) = 20;
s = num2str(d(1));
end

function s = g_cell_alias_delete()
c = {1, 2, 3};
c2 = c; c2(2) = [];
s = num2str(numel(c));
end

% --- handles holding values, nested objects, other payload types (borrow_probe6.m) -----------------

function s = g_handle_prop_read_then_write()
h = HandleHolder(); h.data = ones(1, 5);
x = h.data; h.data(1) = 7;
s = num2str(x(1));
end

function s = a022_handle_prop_operand_method_write()
h = HandleHolder(); h.data = ones(1, 5);
r = plus(h.data, h.bump());
s = num2str(r(1));
end

function s = g_handle_holds_value_object()
h = HandleHolder(); h.data = ValueBox();
v = h.data; v.p = 7;
s = num2str(h.data.p);
end

function s = g_struct_holds_value_object()
st.o = ValueBox();
t = st; t.o.p = 7;
s = num2str(st.o.p);
end

function s = g_object_holds_array()
o = ValueBox(); o.p = ones(1, 3);
q = o; q.p(1) = 7;
s = num2str(o.p(1));
end

function s = g_object_holds_value_object()
o = ValueBox(); o.p = ValueBox();
q = o; q.p.p = 7;
s = num2str(o.p.p);
end

function s = g_nested_cell_alias()
c = {{1, 2}};
d = c; d{1}{1} = 7;
s = num2str(c{1}{1});
end

function s = g_nested_struct_alias()
st.a.b = 1;
t = st; t.a.b = 7;
s = num2str(st.a.b);
end

function s = g_string_array_alias()
sa = ["a" "b"];
t = sa; t(1) = "z";
s = char(sa(1));
end

function s = g_char_matrix_alias()
cm = ['ab'; 'cd'];
t = cm; t(1, 1) = 'z';
s = cm(1, 1);
end

function s = g_datetime_alias()
d = datetime(2020, 1, 1:3);
e = d; e(1) = datetime(2000, 1, 1);
s = num2str(year(d(1)));
end

function s = g_table_alias_dot()
T = table([1; 2]);
U = T; U.Var1(1) = 7;
s = num2str(T.Var1(1));
end

function s = g_logical_alias()
L = true(1, 5);
M = L; M(1) = false;
s = num2str(L(1));
end

function s = g_int_alias()
I = int8([1 2 3]);
J = I; J(1) = 7;
s = num2str(I(1));
end

function s = g_loop_alias_snapshot()
x = zeros(1, 5);
y = [];
for i = 1:5
    y = x;
    x(i) = i;
end
s = mat2str(y);
end

function z = bump_gv()
global gv
gv(1) = 7;
z = 0;
end

function s = a023_deal_multi_assign()
global gv
gv = ones(1, 5);
[a, b] = deal(gv, bump_gv()); %#ok<ASGLU>
s = num2str(a(1));
end

% --- evalin and evalc writing the caller mid-expression (borrow_probe11.m) -------------------------

function s = a057_eval_writes_mid_expression()
x = [1 2 3];
r = x + eval_side(); %#ok<NASGU>
s = mat2str(r);
    function z = eval_side()
        evalin('caller', 'x(1) = 7;');
        z = 0;
    end
end

function s = a058_evalc_writes_mid_argument()
x = [1 2 3];
r = plus(x, 0 * numel(evalc('x(1) = 7;')));
s = sprintf('%s %s', mat2str(r), mat2str(x));
end

% --- clear inside a loop over a cell whose child is a native buffer (borrow_probe3b.m) -------------

function s = a015_clear_in_cell_loop()
C = {0, 0};
C{2} = ones(1, 1100000) + 0;
s = '';
for q = C
    if isscalar(q{1})
        clear
    else
        s = num2str(q{1}(1));
    end
end
end
