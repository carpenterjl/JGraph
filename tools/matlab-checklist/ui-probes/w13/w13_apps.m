% w13_apps - what R2025b packages, installs and refuses for Share > MATLAB App, matlab.addons.toolbox
% and matlab.apputil (stage W13 of the App Designer window plan), headless:
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name w13_apps
% rt\apps\AcceptForm.mlapp is a fresh copy of the DS8 acceptance app (ds8\rt\accept). The probe writes
% App Details into it with App Designer's own FileWriter, packages it as the MATLAB App share window does
% (the .mltbx, with extra files of several kinds and a toolbox image, to see every part R2025b writes),
% builds a legacy .mlappinstall with appcreate.internal.buildapp and lists its parts, installs each
% package into this MATLAB and uninstalls it again (the add-ons are left as they were), and records
% the refusals of the two packages' functions. Nothing is run in a window.
here = fileparts(mfilename('fullpath'));
root = fullfile(here, 'rt', 'apps');
short = @(s) strrep(strrep(strrep(char(s), root, '<apps>'), here, '<w13>'), char(matlab.internal.addons.util.retrieveAddOnsInstallationFolder), '<addons>');
if exist(root, 'dir'), rmdir(root, 's'); end
mkdir(root);
copyfile(fullfile(fileparts(here), 'ds8', 'rt', 'accept', 'AcceptForm.mlapp'), root);
app = fullfile(root, 'AcceptForm.mlapp');
w = appdesigner.internal.serialization.FileWriter(app);
w.writeAppMetadata(struct('Name', 'Accept Form', 'Author', 'W13 Probe', 'Version', '2.1', ...
    'Summary', 'A form that adds or multiplies.', 'Description', 'One line.'));
[~, d] = matlab.internal.deployment.getAppDetails(app);
fid = fopen(fullfile(root, 'data.txt'), 'w'); fprintf(fid, 'x\n'); fclose(fid);
fid = fopen(fullfile(root, 'helper.m'), 'w'); fprintf(fid, 'function y = helper(x)\ny = x + 1;\nend\n'); fclose(fid);
mkdir(fullfile(root, 'sub'));
fid = fopen(fullfile(root, 'sub', 'deep.m'), 'w'); fprintf(fid, 'function deep()\nend\n'); fclose(fid);
imwrite(uint8(repmat(reshape([200 60 60], 1, 1, 3), 32, 32)), fullfile(root, 'art.png'));
save(fullfile(root, 'table.mat'), 'd');
beep off;
cleanupAddons = onCleanup(@() cleanAll());

fprintf('== the add-ons folders\n');
fprintf('Add-Ons: %s\n', char(matlab.internal.addons.util.retrieveAddOnsInstallationFolder));
fprintf('Apps: %s\n', char(com.mathworks.appmanagement.MlappinstallUtil.getAppInstallationFolder.getAbsolutePath));

% --- The MATLAB App share script, with extra files and a toolbox image ------------------------------
fprintf('== the share window''s script with files\n');
files = string({fullfile(root, 'data.txt'), fullfile(root, 'helper.m'), fullfile(root, 'sub', 'deep.m'), fullfile(root, 'art.png'), fullfile(root, 'table.mat')});
b = matlab.internal.deployment.MatlabAppDesignerScriptBuilder(app, files', {'', ''});
s = b.getPackageScript(d);
fprintf('%s\n', short(s));
out = evalc('eval(s)');
fprintf('%s\n', short(out));
t = fullfile(root, 'release', 'AcceptForm.mltbx');
fprintf('mltbx exists %d\n', exist(t, 'file') == 2);
z = unzip(t, fullfile(root, 'mltbx1'));
fprintf('== parts of the mltbx with files\n');
for k = 1:numel(z), i = dir(z{k}); fprintf('  %s (%d bytes)\n', short(z{k}), i.bytes); end
for n = {'[Content_Types].xml', 'metadata/filesystemManifest.xml', 'metadata/configuration.xml', 'fsroot/DesktopToolset.xml', 'metadata/addonProperties.xml'}
    f = fullfile(root, 'mltbx1', n{1});
    if exist(f, 'file'), fprintf('---- %s\n%s\n', n{1}, short(fileread(f))); end
end
fprintf('== mlAddonGetProperties\n');
try
    p = mlAddonGetProperties(t); disp(p);
catch e
    fprintf('-> %s: %s\n', e.identifier, short(e.message));
end
fprintf('== toolboxVersion: %s\n', matlab.addons.toolbox.toolboxVersion(t));

% --- ToolboxOptions by hand, with every kind of setting -------------------------------------------------
fprintf('== ToolboxOptions by hand\n');
o = matlab.addons.toolbox.ToolboxOptions(root, 'w13-probe-id', ToolboxFiles={app, fullfile(root, 'data.txt'), fullfile(root, 'sub', 'deep.m')});
disp(o);
fprintf('ToolboxFolder %s\nOutputFile %s\nToolboxMatlabPath %s\nMin %s Max %s\n', short(o.ToolboxFolder), short(o.OutputFile), short(strjoin(o.ToolboxMatlabPath, ' | ')), o.MinimumMatlabRelease, o.MaximumMatlabRelease);
disp(o.SupportedPlatforms);
o.ToolboxName = 'Probe Tools';
o.ToolboxVersion = '3.0.1';
o.AuthorName = 'W13 Probe'; o.AuthorEmail = 'probe@example.com'; o.AuthorCompany = 'JGraph';
o.Summary = 'Sum.'; o.Description = 'Desc.';
o.ToolboxImageFile = fullfile(root, 'art.png');
o.AppGalleryFiles = {app};
o.MinimumMatlabRelease = 'R2020a';
o.OutputFile = fullfile(root, 'release', 'ProbeTools.mltbx');
matlab.addons.toolbox.packageToolbox(o);
z = unzip(o.OutputFile, fullfile(root, 'mltbx2'));
fprintf('== parts of the hand-made mltbx\n');
for k = 1:numel(z), i = dir(z{k}); fprintf('  %s (%d bytes)\n', short(z{k}), i.bytes); end
for n = {'[Content_Types].xml', '_rels/.rels', 'metadata/filesystemManifest.xml', 'metadata/configuration.xml', 'metadata/systemRequirements.xml', 'metadata/addonProperties.xml', 'metadata/coreProperties.xml', 'fsroot/DesktopToolset.xml'}
    f = fullfile(root, 'mltbx2', n{1});
    if exist(f, 'file'), fprintf('---- %s\n%s\n', n{1}, short(fileread(f))); end
end
l = dir(fullfile(root, 'mltbx2', '**', '*.rels'));
for k = 1:numel(l), fprintf('---- %s\n%s\n', short(fullfile(l(k).folder, l(k).name)), fileread(fullfile(l(k).folder, l(k).name))); end

% --- A legacy .mlappinstall -------------------------------------------------------------------------------
fprintf('== buildapp\n');
mla = fullfile(root, 'AcceptFormApp.mlappinstall');
try
    r = appcreate.internal.buildapp(mla, app, '2.1', 'A form that adds or multiplies.', 'One line.', 'W13 Probe', 'probe@example.com', 'JGraph', '', '');
    disp(r);
    z = unzip(mla, fullfile(root, 'mlappinstall'));
    fprintf('== parts of the mlappinstall\n');
    for k = 1:numel(z), i = dir(z{k}); fprintf('  %s (%d bytes)\n', short(z{k}), i.bytes); end
    for k = 1:numel(z)
        [~, ~, e] = fileparts(z{k});
        if any(strcmpi(e, {'.xml', '.rels'}))
            fprintf('---- %s\n%s\n', short(z{k}), short(fileread(z{k})));
        end
    end
    fprintf('== mlappinfo\n');
    disp(mlappinfo(mla));
    fprintf('== getappmetadata fields: %s\n', strjoin(fieldnames(appinstall.internal.getappmetadata(mla))', ', '));
catch e
    fprintf('buildapp -> %s: %s\n', e.identifier, short(e.message));
end

% --- Install and uninstall ---------------------------------------------------------------------------------
fprintf('== installToolbox (the share window''s mltbx)\n');
try
    info = matlab.addons.toolbox.installToolbox(t);
    disp(info);
    fprintf('installedToolboxes:\n'); disp(matlab.addons.toolbox.installedToolboxes);
    fprintf('installedAddons:\n'); disp(matlab.addons.installedAddons);
    fprintf('isAddonEnabled(Guid): %d\n', matlab.addons.isAddonEnabled(info.Guid));
    fprintf('apputil.getInstalledAppInfo after a toolbox install:\n'); matlab.apputil.getInstalledAppInfo;
    fprintf('== the installed folder\n');
    listTree(char(matlab.internal.addons.util.retrieveAddOnsInstallationFolder), short);
    matlab.addons.toolbox.uninstallToolbox(info);
    fprintf('uninstalled; installedToolboxes now %d\n', numel(matlab.addons.toolbox.installedToolboxes));
catch e
    fprintf('installToolbox -> %s: %s\n', e.identifier, short(e.message));
end
fprintf('== matlab.apputil.install (the mlappinstall)\n');
try
    st = matlab.apputil.install(mla);
    disp(st);
    fprintf('getInstalledAppInfo:\n'); matlab.apputil.getInstalledAppInfo;
    i2 = matlab.apputil.getInstalledAppInfo; disp(i2);
    fprintf('installedAddons:\n'); disp(matlab.addons.installedAddons);
    fprintf('== the installed app folder\n');
    listTree(st.location, short);
    wrapper = dir(fullfile(st.location, '*App.m'));
    for k = 1:numel(wrapper), fprintf('---- %s\n%s\n', wrapper(k).name, short(fileread(fullfile(wrapper(k).folder, wrapper(k).name)))); end
    fprintf('== install again (an update)\n');
    st2 = matlab.apputil.install(mla); disp(st2);
    matlab.apputil.uninstall(st.id);
    fprintf('uninstalled; getInstalledAppInfo now %d\n', numel(matlab.apputil.getInstalledAppInfo));
catch e
    fprintf('apputil.install -> %s: %s\n', e.identifier, short(e.message));
end

% --- Refusals -----------------------------------------------------------------------------------------------
fprintf('== refusals\n');
refuse('installToolbox(missing)', @() matlab.addons.toolbox.installToolbox(fullfile(root, 'nothere.mltbx')), short);
refuse('installToolbox(txt)', @() matlab.addons.toolbox.installToolbox(fullfile(root, 'data.txt')), short);
refuse('uninstallToolbox(bogus)', @() matlab.addons.toolbox.uninstallToolbox(struct('Name', 'x', 'Version', '1', 'Guid', 'nothere-guid')), short);
refuse('uninstallToolbox(text)', @() matlab.addons.toolbox.uninstallToolbox('nothere-guid'), short);
refuse('toolboxVersion(txt)', @() matlab.addons.toolbox.toolboxVersion(fullfile(root, 'data.txt')), short);
refuse('toolboxVersion(missing)', @() matlab.addons.toolbox.toolboxVersion(fullfile(root, 'nothere.mltbx')), short);
refuse('toolboxVersion(mltbx, new)', @() matlab.addons.toolbox.toolboxVersion(t, '2'), short);
refuse('ToolboxOptions()', @() matlab.addons.toolbox.ToolboxOptions(''), short);
refuse('ToolboxOptions(folder)', @() matlab.addons.toolbox.ToolboxOptions(root), short);
refuse('ToolboxOptions(missing folder, id)', @() matlab.addons.toolbox.ToolboxOptions(fullfile(root, 'nothere'), 'id'), short);
refuse('ToolboxOptions(folder, bad id)', @() matlab.addons.toolbox.ToolboxOptions(root, 'bad id!'), short);
refuse('ToolboxOptions(folder, id, missing file)', @() matlab.addons.toolbox.ToolboxOptions(root, 'id', ToolboxFiles={fullfile(root, 'nothere.m')}), short);
refuse('ToolboxOptions(folder, id, no files)', @() matlab.addons.toolbox.ToolboxOptions(root, 'id', ToolboxFiles={}), short);
refuse('ToolboxVersion = x', @() setfield(o, 'ToolboxVersion', 'x'), short);
refuse('MinimumMatlabRelease = R20', @() setfield(o, 'MinimumMatlabRelease', 'R20'), short);
refuse('MinimumMatlabRelease = R2010a', @() setfield(o, 'MinimumMatlabRelease', 'R2010a'), short);
refuse('MaximumMatlabRelease = R2015a (min R2020a)', @() setfield(o, 'MaximumMatlabRelease', 'R2015a'), short);
refuse('AppGalleryFiles = txt', @() setfield(o, 'AppGalleryFiles', {fullfile(root, 'data.txt')}), short);
refuse('AppGalleryFiles = not in files', @() setfield(o, 'AppGalleryFiles', {fullfile(root, 'helper.m')}), short);
refuse('ToolboxImageFile = txt', @() setfield(o, 'ToolboxImageFile', fullfile(root, 'data.txt')), short);
refuse('ToolboxMatlabPath = folder with no file', @() setfield(o, 'ToolboxMatlabPath', {fullfile(root, 'release')}), short);
refuse('OutputFile = empty', @() setfield(o, 'OutputFile', ''), short);
refuse('packageToolbox(opts, out)', @() matlab.addons.toolbox.packageToolbox(o, 'x.mltbx'), short);
refuse('packageToolbox(txt)', @() matlab.addons.toolbox.packageToolbox(fullfile(root, 'data.txt')), short);
refuse('packageToolbox(missing)', @() matlab.addons.toolbox.packageToolbox(fullfile(root, 'nothere.prj')), short);
refuse('apputil.run(bogus)', @() matlab.apputil.run('bogusAPP'), short);
refuse('apputil.uninstall(bogus)', @() matlab.apputil.uninstall('bogusAPP'), short);
refuse('apputil.install(txt)', @() matlab.apputil.install(fullfile(root, 'data.txt')), short);
refuse('apputil.install(mltbx)', @() matlab.apputil.install(t), short);
refuse('apputil.package(mltbx)', @() matlab.apputil.package(t), short);
refuse('addons.install(mlappinstall)', @() matlab.addons.install(mla), short);
refuse('addons.install(missing)', @() matlab.addons.install(fullfile(root, 'nothere.mltbx')), short);
refuse('addons.uninstall(bogus)', @() matlab.addons.uninstall('bogus'), short);
refuse('addons.isAddonEnabled(bogus)', @() matlab.addons.isAddonEnabled('bogus'), short);
refuse('addons.enableAddon(bogus)', @() matlab.addons.enableAddon('bogus'), short);
fprintf('== matlab.addons.install of the share mltbx (the table)\n');
try
    tb = matlab.addons.install(t); disp(tb);
    matlab.addons.uninstall(char(tb.Identifier));
    fprintf('uninstalled through matlab.addons.uninstall\n');
catch e
    fprintf('addons.install -> %s: %s\n', e.identifier, short(e.message));
end
fprintf('done\n');

function refuse(what, f, short)
    try
        f();
        fprintf('%s -> ran\n', what);
    catch e
        fprintf('%s -> %s: %s\n', what, e.identifier, short(strrep(e.message, newline, ' / ')));
    end
end

function listTree(folder, short)
    l = dir(fullfile(folder, '**', '*'));
    for k = 1:numel(l)
        if ~l(k).isdir && ~strcmp(l(k).name, '.') && ~strcmp(l(k).name, '..')
            fprintf('  %s (%d bytes)\n', short(fullfile(l(k).folder, l(k).name)), l(k).bytes);
        end
    end
end

function cleanAll()
    % Whatever the probe installed is taken out again, so this MATLAB's add-ons are as they were.
    try
        for tbx = matlab.addons.toolbox.installedToolboxes
            if any(strcmp(tbx.Name, {'Accept Form', 'Probe Tools'}))
                matlab.addons.toolbox.uninstallToolbox(tbx);
            end
        end
    catch
    end
    try
        infos = matlab.apputil.getInstalledAppInfo;
        for k = 1:numel(infos)
            if contains(infos(k).name, 'Accept Form')
                matlab.apputil.uninstall(infos(k).id);
            end
        end
    catch
    end
end
