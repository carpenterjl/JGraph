% w0_share - what App Designer's Share items make of an app in R2025b (stage W0 of the App Designer
% window plan), headless:
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name w0_share
% rt\share\AcceptForm.mlapp is a fresh copy of the DS8 acceptance app (ds8\rt\accept, which the local
% test AppDesignerAcceptanceTests writes). The probe writes App
% Details into it with App Designer's own FileWriter, reads them back as the share windows do
% (matlab.internal.deployment.getAppDetails), prints the scripts the three share windows' builders
% write (deployAppDesignerProject's road, without its windows), runs the MATLAB App script to make the
% .mltbx and lists what is in it. Nothing is installed: install/uninstall would change this MATLAB's
% add-ons, so only their sources are read (see the notes).
here = fileparts(mfilename('fullpath'));
share = fullfile(here, 'rt', 'share');
app = fullfile(share, 'AcceptForm.mlapp');
short = @(s) strrep(strrep(char(s), share, '<share>'), here, '<w0>');
if exist(share, 'dir'), rmdir(share, 's'); end
mkdir(share);
copyfile(fullfile(fileparts(here), 'ds8', 'rt', 'accept', 'AcceptForm.mlapp'), share);

% --- App Details as App Designer stores them --------------------------------------------------------
fprintf('== details before\n');
[a, d] = matlab.internal.deployment.getAppDetails(app);
fprintf('getAppDetails outputs: %s | %s\n', class(a), class(d));
disp(a); disp(d);
w = appdesigner.internal.serialization.FileWriter(app);
for v = {'1', '2.1', '1.2.3', '1.2.3.4', 'abc', '', ' 3.0 '}
    try
        w.writeAppMetadata(struct('Version', v{1}));
        r = appdesigner.internal.serialization.FileReader(app);
        m = r.readAppMetadata();
        fprintf('Version %-9s -> stored %s\n', ['''' v{1} ''''], mat2str(m.Version));
    catch e
        fprintf('Version %-9s -> %s: %s\n', ['''' v{1} ''''], e.identifier, short(e.message));
    end
end
w.writeAppMetadata(struct('Name', 'Accept Form', 'Author', 'W0 Probe', 'Version', '2.1', ...
    'Summary', 'A form that adds or multiplies.', 'Description', sprintf('Line one.\nLine two.')));
r = appdesigner.internal.serialization.FileReader(app);
fprintf('== readAppMetadata after\n'); disp(r.readAppMetadata());
fprintf('== details after\n');
[a, d] = matlab.internal.deployment.getAppDetails(app);
disp(a); disp(d);
z = unzip(app, fullfile(share, 'unzipped'));
fprintf('== parts of the app after the write\n');
for k = 1:numel(z), fprintf('  %s\n', short(z{k})); end
fprintf('%s\n', fileread(fullfile(share, 'unzipped', 'metadata', 'appMetadata.xml')));
fprintf('%s\n', fileread(fullfile(share, 'unzipped', 'metadata', 'coreProperties.xml')));
rmdir(fullfile(share, 'unzipped'), 's');

% --- The scripts the share windows run ---------------------------------------------------------------
req = string(fullfile(share, 'data.txt'));
fid = fopen(req, 'w'); fprintf(fid, 'x\n'); fclose(fid);
for c = {{"", {'', ''}}, {req, {fullfile(share, 'splash.png'), fullfile(share, 'icon.png')}}}
    files = c{1}{1}; images = c{1}{2};
    fprintf('== standalone, files %s, images %s\n', short(strjoin(files, ',')), short(strjoin(images, ',')));
    b = compiler.internal.ui.StandaloneAppDesignerScriptBuilder(app, files, images);
    s = b.getBuildScript(d);
    s = append(s, newline, b.getPackageScript(d));
    fprintf('%s\n', short(s));
    fprintf('== web app, files %s\n', short(strjoin(files, ',')));
    b = compiler.internal.ui.WebAppAppDesignerScriptBuilder(app, files, images);
    fprintf('%s\n', short(b.getBuildScript(d)));
    fprintf('== matlab app, files %s\n', short(strjoin(files, ',')));
    b = matlab.internal.deployment.MatlabAppDesignerScriptBuilder(app, files, images);
    fprintf('%s\n', short(b.getPackageScript(d)));
end
fprintf('== getAppOutputFolder: %s\n', short(matlab.internal.deployment.getAppOutputFolder(app)));

fprintf('== the description as read: %s\n', mat2str(double(d.Description)));
m = appdesigner.internal.serialization.FileReader(app).readAppMetadata();
fprintf('== the description as FileReader reads it: %s\n', mat2str(double(m.Description)));

% --- Share > MATLAB App, run ------------------------------------------------------------------------
b = matlab.internal.deployment.MatlabAppDesignerScriptBuilder(app, "", {'', ''});
s = b.getPackageScript(d);
fprintf('== running the MATLAB App script with a two-line description\n');
try
    evalc('eval(s)');
catch e
    fprintf('-> %s: %s\n', e.identifier, short(e.message));
end
for q = {'He said "hi"', 'it''s'}
    w.writeAppMetadata(struct('Description', q{1}));
    [~, d] = matlab.internal.deployment.getAppDetails(app);
    s = b.getPackageScript(d);
    fprintf('== a description of %s: line %s\n', q{1}, short(regexp(s, 'packageOpts.Description[^\n]*', 'match', 'once')));
    try
        evalc('eval(s)');
        fprintf('-> ran\n');
    catch e
        fprintf('-> %s: %s\n', e.identifier, short(e.message));
    end
end
w.writeAppMetadata(struct('Description', 'One line.'));
[~, d] = matlab.internal.deployment.getAppDetails(app);
if exist(fullfile(share, 'release'), 'dir'), rmdir(fullfile(share, 'release'), 's'); end
s = b.getPackageScript(d);
fprintf('== running the MATLAB App script\n');
out = evalc('eval(s)');
fprintf('%s\n', short(out));
t = fullfile(share, 'release', 'AcceptForm.mltbx');
fprintf('mltbx exists %d, %d bytes\n', exist(t, 'file') == 2, dir(t).bytes);
z = unzip(t, fullfile(share, 'mltbx'));
fprintf('== parts of the mltbx\n');
for k = 1:numel(z)
    i = dir(z{k});
    fprintf('  %s (%d bytes)\n', short(z{k}), i.bytes);
end
for k = 1:numel(z)
    [~, ~, e] = fileparts(z{k});
    if any(strcmpi(e, {'.xml', '.rels', '.json'})) || endsWith(z{k}, '[Content_Types].xml')
        fprintf('---- %s\n%s\n', short(z{k}), short(fileread(z{k})));
    end
end
fprintf('== matlab.addons.toolbox.toolboxVersion: %s\n', matlab.addons.toolbox.toolboxVersion(t));
try
    o = matlab.addons.toolbox.ToolboxOptions(t);
    fprintf('== ToolboxOptions read back from the mltbx\n'); disp(o);
catch e
    fprintf('ToolboxOptions(mltbx) -> %s: %s\n', e.identifier, short(e.message));
end
fprintf('== files left in release\n');
l = dir(fullfile(share, 'release', '**', '*'));
for k = 1:numel(l), if ~l(k).isdir, fprintf('  %s\n', short(fullfile(l(k).folder, l(k).name))); end, end

% --- matlab.apputil without installing ----------------------------------------------------------------
fprintf('== matlab.apputil\n');
info = matlab.apputil.getInstalledAppInfo;
if isstruct(info), fl = strjoin(fieldnames(info)', ','); else, fl = '-'; end
fprintf('getInstalledAppInfo: %s %s, fields %s\n', class(info), mat2str(size(info)), fl);
fprintf('getInstalledAppInfo with no output:\n'); matlab.apputil.getInstalledAppInfo;
for f = {'package', 'install', 'run', 'uninstall'}
    try
        feval(['matlab.apputil.' f{1}], fullfile(share, 'nothere.xyz'));
    catch e
        fprintf('%s(missing) -> %s: %s\n', f{1}, e.identifier, short(e.message));
    end
end
try
    matlab.apputil.package(fullfile(share, 'AcceptForm.mlapp'));
catch e
    fprintf('package(mlapp) -> %s: %s\n', e.identifier, short(e.message));
end
w = warning('off', 'backtrace');
[msg, id] = lastwarn('');
try
    matlab.apputil.create(fullfile(share, 'nothere.prj'));
catch e
    fprintf('create(missing) -> %s: %s\n', e.identifier, short(e.message));
end
[msg, id] = lastwarn;
fprintf('create warned: %s: %s\n', id, msg);
warning(w);
fprintf('done\n');
