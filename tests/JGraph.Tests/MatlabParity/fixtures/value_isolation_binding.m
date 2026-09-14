% value_isolation_binding.m -- appendix A of the value-ownership plan (docs/plans/zfit-copies-and-
% temporaries-plan.md): the entries that retain a wrapper without a binding copy (rule M2) --
% anonymous captures, containers.Map values, ans, a nested function's shared workspace, str2func --
% and the stores already isolated on 90f9f87, kept as guards. A case named aNNN is appendix row NNN;
% a case named g_ agrees on both engines today. Each case prints one exact line; a line JGraph
% fails is stamped pending its owning stage (value_isolation_binding.owners), and the ratchet holds
% it to that exact output until the stage lands.

run_case('a001_anon_capture', @a001_anon_capture);
run_case('a002_map_value', @a002_map_value);
run_case('a003_ans_brace', @a003_ans_brace);
run_case('a004_ans_field', @a004_ans_field);
run_case('a024_nested_capture_write', @a024_nested_capture_write);
run_case('a080_str2func_no_capture', @a080_str2func_no_capture);
run_case('a030_struct_array_deal', @a030_struct_array_deal);
run_case('g_cell_literal', @g_cell_literal);
run_case('g_struct_ctor', @g_struct_ctor);
run_case('g_deal', @g_deal);
run_case('g_bracket', @g_bracket);
run_case('g_reshape', @g_reshape);
run_case('g_anon_return', @g_anon_return);
run_case('g_field_assign', @g_field_assign);
run_case('g_brace_assign', @g_brace_assign);
run_case('g_colon_read', @g_colon_read);
run_case('g_double_noop', @g_double_noop);
run_case('g_cellfun_collect', @g_cellfun_collect);
run_case('g_arrayfun_struct_collect', @g_arrayfun_struct_collect);
run_case('g_struct_from_cell', @g_struct_from_cell);
run_case('g_cell2struct_store', @g_cell2struct_store);
run_case('g_struct2cell_store', @g_struct2cell_store);
run_case('g_cell_concat_store', @g_cell_concat_store);
run_case('g_cs_list_assign', @g_cs_list_assign);
run_case('g_assignin_caller', @g_assignin_caller);
run_case('g_anonymous_does_capture', @g_anonymous_does_capture);
run_case('g_str2func_handle_call', @g_str2func_handle_call);

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

function s = a001_anon_capture()
v = ones(1, 5); f = @() v; v(1) = 7;
r = f();
s = num2str(r(1));
end

function s = a002_map_value()
v = ones(1, 5); m = containers.Map(); m('k') = v; v(1) = 7; q = m('k');
s = num2str(q(1));
end

function s = a003_ans_brace()
C = {ones(1, 5)}; C{1}; ans(1) = 7; %#ok<NOANS,VUNUS>
s = num2str(C{1}(1));
end

function s = a004_ans_field()
st.f = ones(1, 5); st.f; ans(1) = 7; %#ok<NOANS,VUNUS>
s = num2str(st.f(1));
end

function s = a024_nested_capture_write()
v = ones(1, 5);
f = @() v;
setv();
r = f();
s = num2str(r(1));
    function setv()
        v(1) = 7;
    end
end

function s = a080_str2func_no_capture()
round8_value = 7; %#ok<NASGU>
ok = 0;
try
    f = str2func('@() round8_value');
    y = f(); %#ok<NASGU>
    ok = 1;
catch
end
s = num2str(ok);
end

function s = a030_struct_array_deal()
v = ones(1, 3);
st = struct('f', {0, 0});
[st.f] = deal(v);
v(1) = 7;
s = sprintf('%d %d', st(1).f(1), st(2).f(1));
end

function s = g_cell_literal()
v = ones(1, 5); c = {v}; v(1) = 7;
s = num2str(c{1}(1));
end

function s = g_struct_ctor()
v = ones(1, 5); st = struct('f', v); v(1) = 7;
s = num2str(st.f(1));
end

function s = g_deal()
v = ones(1, 5); [a, b] = deal(v); v(1) = 7; a(2) = 9;
s = sprintf('%d %d %d', a(1), b(1), b(2));
end

function s = g_bracket()
v = ones(1, 5); w = [v]; v(1) = 7; %#ok<NBRAK>
s = num2str(w(1));
end

function s = g_reshape()
v = ones(1, 5); t = reshape(v, 5, 1); v(1) = 7;
s = num2str(t(1));
end

function s = g_anon_return()
v = ones(1, 5); g = @(x) x; u = g(v); v(1) = 7;
s = num2str(u(1));
end

function s = g_field_assign()
v = ones(1, 5); s2.f = v; v(1) = 7;
s = num2str(s2.f(1));
end

function s = g_brace_assign()
v = ones(1, 5); c2 = cell(1, 1); c2{1} = v; v(1) = 7;
s = num2str(c2{1}(1));
end

function s = g_colon_read()
v = ones(1, 5); x = v(:)'; v(1) = 7;
s = num2str(x(1));
end

function s = g_double_noop()
v = ones(1, 5); y = double(v); v(1) = 7;
s = num2str(y(1));
end

function s = g_cellfun_collect()
C = {ones(1, 3)};
D = cellfun(@(x) x, C, 'UniformOutput', false);
C{1}(1) = 7;
s = num2str(D{1}(1));
end

function s = g_arrayfun_struct_collect()
S = struct('f', {ones(1, 3)});
D = arrayfun(@(e) e.f, S, 'UniformOutput', false);
S(1).f(1) = 7;
s = num2str(D{1}(1));
end

function s = g_struct_from_cell()
c = {ones(1, 3)};
st = struct('f', c);
c{1}(1) = 7;
s = num2str(st.f(1));
end

function s = g_cell2struct_store()
c = {ones(1, 3)};
st = cell2struct(c, {'f'}, 1);
c{1}(1) = 7;
s = num2str(st.f(1));
end

function s = g_struct2cell_store()
st.f = ones(1, 3);
c = struct2cell(st);
st.f(1) = 7;
s = num2str(c{1}(1));
end

function s = g_cell_concat_store()
v = ones(1, 3);
C = [{v}, {1}];
v(1) = 7;
s = num2str(C{1}(1));
end

function s = g_cs_list_assign()
C = {ones(1, 3), 2};
[a, b] = C{:}; %#ok<ASGLU>
C{1}(1) = 7;
s = num2str(a(1));
end

function s = g_assignin_caller()
v = ones(1, 3);
put_w(v);
v(1) = 7; %#ok<NASGU>
s = num2str(eval('w(1)'));
end

function put_w(x)
assignin('caller', 'w', x);
end

function s = g_anonymous_does_capture()
round8_value = 7;
f = @() round8_value;
s = num2str(f());
end

function s = g_str2func_handle_call()
f = str2func('@(x) x + 1');
s = num2str(f(4));
end
