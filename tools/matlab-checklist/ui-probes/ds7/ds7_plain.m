% DS7a (designer plan): R2025b reading and running the plain-text (.m) apps JGraph's designer writes,
% headless. The files come from the local test AppDesignerPlainTextTests.TheProbesAppsAreWrittenWhenAsked,
% run with JGRAPH_DS7_OUT naming ds7/rt: rt/plain/<k>_<App>/<App>.m (the plain-text app), <App>.mlapp
% (the same design as a package, the control) and run.txt (the constructor's arguments).
%
% Part 1, with the feature AppDesignerPlainTextFileFormat as it is (off by default): App Designer's
% own reader (DeserializerFactory ... getAppData, which R2025b does not gate) opens the .m and the
% .mlapp, and the two trees are held against each other by code name (ds1b_objdiff; what a tree
% carries for App Designer alone - DesignTimeProperties - is not counted), with the code data: class
% name, callbacks, startup function, input parameters, single instance, the editable section.
%
% Part 2, with the feature switched on for this session only: the app runs from its .m through
% matlab.apps.App - the components by their code names, each callback the code names bound as a
% function handle and called once, a second construction of a single-instance app answering the
% running one - and delete(app) takes its figure away.
%
% One line per app and part, then a tally. rt/ is ignored.
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, '..', 'ds1b'));
root = fullfile(here, 'rt', 'plain');
only = getenv('DS7_ONLY');
flag0 = feature('AppDesignerPlainTextFileFormat');
restoreFeature = onCleanup(@() feature('AppDesignerPlainTextFileFormat', flag0));
fprintf('feature AppDesignerPlainTextFileFormat: %d\n', flag0);
tally = struct('apps', 0, 'same', 0, 'differs', 0, 'runs', 0, 'failed', 0);
folders = dir(root);
for d = folders'
    if ~d.isdir || isempty(regexp(d.name, '^\d+_', 'once')), continue; end
    app = regexprep(d.name, '^\d+_', '');
    if ~isempty(only) && ~strcmp(only, app), continue; end
    folder = fullfile(d.folder, d.name);
    tally.apps = tally.apps + 1;
    previous = cd(folder);
    restore = onCleanup(@() cd(previous));
    addpath(folder);

    % --- Part 1: the loader, feature off --------------------------------------------------------
    feature('AppDesignerPlainTextFileFormat', 0);
    loaded = [];
    try
        [B, ~] = openApp(fullfile(folder, [app '.mlapp']));
        loaded = B.components.UIFigure; % the control's tree names the components the run must make
    catch e
        fprintf('%s: the .mlapp control failed to load %s: %s\n', app, e.identifier, strtok(e.message, newline));
        B = [];
    end
    try
        [M, mWarn] = openApp(fullfile(folder, [app '.m']));
        if isempty(B)
            fprintf('%s: loads (%d components) but has no control\n', app, numel(findall(M.components.UIFigure, '-property', 'DesignTimeProperties')));
            tally.differs = tally.differs + 1;
        else
            problems = compareTrees(M.components.UIFigure, B.components.UIFigure);
            problems = [problems compareCode(M.code, B.code)]; %#ok<AGROW>
            if ~isempty(mWarn), problems{end + 1} = sprintf('warnings: %s', strjoin(mWarn, ', ')); end %#ok<AGROW>
            n = numel(findall(M.components.UIFigure, '-property', 'DesignTimeProperties'));
            if isempty(problems)
                fprintf('%s: loads the same as its .mlapp (%d components, %d callbacks)\n', app, n, numel(M.code.Callbacks));
                tally.same = tally.same + 1;
            else
                fprintf('%s: loads with differences: %s\n', app, strjoin(problems, '; '));
                tally.differs = tally.differs + 1;
            end
        end
        delete(M.components.UIFigure);
    catch e
        if strcmp(e.identifier, 'MATLAB:maxrhs')
            % PlainTextCodeParser.buildCallbackData hands strcmp a comma-separated list when a component
            % binds two callbacks (R2025b's own defect, which its own files meet too).
            fprintf('%s: R2025b''s plain-text parser cannot load it: a component bound to two callbacks (%s)\n', app, e.identifier);
        else
            fprintf('%s: load failed %s: %s\n', app, e.identifier, strrep(strtrim(e.message), newline, ' | '));
            for s = 1:min(4, numel(e.stack)), fprintf('   at %s:%d\n', e.stack(s).name, e.stack(s).line); end
        end
        tally.failed = tally.failed + 1;
    end

    % --- Part 2: running, feature on ------------------------------------------------------------
    % The .m is run from a folder holding it alone: beside its .mlapp, MATLAB resolves the name
    % to the .mlapp (U7), and the run would be the control's, not the plain-text file's.
    feature('AppDesignerPlainTextFileFormat', 1);
    args = readArgs(fullfile(folder, 'run.txt'));
    runFolder = fullfile(tempname, app);
    mkdir(runFolder);
    copyfile(fullfile(folder, [app '.m']), runFolder);
    rmpath(folder);
    addpath(runFolder);
    cd(runFolder);
    restoreRun = onCleanup(@() cleanupRun(previous, runFolder));
    try
        inst = feval(app, args{:});
        if ~strcmp(which(app), fullfile(runFolder, [app '.m']))
            error('ds7:control', 'the run resolved %s, not the plain-text file', which(app));
        end
        figs = findall(groot, 'Type', 'figure', '-property', 'RunningAppInstance');
        fig = figs(arrayfun(@(f) isequal(f.RunningAppInstance, inst), figs));
        checks = {sprintf('%s with %d running-app figure(s)', class(inst), numel(fig))};
        % The components are the app's own properties, named by their code names.
        wanted = {};
        if ~isempty(loaded)
            wanted = names(findall(loaded, '-property', 'DesignTimeProperties'), 'DesignTimeProperties');
        end
        made = wanted(cellfun(@(n) isprop(inst, n) && ~isempty(inst.(n)) && all(isvalid(inst.(n))), wanted));
        missing = setdiff(wanted, made);
        if isempty(missing)
            checks{end + 1} = sprintf('%d components by their code names', numel(made)); %#ok<AGROW>
        else
            checks{end + 1} = sprintf('components not made: %s', strjoin(missing, ',')); %#ok<AGROW>
        end
        % Every callback the loaded tree names as a char is a function handle in the running app, and runs.
        bound = 0; ran = 0; errors = {};
        for h = reshape(findall(loaded, '-property', 'DesignTimeProperties'), 1, [])
            mc = metaclass(h);
            for p = mc.PropertyList'
                if ~strcmp(p.Type.Name, 'matlab.graphics.datatype.Callback') || ~ischar(p.GetAccess) || ~strcmp(p.GetAccess, 'public') || p.Hidden, continue; end
                name = h.(p.Name);
                if isempty(name) || ~ischar(name) || strcmp(name, 'closereq'), continue; end % closereq: a figure's default
                codeName = h.DesignTimeProperties.CodeName;
                if ~any(strcmp(made, codeName)), errors{end + 1} = sprintf('%s not made', codeName); continue; end %#ok<AGROW>
                target = inst.(codeName);
                f = target.(p.Name);
                if ~isa(f, 'function_handle'), errors{end + 1} = sprintf('%s.%s is %s', codeName, p.Name, class(f)); continue; end %#ok<AGROW>
                bound = bound + 1;
                try
                    f(target, []);
                    ran = ran + 1;
                catch ce
                    errors{end + 1} = sprintf('%s.%s: %s', codeName, p.Name, strtok(ce.message, newline)); %#ok<AGROW>
                end
            end
        end
        checks{end + 1} = sprintf('%d callbacks bound, %d ran', bound, ran); %#ok<AGROW>
        if ~isempty(errors), checks{end + 1} = strjoin(errors, '; '); end %#ok<AGROW>
        if isSingletonFile(fullfile(folder, [app '.m']))
            again = feval(app, args{:});
            checks{end + 1} = sprintf('single instance: a second construction %s the running app', ifelse(again == inst, 'is', 'IS NOT')); %#ok<AGROW>
        end
        delete(inst);
        checks{end + 1} = sprintf('delete(app): app valid %d, figure valid %d', isvalid(inst), isvalid(fig)); %#ok<AGROW>
        fprintf('%s: runs: %s\n', app, strjoin(checks, '; '));
        tally.runs = tally.runs + 1;
    catch e
        fprintf('%s: run failed %s: %s\n', app, e.identifier, strtok(e.message, newline));
        for s = 1:min(4, numel(e.stack)), fprintf('   at %s:%d\n', e.stack(s).name, e.stack(s).line); end
        tally.failed = tally.failed + 1;
    end
    if ~isempty(loaded), delete(loaded); end
    delete(findall(groot, 'Type', 'figure'));
    clear restoreRun
    clear restore
end
disp(tally)

function cleanupRun(previous, runFolder)
    cd(previous);
    rmpath(runFolder);
    rmdir(fileparts(runFolder), 's');
end

function list = names(handles, property)
    % The code names of handles, sorted, as a cell of char; empty for none.
    list = {};
    for h = reshape(handles, 1, [])
        v = h.(property);
        if isstruct(v), v = v.CodeName; end
        list{end + 1} = char(v); %#ok<AGROW>
    end
    list = sort(list);
end

function s = isSingletonFile(fn)
    s = contains(fileread(fn), '<SingleRunningInstance>true</SingleRunningInstance>');
end

function [a, warnings] = openApp(fn)
    des = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fn);
    a = des.getAppData();
    warnings = {};
    if isprop(des, 'Warnings')
        for w = des.Warnings(:)'
            warnings{end + 1} = char(w.Id); %#ok<AGROW>
        end
    end
end

function problems = compareTrees(m, b)
    % The .m's components against the .mlapp's, by code name; what the package holds for App
    % Designer alone (DesignTimeProperties: the stored code, the group) is left out.
    problems = {};
    mm = byCodeName(m); mb = byCodeName(b);
    for k = keys(mb)
        name = k{1};
        if ~isKey(mm, name), problems{end + 1} = sprintf('%s missing', name); continue; end %#ok<AGROW>
        diffs = ds1b_objdiff(mm(name), mb(name));
        diffs = diffs(~contains(diffs, 'DesignTimeProperties'));
        for i = 1:numel(diffs), problems{end + 1} = sprintf('%s: %s', name, diffs{i}); end %#ok<AGROW>
    end
    for k = keys(mm)
        if ~isKey(mb, k{1}), problems{end + 1} = sprintf('%s extra', k{1}); end %#ok<AGROW>
    end
end

function problems = compareCode(m, b)
    problems = {};
    if ~strcmp(m.ClassName, b.ClassName), problems{end + 1} = sprintf('class %s vs %s', m.ClassName, b.ClassName); end
    mc = callbackNames(m); bc = callbackNames(b);
    if ~isequal(sort(mc), sort(bc)), problems{end + 1} = sprintf('callbacks %s vs %s', strjoin(mc, ','), strjoin(bc, ',')); end
    ms = startupOf(m); bs = startupOf(b);
    if ~strcmp(ms, bs), problems{end + 1} = sprintf('startup %s vs %s', ms, bs); end
    mp = paramsOf(m); bp = paramsOf(b);
    if ~strcmp(mp, bp), problems{end + 1} = sprintf('input parameters ''%s'' vs ''%s''', mp, bp); end
    if singletonOf(m) ~= singletonOf(b), problems{end + 1} = sprintf('single instance %d vs %d', singletonOf(m), singletonOf(b)); end
    me = sectionOf(m); be = sectionOf(b);
    if isequal(me, be) || (numel(be) > 1 && isequal(me, be(2:end)))
        % PlainTextCodeParser.findEditableSectionCode cuts from the properties block's `end` keyword
        % and drops three lines, one too many: the section's first line is lost (R2025b's own defect).
    else
        problems{end + 1} = sprintf('editable section [%s] vs [%s]', strjoin(me, ' / '), strjoin(be, ' / '));
    end
end

function names = callbackNames(c)
    names = {};
    if isfield(c, 'Callbacks') && ~isempty(c.Callbacks), names = {c.Callbacks.Name}; end
end

function s = startupOf(c)
    s = '';
    for f = {'StartupFcn', 'StartupCallback'}
        if isfield(c, f{1}) && ~isempty(c.(f{1})) && isfield(c.(f{1}), 'Name'), s = c.(f{1}).Name; end
    end
end

function p = paramsOf(c)
    p = '';
    if isfield(c, 'InputParameters') && ~isempty(c.InputParameters)
        p = c.InputParameters;
        if iscell(p), p = strjoin(p, ', '); end
        p = char(p);
    end
end

function s = singletonOf(c)
    s = false;
    for f = {'singletonMode', 'SingletonMode'}
        if isfield(c, f{1}) && ~isempty(c.(f{1})), s = true; end
    end
end

function e = sectionOf(c)
    e = {};
    if isfield(c, 'EditableSectionCode') && ~isempty(c.EditableSectionCode)
        e = cellstr(c.EditableSectionCode);
        e = strtrim(e(~cellfun(@isempty, strtrim(e))));
    end
end

function m = byCodeName(fig)
    m = containers.Map();
    for h = findall(fig, '-property', 'DesignTimeProperties')'
        m(h.DesignTimeProperties.CodeName) = h;
    end
end

function args = readArgs(fn)
    args = {};
    if ~isfile(fn), return; end
    text = strtrim(fileread(fn));
    if isempty(text), return; end
    parts = strsplit(text, ',');
    args = cellfun(@(p) eval(strtrim(p)), parts, 'UniformOutput', false);
end

function out = ifelse(cond, a, b)
    if cond, out = a; else, out = b; end
end
