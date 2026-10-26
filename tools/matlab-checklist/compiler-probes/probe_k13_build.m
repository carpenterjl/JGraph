function probe_k13_build(work)
%PROBE_K13_BUILD Builds stage K13's Python packages (compiler and SDK plan) with
%compiler.build.pythonPackage and mcc -W python, and prints what each build wrote. Run under
%matlab -batch -noFigureWindows over stage K9's functions (src\k9) and K13's (src\k13); nothing
%built is run here (record-k13.ps1 runs src\k13client against the build in py\k13pkg). One
%key<TAB>value line per question, a display or a file's text as indented lines under its key.
beep('off'); % the refusals below would each beep
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'src'));
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
src = fullfile(work, 'src');
mkdir(src);
copyfile(fullfile(here, 'src', 'k9', '*'), src);
copyfile(fullfile(here, 'src', 'k13', '*'), src);
old = cd(src);
cleanup = onCleanup(@() cd(old));
fn = @(names) cellfun(@(n) fullfile(src, [n '.m']), names, 'UniformOutput', false);
mainNames = {'k9describe', 'k9make', 'k9echo', 'k9multi', 'k9nv', 'k9print', 'k9noout', 'k9nout', 'k9nargs', ...
    'k9slow', 'k9err', 'k9quiet', 'k9point', 'k9counter', 'k9color', 'k13state'};

% The client's package.
d = fullfile(work, 'py', 'k13pkg');
o = compiler.build.PythonPackageOptions(fn(mainNames), 'PackageName', 'k13pkg', 'OutputDir', d, 'Verbose', 'on');
show('main.options', o);
apiBuild('main', @() compiler.build.pythonPackage(o), d);
printText('main', d);

% Builds of other shapes, each into a folder of its own.
builds = {
    'defaults',   {fullfile(src, 'k9echo.m')}
    'dotted',     {fullfile(src, 'k9echo.m'), 'PackageName', 'org.k13.deep'}
    'twolevel',   {fullfile(src, 'k9echo.m'), 'PackageName', 'k13a.k13b'}
    'upper',      {fullfile(src, 'k9echo.m'), 'PackageName', 'K13Upper'}
    'underscore', {fullfile(src, 'k9echo.m'), 'PackageName', '_k13u'}
    'classonly',  {fullfile(src, 'k9point.m'), 'PackageName', 'k13c'}
    'handleonly', {fullfile(src, 'k9counter.m'), 'PackageName', 'k13h'}
    'enumonly',   {fullfile(src, 'k9color.m'), 'PackageName', 'k13e'}
    'pkg',        {fullfile(src, '+k9pkg', 'k9inner.m'), 'PackageName', 'k13p'}
    'folder',     {{fullfile(src, '+k9pkg')}, 'PackageName', 'k13f'}
    'varargs',    {fn({'k9nargs', 'k9nout'}), 'PackageName', 'k13v'}
    'additional', {fullfile(src, 'k9echo.m'), 'PackageName', 'k13ad', 'AdditionalFiles', fullfile(src, 'k13data.txt')}
    'samples',    {fn({'k9echo', 'k9multi'}), 'PackageName', 'k13s', 'SampleGenerationFiles', fullfile(src, 'k13sample.m')}
    'script',     {fullfile(src, 'k13script.m'), 'PackageName', 'k13sc'}
    'keyword',    {fullfile(src, 'k9echo.m'), 'PackageName', 'class'}
    'builtinname', {fullfile(src, 'k9echo.m'), 'PackageName', 'json'}
    };
for k = 1:size(builds, 1)
    [key, args] = builds{k, :};
    d = fullfile(work, key);
    apiBuild(key, @() compiler.build.pythonPackage(args{:}, 'OutputDir', d), d);
    printInits(key, d);
end
printText('samples', fullfile(work, 'samples'));
printFile('defaults.GettingStarted', fullfile(work, 'defaults', 'GettingStarted.html'));
printFile('defaults.setup', fullfile(work, 'defaults', 'setup.py'));
printFile('dotted.setup', fullfile(work, 'dotted', 'setup.py'));

% A build left to its own output folder: .\<name>pythonPackage under the current folder.
t = tic;
try
    r = compiler.build.pythonPackage(fullfile(src, 'k9echo.m'));
    fprintf('nooutdir.Files\t%s\n', strjoin(strrep(r.Files(:)', [src filesep], '<SRC>\'), ' | '));
    listFiles('nooutdir', fullfile(src, 'k9echopythonPackage'));
catch ME
    fprintf('nooutdir\tERR %s | %s\n', ME.identifier, k0_flat(ME.message));
end
fprintf('nooutdir.seconds\t%.1f\n', toc(t));

% mcc -W python.
mccs = {
    'mcc_default',  {'-W', 'python:k13m', '-T', 'link:lib', 'k9echo.m', 'k9multi.m'}
    'mcc_notarget', {'-W', 'python:k13nt', 'k9echo.m'}
    'mcc_dotted',   {'-W', 'python:org.k13m', '-T', 'link:lib', 'k9echo.m'}
    'mcc_noname',   {'-W', 'python:', '-T', 'link:lib', 'k9echo.m'}
    'mcc_nocolon',  {'-W', 'python', '-T', 'link:lib', 'k9echo.m'}
    'mcc_version',  {'-W', 'python:k13vr,version=1.0', '-T', 'link:lib', 'k9echo.m'}
    'mcc_comma',    {'-W', 'python:k13cm,extra', '-T', 'link:lib', 'k9echo.m'}
    'mcc_separate', {'-W', 'python:k13sp', '-T', 'link:lib', '-C', 'k9echo.m'}
    'mcc_codegen',  {'-W', 'python:k13cg', '-T', 'codegen', 'k9echo.m'}
    'mcc_compile',  {'-W', 'python:k13cp', '-T', 'compile:lib', 'k9echo.m'}
    'mcc_c',        {'-W', 'python:k13cc', '-c', 'k9echo.m'}
    'mcc_verbose',  {'-W', 'python:k13vb', '-T', 'link:lib', '-v', 'k9echo.m'}
    'mcc_debug',    {'-W', 'python:k13db', '-T', 'link:lib', '-g', 'k9echo.m'}
    'mcc_o',        {'-W', 'python:k13on', '-T', 'link:lib', '-o', 'k13other', 'k9echo.m'}
    'mcc_ofirst',   {'-o', 'k13of', '-W', 'python:k13of2', '-T', 'link:lib', 'k9echo.m'}
    'mcc_exe',      {'-W', 'python:k13ex', '-T', 'link:exe', 'k9echo.m'}
    'mcc_twice',    {'-W', 'python:k13t1', '-W', 'python:k13t2', '-T', 'link:lib', 'k9echo.m'}
    'mcc_badname',  {'-W', 'python:1bad', '-T', 'link:lib', 'k9echo.m'}
    'mcc_dash',     {'-W', 'python:a-b', '-T', 'link:lib', 'k9echo.m'}
    'mcc_keyword',  {'-W', 'python:class', '-T', 'link:lib', 'k9echo.m'}
    'mcc_dotdot',   {'-W', 'python:a..b', '-T', 'link:lib', 'k9echo.m'}
    'mcc_classes',  {'-W', 'python:k13cl', '-T', 'link:lib', 'k9point.m', 'k9counter.m', 'k9color.m'}
    'mcc_script',   {'-W', 'python:k13sc', '-T', 'link:lib', 'k13script.m'}
    'mcc_data',     {'-W', 'python:k13da', '-T', 'link:lib', '-a', 'k13data.txt', 'k9echo.m'}
    'mcc_withlib',  {'-W', 'python:k13wl', '-W', 'lib:k13wl2', '-T', 'link:lib', 'k9echo.m'}
    };
for k = 1:size(mccs, 1)
    [key, args] = mccs{k, :};
    d = fullfile(work, key);
    mkdir(d);
    lastwarn('', '');
    t = tic;
    try
        txt = quietMcc([args, {'-d', d}]);
        fprintf('%s\tok\n', key);
        printLines(txt);
        listFiles(key, d);
        printInits(key, d);
    catch ME
        fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
        listFiles(key, d);
    end
    fprintf('%s.seconds\t%.1f\n', key, toc(t));
    k0_warned(key);
end
printFile('mcc_default.readme', fullfile(work, 'mcc_default', 'readme.txt'));
printFile('mcc_default.setup', fullfile(work, 'mcc_default', 'setup.py'));

% What the options take.
f1 = fullfile(src, 'k9echo.m');
o = compiler.build.PythonPackageOptions(f1);
show('opts.default', o);
k0_try('opts.properties', @() strjoin(properties(o)', ' '));
k0_try('opts.PackageName', @() o.PackageName);
k0_try('opts.OutputDir', @() strrep(o.OutputDir, [src filesep], '<SRC>\'));
names = {'k13ok', 'org.k13.ok', '1abc', 'a-b', 'a..b', 'a.', '.a', 'a.1b', 'class', 'import', '_k13', ...
    'K13', 'a b', '', repmat('a', 1, 70), 'a.b.c.d.e', 'pkg_1', ['a' char(955)]};
for k = 1:numel(names)
    k0_try(sprintf('opts.name.%d', k), @() [names{k} ' -> ' setAndRead(o, 'PackageName', names{k})]);
end
k0_try('opts.name.string', @() setAndRead(o, 'PackageName', "k13str"));
k0_try('opts.name.number', @() setAndRead(o, 'PackageName', 3));
k0_try('opts.name.cell', @() setAndRead(o, 'PackageName', {'k13'}));
o2 = compiler.build.PythonPackageOptions(f1, 'PackageName', 'org.k13.deep');
k0_try('opts.dotted.OutputDir', @() strrep(o2.OutputDir, [src filesep], '<SRC>\'));
o3 = compiler.build.PythonPackageOptions(f1);
o3.PackageName = 'k13after';
k0_try('opts.named.after.OutputDir', @() strrep(o3.OutputDir, [src filesep], '<SRC>\'));
k0_try('opts.samples.bad', @() setAndRead(o, 'SampleGenerationFiles', 'nosuch.m'));
k0_try('opts.samples.txt', @() setAndRead(o, 'SampleGenerationFiles', fullfile(src, 'k13data.txt')));
k0_try('opts.samples.ok', @() setAndRead(o, 'SampleGenerationFiles', fullfile(src, 'k13sample.m')));
k0_try('opts.files.bad', @() compiler.build.PythonPackageOptions('nosuch.m'));
k0_try('opts.files.txt', @() compiler.build.PythonPackageOptions(fullfile(here, 'README.md')));
k0_try('opts.files.script', @() strjoin(strrep(compiler.build.PythonPackageOptions(fullfile(src, 'k13script.m')).FunctionFiles(:)', [src filesep], '<SRC>\'), ' '));
k0_try('opts.files.two', @() strjoin(strrep(compiler.build.PythonPackageOptions(fn({'k9echo', 'k9multi'})).FunctionFiles(:)', [src filesep], '<SRC>\'), ' '));
k0_try('opts.nv.ambiguous', @() compiler.build.PythonPackageOptions(f1, 'Package', 'x'));
k0_try('opts.nv.prefix', @() compiler.build.PythonPackageOptions(f1, 'PackageN', 'k13pre').PackageName);
k0_try('opts.nv.case', @() compiler.build.PythonPackageOptions(f1, 'packagename', 'k13case').PackageName);
k0_try('opts.nv.bogus', @() compiler.build.PythonPackageOptions(f1, 'Bogus', 1));
k0_try('opts.nv.odd', @() compiler.build.PythonPackageOptions(f1, 'PackageName'));
k0_try('opts.nv.Interface', @() compiler.build.PythonPackageOptions(f1, 'Interface', 'x'));
k0_try('opts.nargin', @() compiler.build.PythonPackageOptions());
k0_try('build.nargin', @() compiler.build.pythonPackage());
k0_try('build.opts.and.nv', @() compiler.build.pythonPackage(o, 'Verbose', 'on'));
k0_try('build.bad.nv', @() compiler.build.pythonPackage(f1, 'Bogus', 1));
k0_try('build.nargout', @() nargout2(@() compiler.build.pythonPackage(f1, 'OutputDir', fullfile(work, 'nargout2'))));
end

function nargout2(f)
[a, b] = f(); %#ok<ASGLU>
end

function v = setAndRead(o, prop, value)
o.(prop) = value;
v = o.(prop);
if ~ischar(v), v = strtrim(evalc('disp(v)')); end
end

function apiBuild(key, f, d)
lastwarn('', '');
t = tic;
try
    txt = evalc('r = f();');
    if ~isempty(strtrim(txt))
        fprintf('%s.printed\n', key);
        printLines(txt);
    end
    show([key '.results'], r);
    fprintf('%s.BuildType\t%s\n', key, r.BuildType);
    files = cellfun(@(p) strrep(p, [d filesep], '<OUT>\'), r.Files, 'UniformOutput', false);
    fprintf('%s.Files\t%s\n', key, strjoin(files(:)', ' | '));
    show([key '.Options'], r.Options);
    show([key '.RuntimeDependencies'], r.RuntimeDependencies);
catch ME
    fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
end
listFiles(key, d);
fprintf('%s.seconds\t%.1f\n', key, toc(t));
k0_warned(key);
end

function printText(key, d)
% Every generated text file of a build but its archive.
if ~exist(d, 'dir'), return, end
listing = dir(fullfile(d, '**', '*'));
listing = listing(~[listing.isdir]);
for k = 1:numel(listing)
    [~, ~, ext] = fileparts(listing(k).name);
    if any(strcmpi(ext, {'.py', '.toml', '.txt', '.json', '.log', '.html', '.cfg', '.in'}))
        rel = strrep(fullfile(listing(k).folder, listing(k).name), [d filesep], '');
        printFile([key '.file.' rel], fullfile(listing(k).folder, listing(k).name));
    end
end
end

function printInits(key, d)
% A build's __init__.py files, as how each differs from the main build's says enough.
if ~exist(d, 'dir'), return, end
listing = dir(fullfile(d, '**', '__init__.py'));
for k = 1:numel(listing)
    rel = strrep(fullfile(listing(k).folder, listing(k).name), [d filesep], '');
    printFile([key '.init.' rel], fullfile(listing(k).folder, listing(k).name));
end
listing = dir(fullfile(d, '**', '*.toml'));
for k = 1:numel(listing)
    rel = strrep(fullfile(listing(k).folder, listing(k).name), [d filesep], '');
    printFile([key '.toml.' rel], fullfile(listing(k).folder, listing(k).name));
end
end

function listFiles(key, d)
if ~exist(d, 'dir')
    fprintf('%s.files\t(no folder)\n', key);
    return
end
listing = dir(fullfile(d, '**', '*'));
listing = listing(~[listing.isdir]);
names = {};
for k = 1:numel(listing)
    rel = strrep(fullfile(listing(k).folder, listing(k).name), [d filesep], '');
    names{end + 1} = sprintf('%s (%d)', rel, listing(k).bytes); %#ok<AGROW>
end
fprintf('%s.files\t%s\n', key, strjoin(sort(names), ' | '));
end

function printFile(key, file)
if ~exist(file, 'file')
    fprintf('%s\tmissing\n', key);
    return
end
bytes = fileread(file);
fprintf('%s\t%d chars, CRLF %d, ends with newline %d\n', key, numel(bytes), count(bytes, sprintf('\r\n')), endsWith(bytes, newline));
printLines(bytes);
end

function printLines(txt)
txt = strrep(txt, sprintf('\r\n'), newline);
txt = regexprep(txt, '<a [^>]*>|</a>', '');
lines = splitlines(txt);
if ~isempty(lines) && isempty(lines{end}), lines(end) = []; end
for j = 1:numel(lines)
    fprintf('  |%s\n', lines{j});
end
end

function show(key, v)
txt = evalc('disp(v)');
txt = regexprep(txt, '<a [^>]*>|</a>', '');
fprintf('%s\n', key);
lines = splitlines(txt);
for j = 1:numel(lines)
    if strlength(strtrim(lines{j})) > 0, fprintf('  %s\n', lines{j}); end
end
end

function txt = quietMcc(args)
% evalc in a named function: a lambda's workspace holds only what its body names.
txt = evalc('mcc(args{:})');
end
