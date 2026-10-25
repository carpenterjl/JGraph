function probe_k11_build(work)
%PROBE_K11_BUILD Builds stage K11's C shared libraries (compiler and SDK plan) with mcc -W lib and
%compiler.build.cSharedLibrary, and prints what each build printed and wrote, the generated text,
%and what compiler.build.CSharedLibraryOptions answers and refuses. Run under matlab -batch
%-noFigureWindows (record-k11.ps1) or jgraph -batch (run-k11.ps1); nothing built is run here, the
%scripts build k11client against the library in main and run it. One key<TAB>value line per
%question, a display or a file's text as indented lines under its key.
beep('off'); % the refusals below would each beep
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'src'));
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
src = fullfile(work, 'src');
mkdir(src);
copyfile(fullfile(here, 'src', 'k11', '*.m'), src);
old = cd(src);
cleanup = onCleanup(@() cd(old));

% The client's library: every shape of signature that compiles, in this order on the command line.
mainFiles = {'k11plain.m', 'k11Mixed.m', 'K11upper.m', 'k11_under.m', 'k11long.m', 'k11wide.m', ...
    'k11_a_function_name_long_enough_to_make_generated_lines_wrap.m', 'k11sub.m', 'k11noparen.m', ...
    'k11outonly.m', 'k11varin.m', 'k11quiet.m', 'k11fail.m'};
d = fullfile(work, 'main');
mccBuild('main', [{'-W', 'lib:k11lib', '-T', 'link:lib', '-d', d}, mainFiles], d);
printFile('main.h', fullfile(d, 'k11lib.h'));
printFile('main.c', fullfile(d, 'k11lib.c'));
printFile('main.def', fullfile(d, 'k11lib.def'));
printFile('main.exports', fullfile(d, 'k11lib.exports'));
printFile('main.readme', fullfile(d, 'readme.txt'));

% A P-file, made in a folder of its own so the source folder keeps only .m files.
pdir = fullfile(work, 'pfile');
mkdir(pdir);
copyfile(fullfile(src, 'k11plain.m'), pdir);
cd(pdir);
pcode('k11plain.m');
delete(fullfile(pdir, 'k11plain.m'));
cd(src);
pfile = fullfile(pdir, 'k11plain.p');

% mcc's other forms, each into a folder of its own, one file unless the case is about the files.
builds = {
    'ell',        {'-l', 'k11plain.m'}
    'ell_o',      {'-l', '-o', 'k11named', 'k11plain.m'}
    'lib_o',      {'-W', 'lib:k11w', '-o', 'k11other', '-T', 'link:lib', 'k11plain.m'}
    'version',    {'-W', 'lib:k11ver,version=2.5', '-T', 'link:lib', 'k11plain.m'}
    'version4',   {'-W', 'lib:k11ver4,version=1.2.3.4', '-T', 'link:lib', 'k11plain.m'}
    'badversion', {'-W', 'lib:k11bad,version=x.y', '-T', 'link:lib', 'k11plain.m'}
    'separate',   {'-W', 'lib:k11sep', '-T', 'link:lib', '-C', 'k11plain.m'}
    'debug',      {'-W', 'lib:k11dbg', '-T', 'link:lib', '-g', 'k11plain.m'}
    'codegen',    {'-W', 'lib:k11cg', '-T', 'codegen', 'k11plain.m'}
    'compile',    {'-W', 'lib:k11cmp', '-T', 'compile:lib', 'k11plain.m'}
    'dashc',      {'-W', 'lib:k11c', '-c', 'k11plain.m'}
    'noT',        {'-W', 'lib:k11noT', 'k11plain.m'}
    'exeT',       {'-W', 'lib:k11exe', '-T', 'link:exe', 'k11plain.m'}
    'fnname',     {'-W', 'lib:k11plain', '-T', 'link:lib', 'k11plain.m'}
    'badname',    {'-W', 'lib:k11-bad', '-T', 'link:lib', 'k11plain.m'}
    'digitname',  {'-W', 'lib:1k11', '-T', 'link:lib', 'k11plain.m'}
    'pcode',      {'-W', 'lib:k11j', '-T', 'link:lib', '-j', 'k11plain.m'}
    'pfile',      {'-W', 'lib:k11p', '-T', 'link:lib', pfile}
    'same',       {'-W', 'lib:k11same', '-T', 'link:lib', 'k11same.m'}
    'tilde',      {'-W', 'lib:k11tilde', '-T', 'link:lib', 'k11tilde.m'}
    'cwords',     {'-W', 'lib:k11cwords', '-T', 'link:lib', 'k11cwords.m'}
    'clash',      {'-W', 'lib:k11clash', '-T', 'link:lib', 'k11clash.m'}
    'script',     {'-W', 'lib:k11script', '-T', 'link:lib', 'k11script.m'}
    'classdef',   {'-W', 'lib:k11cls', '-T', 'link:lib', 'k11cls.m'}
    'twice',      {'-W', 'lib:k11tw', '-T', 'link:lib', 'k11plain.m', 'k11plain.m'}
    'nofiles',    {'-W', 'lib:k11nf', '-T', 'link:lib'}
    'verbose',    {'-W', 'lib:k11v', '-T', 'link:lib', '-v', 'k11plain.m'}
    };
shown = {'codegen', 'same', 'tilde', 'cwords', 'clash', 'script', 'classdef'};
for k = 1:size(builds, 1)
    [key, args] = builds{k, :};
    d = fullfile(work, key);
    mkdir(d);
    mccBuild(key, [args, {'-d', d}], d);
    if any(strcmp(key, shown))
        name = regexp(strjoin(args, ' '), 'lib:(\w+)', 'tokens', 'once');
        printFile([key '.h'], fullfile(d, [name{1} '.h']));
        printFile([key '.c'], fullfile(d, [name{1} '.c']));
        printFile([key '.def'], fullfile(d, [name{1} '.def']));
    end
end

% compiler.build.cSharedLibrary from a folder of its own, so the default output folder lands there.
f1 = fullfile(src, 'k11plain.m');
f2 = fullfile(src, 'k11Mixed.m');
api = fullfile(work, 'api');
mkdir(api);
cd(api);
o = compiler.build.CSharedLibraryOptions(f1);
show('opts.default', o);
k0_try('opts.default.LibraryName', @() o.LibraryName);
k0_try('opts.default.OutputDir', @() o.OutputDir);
k0_try('opts.default.getName', @() o.getName);
k0_try('opts.default.LibraryVersion', @() o.LibraryVersion);
k0_try('opts.default.class.EmbedArchive', @() class(o.EmbedArchive));
k0_try('opts.default.FunctionFiles', @() o.FunctionFiles);
apiBuild('api_default', @() compiler.build.cSharedLibrary(f1), fullfile(api, 'k11plaincSharedLibrary'));
printFile('api_default.gettingstarted', fullfile(api, 'k11plaincSharedLibrary', 'GettingStarted.html'));
printFile('api_default.readme', fullfile(api, 'k11plaincSharedLibrary', 'readme.txt'));

d = fullfile(work, 'api_named');
o = compiler.build.CSharedLibraryOptions({f1, f2}, 'LibraryName', 'K11Named', 'LibraryVersion', '3.1', 'OutputDir', d, 'Verbose', 'on');
show('opts.named', o);
apiBuild('api_named', @() compiler.build.cSharedLibrary(o), d);
d = fullfile(work, 'api_separate');
apiBuild('api_separate', @() compiler.build.cSharedLibrary(f1, 'LibraryName', 'K11Sep', 'EmbedArchive', 'off', ...
    'DebugBuild', 'on', 'OutputDir', d, 'Verbose', 'on'), d);
d = fullfile(work, 'api_files');
apiBuild('api_files', @() compiler.build.cSharedLibrary(f1, 'AdditionalFiles', fullfile(src, 'k11sub.m'), ...
    'AutoDetectDataFiles', 'off', 'SupportPackages', 'none', 'OutputDir', d, 'Verbose', 'on'), d);
d = fullfile(work, 'api_pfile');
apiBuild('api_pfile', @() compiler.build.cSharedLibrary(pfile, 'OutputDir', d), d);
cd(src);

% The options' refusals and conversions.
k0_try('opts.none', @() compiler.build.CSharedLibraryOptions());
k0_try('opts.missing', @() compiler.build.CSharedLibraryOptions('k11nosuch.m'));
k0_try('opts.txt', @() compiler.build.CSharedLibraryOptions(fullfile(here, 'README.md')));
k0_try('opts.number', @() compiler.build.CSharedLibraryOptions(5));
k0_try('opts.emptycell', @() compiler.build.CSharedLibraryOptions({}));
k0_try('opts.unknown.nv', @() compiler.build.CSharedLibraryOptions(f1, 'Nonsense', 1));
k0_try('opts.partial.nv', @() compiler.build.CSharedLibraryOptions(f1, 'libraryn', 'P').LibraryName);
k0_try('opts.ambiguous.nv', @() compiler.build.CSharedLibraryOptions(f1, 'Library', 'P'));
k0_try('opts.odd.nv', @() compiler.build.CSharedLibraryOptions(f1, 'LibraryName'));
k0_try('opts.cell.LibraryName', @() compiler.build.CSharedLibraryOptions({f2, f1}).LibraryName);
k0_try('opts.string.FunctionFiles', @() compiler.build.CSharedLibraryOptions(string(f1)).FunctionFiles);
k0_try('opts.pfile', @() compiler.build.CSharedLibraryOptions(pfile).LibraryName);
k0_try('opts.script', @() compiler.build.CSharedLibraryOptions(fullfile(src, 'k11script.m')).LibraryName);
o = compiler.build.CSharedLibraryOptions(f1);
setCases = {
    'LibraryName', 'k11x'; 'LibraryName', ''; 'LibraryName', 'a b'; 'LibraryName', '1abc'; 'LibraryName', 'a-b';
    'LibraryName', 5; 'LibraryName', "str"; 'LibraryName', 'a/b'; 'LibraryName', 'a.b'; 'LibraryName', 'a:b';
    'LibraryVersion', '2.0'; 'LibraryVersion', ''; 'LibraryVersion', 5; 'LibraryVersion', 'abc'; 'LibraryVersion', "3.1.4";
    'EmbedArchive', 'off'; 'EmbedArchive', 'maybe'; 'EmbedArchive', 0;
    'DebugBuild', 'on'; 'DebugBuild', 2;
    'FunctionFiles', f2; 'FunctionFiles', 'k11nosuch.m'; 'FunctionFiles', fullfile(here, 'README.md'); 'FunctionFiles', {};
    'FunctionFiles', 5; 'FunctionFiles', {f1, f2};
    'OutputDir', 'out<1>'; 'OutputDir', 'k11out'; 'Verbose', 'on'; 'SupportPackages', 'none'; 'AdditionalFiles', 'k11nosuch.txt'};
for k = 1:size(setCases, 1)
    [prop, value] = setCases{k, :};
    key = sprintf('set.%s.%s', prop, k0_show(value));
    k0_try(key, @() setAndRead(o, prop, value));
end
k0_try('api.wrong.options', @() compiler.build.cSharedLibrary(compiler.build.StandaloneApplicationOptions(f1)));
k0_try('api.no.args', @() compiler.build.cSharedLibrary());
k0_try('api.nargout2', @() twoOut(f1));
k0_try('api.opts.extra', @() compiler.build.cSharedLibrary(o, 'Verbose', 'on'));
k0_try('api.badname', @() compiler.build.cSharedLibrary(f1, 'LibraryName', 'a b', 'OutputDir', fullfile(work, 'api_badname')));
end

function v = setAndRead(o, prop, value)
o.(prop) = value;
v = o.(prop);
end

function twoOut(f1)
[a, b] = compiler.build.cSharedLibrary(f1); %#ok<ASGLU>
end

function mccBuild(key, args, d)
lastwarn('', '');
t = tic;
try
    txt = quietMcc(args);
    fprintf('%s\tok\n', key);
    printBuildText(key, txt);
catch ME
    fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
end
fprintf('%s.seconds\t%.1f\n', key, toc(t));
k0_warned(key);
listFiles(key, d);
end

function apiBuild(key, f, d)
lastwarn('', '');
t = tic;
try
    txt = evalc('r = f();');
    printBuildText(key, txt);
    show([key '.results'], r);
    fprintf('%s.BuildType\t%s\n', key, r.BuildType);
    files = cellfun(@(p) strrep(p, [d filesep], '<OUT>\'), r.Files, 'UniformOutput', false);
    fprintf('%s.Files\t%s\n', key, strjoin(files(:)', ' | '));
    show([key '.Options'], r.Options);
catch ME
    fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
end
fprintf('%s.seconds\t%.1f\n', key, toc(t));
k0_warned(key);
listFiles(key, d);
end

% What a build printed, without mbuild's search for a compiler and its environment (they describe
% the machine, not the build).
function printBuildText(key, txt)
txt = strrep(txt, sprintf('\r\n'), newline);
txt = regexprep(txt, '<a [^>]*>|</a>', '');
lines = splitlines(txt);
noise = {'^\.\.\. ', '^Did not find ', '^Set (PATH|INCLUDE|LIB|LIBPATH) = ', '^\t[A-Z0-9_]+ : ', '^ {10,}set ', ...
    '^-{20,}', '^Options file details', '^No MEX options file identified'};
kept = {};
for j = 1:numel(lines)
    if isempty(lines{j}) && (isempty(kept) || j == numel(lines)), continue, end
    if any(cellfun(@(p) ~isempty(regexp(lines{j}, p, 'once')), noise)), continue, end
    kept{end + 1} = lines{j}; %#ok<AGROW>
end
if ~isempty(kept)
    fprintf('%s.printed\n', key);
    for j = 1:numel(kept)
        fprintf('  |%s\n', kept{j});
    end
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
bytes = strrep(bytes, sprintf('\r\n'), newline);
lines = splitlines(bytes);
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
