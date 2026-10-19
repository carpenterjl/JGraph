% DS6 (designer plan): R2025b's App Designer reading the files JGraph's save wrote, headless, through
% App Designer's own reader (DeserializerFactory ... getAppData, which runs its loaders). The files
% come from the local tests AppDesignerModelSaveTests, run with JGRAPH_DS6_OUT naming rt/.
%
% Part 1, rt/edits/<k>_<App>/: a shipped app as R2025b ships it (__orig) and as JGraph saved it
% after an edit to its code (__edit): one component's Text changed, a label removed, a button
% JGraphAddedButton added to the figure (edits.txt names them). Every component the edit did not
% touch must load with every property as the original's (ds1b_objdiff, hidden properties
% included): the decision of 2026-10-18 is that a save keeps the old values. The changed component
% may differ in its Text and its stored code alone, its parent and the figure in their children;
% the removed one is gone; the added one is a button with its text and place. The stored code of
% the changed and the added component must be R2025b's generator's for the loaded component; the
% code copy reads with the same class and as many callbacks bound; no warning the original lacks.
%
% Part 2, rt/new/<k>_<App>/<App>.mlapp: apps JGraph wrote whole (an R2025b app's code, the
% fixture DesignerSpecApp, a blank new app). Each is opened as it is - no serializer in between -
% and held against the tree the app's code makes (ds1b_reference), which is also saved by App
% Designer's serializer and opened the same way as a control: only findings the control lacks are
% JGraph's. Every code name, every component as the code makes it, stored code = generator's,
% every callback bound, no warnings.
%
% One line per app, then a tally. rt/ is ignored; MathWorks' files are read in place.
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, '..', 'ds1b'));
adapters = appdesigner.internal.appmetadata.getProductionComponentAdapterMap();
gen = appdesigner.internal.codegeneration.ComponentCodeGenerator();
only = getenv('DS6_ONLY');
part = getenv('DS6_PART'); % 'edits', 'new', or empty for both
tally = struct('edited', 0, 'editedAsSaved', 0, 'new', 0, 'newSame', 0, 'failed', 0);

% --- Part 1: edits -------------------------------------------------------------------------------
edits = dir(fullfile(here, 'rt', 'edits'));
if strcmp(part, 'new'), edits = edits([]); end
for d = edits'
    if ~d.isdir || isempty(regexp(d.name, '^\d+_', 'once')), continue; end % <k>_<App> folders only
    app = regexprep(d.name, '^\d+_', '');
    if ~isempty(only) && ~contains(only, app), continue; end
    folder = fullfile(d.folder, d.name);
    e = readEdits(fullfile(folder, 'edits.txt'));
    try
        [O, oWarn, oType] = openApp(fullfile(folder, [app '__orig.mlapp']));
        [E, eWarn, eType] = openApp(fullfile(folder, [app '__edit.mlapp']));
    catch err
        fprintf('edit %s: failed %s: %s\n', app, err.identifier, strtok(err.message, newline));
        tally.failed = tally.failed + 1;
        continue
    end
    tally.edited = tally.edited + 1;
    mo = byCodeName(O.components.UIFigure);
    me = byCodeName(E.components.UIFigure);
    problems = {};
    parents = {codeNameOf(O.components.UIFigure)};
    if ~isempty(e.removed) && isKey(mo, e.removed)
        parents{end + 1} = codeNameOf(mo(e.removed).Parent);
    end
    untouched = 0;
    for name = keys(mo)
        n = name{1};
        if strcmp(n, e.removed)
            if isKey(me, n), problems{end + 1} = [n ' is still there']; end %#ok<AGROW>
            continue
        end
        if ~isKey(me, n), problems{end + 1} = [n ' is gone']; continue; end %#ok<AGROW>
        diffs = ds1b_objdiff(me(n), mo(n));
        if strcmp(n, e.changed)
            diffs = diffs(cellfun(@isempty, regexp(diffs, '^\w+\.(Text|DesignTimeProperties\.ComponentCode)', 'once')));
            if ~isequal(char(string(me(n).Text)), 'Edited in JGraph')
                problems{end + 1} = sprintf('%s.Text is %s', n, char(join(string(me(n).Text)))); %#ok<AGROW>
            end
            problems = [problems, generatorCheck(me(n), n, adapters, gen, eType)]; %#ok<AGROW>
        elseif any(strcmp(n, parents))
            diffs = diffs(cellfun(@isempty, regexp(diffs, '^\w+\.(Children|Children_I|NodeChildren)=', 'once')));
        else
            untouched = untouched + isempty(diffs);
        end
        problems = [problems, strcat(n, ':', diffs)]; %#ok<AGROW>
    end
    if ~isKey(me, 'JGraphAddedButton')
        problems{end + 1} = 'JGraphAddedButton is missing';
    else
        b = me('JGraphAddedButton');
        if ~isa(b, 'matlab.ui.control.Button') || ~isequal(b.Text, 'Added') || ~isequal(b.Position, [10 10 100 22]) || b.Parent ~= E.components.UIFigure
            problems{end + 1} = sprintf('JGraphAddedButton is a %s with Text %s at %s', class(b), char(join(string(b.Text))), mat2str(b.Position));
        end
        problems = [problems, generatorCheck(b, 'JGraphAddedButton', adapters, gen, eType)]; %#ok<AGROW>
    end
    if ~strcmp(E.code.ClassName, O.code.ClassName)
        problems{end + 1} = sprintf('class %s (was %s)', E.code.ClassName, O.code.ClassName);
    end
    if bound(E) ~= bound(O)
        problems{end + 1} = sprintf('%d callbacks bound (was %d)', bound(E), bound(O));
    end
    newWarnings = setdiff(eWarn, oWarn);
    if ~isempty(newWarnings), problems{end + 1} = ['warnings ' strjoin(newWarnings, ',')]; end
    if isempty(problems)
        tally.editedAsSaved = tally.editedAsSaved + 1;
        fprintf('edit %s: as saved (%d untouched components load as the original''s; %s changed, %s removed, a button added)\n', ...
            app, untouched, orNone(e.changed), orNone(e.removed));
    else
        fprintf('edit %s: %s\n', app, strjoin(problems, '; '));
    end
    delete(O.components.UIFigure);
    delete(E.components.UIFigure);
end

% --- Part 2: new apps ----------------------------------------------------------------------------
news = dir(fullfile(here, 'rt', 'new'));
news = news([news.isdir] & ~cellfun(@isempty, regexp({news.name}, '^\d+_', 'once')));
if strcmp(part, 'edits'), news = news([]); end
% DS6_SHARD 'i/n' takes every n-th app from the i-th, so that n runs share the part.
shard = sscanf(getenv('DS6_SHARD'), '%d/%d');
if numel(shard) == 2, news = news(shard(1):shard(2):end); end
for d = news'
    if ~d.isdir || isempty(regexp(d.name, '^\d+_', 'once')), continue; end % <k>_<App> folders only
    app = regexprep(d.name, '^\d+_', '');
    if ~isempty(only) && ~contains(only, app), continue; end
    folder = fullfile(d.folder, d.name);
    fn = fullfile(folder, [app '.mlapp']);
    try
        text = matlab.internal.getCode(fn);
        [srcFolder, appType] = sourceOf(app, here);
        copyImages(text, srcFolder, folder);
        % Opened from the app's own folder, where an image or page the code names without a path
        % is found, as the control's tree is made there.
        home = cd(srcFolder);
        back = onCleanup(@() cd(home));
        [A, warnings] = openApp(fn);
        % The control is createComponents as App Designer's generator writes it (left by the test),
        % so that what the app's own code sets otherwise - a default stated, a one-element cell for a
        % label's text - is not counted against the tree.
        method = fileread(fullfile(folder, [app '__code.m']));
        [plain, why] = ds1b_reference(method, srcFolder, appType);
        if isempty(plain)
            fprintf('new %s: opens (%d components); no control: %s\n', app, numel(keys(byCodeName(A.components.UIFigure))), why);
            delete(A.components.UIFigure);
            tally.new = tally.new + 1;
            continue
        end
        found = findings(A, plain, adapters, gen, appType, warnings);
        % The control: the code's own tree through App Designer's serializer and reader.
        [ref, ~] = ds1b_reference(method, srcFolder, appType);
        [plainRef, ~] = ds1b_reference(method, srcFolder, appType);
        controlFolder = fullfile(here, 'rt', 'control', d.name);
        if ~isfolder(controlFolder), mkdir(controlFolder); end
        copyImages(text, srcFolder, controlFolder);
        cfn = fullfile(controlFolder, [app '.mlapp']);
        % The control lists the callbacks JGraph's copy of the code lists - every function of the
        % callbacks block, as App Designer's own file does - so that one no component uses is
        % bound to nothing in both.
        callbacks = {};
        if isfield(A.code, 'Callbacks'), callbacks = {A.code.Callbacks.Name}; end
        s = appdesigner.internal.serialization.MLAPPSerializer(cfn, ref);
        s.OverwriteTargetFile = true;
        s.MatlabCodeText = text;
        s.Callbacks = struct('Name', callbacks, 'Code', {{}});
        if contains(text, 'function startupFcn(app')
            s.StartupCallback = struct('Name', 'startupFcn', 'Code', {{}});
        end
        if strcmp(appType, 'Responsive')
            % saved as App Designer saves a responsive app, so that its validator reads the control too
            meta = s.Metadata;
            meta.AppType = 'Responsive';
            meta.MinimumSupportedMATLABRelease = 'R2019a';
            s.Metadata = meta;
        end
        s.save();
        delete(ref);
        [C, cWarnings] = openApp(cfn);
        control = findings(C, plainRef, adapters, gen, appType, cWarnings);
        components = numel(keys(byCodeName(A.components.UIFigure)));
        delete(A.components.UIFigure); delete(C.components.UIFigure); delete(plain); delete(plainRef);
        clear back
    catch err
        fprintf('new %s: failed %s: %s\n', app, err.identifier, strtok(err.message, newline));
        tally.failed = tally.failed + 1;
        continue
    end
    tally.new = tally.new + 1;
    own = setdiff(found, control, 'stable');
    if isempty(own)
        tally.newSame = tally.newSame + 1;
        fprintf('new %s: same (%d components; %d findings shared with the control)\n', app, components, numel(intersect(found, control)));
    else
        fprintf('new %s: differs in %s\n', app, strjoin(own, ', '));
    end
end
disp(tally)

% --- Helpers -------------------------------------------------------------------------------------
function [a, warnings, appType] = openApp(fn)
    des = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fn);
    a = des.getAppData();
    warnings = {};
    if ~isempty(des.Warnings)
        warnings = arrayfun(@(x) char(string(x.Id)), des.Warnings, 'UniformOutput', false);
    end
    meta = des.getAppMetadata();
    appType = 'Standard'; if isfield(meta, 'AppType'), appType = char(meta.AppType); end
end

function problems = findings(a, plainFig, adapters, gen, appType, warnings)
    % What differs between the loaded app a and the code's own tree, and what App Designer reports.
    problems = {};
    plain = byCodeName(plainFig);
    got = byCodeName(a.components.UIFigure);
    missing = setdiff(keys(plain), keys(got)); extra = setdiff(keys(got), keys(plain));
    if ~isempty(missing), problems{end + 1} = ['lost ' strjoin(missing, ' ')]; end
    if ~isempty(extra), problems{end + 1} = ['gained ' strjoin(extra, ' ')]; end
    for name = intersect(keys(got), keys(plain))
        h = got(name{1});
        diffs = ds1b_objdiff(h, plain(name{1}));
        if isprop(h, 'Layout') && isa(h.Layout, 'matlab.ui.layout.GridLayoutOptions')
            % A grid child's place is the grid's, which App Designer's reader does not lay out.
            diffs = diffs(cellfun(@isempty, regexp(diffs, ...
                '^\w+\.(Position|InnerPosition|OuterPosition|PlotBoxAspectRatio|CurrentPoint)(_I|Mode)?=', 'once')));
        end
        % An image's full path in the stored code is where each file was saved; the generator
        % check below holds the tree's own path against the loaded image.
        diffs = diffs(cellfun(@isempty, regexp(diffs, 'ComponentCode (ref )?has app\.ad_CODENAME_ad\.(ImageSource|Icon) = ''', 'once')));
        problems = [problems, strcat(name{1}, ':', diffs)]; %#ok<AGROW>
        problems = [problems, generatorCheck(h, name{1}, adapters, gen, appType)]; %#ok<AGROW>
    end
    if isfield(a.code, 'Callbacks')
        for c = 1:numel(a.code.Callbacks)
            if isempty(a.code.Callbacks(c).ComponentData)
                problems{end + 1} = sprintf('callback %s bound to nothing', a.code.Callbacks(c).Name); %#ok<AGROW>
            end
        end
    end
    if ~isempty(warnings), problems{end + 1} = ['warnings ' strjoin(warnings, ',')]; end
end

function problems = generatorCheck(h, name, adapters, gen, appType)
    problems = {};
    code = gen.getComponentGenerationCode(h, eval(adapters(class(h))), appType, 'light');
    stored = h.DesignTimeProperties.ComponentCode;
    if ~isequal(code(:), stored(:))
        k = find(~ismember(code(:), stored(:)), 1);
        if isempty(k), detail = 'order'; else, detail = code{k}; end
        problems{end + 1} = sprintf('%s: stored code is not the generator''s (%s)', name, detail);
    end
end

function n = bound(a)
    n = 0;
    if isfield(a.code, 'Callbacks')
        n = sum(arrayfun(@(c) ~isempty(c.ComponentData), a.code.Callbacks));
    end
end

function m = byCodeName(fig)
    m = containers.Map();
    for h = findall(fig, '-property', 'DesignTimeProperties')'
        m(h.DesignTimeProperties.CodeName) = h;
    end
end

function n = codeNameOf(h)
    n = '';
    if isprop(h, 'DesignTimeProperties'), n = h.DesignTimeProperties.CodeName; end
end

function e = readEdits(fn)
    e = struct('changed', '', 'removed', '');
    for line = splitlines(strtrim(fileread(fn)))'
        parts = split(strtrim(line{1}));
        if numel(parts) >= 2 && any(strcmp(parts{1}, {'changed', 'removed'}))
            e.(parts{1}) = parts{2};
        end
    end
end

function s = orNone(t)
    if isempty(t), s = 'nothing'; else, s = t; end
end

function [folder, appType] = sourceOf(app, here)
    % Where the app's images are found from, and its type.
    appType = 'Standard';
    % JGraph's own apps (PaletteApp: every library entry added in the designer, DS3; GridApp:
    % grids laid out, nested, spanned and given rows in the designer, DS4; ResponsiveApp: a new
    % 3-panel app with components in its regions and a splitter dragged, DS4b).
    if strcmp(app, 'ResponsiveApp'), appType = 'Responsive'; end
    if any(strcmp(app, {'DesignerSpecApp', 'NewApp', 'PaletteApp', 'GridApp', 'ResponsiveApp'}))
        folder = fullfile(here, '..', 'ds1b', 'rt', 'spec');
        if ~isfolder(fullfile(folder, 'img')), mkdir(fullfile(folder, 'img')); end
        if ~isfile(fullfile(folder, 'img', 'logo.png'))
            imwrite(uint8(255 * rand(8, 8, 3)), fullfile(folder, 'img', 'logo.png'));
        end
        return
    end
    shipped = dir(fullfile(matlabroot, 'toolbox', '**', [app '.mlapp']));
    folder = shipped(1).folder;
    meta = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fullfile(folder, shipped(1).name)).getAppMetadata();
    if isfield(meta, 'AppType'), appType = char(meta.AppType); end
end

function copyImages(text, from, to)
    % The images the code names beside the app, copied beside the app checked.
    for t = regexp(text, 'fullfile\(pathToMLAPP, ([^)]*)\)', 'tokens')
        parts = eval(['{' t{1}{1} '}']);
        source = fullfile(from, parts{:});
        target = fullfile(to, parts{:});
        if isfile(source) && ~isfile(target)
            if ~isfolder(fileparts(target)), mkdir(fileparts(target)); end
            copyfile(source, target);
        end
    end
end
