% record: -noFigureWindows
% Open item 205 (ADR 0363): true and false are names in MATLAB, not keywords. A function may take
% one as a parameter, an output or a variable, and the word is then that variable in its body; a
% function whose own text never binds it keeps the built-in whatever eval, load, assignin or a
% called script puts in its workspace; a script and the command line read whatever the workspace
% holds; a local function named true answers the bare word in its file, a true.m on the path only
% the call. The constructors take a trailing 'logical', and `true 2` is refused as a class name.
% Probe p205 (scratch-5b).

root = tempname;
mkdir(root);
writef(root, 'f205_param', {'function r = f205_param(true)', 'r = true + 1;', 'end'});
writef(root, 'f205_deal', {'function r = f205_deal()', '[a, true] = deal(1, 2);', 'r = [a true];', 'end'});
writef(root, 'f205_assign', {'function r = f205_assign()', 'true = 1;', 'r = class(true);', 'end'});
writef(root, 'f205_before', {'function r = f205_before()', 'a = true;', 'true = 5;', 'r = {class(a), true};', 'end'});
writef(root, 'f205_eval', {'function r = f205_eval()', 'w = warning(''off'', ''all'');', 'eval(''true = 7;'');', 'r = {true, eval(''true'')};', 'warning(w);', 'end'});
writef(root, 'f205_assignin', {'function r = f205_assignin()', 'f205_setter();', 'r = true;', 'end'});
writef(root, 'f205_setter', {'function f205_setter()', 'assignin(''caller'', ''true'', 9);', 'end'});
writef(root, 'f205_load', {'function r = f205_load()', 'S.true = 8;', 'file = [tempname ''.mat''];', 'save(file, ''-struct'', ''S'');', 'load(file);', 'delete(file);', 'r = true;', 'end'});
writef(root, 'f205_clear', {'function r = f205_clear()', 'true = 3;', 'clear true', 'r = true;', 'end'});
writef(root, 'f205_exist', {'function r = f205_exist()', 'e1 = exist(''true'');', 'f1 = exist(''false'', ''builtin'');', 'true = 1;', 'r = [e1 f1 exist(''true'') exist(''true'', ''var'') exist(''true'', ''builtin'') true];', 'end'});
writef(root, 'f205_nested_read', {'function r = f205_nested_read()', 'true = 4;', 'r = inner();', '    function v = inner()', '        v = true;', '    end', 'end'});
writef(root, 'f205_nested_write', {'function r = f205_nested_write()', 'true = 0;', 'inner();', 'r = true;', '    function inner()', '        true = 6;', '    end', 'end'});
writef(root, 'f205_nested_param', {'function r = f205_nested_param()', 'a = inner(3);', 'r = {a, class(true)};', '    function v = inner(true)', '        v = true * 2;', '    end', 'end'});
writef(root, 'f205_local_param', {'function r = f205_local_param()', 'a = helper(3);', 'r = {a, class(true), true};', 'end', '', 'function v = helper(true)', 'v = true * 2;', 'end'});
writef(root, 'f205_outname', {'function true = f205_outname()', 'true = 3;', 'end'});
writef(root, 'f205_localtrue', {'function r = f205_localtrue()', 'r = {true, true(2)};', 'end', '', 'function v = true(varargin)', 'v = 42 + nargin;', 'end'});
writef(root, 'f205_handle', {'function r = f205_handle()', 'h = @true;', 'r = {h(), h(1, 2)};', 'end'});
writef(root, 'f205_handle_after', {'function r = f205_handle_after()', 'true = 1;', 'h = @true;', 'r = {h(), true};', 'end'});
writef(root, 'f205_field', {'function r = f205_field()', 's.true = 1;', 's.false = 2;', 'r = {s.true + s.false, fieldnames(s)''};', 'end'});
writef(root, 'f205_switch', {'function r = f205_switch()', 'x = 1 > 0;', 'switch x', '    case false', '        r = ''f'';', '    case true', '        r = ''t'';', 'end', 'end'});
writef(root, 'f205_while', {'function r = f205_while()', 'n = 0;', 'while true', '    n = n + 1;', '    if n == 3, break; end', 'end', 'r = n;', 'end'});
writef(root, 'f205_index', {'function r = f205_index()', 'true = [10 20 30];', 'r = [true(2), true(end), numel(true)];', 'end'});
writef(root, 'f205_global', {'function r = f205_global()', 'global true', 'true = 5;', 'r = f205_global_read();', 'clear global true', 'end'});
writef(root, 'f205_global_read', {'function r = f205_global_read()', 'global true', 'r = true;', 'end'});
writef(root, 'f205_persistent', {'function r = f205_persistent()', 'persistent true', 'if isempty(true), true = 0; end', 'true = true + 1;', 'r = true;', 'end'});
writef(root, 'f205_for', {'function r = f205_for()', 'for true = 1:3', 'end', 'r = true;', 'end'});
writef(root, 'f205_anon_capture', {'function r = f205_anon_capture()', 'true = 5;', 'g = @() true;', 'clear true', 'r = {g(), true};', 'end'});
writef(root, 'f205_anon_builtin', {'function r = f205_anon_builtin()', 'g = @() true;', 'r = g();', 'end'});
writef(root, 'f205_anon_param', {'function r = f205_anon_param()', 'g = @(true) true + 1;', 'r = g(1);', 'end'});
writef(root, 'f205_matrix_shadow', {'function r = f205_matrix_shadow()', 'true = 7;', 'r = [true false true];', 'end'});
writef(root, 'f205_plain', {'function r = f205_plain()', 'r = true;', 'end'});
writef(root, 'f205_run_script', {'function r = f205_run_script()', 's205_script;', 'r = true;', 'end'});
writef(root, 's205_script', {'true = 13;'});
writef(root, 's205_read', {'c205 = true;'});
writef(root, 's205_bind', {'d205 = true;', 'true = 3;', 'e205 = true;', 'clear true', 'f205v = true;'});
writef(root, 'f205_loop', {'function r = f205_loop()', 's = 0;', 'for k = 1:5', '    s = s + true - false;', 'end', 'r = s;', 'end'});
addpath(root);

% --- the constructors (and `true 2`, a class name) ---
u9b_chk('bare_true', @() true);
u9b_chk('bare_false', @() false);
u9b_chk('true2', @() true(2));
u9b_chk('false_like_logical', @() false(1, 3, 'like', [true false]));
u9b_chk('false_like_single', @() false(1, 3, 'like', single(1)));
u9b_chk('true_logical', @() true('logical'));
u9b_chk('true_2_logical', @() true(2, 'logical'));
u9b_chk('false_vec_logical', @() false([1 2], 'logical'));
u9b_chk('true_string_logical', @() true(2, "logical"));
u9b_chk('true_2_Logical', @() true(2, 'Logical'));
u9b_chk('false_2_double', @() false(2, 'double'));
u9b_chk('true_like_then_class', @() true(2, 'like', true, 'logical'));
u9b_chk('true_class_then_like', @() true(2, 'logical', 'like', true));
u9b_chk('true_cell', @() true({2}));
u9b_chk('cmd_word', @() evalc('true 2'));
u9b_chk('cmd_false_word', @() evalc('false 3'));
u9b_chk('cmd_disp', @() evalc('disp true'));

% --- in a function ---
u9b_chk('param', @() f205_param(5));
u9b_chk('deal', @() f205_deal());
u9b_chk('assign', @() f205_assign());
% (R2025b warns MATLAB:lang:ExternalShadowedInEval as eval binds the word; JGraph does not.)
ix_chk('eval_in_fn', one(@() f205_eval()));
u9b_chk('assignin_in_fn', @() f205_assignin());
u9b_chk('load_in_fn', @() f205_load());
u9b_chk('exist', @() f205_exist());
u9b_chk('nested_read', @() f205_nested_read());
u9b_chk('nested_write', @() f205_nested_write());
u9b_chk('nested_param', @() f205_nested_param());
u9b_chk('local_param', @() f205_local_param());
u9b_chk('outname', @() f205_outname());
u9b_chk('localtrue', @() f205_localtrue());
u9b_chk('handle', @() f205_handle());
u9b_chk('handle_after', @() f205_handle_after());
u9b_chk('field', @() f205_field());
u9b_chk('struct_field', @() fieldnames(struct('true', 1, 'false', 2)));
u9b_chk('switch', @() f205_switch());
u9b_chk('while', @() f205_while());
u9b_chk('index', @() f205_index());
u9b_chk('global', @() f205_global());
u9b_chk('persistent', @() f205_persistent());
u9b_chk('for', @() f205_for());
u9b_chk('anon_builtin', @() f205_anon_builtin());
u9b_chk('anon_param', @() f205_anon_param());
u9b_chk('matrix_shadow', @() f205_matrix_shadow());
u9b_chk('script_in_fn', @() f205_run_script());
u9b_chk('loop_in_fn', @() f205_loop());
% R2025b refuses a read the function's text makes a variable before it is bound, or after a clear;
% JGraph reads the built-in there, as it does for every other built-in's name.
ix_chk('before', one(@() f205_before()), 'div=ADR0363');
ix_chk('clear_in_fn', one(@() f205_clear()), 'div=ADR0363');
ix_chk('anon_capture_cleared', one(@() f205_anon_capture()), 'div=ADR0363');

% --- the script's own workspace ---
u9b_chk('base_fresh', @() true);
true = 11;
u9b_chk('base_read', @() true);
u9b_chk('base_class', @() class(true));
e205 = exist('true'); % in the script's workspace, not an anonymous function's
u9b_chk('base_exist', @() e205);
u9b_chk('base_matrix', @() [true false]);
u9b_chk('fn_ignores_base', @() f205_plain());
s205_read;
u9b_chk('base_script_read', @() c205);
true = [4 5 6];
u9b_chk('base_index', @() true(2));
u9b_chk('base_end', @() true(end));
clear true
u9b_chk('base_after_clear', @() true);
e205 = exist('true');
u9b_chk('base_exist_after', @() e205);
s205_bind;
u9b_chk('base_script_bind', @() {d205, e205, f205v});
true = 5;
g205 = @() true;
clear true
u9b_chk('base_anon_capture', @() g205());
h205 = @() true;
true = 6;
u9b_chk('base_anon_late', @() h205());
clear true
global true
true = 21;
u9b_chk('base_global', @() f205_global_read());
u9b_chk('base_global_read', @() true);
clear global true
u9b_chk('base_after_global', @() true);

% --- loops: compiled while the words are the built-ins, walked once one is bound ---
s = 0;
for k = 1:5
    s = s + true - false;
end
u9b_chk('loop_plain', @() s);
false = 0;
k205 = 0;
while ~false
    k205 = k205 + 1;
    if k205 == 4, false = 1; end
end
u9b_chk('loop_while_shadow', @() {k205, false});
clear false
s = 0;
for k = 1:4
    s = s + true;
    if k == 2, eval('true = 10;'); end
end
u9b_chk('loop_eval_binds', @() s);
clear true
s = 0;
true = 2;
for k = 1:3
    s = s + true;
end
u9b_chk('loop_bound_at_entry', @() s);
clear true

% --- a true.m on the path takes the call and not the bare word ---
shadow = fullfile(root, 'shadow');
mkdir(shadow);
writef(shadow, 'true', {'function v = true(varargin)', 'v = 100 + nargin;', 'end'});
w = warning('off', 'all');
old = cd(shadow);
u9b_chk('file_bare', @() true);
u9b_chk('file_call', @() true(2));
% R2025b keeps the built-in a function resolved before the file appeared; JGraph re-resolves.
ix_chk('file_fn_bare', one(@() f205_plain()), 'div=ADR0363');
u9b_chk('file_exist', @() exist('true'));
cd(old);
warning(w);

rmpath(root);
rmdir(root, 's');

function writef(folder, name, lines)
fid = fopen(fullfile(folder, [name '.m']), 'w');
fprintf(fid, '%s\n', lines{:});
fclose(fid);
end

function s = one(fn)
% A value, or the error it raises, as one line (for a row that is a divergence).
try
    s = u9b_text(fn());
catch err
    s = ['ERR ' err.identifier];
end
end
