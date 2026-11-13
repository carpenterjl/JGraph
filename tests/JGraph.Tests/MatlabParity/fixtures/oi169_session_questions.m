% record: -noFigureWindows
% Open items 169 and 245: the session questions K0's k0_env asked a compiled program and JGraph had no
% builtin for - ver, prefdir, userpath, usejava, feature('numcores'), inmem, mlock, munlock and
% mislocked. Machine facts (paths, dates of JGraph's build, core counts, the header's lines) are asked
% as classes, shapes and relations; ver's product rows and a toolbox's Contents.m are compared as laid
% out, blanks shown as dots.

% --- ver: the struct ---------------------------------------------------------------------------
u2_chk('ver_class', @() class(ver));
u2_chk('ver_fields', @() strjoin(fieldnames(ver)', ','));
u2_chk('ver_is_row', @() size(ver, 1) == 1 && size(ver, 2) > 1);
u2_chk('ver_sorted', @() issorted_names(ver));
u2_chk('ver_matlab_name', @() getfield(ver('MATLAB'), 'Name'));
u2_chk('ver_matlab_version', @() getfield(ver('MATLAB'), 'Version'));
u2_chk('ver_matlab_release', @() getfield(ver('MATLAB'), 'Release'));
u2_chk('ver_matlab_date', @() getfield(ver('MATLAB'), 'Date'));
u2_chk('ver_matlab_size', @() size(ver('MATLAB')));
u2_chk('ver_lower', @() getfield(ver('matlab'), 'Name'));
u2_chk('ver_string', @() getfield(ver("MATLAB"), 'Name'));
u2_chk('ver_general', @() getfield(ver('general'), 'Name'));
u2_chk('ver_tail', @() getfield(ver('Toolbox/Matlab'), 'Name'));
u2_chk('ver_backslash', @() getfield(ver('toolbox\matlab'), 'Name'));
u2_chk('ver_compiler', @() strjoin(struct2cell(ver('compiler'))', ' | '));
u2_chk('ver_compiler_sdk', @() getfield(ver('compiler_sdk'), 'Name'));
u2_chk('ver_compiler_tail', @() getfield(ver('toolbox/compiler'), 'Name'));
u2_chk('ver_in_list', @() all(ismember({'MATLAB', 'MATLAB Compiler', 'MATLAB Compiler SDK'}, names_of(ver))));
u2_chk('ver_full_name', @() size(ver('MATLAB Compiler')));
u2_chk('ver_nosuch', @() size(ver('oi169nosuch')));
u2_chk('ver_nosuch_class', @() class(ver('oi169nosuch')));
u2_chk('ver_nosuch_fields', @() strjoin(fieldnames(ver('oi169nosuch'))', ','));
u2_chk('ver_empty', @() size(ver('')));
u2_chk('ver_trailing_blank', @() size(ver('MATLAB ')));
u2_chk('ver_number', @() warned_size(@() ver(5)));
u2_chk('ver_cell', @() warned_size(@() ver({'MATLAB'})));
u2_chk('ver_two_inputs', @() ver('a', 'b'));
u2_chk('ver_two_outputs', @() two_ver());
u2_chk('ver_support_fields', @() strjoin(fieldnames(ver('-support'))', ','));
u2_chk('ver_support_licenses', @() class(getfield(ver('-support'), 'Licenses')));

% --- ver: what it prints ------------------------------------------------------------------------
t = evalc('ver(''matlab'')');
lines = printed_lines(t);
u2_chk('ver_disp_count', @() numel(lines));
u2_chk('ver_disp_rules', @() all(lines{1} == '-') && isequal(lines{1}, lines{6}) && numel(lines{1}) == max(cellfun(@numel, lines(2:5))));
div_chk('ver_disp_labels', @() strjoin(cellfun(@(l) extractBefore(l, ':'), lines(2:5), 'UniformOutput', false), ' / '));
u2_chk('ver_disp_java', @() lines{5});
u2_chk('ver_disp_row', @() dotted(lines{7}));
t = evalc('ver(''compiler'')');
lines = printed_lines(t);
u2_chk('ver_disp_compiler', @() dotted(lines{end}));
lastwarn('');
t = evalc('ver(''oi169nosuch'')');
[~, id] = lastwarn;
u2_chk('ver_disp_nosuch_warn', @() id);
u2_chk('ver_disp_nosuch_rows', @() numel(printed_lines(t)) >= 6);

% --- ver: a toolbox's Contents.m on the path ------------------------------------------------------
root = tempname;
makebox(root, 'oi169box', ["% My Toolbox"; "% Version 1.2.3 (R2024a) 05-Mar-2024"; "%"; "% Files"]);
makebox(root, 'oi169norel', ["% No Release Toolbox"; "% Version 2.0 01-Jan-2020"]);
makebox(root, 'oi169nover', ["% No Version Toolbox"; "% Just a line"]);
makebox(root, 'oi169dot', ["% Dot Name Toolbox."; "% Version 3 (Beta 2) 1-Feb-2021"]);
makebox(root, 'oi169long', ["% A Toolbox Whose Name Is Rather Long Indeed, Over Fifty Characters"; "% Version 4.5.6.7.8.9.10.11 (R2023b) 12-Dec-2023"]);
makebox(root, 'oi169longrel', ["% Long Release Toolbox"; "% Version 1.0 (Release Twenty Twenty Five) 2025-06-30"]);
makebox(root, 'oi169date', ["% Date Format Toolbox"; "% Version 1.0 (R2025a) 5-mar-2025"]);
boxes = fullfile(root, {'oi169box', 'oi169norel', 'oi169nover', 'oi169dot', 'oi169long', 'oi169longrel', 'oi169date'});
addpath(boxes{:});
u2_chk('box_values', @() strjoin(struct2cell(ver('oi169box'))', ' | '));
u2_chk('box_case', @() getfield(ver('OI169BOX'), 'Name'));
u2_chk('box_norel', @() strjoin(struct2cell(ver('oi169norel'))', ' | '));
u2_chk('box_norel_release', @() size(getfield(ver('oi169norel'), 'Release')));
u2_chk('box_nover', @() size(ver('oi169nover')));
u2_chk('box_dot', @() strjoin(struct2cell(ver('oi169dot'))', ' | '));
u2_chk('box_long', @() strjoin(struct2cell(ver('oi169long'))', ' | '));
u2_chk('box_longrel', @() strjoin(struct2cell(ver('oi169longrel'))', ' | '));
u2_chk('box_date', @() getfield(ver('oi169date'), 'Date'));
u2_chk('box_by_name', @() size(ver('My Toolbox')));
u2_chk('box_in_list', @() all(ismember({'My Toolbox', 'Dot Name Toolbox', 'No Release Toolbox'}, names_of(ver))));
u2_chk('box_not_listed', @() any(strcmp(names_of(ver), 'No Version Toolbox')));
u2_chk('box_list_sorted', @() issorted_names(ver));
u2_chk('box_disp_row', @() dotted(last_line(evalc('ver(''oi169box'')'))));
u2_chk('box_disp_norel', @() dotted(last_line(evalc('ver(''oi169norel'')'))));
u2_chk('box_disp_long', @() dotted(last_line(evalc('ver(''oi169long'')'))));
u2_chk('box_disp_longrel', @() dotted(last_line(evalc('ver(''oi169longrel'')'))));
u2_chk('box_disp_dot', @() dotted(last_line(evalc('ver(''oi169dot'')'))));
rmpath(boxes{:});
rmdir(root, 's');
u2_chk('box_gone', @() size(ver('oi169box')));

% --- usejava ------------------------------------------------------------------------------------
div_chk('usejava_jvm', @() usejava('jvm'));
div_chk('usejava_awt', @() usejava('awt'));
div_chk('usejava_swing', @() usejava('swing'));
u2_chk('usejava_desktop', @() usejava('desktop'));
u2_chk('usejava_desktop_upper', @() usejava('DESKTOP'));
u2_chk('usejava_nosuch', @() usejava('oi169nosuch'));
u2_chk('usejava_empty', @() usejava(''));
u2_chk('usejava_none', @() usejava());
u2_chk('usejava_number', @() usejava(1));
u2_chk('usejava_cell', @() usejava({'jvm'}));
u2_chk('usejava_two', @() usejava('jvm', 'awt'));
u2_chk('usejava_two_outputs', @() two_usejava());

% --- prefdir ------------------------------------------------------------------------------------
u2_chk('prefdir_class', @() class(prefdir));
u2_chk('prefdir_row', @() size(prefdir, 1));
u2_chk('prefdir_absolute', @() ~isempty(regexp(prefdir, '^[A-Za-z]:\\', 'once')));
u2_chk('prefdir_one', @() isequal(prefdir(1), prefdir));
u2_chk('prefdir_exists', @() exist(prefdir(1), 'dir'));
u2_chk('prefdir_zero', @() isequal(prefdir(0), prefdir));
u2_chk('prefdir_true', @() isequal(prefdir(true), prefdir));
u2_chk('prefdir_text', @() prefdir('x'));
u2_chk('prefdir_string', @() prefdir("1"));
u2_chk('prefdir_vector', @() prefdir([1 2]));
u2_chk('prefdir_two', @() prefdir(1, 2));
u2_chk('prefdir_two_outputs', @() two_prefdir());

% --- userpath (the query and its refusals; the setters change R2025b's saved setting) ------------
u2_chk('userpath_class', @() class(userpath));
u2_chk('userpath_there', @() isempty(userpath) || isfolder(userpath));
u2_chk('userpath_two', @() userpath('a', 'b'));
u2_chk('userpath_number', @() userpath(5));
u2_chk('userpath_empty', @() userpath(''));
u2_chk('userpath_missing', @() userpath(fullfile(tempdir, 'oi169_no_such_folder')));
u2_chk('userpath_two_outputs', @() two_userpath());

% --- feature('numcores') -------------------------------------------------------------------------
u2_chk('numcores_class', @() class(feature('numcores')));
u2_chk('numcores_whole', @() isscalar(feature('numcores')) && feature('numcores') >= 1 && feature('numcores') == round(feature('numcores')));
u2_chk('numcores_case', @() feature('NumCores') == feature('numcores'));
u2_chk('numcores_string', @() feature("numcores") == feature('numcores'));
u2_chk('numcores_extra', @() feature('numcores', 1));

% --- inmem --------------------------------------------------------------------------------------
u2_chk('inmem_class', @() class(inmem));
u2_chk('inmem_column', @() size(inmem, 2));
u2_chk('inmem_helper', @() any(strcmp(inmem, 'u2_chk')));
u2_chk('inmem_complete_helper', @() any(endsWith(inmem('-completenames'), [filesep 'u2_chk.m'])));
u2_chk('inmem_upper_option', @() isequal(inmem('-COMPLETENAMES'), inmem));
u2_chk('inmem_string_option', @() isequal(inmem("-completenames"), inmem('-completenames')));
u2_chk('inmem_number', @() inmem(5));
u2_chk('inmem_three', @() three_inmem());
u2_chk('inmem_four', @() four_inmem());

% --- mlock, munlock, mislocked ---------------------------------------------------------------------
u2_chk('lock_first', @() oi169_lockme());
u2_chk('lock_second', @() oi169_lockme());
u2_chk('lock_is_locked', @() mislocked('oi169_lockme'));
u2_chk('lock_is_locked_string', @() mislocked("oi169_lockme"));
u2_chk('lock_in_inmem', @() any(strcmp(inmem, 'oi169_lockme')));
clear oi169_lockme
u2_chk('lock_after_clear_name', @() oi169_lockme());
clear functions
u2_chk('lock_after_clear_functions', @() oi169_lockme());
clear all
u2_chk('lock_after_clear_all', @() oi169_lockme());
u2_chk('lock_in_inmem_after_clear', @() any(strcmp(inmem, 'oi169_lockme')));
munlock('oi169_lockme');
u2_chk('lock_unlocked', @() mislocked('oi169_lockme'));
clear oi169_lockme
u2_chk('lock_after_unlock_clear', @() oi169_lockme());
munlock oi169_lockme
u2_chk('lock_unlock_inside', @() oi169_unlockme());
u2_chk('lock_local_function', @() oi169_locallock());
munlock oi169_locallock
u2_chk('lock_command_unlock', @() mislocked('oi169_locallock'));
u2_chk('lock_anonymous', @() lock_through_handle());
u2_chk('lock_script_mlock', @() evalc('mlock'));
u2_chk('lock_script_mislocked', @() mislocked);
u2_chk('lock_script_munlock', @() evalc('munlock'));
u2_chk('lock_nosuch_mislocked', @() mislocked('oi169nosuch'));
u2_chk('lock_unlocked_mislocked', @() mislocked('u2_chk'));
u2_chk('lock_builtin_mislocked', @() mislocked('sin'));
u2_chk('lock_builtin_munlock', @() evalc('munlock(''sin'')'));
u2_chk('lock_unlocked_munlock', @() evalc('munlock(''u2_chk'')'));
u2_chk('lock_nosuch_munlock', @() evalc('munlock(''oi169nosuch'')'));
u2_chk('lock_nosuch_munlock_string', @() evalc('munlock("oi169nosuch")'));
u2_chk('lock_number_munlock', @() evalc('munlock(5)'));
u2_chk('lock_munlock_output', @() munlock('u2_chk'));
u2_chk('lock_number_mislocked', @() mislocked(5));
u2_chk('lock_two_mislocked', @() mislocked('a', 'b'));
u2_chk('lock_mlock_input', @() evalc('mlock(1)'));
u2_chk('lock_mlock_output', @() mlock_output());
u2_chk('lock_mislocked_two_outputs', @() two_mislocked());

function tf = issorted_names(v)
names = {v.Name};
tf = isequal(names, sort(names));
end

function c = names_of(v)
c = {v.Name};
end

function s = warned_size(f)
lastwarn('');
v = f();
[~, id] = lastwarn;
s = [mat2str(size(v)) ' ' id];
end

function v = two_ver()
[a, b] = ver; %#ok<ASGLU>
v = 1;
end

function v = two_usejava()
[a, b] = usejava('jvm'); %#ok<ASGLU>
v = 1;
end

function v = two_prefdir()
[a, b] = prefdir; %#ok<ASGLU>
v = 1;
end

function v = two_userpath()
[a, b] = userpath; %#ok<ASGLU>
v = 1;
end

function v = two_mislocked()
[a, b] = mislocked('x'); %#ok<ASGLU>
v = 1;
end

function v = mlock_output()
v = mlock;
end

function v = three_inmem()
[a, b, c] = inmem;
v = sprintf('%s %s %s %s %s %s', class(a), mat2str(size(a, 2)), class(b), mat2str(size(b)), class(c), mat2str(size(c, 2)));
end

function v = four_inmem()
[a, b, c, d] = inmem; %#ok<ASGLU>
v = 1;
end

function v = lock_through_handle()
f = @() mlock;
f();
v = mislocked;
munlock;
end

function c = printed_lines(t)
c = regexp(t, '\n', 'split');
if ~isempty(c) && isempty(c{end})
    c(end) = [];
end
end

function s = last_line(t)
c = printed_lines(t);
s = c{end};
end

function s = dotted(s)
s = strrep(s, ' ', '.');
end

function makebox(root, name, lines)
mkdir(fullfile(root, name));
writelines(lines, fullfile(root, name, 'Contents.m'));
end

function div_chk(name, fn)
% A u2_chk line under this ADR's divergence rule (the value JGraph answers differs by design).
fprintf('CHK|%s|%s|div=ADR0341\n', name, u2_text(fn()));
end
