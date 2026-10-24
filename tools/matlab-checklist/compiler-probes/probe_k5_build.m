function probe_k5_build(work)
%PROBE_K5_BUILD Builds stage K5's component (compiler and SDK plan): k5.m as the .NET assembly K5,
%for k5client. Run under matlab -batch -noFigureWindows; nothing built is run here (record-k5.ps1
%runs it).
beep('off');
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'src'));
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
dsrc = fullfile(work, 'dnsrc');
mkdir(dsrc);
copyfile(fullfile(here, 'src', 'k5', 'k5.m'), dsrc);
d = fullfile(work, 'dn', 'K5');
try
    r = compiler.build.dotNETAssembly(fullfile(dsrc, 'k5.m'), 'AssemblyName', 'K5', ...
        'FrameworkVersion', '5.0', 'OutputDir', d);
    files = cellfun(@(f) strrep(f, [d filesep], ''), r.Files, 'UniformOutput', false);
    fprintf('dotnet.K5\tok %s\n', strjoin(sort(files(:))', ', '));
catch e
    fprintf('dotnet.K5\tERR %s | %s\n', e.identifier, k0_flat(e.message));
end
end
