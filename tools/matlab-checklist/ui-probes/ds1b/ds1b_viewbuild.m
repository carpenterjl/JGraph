% DS1b (designer plan): the .mlapp files the App Designer window check (ds1b_view.ps1) opens. For
% each tree JGraph's DesignTreeWriter wrote (rt/jgraph/<App>__x.mat) it writes
% rt/view/jgraph/<App>.mlapp, and for the tree the app's own createComponents makes
% (ds1b_reference) rt/view/code/<App>.mlapp, the control. Both are saved by App Designer's own
% serializer (MLAPPSerializer) with the app's class text and callbacks, as ds1b_appdesigner does, so
% the two differ only in their component tree; images are copied beside them. Note the serializer
% saves the objects JGraph's file loads as: the component bytes in the .mlapp are R2025b's, not
% JGraph's (the rest of appModel.mat is DS6). An app already written is skipped; delete rt/view to
% write them all again. Headless; rt/view is ignored.
here = fileparts(mfilename('fullpath'));
addpath(here);
folder = fullfile(here, 'rt', 'jgraph');
files = dir(fullfile(folder, '*__x.mat'));
out = fullfile(here, 'rt', 'view');
for k = 1:numel(files)
    app = extractBefore(files(k).name, '__x.mat');
    if isfile(fullfile(out, 'jgraph', [app '.mlapp'])), continue; end % written by an earlier run
    src = ds1b_appsource(app);
    previous = cd(src.folder); % where a file the app names without a path is found
    restore = onCleanup(@() cd(previous));
    try
        S = load(fullfile(folder, files(k).name));
        write(app, S.components.UIFigure, src, fullfile(out, 'jgraph'));
        [ref, why] = ds1b_reference(src.method, src.folder, src.appType);
        if isempty(ref)
            note = sprintf(' (no control: %s)', why);
        else
            write(app, ref, src, fullfile(out, 'code'));
            note = '';
        end
        fprintf('%s: written%s\n', app, note);
    catch e
        fprintf('%s: failed %s: %s\n', app, e.identifier, strtok(e.message, newline));
    end
    clear restore
end

function write(app, fig, src, out)
    if ~isfolder(out), mkdir(out); end
    for h = findall(fig, '-property', 'DesignTimeProperties')'
        d = h.DesignTimeProperties;
        if isfield(d, 'ImageRelativePath') && ~isempty(d.ImageRelativePath)
            target = fullfile(out, d.ImageRelativePath);
            if ~isfolder(fileparts(target)), mkdir(fileparts(target)); end
            copyfile(fullfile(src.folder, d.ImageRelativePath), target);
        end
    end
    callbacks = regexp(src.classText, 'createCallbackFcn\(app, @(\w+)', 'tokens');
    callbacks = unique(cellfun(@(c) c{1}, callbacks, 'UniformOutput', false));
    s = appdesigner.internal.serialization.MLAPPSerializer(fullfile(out, [app '.mlapp']), fig);
    s.OverwriteTargetFile = true;
    s.MatlabCodeText = src.classText;
    s.Callbacks = struct('Name', callbacks, 'Code', {{}});
    if contains(src.classText, 'function startupFcn(app')
        s.StartupCallback = struct('Name', 'startupFcn', 'Code', {{}});
    end
    s.save();
    delete(fig);
end
