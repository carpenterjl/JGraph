% p3_imports.m -- the packages and class folders plan's stage P3 (ADR 0309): import of +package
% names -- explicit and wildcard imports of a package's functions, scripts and classes, a class's
% statics through import pkg.C.*, a sub-package in front of a dot; the measured precedence (an
% explicit import above nested and local functions, a wildcard below them and above private/, the
% current folder and the built-ins; the earlier of two wildcards; an explicit import over any
% wildcard; a variable over a wildcard); the parse-time refusals with R2025b's File/Line/Column
% message; the listing; the scope (nested and anonymous functions see a function's imports, local
% functions and callees do not, a script's import applies to its statements); which, exist, isa,
% nargin and methods over imported names; handles, str2func, feval and eval against a file of the
% name in reach; clear all and clear import; and an import at a script's own level. Every rule is
% R2025b's as recorded here; the research probes (docs/plans/notes/packages-research-2026-11-05)
% settled most of them first.
%
% R2025b resolves a function's imports when it parses the file, so every case is a function file of
% its own, written under tempdir: a library (+pkg, +other, the helpers), a second path folder
% holding a plain f.m, a clean current folder (cur2) for most cases, and a current folder (cur)
% holding private/f.m and only.m for the cases about a file of the name in reach.

start = pwd;
root = fullfile(tempdir, 'p3_imports');
if exist(root, 'dir') == 7
    rmdir(root, 's');
end
cur = fullfile(root, 'cur');
cur2 = fullfile(root, 'cur2');
lib = fullfile(root, 'lib');
lib2 = fullfile(root, 'lib2');
mkdir(cur);
mkdir(cur2);
mkdir(lib);
mkdir(lib2);

% ---- the library ----
put(fullfile(lib, '+pkg', 'f.m'), {'function r = f(x)', 'r = x * 10;', 'end'});
put(fullfile(lib, '+pkg', 'two.m'), {'function r = two(x)', 'r = x;', 'end'});
put(fullfile(lib, '+pkg', 'C.m'), {'classdef C', '    properties (Constant)', '        K = 7', '    end', '    properties', '        V = 0', '    end', '    methods', '        function o = C(v)', '            if nargin, o.V = v; end', '        end', '        function r = m(o)', '            r = o.V * 2;', '        end', '    end', '    methods (Static)', '        function r = stat(x)', '            r = x + 100;', '        end', '    end', 'end'});
put(fullfile(lib, '+pkg', 'E.m'), {'classdef E', '    enumeration', '        A, B', '    end', 'end'});
put(fullfile(lib, '+pkg', 's.m'), {'sval = 42;'});
put(fullfile(lib, '+pkg', 'max.m'), {'function r = max(x)', 'r = ''pkg max'';', 'end'});
put(fullfile(lib, '+pkg', 'Pow.m'), {'function r = Pow(a, b)', 'r = ''pkg Pow'';', 'end'});
put(fullfile(lib, '+pkg', 'mk2.m'), {'function r = mk2()', 'import pkg.C', 'r = C(2);', 'end'});
put(fullfile(lib, '+pkg', '+sub', 'h.m'), {'function r = h(x)', 'r = x - 1;', 'end'});
put(fullfile(lib, '+pkg', '+sub', 'K.m'), {'classdef K < pkg.C', '    methods', '        function r = m(o)', '            r = m@pkg.C(o) + 1;', '        end', '    end', 'end'});
put(fullfile(lib, '+pkg', '+sub', 'usesimport.m'), {'function r = usesimport(x)', 'import pkg.*', 'r = f(x);', 'end'});
put(fullfile(lib, '+other', 'f.m'), {'function r = f(x)', 'r = x * 1000;', 'end'});
put(fullfile(lib, '+other', 'only.m'), {'function r = only()', 'r = ''other only'';', 'end'});
put(fullfile(lib, 'P3UseImp.m'), {'classdef P3UseImp', '    methods (Static)', '        function r = go(x)', '            import pkg.*', '            r = f(x);', '        end', '    end', 'end'});
put(fullfile(lib, 'p3_calls_f.m'), {'function r = p3_calls_f()', 'r = f(3);', 'end'});
put(fullfile(lib, 'p3_imp_script.m'), {'import pkg.*', 'scriptval = f(4);'});
put(fullfile(lib2, 'f.m'), {'function r = f(x)', 'r = ''path f'';', 'end'});

% ---- the clean current folder's cases ----
put(fullfile(cur2, 'imp_explicit.m'), {'function r = imp_explicit()', 'import pkg.f', 'r = f(2);', 'end'});
put(fullfile(cur2, 'imp_wild.m'), {'function r = imp_wild()', 'import pkg.*', 'r = {f(2), C(3).V, C.K, C.stat(1), two(4), class(E.A)};', 'end'});
put(fullfile(cur2, 'imp_wild_class.m'), {'function r = imp_wild_class()', 'import pkg.*', 'o = C(3);', 'r = {class(o), o.m()};', 'end'});
put(fullfile(cur2, 'imp_class.m'), {'function r = imp_class()', 'import pkg.C', 'r = {C(4).V, C.K, C.stat(1), class(C)};', 'end'});
put(fullfile(cur2, 'imp_enum.m'), {'function r = imp_enum()', 'import pkg.E', 'r = class(E.A);', 'end'});
put(fullfile(cur2, 'imp_sub.m'), {'function r = imp_sub()', 'import pkg.sub.*', 'r = {h(5), class(K(1))};', 'end'});
put(fullfile(cur2, 'imp_sub_explicit.m'), {'function r = imp_sub_explicit()', 'import pkg.sub', 'r = sub.h(4);', 'end'});
put(fullfile(cur2, 'imp_class_wild.m'), {'function r = imp_class_wild()', 'import pkg.C.*', 'r = stat(1);', 'end'});
put(fullfile(cur2, 'imp_class_wild_const.m'), {'function r = imp_class_wild_const()', 'import pkg.C.*', 'r = K;', 'end'});
put(fullfile(cur2, 'imp_dotted_call.m'), {'function r = imp_dotted_call()', 'import pkg.*', 'r = sub.h(4);', 'end'});
put(fullfile(cur2, 'imp_script_wild.m'), {'function r = imp_script_wild()', 'import pkg.*', 's', 'r = sval;', 'end'});
put(fullfile(cur2, 'imp_script_explicit.m'), {'function r = imp_script_explicit()', 'import pkg.s', 's', 'r = sval;', 'end'});
put(fullfile(cur2, 'imp_builtin_name.m'), {'function r = imp_builtin_name()', 'import pkg.*', 'r = max([1 5 2]);', 'end'});
put(fullfile(cur2, 'imp_builtin_explicit.m'), {'function r = imp_builtin_explicit()', 'import pkg.max', 'r = max([1 5 2]);', 'end'});
put(fullfile(cur2, 'imp_after_use.m'), {'function r = imp_after_use()', 'a = 1 + 1;', 'import pkg.*', 'r = f(a);', 'end'});
put(fullfile(cur2, 'imp_nosuch_pkg.m'), {'function r = imp_nosuch_pkg()', 'import nosuchpkg.*', 'r = 1;', 'end'});
put(fullfile(cur2, 'imp_two_wild.m'), {'function r = imp_two_wild()', 'import pkg.*', 'import other.*', 'r = f(2);', 'end'});
put(fullfile(cur2, 'imp_two_wild_swapped.m'), {'function r = imp_two_wild_swapped()', 'import other.*', 'import pkg.*', 'r = f(2);', 'end'});
put(fullfile(cur2, 'imp_two_wild_only.m'), {'function r = imp_two_wild_only()', 'import pkg.*', 'import other.*', 'r = only();', 'end'});
put(fullfile(cur2, 'imp_explicit_over_wild.m'), {'function r = imp_explicit_over_wild()', 'import other.*', 'import pkg.f', 'r = f(2);', 'end'});
put(fullfile(cur2, 'imp_wild_over_explicit.m'), {'function r = imp_wild_over_explicit()', 'import pkg.f', 'import other.*', 'r = f(2);', 'end'});
put(fullfile(cur2, 'imp_twice.m'), {'function r = imp_twice()', 'import pkg.f', 'import other.f', 'r = f(2);', 'end'});
put(fullfile(cur2, 'imp_twice_swapped.m'), {'function r = imp_twice_swapped()', 'import other.f', 'import pkg.f', 'r = f(2);', 'end'});
put(fullfile(cur2, 'imp_net_first.m'), {'function r = imp_net_first()', 'import System.Math.*', 'import pkg.*', 'r = Pow(2, 3);', 'end'});
put(fullfile(cur2, 'imp_pkg_first.m'), {'function r = imp_pkg_first()', 'import pkg.*', 'import System.Math.*', 'r = Pow(2, 3);', 'end'});
put(fullfile(cur2, 'imp_wild_var.m'), {'function r = imp_wild_var()', 'import pkg.*', 'f = [10 20 30];', 'r = f(2);', 'end'});
put(fullfile(cur2, 'imp_var.m'), {'function r = imp_var()', 'import pkg.f', 'f = 5;', 'r = f(2);', 'end'});
put(fullfile(cur2, 'imp_shadow_nested.m'), {'function r = imp_shadow_nested()', 'import pkg.f', 'r = f(2);', '    function v = f(x)', '        v = ''nested f'';', '    end', 'end'});
put(fullfile(cur2, 'imp_shadow_local.m'), {'function r = imp_shadow_local()', 'import pkg.*', 'r = f(2);', 'end', 'function r = f(x)', 'r = ''local f'';', 'end'});
put(fullfile(cur2, 'imp_pkg_bare.m'), {'function r = imp_pkg_bare()', 'import pkg', 'r = f(2);', 'end'});
put(fullfile(cur2, 'imp_nosuch.m'), {'function r = imp_nosuch()', 'import pkg.nosuch', 'r = 1;', 'end'});
put(fullfile(cur2, 'imp_two_names.m'), {'function r = imp_two_names()', 'import pkg.f pkg.nosuch', 'r = 1;', 'end'});
put(fullfile(cur2, 'imp_list.m'), {'function r = imp_list()', 'import pkg.f', 'import other.*', 'L = import;', 'r = strjoin(L'', '','');', 'end'});
put(fullfile(cur2, 'imp_list_fn.m'), {'function r = imp_list_fn()', 'import pkg.f', 'import other.*', 'L = import();', 'r = strjoin(L'', '','');', 'end'});
put(fullfile(cur2, 'imp_list_empty.m'), {'function r = imp_list_empty()', 'L = import;', 'r = sprintf(''%dx%d %s'', size(L, 1), size(L, 2), class(L));', 'end'});
put(fullfile(cur2, 'imp_clear_in_fn.m'), {'function r = imp_clear_in_fn()', 'import pkg.*', 'clear import', 'r = 1;', 'end'});
put(fullfile(cur2, 'imp_nested.m'), {'function r = imp_nested()', 'import pkg.*', 'r = inner();', '    function v = inner()', '        v = f(3);', '    end', 'end'});
put(fullfile(cur2, 'imp_local.m'), {'function r = imp_local()', 'import pkg.*', 'r = loc();', 'end', 'function v = loc()', 'v = f(3);', 'end'});
put(fullfile(cur2, 'imp_anon.m'), {'function r = imp_anon()', 'import pkg.*', 'g = @(x) f(x);', 'r = g(3);', 'end'});
put(fullfile(cur2, 'imp_callee.m'), {'function r = imp_callee()', 'import pkg.*', 'r = p3_calls_f();', 'end'});
put(fullfile(cur2, 'imp_clear_all.m'), {'function r = imp_clear_all()', 'import pkg.*', 'clear all', 'r = f(2);', 'end'});
put(fullfile(cur2, 'imp_scriptcase.m'), {'function r = imp_scriptcase()', 'evalc(''p3_imp_script'');', 'L = import;', 'r = {scriptval, strjoin(L'', '','')};', 'end'});
put(fullfile(cur2, 'imp_which.m'), {'function r = imp_which()', 'import pkg.f', 'r = {which(''f''), exist(''f''), exist(''f'', ''file'')};', 'end'});
put(fullfile(cur2, 'imp_exist_class.m'), {'function r = imp_exist_class()', 'import pkg.C', 'r = {exist(''C''), exist(''C'', ''class''), which(''C''), isa(C(1), ''C''), isa(C(1), ''pkg.C''), class(C(1))};', 'end'});
put(fullfile(cur2, 'imp_isa_wild.m'), {'function r = imp_isa_wild()', 'import pkg.*', 'r = {isa(C(1), ''C''), exist(''C''), exist(''C'', ''class''), exist(''f''), which(''f'')};', 'end'});
put(fullfile(cur2, 'imp_nargin.m'), {'function r = imp_nargin()', 'import pkg.f', 'try, r = nargin(''f''); catch e, r = e.identifier; end', 'end'});
put(fullfile(cur2, 'imp_methods.m'), {'function r = imp_methods()', 'import pkg.C', 'try, r = double(ismember(''stat'', methods(''C''))); catch e, r = e.identifier; end', 'end'});
put(fullfile(cur2, 'imp_handle.m'), {'function r = imp_handle()', 'import pkg.*', 'h = @f;', 'r = {func2str(h), h(2)};', 'end'});
put(fullfile(cur2, 'imp_handle_explicit.m'), {'function r = imp_handle_explicit()', 'import pkg.f', 'h = @f;', 'r = {func2str(h), h(2)};', 'end'});
put(fullfile(cur2, 'imp_handle_builtin.m'), {'function r = imp_handle_builtin()', 'import pkg.*', 'h = @max;', 'r = {func2str(h), h([1 5 2])};', 'end'});
put(fullfile(cur2, 'imp_str2func.m'), {'function r = imp_str2func()', 'import pkg.*', 'h = str2func(''f'');', 'r = {func2str(h), h(2)};', 'end'});
put(fullfile(cur2, 'imp_feval.m'), {'function r = imp_feval()', 'import pkg.*', 'r = feval(''f'', 2);', 'end'});
put(fullfile(cur2, 'imp_eval.m'), {'function r = imp_eval()', 'import pkg.*', 'r = eval(''f(2)'');', 'end'});
put(fullfile(cur2, 'imp_call.m'), {'function r = imp_call()', 'import pkg.*', 'r = f(2);', 'end'});

% ---- the current folder with private/f.m and only.m ----
put(fullfile(cur, 'private', 'f.m'), {'function r = f(x)', 'r = ''private f'';', 'end'});
put(fullfile(cur, 'only.m'), {'function r = only()', 'r = ''current folder only'';', 'end'});
put(fullfile(cur, 'impp_call.m'), {'function r = impp_call()', 'import pkg.*', 'r = f(2);', 'end'});
put(fullfile(cur, 'impp_call_explicit.m'), {'function r = impp_call_explicit()', 'import pkg.f', 'r = f(2);', 'end'});
put(fullfile(cur, 'impp_curfolder.m'), {'function r = impp_curfolder()', 'import other.*', 'r = only();', 'end'});
put(fullfile(cur, 'impp_handle.m'), {'function r = impp_handle()', 'import pkg.*', 'h = @f;', 'r = {func2str(h), h(2)};', 'end'});
put(fullfile(cur, 'impp_handle_explicit.m'), {'function r = impp_handle_explicit()', 'import pkg.f', 'h = @f;', 'r = {func2str(h), h(2)};', 'end'});
put(fullfile(cur, 'impp_str2func.m'), {'function r = impp_str2func()', 'import pkg.*', 'h = str2func(''f'');', 'r = {func2str(h), h(2)};', 'end'});
put(fullfile(cur, 'impp_feval.m'), {'function r = impp_feval()', 'import pkg.*', 'r = feval(''f'', 2);', 'end'});
put(fullfile(cur, 'impp_eval.m'), {'function r = impp_eval()', 'import pkg.*', 'r = eval(''f(2)'');', 'end'});
put(fullfile(cur, 'impp_cellfun.m'), {'function r = impp_cellfun()', 'import pkg.*', 'r = cellfun(@f, {1, 2});', 'end'});
put(fullfile(cur, 'impp_which.m'), {'function r = impp_which()', 'import pkg.f', 'r = {which(''f''), exist(''f'')};', 'end'});

cd(cur2);
addpath(lib);
rel = @(p) strrep(p, root, '<root>');
oneline = @(t) strrep(strrep(t, char([13 10]), '/'), newline, '/');

% ---- explicit and wildcard imports ----
chk('explicit_f', imp_explicit());
r = imp_wild();
chk('wild_f', r{1});
chk('wild_ctor', r{2});
chk('wild_const', r{3});
chk('wild_static', r{4});
chk('wild_two', r{5});
chk('wild_enum', r{6});
r = imp_wild_class();
chk('wild_class_name', r{1});
chk('wild_class_method', r{2});
r = imp_class();
chk('explicit_class_ctor', r{1});
chk('explicit_class_const', r{2});
chk('explicit_class_static', r{3});
chk('explicit_class_name', r{4});
chk('explicit_enum', imp_enum());
r = imp_sub();
chk('sub_wild_h', r{1});
chk('sub_wild_class', r{2});
chk('sub_explicit_id', idof(@() imp_sub_explicit()));
chk('sub_explicit_msg', rel(oneline(msgof(@() imp_sub_explicit()))));
chk('class_wild_static', imp_class_wild());
chk('class_wild_const_id', idof(@() imp_class_wild_const()));
chk('wild_sub_dotted', imp_dotted_call());
chk('wild_script', imp_script_wild());
chk('explicit_script', imp_script_explicit());
chk('builtin_wild', imp_builtin_name());
chk('builtin_explicit', imp_builtin_explicit());
chk('after_use', imp_after_use());
chk('nosuch_pkg_wild', imp_nosuch_pkg());
chk('in_pkg_fn', pkg.sub.usesimport(1));
chk('in_pkg_fn_explicit', pkg.mk2().V);
chk('in_static_method', P3UseImp.go(1));

% ---- precedence ----
chk('two_wild_pkg_first', imp_two_wild());
chk('two_wild_other_first', imp_two_wild_swapped());
chk('two_wild_only', imp_two_wild_only());
chk('explicit_over_wild', imp_explicit_over_wild());
chk('wild_over_explicit', imp_wild_over_explicit());
chk('twice_pkg_first', imp_twice());
chk('twice_other_first', imp_twice_swapped());
chk('net_wild_first', imp_net_first());
chk('pkg_wild_first', imp_pkg_first());
chk('wild_var', imp_wild_var());
chk('var_clash_id', idof(@() imp_var()));
chk('var_clash_msg', rel(oneline(msgof(@() imp_var()))));
chk('nested_clash_id', idof(@() imp_shadow_nested()));
chk('nested_clash_msg', rel(oneline(msgof(@() imp_shadow_nested()))));
chk('shadow_local', imp_shadow_local());

% ---- refusals and the listing ----
chk('pkg_bare_id', idof(@() imp_pkg_bare()));
chk('pkg_bare_msg', rel(oneline(msgof(@() imp_pkg_bare()))));
chk('nosuch_id', idof(@() imp_nosuch()));
chk('nosuch_msg', rel(oneline(msgof(@() imp_nosuch()))));
chk('two_names_msg', rel(oneline(msgof(@() imp_two_names()))));
chk('list', imp_list());
chk('list_fn', imp_list_fn());
chk('list_empty', imp_list_empty());
chk('clear_in_fn_id', idof(@() imp_clear_in_fn()));
chk('clear_in_fn_msg', msgof(@() imp_clear_in_fn()));

% ---- scope ----
chk('nested_sees', imp_nested());
chk('local_id', idof(@() imp_local()));
chk('local_msg', msgof(@() imp_local()));
chk('anon_sees', imp_anon());
chk('callee_id', idof(@() imp_callee()));
chk('clear_all_keeps', imp_clear_all());
r = imp_scriptcase();
chk('script_from_fn_value', r{1});
chk('script_from_fn_list', r{2});

% ---- the tools and name-based calls, no file of the name in reach ----
r = imp_which();
chk('which_f', rel(r{1}));
chk('exist_f', r{2});
chk('exist_f_file', r{3});
r = imp_exist_class();
chk('exist_C', r{1});
chk('exist_C_class', r{2});
chk('which_C', rel(r{3}));
chk('isa_short', r{4});
chk('isa_qualified', r{5});
chk('class_C', r{6});
r = imp_isa_wild();
chk('isa_short_wild', r{1});
chk('exist_C_wild', r{2});
chk('exist_C_class_wild', r{3});
chk('exist_f_wild', r{4});
chk('which_f_wild', rel(r{5}));
chk('nargin_f', imp_nargin());
chk('methods_C', imp_methods());
r = imp_handle();
chk('handle_name', r{1});
chk('handle_value', r{2});
r = imp_handle_explicit();
chk('handle_explicit_name', r{1});
chk('handle_explicit_value', r{2});
r = imp_handle_builtin();
chk('handle_builtin_name', r{1});
chk('handle_builtin_value', r{2});
r = imp_str2func();
chk('str2func_name', r{1});
chk('str2func_value', r{2});
chk('feval_value', imp_feval());
chk('eval_value', imp_eval());

% ---- a plain f.m on the path ----
addpath(lib2);
chk('pathfile_call', imp_call());
chk('pathfile_call_explicit', imp_explicit());
r = imp_handle();
chk('pathfile_handle_name', r{1});
chk('pathfile_handle_value', r{2});
r = imp_handle_explicit();
chk('pathfile_handle_explicit_name', r{1});
chk('pathfile_handle_explicit_value', r{2});
chk('pathfile_feval', imp_feval());
chk('pathfile_eval', imp_eval());
r = imp_which();
chk('pathfile_which_f', rel(r{1}));
chk('pathfile_exist_f', r{2});
rmpath(lib2);

% ---- private/f.m and only.m in the current folder ----
cd(cur);
chk('private_call', impp_call());
chk('private_call_explicit', impp_call_explicit());
chk('curfolder_wild', impp_curfolder());
r = impp_handle();
chk('private_handle_name', r{1});
chk('private_handle_value', r{2});
r = impp_handle_explicit();
chk('private_handle_explicit_name', r{1});
chk('private_handle_explicit_value', r{2});
r = impp_str2func();
chk('private_str2func_name', r{1});
chk('private_str2func_value', r{2});
chk('private_feval', impp_feval());
chk('private_eval', impp_eval());
chk('private_cellfun_id', idof(@() impp_cellfun()));
r = impp_which();
chk('private_which_f', rel(r{1}));
chk('private_exist_f', r{2});
cd(cur2);

% ---- the script's own level ----
import pkg.*
chk('script_wild_f', f(2));
chk('script_wild_const', C.K);
L = import;
chk('script_list', strjoin(L', ','));
import other.*
chk('script_two_wild_f', f(2));
chk('script_wild_only', only());
try
    clear import
    chk('script_clear_import_id', 'none');
catch e
    chk('script_clear_import_id', e.identifier);
end
L = import;
chk('script_list_after', strjoin(L', ','));

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
