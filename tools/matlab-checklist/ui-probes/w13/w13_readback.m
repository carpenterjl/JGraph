% w13_readback - R2025b reads and installs the .mltbx JGraph's Share > MATLAB App wrote (stage W13's
% parity check in the other direction), headless:
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name w13_readback
% Copy the toolbox the window check run-w13designer.ps1 left at <WorkRoot>\release\W4App.mltbx into rt\jgraph\
% first. The toolbox is installed into this MATLAB, read back and uninstalled again.
here = fileparts(mfilename('fullpath'));
folder = fullfile(here, 'rt', 'jgraph');
addons = char(matlab.internal.addons.util.retrieveAddOnsInstallationFolder);
short = @(s) strrep(strrep(char(s), folder, '<jgraph>'), addons, '<addons>');
beep off;
files = dir(fullfile(folder, '*.mltbx'));
if isempty(files)
    fprintf('no .mltbx in %s\n', short(folder));
    return;
end
for k = 1:numel(files)
    t = fullfile(files(k).folder, files(k).name);
    fprintf('== %s\n', short(t));
    fprintf('toolboxVersion: %s\n', matlab.addons.toolbox.toolboxVersion(t));
    try
        p = mlAddonGetProperties(t); disp(p);
    catch e
        fprintf('mlAddonGetProperties -> %s: %s\n', e.identifier, short(e.message));
    end
    z = unzip(t, fullfile(folder, 'unzipped'));
    fprintf('parts: %d\n', numel(z));
    for j = 1:numel(z), fprintf('  %s\n', short(z{j})); end
    rmdir(fullfile(folder, 'unzipped'), 's');
    try
        info = matlab.addons.toolbox.installToolbox(t);
        disp(info);
        installed = fullfile(addons, 'Toolboxes', info.Name);
        fprintf('installed folder exists: %d\n', exist(installed, 'dir') == 7);
        l = dir(fullfile(installed, '**', '*'));
        for j = 1:numel(l), if ~l(j).isdir, fprintf('  %s (%d bytes)\n', short(fullfile(l(j).folder, l(j).name)), l(j).bytes); end, end
        reg = fullfile(installed, 'resources', 'mltbx_app_gallery_registration.xml');
        if exist(reg, 'file'), fprintf('---- gallery registration\n%s\n', fileread(reg)); end
        fprintf('path has the folder: %d\n', contains(path, installed));
        a = matlab.addons.installedAddons;
        fprintf('installedAddons lists it: %d\n', any(a.Identifier == string(info.Guid)));
        matlab.addons.toolbox.uninstallToolbox(info);
        fprintf('uninstalled; folder exists: %d\n', exist(installed, 'dir') == 7);
    catch e
        fprintf('installToolbox -> %s: %s\n', e.identifier, short(e.message));
    end
end
fprintf('done\n');
