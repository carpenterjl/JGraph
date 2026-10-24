function root = k2_make_tree(root)
%K2_MAKE_TREE Write the stage K2 dependency probe's tree under root and answer root.
%   app\      the main k2_main.m and what it names, one file per question
%   lib\      a folder outside app that a build adds with addpath, -I or -a
%   other\    a second k2_helper.m, for the duplicate-name question
%   pack\     a folder of data and code that a build adds whole with -a
% Both R2025b (probe_k2_depend) and JGraph run the same tree.
if exist(root, 'dir'), rmdir(root, 's'); end
app = fullfile(root, 'app');
mkdir(app); mkdir(fullfile(app, 'private')); mkdir(fullfile(app, 'sub'));
mkdir(fullfile(root, 'lib')); mkdir(fullfile(root, 'other'));
mkdir(fullfile(root, 'pack')); mkdir(fullfile(root, 'pack', 'deeper'));

put(app, 'k2_main.m', [
    "function k2_main(varargin)"
    "%#function k2_pragma"
    "%#exclude k2_excl"
    "k2_helper(1);"
    "k2_script"
    "o = k2_cls(2); o.go();"
    "x = load('k2_data.mat');"
    "t = fileread(""k2_notes.txt"");"
    "s = readtable('sub/k2_table.csv');"
    "f = fopen('k2_missing.txt');"
    "im = imread(fullfile('a', 'b.png'));"
    "feval('k2_byname');"
    "g = str2func('k2_strfn');"
    "h = @k2_handle;"
    "c = cellfun(@k2_cellfn, {1});"
    "k2_excl();"
    "k2_shadow = 3; disp(k2_shadow)"
    "k2_cmd hello"
    "q = x.k2_field;"
    "w = 'k2_inquote';"
    "k2_libfn(1);"
    "unknown_k2_fn(3);"
    "edit k2_main"
    "help k2_main"
    "y = butter(2, 0.5);"
    "z = local_one(1);"
    "end"
    "function r = local_one(a)"
    "r = k2_from_local(a);"
    "end"
    ]);
put(app, 'k2_helper.m', ["function k2_helper(a)", "k2_priv(a);", "helper_sub(a);", "end", ...
    "function helper_sub(a)", "k2_from_sub(a);", "end"]);
put(fullfile(app, 'private'), 'k2_priv.m', ["function k2_priv(a)", "disp(a)", "end"]);
put(app, 'k2_from_sub.m', ["function k2_from_sub(a)", "disp(a)", "end"]);
put(app, 'k2_script.m', ["k2_from_script(1);"]);
put(app, 'k2_from_script.m', ["function k2_from_script(a)", "disp(a)", "end"]);
put(app, 'k2_cls.m', ["classdef k2_cls", "    properties", "        V", "    end", ...
    "    methods", "        function o = k2_cls(v)", "            o.V = v;", "        end", ...
    "        function go(o)", "            k2_from_method(o.V);", "        end", "    end", "end"]);
put(app, 'k2_from_method.m', ["function k2_from_method(a)", "k2_deep(a);", "end"]);
put(app, 'k2_deep.m', ["function k2_deep(a)", "disp(a)", "end"]);
put(app, 'k2_from_local.m', ["function r = k2_from_local(a)", "r = a;", "end"]);
for n = ["k2_byname", "k2_strfn", "k2_pragma", "k2_handle", "k2_cellfn", "k2_excl", ...
        "k2_shadow", "k2_cmd", "k2_field", "k2_inquote", "k2_unused"]
    put(app, n + ".m", ["function varargout = " + n + "(varargin)", "varargout = {1};", "end"]);
end
note = 'k2 data'; save(fullfile(app, 'k2_data.mat'), 'note');
save(fullfile(app, 'k2_helper.fig'), '-mat', 'note');
save(fullfile(app, 'k2_main.fig'), '-mat', 'note');
writelines("k2 notes", fullfile(app, 'k2_notes.txt'));
writelines(["a,b", "1,2"], fullfile(app, 'sub', 'k2_table.csv'));
writelines("not named", fullfile(app, 'k2_unnamed.txt'));

put(fullfile(root, 'lib'), 'k2_libfn.m', ["function k2_libfn(a)", "k2_libdeep(a);", "end"]);
put(fullfile(root, 'lib'), 'k2_libdeep.m', ["function k2_libdeep(a)", "disp(a)", "end"]);
put(fullfile(root, 'other'), 'k2_helper.m', ["function k2_helper(a)", "disp(-a)", "end"]);
put(fullfile(root, 'pack'), 'k2_packfn.m', ["function k2_packfn", "k2_packdeep;", "end"]);
put(fullfile(root, 'pack', 'deeper'), 'k2_packdeep.m', ["function k2_packdeep", "disp(1)", "end"]);
writelines("pack text", fullfile(root, 'pack', 'k2_pack.txt'));
writelines("deeper text", fullfile(root, 'pack', 'deeper', 'k2_deeper.txt'));
end

function put(folder, name, lines)
writelines(lines, fullfile(folder, name));
end
