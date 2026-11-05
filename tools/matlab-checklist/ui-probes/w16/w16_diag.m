% w16_diag - why installToolbox refused: the add-on registry's view of the W16 toolboxes.
here = fileparts(mfilename('fullpath'));
root = fullfile(here, 'rt', 'accept');
t = fullfile(root, 'release', 'W16Blank.mltbx');
fprintf('mltbx exists %d\n', isfile(t));
a = matlab.addons.installedAddons;
disp(a(contains(string(a.Name), 'W16'), :));
tb = matlab.addons.toolbox.installedToolboxes;
fprintf('installedToolboxes: %s %s, fields %s\n', class(tb), mat2str(size(tb)), strjoin(fieldnames(tb)', ','));
try
    info = matlab.addons.toolbox.installToolbox(t);
    disp(info);
    matlab.addons.toolbox.uninstallToolbox(info);
    fprintf('install and uninstall ran\n');
catch e
    fprintf('install -> %s\n', getReport(e, 'extended', 'hyperlinks', 'off'));
end
a = matlab.addons.installedAddons;
disp(a(contains(string(a.Name), 'W16'), :));
