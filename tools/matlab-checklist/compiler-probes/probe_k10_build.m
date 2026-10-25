function probe_k10_build(work)
%PROBE_K10_BUILD Builds stage K10's C shared library k10lib from src\k10 with R2025b's mcc -W lib
%(compiler and SDK plan, stage K10: the native bridge's callers), and prints what the build wrote.
%Run under matlab -batch -noFigureWindows; record-k10.ps1 builds the C clients in src\k10client
%against it and runs them. One key<TAB>value line per question.
beep('off');
here = fileparts(mfilename('fullpath'));
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
src = fullfile(work, 'src');
mkdir(src);
copyfile(fullfile(here, 'src', 'k10', '*'), src);
old = cd(src);
cleanup = onCleanup(@() cd(old));
listing = dir(fullfile(src, '*.m'));
names = sort({listing.name});
d = fullfile(work, 'lib');
mkdir(d);
t = tic;
try
    mcc('-W', 'lib:k10lib', '-T', 'link:lib', '-d', d, '-v', names{:});
    fprintf('build\tok\n');
catch e
    fprintf('build\terror\t%s\t%s\n', e.identifier, e.message);
end
fprintf('build.seconds\t%.1f\n', toc(t));
listing = dir(d);
for k = 1:numel(listing)
    if ~listing(k).isdir
        fprintf('file\t%s\n', listing(k).name);
    end
end
end
