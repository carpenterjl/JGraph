function probe_k12_build(work)
%PROBE_K12_BUILD Builds stage K12's C++ shared libraries for the MATLAB Data API (compiler and SDK
%plan) with compiler.build.cppSharedLibrary and mcc -W cpplib, and prints what each build wrote,
%the strongly typed header among it. Run under matlab -batch -noFigureWindows over stage K9's
%functions (src\k9); nothing built is run here (record-k12.ps1 compiles and runs src\k12client
%against the builds in cpp\k12lib and cpp\k12v). One key<TAB>value line per question, a display or
%a file's text as indented lines under its key.
beep('off'); % the refusals below would each beep
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'src'));
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
src = fullfile(work, 'src');
mkdir(src);
copyfile(fullfile(here, 'src', 'k9', '*'), src);
old = cd(src);
cleanup = onCleanup(@() cd(old));
fn = @(names) cellfun(@(n) fullfile(src, [n '.m']), names, 'UniformOutput', false);
mainNames = {'k9describe', 'k9make', 'k9echo', 'k9multi', 'k9nv', 'k9print', ...
    'k9noout', 'k9slow', 'k9err', 'k9quiet', 'k9t_real', 'k9t_cplx', 'k9t_ints', 'k9t_misc', 'k9t_untyped', ...
    'k9t_opt', 'k9t_ref', 'k9t_keyword', 'k9t_multi', 'k9t_none', 'k9t_objects', 'k9point', 'k9counter', 'k9color', 'k9opt'};
mainFiles = [fn(mainNames), {fullfile(src, '+k9pkg', 'k9inner.m')}];

% The client's library: every function but the variadic two, the classes and the package function.
d = fullfile(work, 'cpp', 'k12lib');
o = compiler.build.CppSharedLibraryOptions(mainFiles, 'LibraryName', 'k12lib', 'OutputDir', d, 'Verbose', 'on');
show('main.options', o);
apiBuild('main', @() compiler.build.cppSharedLibrary(o), d);
printFile('main.readme', fullfile(d, 'readme.txt'));
printFile('main.generic.readme', fullfile(d, 'v2', 'generic_interface', 'readme.txt'));
printTyped('main', d);

% The client's second library: functions with varargin or varargout.
d = fullfile(work, 'cpp', 'k12v');
apiBuild('variadic', @() compiler.build.cppSharedLibrary(fn({'k9nargs', 'k9nout', 'k9describe'}), ...
    'LibraryName', 'k12v', 'OutputDir', d), d);
printTyped('variadic', d);

% Builds of other shapes, each into a folder of its own.
builds = {
    'defaults',    {fullfile(src, 'k9echo.m')}
    'varargin',    {fullfile(src, 'k9nargs.m'), 'LibraryName', 'k12va'}
    'varargout',   {fullfile(src, 'k9nout.m'), 'LibraryName', 'k12vo'}
    'mixed',       {fn({'k9echo', 'k9nargs', 'k9nout'}), 'LibraryName', 'k12mx'}
    'classonly',   {fullfile(src, 'k9point.m'), 'LibraryName', 'k12c'}
    'handleonly',  {fullfile(src, 'k9counter.m'), 'LibraryName', 'k12h'}
    'enumonly',    {fullfile(src, 'k9color.m'), 'LibraryName', 'k12e'}
    'pkg',         {fullfile(src, '+k9pkg', 'k9inner.m'), 'LibraryName', 'k12p'}
    'untyped',     {fullfile(src, 'k9t_untyped.m'), 'LibraryName', 'k12u'}
    'optmethod',   {fullfile(src, 'k9opt.m'), 'LibraryName', 'k12o'}
    'objects',     {fn({'k9t_objects', 'k9point', 'k9color'}), 'LibraryName', 'k12ob'}
    'version',     {fullfile(src, 'k9echo.m'), 'LibraryName', 'k12ver', 'LibraryVersion', '2.3.4'}
    'debug',       {fullfile(src, 'k9echo.m'), 'LibraryName', 'k12g', 'DebugBuild', 'on'}
    'explicit',    {fullfile(src, 'k9echo.m'), 'LibraryName', 'k12x', 'Interface', 'matlab-data'}
    'folder',      {{fullfile(src, '+k9pkg')}, 'LibraryName', 'k12f'}
    'mwarray',     {fullfile(src, 'k9echo.m'), 'LibraryName', 'k12mw', 'Interface', 'mwarray'}
    'mwarrayver',  {fullfile(src, 'k9echo.m'), 'LibraryName', 'k12mwv', 'Interface', 'mwarray', 'LibraryVersion', '3.1'}
    'mwfolder',    {{fullfile(src, '+k9pkg')}, 'LibraryName', 'k12mf', 'Interface', 'mwarray'}
    };
for k = 1:size(builds, 1)
    [key, args] = builds{k, :};
    d = fullfile(work, key);
    apiBuild(key, @() compiler.build.cppSharedLibrary(args{:}, 'OutputDir', d), d);
    printTyped(key, d);
end
printFile('defaults.GettingStarted', fullfile(work, 'defaults', 'GettingStarted.html'));
printFile('mwarray.readme', fullfile(work, 'mwarray', 'readme.txt'));

% A build left to its own output folder: .\<name>cppSharedLibrary under the current folder.
t = tic;
try
    r = compiler.build.cppSharedLibrary(fullfile(src, 'k9echo.m'));
    fprintf('nooutdir.Files\t%s\n', strjoin(strrep(r.Files(:)', [src filesep], '<SRC>\'), ' | '));
    listFiles('nooutdir', fullfile(src, 'k9echocppSharedLibrary'));
catch ME
    fprintf('nooutdir\tERR %s | %s\n', ME.identifier, k0_flat(ME.message));
end
fprintf('nooutdir.seconds\t%.1f\n', toc(t));

% mcc -W cpplib.
mccs = {
    'mcc_default',  {'-W', 'cpplib:k12m', '-T', 'link:lib', 'k9echo.m', 'k9t_opt.m'}
    'mcc_generic',  {'-W', 'cpplib:k12gn,generic', '-T', 'link:lib', 'k9echo.m', 'k9t_opt.m'}
    'mcc_all',      {'-W', 'cpplib:k12al,all', '-T', 'link:lib', 'k9echo.m'}
    'mcc_legacy',   {'-W', 'cpplib:k12lg,legacy', '-T', 'link:lib', 'k9echo.m'}
    'mcc_notarget', {'-W', 'cpplib:k12nt,generic', 'k9echo.m'}
    'mcc_version',  {'-W', 'cpplib:k12vr,generic,version=2.0', '-T', 'link:lib', 'k9echo.m'}
    'mcc_verfirst', {'-W', 'cpplib:k12vf,version=2.0', '-T', 'link:lib', 'k9echo.m'}
    'mcc_separate', {'-W', 'cpplib:k12sp,generic', '-T', 'link:lib', '-C', 'k9echo.m'}
    'mcc_codegen',  {'-W', 'cpplib:k12cg,generic', '-T', 'codegen', 'k9echo.m'}
    'mcc_compile',  {'-W', 'cpplib:k12cp,generic', '-T', 'compile:lib', 'k9echo.m'}
    'mcc_c',        {'-W', 'cpplib:k12cc,generic', '-c', 'k9echo.m'}
    'mcc_verbose',  {'-W', 'cpplib:k12vb,generic', '-T', 'link:lib', '-v', 'k9echo.m'}
    'mcc_debug',    {'-W', 'cpplib:k12db,generic', '-T', 'link:lib', '-g', 'k9echo.m'}
    'mcc_o',        {'-W', 'cpplib:k12on,generic', '-T', 'link:lib', '-o', 'k12other', 'k9echo.m'}
    'mcc_exe',      {'-W', 'cpplib:k12ex,generic', '-T', 'link:exe', 'k9echo.m'}
    'mcc_classes',  {'-W', 'cpplib:k12cl,generic', '-T', 'link:lib', 'k9point.m', 'k9t_objects.m', 'k9color.m'}
    'mcc_varargin', {'-W', 'cpplib:k12vg,generic', '-T', 'link:lib', 'k9nargs.m'}
    'mcc_twice',    {'-W', 'cpplib:k12t1,generic', '-W', 'cpplib:k12t2,generic', '-T', 'link:lib', 'k9echo.m'}
    'mcc_badname',  {'-W', 'cpplib:1bad,generic', '-T', 'link:lib', 'k9echo.m'}
    'mcc_dash',     {'-W', 'cpplib:a-b,generic', '-T', 'link:lib', 'k9echo.m'}
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
        printTyped(key, d);
    catch ME
        fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
        listFiles(key, d);
    end
    fprintf('%s.seconds\t%.1f\n', key, toc(t));
    k0_warned(key);
end
printFile('mcc_default.readme', fullfile(work, 'mcc_default', 'readme.txt'));

% What the options take.
f1 = fullfile(src, 'k9echo.m');
o = compiler.build.CppSharedLibraryOptions(f1);
show('opts.default', o);
k0_try('opts.properties', @() strjoin(properties(o)', ' '));
k0_try('opts.Interface', @() o.Interface);
k0_try('opts.LibraryName', @() o.LibraryName);
k0_try('opts.OutputDir', @() strrep(o.OutputDir, [src filesep], '<SRC>\'));
k0_try('opts.mwarray', @() setAndRead(o, 'Interface', 'mwarray'));
k0_try('opts.MWARRAY', @() setAndRead(o, 'Interface', 'MWARRAY'));
k0_try('opts.Matlab-Data', @() setAndRead(o, 'Interface', 'Matlab-Data'));
k0_try('opts.bogus', @() setAndRead(o, 'Interface', 'bogus'));
k0_try('opts.interface.number', @() setAndRead(o, 'Interface', 3));
k0_try('opts.version', @() setAndRead(o, 'LibraryVersion', '1.2'));
k0_try('opts.version.bad', @() setAndRead(o, 'LibraryVersion', 'x.y'));
k0_try('opts.version.long', @() setAndRead(o, 'LibraryVersion', '1.2.3.4.5'));
k0_try('opts.name.digit', @() setAndRead(o, 'LibraryName', '1abc'));
k0_try('opts.name.dash', @() setAndRead(o, 'LibraryName', 'a-b'));
k0_try('opts.name.dotted', @() setAndRead(o, 'LibraryName', 'Org.Group'));
k0_try('opts.name.long', @() setAndRead(o, 'LibraryName', repmat('a', 1, 70)));
k0_try('opts.debug', @() setAndRead(o, 'DebugBuild', 'on'));
k0_try('opts.samples.bad', @() setAndRead(o, 'SampleGenerationFiles', 'nosuch.m'));
k0_try('opts.files.bad', @() compiler.build.CppSharedLibraryOptions('nosuch.m'));
k0_try('opts.files.txt', @() compiler.build.CppSharedLibraryOptions(fullfile(here, 'README.md')));
k0_try('opts.nv.Interface', @() compiler.build.CppSharedLibraryOptions(f1, 'Interface', 'mwarray').Interface);
k0_try('opts.nv.ambiguous', @() compiler.build.CppSharedLibraryOptions(f1, 'Library', 'x'));
k0_try('opts.nv.EmbedArchive', @() compiler.build.CppSharedLibraryOptions(f1, 'EmbedArchive', 'on'));
k0_try('opts.folder.mwarray', @() compiler.build.CppSharedLibraryOptions({fullfile(src, '+k9pkg')}, 'Interface', 'mwarray').Interface);
o2 = compiler.build.CppSharedLibraryOptions({fullfile(src, '+k9pkg')});
k0_try('opts.folder.then.mwarray', @() setAndRead(o2, 'Interface', 'mwarray'));
k0_try('build.nargin', @() compiler.build.cppSharedLibrary());
k0_try('build.opts.and.nv', @() compiler.build.cppSharedLibrary(o, 'Verbose', 'on'));
end

function v = setAndRead(o, prop, value)
o.(prop) = value;
v = o.(prop);
if ~ischar(v), v = evalc('disp(v)'); end
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
catch ME
    fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
end
listFiles(key, d);
fprintf('%s.seconds\t%.1f\n', key, toc(t));
k0_warned(key);
end

function printTyped(key, d)
listing = dir(fullfile(d, '**', '*.hpp'));
for k = 1:numel(listing)
    rel = strrep(fullfile(listing(k).folder, listing(k).name), [d filesep], '');
    printFile([key '.hpp.' rel], fullfile(listing(k).folder, listing(k).name));
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
