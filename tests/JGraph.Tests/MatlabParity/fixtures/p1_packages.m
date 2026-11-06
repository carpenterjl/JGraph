% p1_packages.m -- the packages and class folders plan's stage P1 (ADR 0307): a +pkg folder's
% functions and scripts answer to their dotted names from the current folder and from any addpath
% folder; one package spread over several folders is one package, the first folder winning per
% member; members never see each other bare; a package's private/ is its members' alone; nested
% packages; a function and a sub-package of one name; a variable beats a package; a file pkg.m
% beside +pkg is another thing and the dotted name still reaches the package; handles, feval,
% str2func, eval, nargin, mfilename, dbstack, dbstop, which, exist, what, help, type, addpath's
% refusals, cd into a package folder (and cd's output, chip 244), clear. Every row is one of the
% plan's R2025b probes (docs/plans/notes/packages-research-2026-11-05), written as a line.
%
% A fixture is one file, so this one builds the folders it needs under tempdir -- a current folder
% holding +pkg and +cpkg and the case files, and a library holding the rest of +pkg -- and removes
% them at the end. The rows JGraph decides differently are div=ADR0307: a package file written after
% the package was first used is seen at the next statement, where R2025b needs rehash.

start = pwd;
root = fullfile(tempdir, 'p1_packages');
if exist(root, 'dir') == 7
    rmdir(root, 's');
end
cur = fullfile(root, 'cur');
lib = fullfile(root, 'lib');
mkdir(cur);
mkdir(lib);

% ---- the library's package ----
put(fullfile(lib, '+pkg', 'f.m'), {'function r = f(x)', 'r = x * 10;', 'end'});
put(fullfile(lib, '+pkg', 'g.m'), {'function r = g(x)', '% calls a sibling unqualified', 'r = f(x) + 1;', 'end'});
put(fullfile(lib, '+pkg', 'g2.m'), {'function r = g2(x)', 'r = pkg.f(x) + 1;', 'end'});
put(fullfile(lib, '+pkg', 'two.m'), {'function [a, b] = two(x)', 'a = x;', 'b = -x;', 'end'});
put(fullfile(lib, '+pkg', 'q.m'), {'function r = q()', 'r = p();', 'end'});
put(fullfile(lib, '+pkg', 'private', 'p.m'), {'function r = p()', 'r = ''private p'';', 'end'});
put(fullfile(lib, '+pkg', 's.m'), {'disp(''script s ran'');', 'sval = 42;'});
put(fullfile(lib, '+pkg', 'sm.m'), {'smfull = mfilename(''fullpath'');', 'smbare = mfilename;'});
put(fullfile(lib, '+pkg', 'max.m'), {'function r = max(x)', 'r = ''pkg max'';', 'end'});
put(fullfile(lib, '+pkg', 'dup.m'), {'function r = dup()', 'r = ''lib dup'';', 'end'});
put(fullfile(lib, '+pkg', 'info.m'), {'function r = info()', 's = dbstack;', 'r = struct(''mf'', mfilename, ''mfp'', mfilename(''fullpath''), ''stack'', {{s.name}});', 'end'});
put(fullfile(lib, '+pkg', 'x.m'), {'function r = x()', 'r = ''function x'';', 'end'});
put(fullfile(lib, '+pkg', '+x', 'y.m'), {'function r = y()', 'r = ''sub x y'';', 'end'});
put(fullfile(lib, '+pkg', '+sub', 'h.m'), {'function r = h(x)', 'r = x - 1;', 'end'});
put(fullfile(lib, '+pkg', '+sub', 'stk.m'), {'function r = stk()', 'r = evalc(''dbstack'');', 'end'});
put(fullfile(lib, '+pkg', 'stk.m'), {'function r = stk()', 'r = evalc(''dbstack'');', 'end'});
put(fullfile(lib, '+pkg', 'stkloc.m'), {'function r = stkloc()', 'r = inner();', 'end', 'function r = inner()', 'r = evalc(''dbstack'');', 'end'});
put(fullfile(lib, '+pkg', 'serr.m'), {'function serr()', 'error(''pkg:serr'', ''raised in serr'');', 'end'});
put(fullfile(lib, '+pkg', '@D', 'D.m'), {'classdef D', 'end'});
put(fullfile(lib, '@P1Cls', 'P1Cls.m'), {'classdef P1Cls', 'end'});
put(fullfile(lib, 'p1doc.m'), {'function r = p1doc(x)', '%P1DOC  Doubles its input.', '%   r = p1doc(x) is twice x.', 'r = 2 * x;', 'end'});
put(fullfile(lib, 'p1plain.m'), {'function r = p1plain(x)', 'r = x + 1;', 'end'});

% ---- the current folder's packages and case files ----
put(fullfile(cur, '+pkg', 'dup.m'), {'function r = dup()', 'r = ''cur dup'';', 'end'});
put(fullfile(cur, '+pkg', 'fromcur.m'), {'function r = fromcur()', 'r = ''pkg fromcur'';', 'end'});
put(fullfile(cur, '+cpkg', 'extra.m'), {'function r = extra()', 'r = ''cur extra'';', 'end'});
put(fullfile(cur, 'varshadow.m'), {'function r = varshadow()', 'pkg = struct(''f'', @(x) -x);', 'r = pkg.f(2);', 'end'});
put(fullfile(cur, 'varshadow2.m'), {'function r = varshadow2()', 'pkg = 3;', 'r = pkg.f(2);', 'end'});
put(fullfile(cur, 'multiout.m'), {'function r = multiout()', '[a, b] = pkg.two(3);', 'r = sprintf(''%d,%d'', a, b);', 'end'});
put(fullfile(cur, 'scriptvalue.m'), {'function r = scriptvalue()', 'x = pkg.s;', 'r = x;', 'end'});

cd(cur);
addpath(lib);
rel = @(p) strrep(p, root, '<root>');
oneline = @(t) strrep(strrep(t, char([13 10]), '/'), newline, '/');
firstline = @(t) strtok(t, newline);
nolinks = @(t) regexprep(regexprep(t, '<a href="[^"]*"[^>]*>', ''), '</a>', '');

% ---- calls ----
chk('call_f', pkg.f(2));
chk('call_g2', pkg.g2(2));
chk('call_sub_h', pkg.sub.h(5));
chk('call_q_private', pkg.q());
chk('call_pkg_max', pkg.max(1));
chk('builtin_max_unshadowed', max([3 9 1]));
chk('call_dup_first_folder_wins', pkg.dup());
chk('call_fromcur', pkg.fromcur());
chk('call_cpkg', cpkg.extra());
chk('call_two_one_output', pkg.two(3));
chk('call_two_both', multiout());
chk('bare_dotted_is_a_call', idof(@() pkg.f));
chk('fn_vs_subpkg_call', pkg.x());
chk('fn_vs_subpkg_sub', pkg.x.y());
chk('var_struct_wins', varshadow());
chk('var_number_id', idof(@() varshadow2()));
chk('var_number_msg', msgof(@() varshadow2()));

% ---- refusals ----
chk('nosuch_member_id', idof(@() pkg.nosuch(1)));
chk('nosuch_member_msg', msgof(@() pkg.nosuch(1)));
chk('nosuch_pkg_msg', msgof(@() nosuchpkg.f(1)));
chk('nosuch_sub_member_msg', msgof(@() pkg.sub.nosuch(1)));
chk('sibling_bare_id', idof(@() pkg.g(1)));
chk('sibling_bare_msg', msgof(@() pkg.g(1)));
chk('private_as_subpkg_msg', msgof(@() pkg.private.p()));
chk('private_from_outside_id', idof(@() p()));
chk('too_few_id', idof(@() pkg.f()));
chk('too_many_id', idof(@() pkg.f(1, 2)));
chk('cmd_syntax_id', idof(@() evalc('pkg.f 2')));
try
    pkg.g(1);
catch e
    chk('sibling_stack_names', strjoin({e.stack.name}, ','));
end
try
    pkg.serr();
catch e
    chk('serr_id', e.identifier);
    chk('serr_msg', e.message);
    chk('serr_stack_name', e.stack(1).name);
    chk('serr_stack_file', rel(e.stack(1).file));
end

% ---- handles and name-based calls ----
h = @pkg.f;
i = functions(h);
chk('handle_call', h(2));
chk('handle_func2str', func2str(h));
chk('handle_function', i.function);
chk('handle_type', i.type);
chk('handle_file', i.file);
chk('handle_cellfun', mat2str(cellfun(@pkg.f, {1, 2})));
chk('handle_arrayfun', mat2str(arrayfun(@(x) pkg.f(x), 1:2)));
chk('handle_equal', isequal(@pkg.f, @pkg.f));
chk('handle_unequal', isequal(@pkg.f, @pkg.g2));
s1 = str2func('pkg.f');
chk('str2func_name', s1(2));
s2 = str2func('@pkg.f');
chk('str2func_at', s2(2));
chk('feval_name', feval('pkg.f', 2));
chk('feval_handle', feval(@pkg.f, 4));
chk('eval_dotted', eval('pkg.f(2)'));
chk('evalin_dotted', evalin('base', 'pkg.f(3)'));
chk('nargin_name', nargin('pkg.f'));
chk('nargin_handle', nargin(@pkg.f));
chk('nargout_name', nargout('pkg.two'));
chk('nargout_handle', nargout(@pkg.two));
chk('nargin_nosuch_id', idof(@() nargin('pkg.nosuch')));

% ---- the frames: mfilename, dbstack, dbstop ----
i = pkg.info();
chk('mfilename_bare', i.mf);
chk('mfilename_full', rel(i.mfp));
chk('dbstack_names', strjoin(i.stack, ','));
chk('dbstack_printed', firstline(nolinks(pkg.stk())));
chk('dbstack_printed_sub', firstline(nolinks(pkg.sub.stk())));
chk('dbstack_printed_local', firstline(nolinks(pkg.stkloc())));
dbstop in pkg.f
dbstop in pkg.sub.h
chk('dbstatus_dotted', oneline(nolinks(evalc('dbstatus'))));
dbclear all
chk('dbstop_nosuch_id', idof(@() evalc('dbstop in pkg.nosuch')));
chk('dbstop_nosuch_msg', msgof(@() evalc('dbstop in pkg.nosuch')));

% ---- scripts in packages ----
chk('script_runs', oneline(evalc('pkg.s')));
chk('script_var_exists', exist('sval'));
chk('script_var', sval);
pkg.sm
chk('script_mfilename_full', rel(smfull));
chk('script_mfilename_bare', smbare);
chk('script_as_value_id', idof(@() scriptvalue()));
chk('script_as_value_msg', rel(oneline(msgof(@() scriptvalue()))));
chk('script_with_arg_id', idof(@() pkg.s(1)));
try
    feval('pkg.s');
    chk('script_feval_id', '');
catch e
    chk('script_feval_id', e.identifier);
end
chk('script_run_id', idof(@() run('pkg.s')));
chk('script_which', rel(which('pkg.s')));
chk('script_exist', exist('pkg.s'));

% ---- which, exist ----
chk('which_f', rel(which('pkg.f')));
chk('which_dup', rel(which('pkg.dup')));
chk('which_all_dup', numel(which('pkg.dup', '-all')));
chk('which_sub_h', rel(which('pkg.sub.h')));
chk('which_q', rel(which('pkg.q')));
chk('which_nosuch', which('pkg.nosuch'));
chk('which_sub', which('pkg.sub'));
chk('which_pkg', which('pkg'));
chk('exist_f', exist('pkg.f'));
chk('exist_f_file', exist('pkg.f', 'file'));
chk('exist_pkg', exist('pkg'));
chk('exist_sub', exist('pkg.sub'));
chk('exist_sub_h', exist('pkg.sub.h'));
chk('exist_plus_pkg', exist('+pkg'));
chk('exist_plus_pkg_dir', exist('+pkg', 'dir'));

% ---- what ----
w = what('pkg');
chk('what_pkg_count', numel(w));
chk('what_pkg_1', rel(w(1).path));
chk('what_pkg_2', rel(w(2).path));
chk('what_pkg_2_m', strjoin(w(2).m', ','));
chk('what_pkg_2_classes', strjoin(w(2).classes', ','));
chk('what_pkg_2_packages', strjoin(w(2).packages', ','));
w = what('+pkg');
chk('what_plus_pkg_count', numel(w));
chk('what_dotted_sub_count', numel(what('pkg.sub')));
w = what('pkg/sub');
chk('what_slash_sub', rel(w.path));
chk('what_slash_sub_m', strjoin(w.m', ','));
w = what('+pkg/+sub');
chk('what_plus_slash_sub', rel(w.path));
w = what('cpkg');
chk('what_cpkg', rel(w.path));
chk('what_nosuch_count', numel(what('nosuchpkg')));
w = what(lib);
chk('what_lib_classes', strjoin(w.classes', ','));
chk('what_lib_packages', strjoin(w.packages', ','));

% ---- help, type ----
chk('help_pkg', oneline(help('pkg')));
chk('help_pkg_sub', oneline(help('pkg.sub')));
chk('help_pkg_f', oneline(help('pkg.f')));
chk('help_pkg_g', oneline(help('pkg.g')));
chk('help_pkg_s', oneline(help('pkg.s')));
chk('help_pkg_nosuch_id', idof(@() help('pkg.nosuch')));
chk('help_plain', oneline(help('p1plain')));
chk('help_doc', oneline(help('p1doc')));
chk('type_dotted_id', idof(@() evalc('type pkg.f')));
chk('type_dotted_msg', msgof(@() evalc('type pkg.f')));
chk('type_relative', oneline(evalc('type(''+pkg/f.m'')')));
chk('type_relative_cur_first', oneline(evalc('type(''+pkg/dup.m'')')));
chk('type_plain', oneline(evalc('type p1plain')));

% ---- addpath, rmpath, genpath ----
lastwarn('');
addpath(fullfile(lib, '+pkg'));
[m, id] = lastwarn;
chk('addpath_pkg_msg', rel(m));
chk('addpath_pkg_id', id);
chk('addpath_pkg_not_added', contains(path, fullfile(lib, '+pkg')));
lastwarn('');
addpath(fullfile(lib, '@P1Cls'));
[m, id] = lastwarn;
chk('addpath_class_msg', rel(m));
chk('addpath_class_id', id);
chk('addpath_class_not_added', contains(path, fullfile(lib, '@P1Cls')));
lastwarn('');
addpath(fullfile(lib, '+pkg', 'private'));
[m, id] = lastwarn;
chk('addpath_private_msg', rel(m));
chk('addpath_private_id', id);
lastwarn('');
rmpath(fullfile(lib, '+pkg'));
[m, id] = lastwarn;
chk('rmpath_msg', rel(m));
chk('rmpath_id', id);
chk('genpath_skips_pkg', rel(genpath(lib)));

% ---- cd into a package folder (and cd's output, chip 244) ----
here = cd;
chk('cd_bare_output', strcmp(here, cur));
old = cd(fullfile(lib, '+pkg'));
chk('cd_output_is_the_folder_left', strcmp(old, cur));
chk('cd_in_pkg_bare_id', idof(@() f(2)));
chk('cd_in_pkg_dotted', pkg.f(2));
chk('cd_in_pkg_which', which('f'));
cd(old);
chk('cd_back', strcmp(pwd, cur));

% ---- a file pkg.m beside the package ----
put(fullfile(lib, 'pkg.m'), {'function r = pkg(varargin)', 'r = ''function pkg'';', 'end'});
rehash
chk('fnshadow_member', pkg.f(2));
chk('fnshadow_call', pkg());
r = pkg;
chk('fnshadow_bare', r);
chk('fnshadow_which', rel(which('pkg')));
chk('fnshadow_which_member', rel(which('pkg.f')));
chk('fnshadow_exist', exist('pkg'));
chk('fnshadow_feval', feval('pkg.f', 2));
delete(fullfile(lib, 'pkg.m'));
rehash
clear pkg r
put(fullfile(lib, 'pkg.m'), {'classdef pkg', '    properties (Constant)', '        f = 99', '    end', 'end'});
rehash
chk('classshadow_member', pkg.f(2));
chk('classshadow_bare_id', idof(@() pkg.f));
chk('classshadow_which', rel(which('pkg')));
delete(fullfile(lib, 'pkg.m'));
rehash
clear pkg

% ---- clear and a file written later ----
chk('clear_before', pkg.f(2));
clear pkg.f
chk('clear_after', pkg.f(2));
clear functions
chk('clear_functions_after', pkg.f(2));
put(fullfile(lib, '+pkg', 'late.m'), {'function r = late()', 'r = ''late'';', 'end'});
chkdiv('late_before_rehash', idof(@() pkg.late()));
rehash
chk('late_after_rehash', pkg.late());

cd(start);
rmpath(lib);
rmdir(root, 's');

function put(path, lines)
folder = fileparts(path);
if ~isempty(folder) && exist(folder, 'dir') ~= 7
    mkdir(folder);
end
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
fprintf('CHK|%s|%s|div=ADR0307\n', name, show(v));
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

function id = idof(call)
id = '';
try
    call();
catch e
    id = e.identifier;
end
end

function m = msgof(call)
m = '';
try
    call();
catch e
    m = e.message;
end
end
