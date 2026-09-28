% timing_interop.m -- the interop plan's timing rows (stage 10, ADR 0183): the cost of a .NET call
% and of a calllib round trip, warm, per call in microseconds (the 1e6-element array in
% milliseconds). A warm-up pass, then three timed passes. No figure and no window, so it runs the
% same under R2025b and JGraph:
%
%   matlab -batch "run('tools/interop/timing_interop.m')"
%   jgraph -batch "run('tools/interop/timing_interop.m')"
%
% Run tools/interop/stage-assets.ps1 first: it loads the staged jgtestlib.dll.
dotnetenv("core", Version="8");
% run() runs a script in its own folder in both engines. (Not mfilename: JGraph answers '' for it
% in a script started by run from the command line; open-items entry 20.)
repo = fileparts(fileparts(pwd));
root = fullfile(repo, 'tests', 'JGraph.Tests', 'MatlabParity', 'fixtures', 'interop');
addpath(root);
loadlibrary(fullfile(root, 'jgtestlib.dll'), @jgtestlib_proto);
N = 20000;
big = rand(1, 1e6);
for pass = 0:3
    % a static method with value arguments
    t = tic; for k = 1:N, x = System.Math.Max(3, 7); end; tmax = toc(t) / N;
    % an instance method in a loop
    sb = System.Text.StringBuilder();
    t = tic; for k = 1:N, sb.Append('a'); end; tapp = toc(t) / N;
    % a generic list: Add, then the indexer
    l = NET.createGeneric('System.Collections.Generic.List', {'System.Double'}, N);
    t = tic; for k = 1:N, l.Add(k); end; tadd = toc(t) / N;
    t = tic; s = 0; for k = 0:N-1, s = s + l.Item(k); end; titem = toc(t) / N;
    % a property read
    t = tic; for k = 1:N, n = sb.Length; end; tprop = toc(t) / N;
    % calllib: a scalar call, and a 1e6-double array in and out
    t = tic; for k = 1:N, y = calllib('jgtestlib', 'jg_double', 2.5); end; tlib = toc(t) / N;
    M = 20;
    t = tic; for k = 1:M, r = calllib('jgtestlib', 'jg_scale_double', big, numel(big), 1.0); end; tarr = toc(t) / M;
    if pass > 0
        fprintf(['pass %d: static_max %.2f us | instance_append %.2f us | list_add %.2f us | ' ...
            'list_item %.2f us | property %.2f us | calllib_scalar %.2f us | calllib_1e6 %.2f ms\n'], ...
            pass, tmax * 1e6, tapp * 1e6, tadd * 1e6, titem * 1e6, tprop * 1e6, tlib * 1e6, tarr * 1e3);
    end
end
unloadlibrary('jgtestlib');
