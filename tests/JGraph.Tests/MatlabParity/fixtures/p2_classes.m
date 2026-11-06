% p2_classes.m -- the packages and class folders plan's stage P2 (ADR 0308): a classdef file in a
% +pkg folder is the class pkg.C -- its qualified name is its name everywhere (class, isa, whos,
% metaclass, ?pkg.C, superclasses, cells, struct fields, MAT-files), the object header shows the
% short name; pkg.C(...), pkg.C.K, pkg.C.stat(...), o.stat(...), the refusal for an instance method
% through the class, pkg.C.empty, object arrays, enumerations in packages, classdef K < pkg.C and
% m@pkg.C, a handle class in a package, a plain class of the same bare name beside it, members that
% do not see their own class bare; methods, properties, ismethod, enumeration, display, save/load in
% both MAT formats, help, which, exist, nargin, dbstop, dbstack, MException.stack, clear, and
% meta.package.fromName as JGraph's struct (decision PD5). Every rule is R2025b's as recorded here;
% the research probes (docs/plans/notes/packages-research-2026-11-05) settled most of them first.
%
% A fixture is one file, so this one builds the folders it needs under tempdir -- a current folder
% holding +cpkg and a library holding +pkg and the plain classes -- and removes them at the end.

start = pwd;
root = fullfile(tempdir, 'p2_classes');
if exist(root, 'dir') == 7
    rmdir(root, 's');
end
cur = fullfile(root, 'cur');
lib = fullfile(root, 'lib');
mkdir(cur);
mkdir(lib);

% ---- the library's package ----
put(fullfile(lib, '+pkg', 'C.m'), {'classdef C', '    properties (Constant)', '        K = 7', '    end', '    properties', '        V = 0', '    end', '    methods', '        function o = C(v)', '            if nargin, o.V = v; end', '        end', '        function r = m(o)', '            r = o.V * 2;', '        end', '        function r = selfbare(o)', '            r = C(5);', '        end', '        function r = selfqual(o)', '            r = pkg.C(5);', '        end', '    end', '    methods (Static)', '        function r = stat(x)', '            r = x + 100;', '        end', '    end', 'end'});
put(fullfile(lib, '+pkg', 'E.m'), {'classdef E', '    enumeration', '        A, B', '    end', 'end'});
put(fullfile(lib, '+pkg', 'H.m'), {'classdef H < handle', '    properties', '        N = 0', '    end', '    methods', '        function bump(o)', '            o.N = o.N + 1;', '        end', '    end', 'end'});
put(fullfile(lib, '+pkg', 'W.m'), {'classdef W', '    methods', '        function r = stk(o)', '            r = evalc(''dbstack'');', '        end', '        function r = stkn(o)', '            s = dbstack;', '            r = {s.name};', '        end', '        function err(o)', '            error(''pkg:W'', ''raised in W'');', '        end', '    end', '    methods (Static)', '        function r = sstk()', '            r = evalc(''dbstack'');', '        end', '    end', 'end'});
put(fullfile(lib, '+pkg', '+sub', 'K.m'), {'classdef K < pkg.C', '    methods', '        function r = m(o)', '            r = m@pkg.C(o) + 1;', '        end', '    end', 'end'});
put(fullfile(lib, '+pkg', 'f.m'), {'function r = f(x)', 'r = x * 10;', 'end'});
put(fullfile(lib, '+pkg', 's.m'), {'sval = 42;'});
put(fullfile(lib, '+pkg', 'mk.m'), {'function r = mk()', 'r = C(1);', 'end'});
put(fullfile(lib, '+pkg', '@D', 'D.m'), {'classdef D', 'end'});
put(fullfile(lib, 'E.m'), {'classdef E', '    properties', '        V = -1', '    end', '    methods', '        function o = E(v)', '            if nargin, o.V = v; end', '        end', '    end', 'end'});
put(fullfile(lib, 'P2Plain.m'), {'classdef P2Plain', '    properties', '        V = 0', '    end', '    methods', '        function o = P2Plain(v)', '            if nargin, o.V = v; end', '        end', '        function r = stk(o)', '            r = evalc(''dbstack'');', '        end', '        function r = m(o)', '            r = o.V * 3;', '        end', '    end', '    methods (Static)', '        function r = sstk()', '            r = evalc(''dbstack'');', '        end', '    end', 'end'});

% ---- the current folder's package ----
put(fullfile(cur, '+cpkg', 'Q.m'), {'classdef Q', '    properties', '        V = 0', '    end', '    methods', '        function o = Q(v)', '            o.V = v;', '        end', '    end', 'end'});

cd(cur);
addpath(lib);
rel = @(p) strrep(p, root, '<root>');
oneline = @(t) strrep(strrep(t, char([13 10]), '/'), newline, '/');
firstline = @(t) strtok(t, newline);
nolinks = @(t) regexprep(regexprep(t, '<a href="[^"]*"[^>]*>', ''), '</a>', '');
squash = @(t) regexprep(strtrim(nolinks(t)), '\s+', ' ');
sorted = @(c) strjoin(sort(c), ',');

% ---- construction, constants, statics, methods ----
chk('ctor_property', pkg.C(3).V);
chk('ctor_default', pkg.C().V);
chk('constant', pkg.C.K);
chk('static_call', pkg.C.stat(1));
chk('static_bare_id', idof(@() pkg.C.stat));
chk('method_dot', pkg.C(3).m());
chk('method_dot_noparens', pkg.C(3).m);
chk('method_fn', m(pkg.C(3)));
o = pkg.C(3);
chk('static_via_instance', o.stat(1));
chk('static_via_instance_bare_id', idof(@() o.stat));
chk('instance_method_via_class_id', idof(@() pkg.C.m(o)));
chk('instance_method_via_class_msg', msgof(@() pkg.C.m(o)));
chk('class_nosuch_member_id', idof(@() pkg.C.nosuch));
chk('class_nosuch_member_msg', msgof(@() pkg.C.nosuch));
x = pkg.C;
chk('bare_dotted_constructs', class(x));
chk('cur_pkg_class', cpkg.Q(4).V);
chk('sub_class_method', pkg.sub.K(1).m());
chk('sub_class_ctor_inherited', pkg.sub.K(2).V);
chk('sub_class_constant', pkg.sub.K.K);
chk('sub_class_static', pkg.sub.K.stat(1));
chk('self_bare_id', idof(@() o.selfbare()));
chk('self_bare_msg', msgof(@() o.selfbare()));
chk('self_qualified', o.selfqual().V);
chk('member_fn_sees_no_bare_class_id', idof(@() pkg.mk()));
chk('isequal_same', isequal(pkg.C(1), pkg.C(1)));
chk('isequal_differ', isequal(pkg.C(1), pkg.C(2)));
chk('pkg_as_value_id', idof(@() pkg.sub));
chk('pkg_as_value_msg', msgof(@() pkg.sub));

% ---- a plain class of the same bare name beside the package's enumeration ----
chk('plain_E_class', class(E(1)));
chk('plain_E_property', E().V);
chk('plain_E_not_pkg', isa(E(1), 'pkg.E'));
chk('pkg_E_not_plain', isa(pkg.E.A, 'E'));
chk('pkg_E_after_plain', class(pkg.E.A));

% ---- names ----
k = pkg.sub.K(1);
chk('class_o', class(o));
chk('class_k', class(k));
chk('isa_qualified', isa(o, 'pkg.C'));
chk('isa_bare', isa(o, 'C'));
chk('isa_sub_of_super', isa(k, 'pkg.C'));
chk('isa_sub_of_self', isa(k, 'pkg.sub.K'));
chk('isobject', isobject(o));
chk('superclasses_k', strjoin(superclasses('pkg.sub.K')', ','));
chk('superclasses_o', strjoin(superclasses(o)', ','));
chk('metaclass_name', metaclass(o).Name);
chk('metaclass_name_sub', metaclass(k).Name);
mc = ?pkg.C;
chk('question_name', mc.Name);
mc = ?pkg.sub.K;
chk('question_name_sub', mc.Name);
chk('fromname_name', meta.class.fromName('pkg.C').Name);
chk('fromname_nosuch_empty', isempty(meta.class.fromName('pkg.nosuch')));
w = whos('o');
chk('whos_class', w.class);
chk('func2str_ctor', func2str(@pkg.C));
s1 = str2func('pkg.C');
chk('str2func_ctor', s1(3).V);
chk('feval_ctor', feval('pkg.C', 4).V);
chk('nargin_ctor', nargin('pkg.C'));
chk('nargin_static', nargin('pkg.C.stat'));
chk('nargin_method_slash_id', idof(@() nargin('pkg.C/m')));
chk('nargin_method_dot_id', idof(@() nargin('pkg.C.m')));

% ---- object arrays and empties ----
a = [pkg.C(1), pkg.C(2)];
chk('array_class', class(a));
chk('array_size', mat2str(size(a)));
chk('array_element', a(2).V);
chk('array_values', mat2str(arrayfun(@(e) e.V, a)));
chk('empty_class', class(pkg.C.empty));
chk('empty_size', mat2str(size(pkg.C.empty)));
chk('empty_sized', mat2str(size(pkg.C.empty(0, 3))));

% ---- enumerations ----
e = pkg.E.A;
chk('enum_class', class(e));
chk('enum_char', char(e));
chk('enum_string', char(string(e)));
chk('enum_eq', e == pkg.E.A);
chk('enum_ne', e == pkg.E.B);
chk('enum_isa', isa(e, 'pkg.E'));
chk('enum_disp', squash(evalc('disp(e)')));
chk('enum_listing', oneline(evalc('enumeration(''pkg.E'')')));
[em, en] = enumeration('pkg.E');
chk('enum_members_class', class(em));
chk('enum_members_names', strjoin(en', ','));
chk('enum_nosuch_member_id', idof(@() pkg.E.Z));
chk('enum_nosuch_member_msg', msgof(@() pkg.E.Z));

% ---- a handle class in a package ----
h = pkg.H();
h.bump();
bump(h);
chk('handle_class', class(h));
chk('handle_count', h.N);
chk('handle_isa_handle', isa(h, 'handle'));
chk('handle_isa_qualified', isa(h, 'pkg.H'));
chk('handle_ismethod', ismethod(h, 'bump'));
chk('handle_valid', isvalid(h));
delete(h);
chk('handle_deleted', isvalid(h));
chk('handle_deleted_display', squash(evalc('disp(h)')));

% ---- methods, properties, fieldnames, ismethod ----
chk('methods_name', strjoin(methods('pkg.C')', ','));
chk('methods_instance', strjoin(methods(o)', ','));
chk('methods_sub', strjoin(methods('pkg.sub.K')', ','));
chk('methods_printed', oneline(evalc('methods(''pkg.C'')')));
chk('properties_name', strjoin(properties('pkg.C')', ','));
chk('properties_instance', strjoin(properties(o)', ','));
chk('fieldnames_instance', strjoin(fieldnames(o)', ','));
chk('ismethod_m', ismethod(o, 'm'));
chk('ismethod_stat', ismethod(o, 'stat'));
chk('ismethod_nosuch', ismethod(o, 'nosuch'));

% ---- display ----
% (each display is disp's text with its whitespace squashed: the class name is what the row pins)
chk('display_object', squash(evalc('disp(pkg.C(3))')));
chk('display_sub', squash(evalc('disp(pkg.sub.K(1))')));
a = [pkg.C(1) pkg.C(2)];
chk('display_array', squash(evalc('disp(a)')));
e2 = pkg.C.empty;
chk('display_empty', squash(evalc('disp(e2)')));
chk('display_class_text', squash(evalc('disp(class(pkg.C(1)))')));

% ---- the package listing, asked before anything loads the class folder +pkg/@D (R2025b lists a loaded one as a class) ----
chk('help_pkg_listing', oneline(help('pkg')));

% ---- meta.package.fromName (JGraph answers a struct of the same names: decision PD5) ----
p = meta.package.fromName('pkg');
chk('metapkg_name', p.Name);
chk('metapkg_description', p.Description);
chk('metapkg_functions', sorted({p.FunctionList.Name}));
chk('metapkg_classes', sorted({p.ClassList.Name}));
chk('metapkg_packages', sorted({p.PackageList.Name}));
chk('metapkg_inner', sorted({p.InnerNamespaces.Name}));
chk('metapkg_no_parent', isempty(p.ContainingPackage));
chk('metapkg_no_outer', isempty(p.OuterNamespace));
chkdiv('metapkg_class', class(p));
chkdiv('metapkg_functionlist_class', class(p.FunctionList));
p2 = meta.package.fromName('pkg.sub');
chk('metapkg_sub_name', p2.Name);
chk('metapkg_sub_parent', p2.ContainingPackage.Name);
chk('metapkg_sub_outer', p2.OuterNamespace.Name);
chk('metapkg_sub_classes', sorted({p2.ClassList.Name}));
chk('metapkg_sub_functions_count', numel(p2.FunctionList));
chk('metapkg_sub_packages_count', numel(p2.PackageList));
chk('metapkg_nosuch_empty', isempty(meta.package.fromName('nosuchpkg')));
chk('metapkg_cpkg_classes', sorted({meta.package.fromName('cpkg').ClassList.Name}));

% ---- save and load (version 7; -v7.3 of objects is open item 131) ----
file5 = fullfile(root, 'p2_v5.mat');
o = pkg.C(3); k = pkg.sub.K(1); e = pkg.E.B; q = cpkg.Q(8); pl = P2Plain(6);
save(file5, 'o', 'k', 'e', 'q', 'pl');
clear o k e q pl
clear pkg.C pkg.sub.K pkg.E cpkg.Q P2Plain
S = load(file5);
chk('load_v5_class', class(S.o));
chk('load_v5_value', S.o.V);
chk('load_v5_sub_class', class(S.k));
chk('load_v5_sub_method', S.k.m());
chk('load_v5_enum_class', class(S.e));
chk('load_v5_enum_member', char(S.e));
chk('load_v5_cur_class', class(S.q));
chk('load_v5_plain', S.pl.m());
delete(file5);
o = pkg.C(3);

% ---- help ----
chk('help_class', oneline(help('pkg.C')));
chk('help_class_sub', oneline(help('pkg.sub.K')));
chk('help_enum', oneline(help('pkg.E')));
chk('help_method_dot', oneline(help('pkg.C.m')));
chk('help_method_slash', oneline(help('pkg.C/m')));
chk('help_static_dot', oneline(help('pkg.C.stat')));
chk('help_ctor_slash', oneline(help('pkg.C/C')));
chk('help_nosuch_method_id', idof(@() help('pkg.C.nosuch')));
chk('help_plain_class', oneline(help('P2Plain')));

% ---- which, exist ----
chk('which_class', rel(which('pkg.C')));
chk('which_class_sub', rel(which('pkg.sub.K')));
chk('which_static', rel(which('pkg.C.stat')));
chk('which_method_slash', which('pkg.C/m'));
chk('which_method_dot', which('pkg.C.m'));
chk('which_constant', which('pkg.C.K'));
chk('which_enum_member', which('pkg.E.A'));
chk('which_enum', rel(which('pkg.E')));
chk('exist_class', exist('pkg.C'));
chk('exist_class_kind', exist('pkg.C', 'class'));
chk('exist_class_file', exist('pkg.C', 'file'));
chk('exist_enum', exist('pkg.E'));
chk('exist_sub_class', exist('pkg.sub.K'));
chk('exist_static', exist('pkg.C.stat'));
chk('exist_constant', exist('pkg.C.K'));
chk('exist_nosuch', exist('pkg.nosuch'));
chk('exist_plain_class', exist('P2Plain'));
chk('exist_plain_class_kind', exist('P2Plain', 'class'));

% ---- the debugger's words: dbstop, dbstatus, dbstack, the error stack ----
dbstop in pkg.C
chk('dbstatus_class', oneline(nolinks(evalc('dbstatus'))));
dbclear all
dbstop in P2Plain
chk('dbstatus_plain_class', oneline(nolinks(evalc('dbstatus'))));
dbclear all
dbstop in pkg.sub.K
chk('dbstatus_sub_class', oneline(nolinks(evalc('dbstatus'))));
dbclear all
chk('dbstop_method_gt_id', idof(@() evalc('dbstop in pkg.C>m')));
chk('dbstop_method_gt_msg', msgof(@() evalc('dbstop in pkg.C>m')));
chk('dbstop_method_slash_msg', msgof(@() evalc('dbstop in pkg.C/m')));
chk('dbstop_method_dot_msg', msgof(@() evalc('dbstop in pkg.C.m')));
chk('dbstop_plain_method_slash_msg', msgof(@() evalc('dbstop in P2Plain/m')));
chk('dbstop_plain_method_gt_msg', msgof(@() evalc('dbstop in P2Plain>m')));
chk('dbstack_method', firstline(nolinks(pkg.W().stk())));
chk('dbstack_static', firstline(nolinks(pkg.W.sstk())));
chk('dbstack_plain_method', firstline(nolinks(P2Plain().stk())));
chk('dbstack_plain_static', firstline(nolinks(P2Plain.sstk())));
n = pkg.W().stkn();
chk('dbstack_struct_name', n{1});
try
    pkg.W().err();
catch err
    chk('method_error_id', err.identifier);
    chk('method_error_stack_name', err.stack(1).name);
    chk('method_error_stack_file', rel(err.stack(1).file));
end

% ---- clear, and a class file changed ----
chk('clear_before', pkg.C(2).V);
clear pkg.C
chk('clear_after', pkg.C(2).V);
clear functions
chk('clear_functions_after', pkg.C(2).V);
put(fullfile(lib, '+pkg', 'L.m'), {'classdef L', '    properties (Constant)', '        V = 1', '    end', 'end'});
rehash
chk('late_class_first', pkg.L.V);
put(fullfile(lib, '+pkg', 'L.m'), {'classdef L', '    properties (Constant)', '        V = 2', '    end', 'end'});
clear pkg.L
chk('late_class_changed', pkg.L.V);

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
fprintf('CHK|%s|%s|div=ADR0308\n', name, show(v));
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
