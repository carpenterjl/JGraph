% record: -noFigureWindows
% Compiler and SDK plan, stage K13: Python packages - compiler.build.PythonPackageOptions, its
% defaults, derived names and refusals; mcc -W python, how it names a package and what it refuses,
% and the folders and files a build writes. Nothing is run in Python, so neither engine needs it.
beep('off');
root = tempname;
src = fullfile(root, 'src');
mkdir(src);
put(src, 'k13f.m', {'function [a, b] = k13f(x, n)', '%K13F A probe function.', 'a = x * n; b = numel(x);', 'end'});
put(src, 'k13g.m', {'function k13g(s)', '%K13G Nothing out.', 'disp(s);', 'end'});
put(src, 'k13s.m', {'% A script.', 'k13v = 1;'});
put(src, 'k13sample.m', {'% A sample.', 'x = 2;', '[a, b] = k13f(x, 3);'});
put(src, 'k13notes.txt', {'notes'});
old = cd(src);

% The options from one file: the defaults and the names derived from it.
o = compiler.build.PythonPackageOptions('k13f.m');
u2_chk('class', @() class(o));
u2_chk('props', @() strjoin(properties(o)', ' '));
u2_chk('name', @() o.PackageName);
u2_chk('getname', @() o.getName);
u2_chk('outdir', @() o.OutputDir);
u2_chk('samples', @() class(o.SampleGenerationFiles));
u2_chk('samples_size', @() size(o.SampleGenerationFiles));
u2_chk('files', @() rel(o.FunctionFiles, src));
u2_chk('verbose', @() char(o.Verbose));
u2_chk('two_name', @() compiler.build.PythonPackageOptions({'k13g.m', 'k13f.m'}).PackageName);
u2_chk('given_name', @() compiler.build.PythonPackageOptions('k13f.m', 'PackageName', 'k13named').OutputDir);
u2_chk('dotted_outdir', @() compiler.build.PythonPackageOptions('k13f.m', 'PackageName', 'org.k13.deep').OutputDir);
u2_chk('dotted_getname', @() compiler.build.PythonPackageOptions('k13f.m', 'PackageName', 'org.k13.deep').getName);
u2_chk('nv_prefix', @() compiler.build.PythonPackageOptions('k13f.m', 'PackageN', 'k13pre').PackageName);
u2_chk('nv_case', @() compiler.build.PythonPackageOptions('k13f.m', 'packagename', 'k13case').PackageName);
u2_chk('script_file', @() rel(compiler.build.PythonPackageOptions('k13s.m').FunctionFiles, src));

% The constructor's refusals.
u2_chk('none', @() err(@() compiler.build.PythonPackageOptions()));
u2_chk('missing', @() err(@() compiler.build.PythonPackageOptions('k13nosuch.m')));
u2_chk('not_code', @() err(@() compiler.build.PythonPackageOptions('k13notes.txt')));
u2_chk('folder', @() strrep(err(@() compiler.build.PythonPackageOptions({src})), src, '<src>'));
u2_chk('number', @() err(@() compiler.build.PythonPackageOptions(5)));
u2_chk('bogus_nv', @() err(@() compiler.build.PythonPackageOptions('k13f.m', 'Bogus', 1)));
u2_chk('odd_nv', @() err(@() compiler.build.PythonPackageOptions('k13f.m', 'PackageName')));
u2_chk('interface_nv', @() err(@() compiler.build.PythonPackageOptions('k13f.m', 'Interface', 'x')));

% Each property's checks: a package name may be nearly anything but nothing.
o = compiler.build.PythonPackageOptions('k13f.m');
u2_chk('set_name_digit', @() setp(o, 'PackageName', '1abc'));
u2_chk('set_name_dash', @() setp(o, 'PackageName', 'a-b'));
u2_chk('set_name_dots', @() setp(o, 'PackageName', 'a..b'));
u2_chk('set_name_keyword', @() setp(o, 'PackageName', 'class'));
u2_chk('set_name_space', @() setp(o, 'PackageName', 'a b'));
u2_chk('set_name_string', @() setp(o, 'PackageName', "k13str"));
u2_chk('set_name_empty', @() err(@() setp(o, 'PackageName', '')));
u2_chk('set_name_number', @() err(@() setp(o, 'PackageName', 3)));
u2_chk('set_name_cell', @() err(@() setp(o, 'PackageName', {'k13'})));
u2_chk('set_samples_missing', @() err(@() setp(o, 'SampleGenerationFiles', 'k13nosuch.m')));
u2_chk('set_samples_text', @() err(@() setp(o, 'SampleGenerationFiles', 'k13notes.txt')));
u2_chk('set_samples_ok', @() rel(setp(o, 'SampleGenerationFiles', 'k13sample.m'), src));
u2_chk('set_samples_empty', @() size(setp(o, 'SampleGenerationFiles', {})));
u2_chk('set_verbose', @() char(setp(o, 'Verbose', true)));

% mcc -W python: what each form writes, and its refusals.
u2_chk('mcc_default', @() build(root, 'default', {'-W', 'python:k13m', '-T', 'link:lib', 'k13f.m', 'k13g.m'}));
u2_chk('mcc_notarget', @() build(root, 'notarget', {'-W', 'python:k13nt', 'k13f.m'}));
u2_chk('mcc_dotted', @() build(root, 'dotted', {'-W', 'python:org.k13m', '-T', 'link:lib', 'k13f.m'}));
u2_chk('mcc_dotdot', @() build(root, 'dotdot', {'-W', 'python:a..b', '-T', 'link:lib', 'k13f.m'}));
u2_chk('mcc_twice', @() build(root, 'twice', {'-W', 'python:k13t1', '-W', 'python:k13t2', 'k13f.m'}));
u2_chk('mcc_version', @() build(root, 'version', {'-W', 'python:k13vr,version=1.0', '-T', 'link:lib', 'k13f.m'}));
u2_chk('mcc_extra', @() build(root, 'extra', {'-W', 'python:k13cm,extra', 'k13f.m'}));
u2_chk('mcc_exe', @() build(root, 'exe', {'-W', 'python:k13ex', '-T', 'link:exe', 'k13f.m'}));
u2_chk('mcc_separate', @() build(root, 'separate', {'-W', 'python:k13sp', '-C', 'k13f.m'}));
u2_chk('mcc_badname', @() build(root, 'badname', {'-W', 'python:1bad', 'k13f.m'}));
u2_chk('mcc_script', @() build(root, 'script', {'-W', 'python:k13sc', 'k13s.m'}));
u2_chk('mcc_noname', @() build(root, 'noname', {'-W', 'python:', 'k13f.m'}));
u2_chk('mcc_nocolon', @() build(root, 'nocolon', {'-W', 'python', 'k13f.m'}));
u2_chk('mcc_o_after', @() build(root, 'o_after', {'-W', 'python:k13on', '-o', 'k13other', 'k13f.m'}));
u2_chk('mcc_o_first', @() build(root, 'o_first', {'-o', 'k13of', '-W', 'python:k13of2', 'k13f.m'}));
u2_chk('mcc_dash_c', @() build(root, 'dash_c', {'-W', 'python:k13cc', '-c', 'k13f.m'}));
u2_chk('mcc_no_files', @() build(root, 'no_files', {'-W', 'python:k13nf'}));
u2_chk('mcc_setup_name', @() setupField(fullfile(root, 'default', 'setup.py'), 'name'));
u2_chk('mcc_setup_version', @() setupField(fullfile(root, 'dotted', 'setup.py'), 'version'));
u2_chk('mcc_setup_packages', @() grab(fullfile(root, 'dotted', 'setup.py'), '^ +''org'));
u2_chk('mcc_init_versions', @() grab(fullfile(root, 'default', 'k13m', '__init__.py'), '^ *(SUPPORTED_PYTHON_VERSIONS|PACKAGE_NAME) = '));
u2_chk('mcc_init_functions', @() grab(fullfile(root, 'default', 'k13m', '__init__.py'), '^def '));
u2_chk('mcc_namespace', @() grab(fullfile(root, 'dotted', 'org', '__init__.py'), 'extend_path'));
u2_chk('mcc_toml', @() grab(fullfile(root, 'default', 'pyproject.toml'), '^(\[|requires|build-backend)'));
u2_chk('mcc_readme_title', @() grab(fullfile(root, 'dotted', 'readme.txt'), 'MATLAB Python Package$'));

% compiler.build.pythonPackage: what it lists and writes, and its own refusals.
r = compiler.build.pythonPackage('k13f.m', 'PackageName', 'k13api', 'OutputDir', fullfile(root, 'api'));
u2_chk('api_type', @() r.BuildType);
u2_chk('api_files', @() rel(r.Files, fullfile(root, 'api')));
u2_chk('api_options', @() class(r.Options));
u2_chk('api_written', @() names(fullfile(root, 'api')));
r2 = compiler.build.pythonPackage('k13g.m', 'OutputDir', fullfile(root, 'api2'));
u2_chk('api_default_files', @() rel(r2.Files, fullfile(root, 'api2')));
u2_chk('api_default_written', @() names(fullfile(root, 'api2')));
u2_chk('api_wrong_options', @() err(@() compiler.build.pythonPackage(compiler.build.StandaloneApplicationOptions('k13f.m'))));
u2_chk('api_no_args', @() err(@() compiler.build.pythonPackage()));
u2_chk('api_two_out', @() err(@() twoOut()));
u2_chk('api_extra', @() err(@() compiler.build.pythonPackage(o, 'Verbose', 'on')));
u2_chk('api_bad_nv', @() err(@() compiler.build.pythonPackage('k13f.m', 'Bogus', 1)));
cd(old);
rmdir(root, 's');

function v = setp(o, name, value)
% Sets a property on a copy of the options and answers what it then reads.
o.(name) = value;
v = o.(name);
end

function twoOut()
[a, b] = compiler.build.pythonPackage('k13f.m'); %#ok<ASGLU>
end

function s = build(root, name, args)
% A build's outcome: ok or its refusal, the warning it raised, and the files it wrote.
folder = fullfile(root, name);
mkdir(folder);
lastwarn('', '');
s = err(@() quiet(args, folder));
[~, id] = lastwarn;
s = sprintf('%s | warn %s | files %s', s, id, names(folder));
end

function quiet(args, folder)
% A build with what it prints left out.
evalc('mcc(args{:}, ''-d'', folder)');
end

function s = err(f)
try
    f();
    s = 'no error';
catch e
    s = [e.identifier ' / ' strrep(e.message, newline, ' / ')];
end
end

function s = names(folder)
% The files a build wrote, named from its folder; each engine's logs and archive alike.
if ~exist(folder, 'dir')
    s = '';
    return
end
d = dir(fullfile(folder, '**', '*'));
d = d(~[d.isdir]);
files = cellfun(@(f, n) strrep(fullfile(f, n), [folder filesep], ''), {d.folder}, {d.name}, 'UniformOutput', false);
s = strjoin(sort(strrep(files, filesep, '/')), ' ');
end

function s = grab(file, pattern)
% The lines of a generated file that match, trimmed and joined.
if ~exist(file, 'file')
    s = 'missing';
    return
end
text = strrep(fileread(file), sprintf('\r\n'), newline);
lines = splitlines(text);
keep = lines(~cellfun(@isempty, regexp(lines, pattern, 'once')));
s = strjoin(strtrim(keep)', ' / ');
end

function s = setupField(file, field)
% A setup.py's value for field, written either as a dict entry or as a keyword argument.
text = fileread(file);
found = regexp(text, ['(''' field '''\s*:\s*|\<' field '\s*=\s*)''([^'']*)'''], 'tokens', 'once');
if isempty(found)
    s = 'missing';
else
    s = found{2};
end
end

function s = rel(files, folder)
files = cellfun(@(f) strrep(f, [folder filesep], ''), cellstr(files), 'UniformOutput', false);
s = strjoin(files(:)', ' ');
end

function put(folder, name, lines)
fid = fopen(fullfile(folder, name), 'w');
fprintf(fid, '%s\n', lines{:});
fclose(fid);
end
