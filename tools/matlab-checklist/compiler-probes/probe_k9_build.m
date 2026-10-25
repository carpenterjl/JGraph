function probe_k9_build(work)
%PROBE_K9_BUILD Builds stage K9's .NET assemblies for the MATLAB Data API (compiler and SDK plan)
%with compiler.build.dotNETAssembly and mcc -W dotnet:...,api=matlab-data, and prints what each
%build wrote. Run under matlab -batch -noFigureWindows; nothing built is run here (record-k9.ps1
%builds and runs k9client against the build in dn\K9). One key<TAB>value line per question, a
%display or a file's text as indented lines under its key.
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
    'k9t_opt', 'k9t_ref', 'k9t_keyword', 'k9t_multi', 'k9t_none', 'k9t_objects', 'k9point', 'k9counter', 'k9color'};
mainFiles = [fn(mainNames), {fullfile(src, '+k9pkg', 'k9inner.m')}];

% The client's build: every function but the variadic two, the classes and the package function.
d = fullfile(work, 'dn', 'K9');
o = compiler.build.DotNETAssemblyOptions(mainFiles, 'Interface', 'matlab-data', 'AssemblyName', 'K9', ...
    'EmbedArchive', 'off', 'OutputDir', d, 'Verbose', 'on');
show('main.options', o);
apiBuild('main', @() compiler.build.dotNETAssembly(o), d);
printFile('main.readme', fullfile(d, 'readme.txt'));
printFile('main.csproj', fullfile(d, 'K9.csproj'));
printFile('main.deps', fullfile(d, 'K9.deps.json'));

% A second component for the client: functions with varargin or varargout, for which R2025b
% generates no strongly typed interface at all (the build is the archive alone).
d = fullfile(work, 'dn', 'K9V');
apiBuild('variadic', @() compiler.build.dotNETAssembly(fn({'k9nargs', 'k9nout', 'k9describe'}), 'Interface', 'matlab-data', ...
    'AssemblyName', 'K9V', 'EmbedArchive', 'off', 'OutputDir', d), d);

% Builds of other shapes, each into a folder of its own.
builds = {
    'defaults',    {fullfile(src, 'k9echo.m'), 'Interface', 'matlab-data'}
    'varargin',    {fullfile(src, 'k9nargs.m'), 'Interface', 'matlab-data', 'AssemblyName', 'K9va'}
    'varargout',   {fullfile(src, 'k9nout.m'), 'Interface', 'matlab-data', 'AssemblyName', 'K9vo'}
    'mixed',       {fn({'k9echo', 'k9nargs', 'k9nout'}), 'Interface', 'matlab-data', 'AssemblyName', 'K9mx'}
    'dotted',      {fullfile(src, 'k9t_real.m'), 'Interface', 'matlab-data', 'AssemblyName', 'Org.Group.K9d'}
    'classonly',   {fullfile(src, 'k9point.m'), 'Interface', 'matlab-data', 'AssemblyName', 'K9c'}
    'enumonly',    {fullfile(src, 'k9color.m'), 'Interface', 'matlab-data', 'AssemblyName', 'K9e'}
    'pkg',         {fullfile(src, '+k9pkg', 'k9inner.m'), 'Interface', 'matlab-data', 'AssemblyName', 'K9p'}
    'version',     {fullfile(src, 'k9echo.m'), 'Interface', 'matlab-data', 'AssemblyName', 'K9v', 'AssemblyVersion', '2.3.4'}
    'embed',       {fullfile(src, 'k9echo.m'), 'Interface', 'matlab-data', 'AssemblyName', 'K9b', 'EmbedArchive', 'on'}
    'framework',   {fullfile(src, 'k9echo.m'), 'Interface', 'matlab-data', 'AssemblyName', 'K9f', 'FrameworkVersion', '5.0'}
    'debug',       {fullfile(src, 'k9echo.m'), 'Interface', 'matlab-data', 'AssemblyName', 'K9g', 'DebugBuild', 'on'}
    'untyped',     {fullfile(src, 'k9t_untyped.m'), 'Interface', 'matlab-data', 'AssemblyName', 'K9u'}
    'optmethod',   {fullfile(src, 'k9opt.m'), 'Interface', 'matlab-data', 'AssemblyName', 'K9o'}
    };
for k = 1:size(builds, 1)
    [key, args] = builds{k, :};
    d = fullfile(work, key);
    apiBuild(key, @() compiler.build.dotNETAssembly(args{:}, 'OutputDir', d), d);
    printTyped(key, d);
end

% mcc -W dotnet:...,api=matlab-data.
mccs = {
    'mcc_min',      {'-W', 'dotnet:K9m,api=matlab-data', '-T', 'link:lib', 'k9echo.m', 'k9t_opt.m'}
    'mcc_full',     {'-W', 'dotnet:K9n,api=matlab-data,K9nClass,5.0,Private,local', '-T', 'link:lib', 'k9echo.m'}
    'mcc_class',    {'-W', 'dotnet:K9k,api=matlab-data', '-T', 'link:lib', 'k9point.m', 'k9t_objects.m', 'k9color.m'}
    'mcc_separate', {'-W', 'dotnet:K9s,api=matlab-data', '-T', 'link:lib', '-C', 'k9echo.m'}
    'mcc_notarget', {'-W', 'dotnet:K9t,api=matlab-data', 'k9echo.m'}
    'mcc_varargin', {'-W', 'dotnet:K9w,api=matlab-data', '-T', 'link:lib', 'k9nargs.m'}
    'mcc_classes',  {'-W', 'dotnet:K9x,api=matlab-data', '-T', 'link:lib', ['class{Second:' fullfile(src, 'k9t_real.m') '}'], 'k9echo.m'}
    'mcc_verbose',  {'-W', 'dotnet:K9y,api=matlab-data', '-T', 'link:lib', '-v', 'k9echo.m'}
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
    end
    fprintf('%s.seconds\t%.1f\n', key, toc(t));
    k0_warned(key);
end

% The order meta.class lists a class's members in, which the generator writes them in.
for c = {'k9point', 'k9counter', 'k9opt', 'k9color'}
    try
        mc = meta.class.fromName(c{1});
        fprintf('meta.%s.methods\t%s\n', c{1}, strjoin({mc.MethodList.Name}, ' '));
        fprintf('meta.%s.defining\t%s\n', c{1}, strjoin(arrayfun(@(m) m.DefiningClass.Name, mc.MethodList', 'UniformOutput', false), ' '));
        fprintf('meta.%s.properties\t%s\n', c{1}, strjoin({mc.PropertyList.Name}, ' '));
    catch ME
        fprintf('meta.%s\tERR %s | %s\n', c{1}, ME.identifier, k0_flat(ME.message));
    end
end

% What the options take with the Data API.
f1 = fullfile(src, 'k9echo.m');
o = compiler.build.DotNETAssemblyOptions(f1, 'Interface', 'matlab-data');
show('mda.options', o);
k0_try('mda.ClassMap.keys', @() keys(o.ClassMap));
k0_try('mda.ClassName', @() compiler.build.DotNETAssemblyOptions(f1, 'Interface', 'matlab-data', 'ClassName', 'X').AssemblyName);
k0_try('mda.then.mwarray', @() setAndRead(o, 'Interface', 'mwarray'));
k0_try('mda.then.embed', @() setAndRead(o, 'EmbedArchive', 'on'));
k0_try('mda.then.framework', @() setAndRead(o, 'FrameworkVersion', '5.0'));
k0_try('mda.then.remoting', @() setAndRead(o, 'EnableRemoting', 'on'));
k0_try('mda.then.key', @() setAndRead(o, 'StrongNameKeyFile', fullfile(here, 'README.md')));
end

function v = setAndRead(o, prop, value)
o.(prop) = value;
v = o.(prop);
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
    listFiles(key, d);
catch ME
    fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
end
fprintf('%s.seconds\t%.1f\n', key, toc(t));
k0_warned(key);
end

function printTyped(key, d)
listing = dir(fullfile(d, 'strongly_typed_interface', '*.cs'));
for k = 1:numel(listing)
    printFile([key '.typed.' listing(k).name], fullfile(listing(k).folder, listing(k).name));
end
end

function listFiles(key, d)
listing = dir(fullfile(d, '**', '*'));
listing = listing(~[listing.isdir]);
names = {};
for k = 1:numel(listing)
    rel = strrep(fullfile(listing(k).folder, listing(k).name), [d filesep], '');
    names{end + 1} = rel; %#ok<AGROW>
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
