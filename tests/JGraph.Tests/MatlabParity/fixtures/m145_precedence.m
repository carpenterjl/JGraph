% m145_precedence.m -- M145's function precedence: which layer answers a name. A file takes a
% built-in's name unless a method of the built-in claims the arguments; a nested function beats a
% local one, a local one beats a private file, a private file beats a user method, a user method
% beats the current folder's file; a bound name beats them all; builtin() reaches past a shadow;
% exist and which name the layer that answers; a shadowing file draws MATLAB's warning. Every row
% is one of the plan's R2025b probes (tools/matlab-checklist/precedence-probes), written as a line.
%
% A fixture is one file, so this one builds the folders it needs under tempdir -- a current folder
% of shadowing files with a private/ folder under it, a library, a folder where builtin.m shadows
% builtin, one where clear.m shadows clear, and one holding the shadows MATLAB's own .m library
% cannot survive -- and removes them at the end. Files that take numel, plus, mod, eps and mean live
% in that last folder, entered only for their own rows, because fullfile, mat2str and table are .m
% files in MATLAB and they call numel; while the folder is current nothing here prints. The
% shadowing warning is recorded at the first cd and at addpath; JGraph prints it once per name,
% so the warning rows come first.
%
% The rows JGraph decides differently are div=ADR0149: exist answers 5 for a name JGraph holds as
% a built-in where MATLAB holds a .m file (mean), builtin reaches such a name, which -all lists the
% layers and not MATLAB's twenty-odd @class methods, a folder added at the end of the path still
% shadows, a deleted shadowing file falls through to the next layer where MATLAB errors, and
% JGraph's library is immune to a shadow that breaks MATLAB's. clear all is not here, since nothing
% in the workspace survives it; stess_85.m has it.

start = pwd;
root = fullfile(tempdir, 'm145_precedence');
if exist(root, 'dir') == 7
    rmdir(root, 's');
end
cur = fullfile(root, 'cur');
lib = fullfile(root, 'lib');
cur2 = fullfile(root, 'cur2');
clr = fullfile(root, 'clr');
shd = fullfile(root, 'shd');
endlib = fullfile(root, 'endlib');
mkdir(cur);
mkdir(fullfile(cur, 'private'));
mkdir(fullfile(cur, 'fixdir'));
mkdir(lib);
mkdir(cur2);
mkdir(clr);
mkdir(shd);
mkdir(endlib);

% The current folder: files that take built-in names, a class, plain files, and the probe
% functions that must run from inside the folder because private/ is visible only from there.
put(fullfile(cur, 'max.m'), { ...
    'function y = max(varargin)', ...
    'if nargin == 1 && ischar(varargin{1}) && strcmp(varargin{1}, ''handle'')', ...
    '    y = @max;', ...
    'else', ...
    '    y = -999;', ...
    'end', ...
    'end'});
put(fullfile(cur, 'sum.m'), {'function y = sum(varargin)', 'y = -555;', 'end'});
put(fullfile(cur, 'foo.m'), {'function y = foo(x)', 'y = ''file foo'';', 'end'});
put(fullfile(cur, 'dup.m'), {'function y = dup()', 'y = ''cur dup'';', 'end'});
put(fullfile(cur, 'plain.m'), {'function y = plain()', 'y = ''plain'';', 'end'});
put(fullfile(cur, 'Meth.m'), { ...
    'classdef Meth', ...
    '    methods', ...
    '        function y = foo(obj)', ...
    '            y = ''method foo'';', ...
    '        end', ...
    '        function y = bar(obj)', ...
    '            y = ''method bar'';', ...
    '        end', ...
    '    end', ...
    'end'});
put(fullfile(cur, 'private', 'secret.m'), {'function y = secret()', 'y = ''private secret'';', 'end'});
put(fullfile(cur, 'private', 'dup.m'), {'function y = dup()', 'y = ''private dup'';', 'end'});
put(fullfile(cur, 'private', 'bar.m'), {'function y = bar(x)', 'y = ''private bar'';', 'end'});
put(fullfile(cur, 'private', 'secret2.m'), { ...
    'function y = secret2()', ...
    'y = [''secret2->'' secret() ''|'' dup()];', ...
    'end'});
put(fullfile(cur, 'nestcheck.m'), { ...
    'function out = nestcheck()', ...
    'o = Meth();', ...
    'out = [bar(o) ''|'' inner(o) ''|'' foo(o)];', ...
    '    function y = inner(x)', ...
    '        y = ''nested inner'';', ...
    '    end', ...
    'end', ...
    'function y = inner(x)', ...
    'y = ''local inner'';', ...
    'end'});
put(fullfile(cur, 'm145_scopes.m'), { ...
    'function out = m145_scopes()', ...
    'o = Meth();', ...
    'hs = @sum;', ...
    'out = {foo(o), foo(1), sum([1 2]), hs([1 2]), feval(''sum'', [1 2]), bar(o)};', ...
    'end', ...
    'function y = foo(x)', ...
    'y = ''local foo'';', ...
    'end', ...
    'function y = sum(x)', ...
    'y = -5;', ...
    'end'});
put(fullfile(cur, 'm145_private.m'), { ...
    'function [out, h] = m145_private()', ...
    'out = [secret() ''|'' dup() ''|'' secret2()];', ...
    'h = @secret;', ...
    'end'});
put(fullfile(cur, 'm145_exist.m'), { ...
    'function out = m145_exist()', ...
    'out = [exist(''max''), exist(''max'', ''builtin''), exist(''max'', ''file''), ...', ...
    '       exist(''secret''), exist(''secret'', ''file''), exist(''loc''), exist(''plain''), ...', ...
    '       exist(''nosuch''), exist(''fixdir''), exist(''fixdir'', ''dir''), exist(''fixdir'', ''file''), ...', ...
    '       exist(''sin''), exist(''sin'', ''file''), exist(''sin'', ''builtin''), exist(''q'')];', ...
    'end', ...
    'function y = loc()', ...
    'y = 1;', ...
    'end', ...
    'function y = q()', ...
    'y = 2;', ...
    'end'});
put(fullfile(cur, 'm145_which.m'), { ...
    'function [one, all] = m145_which()', ...
    'one = {which(''max''), which(''sin''), which(''secret''), which(''loc''), which(''nosuch''), which(''plain'')};', ...
    'all = {which(''max'', ''-all''), which(''-all'', ''max''), which(''nosuch'', ''-all''), ...', ...
    '       which(''plain'', ''-all''), which(''secret'', ''-all''), which(''loc'', ''-all'')};', ...
    'end', ...
    'function y = loc()', ...
    'y = 1;', ...
    'end'});
put(fullfile(cur, 'm145_sizes.m'), { ...
    'function mn = m145_sizes()', ...
    '[m, n] = builtin(''size'', [1 2; 3 4; 5 6]);', ...
    'mn = [m n];', ...
    'end'});

% A library folder: a file that takes abs, and the function-storage rows.
put(fullfile(lib, 'abs.m'), {'function y = abs(varargin)', 'y = -777;', 'end'});
put(fullfile(lib, 'reads_gain.m'), {'function y = reads_gain(x)', 'y = gain * x;', 'end'});
put(fullfile(lib, 'calls_helper.m'), {'function s = calls_helper()', 's = helper();', 'end'});
put(fullfile(lib, 'counter.m'), { ...
    'function n = counter()', ...
    'persistent k', ...
    'if isempty(k), k = 0; end', ...
    'k = k + 1;', ...
    'n = k;', ...
    'end'});
put(fullfile(lib, 'maker5.m'), { ...
    'function h = maker5()', ...
    'h = @(x) [helper() ''-'' num2str(x)];', ...
    'end', ...
    'function s = helper()', ...
    's = ''maker5-helper'';', ...
    'end'});
put(fullfile(lib, 'scriptA.m'), { ...
    'a_answer = helper();', ...
    'function s = helper()', ...
    's = ''A-helper'';', ...
    'end'});
put(fullfile(lib, 'scriptB.m'), { ...
    'b_answer = helper();', ...
    'function s = helper()', ...
    's = ''B-helper'';', ...
    'end'});

% A folder where builtin.m shadows builtin itself, one where clear.m shadows clear, one holding
% the shadows MATLAB's library cannot survive, and one that goes to the end of the path.
put(fullfile(cur2, 'builtin.m'), {'function y = builtin(varargin)', 'y = -1;', 'end'});
put(fullfile(clr, 'clear.m'), { ...
    'function clear(varargin)', ...
    'assignin(''caller'', ''CLEAR_SEEN'', numel(varargin));', ...
    'end'});
put(fullfile(shd, 'numel.m'), {'function y = numel(varargin)', 'y = -333;', 'end'});
put(fullfile(shd, 'plus.m'), {'function y = plus(varargin)', 'y = -444;', 'end'});
put(fullfile(shd, 'mod.m'), {'function y = mod(varargin)', 'y = -888;', 'end'});
put(fullfile(shd, 'eps.m'), {'function y = eps(varargin)', 'y = -777;', 'end'});
put(fullfile(shd, 'mean.m'), {'function y = mean(varargin)', 'y = -222;', 'end'});
put(fullfile(endlib, 'mod.m'), {'function y = mod(varargin)', 'y = -666;', 'end'});

% ---------------------------------------------------------------------------------------------
% The warning, at cd and at addpath.
% ---------------------------------------------------------------------------------------------
lastwarn('');
cd(cur);
w = lastwarn;
chk('cd_warns', contains(w, 'has the same name as a MATLAB built-in'));
chk('cd_warns_rename', contains(w, 'We suggest you rename the function to avoid a potential name conflict.'));
lastwarn('');
addpath(lib);
chk('addpath_warning', lastwarn);
chk('abs_exist', exist('abs'));
chk('abs_which', endsWith(which('abs'), fullfile('lib', 'abs.m')));

% ---------------------------------------------------------------------------------------------
% A file takes a built-in's name wherever no method of the built-in claims the arguments.
% ---------------------------------------------------------------------------------------------
chk('max_cell', max({1}));
chk('max_none', max());
chk('max_char', max('x'));
chk('max_string', max("x"));
chk('max_struct', max(struct('a', 1)));
chk('max_handle', max(@sin));
chk('max_double_cell', max(1, {1}));
chk('max_cell_double', max({1}, 1));
chk('max_char_double', max('x', 1));
chk('max_three_string', max(1, 2, "x"));
chk('max_three_cell', max(1, 2, {1}));
chk('max_all_flag', max([1 2], [], "all"));
chk('abs_cell', abs({-3}));

% ---------------------------------------------------------------------------------------------
% The built-in's own method keeps the name for the classes it is defined on; the leftmost of
% the numeric, logical and char arguments decides, and a second such argument never blocks.
% ---------------------------------------------------------------------------------------------
chk('max_double', max([1 5 3]));
chk('max_two', max(1, 2));
chk('max_logical', max(true, false));
chk('max_int8', max(int8(3), int8(1)));
chk('max_int8_double', max(int8(3), 1));
chk('max_int8_fraction', max(int8(3), 2.5));
chk('max_double_single', max(1, single(2)));
chk('abs_logical', abs(true));
chk('abs_double', abs(-2));

% ---------------------------------------------------------------------------------------------
% Per name: a string in second position blocks the built-in for numel and not for plus; a bare
% constant reaches the file; the same rows the library's own code would break under.
% ---------------------------------------------------------------------------------------------
cd(shd);
v_numel_double_string = numel(1, "x");
v_numel_string = numel("x");
v_numel_double = numel([1 2 3]);
v_plus = plus(1, 2);
v_plus_string = valueor(@() plus(1, "x"), 'error');
v_mod = mod(5, 2);
v_eps_bare = eps;
v_eps_double = eps(1);
v_eps_string = eps("double");
v_mean = mean([1 2 3]);
v_exist_mean = exist('mean');
v_exist_mean_builtin = exist('mean', 'builtin');
v_exist_mean_file = exist('mean', 'file');
v_builtin_mean = valueor(@() builtin('mean', [1 2 3]), 'error');
v_table_under_numel = valueor(@() class(table(1)), 'error');
v_which_all_under_numel = valueor(@() size(which('numel', '-all'), 1), 'error');
delete(fullfile(shd, 'eps.m'));
v_eps_deleted = valueor(@() eps, 'error');
cd(cur);
chk('numel_double_string', v_numel_double_string);
chk('numel_string', v_numel_string);
chk('numel_double', v_numel_double);
chk('plus_double', v_plus);
chk('plus_double_string', v_plus_string);
chk('mod_double', v_mod);
chk('eps_bare', v_eps_bare);
chk('eps_double', v_eps_double);
chk('eps_string', v_eps_string);
chk('mean_file', v_mean);
chkdiv('exist_mean', v_exist_mean);
chkdiv('exist_mean_builtin', v_exist_mean_builtin);
chk('exist_mean_file', v_exist_mean_file);
chkdiv('builtin_mean', v_builtin_mean);
chkdiv('table_under_numel', v_table_under_numel);
chkdiv('which_all_under_numel', v_which_all_under_numel);
chkdiv('eps_deleted', v_eps_deleted);

% ---------------------------------------------------------------------------------------------
% A discarded call binds ans to whichever layer answered; a loop dispatches per iteration.
% ---------------------------------------------------------------------------------------------
max([1 5 3]);
a = ans;
max({1});
chk('ans_method', a);
chk('ans_file', ans);
x = 0;
y = 0;
for k = 1:3
    x = x + max({1});
    y = y + max(1, k);
end
chk('loop_file', x);
chk('loop_method', y);

% ---------------------------------------------------------------------------------------------
% Handles: taken inside max.m, taken beside it and kept after cd, through cellfun and feval,
% and by name through feval and str2func.
% ---------------------------------------------------------------------------------------------
h = max('handle');
chk('inside_double', h([1 5 3]));
chk('inside_cell', h({1}));
chk('inside_func2str', strcmp(func2str(h), func2str(@max)));
hm = @max;
cd(lib);
chk('beside_cell_after_cd', hm({1}));
chk('beside_double_after_cd', hm([1 5 3]));
chkshape('beside_cellfun', cellfun(hm, {{1}, [1 5 3]}));
chk('beside_feval', feval(hm, {1}));
cd(cur);
chk('feval_name_cell', feval('max', {1}));
chk('feval_name_double', feval('max', [1 5 3]));
chk('str2func_cell', feval(str2func('max'), {1}));

% ---------------------------------------------------------------------------------------------
% A user object: a method of its class wins in either position; without the method the object
% falls to the file; a double keeps the built-in with the file present; the handle agrees.
% ---------------------------------------------------------------------------------------------
o = Meth();
chk('object_file', sum(o));
chk('object_second_file', sum(1, o));
chk('double_method_with_file', sum([1 2]));
hs = @sum;
chk('object_handle', hs(o));
cd(lib);
chk('object_handle_after_cd', hs(o));
chk('object_no_file', valueor(@() sum(o), 'error'));
cd(cur);
chk('method_foo', foo(o));
chk('file_foo', foo(1));
foo = @(x) 'bound';
chk('bound_foo', foo(o));
clear foo
chk('method_foo_again', foo(o));

% ---------------------------------------------------------------------------------------------
% private/ is visible to the folder's own files and to nobody else; local, private and nested.
% ---------------------------------------------------------------------------------------------
chk('private_from_script', refused(@() secret()));
chk('dup_from_script', dup());
[got, hp] = m145_private();
chk('private_from_file', got);
cd(lib);
chk('private_handle_after_cd', hp());
cd(cur);
got = m145_scopes();
chk('local_over_method', got{1});
chk('local_over_file', got{2});
chk('local_over_method_double', got{3});
chk('local_through_handle', got{4});
chk('local_through_feval', got{5});
chk('private_over_method', got{6});
chk('nestcheck', nestcheck());

% ---------------------------------------------------------------------------------------------
% clear; as a bare statement runs a clear.m that shadows it, and the real clear returns with
% the folder.
% ---------------------------------------------------------------------------------------------
cd(clr);
z = 5;
clear;
c1 = [exist('z'), exist('CLEAR_SEEN'), CLEAR_SEEN];
clear z
c2 = [exist('z'), CLEAR_SEEN];
cd(cur);
clear z CLEAR_SEEN
chkshape('clear_file_bare', c1);
chkshape('clear_file_named', c2);
chk('clear_real', exist('z'));

% ---------------------------------------------------------------------------------------------
% exist answers by the layers; which names the layer that answers, and -all names every one.
% ---------------------------------------------------------------------------------------------
chkshape('exist_fifteen', m145_exist());
chk('exist_max', exist('max'));
chk('exist_max_file', exist('max', 'file'));
chk('exist_plain', exist('plain'));
[one, all] = m145_which();
chk('which_max', endsWith(one{1}, fullfile('cur', 'max.m')));
chk('which_sin', contains(one{2}, 'built-in'));
chk('which_secret', endsWith(one{3}, fullfile('private', 'secret.m')));
chk('which_loc', endsWith(one{4}, 'm145_which.m'));
chk('which_nosuch', isempty(one{5}));
chk('which_plain', endsWith(one{6}, 'plain.m'));
wa = all{1};
chk('which_all_is_column', iscell(wa) && size(wa, 2) == 1);
chkdiv('which_all_count', size(wa, 1));
chk('which_all_file_first', endsWith(wa{1}, 'max.m'));
chk('which_all_builtin_second', contains(wa{2}, 'built-in'));
chk('which_all_either_order', isequal(all{2}, wa));
chkshape('which_all_nosuch', size(all{3}));
chkshape('which_all_plain', size(all{4}));
chkshape('which_all_secret', size(all{5}));
chkshape('which_all_loc', size(all{6}));

% ---------------------------------------------------------------------------------------------
% builtin reaches the built-in past a shadow, with the arguments and output count as written;
% refuses with MATLAB's identifiers; binds ans exactly when the target would; and is itself
% shadowed by a builtin.m in the current folder.
% ---------------------------------------------------------------------------------------------
chk('builtin_class_string', builtin('class', "abc"));
chk('builtin_class_char', builtin('class', 'abc'));
chkshape('builtin_two_outputs', m145_sizes());
chk('builtin_max_double', builtin('max', [1 5 3]));
chk('builtin_string_name', builtin("class", 1));
chk('builtin_through_feval', feval('builtin', 'class', 1));
chk('builtin_through_handle', feval(@builtin, 'class', 1));
chk('builtin_handle_class', class(@builtin));
chk('builtin_exist', exist('builtin'));
chk('builtin_which', contains(which('builtin'), 'built-in'));
chk('builtin_refuses_path_function', idof(@() builtin('plain')));
chk('builtin_refuses_private', idof(@() builtin('secret')));
chk('builtin_refuses_unknown', idof(@() builtin('nosuch')));
chk('builtin_refuses_none', idof(@() builtin()));
chk('builtin_refuses_number', idof(@() builtin(1)));
chk('builtin_refuses_handle', idof(@() builtin(@sin, 1)));
chk('builtin_max_cell_refused', refused(@() builtin('max', {1})));
chk('builtin_size_no_args_refused', refused(@() builtin('size')));
builtin('size', [1 2 3]);
chkshape('builtin_statement_ans', ans);
clear ans
builtin('disp', 'm145: a line through builtin(''disp'', ...)');
chk('builtin_disp_no_ans', exist('ans'));
cd(cur2);
b1 = builtin('class', 1);
b2 = feval('builtin', 'class', 1);
b3 = feval(@builtin, 'class', 1);
b4 = exist('builtin');
b5 = endsWith(which('builtin'), fullfile('cur2', 'builtin.m'));
cd(cur);
chk('builtin_shadowed_call', b1);
chk('builtin_shadowed_feval', b2);
chk('builtin_shadowed_handle', b3);
chk('builtin_shadowed_exist', b4);
chk('builtin_shadowed_which', b5);
chk('builtin_back', builtin('class', 1));

% ---------------------------------------------------------------------------------------------
% A file's functions see their own file and nothing of the caller's workspace.
% ---------------------------------------------------------------------------------------------
gain = 3;
chk('path_function_reads_no_base', refused(@() reads_gain(2)));
chk('local_function_reads_no_base', refused(@() local_reads_gain(2)));
chk('path_function_sees_no_script_helper', refused(@() calls_helper()));
scriptA
scriptB
chk('script_a_helper', a_answer);
chk('script_b_helper', b_answer);
scriptA
chk('script_a_helper_again', a_answer);
counter();
counter();
chk('persistent_counts', counter());
hh = maker5();
cd(root);
chk('anonymous_keeps_its_file', hh(1));
cd(cur);

% ---------------------------------------------------------------------------------------------
% An anonymous body asks the current folder when it is called, not when the handle is made.
% ---------------------------------------------------------------------------------------------
f = @() max({1});
g = @(c) max(c);
chk('anon_cell', f());
chk('anon_double', g([1 5 3]));
chk('anon_arg_cell', g({1}));
chkshape('anon_cellfun', cellfun(@(c) max(c), {{1}}));
chk('anon_in_local', local_anon());
cd(lib);
chk('anon_after_cd_refused', refused(f));
chk('anon_after_cd_double', g([1 5 3]));
h2 = @() max({1});
cd(cur);
chk('anon_made_elsewhere', h2());

% ---------------------------------------------------------------------------------------------
% A folder added at the end of the path: MATLAB's built-ins come before it, JGraph's do not.
% ---------------------------------------------------------------------------------------------
addpath(endlib, '-end');
chkdiv('end_of_path_shadow', valueor(@() mod({1}), 'error'));
chk('end_of_path_method', mod(5, 2));

cd(start);
rmpath(lib);
rmpath(endlib);
rmdir(root, 's');

function put(path, lines)
fid = fopen(path, 'w');
for k = 1:length(lines)
    fprintf(fid, '%s\n', lines{k});
end
fclose(fid);
end

function chk(name, v)
fprintf('CHK|%s|%s|exact\n', name, show(v));
end

function chkdiv(name, v)
fprintf('CHK|%s|%s|div=ADR0149\n', name, show(v));
end

function chkshape(name, v)
fprintf('CHK|%s|%s|shape\n', name, mat2str(double(v)));
end

function s = show(v)
if ischar(v)
    s = v;
elseif isstring(v)
    s = char(v);
elseif isscalar(v) && (isnumeric(v) || islogical(v))
    s = sprintf('%.17g', double(v));
else
    s = ['<' class(v) '>'];
end
end

function no = refused(call)
no = false;
try
    call();
catch
    no = true;
end
end

function id = idof(call)
id = '';
try
    call();
catch e
    id = e.identifier;
end
end

function v = valueor(call, fallback)
try
    v = call();
catch
    v = fallback;
end
end

function s = helper()
s = 'script-helper';
end

function y = local_reads_gain(x)
y = gain * x;
end

function k = local_anon()
hh = @() max({1});
k = hh();
end
