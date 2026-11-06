% p4_classfolders.m -- the packages and class folders plan's stage P4 (ADR 0310): a class defined
% by an @Class folder -- @P4Thing/P4Thing.m with its methods in files of their own, declared as
% signature lines in the classdef's methods blocks (instance, static, private) or not declared at
% all, the folder's private/ functions as the class's private methods, a method file's local
% functions as its own, a method file named differently from its function, operators and a
% built-in's name (plus, size) as method files, a subclass folder overriding across folders, a
% handle class folder, a class folder in the current folder and one inside a +package, a class
% folder beside a function of the same name (R2025b's class-folder-wins rule and its warning,
% decision PD3), a class folder whose class file names another class, a class folder without its
% class file, cd into a class folder, and the tools over all of it: which (bare, slash, dotted,
% -all and the argument form), exist, what, help, type, nargin, dbstop, dbstatus, dbstack, the
% error stack, mfilename, methods, ismethod, metaclass, ?Class, superclasses. Every rule is R2025b's
% as recorded here; the research probes (docs/plans/notes/packages-research-2026-11-05) settled
% most of them first.
%
% A fixture is one file, so this one builds the folders it needs under tempdir -- a current folder
% holding @P4Cur and a library holding the rest -- and removes them at the end.

start = pwd;
root = fullfile(tempdir, 'p4_classfolders');
if exist(root, 'dir') == 7
    rmdir(root, 's');
end
cur = fullfile(root, 'cur');
lib = fullfile(root, 'lib');
mkdir(cur);
mkdir(lib);
warning('off', 'backtrace');

% ---- the class folder @P4Thing ----
put(fullfile(lib, '@P4Thing', 'P4Thing.m'), {'classdef P4Thing', '    properties', '        W = 0', '    end', '    methods', '        function o = P4Thing(w)', '            if nargin, o.W = w; end', '        end', '        r = extent(o)', '        r = size(o, varargin)', '        r = plus(a, b)', '        function r = viaSecret(o)', '            r = secret(o);', '        end', '    end', '    methods (Static)', '        r = staticOne(x)', '    end', '    methods (Access = private)', '        r = secret(o)', '    end', 'end'});
put(fullfile(lib, '@P4Thing', 'extent.m'), {'function r = extent(o)', 'r = o.W * o.W;', 'end'});
put(fullfile(lib, '@P4Thing', 'scale.m'), {'function r = scale(o, k)', '% not declared in the classdef', 'r = p4hidden(o.W) * k;', 'end'});
put(fullfile(lib, '@P4Thing', 'private', 'p4hidden.m'), {'function r = p4hidden(w)', 'r = w + 0.5;', 'end'});
put(fullfile(lib, '@P4Thing', 'staticOne.m'), {'function r = staticOne(x)', 'r = x * 3;', 'end'});
put(fullfile(lib, '@P4Thing', 'secret.m'), {'function r = secret(o)', 'r = ''secret'';', 'end'});
put(fullfile(lib, '@P4Thing', 'size.m'), {'function r = size(o, varargin)', 'r = [o.W 1];', 'end'});
put(fullfile(lib, '@P4Thing', 'plus.m'), {'function r = plus(a, b)', 'r = P4Thing(a.W + b.W);', 'end'});
put(fullfile(lib, '@P4Thing', 'withlocal.m'), {'function r = withlocal(o)', 'r = helper(o.W);', 'end', 'function r = helper(w)', 'r = w * 7;', 'end'});
put(fullfile(lib, '@P4Thing', 'misnamed.m'), {'function r = othername(o)', 'r = ''misnamed'';', 'end'});
put(fullfile(lib, '@P4Thing', 'info.m'), {'function r = info(o)', 's = dbstack;', 'r = struct(''mf'', mfilename, ''mfp'', mfilename(''fullpath''), ''stack'', {{s.name}});', 'end'});
put(fullfile(lib, '@P4Thing', 'stk.m'), {'function r = stk(o)', 'r = evalc(''dbstack'');', 'end'});
put(fullfile(lib, '@P4Thing', 'terr.m'), {'function terr(o)', 'error(''P4Thing:terr'', ''raised in terr'');', 'end'});

% ---- a subclass folder, a handle class folder ----
put(fullfile(lib, '@P4Sub', 'P4Sub.m'), {'classdef P4Sub < P4Thing', '    methods', '        function o = P4Sub(w)', '            o@P4Thing(w);', '        end', '    end', 'end'});
put(fullfile(lib, '@P4Sub', 'extent.m'), {'function r = extent(o)', 'r = -1;', 'end'});
put(fullfile(lib, '@P4HThing', 'P4HThing.m'), {'classdef P4HThing < handle', '    properties', '        N = 0', '    end', 'end'});
put(fullfile(lib, '@P4HThing', 'bump.m'), {'function bump(o)', 'o.N = o.N + 1;', 'end'});

% ---- a class folder beside a function of the same name, a misnamed class, a folder without its class ----
put(fullfile(lib, '@P4Both', 'P4Both.m'), {'classdef P4Both', '    properties', '        Z = 1', '    end', 'end'});
put(fullfile(lib, 'P4Both.m'), {'function r = P4Both()', 'r = ''function P4Both'';', 'end'});
put(fullfile(lib, '@P4Mis', 'P4Mis.m'), {'classdef Wrong', 'end'});
put(fullfile(lib, '@P4Lone', 'method1.m'), {'function r = method1(o)', 'r = 1;', 'end'});

% ---- a class folder inside a package ----
put(fullfile(lib, '+p4pkg', '@D', 'D.m'), {'classdef D', '    properties', '        V', '    end', '    methods', '        function o = D(v)', '            o.V = v;', '        end', '        r = dm(o)', '    end', 'end'});
put(fullfile(lib, '+p4pkg', '@D', 'dm.m'), {'function r = dm(o)', 'r = o.V + 1000;', 'end'});
put(fullfile(lib, '+p4pkg', 'f.m'), {'function r = f(x)', 'r = x;', 'end'});

% ---- the current folder: a class folder and a function using the library's class ----
put(fullfile(cur, '@P4Cur', 'P4Cur.m'), {'classdef P4Cur', '    properties', '        W = 2', '    end', '    methods', '        r = twice(o)', '    end', 'end'});
put(fullfile(cur, '@P4Cur', 'twice.m'), {'function r = twice(o)', 'r = o.W * 2;', 'end'});
put(fullfile(cur, 'p4usething.m'), {'function r = p4usething(x)', 't = P4Thing(x);', 'r = scale(t, 2);', 'end'});

cd(cur);
addpath(lib);
rel = @(p) strrep(p, root, '<root>');
oneline = @(t) strrep(strrep(t, char([13 10]), '/'), newline, '/');
firstline = @(t) strtok(t, newline);
nolinks = @(t) regexprep(regexprep(t, '<a href="[^"]*"[^>]*>', ''), '</a>', '');
squash = @(t) regexprep(strtrim(nolinks(t)), '\s+', ' ');
warntext = @(t) squash(regexprep(t(t ~= 8), '[\[\]]', ''));
sorted = @(c) strjoin(sort(c), ',');
relall = @(c) sorted(cellfun(rel, c, 'UniformOutput', false));

% ---- a classdef in a class folder: declared, undeclared, static, private, operators ----
chk('extent_fn', extent(P4Thing(3)));
chk('extent_dot', P4Thing(3).extent);
chk('extent_dot_parens', P4Thing(3).extent());
chk('scale_fn', scale(P4Thing(3), 2));
chk('scale_dot', P4Thing(3).scale(2));
chk('static_class', P4Thing.staticOne(2));
chk('static_instance', P4Thing(1).staticOne(2));
chk('static_bare_id', idof(@() staticOne(2)));
chk('static_bare_msg', msgof(@() staticOne(2)));
chk('size_method', mat2str(size(P4Thing(3))));
chk('size_plain', mat2str(size([1 2 3])));
chk('numel_object', numel(P4Thing(3)));
tt = P4Thing(1) + P4Thing(2);
chk('plus_operator_w', tt.W);
tt2 = plus(P4Thing(1), P4Thing(2));
chk('plus_fn_class', class(tt2));
chk('plus_fn_w', tt2.W);
chk('withlocal', withlocal(P4Thing(2)));
chk('local_helper_id', idof(@() helper(2)));
chk('local_helper_msg', msgof(@() helper(2)));
i = info(P4Thing(1));
chk('info_mfilename', i.mf);
chk('info_fullpath', rel(i.mfp));
chk('info_stack_name', i.stack{1});
t = P4Thing(2);
chk('secret_fn_id', idof(@() secret(t)));
chk('secret_fn_msg', msgof(@() secret(t)));
chk('secret_dot_id', idof(@() t.secret()));
chk('secret_via_public', viaSecret(t));
chk('hidden_fn_id', idof(@() p4hidden(2)));
chk('hidden_fn_msg', msgof(@() p4hidden(2)));
chk('hidden_dot_id', idof(@() t.p4hidden()));
chk('hidden_dot_msg', msgof(@() t.p4hidden()));
chk('hidden_class_id', idof(@() P4Thing.p4hidden(2)));
chk('hidden_class_msg', msgof(@() P4Thing.p4hidden(2)));
chk('extent_double_id', idof(@() extent(3)));
chk('extent_double_msg', msgof(@() extent(3)));
chk('scale_double_msg', msgof(@() scale(3, 2)));
chk('misnamed_by_file', misnamed(P4Thing(1)));
chk('othername_id', idof(@() othername(P4Thing(1))));
chk('othername_msg', msgof(@() othername(P4Thing(1))));
chk('empty_size', mat2str(size(P4Thing.empty)));
chk('class_of_name', class(P4Thing));
chk('property', P4Thing(2).W);
chk('isa_thing', isa(P4Thing(1), 'P4Thing'));
chk('used_from_cur_function', p4usething(2));
clear P4Thing
chk('after_clear', extent(P4Thing(3)));

% ---- the class folder as the current folder ----
cd(fullfile(lib, '@P4Thing'));
t2 = P4Thing(2);
chk('atcd_extent', extent(t2));
chk('atcd_bare_id', idof(@() extent(3)));
chk('atcd_which', rel(which('extent')));
cd(cur);

% ---- inheritance across folders, a handle class, the current folder's class folder ----
chk('sub_extent', extent(P4Sub(2)));
chk('sub_scale', scale(P4Sub(2), 2));
chk('sub_size', mat2str(size(P4Sub(2))));
chk('sub_class', class(P4Sub(2)));
chk('sub_isa', isa(P4Sub(1), 'P4Thing'));
chk('sub_superclasses', strjoin(superclasses('P4Sub')', ','));
h = P4HThing;
bump(h);
h.bump();
chk('hthing_n', h.N);
chk('hthing_isa_handle', isa(h, 'handle'));
chk('cur_twice', twice(P4Cur));
chk('cur_twice_dot', P4Cur().twice());
chk('cur_class', class(P4Cur));

% ---- a class folder inside a package ----
chk('pkg_class_method_dot', p4pkg.D(1).dm());
chk('pkg_class_method_fn', dm(p4pkg.D(1)));
chk('pkg_class_name', class(p4pkg.D(1)));
chk('pkg_class_methods', strjoin(methods('p4pkg.D')', ','));
chk('pkg_class_which', rel(which('p4pkg.D')));
chk('pkg_class_which_slash', which('p4pkg.D/dm'));
chk('pkg_class_dbstop_slash_msg', msgof(@() evalc('dbstop in p4pkg.D/dm')));
chk('pkg_class_exist', exist('p4pkg.D'));
chk('pkg_help_listing', oneline(help('p4pkg')));

% ---- a class folder beside a function of the same name (PD3) ----
lastwarn('');
txt = evalc('b = P4Both();');
chk('both_class', class(b));
chk('both_warning', warntext(txt));
txt2 = evalc('b2 = P4Both();');
chk('both_warning_once', warntext(txt2));
[wm, wid] = lastwarn;
chk('both_lastwarn_id', wid);
chk('both_which', rel(which('P4Both')));
chk('both_which_all', relall(which('P4Both', '-all')));
chk('both_exist', exist('P4Both'));
chk('both_exist_class', exist('P4Both', 'class'));
chk('both_property', b.Z);

% ---- a class file naming another class, a folder without its class file ----
chk('mis_id', idof(@() P4Mis()));
chk('mis_msg', oneline(rel(msgof(@() P4Mis()))));
chk('mis_exist', exist('P4Mis'));
chk('mis_exist_class', exist('P4Mis', 'class'));
chk('mis_which', rel(which('P4Mis')));
chk('lone_id', idof(@() P4Lone()));
chk('lone_msg', msgof(@() P4Lone()));
chk('lone_method_id', idof(@() method1(1)));
chk('lone_exist', exist('P4Lone'));
chk('lone_exist_class', exist('P4Lone', 'class'));
chk('lone_which', which('P4Lone'));
chk('lone_which_method', rel(which('P4Lone/method1')));
chk('lone_methods_size', mat2str(size(methods('P4Lone'))));

% ---- which ----
t = P4Thing(2);
s = P4Sub(1);
chk('which_class', rel(which('P4Thing')));
chk('which_bare_method', rel(which('extent')));
chk('which_all_bare_method', relall(which('extent', '-all')));
chk('which_slash_declared', rel(which('P4Thing/extent')));
chk('which_slash_undeclared', rel(which('P4Thing/scale')));
chk('which_slash_builtin_name', rel(which('P4Thing/size')));
chk('which_slash_private_folder', which('P4Thing/p4hidden'));
chk('which_slash_private_method', rel(which('P4Thing/secret')));
chk('which_slash_static', rel(which('P4Thing/staticOne')));
chk('which_dot_static', rel(which('P4Thing.staticOne')));
chk('which_slash_ctor', rel(which('P4Thing/P4Thing')));
chk('which_slash_sub', rel(which('P4Sub/extent')));
chk('which_slash_cur', rel(which('P4Cur/twice')));
chk('which_slash_nosuch', which('P4Thing/nosuch'));
chk('which_arg_declared', oneline(rel(evalc('which extent(t)'))));
chk('which_arg_undeclared', oneline(rel(evalc('which scale(t, 1)'))));
chk('which_arg_builtin_name', oneline(rel(evalc('which size(t)'))));
chk('which_arg_sub', oneline(rel(evalc('which extent(s)'))));

% ---- exist ----
chk('exist_class', exist('P4Thing'));
chk('exist_class_kind', exist('P4Thing', 'class'));
chk('exist_class_file', exist('P4Thing', 'file'));
chk('exist_at_folder', exist('@P4Thing'));
chk('exist_slash_method', exist('P4Thing/extent'));
chk('exist_slash_method_file', exist('P4Thing/extent', 'file'));
chk('exist_bare_method', exist('extent'));
chk('exist_bare_undeclared', exist('scale'));
chk('exist_sub', exist('P4Sub'));
chk('exist_sub_class', exist('P4Sub', 'class'));
chk('exist_cur', exist('P4Cur'));
chk('exist_cur_class', exist('P4Cur', 'class'));

% ---- what ----
w = what('P4Thing');
chk('what_class_path', rel(w.path));
chk('what_class_m', strjoin(w.m', ','));
chk('what_class_classes_count', numel(w.classes));
w = what('@P4Thing');
chk('what_at_path', rel(w.path));
w = what(fullfile(lib, '@P4Thing'));
chk('what_full_path', rel(w.path));
chk('what_full_m', strjoin(w.m', ','));
w = what(lib);
chk('what_lib_classes', strjoin(w.classes', ','));
chk('what_lib_m', strjoin(w.m', ','));
chk('what_lib_packages', strjoin(w.packages', ','));
chk('what_private_empty', isempty(what('@P4Thing/private')));

% ---- help, type, nargin ----
chk('help_class_folder', oneline(help('P4Thing')));
chk('help_slash_declared', oneline(help('P4Thing/extent')));
chk('help_slash_undeclared', oneline(help('P4Thing/scale')));
chk('help_dot_undeclared', oneline(help('P4Thing.scale')));
chk('help_slash_static', oneline(help('P4Thing/staticOne')));
chk('help_sub_folder', oneline(help('P4Sub')));
chk('type_slash', oneline(evalc('type(''P4Thing/extent'')')));
chk('type_relative', oneline(evalc('type(''@P4Thing/extent.m'')')));
chk('nargin_slash_declared', nargin('P4Thing/extent'));
chk('nargin_slash_undeclared', nargin('P4Thing/scale'));
chk('nargin_class', nargin('P4Thing'));
chk('nargin_static_handle', nargin(@P4Thing.staticOne));
chk('nargin_dot_id', idof(@() nargin('P4Thing.extent')));

% ---- the debugger's words ----
dbstop in P4Thing/extent
chk('dbstatus_slash_declared', oneline(nolinks(evalc('dbstatus'))));
dbclear all
dbstop in P4Thing/scale
chk('dbstatus_slash_undeclared', oneline(nolinks(evalc('dbstatus'))));
dbclear all
dbstop in P4Thing
chk('dbstatus_class', oneline(nolinks(evalc('dbstatus'))));
dbclear all
dbstop in P4Thing/P4Thing
chk('dbstatus_class_slash', oneline(nolinks(evalc('dbstatus'))));
dbclear all
dbstop in P4Thing/withlocal>helper
chk('dbstatus_local', oneline(nolinks(evalc('dbstatus'))));
dbclear all
chk('dbstop_dot_msg', msgof(@() evalc('dbstop in P4Thing.extent')));
chk('dbstop_gt_msg', msgof(@() evalc('dbstop in P4Thing>extent')));
chk('dbstop_local_dot_msg', msgof(@() evalc('dbstop in P4Thing/withlocal>withlocal.helper')));
chk('dbstack_method_file', firstline(nolinks(stk(P4Thing(1)))));
chk('dbstack_method_file_dot', firstline(nolinks(P4Thing(1).stk())));
try
    terr(P4Thing(1));
catch err
    chk('terr_id', err.identifier);
    chk('terr_stack_name', err.stack(1).name);
    chk('terr_stack_file', rel(err.stack(1).file));
end

% ---- methods, ismethod, metaclass ----
chk('methods_names', strjoin(methods('P4Thing')', ','));
chk('methods_printed', oneline(evalc('methods(''P4Thing'')')));
chk('methods_full', oneline(evalc('methods(''P4Thing'', ''-full'')')));
chk('methods_sub', strjoin(methods('P4Sub')', ','));
chk('methods_hthing_own', strjoin(intersect(methods('P4HThing'), {'P4HThing', 'bump'})', ','));
chk('ismethod_undeclared', ismethod(t, 'scale'));
chk('ismethod_static', ismethod(t, 'staticOne'));
chk('ismethod_nosuch', ismethod(t, 'nosuch'));
chk('metaclass_name', metaclass(t).Name);
mc = ?P4Thing;
chk('question_name', mc.Name);
mc = ?P4Sub;
chk('question_sub_name', mc.Name);

% ---- a method file written after the class was used (R2025b needs clear classes; JGraph sees it) ----
put(fullfile(lib, '@P4Thing', 'late.m'), {'function r = late(o)', 'r = ''late method'';', 'end'});
chkdiv('late_before_rehash', msgof(@() late(P4Thing(1))));
rehash
chkdiv('late_after_rehash', msgof(@() late(P4Thing(1))));

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
fprintf('CHK|%s|%s|div=ADR0310\n', name, show(v));
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
