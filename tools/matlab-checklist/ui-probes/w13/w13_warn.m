% w13_warn - why R2025b warns "not supported for this MATLAB release or for this platform" when it installs a
% toolbox JGraph wrote (stage W13's parity work), headless:
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name w13_warn
% Installs each .mltbx in rt\jgraph\variants (R2025b's own package with its signature stripped, JGraph's), reads
% lastwarn and uninstalls it again.
here = fileparts(mfilename('fullpath'));
folder = fullfile(here, 'rt', 'jgraph', 'variants');
files = dir(fullfile(folder, '*.mltbx'));
beep off;
for k = 1:numel(files)
    t = fullfile(files(k).folder, files(k).name);
    fprintf('== %s\n', files(k).name);
    lastwarn('');
    try
        info = matlab.addons.toolbox.installToolbox(t);
        [msg, id] = lastwarn;
        fprintf('installed %s; warning [%s] %s\n', info.Name, id, msg);
        matlab.addons.toolbox.uninstallToolbox(info);
    catch e
        fprintf('-> %s: %s\n', e.identifier, strrep(e.message, newline, ' / '));
    end
end
fprintf('done\n');
