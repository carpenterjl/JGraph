% w13_installed - what R2025b writes beside an installed toolbox and an installed app (stage W13),
% headless, after w13_apps has left rt\apps\release\AcceptForm.mltbx and rt\apps\AcceptFormApp.mlappinstall:
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name w13_installed
% Each package is installed into this MATLAB, its resources folder printed, and uninstalled again.
here = fileparts(mfilename('fullpath'));
root = fullfile(here, 'rt', 'apps');
addons = char(matlab.internal.addons.util.retrieveAddOnsInstallationFolder);
short = @(s) strrep(strrep(char(s), root, '<apps>'), addons, '<addons>');
beep off;
t = fullfile(root, 'release', 'AcceptForm.mltbx');
mla = fullfile(root, 'AcceptFormApp.mlappinstall');
fprintf('== the toolbox''s resources\n');
try
    info = matlab.addons.toolbox.installToolbox(t);
    folder = fullfile(addons, 'Toolboxes', info.Name);
    l = dir(fullfile(folder, 'resources', '*'));
    for k = 1:numel(l)
        if l(k).isdir, continue; end
        f = fullfile(l(k).folder, l(k).name);
        [~, ~, e] = fileparts(f);
        if strcmpi(e, '.xml'), fprintf('---- %s\n%s\n', short(f), short(fileread(f))); else, fprintf('---- %s (%d bytes)\n', short(f), l(k).bytes); end
    end
    fprintf('path has the folder: %d\n', contains(path, folder));
    fprintf('path has sub: %d\n', contains(path, fullfile(folder, 'sub')));
    matlab.addons.toolbox.uninstallToolbox(info);
    fprintf('path has the folder after uninstall: %d; folder exists: %d\n', contains(path, folder), exist(folder, 'dir') == 7);
catch e
    fprintf('-> %s: %s\n', e.identifier, short(e.message));
end
fprintf('== the app''s resources\n');
try
    st = matlab.apputil.install(mla);
    l = dir(fullfile(st.location, 'resources', '*'));
    for k = 1:numel(l)
        if l(k).isdir, continue; end
        f = fullfile(l(k).folder, l(k).name);
        [~, ~, e] = fileparts(f);
        if strcmpi(e, '.xml'), fprintf('---- %s\n%s\n', short(f), short(fileread(f))); else, fprintf('---- %s (%d bytes)\n', short(f), l(k).bytes); end
    end
    fprintf('path has the app folder: %d\n', contains(path, st.location));
    fprintf('== install again\n');
    st2 = matlab.apputil.install(mla); disp(st2);
    matlab.apputil.uninstall(st.id);
    fprintf('folder exists after uninstall: %d\n', exist(st.location, 'dir') == 7);
catch e
    fprintf('-> %s: %s\n', e.identifier, short(e.message));
end
fprintf('done\n');
