function record_k0_build(outRoot)
%RECORD_K0_BUILD What R2025b's build side prints: mcc refusals, the standalone options class's
%display and validation errors, a compiler.build.Results and the files a build writes
%(compiler and SDK plan, stage K0). Run under matlab -batch; one key<TAB>value line per question,
%a display as indented lines under its key. outRoot is a scratch folder outside the repo.
here = fileparts(mfilename('fullpath'));
src = fullfile(here, 'src');
addpath(src);
beep('off'); % the refusals below would each beep
work = fullfile(outRoot, 'buildside');
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
old = cd(src);
cleanup = onCleanup(@() cd(old));

% mcc refusals.
bad = {
    'mcc.obsolete.F',     {'-F', 'x.prj'}
    'mcc.obsolete.x',     {'-x', 'k0_args.m'}
    'mcc.obsolete.L',     {'-L', 'C', 'k0_args.m'}
    'mcc.obsolete.t',     {'-t', 'k0_args.m'}
    'mcc.missing.file',   {'-m', 'k0_nosuchfile.m'}
    'mcc.no.file',        {'-m'}
    'mcc.bad.W',          {'-W', 'bogus:x', 'k0_args.m'}
    'mcc.mismatch.WT',    {'-W', 'main:k0_args', '-T', 'link:lib', 'k0_args.m'}
    'mcc.bad.T',          {'-T', 'link:nothing', 'k0_args.m'}
    'mcc.unknown.flag',   {'-Q', 'k0_args.m'}
    'mcc.bad.version',    {'-W', 'main:k0_args,version=1.2.3.4.5', '-T', 'link:exe', 'k0_args.m'}
    'mcc.bad.version.big',{'-W', 'main:k0_args,version=70000', '-T', 'link:exe', 'k0_args.m'}
    'mcc.a.missing',      {'-m', 'k0_args.m', '-a', 'k0_nosuch_data.mat'}
    'mcc.e.and.m',        {'-m', '-e', 'k0_args.m'}
    'mcc.script.main',    {'-m', 'k0_inside_script.m'}
    };
for k = 1:size(bad, 1)
    [key, args] = bad{k, :};
    d = fullfile(work, matlab.lang.makeValidName(key));
    mkdir(d);
    lastwarn('', '');
    try
        txt = evalc('mcc(args{:}, ''-d'', d)');
        fprintf('%s\tok (%s)\n', key, k0_flat(strtrim(txt)));
    catch ME
        fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
    end
    k0_warned(key);
end
k0_try('mcc.help', @() evalc('mcc -?'));

% Duplicate names: two folders each holding dup_util.m, both added.
d1 = fullfile(work, 'dupA'); d2 = fullfile(work, 'dupB');
mkdir(d1); mkdir(d2);
writelines(["function dup_util", "disp('A')", "end"], fullfile(d1, 'dup_util.m'));
writelines(["function dup_util", "disp('B')", "end"], fullfile(d2, 'dup_util.m'));
k0_try('mcc.duplicate.names', @() quietMcc({'-m', 'k0_args.m', '-a', d1, '-a', d2, '-d', fullfile(work, 'dupout')}));

% The options class.
opts = compiler.build.StandaloneApplicationOptions('k0_args.m');
show('opts.display', opts);
setters = {
    'ExecutableVersion', '1.2.3.4.5'
    'ExecutableVersion', '70000'
    'ExecutableVersion', 'abc'
    'ExecutableVersion', '2'
    'ExecutableName', 'a/b'
    'ExecutableName', 'two words'
    'ExecutableName', ''
    'OutputDir', 'a|b'
    'TreatInputsAsNumeric', 'maybe'
    'TreatInputsAsNumeric', true
    'EmbedArchive', 0
    'AdditionalFiles', 'k0_nosuch_data.mat'
    'AdditionalFiles', {'k0_show.m'}
    'SupportPackages', {}
    'ExternalEncryptionKey', struct('EncryptionKeyFile', 'x')
    'ObfuscateArchive', 'on'
    'ExecutableIcon', 'k0_nosuch.png'
    'CustomHelpTextFile', 'k0_nosuch.txt'
    'Verbose', 'loud'
    };
for k = 1:size(setters, 1)
    [prop, val] = setters{k, :};
    o = compiler.build.StandaloneApplicationOptions('k0_args.m');
    key = sprintf('opts.set.%s.%s', prop, matlab.lang.makeValidName(k0_show(val)));
    try
        o.(prop) = val;
        fprintf('%s\tok %s\n', key, k0_show(o.(prop)));
    catch ME
        fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
    end
end
k0_try('opts.ctor.missing', @() compiler.build.StandaloneApplicationOptions('k0_nosuch.m'));
k0_try('opts.ctor.txt', @() compiler.build.StandaloneApplicationOptions('inside.txt'));
k0_try('opts.ctor.nv', @() compiler.build.StandaloneApplicationOptions('k0_args.m', 'ExecutableName', 'named'));
k0_try('opts.ctor.badname', @() compiler.build.StandaloneApplicationOptions('k0_args.m', 'NoSuchOption', 1));
k0_try('build.opts.and.nv', @() compiler.build.standaloneApplication(opts, 'Verbose', 'on'));
k0_try('build.missing', @() compiler.build.standaloneApplication('k0_nosuch.m'));

% A build through the API, and what it leaves.
outDir = fullfile(work, 'apibuild');
r = compiler.build.standaloneApplication('k0_args.m', 'OutputDir', outDir, 'ExecutableVersion', '2.3.4.5');
show('results.display', r);
k0_try('results.BuildType', @() r.BuildType);
k0_try('results.Files.names', @() cellfun(@(f) relname(f, outDir), r.Files', 'UniformOutput', false));
k0_try('results.IncludedSupportPackages', @() r.IncludedSupportPackages);
show('results.RuntimeDependencies', r.RuntimeDependencies);
show('results.Required', r.RuntimeDependencies.Required);
k0_try('results.class.Options', @() class(r.Options));
k0_try('results.hidden.BuildID.class', @() class(r.BuildID));
k0_try('results.hidden.BuildDuration.class', @() class(r.BuildDuration));
listing = dir(outDir);
k0_try('apibuild.files', @() sort({listing(~[listing.isdir]).name}));
for f = {'buildresult.json', 'requiredMCRProducts.txt', 'unresolvedSymbols.txt', 'mccExcludedFiles.log', 'includedSupportPackages.txt'}
    p = fullfile(outDir, f{1});
    if exist(p, 'file')
        k0_try(['file.' f{1}], @() strrep(fileread(p), outDir, '<OUT>'));
    end
end
k0_try('file.readme.txt.sections', @() regexp(fileread(fullfile(outDir, 'readme.txt')), '(?m)^\d+\. [^\r\n]*', 'match'));

% Default OutputDir and ExecutableName with no options.
o = compiler.build.StandaloneApplicationOptions('k0_args.m');
k0_try('opts.default.OutputDir', @() relname(o.OutputDir, pwd));
k0_try('opts.default.ExecutableName', @() o.ExecutableName);
k0_try('opts.default.ExecutableIcon', @() relname(o.ExecutableIcon, matlabroot));
k0_try('opts.default.ExecutableSplashScreen', @() relname(o.ExecutableSplashScreen, matlabroot));
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

function s = relname(p, base)
s = strrep(p, base, '<BASE>');
end
