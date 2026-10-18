% DS1b (designer plan): App Designer's full load of each tree JGraph's DesignTreeWriter wrote
% (rt/jgraph/<App>__x.mat), against the same for the tree the app's code makes (ds1b_reference), so
% that what App Designer and MATLAB do to any tree is not counted against JGraph's.
% Each tree is saved as an .mlapp by App Designer's own serializer (MLAPPSerializer, as App Designer
% saves) with the app's class text and callbacks, and opened by App Designer's own reader
% (DeserializerFactory ... getAppData), which runs its loaders: images set from
% DesignTimeProperties.ImageRelativePath, callbacks bound to components, and so on. What is checked:
% every component keeps its code name; App Designer's load changes nothing but what its loaders set
% (each component against the tree before the round trip, through ds1b_objdiff); every callback in
% the class is bound to a component; R2025b's generator, run on the loaded components as App
% Designer runs it, gives each component's stored ComponentCode; App Designer reports no warnings.
% One line per app: same (and how many findings the code's own tree shares), or the findings only
% JGraph's tree has. Headless; rt/ad (ignored) holds the files, images copied beside them.
here = fileparts(mfilename('fullpath'));
addpath(here);
folder = fullfile(here, 'rt', 'jgraph');
files = dir(fullfile(folder, '*__x.mat'));
only = getenv('DS1B_ONLY');
tally = struct('same', 0, 'differs', 0, 'failed', 0);
for k = 1:numel(files)
    app = extractBefore(files(k).name, '__x.mat');
    if ~isempty(only) && ~strcmp(only, app), continue; end
    src = ds1b_appsource(app);
    previous = cd(src.folder); % where a file the app names without a path is found
    restore = onCleanup(@() cd(previous));
    try
        S = load(fullfile(folder, files(k).name));
        P = load(fullfile(folder, files(k).name));
        [found, components, bound] = roundTrip(app, S.components.UIFigure, P.components.UIFigure, src, fullfile(here, 'rt', 'ad'));
        delete(P.components.UIFigure);
        [ref, why] = ds1b_reference(src.method, src.folder, src.appType);
        if isempty(ref)
            control = {}; note = sprintf(' (no control: %s)', why);
        else
            [plainRef, ~] = ds1b_reference(src.method, src.folder, src.appType);
            control = roundTrip(app, ref, plainRef, src, fullfile(here, 'rt', 'ad'));
            delete(plainRef);
            note = '';
        end
    catch e
        fprintf('%s: failed %s: %s\n', app, e.identifier, strtok(e.message, newline));
        tally.failed = tally.failed + 1;
        continue
    end
    clear restore
    own = setdiff(found, control, 'stable');
    shared = numel(intersect(found, control));
    if isempty(own)
        fprintf('%s: same (%d components, %d callbacks bound; %d findings shared with the code''s own tree)%s\n', app, components, bound, shared, note);
        tally.same = tally.same + 1;
    else
        fprintf('%s: differs in %s%s\n', app, strjoin(own, ', '), note);
        tally.differs = tally.differs + 1;
    end
end
disp(tally)

function [problems, components, bound] = roundTrip(app, fig, plainFig, src, out)
    % Saves fig as an .mlapp with App Designer's serializer, opens it with App Designer's reader,
    % and lists what differs from plainFig (the same tree, kept apart) and what App Designer reports.
    if ~isfolder(out), mkdir(out); end
    adapters = appdesigner.internal.appmetadata.getProductionComponentAdapterMap();
    gen = appdesigner.internal.codegeneration.ComponentCodeGenerator();
    problems = {};
    plain = byCodeName(plainFig);
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
    fn = fullfile(out, [app '.mlapp']);
    s = appdesigner.internal.serialization.MLAPPSerializer(fn, fig);
    s.OverwriteTargetFile = true;
    s.MatlabCodeText = src.classText;
    s.Callbacks = struct('Name', callbacks, 'Code', {{}});
    if contains(src.classText, 'function startupFcn(app')
        s.StartupCallback = struct('Name', 'startupFcn', 'Code', {{}});
    end
    s.save();
    delete(fig);

    des = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fn);
    a = des.getAppData();
    got = byCodeName(a.components.UIFigure);
    components = numel(keys(got));
    missing = setdiff(keys(plain), keys(got)); extra = setdiff(keys(got), keys(plain));
    if ~isempty(missing), problems{end + 1} = ['lost ' strjoin(missing, ' ')]; end
    if ~isempty(extra), problems{end + 1} = ['gained ' strjoin(extra, ' ')]; end
    for name = intersect(keys(got), keys(plain))
        h = got(name{1});
        problems = [problems, strcat(name{1}, ':', ds1b_objdiff(h, plain(name{1})))]; %#ok<AGROW>
        code = gen.getComponentGenerationCode(h, eval(adapters(class(h))), src.appType, 'light');
        if ~isequal(code(:), h.DesignTimeProperties.ComponentCode(:))
            k1 = find(~ismember(code(:), h.DesignTimeProperties.ComponentCode(:)), 1);
            if isempty(k1), detail = 'order'; else, detail = code{k1}; end
            problems{end + 1} = sprintf('%s: stored code is not the generator''s (%s)', name{1}, detail); %#ok<AGROW>
        end
    end
    bound = 0;
    if isfield(a.code, 'Callbacks')
        for c = 1:numel(a.code.Callbacks)
            if isempty(a.code.Callbacks(c).ComponentData)
                problems{end + 1} = sprintf('callback %s bound to nothing', a.code.Callbacks(c).Name); %#ok<AGROW>
            else
                bound = bound + 1;
            end
        end
    end
    if ~isempty(des.Warnings)
        problems{end + 1} = ['warnings ' strjoin(arrayfun(@(x) char(string(x.Id)), des.Warnings, 'UniformOutput', false), ',')];
    end
    delete(a.components.UIFigure);
end

function m = byCodeName(fig)
    m = containers.Map();
    for h = findall(fig, '-property', 'DesignTimeProperties')'
        m(h.DesignTimeProperties.CodeName) = h;
    end
end
