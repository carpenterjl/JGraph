% probe_mlapp.m - how R2025b runs a .mlapp; everything stays invisible.
here = pwd;
diary(fullfile(here, 'probe_mlapp.out')); diary on
cleanupFigs = onCleanup(@() delete(findall(groot, 'Type', 'figure')));
set(groot, 'DefaultFigureVisible', 'off');

fprintf('--- feature flag\n');
try
    fprintf('AppDesignerPlainTextFileFormat = %d\n', matlab.internal.feature('AppDesignerPlainTextFileFormat'));
catch e, fprintf('flag err: %s\n', e.message); end

fprintf('--- appModel.mat of a shipped app\n');
try
    tmp = fullfile(tempdir, 'probe_appModel.mat');
    z = fullfile(here, 'unz_app1', 'appdesigner', 'appModel.mat');
    copyfile(z, tmp);
    w = whos('-file', tmp);
    for k = 1:numel(w), fprintf('  var %s class %s size %s\n', w(k).name, w(k).class, mat2str(w(k).size)); end
    warning('off', 'all');
    s = load(tmp);
    fn = fieldnames(s);
    for k = 1:numel(fn)
        v = s.(fn{k});
        fprintf('  %s: %s\n', fn{k}, class(v));
        if isstruct(v)
            f2 = fieldnames(v);
            fprintf('    fields: %s\n', strjoin(f2', ', '));
            for j = 1:numel(f2)
                vv = v.(f2{j});
                fprintf('      %s -> %s %s\n', f2{j}, class(vv), mat2str(size(vv)));
            end
        end
    end
    warning('on', 'all');
catch e, fprintf('appModel err: %s\n', e.message); end

fprintf('--- minimal mlapp (no appModel.mat)\n');
addpath(fullfile(here, 'build', 'min'));
addpath(fullfile(here, 'build', 'bare'));
try
    fprintf('which: %s\n', which('ProbeApp'));
    fprintf('exist: %d\n', exist('ProbeApp'));
    c = matlab.internal.getCode(which('ProbeApp'));
    fprintf('getCode first line: %s\n', strtok(c, newline));
catch e, fprintf('which/getCode err: %s\n', e.message); end
try
    mc = ?ProbeApp;
    fprintf('superclass: %s\n', mc.SuperclassList(1).Name);
    fprintf('methods: %s\n', strjoin(sort({mc.MethodList.Name}), ', '));
catch e, fprintf('metaclass err: %s\n', e.message); end

try
    app = ProbeApp(5);
    fprintf('class(app) = %s, isa AppBase = %d\n', class(app), isa(app, 'matlab.apps.AppBase'));
    fprintf('figure visible = %s\n', app.UIFigure.Visible);
    fprintf('fig has RunningAppInstance = %d, same = %d\n', isprop(app.UIFigure, 'RunningAppInstance'), app.UIFigure.RunningAppInstance == app);
    fprintf('fig RunningInstanceFullFileName = %s\n', app.UIFigure.RunningInstanceFullFileName);
    fprintf('HandleVisibility = %s\n', app.UIFigure.HandleVisibility);
    p = peek(app); fprintf('after startup Count=%d Log=%s\n', p.Count, strjoin(p.Log, ','));
    cb = app.AddButton.ButtonPushedFcn;
    fprintf('ButtonPushedFcn class %s : %s\n', class(cb), func2str(cb));
    cb(app.AddButton, 'fake-event');
    p = peek(app); fprintf('after cb Count=%d Log=%s\n', p.Count, strjoin(p.Log, ','));
    fprintf('label = %s\n', app.CountLabel.Text);
    % a second construction: getRunningApp makes it a singleton in this probe class
    app2 = ProbeApp(100);
    fprintf('second construction returns same object = %d\n', app2 == app);
    % private method from outside
    try, refresh(app); fprintf('private refresh callable from outside!\n');
    catch e, fprintf('private refresh refused: %s\n', e.identifier); end
    try, app.Count = 3; fprintf('private prop write allowed!\n');
    catch e, fprintf('private prop write refused: %s\n', e.identifier); end
    % CloseRequestFcn -> delete(app) -> delete(fig)
    fig = app.UIFigure;
    close(fig);
    fprintf('after close: isvalid(app)=%d isvalid(fig)=%d\n', isvalid(app), isvalid(fig));
catch e
    fprintf('run err: %s | %s\n', e.identifier, e.message);
    for k = 1:numel(e.stack), fprintf('   at %s:%d\n', e.stack(k).name, e.stack(k).line); end
end

try
    % nargout == 0: app object cleared from the constructor, but the app lives on
    ProbeApp;
    figs = findall(groot, 'Type', 'figure', '-property', 'RunningAppInstance');
    fprintf('bare call: running app figures = %d, app valid = %d\n', numel(figs), isvalid(figs(1).RunningAppInstance));
    a = figs(1).RunningAppInstance;
    delete(figs(1));
    fprintf('delete(fig) -> isvalid(app) = %d\n', isvalid(a));
catch e, fprintf('bare call err: %s\n', e.message); end

try
    b = ProbeBare();
    fprintf('bare zip (document.xml only) runs: class %s\n', class(b));
    delete(b);
catch e, fprintf('bare zip err: %s | %s\n', e.identifier, e.message); end

fprintf('--- typed property default\n');
try
    mc = ?ProbeApp;
    pl = mc.PropertyList;
    for k = 1:numel(pl)
        if strcmp(pl(k).Name, 'UIFigure')
            fprintf('UIFigure: HasDefault=%d Validation class=%s\n', pl(k).HasDefault, pl(k).Validation.Class.Name);
        end
    end
catch e, fprintf('meta err: %s\n', e.message); end

fprintf('--- AppBase meta\n');
mc = ?matlab.apps.AppBase;
for k = 1:numel(mc.MethodList)
    m = mc.MethodList(k);
    if ischar(m.Access), acc = m.Access; else, acc = 'class-list'; end
    if ~strcmp(m.DefiningClass.Name, 'handle')
        fprintf('  %s Access=%s Sealed=%d Static=%d\n', m.Name, acc, m.Sealed, m.Static);
    end
end
diary off
