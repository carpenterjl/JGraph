% save_load_roundtrip.m -- V6 of the value-ownership plan (ADR 0167, appendix A #110-#113): what
% save writes and load hands back. save -struct; a value object and a handle object (a loaded
% handle is a new instance, two names over one handle load as one); a function handle (an
% anonymous one answers what it captured); matfile reads, indexed and whole writes, the settings;
% and the forms that agreed before this stage: load over an alias, two loads, detached cell
% children, struct aliases, -append, complex aliases, load inside a function. Every message that
% names the file has the path replaced by FN, so the recording reads the same on every machine.

run_case('r_struct_flag', @r_struct_flag);
run_case('r_struct_flag_fields', @r_struct_flag_fields);
run_case('r_struct_flag_command', @r_struct_flag_command);
run_case('r_struct_flag_append', @r_struct_flag_append);
run_case('r_struct_flag_not_struct', @r_struct_flag_not_struct);
run_case('r_struct_flag_array', @r_struct_flag_array);
run_case('r_struct_flag_missing', @r_struct_flag_missing);
run_case('r_struct_flag_bad_field', @r_struct_flag_bad_field);
run_case('r_struct_flag_no_name', @r_struct_flag_no_name);
run_case('r_struct_flag_after', @r_struct_flag_after);
run_case('r_handle_load_new', @r_handle_load_new);
run_case('r_handle_aliases', @r_handle_aliases);
run_case('r_value_object', @r_value_object);
run_case('r_handle_in_struct_and_top', @r_handle_in_struct_and_top);
run_case('r_handle_in_cell', @r_handle_in_cell);
run_case('r_value_props_share_handle', @r_value_props_share_handle);
run_case('r_deleted_handle_save', @r_deleted_handle_save);
run_case('r_load_runs_no_ctor', @r_load_runs_no_ctor);
run_case('r_loaded_handle_kind', @r_loaded_handle_kind);
run_case('r_loaded_method_works', @r_loaded_method_works);
run_case('r_load_statement_object', @r_load_statement_object);
run_case('r_save_whole_workspace', @r_save_whole_workspace);
run_case('r_load_selected_object', @r_load_selected_object);
run_case('r_struct_with_handle_and_fn', @r_struct_with_handle_and_fn);
run_case('r_fn_anon', @r_fn_anon);
run_case('r_fn_builtin', @r_fn_builtin);
run_case('r_fn_local', @r_fn_local);
run_case('r_fn_captured_handle', @r_fn_captured_handle);
run_case('r_fn_nested_anon', @r_fn_nested_anon);
run_case('r_fn_captured_scalar', @r_fn_captured_scalar);
run_case('r_fn_in_cell', @r_fn_in_cell);
run_case('r_fn_anon_args', @r_fn_anon_args);
run_case('r_matfile_read', @r_matfile_read);
run_case('r_matfile_class', @r_matfile_class);
run_case('r_matfile_props', @r_matfile_props);
run_case('r_matfile_who', @r_matfile_who);
run_case('r_matfile_whos', @r_matfile_whos);
run_case('r_matfile_size', @r_matfile_size);
run_case('r_matfile_size_two_outputs', @r_matfile_size_two_outputs);
run_case('r_matfile_size_bare', @r_matfile_size_bare);
run_case('r_matfile_partial_read', @r_matfile_partial_read);
run_case('r_matfile_partial_end', @r_matfile_partial_end);
run_case('r_matfile_indexed_write', @r_matfile_indexed_write);
run_case('r_matfile_whole_write', @r_matfile_whole_write);
run_case('r_matfile_new_var', @r_matfile_new_var);
run_case('r_matfile_growth', @r_matfile_growth);
run_case('r_matfile_not_writable', @r_matfile_not_writable);
run_case('r_matfile_set_writable', @r_matfile_set_writable);
run_case('r_matfile_missing_var', @r_matfile_missing_var);
run_case('r_matfile_nofile_read', @r_matfile_nofile_read);
run_case('r_matfile_nofile_write', @r_matfile_nofile_write);
run_case('r_matfile_alias', @r_matfile_alias);
run_case('r_matfile_rereads', @r_matfile_rereads);
run_case('r_matfile_object_read', @r_matfile_object_read);
run_case('r_matfile_struct_field_read', @r_matfile_struct_field_read, 'div=ADR0167');
run_case('r_matfile_cell_brace_read', @r_matfile_cell_brace_read, 'div=ADR0167');
run_case('r_matfile_bad_arg', @r_matfile_bad_arg);
run_case('r_matfile_no_ext', @r_matfile_no_ext);
run_case('r_matfile_scalar_read', @r_matfile_scalar_read);
run_case('r_matfile_isobject', @r_matfile_isobject);
run_case('r_matfile_two_index_read', @r_matfile_two_index_read);
run_case('r_matfile_props_write_bad', @r_matfile_props_write_bad);
run_case('r_matfile_eq', @r_matfile_eq);
run_case('r_load_over_alias', @r_load_over_alias);
run_case('r_two_loads_independent', @r_two_loads_independent);
run_case('r_save_cell_detached_children', @r_save_cell_detached_children);
run_case('r_load_struct_aliases_independent', @r_load_struct_aliases_independent);
run_case('r_save_append', @r_save_append);
run_case('r_save_complex_alias', @r_save_complex_alias);
run_case('r_load_in_function_binds', @r_load_in_function_binds);

function run_case(name, fn, rule)
if nargin < 3
    rule = 'exact';
end
global vlog_text
vlog_text = '';
try
    fprintf('CHK|%s|%s|%s\n', name, clean(fn()), rule);
catch err
    fprintf('CHK|%s|ERR %s|%s\n', name, clean(err.message), rule);
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

function fn = scratch_mat()
fn = [tempname '.mat'];
end

function s = nopath(msg, fn)
% The message with the file's path replaced, so the line reads the same on every machine.
s = strrep(msg, fn, 'FN');
end

function s = logged_text()
global vlog_text
s = vlog_text;
end

function z = local_double(x)
z = 2 * x;
end

% --- save -struct (appendix A #110) -----------------------------------------------------------------

function s = r_struct_flag()
fn = scratch_mat();
st.a = [1 2];
st.b = 'x';
save(fn, '-struct', 'st');
S = load(fn);
s = sprintf('%s %s %s', strjoin(fieldnames(S)', ','), mat2str(S.a), S.b);
delete(fn);
end

function s = r_struct_flag_fields()
fn = scratch_mat();
st.a = [1 2];
st.b = 'x';
st.c = 3;
save(fn, '-struct', 'st', 'a', 'c');
S = load(fn);
s = strjoin(fieldnames(S)', ',');
delete(fn);
end

function s = r_struct_flag_command()
% The command form, on a bare name in the temp folder (a full path holds characters the command
% form reads as operators).
here = cd;
cd(tempdir);
[~, leaf] = fileparts(tempname);
name = [leaf '.mat'];
st.a = 1;
st.b = 2; %#ok<STRNU>
eval(['save ' name ' -struct st']);
S = load(name);
s = strjoin(fieldnames(S)', ',');
delete(name);
cd(here);
end

function s = r_struct_flag_append()
fn = scratch_mat();
v = 5; %#ok<NASGU>
save(fn, 'v');
st.a = 1;
st.b = 2; %#ok<STRNU>
save(fn, '-struct', 'st', '-append');
S = load(fn);
s = strjoin(sort(fieldnames(S))', ',');
delete(fn);
end

function s = r_struct_flag_not_struct()
fn = scratch_mat();
st = 5; %#ok<NASGU>
save(fn, '-struct', 'st');
s = 'saved';
delete(fn);
end

function s = r_struct_flag_array()
fn = scratch_mat();
st = struct('a', {1, 2}); %#ok<NASGU>
save(fn, '-struct', 'st');
s = 'saved';
delete(fn);
end

function s = r_struct_flag_missing()
fn = scratch_mat();
save(fn, '-struct', 'nothere');
s = 'saved';
delete(fn);
end

function s = r_struct_flag_bad_field()
fn = scratch_mat();
st.a = 1; %#ok<STRNU>
save(fn, '-struct', 'st', 'zz');
s = 'saved';
delete(fn);
end

function s = r_struct_flag_no_name()
fn = scratch_mat();
st.a = 1; %#ok<STRNU>
save(fn, '-struct');
s = 'saved';
delete(fn);
end

function s = r_struct_flag_after()
fn = scratch_mat();
st.a = 1; %#ok<STRNU>
save(fn, 'st', '-struct');
s = 'saved';
delete(fn);
end

% --- objects (appendix A #111) -----------------------------------------------------------------------

% S = load(fn) also rebinds the loaded names in this build until V9 (appendix A #109), so every
% case below that looks at the object it saved looks through an alias made before the load.

function s = r_handle_load_new()
fn = scratch_mat();
h = HandleHolder();
h.data = [1 2];
g = h;
save(fn, 'h');
S = load(fn);
S.h.data(1) = 7;
s = sprintf('%s %s %d', mat2str(g.data), mat2str(S.h.data), S.h == g);
delete(fn);
end

function s = r_handle_aliases()
fn = scratch_mat();
h = HandleHolder();
h.data = 1;
g = h; %#ok<NASGU>
save(fn, 'h', 'g');
S = load(fn);
S.h.data = 5;
s = sprintf('%g %d', S.g.data, S.g == S.h);
delete(fn);
end

function s = r_value_object()
fn = scratch_mat();
o = ValueBox();
o.p = 3;
save(fn, 'o');
o.p = 4;
q = o;
S = load(fn);
s = sprintf('%s %g %g', class(S.o), S.o.p, q.p);
delete(fn);
end

function s = r_handle_in_struct_and_top()
fn = scratch_mat();
h = HandleHolder();
h.data = 1;
st.h = h; %#ok<STRNU>
save(fn, 'h', 'st');
S = load(fn);
S.st.h.data = 9;
s = sprintf('%d %g', S.st.h == S.h, S.h.data);
delete(fn);
end

function s = r_handle_in_cell()
fn = scratch_mat();
h = HandleHolder();
h.data = 1;
c = {h, h, HandleHolder()}; %#ok<NASGU>
save(fn, 'c');
S = load(fn);
s = sprintf('%d %d', S.c{1} == S.c{2}, S.c{1} == S.c{3});
delete(fn);
end

function s = r_value_props_share_handle()
fn = scratch_mat();
h = HandleHolder();
h.data = 1;
a = ValueBox();
a.p = h;
b = ValueBox();
b.p = h; %#ok<NASGU>
save(fn, 'a', 'b');
S = load(fn);
S.a.p.data = 4;
s = sprintf('%d %g', S.a.p == S.b.p, S.b.p.data);
delete(fn);
end

function s = r_deleted_handle_save()
fn = scratch_mat();
h = HandleHolder();
h.data = 1;
delete(h);
save(fn, 'h');
S = load(fn);
s = sprintf('%s %d', class(S.h), isvalid(S.h));
delete(fn);
end

function s = r_load_runs_no_ctor()
fn = scratch_mat();
c = CtorLog();
c.n = 4;
save(fn, 'c');
vlog('saved');
S = load(fn);
s = sprintf('%s %g', logged_text(), S.c.n);
delete(fn);
end

function s = r_loaded_handle_kind()
fn = scratch_mat();
h = HandleHolder();
h.data = 1;
save(fn, 'h');
S = load(fn);
s = sprintf('%s %d %d %d', class(S.h), isa(S.h, 'handle'), isobject(S.h), isvalid(S.h));
delete(fn);
end

function s = r_loaded_method_works()
fn = scratch_mat();
h = HandleHolder();
h.data = [1 2];
g = h;
save(fn, 'h');
S = load(fn);
S.h.bump();
s = sprintf('%s %s', mat2str(S.h.data), mat2str(g.data));
delete(fn);
end

function s = r_load_statement_object()
fn = scratch_mat();
h = HandleHolder();
h.data = [1 2];
save(fn, 'h');
h.data = 5;
load(fn);
s = mat2str(h.data);
delete(fn);
end

function s = r_save_whole_workspace()
fn = scratch_mat();
h = HandleHolder(); %#ok<NASGU>
o = ValueBox(); %#ok<NASGU>
v = [1 2]; %#ok<NASGU>
save(fn);
S = load(fn);
s = sprintf('%s %s %s', strjoin(sort(fieldnames(S))', ','), class(S.h), class(S.o));
delete(fn);
end

function s = r_load_selected_object()
fn = scratch_mat();
h = HandleHolder();
h.data = 2;
v = 1; %#ok<NASGU>
save(fn, 'h', 'v');
S = load(fn, 'h');
s = sprintf('%s %g', strjoin(fieldnames(S)', ','), S.h.data);
delete(fn);
end

function s = r_struct_with_handle_and_fn()
fn = scratch_mat();
h = HandleHolder();
h.data = 3;
st.h = h;
st.f = @(x) x + 1; %#ok<STRNU>
save(fn, 'st');
S = load(fn);
s = sprintf('%s %g %g %d', class(S.st.h), S.st.h.data, S.st.f(1), S.st.h == h);
delete(fn);
end

% --- function handles (appendix A #113) -------------------------------------------------------------

function s = r_fn_anon()
fn = scratch_mat();
v = [1 2 3];
f = @() v; %#ok<NASGU>
save(fn, 'f');
v(1) = 7; %#ok<NASGU>
S = load(fn);
s = sprintf('%s %s', mat2str(S.f()), class(S.f));
delete(fn);
end

function s = r_fn_builtin()
fn = scratch_mat();
f = @sin; %#ok<NASGU>
save(fn, 'f');
S = load(fn);
s = sprintf('%g %s', S.f(0), class(S.f));
delete(fn);
end

function s = r_fn_local()
fn = scratch_mat();
f = @local_double; %#ok<NASGU>
save(fn, 'f');
S = load(fn);
s = sprintf('%g', S.f(4));
delete(fn);
end

function s = r_fn_captured_handle()
fn = scratch_mat();
h = HandleHolder();
h.data = 1;
f = @() h.data;
f2 = f;
save(fn, 'f');
h.data = 9;
S = load(fn);
s = sprintf('%g %g', S.f(), f2());
delete(fn);
end

function s = r_fn_nested_anon()
fn = scratch_mat();
g = @(y) y * 2;
f = @(x) g(x) + 1; %#ok<NASGU>
save(fn, 'f');
S = load(fn);
s = sprintf('%g', S.f(3));
delete(fn);
end

function s = r_fn_captured_scalar()
fn = scratch_mat();
v = 2;
f = @(x) x + v; %#ok<NASGU>
save(fn, 'f');
v = 5; %#ok<NASGU>
S = load(fn);
s = sprintf('%g', S.f(1));
delete(fn);
end

function s = r_fn_in_cell()
fn = scratch_mat();
c = {@sin, @(x) x + 1}; %#ok<NASGU>
save(fn, 'c');
S = load(fn);
s = sprintf('%g %g', S.c{1}(0), S.c{2}(1));
delete(fn);
end

function s = r_fn_anon_args()
fn = scratch_mat();
f = @(a, b) a * b; %#ok<NASGU>
save(fn, 'f');
S = load(fn);
s = sprintf('%g', S.f(3, 4));
delete(fn);
end

% --- matfile (appendix A #112) -----------------------------------------------------------------------

function s = r_matfile_read()
fn = scratch_mat();
v = [1 2 3]; %#ok<NASGU>
save(fn, 'v');
m = matfile(fn);
x = m.v;
x(1) = 7;
s = mat2str(m.v);
delete(fn);
end

function s = r_matfile_class()
fn = scratch_mat();
v = 1; %#ok<NASGU>
save(fn, 'v');
m = matfile(fn);
s = sprintf('%s %d %d', class(m), isobject(m), isa(m, 'handle'));
delete(fn);
end

function s = r_matfile_props()
fn = scratch_mat();
v = 1; %#ok<NASGU>
save(fn, 'v');
m = matfile(fn);
p = m.Properties;
s = sprintf('%s %d %d %s', class(p), p.Writable, strcmp(p.Source, fn), strjoin(properties(m)', ','));
delete(fn);
end

function s = r_matfile_who()
fn = scratch_mat();
v = 1; %#ok<NASGU>
w = 2; %#ok<NASGU>
save(fn, 'v', 'w');
m = matfile(fn);
n = who(m);
s = sprintf('%s %s', class(n), strjoin(n', ','));
delete(fn);
end

function s = r_matfile_whos()
fn = scratch_mat();
v = [1 2 3]; %#ok<NASGU>
save(fn, 'v');
m = matfile(fn);
w = whos(m);
s = sprintf('%s %s %s', w(1).name, w(1).class, mat2str(w(1).size));
delete(fn);
end

function s = r_matfile_size()
fn = scratch_mat();
v = [1 2 3; 4 5 6]; %#ok<NASGU>
save(fn, 'v');
m = matfile(fn);
s = mat2str(size(m, 'v'));
delete(fn);
end

function s = r_matfile_size_two_outputs()
fn = scratch_mat();
v = [1 2 3; 4 5 6]; %#ok<NASGU>
save(fn, 'v');
m = matfile(fn);
[r, c] = size(m, 'v');
s = sprintf('%d %d', r, c);
delete(fn);
end

function s = r_matfile_size_bare()
fn = scratch_mat();
v = [1 2 3]; %#ok<NASGU>
save(fn, 'v');
m = matfile(fn);
s = mat2str(size(m));
delete(fn);
end

function s = r_matfile_partial_read()
fn = scratch_mat();
v = [1 2 3]; %#ok<NASGU>
save(fn, 'v');
m = matfile(fn);
s = mat2str(m.v(1, 2:3));
delete(fn);
end

function s = r_matfile_partial_end()
fn = scratch_mat();
v = [1 2 3]; %#ok<NASGU>
save(fn, 'v');
m = matfile(fn);
s = mat2str(m.v(1, end));
delete(fn);
end

function s = r_matfile_indexed_write()
fn = scratch_mat();
v = [1 2 3]; %#ok<NASGU>
save(fn, 'v');
m = matfile(fn, 'Writable', true);
m.v(1, 2) = 8;
S = load(fn);
s = mat2str(S.v);
delete(fn);
end

function s = r_matfile_whole_write()
fn = scratch_mat();
v = [1 2 3]; %#ok<NASGU>
save(fn, 'v');
m = matfile(fn, 'Writable', true);
m.v = [4 5 6];
S = load(fn);
s = mat2str(S.v);
delete(fn);
end

function s = r_matfile_new_var()
fn = scratch_mat();
v = [1 2 3]; %#ok<NASGU>
save(fn, 'v');
m = matfile(fn, 'Writable', true);
m.w = 5;
S = load(fn);
s = sprintf('%s %g', strjoin(sort(fieldnames(S))', ','), S.w);
delete(fn);
end

function s = r_matfile_growth()
fn = scratch_mat();
v = [1 2 3]; %#ok<NASGU>
save(fn, 'v');
m = matfile(fn, 'Writable', true);
m.v(1, 5) = 1;
S = load(fn);
s = mat2str(S.v);
delete(fn);
end

function s = r_matfile_not_writable()
fn = scratch_mat();
v = [1 2 3]; %#ok<NASGU>
save(fn, 'v');
m = matfile(fn);
m.v(1, 2) = 8;
s = 'wrote';
delete(fn);
end

function s = r_matfile_set_writable()
fn = scratch_mat();
v = [1 2 3]; %#ok<NASGU>
save(fn, 'v');
m = matfile(fn);
m.Properties.Writable = true;
m.v(1, 2) = 8;
S = load(fn);
s = sprintf('%s %d', mat2str(S.v), m.Properties.Writable);
delete(fn);
end

function s = r_matfile_missing_var()
fn = scratch_mat();
v = 1; %#ok<NASGU>
save(fn, 'v');
m = matfile(fn);
try
    x = m.zz; %#ok<NASGU>
    s = 'read';
catch err
    s = ['ERR ' nopath(err.message, fn)];
end
delete(fn);
end

function s = r_matfile_nofile_read()
fn = scratch_mat();
m = matfile(fn);
e1 = exist(fn, 'file');
try
    x = m.v; %#ok<NASGU>
    s = 'read';
catch err
    s = ['ERR ' nopath(err.message, fn)];
end
s = sprintf('%d %d %s', e1, exist(fn, 'file'), s);
if exist(fn, 'file')
    delete(fn);
end
end

function s = r_matfile_nofile_write()
fn = scratch_mat();
m = matfile(fn, 'Writable', true);
m.x = 1;
S = load(fn);
s = sprintf('%d %s', exist(fn, 'file'), strjoin(fieldnames(S)', ','));
delete(fn);
end

function s = r_matfile_alias()
fn = scratch_mat();
v = [1 2 3]; %#ok<NASGU>
save(fn, 'v');
m = matfile(fn, 'Writable', true);
m2 = m;
m2.v(1, 2) = 8;
s = sprintf('%s %d', mat2str(m.v), m == m2);
delete(fn);
end

function s = r_matfile_rereads()
fn = scratch_mat();
v = [1 2 3]; %#ok<NASGU>
save(fn, 'v');
m = matfile(fn);
a = m.v;
v = [9 9]; %#ok<NASGU>
save(fn, 'v');
s = sprintf('%s %s', mat2str(a), mat2str(m.v));
delete(fn);
end

function s = r_matfile_object_read()
fn = scratch_mat();
h = HandleHolder();
h.data = [1 2];
save(fn, 'h');
m = matfile(fn);
k = m.h;
s = sprintf('%s %d %s', class(k), k == h, mat2str(k.data));
delete(fn);
end

function s = r_matfile_struct_field_read()
% R2025b refuses a dot past the variable ("MatFile objects only support '()' indexing"); this
% build reads the variable whole and indexes it - the accepted divergence, ADR 0167.
fn = scratch_mat();
st.a = [1 2]; %#ok<STRNU>
save(fn, 'st');
m = matfile(fn);
s = mat2str(m.st.a);
delete(fn);
end

function s = r_matfile_cell_brace_read()
% The same divergence for a brace past the variable.
fn = scratch_mat();
c = {1, [2 3]}; %#ok<NASGU>
save(fn, 'c');
m = matfile(fn);
s = mat2str(m.c{2});
delete(fn);
end

function s = r_matfile_bad_arg()
m = matfile(5); %#ok<NASGU>
s = 'made';
end

function s = r_matfile_no_ext()
fn = tempname;
v = 1; %#ok<NASGU>
save(fn, 'v');
m = matfile(fn);
s = sprintf('%g %d', m.v, strcmp(m.Properties.Source, [fn '.mat']));
delete([fn '.mat']);
end

function s = r_matfile_scalar_read()
fn = scratch_mat();
v = 5; %#ok<NASGU>
save(fn, 'v');
m = matfile(fn);
s = sprintf('%g %s', m.v + 1, mat2str(size(m, 'v')));
delete(fn);
end

function s = r_matfile_isobject()
fn = scratch_mat();
v = 1; %#ok<NASGU>
save(fn, 'v');
m = matfile(fn);
s = sprintf('%d %d %d', isstruct(m), ishandle(m), isvalid(m));
delete(fn);
end

function s = r_matfile_two_index_read()
fn = scratch_mat();
v = magic(3); %#ok<NASGU>
save(fn, 'v');
m = matfile(fn);
s = sprintf('%s %s', mat2str(m.v(2, :)), mat2str(m.v(:, 1)'));
delete(fn);
end

function s = r_matfile_props_write_bad()
fn = scratch_mat();
v = 1; %#ok<NASGU>
save(fn, 'v');
m = matfile(fn);
try
    m.Properties.Writable = 'yes';
    s = 'set';
catch err
    s = ['ERR ' err.message];
end
s = sprintf('%s %d', s, m.Properties.Writable);
delete(fn);
end

function s = r_matfile_eq()
fn = scratch_mat();
v = 1; %#ok<NASGU>
save(fn, 'v');
m = matfile(fn);
n = matfile(fn);
m2 = m;
s = sprintf('%d %d', m == n, m == m2);
delete(fn);
end

% --- the forms that agreed before this stage (value_isolation_lifetime's g_ cases) -----------------

function s = r_load_over_alias()
fn = scratch_mat();
v = [1 2 3];
w = v;
save(fn, 'v');
v = [9 9 9]; %#ok<NASGU>
load(fn);
v(2) = 5;
s = sprintf('%s %s', mat2str(v), mat2str(w));
delete(fn);
end

function s = r_two_loads_independent()
fn = scratch_mat();
v = [1 2 3]; %#ok<NASGU>
save(fn, 'v');
S1 = load(fn);
S2 = load(fn);
S1.v(1) = 0;
s = sprintf('%s %s', mat2str(S1.v), mat2str(S2.v));
delete(fn);
end

function s = r_save_cell_detached_children()
fn = scratch_mat();
c = {[1 2], [1 2]};
d = c;
d{1}(1) = 9;
save(fn, 'c', 'd');
S = load(fn);
S.c{2}(2) = 0;
s = sprintf('%s %s %s', mat2str(S.c{1}), mat2str(S.d{1}), mat2str(S.c{2}));
delete(fn);
end

function s = r_load_struct_aliases_independent()
fn = scratch_mat();
st.a = [1 2];
t = st; %#ok<NASGU>
save(fn, 'st', 't');
S = load(fn);
S.st.a(1) = 7;
s = mat2str(S.t.a);
delete(fn);
end

function s = r_save_append()
fn = scratch_mat();
v = [1 2 3]; %#ok<NASGU>
save(fn, 'v');
u = 5; %#ok<NASGU>
save(fn, 'u', '-append');
S = load(fn);
s = strjoin(sort(fieldnames(S))', ',');
delete(fn);
end

function s = r_save_complex_alias()
fn = scratch_mat();
z = [1 2 3];
y = z;
y(2) = 1i; %#ok<NASGU>
save(fn, 'z', 'y');
S = load(fn);
s = sprintf('%d %d', isreal(S.z), isreal(S.y));
delete(fn);
end

function s = r_load_in_function_binds()
fn = scratch_mat();
q = [4 5]; %#ok<NASGU>
save(fn, 'q');
clear q
load(fn);
r = q;
r(1) = 0;
s = sprintf('%s %s', mat2str(q), mat2str(r));
delete(fn);
end
