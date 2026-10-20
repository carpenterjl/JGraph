% DS7b (designer plan): apps holding a custom component, saved by JGraph's designer, in R2025b,
% headless. Run from tools\matlab-checklist\ui-probes\ds7 with
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name ds7_uac_verify -TimeoutSec 300
% after the local test TheProbesAppIsWrittenWhenAsked (JGRAPH_DS7_OUT = this folder's rt) has left in
% rt\uac\jgraph: UacDesigned.mlapp (saved whole from its code), UacEdited.mlapp (the same app saved
% again over its tree with the gauge moved, its value changed and its callback bound to another
% function), UacDesignedPlain.m (the plain-text copy), and the gauge's class SpinnerGauge.m beside
% them. One printed line per finding. For each .mlapp:
%  1. App Designer's reader loads it: warnings, components by code name, the gauge's class and
%     values, each callback's ComponentData; the package's userComponents element.
%  2. load() of appModel.mat: the children's classes; the gauge object against a control made as
%     App Designer makes one with the expected values (ds1b_objdiff, DesignTimeProperties and the
%     class's counters left out).
%  3. The app runs from the .mlapp: the gauge's class and value, its callback bound and run, delete.
% Then the plain-text file read (feature off) and run (feature on).
warning('off', 'backtrace');
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, '..', 'ds1b'));
folder = fullfile(here, 'rt', 'uac', 'jgraph');
previous = cd(folder);
restore = onCleanup(@() cd(previous));
addpath(folder);
checkApp(folder, 'UacDesigned', 40, [20 20 220 80], 'GaugeValueChanged');
checkApp(folder, 'UacEdited', 55, [30 40 200 70], 'GoButtonPushed');

flag0 = feature('AppDesignerPlainTextFileFormat');
restoreFeature = onCleanup(@() feature('AppDesignerPlainTextFileFormat', flag0));
try
    feature('AppDesignerPlainTextFileFormat', 0);
    des = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fullfile(folder, 'UacDesignedPlain.m'));
    a = des.getAppData();
    f = a.components.UIFigure;
    comps = findall(f, '-property', 'DesignTimeProperties');
    say('plain read: components %s', strjoin(arrayfun(@(c) [c.DesignTimeProperties.CodeName ':' class(c)], comps, 'UniformOutput', false)', ','));
    g = findall(f, '-isa', 'matlab.ui.componentcontainer.ComponentContainer');
    say('plain read gauge: Value %g, Position %s, ValueChangedFcn %s', g.Value, mat2str(g.Position), v2s(g.ValueChangedFcn));
    delete(f);
catch e, oops('plain read', e); end
try
    feature('AppDesignerPlainTextFileFormat', 1);
    r = UacDesignedPlain;
    say('plain run: Gauge %s Value %g, ValueChangedFcn %s', class(r.Gauge), r.Gauge.Value, class(r.Gauge.ValueChangedFcn));
    delete(r);
catch e, oops('plain run', e); end
delete(findall(groot, 'Type', 'figure'));
say('done');

function checkApp(folder, app, value, position, callback)
    fn = fullfile(folder, [app '.mlapp']);
    % --- 1. App Designer's reader -------------------------------------------------------------
    try
        des = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fn);
        a = des.getAppData();
        if isempty(des.Warnings), say('%s read: no warnings', app); end
        for w = des.Warnings, say('%s read warning %s: %s', app, w.Id, jsonencode(w.Info)); end
        f = a.components.UIFigure;
        comps = findall(f, '-property', 'DesignTimeProperties');
        say('%s read: components %s', app, strjoin(arrayfun(@(c) [c.DesignTimeProperties.CodeName ':' class(c)], comps, 'UniformOutput', false)', ','));
        g = findall(f, '-isa', 'matlab.ui.componentcontainer.ComponentContainer');
        say('%s read gauge: class %s, Value %g, Limits %s, Position %s, ValueChangedFcn %s, SetupCalls %d, UpdateCalls %d', ...
            app, class(g), g.Value, mat2str(g.Limits), mat2str(g.Position), v2s(g.ValueChangedFcn), g.SetupCalls, g.UpdateCalls);
        say('%s read gauge: ComponentCode %s', app, strjoin(g.DesignTimeProperties.ComponentCode', ' || '));
        for k = 1:numel(a.code.Callbacks)
            say('%s read callback %s -> %s', app, a.code.Callbacks(k).Name, jsonencode(a.code.Callbacks(k).ComponentData));
        end
        delete(f);
    catch e, oops([app ' read'], e); end
    zdir = fullfile(folder, [app '_parts']);
    try
        zcopy = fullfile(folder, [app '.zip']);
        if isfolder(zdir), rmdir(zdir, 's'); end
        copyfile(fn, zcopy); unzip(zcopy, zdir); delete(zcopy);
        xml = fileread(fullfile(zdir, 'metadata', 'appMetadata.xml'));
        say('%s userComponents element: %s', app, regexp(xml, '<userComponents\s*/>|<userComponents>.*?</userComponents>', 'match', 'once'));
    catch e, oops([app ' parts'], e); end
    % --- 2. load() against a control ----------------------------------------------------------
    try
        S = load(fullfile(zdir, 'appdesigner', 'appModel.mat'));
        kids = S.components.UIFigure.Children;
        say('%s load: children %s', app, strjoin(arrayfun(@class, kids, 'UniformOutput', false)', ','));
        mine = kids(arrayfun(@(k) isa(k, 'matlab.ui.componentcontainer.ComponentContainer'), kids));
        ctrl = uifigure('Visible', 'off', 'Position', [100 100 400 200], 'Name', 'UAC app');
        cg = SpinnerGauge(ctrl); cg.Value = value; cg.ValueChangedFcn = callback; cg.Position = position;
        drawnow;
        diffs = ds1b_objdiff(mine, cg);
        diffs = diffs(~contains(diffs, 'DesignTimeProperties') & ~contains(diffs, 'SetupCalls') & ~contains(diffs, 'UpdateCalls'));
        if isempty(diffs), say('%s load gauge: same as a control made in MATLAB (Value %g, Position %s, callback %s)', app, value, mat2str(position), callback);
        else, say('%s load gauge: %d differences: %s', app, numel(diffs), strjoin(diffs, ' ; ')); end
        delete(ctrl); delete(S.components.UIFigure);
    catch e, oops([app ' load'], e); end
    % --- 3. run --------------------------------------------------------------------------------
    try
        r = feval(app);
        say('%s run: %s, Gauge %s Value %g, ValueChangedFcn %s', app, class(r), class(r.Gauge), r.Gauge.Value, class(r.Gauge.ValueChangedFcn));
        f = r.Gauge.ValueChangedFcn;
        evalc('f(r.Gauge, [])');
        say('%s run: callback ran', app);
        delete(r);
        say('%s run: deleted', app);
    catch e, oops([app ' run'], e); end
end

function say(varargin)
    fprintf('%s\n', sprintf(varargin{:}));
end

function oops(label, e)
    fprintf('%s ERROR %s | %s\n', label, e.identifier, strrep(strtrim(e.message), newline, ' | '));
    for s = 1:min(3, numel(e.stack)), fprintf('   at %s:%d\n', e.stack(s).name, e.stack(s).line); end
end

function s = v2s(x)
    if ischar(x), s = ['''' x '''']; elseif isa(x, 'function_handle'), s = ['@' func2str(x)]; else, s = class(x); end
end
