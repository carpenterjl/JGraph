% p7_accept - the packages and class folders plan's acceptance library (stage P7, ADR 0313) run in
% both engines: lib/+signal (+io/readTrace.m with +io/private/parseNumbers.m, Trace.m a handle
% class, summarize.m a script) and lib/@Series (Series.m with the method files total.m and smooth.m
% declared, describe.m not, and private/weight.m), used by the top-level script p7_main.m through
% its imports, then the tools over the library. One key<TAB>value line each; nothing here is
% engine-specific, and the two outputs are held line for line:
%   powershell -ExecutionPolicy Bypass -File .\accept-p7.ps1 -Engine R2025b -WorkRoot <folder>
%   powershell -ExecutionPolicy Bypass -File .\accept-p7.ps1 -Engine JGraph -WorkRoot <folder> -Runtime <runtime>
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'lib'));
rt = fullfile(here, 'rt');
if ~exist(rt, 'dir'), mkdir(rt); end
p7file = fullfile(rt, 'trace.txt');
fid = fopen(p7file, 'w');
fprintf(fid, 'bench A\n3\n1\n4\nnot a number\n1\n5\n');
fclose(fid);

p7_main

% ---- the tools over the library ----
fprintf('which.readTrace\t%s\n', short(which('signal.io.readTrace'), here));
fprintf('which.Trace\t%s\n', short(which('signal.Trace'), here));
fprintf('which.summarize\t%s\n', short(which('signal.summarize'), here));
fprintf('which.total\t%s\n', short(which('Series/total'), here));
fprintf('which.describe\t%s\n', short(which('Series/describe'), here));
fprintf('which.Series\t%s\n', short(which('Series'), here));
% exist('signal') itself is left out: R2025b answers 7 for its own toolbox folder of that name.
fprintf('exist\t%d %d %d %d\n', exist('signal.Trace'), exist('Series'), exist('signal.io.readTrace'), exist('signal.summarize'));
m = methods('Series');
fprintf('methods.Series\t%s\n', strjoin(sort(m(:)'), ' '));
fprintf('super\t%s\n', strjoin(superclasses('signal.Trace')', ' '));
w = what(fullfile(here, 'lib', '+signal'));
fprintf('what.m\t%s\n', strjoin(sort(w.m(:)'), ' '));
fprintf('what.classes\t%s\n', strjoin(sort(w.classes(:)'), ' '));
fprintf('what.packages\t%s\n', strjoin(sort(w.packages(:)'), ' '));
fprintf('private.outside\t%s\n', refused(@() weight(3)));
fprintf('private.pkg\t%s\n', refused(@() parseNumbers({'1'})));
fprintf('bare.member\t%s\n', refused(@() readTrace(p7file)));
fprintf('nargin\t%d %d %d\n', nargin('signal.io.readTrace'), nargin('Series/smooth'), nargin('signal.Trace'));

function s = short(p, here)
% A path under the probe's folder, relative with forward slashes.
s = strrep(strrep(p, [here filesep], ''), '\', '/');
end

function s = refused(f)
try
    f();
    s = 'ran';
catch e
    s = [e.identifier ' | ' e.message];
end
end
