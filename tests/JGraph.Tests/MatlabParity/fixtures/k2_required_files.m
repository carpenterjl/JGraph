% record: -noFigureWindows
% Compiler and SDK plan, stage K2: matlab.codetools.requiredFilesAndProducts over a small tree -
% calls, subfunctions, private/, a class and its superclass, a script, handles, %#function and
% %#exclude, variables that shadow a file, the .fig beside a code file, data files named by
% literals - its products, its 'toponly' flag, its refusals, and the support-package stub. The
% rules were measured first by probe_k2_depend and probe_k2_rules (tools/matlab-checklist/compiler-probes).

root = tempname;
app = fullfile(root, 'app');
lib = fullfile(root, 'lib');
mkdir(fullfile(app, 'private'));
mkdir(fullfile(app, 'sub'));
mkdir(lib);
put(app, 'k2f_main', {
    'function k2f_main(varargin)'
    '%#function k2f_pragma'
    '%#exclude k2f_excl'
    'k2f_helper(1);'
    'k2f_script'
    'o = k2f_cls(2); o.go();'
    'x = load(''k2f_data'');'
    't = fileread("k2f_notes.txt");'
    'n = ''k2f_named.txt''; u = fileread(n);'
    's = readtable(''sub/k2f_table.csv'');'
    'feval(''k2f_byname'');'
    'h = @k2f_handle;'
    'k2f_excl();'
    'k2f_shadow = 3; disp(k2f_shadow)'
    'k2f_later(1); k2f_later = 2;'
    'k2f_cmd hello'
    'q = x.k2f_field;'
    'k2f_libfn(1);'
    'unknown_k2f_fn(3);'
    'y = butter(2, 0.5);'
    'z = local_one(1);'
    'end'
    'function r = local_one(a)'
    'r = k2f_from_local(a);'
    'end'
    });
put(app, 'k2f_helper', {'function k2f_helper(a)', 'k2f_priv(a);', 'helper_sub(a);', 'end', ...
    'function helper_sub(a)', 'k2f_from_sub(a);', 'end'});
put(fullfile(app, 'private'), 'k2f_priv', {'function k2f_priv(a)', 'disp(a)', 'end'});
put(app, 'k2f_script', {'k2f_from_script(1);'});
put(app, 'k2f_cls', {'classdef k2f_cls < k2f_base', 'properties', 'V = k2f_default(1);', 'end', ...
    'methods', 'function o = k2f_cls(v)', 'o.V = v;', 'end', 'function go(o)', 'k2f_from_method(o.V);', ...
    'end', 'end', 'end'});
put(app, 'k2f_base', {'classdef k2f_base < handle', 'end'});
for n = {'k2f_from_sub', 'k2f_from_script', 'k2f_from_method', 'k2f_from_local', 'k2f_default', ...
        'k2f_pragma', 'k2f_byname', 'k2f_handle', 'k2f_excl', 'k2f_shadow', 'k2f_later', 'k2f_cmd', ...
        'k2f_field', 'k2f_unused'}
    put(app, n{1}, {['function varargout = ' n{1} '(varargin)'], 'varargout = {1};', 'end'});
end
put(lib, 'k2f_libfn', {'function k2f_libfn(a)', 'k2f_libdeep(a);', 'end'});
put(lib, 'k2f_libdeep', {'function k2f_libdeep(a)', 'disp(a)', 'end'});
for f = {'k2f_data.mat', 'k2f_notes.txt', 'k2f_named.txt', 'k2f_helper.fig', 'k2f_main.fig', 'sub/k2f_table.csv'}
    writelines('1,2', fullfile(app, f{1}));
end

old = cd(app);
addpath(lib);
u2_chk('main_files', @() files(root, 'k2f_main.m'));
u2_chk('main_products', @() products('k2f_main.m'));
u2_chk('toponly_files', @() files(root, 'k2f_main.m', 'toponly'));
u2_chk('toponly_upper', @() files(root, 'k2f_main.m', 'TOPONLY'));
u2_chk('noext', @() files(root, 'k2f_helper'));
u2_chk('fullpath', @() files(root, fullfile(app, 'k2f_helper.m')));
u2_chk('cell_two', @() files(root, {'k2f_helper.m', 'k2f_script.m'}));
u2_chk('string_array', @() files(root, ["k2f_helper.m", "k2f_script.m"]));
u2_chk('class_file', @() files(root, 'k2f_cls.m'));
u2_chk('script_file', @() files(root, 'k2f_script.m'));
u2_chk('data_file', @() files(root, 'k2f_notes.txt'));
u2_chk('fig_file', @() files(root, 'k2f_helper.fig'));
u2_chk('folder', @() files(root, lib));
u2_chk('builtin_name', @() files(root, 'ones'));
u2_chk('builtin_products', @() products('ones'));
u2_chk('toolbox_name_products', @() products('butter'));
u2_chk('empty_cell', @() files(root, {}));
u2_chk('empty_cell_size', @() mat2str(size(matlab.codetools.requiredFilesAndProducts({}))));
u2_chk('files_shape', @() mat2str(size(matlab.codetools.requiredFilesAndProducts('k2f_script.m'))));
u2_chk('files_class', @() class(matlab.codetools.requiredFilesAndProducts('k2f_script.m')));
u2_chk('product_fields', @() strjoin(fieldnames(secondOut('k2f_script.m'))', ','));
u2_chk('product_shape', @() mat2str(size(secondOut('k2f_main.m'))));
u2_chk('product_classes', @() productClasses());
u2_chk('missing', @() matlab.codetools.requiredFilesAndProducts('k2f_nosuch.m'));
u2_chk('missing_noext', @() matlab.codetools.requiredFilesAndProducts('k2f_nosuch'));
u2_chk('number', @() matlab.codetools.requiredFilesAndProducts(5));
u2_chk('cell_number', @() matlab.codetools.requiredFilesAndProducts({5}));
u2_chk('bad_flag', @() matlab.codetools.requiredFilesAndProducts('k2f_helper.m', 'bogus'));
u2_chk('no_args', @() matlab.codetools.requiredFilesAndProducts());
u2_chk('three_args', @() matlab.codetools.requiredFilesAndProducts('k2f_helper.m', 'toponly', 'toponly'));
u2_chk('three_outputs', @() threeOut());
rmpath(lib);
u2_chk('main_without_lib', @() files(root, 'k2f_main.m'));
cd(root);
u2_chk('off_path', @() files(root, fullfile(app, 'k2f_helper.m')));
cd(app);
u2_chk('dsp_main', @() mat2str(size(compiler.codetools.deployableSupportPackages('k2f_main.m'))));
u2_chk('dsp_class', @() class(compiler.codetools.deployableSupportPackages('k2f_main.m')));
u2_chk('dsp_missing', @() compiler.codetools.deployableSupportPackages('k2f_nosuch.m'));
cd(old);
rmdir(root, 's');

function put(folder, name, lines)
writelines(lines, fullfile(folder, [name '.m']));
end

function s = files(root, varargin)
f = matlab.codetools.requiredFilesAndProducts(varargin{:});
s = strjoin(strrep(f, [root filesep], ''), ' | ');
end

function s = products(varargin)
[~, p] = matlab.codetools.requiredFilesAndProducts(varargin{:});
s = strjoin(arrayfun(@(e) sprintf('%s %s %d %d', e.Name, e.Version, e.ProductNumber, e.Certain), p, ...
    'UniformOutput', false), ' | ');
end

function p = secondOut(varargin)
[~, p] = matlab.codetools.requiredFilesAndProducts(varargin{:});
end

function s = productClasses()
p = secondOut('k2f_script.m');
s = strjoin({class(p(1).Name), class(p(1).Version), class(p(1).ProductNumber), class(p(1).Certain)}, ',');
end

function v = threeOut()
[a, b, c] = matlab.codetools.requiredFilesAndProducts('k2f_script.m'); %#ok<ASGLU>
v = 1;
end
