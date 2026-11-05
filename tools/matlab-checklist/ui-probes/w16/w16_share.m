% w16_share - the acceptance apps shared (W16), in two stages run as two processes:
%   package: each app packaged as a MATLAB App with the statements Share > MATLAB App's script runs
%            (ToolboxOptions, packageToolbox), and W16Blank built into a standalone program with the statements
%            Share > Standalone Desktop App's script runs (StandaloneApplicationOptions, standaloneWindowsApplication);
%   install: each package installed, listed, resolved and run from the install, and uninstalled.
% W16_STAGE names the stage ('package', 'install'; empty runs both). w16_share_package.m and w16_share_install.m
% set it and call this file, so that each stage has an output of its own, in R2025b's -batch and in JGraph's:
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name w16_share_package
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name w16_share_install
%   set JGRAPH_ADDONS=<this folder>\rt\addons & set W16_RELEASE=release-jgraph
%   ..\..\..\..\src\JGraph.Cli\bin\Debug\net8.0\jgraph.exe -batch w16_share_package.m > w16_share_package.jgraph.out
%   ..\..\..\..\src\JGraph.Cli\bin\Debug\net8.0\jgraph.exe -batch w16_share_install.m > w16_share_install.jgraph.out
% Each run packages under a fresh toolbox name, <Name>_<run>: R2025b's add-on manager, asked to install a toolbox
% into a folder (Toolboxes\<ToolboxName>) it once held and uninstalled, answers "Unable to find add-on with
% specified identifier and version" with the files copied and no record made, whatever the identifier and
% whether or not the process packaged it; a name it never held installs. The run's tag is written to
% <release>\run.txt by the package stage and hidden behind <run> in both stages' lines. R2025b installs into its
% own add-ons folder and uninstalls again; JGraph into the folder JGRAPH_ADDONS names. The program is run by
% tools\runtime-checks\run-w16designer.ps1 -Runtime.
here = fileparts(mfilename('fullpath'));
root = fullfile(here, 'rt', 'accept');
% Each engine's output under its own folder (W16_RELEASE names it; 'release' by default), so that the two runs
% can overlap; the token <release> stands for either.
releaseName = getenv('W16_RELEASE');
if isempty(releaseName), releaseName = 'release'; end
release = fullfile(root, releaseName);
stage = getenv('W16_STAGE');
addpath(root);
runFile = fullfile(release, 'run.txt');
if strcmp(stage, 'install')
    runTag = strtrim(fileread(runFile));
else
    rng('shuffle');
    runTag = sprintf('%04x%04x', randi([0 65535], 1, 2));
end
short = @(s) strrep(strrep(strrep(char(s), release, '<release>'), root, '<accept>'), runTag, '<run>');
try, beep('off'); catch, end %#ok<CTCH>
apps = {'W16Blank', 'A blank app from the Start Page.', {}; ...
        'W16Panels', 'A 2-Panel app with auto-reflow.', {}; ...
        'W16Composite', 'An app using a custom component.', ...
            {fullfile(root, 'W16Counter.mlapp'), fullfile(root, 'resources', 'appDesigner.json')}};

if ~strcmp(stage, 'install')
    if exist(release, 'dir'), rmdir(release, 's'); end
    mkdir(release);
    fid = fopen(runFile, 'w'); fprintf(fid, '%s', runTag); fclose(fid);
    % Each package gets a fresh identifier, as the share window gives one.
    freshGuid = @() sprintf('%04x%04x-%04x-%04x-%04x-%04x%04x%04x', randi([0 65535], 1, 8));
    for k = 1:size(apps, 1)
        name = apps{k, 1};
        app = fullfile(root, [name '.mlapp']);
        fprintf('== %s packaged as a MATLAB App\n', name);
        try
            files = [{app}, apps{k, 3}];
            packageOpts = matlab.addons.toolbox.ToolboxOptions(root, freshGuid(), ToolboxFiles=files);
            packageOpts.ToolboxName = [name '_' runTag];
            packageOpts.ToolboxVersion = "1.0";
            packageOpts.AuthorName = "W16";
            packageOpts.Summary = apps{k, 2};
            packageOpts.Description = "The App Designer window plan's acceptance set.";
            packageOpts.AppGalleryFiles = {app};
            packageOpts.ToolboxMatlabPath = {root};
            packageOpts.OutputFile = fullfile(release, [name '.mltbx']);
            matlab.addons.toolbox.packageToolbox(packageOpts);
            fprintf('packaged: %s exists %d, version %s\n', short(packageOpts.OutputFile), isfile(packageOpts.OutputFile), ...
                matlab.addons.toolbox.toolboxVersion(char(packageOpts.OutputFile)));
        catch e
            fprintf('-> %s: %s\n', e.identifier, short(strtok(e.message, newline)));
        end
    end

    % --- Standalone Desktop App: W16Blank built as the share window's script builds it ----------------------
    fprintf('== W16Blank as a standalone program\n');
    try
        buildOpts = compiler.build.StandaloneApplicationOptions(fullfile(root, 'W16Blank.mlapp'));
        buildOpts.OutputDir = fullfile(release, 'build');
        buildOpts.Verbose = false;
        buildOpts.ExecutableVersion = "1.0";
        buildResult = compiler.build.standaloneWindowsApplication(buildOpts);
        files = sort(cellfun(@(f) short(f), cellstr(buildResult.Files), 'UniformOutput', false));
        fprintf('built: %s\n', strjoin(strrep(files, '\', '/'), ', '));
        fprintf('program exists: %d\n', isfile(fullfile(release, 'build', 'W16Blank.exe')));
    catch e
        fprintf('-> %s: %s\n', e.identifier, short(strtok(e.message, newline)));
    end
end

if ~strcmp(stage, 'package')
    % The add-ons folder, to read where an installed app resolves: JGRAPH_ADDONS names JGraph's; R2025b's is asked for.
    addonsRoot = getenv('JGRAPH_ADDONS');
    if isempty(addonsRoot)
        try, addonsRoot = char(matlab.internal.addons.util.retrieveAddOnsInstallationFolder); catch, addonsRoot = '<none>'; end %#ok<CTCH>
    end
    % The add-on manager primed by a listing first: in a fresh R2025b process, installToolbox without one answers
    % "Unable to find add-on with specified identifier and version" (the files copied, no record made; measured
    % over eight runs, with and without a fresh identifier, name, folder and process), and with one it installs.
    primed = matlab.addons.installedAddons; %#ok<NASGU>
    % A W16 toolbox left installed by an earlier run goes first (with none installed, R2025b's list is a struct
    % without fields, so the names are read only when there is something to read).
    left = matlab.addons.toolbox.installedToolboxes;
    if ~isempty(left)
        for k = find(startsWith({left.Name}, 'W16'))
            matlab.addons.toolbox.uninstallToolbox(left(k));
        end
    end
    for k = 1:size(apps, 1)
        name = apps{k, 1};
        fprintf('== %s installed as a MATLAB App\n', name);
        try
            file = fullfile(release, [name '.mltbx']);
            info = matlab.addons.toolbox.installToolbox(file);
            fprintf('installed: %s %s\n', short(info.Name), info.Version);
            listed = matlab.addons.toolbox.installedToolboxes;
            fprintf('installedToolboxes lists it: %d; apputil lists apps: %d (a toolbox''s app is the gallery''s, not apputil''s)\n', ...
                sum(strcmp({listed.Name}, [name '_' runTag])), numel(matlab.apputil.getInstalledAppInfo));
            rmpath(root); % the name must now resolve under the add-ons folder
            where = which(name);
            [~, whichName, whichExt] = fileparts(where);
            fprintf('resolves to %s%s under the add-ons Toolboxes folder: %d\n', whichName, whichExt, contains(where, fullfile(addonsRoot, 'Toolboxes')));
            a = feval(name);
            fprintf('runs from the install: class %s\n', class(a));
            delete(a);
            addpath(root);
            matlab.addons.toolbox.uninstallToolbox(info);
            listed = matlab.addons.toolbox.installedToolboxes;
            [~, whichName, whichExt] = fileparts(which(name));
            fprintf('after uninstall: installedToolboxes is a %s %s with %d field(s), resolves to %s%s under the add-ons Toolboxes folder: %d\n', ...
                class(listed), mat2str(size(listed)), numel(fieldnames(listed)), whichName, whichExt, contains(which(name), fullfile(addonsRoot, 'Toolboxes')));
        catch e
            fprintf('-> %s: %s\n', e.identifier, short(strtok(e.message, newline)));
            fprintf('%s\n', short(getReport(e, 'extended', 'hyperlinks', 'off')));
        end
    end
end
fprintf('done\n');
