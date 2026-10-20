% DS7 (designer plan): custom (user-authored) components on App Designer's MATLAB side, R2025b,
% headless. Run from tools\matlab-checklist\ui-probes\ds7 with
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name ds7_uac -TimeoutSec 900
% (run-probe.ps1 copied from ds6: matlab.exe -noFigureWindows -batch ds7_uac -sd <ds7>).
% Needs, beside this file: ds7_inflate.py and ds7_rename.py (python helpers), ..\ds0\ds0_tree_dump.py.
% Python: DS7_PYTHON, else the 3.12 install, else 'python'.
% One printed line per finding; every step that can fail is caught and printed
% ('<label> ERROR <identifier> | <message>'), and the probe goes on.
%  1. metadata: SpinnerGauge registered through Model + MetadataUIViewModel (no UI), the
%     appDesigner.json printed; U10Probe and Ds7Types registered in one folder, then removed.
%  2. class model: properties/metaclass, UserComponentAdapter's codegen names, non-public names,
%     run-time defaults per theme, mode siblings, generated lines (figure, grid), dirty props,
%     callback names - for SpinnerGauge, U10Probe, ds7pkg.PkgGauge and Ds7Types (one of each type).
%  3. inspector: UserComponentPropertyView's properties, groups and per-property renderer.
%  4. design-time defaults (getComponentDesignTimeDefaults).
%  5. .mlapp: a uifigure with a SpinnerGauge and a button, DesignTimeProperties as App Designer
%     keeps them, Metadata.UserComponents set, saved by MLAPPSerializer (two property-block orders);
%     appMetadata.xml, appModel.mat by load(), the MCOS dump, App Designer's reader.
%  6. errored class (SgFail refuses setup while loading).
%  7. plain text: a .m app whose appendix has <SpinnerGauge>/<U10Probe>, read (feature off) and
%     run (feature on); packaged class by short and by full element name.
%  8. missing class: SpinnerGauge off the path (and a never-loaded renamed copy); the .mlapp's
%     warnings and the plain-text read.
% rt/ (ignored) holds everything it writes.
warning('off', 'backtrace');
ctx = ds7Setup();
ds7Metadata(ctx);
ds7ClassModel(ctx);
ds7Inspector(ctx);
ds7DesignDefaults(ctx);
ds7Mlapp(ctx);
ds7Errored(ctx);
ds7PlainText(ctx);
ds7Missing(ctx);
say('done');

% ------------------------------------------------------------------------------------------------
function ctx = ds7Setup()
    ctx.here = fileparts(mfilename('fullpath'));
    ctx.repo = fileparts(fileparts(fileparts(fileparts(ctx.here))));
    helpers = fullfile(ctx.repo, 'tests', 'JGraph.Tests', 'MatlabParity', 'fixtures', 'helpers');
    ctx.rt = fullfile(ctx.here, 'rt', 'uac');
    if isfolder(ctx.rt)
        try, rmdir(ctx.rt, 's'); catch e, oops('setup rmdir', e); end
    end
    ctx.sg = fullfile(ctx.rt, 'sg');     % SpinnerGauge alone, so it can leave the path
    ctx.cls = fullfile(ctx.rt, 'cls');   % U10Probe, u10_log, Ds7Types, Ds7Mode, SgFail, +ds7pkg
    ctx.out = fullfile(ctx.rt, 'out');
    ctx.plain = fullfile(ctx.rt, 'plain');
    for d = {ctx.sg, ctx.cls, ctx.out, ctx.plain, fullfile(ctx.cls, '+ds7pkg')}
        mkdir(d{1});
    end
    copyfile(fullfile(helpers, 'SpinnerGauge.m'), ctx.sg);
    copyfile(fullfile(helpers, 'U10Probe.m'), ctx.cls);
    copyfile(fullfile(helpers, 'u10_log.m'), ctx.cls);
    writeLines(fullfile(ctx.cls, 'Ds7Mode.m'), {
        'classdef Ds7Mode'
        '    enumeration'
        '        Slow, Fast'
        '    end'
        'end'});
    writeLines(fullfile(ctx.cls, 'Ds7Types.m'), {
        'classdef Ds7Types < matlab.ui.componentcontainer.ComponentContainer'
        '    % DS7TYPES  One property of each kind App Designer''s generator and inspector meet.'
        '    properties'
        '        Count int32 = 1'
        '        Ratio single = 1'
        '        Flag logical = false'
        '        Note string = "x"'
        '        Text char = ''abc'''
        '        Names cell = {''a'', ''b''}'
        '        Nums = {1, 2}'
        '        Mode Ds7Mode = Ds7Mode.Slow'
        '        NoDefaultMode Ds7Mode'
        '        When datetime = datetime(2020, 1, 2)'
        '        Grid2 = {''a'', ''b''; ''c'', ''d''}'
        '        Untyped'
        '        Data struct = struct(''a'', 1)'
        '        Colors (1,3) double = [1 0 0]'
        '        Column (:,1) double = [1; 2]'
        '    end'
        '    properties (Dependent)'
        '        Twice'
        '    end'
        '    properties (Hidden)'
        '        Secret = 1'
        '    end'
        '    properties (SetAccess = protected)'
        '        Prot = 2'
        '    end'
        '    properties (SetAccess = immutable)'
        '        Fixed = 3'
        '    end'
        '    events (HasCallbackProperty, NotifyAccess = public)'
        '        Changed'
        '    end'
        '    methods'
        '        function v = get.Twice(comp)'
        '            v = 2 * double(comp.Ratio);'
        '        end'
        '    end'
        '    methods (Access = protected)'
        '        function setup(~)'
        '        end'
        '        function update(~)'
        '        end'
        '    end'
        'end'});
    writeLines(fullfile(ctx.cls, 'SgFail.m'), {
        'classdef SgFail < matlab.ui.componentcontainer.ComponentContainer'
        '    % SGFAIL  Refuses its setup while groot''s appdata ds7_fail is true.'
        '    properties'
        '        Value (1,1) double = 0'
        '    end'
        '    events (HasCallbackProperty, NotifyAccess = public)'
        '        ValueChanged'
        '    end'
        '    methods (Access = protected)'
        '        function setup(comp)'
        '            if isappdata(groot, ''ds7_fail'') && getappdata(groot, ''ds7_fail'')'
        '                error(''ds7:fail'', ''SgFail setup refuses on purpose'');'
        '            end'
        '            comp.Position = [10 10 220 80];'
        '        end'
        '        function update(~)'
        '        end'
        '    end'
        'end'});
    writeLines(fullfile(ctx.cls, '+ds7pkg', 'PkgGauge.m'), {
        'classdef PkgGauge < matlab.ui.componentcontainer.ComponentContainer'
        '    % PKGGAUGE  A packaged custom component.'
        '    properties'
        '        Value (1,1) double = 0'
        '    end'
        '    events (HasCallbackProperty, NotifyAccess = public)'
        '        ValueChanged'
        '    end'
        '    methods (Access = protected)'
        '        function setup(comp)'
        '            comp.Position = [10 10 120 40];'
        '        end'
        '        function update(~)'
        '        end'
        '    end'
        'end'});
    addpath(ctx.sg); addpath(ctx.cls);
    ctx.py = getenv('DS7_PYTHON');
    if isempty(ctx.py)
        ctx.py = 'C:\Users\Carpe\AppData\Local\Programs\Python\Python312\python.exe';
        if ~isfile(ctx.py), ctx.py = 'python'; end
    end
    ctx.dump = fullfile(ctx.here, '..', 'ds0', 'ds0_tree_dump.py');
    ctx.inflate = fullfile(ctx.here, 'ds7_inflate.py');
    ctx.rename = fullfile(ctx.here, 'ds7_rename.py');
    say('setup: release %s, python %s', version('-release'), ctx.py);
end

% ------------------------------------------------------------------------------------------------
function ds7Metadata(ctx)
    say('== 1 metadata');
    sgFile = fullfile(ctx.sg, 'SpinnerGauge.m');
    registerHeadless(ctx, sgFile);
    printJson(ctx, fullfile(ctx.sg, 'resources', 'appDesigner.json'));
    try
        m2 = appdesigner.internal.usercomponent.metadata.Model(sgFile);
        md = m2.getComponentMetadata();
        say('re-read: status %s, valid %d, categories %s, fields %s', md.status, m2.getModelValidity(), ...
            strjoin(cellstr(m2.getCategories()), ','), strjoin(fieldnames(md)', ','));
    catch e, oops('re-read', e); end
    % two components in one folder, then removed one by one
    registerHeadless(ctx, fullfile(ctx.cls, 'U10Probe.m'));
    registerHeadless(ctx, fullfile(ctx.cls, 'Ds7Types.m'));
    clsJson = fullfile(ctx.cls, 'resources', 'appDesigner.json');
    printJson(ctx, clsJson);
    try
        appdesigner.customcomponent.removeMetadata(fullfile(ctx.cls, 'Ds7Types.m'));
        say('removeMetadata Ds7Types: ok'); printJson(ctx, clsJson);
        appdesigner.customcomponent.removeMetadata(fullfile(ctx.cls, 'U10Probe.m'));
        say('removeMetadata U10Probe: ok'); printJson(ctx, clsJson);
    catch e, oops('removeMetadata', e); end
    % a packaged class: the folder the json goes in, and the className written
    try
        mp = appdesigner.internal.usercomponent.metadata.Model(fullfile(ctx.cls, '+ds7pkg', 'PkgGauge.m'));
        mdp = mp.getComponentMetadata();
        say('packaged: directory %s, className %s, componentName %s', rel(ctx, mp.getDirectory()), mdp.className, mdp.componentName);
    catch e, oops('packaged model', e); end
    try
        say('validated file: %s', rel(ctx, appdesigner.internal.usercomponent.metadata.getValidatedCustomComponentFile(fullfile(ctx.sg, 'SpinnerGauge'))));
        appdesigner.internal.usercomponent.metadata.getValidatedCustomComponentFile(fullfile(ctx.cls, 'Ds7Mode.m'));
    catch e, oops('not a component', e); end
end

function registerHeadless(ctx, file)
    % MetadataUI's register path without its window: the view model fills the defaults, the fields
    % are collected as OkButtonPushed collects them, registerComponent captures the avatar and
    % position, and the controller's listener writes through Model.
    [~, name] = fileparts(file);
    try
        model = appdesigner.internal.usercomponent.metadata.Model(file);
        payload = struct();
        payload.Metadata = model.getComponentMetadata();
        payload.Categories = model.getCategories();
        payload.Directory = model.getDirectory();
        payload.ModelValidity = model.getModelValidity();
        payload.FilePath = file;
        say('%s: model directory %s, status %s, valid %d', name, rel(ctx, payload.Directory), payload.Metadata.status, payload.ModelValidity);
        vm = appdesigner.internal.usercomponent.metadata.MetadataUIViewModel(payload);
        md = vm.getMetadata();
        say('%s: view model fields %s; status %s; categories %s', name, strjoin(fieldnames(md)', ','), vm.getStatus(), strjoin(cellstr(vm.getCategories()), ','));
        say('%s: default description %s | icon %s | category %s (%s) | version %s (%s)', name, v2s(md.description), rel(ctx, char(md.icon)), v2s(md.category), class(md.category), v2s(md.version), class(md.version));
        md.componentName = char(md.componentName);
        md.version = char(md.version);
        md.description = string(md.description);
        md.category = char(md.category);
        if ~startsWith(char(md.icon), 'data:')
            md.icon = appdesigner.internal.application.ImageUtils.getImageDataURIFromFile(char(md.icon));
        end
        say('%s: icon data URI %d chars, starts %s', name, strlength(md.icon), extractBefore(string(md.icon), min(40, strlength(md.icon))));
        md.authorName = '';
        md.authorEmail = string('');
        state = containers.Map({'wrote'}, {false});
        lis = addlistener(vm, 'UpdateModelEvent', @(~, e) onUpdate(e, model, name, state)); %#ok<NASGU>
        lis2 = addlistener(vm, 'RegistrationErrorEvent', @(~, e) say('%s: registration error %s | %s', name, e.RegistrationError.identifier, oneline(char(e.ErrorMessage)))); %#ok<NASGU>
        lastwarn('');
        vm.registerComponent(md);
        [w, wid] = lastwarn();
        if ~isempty(w), say('%s: warning %s | %s', name, wid, oneline(w)); end
        if ~state('wrote')
            say('%s: view model did not register; writing through Model directly', name);
            fig = uifigure('Visible', 'off');
            c = feval(model.getComponentMetadata().className, 'Parent', fig);
            c.Position(1:2) = [1 1];
            md.avatar = ''; md.avatarDark = ''; md.defaultPosition = c.Position;
            delete(fig);
            model.registerComponent(md);
        end
    catch e, oops([name ' register'], e); end
end

function onUpdate(e, model, name, state)
    % ConfigureMetadata.updateModel, minus the window
    say('%s: UpdateModelEvent %s; metadata fields %s; avatar %d chars, avatarDark %d chars, defaultPosition %s', ...
        name, e.UpdateType, strjoin(fieldnames(e.Metadata)', ','), strlength(e.Metadata.avatar), strlength(e.Metadata.avatarDark), mat2str(e.Metadata.defaultPosition));
    try
        switch e.UpdateType
            case 'Register', model.registerComponent(e.Metadata);
            case 'Update', model.updateComponent(e.Metadata);
        end
        state('wrote') = true; %#ok<NASGU>
    catch ex, oops([name ' Model write'], ex); end
end

function printJson(ctx, fn)
    if ~isfile(fn), say('json %s: missing', rel(ctx, fn)); return; end
    raw = fileread(fn);
    say('json %s: %d chars, ends with newline %d, CR %d', rel(ctx, fn), numel(raw), endsWith(raw, newline), contains(raw, char(13)));
    L = splitlines(raw);
    for k = 1:numel(L)
        t = L{k};
        if numel(t) > 140, t = sprintf('%s ...(%d chars)', t(1:140), numel(L{k})); end
        say('json| %s', t);
    end
    try
        d = jsondecode(raw);
        say('json decoded: top fields %s; components class %s', strjoin(fieldnames(d)', ','), class(d.components));
    catch e, oops('jsondecode', e); end
end

% ------------------------------------------------------------------------------------------------
function ds7ClassModel(ctx)
    say('== 2 class model');
    describeClass('SpinnerGauge', true);
    describeClass('U10Probe', false);
    describeClass('Ds7Types', false);
    fig = uifigure('Visible', 'off');
    cleanup = onCleanup(@() delete(fig));
    % SpinnerGauge under the figure: Value 40, Limits [0 100] (its default), callback bound,
    % Position [10 10 220 80] (what its setup sets) and, second, a moved one
    c = codegenCase(ctx, 'SpinnerGauge', fig, struct('Value', 40, 'Limits', [0 100], 'ValueChangedFcn', 'GaugeValueChanged', 'Position', [10 10 220 80]), 'figure');
    codegenCase(ctx, 'SpinnerGauge', fig, struct('Value', 40, 'ValueChangedFcn', 'GaugeValueChanged', 'Position', [20 30 220 80]), 'figure moved');
    g = uigridlayout(fig, [1 1]);
    codegenCase(ctx, 'SpinnerGauge', g, struct('Value', 40, 'Limits', [0 100], 'ValueChangedFcn', 'GaugeValueChanged'), 'grid');
    g2 = uigridlayout(fig, [2 2]);
    cg = codegenCase(ctx, 'SpinnerGauge', g2, struct('Value', 40), 'grid r2c[1 2] before');
    try
        cg.Layout.Row = 2; cg.Layout.Column = [1 2];
        printGen(ctx, cg, 'grid r2c[1 2]');
    catch e, oops('grid span', e); end
    u10_log();
    codegenCase(ctx, 'U10Probe', fig, struct('Value', 40, 'ValueChangedFcn', 'ProbeValueChanged', 'Position', [10 10 220 80]), 'figure');
    say('U10Probe log: %s', u10_log());
    codegenCase(ctx, 'U10Probe', g, struct('Value', 40, 'ValueChangedFcn', 'ProbeValueChanged'), 'grid');
    say('U10Probe log: %s', u10_log());
    codegenCase(ctx, 'ds7pkg.PkgGauge', fig, struct('Value', 5, 'ValueChangedFcn', 'PGValueChanged'), 'figure');
    % one of each kind of value
    t = codegenCase(ctx, 'Ds7Types', fig, struct('Count', int32(5), 'Ratio', single(2.5), 'Flag', true, ...
        'Note', "he said ""hi""" + newline + "x", 'Text', ['it''s' newline 'ok'], 'Names', {{'p'; 'q'}}, ...
        'Nums', {{3.14159265, 2}}, 'Mode', Ds7Mode.Fast, 'NoDefaultMode', Ds7Mode.Fast, 'When', datetime(2021, 3, 4), ...
        'Grid2', {{'w', 'x'; 'y', 'z'}}, 'Untyped', 7, 'Data', struct('a', 2), 'Colors', [0 0.5 1], 'Column', [3; 4], ...
        'ChangedFcn', 'TypesChanged'), 'figure');
    if ~isempty(t)
        a = appdesigner.internal.componentadapter.uicomponents.adapter.UserComponentAdapter('Ds7Types');
        for p = {'Count', 'Ratio', 'Flag', 'Note', 'Text', 'Names', 'Nums', 'Mode', 'NoDefaultMode', 'When', 'Grid2', 'Untyped', 'Data', 'Colors', 'Column', 'ChangedFcn'}
            try
                seg = a.getCodeGenPropertySet(t, 'app', p{1}, 'Types', 'UIFigure');
                if iscell(seg), seg = strjoin(seg, ' || '); end
                s1 = seg;
            catch e, s1 = ['ERROR ' e.identifier ' | ' oneline(e.message)]; end
            try
                s2 = a.getPropertyValueString(t, p{1});
            catch e, s2 = ['ERROR ' e.identifier ' | ' oneline(e.message)]; end
            say('Ds7Types %s: set line %s | value string %s', p{1}, oneline(s1), oneline(char(string(s2))));
        end
    end
    % the flag App Designer sets while it makes a design-time component
    try
        matlab.ui.componentcontainer.ComponentContainer.componentObjectBeingLoadedInAppDesigner(true);
        flagOff = onCleanup(@() matlab.ui.componentcontainer.ComponentContainer.componentObjectBeingLoadedInAppDesigner(false));
        c3 = SpinnerGauge('Parent', fig, 'CreateFcn', []);
        say('design-time flag: SetupCalls %d, UpdateCalls %d before drawnow', c3.SetupCalls, c3.UpdateCalls);
        drawnow;
        say('design-time flag: UpdateCalls %d after drawnow; Position %s', c3.UpdateCalls, mat2str(c3.Position));
        clear flagOff
    catch e, oops('design-time flag', e); end
    clear c t cg g g2 c3
end

function describeClass(name, all)
    try
        say('%s properties(): %s', name, strjoin(properties(name)', ','));
        mc = meta.class.fromName(name);
        say('%s PropertyList order: %s', name, strjoin({mc.PropertyList.Name}, ','));
        for p = mc.PropertyList'
            if ~all && ~strcmp(p.DefiningClass.Name, name), continue; end
            sa = p.SetAccess; if ~ischar(sa), sa = 'classlist'; end
            ga = p.GetAccess; if ~ischar(ga), ga = 'classlist'; end
            def = '-'; if p.HasDefault, def = v2s(p.DefaultValue); end
            say('meta| %s.%s get=%s set=%s hidden=%d dep=%d transient=%d type=%s default=%s setm=%d getm=%d by=%s', name, p.Name, ga, sa, ...
                p.Hidden, p.Dependent, p.Transient, p.Type.Name, def, ~isempty(p.SetMethod), ~isempty(p.GetMethod), p.DefiningClass.Name);
        end
        say('%s callbacks (extractCallbackPropertyNamesFromMetaClass): %s', name, strjoin(appdesigner.internal.usercomponent.extractCallbackPropertyNamesFromMetaClass(mc), ','));
    catch e, oops([name ' describe'], e); end
end

function c = codegenCase(ctx, className, parent, props, label)
    % Makes the component, prints the adapter's view of it, sets props and prints the lines.
    c = [];
    tag = sprintf('%s [%s]', className, label);
    try
        c = feval(className, parent);
        drawnow;
        a = appdesigner.internal.componentadapter.uicomponents.adapter.UserComponentAdapter(className);
        names = a.getCodeGenPropertyNames(c);
        say('%s codegen names: %s', tag, strjoin(names(:)', ','));
        say('%s non-public: %s', tag, strjoin(a.listNonPublicProperties(c), ','));
        say('%s callback names (getCallbackPropertyNames): %s', tag, strjoin(appdesigner.internal.usercomponent.getCallbackPropertyNames(c), ','));
        modes = {};
        for n = names(:)'
            if isprop(c, [n{1} 'Mode']), modes{end + 1} = sprintf('%sMode=%s', n{1}, v2s(c.([n{1} 'Mode']))); end %#ok<AGROW>
        end
        say('%s mode siblings: %s', tag, strjoin(modes, ','));
        if strcmp(label, 'figure')
            for th = {'unthemed', 'light', 'dark'}
                try
                    d = appdesigner.internal.componentadapter.uicomponents.adapter.UserComponentAdapter(className).getComponentRunTimeDefaults(th{1});
                    vals = cellfun(@(f) sprintf('%s=%s', f, v2s(d.(f))), intersect(fieldnames(d), [names(:); {'Units'; 'Type'; 'AutoResizeChildren'}], 'stable'), 'UniformOutput', false);
                    say('%s run-time defaults (%s): %s', tag, th{1}, strjoin(vals', '; '));
                catch e, oops([tag ' defaults ' th{1}], e); end
            end
            try
                d = a.getComponentRunTimeDefaults('light');
                say('%s run-time default fields: %s', tag, strjoin(fieldnames(d)', ','));
            catch e, oops([tag ' default fields'], e); end
        end
        for f = fieldnames(props)'
            try
                c.(f{1}) = props.(f{1});
            catch e, oops(sprintf('%s set %s', tag, f{1}), e); end
        end
        drawnow;
        printGen(ctx, c, label);
    catch e, oops([tag ' codegen'], e); end
end

function printGen(~, c, label)
    className = class(c);
    tag = sprintf('%s [%s]', className, label);
    try
        a = appdesigner.internal.componentadapter.uicomponents.adapter.UserComponentAdapter(className);
        gen = appdesigner.internal.codegeneration.ComponentCodeGenerator();
        for th = {'light', 'unthemed'}
            lines = gen.getComponentGenerationCode(c, a, appdesigner.internal.serialization.app.AppTypes.StandardApp, th{1});
            say('%s generated (%s, %d lines)', tag, th{1}, numel(lines));
            for k = 1:numel(lines), say('gen| %s', lines{k}); end
        end
        lines = gen.getComponentGenerationCode(c, a, appdesigner.internal.serialization.app.AppTypes.UserComponentApp, 'light');
        say('%s generated for a UserComponentApp: %s', tag, strjoin(lines', ' || '));
    catch e, oops([tag ' generate'], e); end
    try
        a = appdesigner.internal.componentadapter.uicomponents.adapter.UserComponentAdapter(className);
        dp = a.getComponentDirtyProps(c, 'light');
        if isempty(dp)
            say('%s dirty props: none', tag);
        else
            say('%s dirty props: %s', tag, strjoin(arrayfun(@(x) sprintf('%s=%s', x.Name, oneline(char(string(x.Value)))), dp, 'UniformOutput', false), ' ; '));
        end
    catch e, oops([tag ' dirty props'], e); end
end

% ------------------------------------------------------------------------------------------------
function ds7Inspector(~)
    say('== 3 inspector');
    fig = uifigure('Visible', 'off');
    cleanup = onCleanup(@() delete(fig));
    for name = {'SpinnerGauge', 'U10Probe', 'Ds7Types', 'ds7pkg.PkgGauge'}
        try
            c = feval(name{1}, fig);
            pv = inspector.internal.UserComponentPropertyView(c);
            say('%s view properties: %s', name{1}, strjoin(properties(pv)', ','));
            try
                dm = internal.matlab.inspector.MLInspectorDataModel('inspector', '', false);
                dm.Data = pv;
                ivm = internal.matlab.inspector.InspectorViewModel(dm);
                groups = ivm.getRenderedGroupData();
                delete(ivm); delete(dm);
                raw = jsonencode(groups);
                say('%s groups json (%d chars): %s', name{1}, numel(raw), raw(1:min(end, 3000)));
                for gi = 1:numel(groups)
                    if iscell(groups), gr = groups{gi}; else, gr = groups(gi); end
                    say('%s group %d: %s', name{1}, gi, groupLine(gr));
                end
            catch e, oops([name{1} ' groups'], e); end
            renderers(pv, name{1});
            delete(pv); delete(c);
        catch e, oops([name{1} ' inspector'], e); end
    end
    % the authoring-side inference (UserComponentApp's Add Property), for comparison
    for v = {'40', '''abc''', '"abc"', 'true', '{''a'',''b''}', 'int8(3)', '[1 2 3]'}
        try
            [cn, dv, idv, r] = appdesigner.internal.usercomponent.UserComponentPropertyUtils.getInferredPropertyDetails(v{1}, '');
            ed = ''; if isstruct(r) && isfield(r, 'InPlaceEditor'), ed = r.InPlaceEditor; end
            say('inferred| %s -> className %s, defaultValue %s, inferred %s, editor %s', v{1}, v2s(cn), v2s(dv), v2s(idv), v2s(ed));
        catch e, oops(['inferred ' v{1}], e); end
    end
end

function s = groupLine(gr)
    s = '';
    try
        f = fieldnames(gr);
        parts = {};
        for k = 1:numel(f)
            v = gr.(f{k});
            if strcmp(f{k}, 'items')
                parts{end + 1} = ['items=' itemNames(v)]; %#ok<AGROW>
            else
                parts{end + 1} = sprintf('%s=%s', f{k}, v2s(v)); %#ok<AGROW>
            end
        end
        s = strjoin(parts, ' ; ');
    catch e, s = ['ERROR ' e.message]; end
end

function s = itemNames(items)
    names = {};
    for k = 1:numel(items)
        if iscell(items), x = items{k}; else, x = items(k); end
        if isstruct(x) && isfield(x, 'name'), names{end + 1} = char(string(x.name)); %#ok<AGROW>
        elseif isstruct(x) && isfield(x, 'items'), names{end + 1} = ['[' itemNames(x.items) ']']; %#ok<AGROW>
        else, names{end + 1} = v2s(x); end %#ok<AGROW>
    end
    s = strjoin(names, ',');
end

function renderers(pv, name)
    % DesignTimeWebUIContainerController.getInspectorRenderer, without a view model.
    try
        wr = internal.matlab.datatoolsservices.WidgetRegistry.getInstance;
        supported = matlab.ui.internal.DesignTimeWebUIContainerController.getInspectorSupportedDataTypes();
        multi = matlab.ui.internal.DesignTimeWebUIContainerController.getMultiLineEditorSupportedDataTypes();
        inherited = properties('matlab.ui.componentcontainer.ComponentContainer');
        say('supported data types: %s; multi-line: %s', strjoin(supported, ','), strjoin(multi, ','));
        for p = properties(pv)'
            try
                value = pv.(p{1});
                classType = internal.matlab.datatoolsservices.FormatDataUtils.getClassString(value, false, true);
                if isKey(pv.PropertyTypeMap, p{1}), propType = pv.PropertyTypeMap(p{1}); else, propType = class(value); end
                [isCE, dataType] = internal.matlab.editorconverters.ComboBoxEditor.isCategoricalOrEnum(classType, propType, value);
                [r, ~, matched] = wr.getWidgets('internal.matlab.inspector.peer.PeerInspectorViewModel', dataType);
                if ~isequal(matched, dataType) && isCE
                    r = wr.getWidgets('internal.matlab.inspector.peer.PeerInspectorViewModel', 'categorical');
                elseif isempty(r.CellRenderer)
                    r = wr.getWidgets('internal.matlab.inspector.peer.PeerInspectorViewModel', classType);
                end
                reset = ~isCE && ~any(strcmp(inherited, p{1})) && ~any(strcmp(supported, matched)) && ~any(strcmp(matched, supported));
                if reset, r.InPlaceEditor = ''; r.CellRenderer = ''; end
                if any(strcmp(multi, matched)), r = wr.getWidgets('internal.matlab.inspector.peer.PeerInspectorViewModel', 'internal.matlab.editorconverters.datatype.MultipleItemsValue'); end
                say('renderer| %s.%s value=%s classType=%s propType=%s catOrEnum=%d dataType=%s matched=%s editor=%s cell=%s converter=%s', name, p{1}, ...
                    v2s(value), classType, v2s(propType), isCE, v2s(dataType), v2s(matched), v2s(r.InPlaceEditor), v2s(r.CellRenderer), v2s(r.EditorConverter));
            catch e, oops(sprintf('renderer %s.%s', name, p{1}), e); end
        end
    catch e, oops([name ' renderers'], e); end
end

% ------------------------------------------------------------------------------------------------
function ds7DesignDefaults(~)
    say('== 4 design-time defaults');
    for name = {'SpinnerGauge', 'U10Probe'}
        try
            a = appdesigner.internal.componentadapter.uicomponents.adapter.UserComponentAdapter(name{1});
            d = a.getComponentDesignTimeDefaults();
            f = fieldnames(d);
            say('%s design-time defaults (%d fields): %s', name{1}, numel(f), strjoin(f', ','));
            for k = 1:numel(f)
                if any(strcmp(f{k}, {'Value', 'Limits', 'Position', 'BackgroundColor', 'IsUserComponent', 'CallbackPropertyNames', 'IsClientDriven', 'ValueChangedFcn', 'Visible', 'Units'}))
                    say('%s design-time %s = %s', name{1}, f{k}, v2s(d.(f{k})));
                end
            end
        catch e, oops([name{1} ' design-time defaults'], e); end
        say('U10Probe log: %s', u10_log());
    end
end

% ------------------------------------------------------------------------------------------------
function ds7Mlapp(ctx)
    say('== 5 mlapp');
    % the same tree saved as a plain MAT-file, for the MCOS dump without the package
    try
        fig = makeTree('SpinnerGauge');
        components = struct('UIFigure', fig); %#ok<NASGU>
        raw = fullfile(ctx.out, 'tree_uac.mat');
        save(raw, 'components', '-v7', '-nocompression');
        delete(fig);
        dumpMat(ctx, raw, 'SpinnerGauge', 'tree_uac.mat');
    catch e, oops('tree mat', e); end
    saveApp(ctx, 'UacLast', 'SpinnerGauge', {'UIFigure', 'GoButton', 'Gauge'});
    saveApp(ctx, 'UacMid', 'SpinnerGauge', {'UIFigure', 'Gauge', 'GoButton'});
    for app = {'UacLast', 'UacMid'}
        fn = fullfile(ctx.out, [app{1} '.mlapp']);
        partsOf(ctx, fn, app{1}, 'SpinnerGauge');
        readApp(ctx, fn, app{1});
    end
    % run the saved app (its class text) headless
    try
        addpath(ctx.out);
        r = feval('UacLast');
        say('run UacLast: %s, Gauge %s Value %g SetupCalls %d', class(r), class(r.Gauge), r.Gauge.Value, r.Gauge.SetupCalls);
        delete(r);
    catch e, oops('run UacLast', e); end
end

function fig = makeTree(gaugeClass)
    % The tree App Designer holds: each component with DesignTimeProperties (CodeName, GroupId,
    % ComponentCode = the generator's lines), callbacks as names.
    gen = appdesigner.internal.codegeneration.ComponentCodeGenerator();
    adapters = appdesigner.internal.appmetadata.getProductionComponentAdapterMap();
    fig = uifigure('Visible', 'off', 'Position', [100 100 400 200], 'Name', 'UAC app');
    gauge = feval(gaugeClass, fig);
    gauge.Value = 40;
    gauge.ValueChangedFcn = 'GaugeValueChanged';
    gauge.Position = [20 20 220 80];
    btn = uibutton(fig, 'push', 'Text', 'Go', 'Position', [260 40 100 22], 'ButtonPushedFcn', 'GoButtonPushed');
    drawnow;
    items = {fig, 'UIFigure'; gauge, 'Gauge'; btn, 'GoButton'};
    for k = 1:size(items, 1)
        h = items{k, 1};
        if isa(h, 'matlab.ui.componentcontainer.ComponentContainer')
            a = appdesigner.internal.componentadapter.uicomponents.adapter.UserComponentAdapter(class(h));
        else
            a = eval(adapters(class(h)));
        end
        code = gen.getComponentGenerationCode(h, a, appdesigner.internal.serialization.app.AppTypes.StandardApp, 'light');
        addprop(h, 'DesignTimeProperties');
        h.DesignTimeProperties = struct('CodeName', items{k, 2}, 'GroupId', '', 'ComponentCode', {code(:)}, 'ImageRelativePath', '');
    end
end

function txt = classText(name, gaugeClass, order)
    decl = struct('UIFigure', 'matlab.ui.Figure', 'Gauge', gaugeClass, 'GoButton', 'matlab.ui.control.Button');
    L = {['classdef ' name ' < matlab.apps.AppBase'], '', '    % Properties that correspond to app components', '    properties (Access = public)'};
    for k = 1:numel(order)
        L{end + 1} = sprintf('        %-9s%s', order{k}, decl.(order{k})); %#ok<AGROW>
    end
    L = [L, {'    end', '', '    % Callbacks that handle component events', '    methods (Access = private)', '', ...
        '        % Value changed function: Gauge', '        function GaugeValueChanged(app, event)', '            disp(''changed'');', '        end', '', ...
        '        % Button pushed function: GoButton', '        function GoButtonPushed(app, event)', '            app.Gauge.Value = app.Gauge.Value + 10;', '        end', ...
        '    end', '', '    % Component initialization', '    methods (Access = private)', '', ...
        '        % Create UIFigure and components', '        function createComponents(app)', '', ...
        '            % Create UIFigure and hide until all components are created', ...
        '            app.UIFigure = uifigure(''Visible'', ''off'');', ...
        '            app.UIFigure.Position = [100 100 400 200];', ...
        '            app.UIFigure.Name = ''UAC app'';', '', ...
        '            % Create Gauge', ...
        ['            app.Gauge = ' gaugeClass '(app.UIFigure);'], ...
        '            app.Gauge.Value = 40;', ...
        '            app.Gauge.ValueChangedFcn = createCallbackFcn(app, @GaugeValueChanged, true);', ...
        '            app.Gauge.Position = [20 20 220 80];', '', ...
        '            % Create GoButton', ...
        '            app.GoButton = uibutton(app.UIFigure, ''push'');', ...
        '            app.GoButton.ButtonPushedFcn = createCallbackFcn(app, @GoButtonPushed, true);', ...
        '            app.GoButton.Position = [260 40 100 22];', ...
        '            app.GoButton.Text = ''Go'';', '', ...
        '            % Show the figure after all components are created', ...
        '            app.UIFigure.Visible = ''on'';', '        end', '    end', '', ...
        '    % App creation and deletion', '    methods (Access = public)', '', '        % Construct app', ...
        ['        function app = ' name], '', '            % Create UIFigure and components', '            createComponents(app)', '', ...
        '            % Register the app with App Designer', '            registerApp(app, app.UIFigure)', '', ...
        '            if nargout == 0', '                clear app', '            end', '        end', '', ...
        '        % Code that executes before app deletion', '        function delete(app)', '', ...
        '            % Delete UIFigure when app is deleted', '            delete(app.UIFigure)', '        end', '    end', 'end'}];
    txt = strjoin(L, newline);
end

function saveApp(ctx, name, gaugeClass, order)
    try
        fig = makeTree(gaugeClass);
        txt = classText(name, gaugeClass, order);
        fn = fullfile(ctx.out, [name '.mlapp']);
        s = appdesigner.internal.serialization.MLAPPSerializer(fn, fig);
        s.OverwriteTargetFile = true;
        s.MatlabCodeText = txt;
        s.Callbacks = struct('Name', {'GaugeValueChanged', 'GoButtonPushed'}, 'Code', {{}});
        md = appdesigner.internal.model.MetadataModel;
        md.UserComponents = {gaugeClass};
        s.Metadata = md;
        s.save();
        g = findall(fig, '-isa', 'matlab.ui.componentcontainer.ComponentContainer');
        say('%s saved (%s order %s); Gauge ComponentCode: %s', name, gaugeClass, strjoin(order, ','), strjoin(g(1).DesignTimeProperties.ComponentCode', ' || '));
        rx = 'properties \(Access = public\)\r?\n(.*?\r?\n)*?\s*end\r?\n';
        t = regexp(txt, rx, 'tokens');
        say('%s validator regex token: %s', name, strrep(t{1}{1}, newline, '\n'));
        delete(fig);
    catch e, oops([name ' save'], e); end
end

function partsOf(ctx, fn, name, gaugeClass)
    try
        zdir = fullfile(ctx.out, [name '_parts']);
        zcopy = fullfile(ctx.out, [name '.zip']);
        copyfile(fn, zcopy); unzip(zcopy, zdir); delete(zcopy);
        xml = fileread(fullfile(zdir, 'metadata', 'appMetadata.xml'));
        say('%s appMetadata.xml: %s', name, oneline(xml));
        say('%s userComponents element: %s', name, regexp(xml, '<userComponents\s*/>|<userComponents>.*?</userComponents>', 'match', 'once'));
        doc = fileread(fullfile(zdir, 'matlab', 'document.xml'));
        say('%s document.xml has the class line: %d', name, contains(doc, ['classdef ' name]));
        mat = fullfile(zdir, 'appdesigner', 'appModel.mat');
        fid = fopen(mat, 'r'); head = fread(fid, 116, '*char')'; fclose(fid);
        say('%s appModel.mat header: %s', name, strtrim(head));
        S = load(mat);
        say('%s appModel.mat variables: %s; components fields %s; code fields %s', name, strjoin(fieldnames(S)', ','), ...
            strjoin(fieldnames(S.components)', ','), strjoin(fieldnames(S.code)', ','));
        f = S.components.UIFigure;
        kids = f.Children;
        say('%s loaded: class(UIFigure.Children) %s; each %s', name, class(kids), strjoin(arrayfun(@class, kids, 'UniformOutput', false)', ','));
        g = kids(arrayfun(@(k) isa(k, 'matlab.ui.componentcontainer.ComponentContainer'), kids));
        if ~isempty(g)
            say('%s loaded gauge: Value %g, Limits %s, Position %s, SetupCalls %d, UpdateCalls %d, ValueChangedFcn %s, has DTP %d', name, ...
                g.Value, mat2str(g.Limits), mat2str(g.Position), g.SetupCalls, g.UpdateCalls, v2s(g.ValueChangedFcn), isprop(g, 'DesignTimeProperties'));
            drawnow;
            say('%s loaded gauge after drawnow: UpdateCalls %d; DTP fields %s', name, g.UpdateCalls, strjoin(fieldnames(g.DesignTimeProperties)', ','));
        end
        delete(f);
        raw = fullfile(ctx.out, [name '_model_raw.mat']);
        [st, o] = system(sprintf('"%s" -I "%s" "%s" "%s"', ctx.py, ctx.inflate, mat, raw));
        say('%s inflate (%d): %s', name, st, oneline(o));
        if st == 0, dumpMat(ctx, raw, gaugeClass, [name ' appModel.mat']); end
    catch e, oops([name ' parts'], e); end
end

function dumpMat(ctx, file, className, label)
    % python ..\ds0\ds0_tree_dump.py <file>; prints the lines of the user class's objects
    [st, o] = system(sprintf('"%s" -I "%s" "%s"', ctx.py, ctx.dump, file));
    if st ~= 0, say('%s dump failed (%d): %s', label, st, oneline(o)); return; end
    L = splitlines(o);
    for k = 1:numel(L)
        if contains(L{k}, className) || startsWith(L{k}, 'per class') || startsWith(L{k}, '==')
            t = L{k}; if numel(t) > 1500, t = [t(1:1500) ' ...']; end
            say('dump %s| %s', label, strrep(t, ctx.out, '<out>'));
        end
    end
end

function readApp(~, fn, label)
    % App Designer's reader, as ds6_verify opens files
    try
        lastwarn('');
        des = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fn);
        a = des.getAppData();
        if isempty(des.Warnings)
            say('%s read: no warnings', label);
        end
        for w = des.Warnings
            say('%s read warning %s: %s', label, w.Id, jsonencode(w.Info));
        end
        f = a.components.UIFigure;
        comps = findall(f, '-property', 'DesignTimeProperties');
        say('%s read: components %s', label, strjoin(arrayfun(@(c) c.DesignTimeProperties.CodeName, comps, 'UniformOutput', false)', ','));
        for c = comps'
            if isa(c, 'matlab.ui.componentcontainer.ComponentContainer')
                info = sprintf('class %s, Value %s, ValueChangedFcn %s', class(c), v2s(c.Value), v2s(c.ValueChangedFcn));
                if isprop(c, 'Limits'), info = [info ', Limits ' mat2str(c.Limits)]; end %#ok<AGROW>
                if isprop(c, 'SetupCalls'), info = sprintf('%s, SetupCalls %d, UpdateCalls %d', info, c.SetupCalls, c.UpdateCalls); end
                say('%s read %s: %s', label, c.DesignTimeProperties.CodeName, info);
                drawnow;
                if isprop(c, 'UpdateCalls'), say('%s read %s after drawnow: UpdateCalls %d', label, c.DesignTimeProperties.CodeName, c.UpdateCalls); end
            end
        end
        if isfield(a.code, 'Callbacks')
            for k = 1:numel(a.code.Callbacks)
                say('%s read callback %s -> %s', label, a.code.Callbacks(k).Name, jsonencode(a.code.Callbacks(k).ComponentData));
            end
        end
        [w, wid] = lastwarn();
        if ~isempty(w), say('%s read lastwarn %s | %s', label, wid, oneline(w)); end
        delete(f);
    catch e, oops([label ' read'], e); end
end

% ------------------------------------------------------------------------------------------------
function ds7Errored(ctx)
    say('== 6 errored class');
    saveApp(ctx, 'UacFail', 'SgFail', {'UIFigure', 'GoButton', 'Gauge'});
    setappdata(groot, 'ds7_fail', true);
    c = onCleanup(@() rmappdata(groot, 'ds7_fail'));
    readApp(ctx, fullfile(ctx.out, 'UacFail.mlapp'), 'UacFail (setup refuses)');
    try
        lastwarn('');
        zdir = fullfile(ctx.out, 'UacFail_parts'); zcopy = fullfile(ctx.out, 'UacFail.zip');
        copyfile(fullfile(ctx.out, 'UacFail.mlapp'), zcopy); unzip(zcopy, zdir); delete(zcopy);
        S = load(fullfile(zdir, 'appdesigner', 'appModel.mat'));
        [w, wid] = lastwarn();
        say('UacFail load(): children %s; lastwarn %s | %s', strjoin(arrayfun(@class, S.components.UIFigure.Children, 'UniformOutput', false)', ','), wid, oneline(w));
        delete(S.components.UIFigure);
    catch e, oops('UacFail load', e); end
end

% ------------------------------------------------------------------------------------------------
function ds7PlainText(ctx)
    say('== 7 plain text');
    flag0 = feature('AppDesignerPlainTextFileFormat');
    restore = onCleanup(@() feature('AppDesignerPlainTextFileFormat', flag0));
    addpath(ctx.plain);
    writeLines(fullfile(ctx.plain, 'UacPlain.m'), plainApp('UacPlain', {
        '      <SpinnerGauge name=''Gauge''>'
        '        <Limits>[0 100]</Limits>'
        '        <Position>[10 10 220 80]</Position>'
        '        <Value>40</Value>'
        '        <ValueChangedFcn>GaugeValueChanged</ValueChangedFcn>'
        '      </SpinnerGauge>'
        '      <U10Probe name=''Probe''>'
        '        <Position>[10 120 120 40]</Position>'
        '        <Value>7</Value>'
        '        <ValueChangedFcn>ProbeValueChanged</ValueChangedFcn>'
        '      </U10Probe>'
        '      <Button name=''GoButton''>'
        '        <ButtonPushedFcn>GoButtonPushed</ButtonPushedFcn>'
        '        <Position>[260 40 100 22]</Position>'
        '        <Text>''Go''</Text>'
        '      </Button>'}, {'Gauge     SpinnerGauge', 'Probe     U10Probe', 'GoButton  matlab.ui.control.Button'}));
    writeLines(fullfile(ctx.plain, 'UacPkgShort.m'), plainApp('UacPkgShort', {
        '      <PkgGauge name=''PG''>'
        '        <Value>5</Value>'
        '      </PkgGauge>'}, {'PG        ds7pkg.PkgGauge'}));
    writeLines(fullfile(ctx.plain, 'UacPkgFull.m'), plainApp('UacPkgFull', {
        '      <ds7pkg.PkgGauge name=''PG''>'
        '        <Value>5</Value>'
        '      </ds7pkg.PkgGauge>'}, {'PG        ds7pkg.PkgGauge'}));
    writeLines(fullfile(ctx.plain, 'UacPkgBare.m'), plainApp('UacPkgBare', {
        '      <PkgGauge name=''PG''/>'}, {'PG        ds7pkg.PkgGauge'}));
    feature('AppDesignerPlainTextFileFormat', 0);
    for n = {'UacPlain', 'UacPkgShort', 'UacPkgFull', 'UacPkgBare'}
        plainRead(fullfile(ctx.plain, [n{1} '.m']), ['off: ' n{1}]);
    end
    feature('AppDesignerPlainTextFileFormat', 1);
    say('feature now %d', feature('AppDesignerPlainTextFileFormat'));
    u10_log();
    try
        app = UacPlain;
        g = app.Gauge;
        say('on: run %s; Gauge %s Value %g Limits %s Position %s SetupCalls %d UpdateCalls %d', class(app), class(g), g.Value, mat2str(g.Limits), mat2str(g.Position), g.SetupCalls, g.UpdateCalls);
        say('on: Gauge ValueChangedFcn %s; AD_CodeName %d; DesignTimeProperties %d', v2s(g.ValueChangedFcn), isprop(g, 'AD_CodeName'), isprop(g, 'DesignTimeProperties'));
        drawnow;
        say('on: Gauge UpdateCalls after drawnow %d', g.UpdateCalls);
        say('on: Probe Value %g, ValueChangedFcn %s; log %s', app.Probe.Value, v2s(app.Probe.ValueChangedFcn), u10_log());
        app.Probe.fire('ValueChanged');
        say('on: after Probe fire: app.Log %s', strjoin(app.Log, ','));
        try
            f = g.ValueChangedFcn; f(g, []);
            say('on: after calling the gauge callback by hand: app.Log %s', strjoin(app.Log, ','));
        catch e, oops('on: gauge callback by hand', e); end
        delete(app);
    catch e, oops('on: run UacPlain', e); end
    for n = {'UacPkgShort', 'UacPkgFull', 'UacPkgBare'}
        try
            app = feval(n{1});
            say('on: run %s: PG %s Value %g', n{1}, class(app.PG), app.PG.Value);
            delete(app);
        catch e, oops(['on: run ' n{1}], e); end
    end
    plainRead(fullfile(ctx.plain, 'UacPlain.m'), 'on: UacPlain');
end

function L = plainApp(name, children, decls)
    L = [{['classdef ' name ' < matlab.apps.App'], '', '    properties (Access = public)', '        UIFigure  matlab.ui.Figure'}, ...
        cellfun(@(d) ['        ' d], decls, 'UniformOutput', false), ...
        {'    end', '', '    properties (Access = public)', '        Log = {}', '    end', '', '    methods (Access = private)', '', ...
        '        function GaugeValueChanged(app, event)', '            app.Log{end+1} = sprintf(''gauge %g %s'', app.Gauge.Value, class(event));', '        end', '', ...
        '        function ProbeValueChanged(app, event)', '            app.Log{end+1} = sprintf(''probe %g %s'', app.Probe.Value, class(event));', '        end', '', ...
        '        function GoButtonPushed(app, event)', '            app.Log{end+1} = ''go'';', '        end', '    end', 'end', '', ...
        '%[appendix]', '%[app:layout]', '%{', '<Components>', '  <UIFigure name=''UIFigure''>', '    <Name>''UAC plain''</Name>', ...
        '    <Position>[100 100 400 260]</Position>', '    <Children>'}, children(:)', ...
        {'    </Children>', '  </UIFigure>', '</Components>', '%}'}];
end

function plainRead(fn, label)
    try
        des = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fn);
        a = des.getAppData();
        say('%s read: deserializer %s, warnings %d', label, class(des), numel(des.Warnings));
        f = a.components.UIFigure;
        comps = findall(f, '-property', 'DesignTimeProperties');
        say('%s read: components %s', label, strjoin(arrayfun(@(c) [c.DesignTimeProperties.CodeName ':' class(c)], comps, 'UniformOutput', false)', ','));
        for c = comps'
            if isa(c, 'matlab.ui.componentcontainer.ComponentContainer')
                info = sprintf('Value %s, ValueChangedFcn %s, Position %s', v2s(c.Value), v2s(c.ValueChangedFcn), mat2str(c.Position));
                if isprop(c, 'Limits'), info = [info ', Limits ' mat2str(c.Limits)]; end %#ok<AGROW>
                say('%s read %s: %s; DTP %s', label, c.DesignTimeProperties.CodeName, info, v2s(c.DesignTimeProperties));
            end
        end
        if isfield(a.code, 'Callbacks') && ~isempty(a.code.Callbacks)
            for k = 1:numel(a.code.Callbacks)
                cd = ''; if isfield(a.code.Callbacks, 'ComponentData'), cd = jsonencode(a.code.Callbacks(k).ComponentData); end
                say('%s read callback %s -> %s', label, a.code.Callbacks(k).Name, cd);
            end
        end
        delete(f);
    catch e, oops([label ' read'], e); end
end

% ------------------------------------------------------------------------------------------------
function ds7Missing(ctx)
    say('== 8 missing class');
    % (a) a never-loaded class: a copy of UacLast/UacMid whose every 'SpinnerGauge' is 'SpinnerGaugX'
    for app = {'UacLast', 'UacMid'}
        src = fullfile(ctx.out, [app{1} '.mlapp']);
        dst = fullfile(ctx.out, [app{1} 'X.mlapp']);
        [st, o] = system(sprintf('"%s" -I "%s" "%s" "%s" SpinnerGauge SpinnerGaugX', ctx.py, ctx.rename, src, dst));
        say('%sX rename (%d): %s', app{1}, st, oneline(o));
        if st == 0
            say('%sX: meta.class.fromName(''SpinnerGaugX'') empty %d', app{1}, isempty(meta.class.fromName('SpinnerGaugX')));
            readApp(ctx, dst, [app{1} 'X (never loaded)']);
            try
                lastwarn('');
                zdir = fullfile(ctx.out, [app{1} 'X_parts']); zcopy = fullfile(ctx.out, [app{1} 'X.zip']);
                copyfile(dst, zcopy); unzip(zcopy, zdir); delete(zcopy);
                S = load(fullfile(zdir, 'appdesigner', 'appModel.mat'));
                [w, wid] = lastwarn();
                say('%sX load(): children %s; lastwarn %s | %s', app{1}, strjoin(arrayfun(@class, S.components.UIFigure.Children, 'UniformOutput', false)', ','), wid, oneline(w));
                delete(S.components.UIFigure);
            catch e, oops([app{1} 'X load'], e); end
        end
    end
    % (b) SpinnerGauge itself taken off the path
    try
        rmpath(ctx.sg);
        lastwarn('');
        clear SpinnerGauge
        rehash;
        [w, wid] = lastwarn();
        say('off path: exist %d, which %s, meta empty %d; lastwarn %s | %s', exist('SpinnerGauge', 'class'), which('SpinnerGauge'), isempty(meta.class.fromName('SpinnerGauge')), wid, oneline(w));
    catch e, oops('off path', e); end
    for app = {'UacLast', 'UacMid'}
        readApp(ctx, fullfile(ctx.out, [app{1} '.mlapp']), [app{1} ' (off path)']);
    end
    flag0 = feature('AppDesignerPlainTextFileFormat');
    feature('AppDesignerPlainTextFileFormat', 0);
    plainRead(fullfile(ctx.plain, 'UacPlain.m'), 'off: UacPlain (off path)');
    feature('AppDesignerPlainTextFileFormat', flag0);
    addpath(ctx.sg);
end

% ------------------------------------------------------------------------------------------------
function say(varargin)
    fprintf('%s\n', sprintf(varargin{:}));
end

function oops(label, e)
    msg = sprintf('%s ERROR %s | %s', label, e.identifier, oneline(e.message));
    if ~isempty(e.cause), msg = sprintf('%s | cause %s: %s', msg, e.cause{1}.identifier, oneline(e.cause{1}.message)); end
    if ~isempty(e.stack), msg = sprintf('%s | at %s:%d', msg, e.stack(1).name, e.stack(1).line); end
    fprintf('%s\n', msg);
end

function s = oneline(s)
    s = strrep(strrep(char(s), char(13), ''), newline, ' | ');
end

function s = rel(ctx, p)
    s = strrep(char(p), ctx.here, '<ds7>');
    s = strrep(s, matlabroot, '<matlabroot>');
end

function writeLines(fn, L)
    fid = fopen(fn, 'w', 'n', 'UTF-8');
    fprintf(fid, '%s\n', L{:});
    fclose(fid);
end

function s = v2s(x)
    try
        if isa(x, 'matlab.lang.OnOffSwitchState'), s = ['onoff ' char(x)]; return; end
        if ischar(x), s = ['''' oneline(x) '''']; if size(x, 1) > 1, s = ['char ' mat2str(size(x))]; end; return; end
        if isstring(x), s = char(jsonencode(x)); return; end
        if isa(x, 'function_handle'), s = ['@' func2str(x)]; return; end
        if isenum(x), s = [class(x) '.' char(x)]; return; end
        if isnumeric(x) || islogical(x)
            s = mat2str(x, 6);
            if ~isa(x, 'double') && ~islogical(x), s = [class(x) '(' s ')']; end
            return;
        end
        if isdatetime(x), s = ['datetime ' char(string(x))]; return; end
        if iscell(x), s = ['cell' mat2str(size(x)) ' ' char(jsonencode(x))]; return; end
        if isstruct(x), s = ['struct ' char(jsonencode(x))]; if numel(s) > 400, s = [s(1:400) '...']; end; return; end
        s = ['<' class(x) ' ' mat2str(size(x)) '>'];
    catch
        s = ['<' class(x) '>'];
    end
end
