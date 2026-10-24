function probe_k4_build(work, part)
%PROBE_K4_BUILD Builds stage K4's programs (compiler and SDK plan): k4r_main once per -R option
%(none, -startmsg, -completemsg, both in one -R, -logfile, -singleCompThread, -nojvm, an unknown
%option, -startmsg with no message), and k4.m as two .NET assemblies, K4A and K4B, for k4client.
%Run under matlab -batch -noFigureWindows; nothing built is run here (record-k4.ps1 runs them).
%PART is 'all' (the default), 'ropts' or 'dotnet'. The class is each assembly's default, k4Class:
%R2025b refuses ClassMap as a name-value pair (Compiler:clishared:doNotSetRequiredPropByNVPair).
if nargin < 2, part = 'all'; end
beep('off');
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'src'));
if strcmp(part, 'all') && exist(work, 'dir'), rmdir(work, 's'); end
if ~exist(work, 'dir'), mkdir(work); end
if any(strcmp(part, {'all', 'ropts'}))
    ropts(here, work);
end
if any(strcmp(part, {'all', 'dotnet'}))
    dotnet(here, work);
end
end

function ropts(here, work)
src = fullfile(work, 'src');
mkdir(src);
for f = {'k0_quiet.m', 'k0_try.m', 'k0_show.m', 'k0_flat.m', 'k0_warned.m'}
    copyfile(fullfile(here, 'src', f{1}), src);
end
copyfile(fullfile(here, 'src', 'k4', 'k4r_main.m'), src);
builds = {
    'none',      {}
    'start',     {'-R', '-startmsg,K4 start'}
    'complete',  {'-R', '-completemsg,K4 complete'}
    'both',      {'-R', '-startmsg,K4 start,-completemsg,K4 complete'}
    'log',       {'-R', '-logfile,k4r.log'}
    'single',    {'-R', '-singleCompThread'}
    'nojvm',     {'-R', '-nojvm'}
    'bogus',     {'-R', '-bogus'}
    'startbare', {'-R', '-startmsg'}
    };
cd(src);
for i = 1:size(builds, 1)
    d = fullfile(work, ['r_' builds{i, 1}]);
    mkdir(d);
    try
        txt = evalc('mcc(''-m'', ''k4r_main.m'', ''-C'', ''-d'', d, builds{i, 2}{:})');
        fprintf('build.%s\tok\n', builds{i, 1});
        report(txt);
    catch e
        fprintf('build.%s\tERR %s | %s\n', builds{i, 1}, e.identifier, k0_flat(e.message));
    end
end
cd(here);
end

function dotnet(here, work)
dsrc = fullfile(work, 'dnsrc');
if ~exist(dsrc, 'dir'), mkdir(dsrc); end
copyfile(fullfile(here, 'src', 'k4', 'k4.m'), dsrc);
for name = {'K4A', 'K4B'}
    d = fullfile(work, 'dn', name{1});
    try
        r = compiler.build.dotNETAssembly(fullfile(dsrc, 'k4.m'), 'AssemblyName', name{1}, ...
            'FrameworkVersion', '5.0', 'OutputDir', d);
        files = cellfun(@(f) strrep(f, [d filesep], ''), r.Files, 'UniformOutput', false);
        fprintf('dotnet.%s\tok %s\n', name{1}, strjoin(sort(files(:))', ', '));
    catch e
        fprintf('dotnet.%s\tERR %s | %s\n', name{1}, e.identifier, k0_flat(e.message));
    end
end
end

function report(txt)
lines = splitlines(strtrim(txt));
for i = 1:numel(lines)
    if strlength(strtrim(lines{i})) > 0
        fprintf('  %s\n', k0_flat(lines{i}));
    end
end
end
