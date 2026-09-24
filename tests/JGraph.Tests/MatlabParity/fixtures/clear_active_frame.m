% clear_active_frame.m -- V7 of the value-ownership plan (ADR 0168): every form of clear resolves
% in the active workspace (#64, #65). Each form - clear, clear name, clear -regexp, clear
% variables, clear all, clear global name, and a plain clear of a global link - runs inside a
% cellfun callback, through evalin('caller', ...), and in a script called from a function, each
% with a same-named base variable that must survive; the nested-function forms are in
% nested_workspaces. The base workspace's own clear is the last thing here, and the clear all
% forms come just before it, because clear all takes the globals with it (measured). A regexp
% with ^ or $ is written in function syntax: JGraph's command syntax stops at an operator
% character (logged at V7, not built).

victim = 42; keep = 5; vic_a = 1; vic_b = 2; zeta = 3; x = 11; %#ok<NASGU>
global nw_cg
nw_cg = 9;

% --- inside a cellfun callback ----------------------------------------------------------------------

r = cellfun(@wipe_named, {0});
fprintf('CHK|c_named_in_callback|%d %d|exact\n', r, exist('victim', 'var'));
r = cellfun(@wipe_plain, {0});
fprintf('CHK|c_plain_in_callback|%d %d|exact\n', r, exist('keep', 'var'));
r = cellfun(@wipe_regexp_command, {0});
fprintf('CHK|c_regexp_command_in_callback|%d %d %d|exact\n', r, exist('vic_a', 'var'), exist('zeta', 'var'));
r = cellfun(@wipe_regexp_function, {0});
fprintf('CHK|c_regexp_function_in_callback|%d %d %d|exact\n', r, exist('vic_a', 'var'), exist('zeta', 'var'));
r = cellfun(@wipe_variables, {0});
fprintf('CHK|c_variables_in_callback|%d %d|exact\n', r, exist('keep', 'var'));
r = cellfun(@wipe_global_link, {0});
fprintf('CHK|c_global_link_in_callback|%d %d %d|exact\n', r, exist('nw_cg', 'var'), nw_cg);
r = cellfun(@wipe_two_names, {0});
fprintf('CHK|c_two_names_in_callback|%d %d %d|exact\n', r, exist('victim', 'var'), exist('keep', 'var'));
r = cellfun(@wipe_missing_name, {0});
fprintf('CHK|c_missing_name_in_callback|%d %d|exact\n', r, exist('victim', 'var'));

% --- through evalin('caller', ...) and in a script run from a function ------------------------------

fprintf('CHK|c_evalin_caller_named|%s|exact\n', c_evalin_caller_named());
fprintf('CHK|c_evalin_caller_plain|%s|exact\n', c_evalin_caller_plain());
fprintf('CHK|c_evalin_caller_regexp|%s|exact\n', c_evalin_caller_regexp());
fprintf('CHK|c_script_named|%s|exact\n', c_script_named());
fprintf('CHK|c_script_plain|%s|exact\n', c_script_plain());
fprintf('CHK|c_script_variables|%s|exact\n', c_script_variables());
fprintf('CHK|c_evalin_base_named|%s|exact\n', c_evalin_base_named());
fprintf('CHK|c_base_after_the_calls|%d %d %d %d|exact\n', exist('victim', 'var'), exist('keep', 'var'), exist('x', 'var'), exist('zeta', 'var'));

% --- a persistent under each form, inside its owner ----------------------------------------------------

fprintf('CHK|c_persistent_plain_clear|%d %d|exact\n', pclear_plain('set'), pclear_plain('get'));
fprintf('CHK|c_persistent_clear_variables|%d %d|exact\n', pclear_variables('set'), pclear_variables('get'));
fprintf('CHK|c_persistent_clear_regexp|%d %d|exact\n', pclear_regexp('set'), pclear_regexp('get'));
fprintf('CHK|c_persistent_clear_all|%d %d|exact\n', pclear_all('set'), pclear_all('get'));

% --- clear global and clear all, then the base workspace's own clear ---------------------------------

global nw_cg2
nw_cg2 = 4;
r = cellfun(@wipe_global, {0});
fprintf('CHK|c_global_in_callback|%d %d|exact\n', r, exist('nw_cg', 'var'));
r = cellfun(@wipe_all, {0});
fprintf('CHK|c_all_in_callback|%d %d %d|exact\n', r, exist('keep', 'var'), exist('nw_cg2', 'var'));
fprintf('CHK|c_script_all|%s|exact\n', c_script_all());
global nw_cg3
nw_cg3 = 1;
clear
fprintf('CHK|c_base_plain_clear|%d %d|exact\n', exist('nw_cg3', 'var'), exist('victim', 'var'));
global nw_cg3
fprintf('CHK|c_base_global_survives_its_link|%d|exact\n', nw_cg3);

% --- the callbacks ------------------------------------------------------------------------------------

function y = wipe_named(~)
victim = 7; %#ok<NASGU>
clear victim
y = exist('victim', 'var');
end

function y = wipe_plain(~)
keep = 7; %#ok<NASGU>
clear
y = exist('keep', 'var');
end

function y = wipe_regexp_command(~)
vic_a = 1; vic_b = 2; zeta = 3; %#ok<NASGU>
clear -regexp vic_
y = exist('vic_a', 'var') + 10 * exist('zeta', 'var');
end

function y = wipe_regexp_function(~)
vic_a = 1; vic_b = 2; zeta = 3; %#ok<NASGU>
clear('-regexp', '^vic', '^ze');
y = exist('vic_a', 'var') + 10 * exist('zeta', 'var');
end

function y = wipe_variables(~)
keep = 7; %#ok<NASGU>
clear variables
y = exist('keep', 'var');
end

function y = wipe_global_link(~)
global nw_cg
clear nw_cg
y = exist('nw_cg', 'var');
end

function y = wipe_two_names(~)
victim = 7; keep = 8; other = 9; %#ok<NASGU>
clear victim keep
y = exist('victim', 'var') + 10 * exist('keep', 'var') + 100 * exist('other', 'var');
end

function y = wipe_missing_name(~)
victim = 7; %#ok<NASGU>
clear nobody
y = exist('victim', 'var');
end

function y = wipe_global(~)
global nw_cg
clear global nw_cg
y = exist('nw_cg', 'var');
end

function y = wipe_all(~)
keep = 7; %#ok<NASGU>
clear all %#ok<CLALL>
y = exist('keep', 'var');
end

% --- evalin and scripts ---------------------------------------------------------------------------------

function s = c_evalin_caller_named()
x = 7; keep = 1; %#ok<NASGU>
clear_in_caller('clear x');
s = sprintf('%d %d %d', exist('x', 'var'), exist('keep', 'var'), evalin('base', 'exist(''x'', ''var'')'));
end

function s = c_evalin_caller_plain()
x = 7; keep = 1; %#ok<NASGU>
clear_in_caller('clear');
s = sprintf('%d %d %d', exist('x', 'var'), exist('keep', 'var'), evalin('base', 'exist(''x'', ''var'')'));
end

function s = c_evalin_caller_regexp()
x = 7; keep = 1; %#ok<NASGU>
clear_in_caller('clear(''-regexp'', ''^k'')');
s = sprintf('%d %d %d', exist('x', 'var'), exist('keep', 'var'), evalin('base', 'exist(''keep'', ''var'')'));
end

function clear_in_caller(text)
evalin('caller', text);
end

function s = c_script_named()
x = 7; keep = 1; %#ok<NASGU>
nw_clear_named
s = sprintf('%d %d %d', exist('x', 'var'), exist('keep', 'var'), evalin('base', 'exist(''x'', ''var'')'));
end

function s = c_script_plain()
x = 7; keep = 1; %#ok<NASGU>
nw_clear_plain
s = sprintf('%d %d %d', exist('x', 'var'), exist('keep', 'var'), evalin('base', 'exist(''x'', ''var'')'));
end

function s = c_script_variables()
x = 7; keep = 1; %#ok<NASGU>
nw_clear_variables
s = sprintf('%d %d %d', exist('x', 'var'), exist('keep', 'var'), evalin('base', 'exist(''x'', ''var'')'));
end

function s = c_script_all()
x = 7; keep = 1; %#ok<NASGU>
nw_clear_all
s = sprintf('%d %d %d', exist('x', 'var'), exist('keep', 'var'), evalin('base', 'exist(''x'', ''var'')'));
end

function s = c_evalin_base_named()
x = 7; %#ok<NASGU>
evalin('base', 'clear vic_b');
s = sprintf('%d %d', exist('x', 'var'), evalin('base', 'exist(''vic_b'', ''var'')'));
end

% --- a persistent under each form ----------------------------------------------------------------------

function r = pclear_plain(cmd)
persistent p
if isempty(p), p = 0; end
p = p + 5;
if strcmp(cmd, 'set')
    clear
    r = exist('p', 'var');
else
    r = p;
end
end

function r = pclear_variables(cmd)
persistent p
if isempty(p), p = 0; end
p = p + 5;
if strcmp(cmd, 'set')
    clear variables
    r = exist('p', 'var');
else
    r = p;
end
end

function r = pclear_regexp(cmd)
persistent p
if isempty(p), p = 0; end
p = p + 5;
if strcmp(cmd, 'set')
    clear('-regexp', '^p$');
    r = exist('p', 'var');
else
    r = p;
end
end

function r = pclear_all(cmd)
persistent p
if isempty(p), p = 0; end
p = p + 5;
if strcmp(cmd, 'set')
    clear all %#ok<CLALL>
    r = exist('p', 'var');
else
    r = p;
end
end
