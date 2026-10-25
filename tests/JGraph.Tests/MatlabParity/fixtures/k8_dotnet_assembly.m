% record: -noFigureWindows
% Compiler and SDK plan, stage K8: .NET assemblies for the MWArray API - compiler.build.DotNETAssemblyOptions,
% its defaults, derived names and refusals; compiler.build.dotNETAssembly and mcc -W dotnet, the files
% a build writes and the switches it refuses; and, met on the way, isreal of what is not a number and
% dir with a ** folder. The assemblies are built and never loaded here.
beep('off');
root = tempname;
src = fullfile(root, 'src');
mkdir(src);
put(src, 'k8f.m', {'function [a, b] = k8f(x, varargin)', '%K8F A probe function.', 'a = x; b = nargin;', 'end'});
put(src, 'k8g.m', {'function k8g()', '%K8G Nothing out.', 'end'});
put(src, 'k8notes.txt', {'notes'});
old = cd(src);

% The options from one file: the defaults and the names derived from it.
o = compiler.build.DotNETAssemblyOptions('k8f.m');
u2_chk('class', @() class(o));
u2_chk('props', @() strjoin(properties(o)', ' '));
u2_chk('assembly', @() o.AssemblyName);
u2_chk('outdir', @() o.OutputDir);
u2_chk('version', @() o.AssemblyVersion);
u2_chk('framework', @() o.FrameworkVersion);
u2_chk('interface', @() o.Interface);
u2_chk('embed', @() char(o.EmbedArchive));
u2_chk('debug', @() char(o.DebugBuild));
u2_chk('remoting', @() char(o.EnableRemoting));
u2_chk('key', @() o.StrongNameKeyFile);
u2_chk('samples', @() size(o.SampleGenerationFiles));
u2_chk('packages', @() strjoin(o.SupportPackages, ' '));
u2_chk('map_class', @() class(o.ClassMap));
u2_chk('map_keys', @() strjoin(keys(o.ClassMap), ' '));
u2_chk('map_files', @() rel(o.ClassMap('k8fClass'), src));
u2_chk('name', @() o.getName);

% A class named, two files, a dotted name, and a map of two classes.
o = compiler.build.DotNETAssemblyOptions({'k8g.m', 'k8f.m'}, 'ClassName', 'Named');
u2_chk('named_keys', @() strjoin(keys(o.ClassMap), ' '));
u2_chk('named_files', @() rel(o.ClassMap('Named'), src));
u2_chk('named_assembly', @() o.AssemblyName);
o = compiler.build.DotNETAssemblyOptions('k8f.m', 'AssemblyName', 'Org.Group.K8n');
u2_chk('dotted_name', @() o.getName);
u2_chk('dotted_outdir', @() o.OutputDir);
u2_chk('partial_name', @() compiler.build.DotNETAssemblyOptions('k8f.m', 'assemblyn', 'P').AssemblyName);
m = containers.Map;
m('Zeta') = {'k8g.m'};
m('Alpha') = 'k8f.m';
o = compiler.build.DotNETAssemblyOptions(m);
u2_chk('map2_keys', @() strjoin(keys(o.ClassMap), ' '));
u2_chk('map2_assembly', @() o.AssemblyName);
u2_chk('map2_alpha', @() rel(o.ClassMap('Alpha'), src));

% The constructor's refusals.
u2_chk('none', @() err(@() compiler.build.DotNETAssemblyOptions()));
u2_chk('missing', @() err(@() compiler.build.DotNETAssemblyOptions('k8nosuch.m')));
u2_chk('not_code', @() err(@() compiler.build.DotNETAssemblyOptions('k8notes.txt')));
u2_chk('number', @() err(@() compiler.build.DotNETAssemblyOptions(5)));
u2_chk('map_classname', @() err(@() compiler.build.DotNETAssemblyOptions(m, 'ClassName', 'X')));
u2_chk('classname_empty', @() err(@() compiler.build.DotNETAssemblyOptions('k8f.m', 'ClassName', '')));
u2_chk('classmap_nv', @() err(@() compiler.build.DotNETAssemblyOptions('k8f.m', 'ClassMap', m)));
u2_chk('unknown_nv', @() err(@() compiler.build.DotNETAssemblyOptions('k8f.m', 'Nonsense', 1)));
u2_chk('odd_nv', @() err(@() compiler.build.DotNETAssemblyOptions('k8f.m', 'AssemblyName')));
u2_chk('map_empty', @() err(@() compiler.build.DotNETAssemblyOptions(containers.Map)));
spaced = containers.Map;
spaced('A B') = {'k8f.m'};
u2_chk('map_spaced', @() err(@() compiler.build.DotNETAssemblyOptions(spaced)));
gone = containers.Map;
gone('A') = {'k8nosuch.m'};
u2_chk('map_missing', @() err(@() compiler.build.DotNETAssemblyOptions(gone)));

% Each property's checks.
o = compiler.build.DotNETAssemblyOptions('k8f.m');
u2_chk('set_framework_empty', @() err(@() setp(o, 'FrameworkVersion', '')));
u2_chk('set_framework_number', @() err(@() setp(o, 'FrameworkVersion', 4)));
u2_chk('set_framework_6', @() setp(o, 'FrameworkVersion', '6.0'));
u2_chk('set_framework_string', @() class(setp(o, 'FrameworkVersion', "5.0")));
u2_chk('set_assembly_spaced', @() setp(o, 'AssemblyName', 'a b'));
u2_chk('set_assembly_slash', @() err(@() setp(o, 'AssemblyName', 'a/b')));
u2_chk('set_assembly_colon', @() err(@() setp(o, 'AssemblyName', 'a:b')));
u2_chk('set_assembly_empty', @() err(@() setp(o, 'AssemblyName', '')));
u2_chk('set_assembly_number', @() err(@() setp(o, 'AssemblyName', 5)));
u2_chk('set_interface_upper', @() setp(o, 'Interface', 'MWARRAY'));
u2_chk('set_interface_bad', @() err(@() setp(o, 'Interface', 'bad')));
u2_chk('set_version', @() setp(o, 'AssemblyVersion', '1.2'));
u2_chk('set_version_empty', @() size(setp(o, 'AssemblyVersion', '')));
u2_chk('set_version_number', @() err(@() setp(o, 'AssemblyVersion', 5)));
u2_chk('set_embed_off', @() char(setp(o, 'EmbedArchive', 'off')));
u2_chk('set_embed_bad', @() err(@() setp(o, 'EmbedArchive', 'maybe')));
u2_chk('set_key_missing', @() err(@() setp(o, 'StrongNameKeyFile', 'k8missing.snk')));
u2_chk('set_key_empty', @() size(setp(o, 'StrongNameKeyFile', "")));
u2_chk('set_remoting_number', @() char(setp(o, 'EnableRemoting', 2)));
u2_chk('set_debug_bad', @() err(@() setp(o, 'DebugBuild', 'yes')));
u2_chk('set_samples_missing', @() err(@() setp(o, 'SampleGenerationFiles', 'k8nosuch.m')));
u2_chk('set_classmap_number', @() err(@() setp(o, 'ClassMap', 5)));
u2_chk('set_outdir_bad', @() err(@() setp(o, 'OutputDir', 'out<1>')));

% The MATLAB Data API interface: its warnings and its version.
lastwarn('', '');
o = compiler.build.DotNETAssemblyOptions('k8f.m', 'Interface', 'matlab-data');
[~, id] = lastwarn;
u2_chk('mda_warn', @() id);
u2_chk('mda_version', @() size(o.AssemblyVersion));
u2_chk('mda_embed', @() char(o.EmbedArchive));
lastwarn('', '');
o = compiler.build.DotNETAssemblyOptions('k8f.m', 'Interface', 'matlab-data', 'ClassName', 'C', 'EmbedArchive', 'off');
[~, id] = lastwarn;
u2_chk('mda_classname_warn', @() id);

% Builds: compiler.build and mcc -W dotnet, each into a folder of its own.
d = fullfile(root, 'api');
r = compiler.build.dotNETAssembly('k8f.m', 'AssemblyName', 'K8f', 'FrameworkVersion', '5.0', 'OutputDir', d);
u2_chk('api_type', @() r.BuildType);
u2_chk('api_files', @() rel(r.Files, d));
u2_chk('api_written', @() names(d));
u2_chk('api_options', @() class(r.Options));
d = fullfile(root, 'api4');
r = compiler.build.dotNETAssembly({'k8f.m', 'k8g.m'}, 'OutputDir', d, 'EmbedArchive', 'off');
u2_chk('api4_files', @() rel(r.Files, d));
u2_chk('api4_written', @() names(d));
u2_chk('mcc_plain', @() build(root, 'mcc_plain', {'-W', 'dotnet:K8m,K8mClass,5.0,Private,local', '-T', 'link:lib', 'k8f.m', 'k8g.m'}));
u2_chk('mcc_noclass', @() build(root, 'mcc_noclass', {'-W', 'dotnet:K8n', '-T', 'link:lib', 'k8f.m'}));
u2_chk('mcc_classes', @() build(root, 'mcc_classes', {'-W', 'dotnet:K8c,First,5.0,Private,local', '-T', 'link:lib', ['class{Second:' fullfile(src, 'k8g.m') '}'], 'k8f.m'}));
u2_chk('mcc_separate', @() build(root, 'mcc_separate', {'-W', 'dotnet:K8p,K8pClass,5.0,Private,local', '-T', 'link:lib', '-C', 'k8f.m'}));
u2_chk('mcc_debug', @() build(root, 'mcc_debug', {'-W', 'dotnet:K8d,K8dClass,5.0,Private,local', '-T', 'link:lib', '-g', 'k8f.m'}));
u2_chk('mcc_badversion', @() build(root, 'mcc_badversion', {'-W', 'dotnet:K8b,K8bClass,5.0,Private,local,version=1.x', '-T', 'link:lib', 'k8f.m'}));
u2_chk('mcc_framework6', @() build(root, 'mcc_framework6', {'-W', 'dotnet:K8x,K8xClass,6.0', '-T', 'link:lib', 'k8f.m'}));
u2_chk('mcc_badkey', @() build(root, 'mcc_badkey', {'-W', 'dotnet:K8y,K8yClass,5.0,nosuch.snk,local', '-T', 'link:lib', 'k8f.m'}));
chkdiv('mcc_remote', @() build(root, 'mcc_remote', {'-W', 'dotnet:K8z,K8zClass,5.0,Private,remote', '-T', 'link:lib', 'k8f.m'}), 'ADR0264');
chkdiv('mcc_mda', @() firstline(build(root, 'mcc_mda', {'-W', 'dotnet:K8g,api=matlab-data,K8gClass,5.0,Private,local', '-T', 'link:lib', 'k8f.m'})), 'ADR0264');
chkdiv('api_remoting', @() err(@() compiler.build.dotNETAssembly('k8f.m', 'OutputDir', fullfile(root, 'rem'), 'EnableRemoting', 'on')), 'ADR0264');
u2_chk('api_wrong_options', @() err(@() compiler.build.dotNETAssembly(compiler.build.StandaloneApplicationOptions('k8f.m'))));
u2_chk('api_no_args', @() err(@() compiler.build.dotNETAssembly()));
u2_chk('api_two_out', @() err(@() twoOut()));

% isreal of what is not a number, and dir down a ** folder.
u2_chk('isreal_kinds', @() mat2str([isreal({1}), isreal(struct('a', 1)), isreal("s"), isreal('c'), isreal(true), isreal(@sin), isreal(int8(1)), isreal(1 + 2i)]));
u2_chk('dir_deep', @() deep(fullfile(root, 'mcc_classes')));
u2_chk('dir_deep_m', @() deep(fullfile(root, '**', 'k8*.m')));
cd(old);
rmdir(root, 's');

function v = setp(o, name, value)
% Sets a property on a copy of the options and answers what it then reads.
o.(name) = value;
v = o.(name);
end

function twoOut()
[a, b] = compiler.build.dotNETAssembly('k8f.m'); %#ok<ASGLU>
end

function s = build(root, name, args)
% A build's outcome: ok or its refusal, the warning it raised, and the files it wrote.
folder = fullfile(root, name);
lastwarn('', '');
s = err(@() mcc(args{:}, '-d', folder));
[~, id] = lastwarn;
s = sprintf('%s | warn %s | files %s', s, id, names(folder));
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
if ~exist(folder, 'dir')
    s = '';
    return
end
d = dir(folder);
d = d(~[d.isdir]);
s = strjoin(sort({d.name}), ' ');
end

function s = deep(pattern)
if exist(pattern, 'dir')
    pattern = fullfile(pattern, '**', '*');
end
d = dir(pattern);
d = d(~[d.isdir]);
s = strjoin(sort({d.name}), ' ');
end

function s = rel(files, folder)
files = cellfun(@(f) strrep(f, [folder filesep], ''), cellstr(files), 'UniformOutput', false);
s = strjoin(files(:)', ' ');
end

function s = firstline(text)
t = splitlines(text);
s = t{1};
end

function put(folder, name, lines)
fid = fopen(fullfile(folder, name), 'w');
fprintf(fid, '%s\n', lines{:});
fclose(fid);
end

function chkdiv(name, fn, adr)
% A u2_chk line whose rule is a recorded divergence: the stamp adds what JGraph prints.
fprintf('CHK|%s|%s|div=%s\n', name, u2_text(fn()), adr);
end
