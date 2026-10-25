function probe_k8_build(work)
%PROBE_K8_BUILD Builds stage K8's .NET assemblies (compiler and SDK plan) with
%compiler.build.dotNETAssembly and mcc -W dotnet, and prints what each build wrote and what
%compiler.build.DotNETAssemblyOptions answers and refuses. Run under matlab -batch -noFigureWindows;
%nothing built is run here (record-k8.ps1 builds and runs k8client against the build in dn\K8).
%One key<TAB>value line per question, a display or a file's text as indented lines under its key.
beep('off'); % the refusals below would each beep
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'src'));
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
src = fullfile(work, 'src');
mkdir(src);
copyfile(fullfile(here, 'src', 'k8', '*.m'), src);
old = cd(src);
cleanup = onCleanup(@() cd(old));
fn = @(names) cellfun(@(n) fullfile(src, [n '.m']), names, 'UniformOutput', false);
mainFiles = fn({'k8echo', 'k8make', 'k8err', 'k8vout', 'k8args', 'k8show', 'k8quiet'});

% The client's build: two classes from a map, for .NET 5.0 and later, verbosely.
cmap = containers.Map;
cmap('K8Class') = mainFiles;
cmap('K8Other') = fn({'k8other'});
d = fullfile(work, 'dn', 'K8');
o = compiler.build.DotNETAssemblyOptions(cmap, 'AssemblyName', 'K8', 'FrameworkVersion', '5.0', 'OutputDir', d, 'Verbose', 'on');
show('main.options', o);
apiBuild('main', @() compiler.build.dotNETAssembly(o), d);
printFile('main.readme', fullfile(d, 'readme.txt'));

% The defaults: one file, nothing named.
o = compiler.build.DotNETAssemblyOptions(fullfile(src, 'k8echo.m'));
show('default.options', o);
k0_try('default.AssemblyName', @() o.AssemblyName);
k0_try('default.OutputDir', @() o.OutputDir);
k0_try('default.ClassMap.keys', @() keys(o.ClassMap));
k0_try('default.getName', @() o.getName);
o = compiler.build.DotNETAssemblyOptions({fullfile(src, 'k8make.m'), fullfile(src, 'k8echo.m')}, 'ClassName', 'Named');
k0_try('named.ClassMap.keys', @() keys(o.ClassMap));
k0_try('named.ClassMap.values', @() o.ClassMap('Named'));
o = compiler.build.DotNETAssemblyOptions(fullfile(src, 'k8echo.m'), 'AssemblyName', 'Org.Group.K8n');
k0_try('dotted.getName', @() o.getName);
k0_try('dotted.OutputDir', @() o.OutputDir);

% Builds of other shapes, each into a folder of its own.
builds = {
    'framework4',   {fullfile(src, 'k8echo.m'), 'AssemblyName', 'K8f'}
    'separate',     {fullfile(src, 'k8echo.m'), 'AssemblyName', 'K8s', 'FrameworkVersion', '5.0', 'EmbedArchive', 'off'}
    'dotted',       {fullfile(src, 'k8echo.m'), 'AssemblyName', 'Org.Group.K8n', 'FrameworkVersion', '5.0'}
    'debug',        {fullfile(src, 'k8echo.m'), 'AssemblyName', 'K8d', 'FrameworkVersion', '5.0', 'DebugBuild', 'on'}
    'remoting',     {fullfile(src, 'k8echo.m'), 'AssemblyName', 'K8r', 'FrameworkVersion', '5.0', 'EnableRemoting', 'on'}
    'version',      {fullfile(src, 'k8echo.m'), 'AssemblyName', 'K8v', 'FrameworkVersion', '5.0', 'AssemblyVersion', '2.3.4'}
    'badversion',   {fullfile(src, 'k8echo.m'), 'AssemblyName', 'K8b', 'FrameworkVersion', '5.0', 'AssemblyVersion', '1.x'}
    'framework6',   {fullfile(src, 'k8echo.m'), 'AssemblyName', 'K8x', 'FrameworkVersion', '6.0'}
    };
for k = 1:size(builds, 1)
    [key, args] = builds{k, :};
    d = fullfile(work, key);
    apiBuild(key, @() compiler.build.dotNETAssembly(args{:}, 'OutputDir', d, 'Verbose', 'on'), d);
end

% mcc -W dotnet.
mccs = {
    'mcc_plain',     {'-W', 'dotnet:K8m,K8mClass,5.0,Private,local', '-T', 'link:lib', 'k8echo.m', 'k8make.m'}
    'mcc_api',       {'-W', 'dotnet:K8a,api=mwarray,K8aClass,5.0,Private,local', '-T', 'link:lib', 'k8echo.m'}
    'mcc_noclass',   {'-W', 'dotnet:K8n', '-T', 'link:lib', 'k8echo.m'}
    'mcc_nameonly',  {'-W', 'dotnet:K8o,,5.0', '-T', 'link:lib', 'k8echo.m'}
    'mcc_classes',   {'-W', 'dotnet:K8c,First,5.0,Private,local', '-T', 'link:lib', ['class{Second:' fullfile(src, 'k8other.m') '}'], 'k8echo.m'}
    'mcc_classonly', {'-W', 'dotnet:K8k,First,5.0,Private,local', '-T', 'link:lib', ['class{Second:' fullfile(src, 'k8other.m') '}']}
    'mcc_separate',  {'-W', 'dotnet:K8p,K8pClass,5.0,Private,local', '-T', 'link:lib', '-C', 'k8echo.m'}
    'mcc_notarget',  {'-W', 'dotnet:K8t,K8tClass,5.0,Private,local', 'k8echo.m'}
    'mcc_exe',       {'-W', 'dotnet:K8e,K8eClass,5.0,Private,local', '-T', 'link:exe', 'k8echo.m'}
    'mcc_fw40',      {'-W', 'dotnet:K8q,K8qClass,4.0,Private,local', '-T', 'link:lib', 'k8echo.m'}
    'mcc_badkey',    {'-W', 'dotnet:K8y,K8yClass,5.0,nosuch.snk,local', '-T', 'link:lib', 'k8echo.m'}
    'mcc_remote',    {'-W', 'dotnet:K8z,K8zClass,5.0,Private,remote', '-T', 'link:lib', 'k8echo.m'}
    'mcc_badlocal',  {'-W', 'dotnet:K8w,K8wClass,5.0,Private,elsewhere', '-T', 'link:lib', 'k8echo.m'}
    'mcc_mda',       {'-W', 'dotnet:K8g,api=matlab-data,K8gClass,5.0,Private,local', '-T', 'link:lib', 'k8echo.m'}
    };
for k = 1:size(mccs, 1)
    [key, args] = mccs{k, :};
    d = fullfile(work, key);
    mkdir(d);
    lastwarn('', '');
    try
        txt = quietMcc([args, {'-d', d}]);
        fprintf('%s\tok (%s)\n', key, k0_flat(strtrim(txt)));
        listFiles(key, d);
    catch ME
        fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
    end
    k0_warned(key);
end

% The options' refusals and conversions.
f1 = fullfile(src, 'k8echo.m');
k0_try('opts.none', @() compiler.build.DotNETAssemblyOptions());
k0_try('opts.missing', @() compiler.build.DotNETAssemblyOptions('k8nosuch.m'));
k0_try('opts.txt', @() compiler.build.DotNETAssemblyOptions(fullfile(here, 'README.md')));
k0_try('opts.number', @() compiler.build.DotNETAssemblyOptions(5));
k0_try('opts.map.classname', @() compiler.build.DotNETAssemblyOptions(cmap, 'ClassName', 'X'));
k0_try('opts.classname.empty', @() compiler.build.DotNETAssemblyOptions(f1, 'ClassName', ''));
k0_try('opts.classmap.nv', @() compiler.build.DotNETAssemblyOptions(f1, 'ClassMap', cmap));
k0_try('opts.unknown.nv', @() compiler.build.DotNETAssemblyOptions(f1, 'Nonsense', 1));
k0_try('opts.partial.nv', @() compiler.build.DotNETAssemblyOptions(f1, 'assemblyn', 'P').AssemblyName);
k0_try('opts.odd.nv', @() compiler.build.DotNETAssemblyOptions(f1, 'AssemblyName'));
k0_try('opts.map.empty', @() compiler.build.DotNETAssemblyOptions(containers.Map));
k0_try('opts.map.doublekeys', @() compiler.build.DotNETAssemblyOptions(containers.Map('KeyType', 'double', 'ValueType', 'any')));
m = containers.Map; m('A B') = {f1};
k0_try('opts.map.spacedkey', @() compiler.build.DotNETAssemblyOptions(m));
m = containers.Map; m('A') = {'k8nosuch.m'};
k0_try('opts.map.missingfile', @() compiler.build.DotNETAssemblyOptions(m));
m = containers.Map; m('A') = f1;
k0_try('opts.map.charvalue', @() keys(compiler.build.DotNETAssemblyOptions(m).ClassMap));
o = compiler.build.DotNETAssemblyOptions(f1);
setCases = {
    'FrameworkVersion', ''; 'FrameworkVersion', 4; 'FrameworkVersion', '6.0'; 'FrameworkVersion', "5.0";
    'AssemblyName', 'a b'; 'AssemblyName', 'a/b'; 'AssemblyName', ''; 'AssemblyName', 'a:b'; 'AssemblyName', 'x.y.z'; 'AssemblyName', 5;
    'Interface', 'MWARRAY'; 'Interface', 'bad'; 'Interface', ''; 'Interface', 'matlab-data';
    'AssemblyVersion', '1.2'; 'AssemblyVersion', ''; 'AssemblyVersion', 5; 'AssemblyVersion', "3";
    'EmbedArchive', 'off'; 'EmbedArchive', 'maybe'; 'EmbedArchive', 0;
    'StrongNameKeyFile', 'missing.snk'; 'StrongNameKeyFile', ''; 'StrongNameKeyFile', "";
    'EnableRemoting', 'on'; 'EnableRemoting', 2; 'DebugBuild', 1; 'DebugBuild', 'yes';
    'SampleGenerationFiles', 'k8nosuch.m'; 'SampleGenerationFiles', {};
    'ClassMap', 5; 'OutputDir', 'out<1>'; 'Verbose', 'on'};
for k = 1:size(setCases, 1)
    [prop, value] = setCases{k, :};
    key = sprintf('set.%s.%s', prop, k0_show(value));
    k0_try(key, @() setAndRead(o, prop, value));
end
o = compiler.build.DotNETAssemblyOptions(f1, 'Interface', 'matlab-data');
k0_try('mda.EmbedArchive', @() o.EmbedArchive);
k0_try('mda.AssemblyVersion', @() o.AssemblyVersion);
k0_try('mda.classname', @() keys(compiler.build.DotNETAssemblyOptions(f1, 'Interface', 'matlab-data', 'ClassName', 'C').ClassMap));
k0_try('mda.version', @() compiler.build.DotNETAssemblyOptions(f1, 'Interface', 'matlab-data', 'AssemblyVersion', '1.0').AssemblyVersion);
k0_try('api.wrong.options', @() compiler.build.dotNETAssembly(compiler.build.StandaloneApplicationOptions(f1)));
k0_try('api.no.args', @() compiler.build.dotNETAssembly());
k0_try('api.nargout2', @() twoOut(f1));
end

function v = setAndRead(o, prop, value)
o.(prop) = value;
v = o.(prop);
end

function twoOut(f1)
[a, b] = compiler.build.dotNETAssembly(f1); %#ok<ASGLU>
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
    listFiles(key, d);
catch ME
    fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
end
fprintf('%s.seconds\t%.1f\n', key, toc(t));
k0_warned(key);
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
