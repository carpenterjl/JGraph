% DS0 (designer plan): what App Designer's load makes of an .mlapp whose component tree
% (appModel.mat's `components`) disagrees with its code (matlab/document.xml). The files are
% written with R2025b's own serializer (MLAPPSerializer), from the class text of the U7b app and a
% tree built here; rt/ (ignored) holds them. For each: the canvas App Designer's full load returns
% (the components carrying DesignTimeProperties, their code names and a button's text), the
% callbacks' ComponentData it restores from that tree, and what the class itself makes when run.
if ~exist('rt', 'dir'), mkdir('rt'); end
folder = fullfile(pwd, 'rt', 'stale');
if ~exist(folder, 'dir'), mkdir(folder); end
addpath(folder);
base = strrep(fileread(fullfile(pwd, '..', 'u7b', 'U7bApp.txt')), sprintf('\r\n'), newline);
if base(end) == newline, base(end) = []; end

cases = {
    'DsS0', 'consistent: the tree is what the code makes'
    'DsS1', 'the code makes ClearButton, the tree lacks it'
    'DsS2', 'the tree has MoreButton, the code does not make it'
    'DsS3', 'the tree says GoButton.Text Run, the code says Go'
    'DsS4', 'consistent, but no component carries DesignTimeProperties'
    };
for k = 1:size(cases, 1)
    name = cases{k, 1};
    fprintf('=== %s: %s\n', name, cases{k, 2});
    text = strrep(base, 'U7bApp', name);
    fig = uifigure('Visible', 'off', 'Position', [100 100 320 160], 'Name', 'U7b App');
    g = uigridlayout(fig, 'ColumnWidth', {'1x', '1x'}, 'RowHeight', {22, 22});
    amp = uieditfield(g, 'numeric', 'Tag', 'amp', 'Value', 2);
    amp.Layout.Row = 1; amp.Layout.Column = [1 2];
    goText = 'Go'; if strcmp(name, 'DsS3'), goText = 'Run'; end
    go = uibutton(g, 'push', 'Tag', 'go', 'Text', goText, 'ButtonPushedFcn', 'GoButtonPushed');
    go.Layout.Row = 2; go.Layout.Column = 1;
    comps = {fig, 'UIFigure'; g, 'GridLayout'; amp, 'AmpField'; go, 'GoButton'};
    if ~strcmp(name, 'DsS1')
        clr = uibutton(g, 'push', 'Tag', 'clear', 'Text', 'Clear', 'ButtonPushedFcn', 'ClearButtonPushed');
        clr.Layout.Row = 2; clr.Layout.Column = 2;
        comps(end+1, :) = {clr, 'ClearButton'};
    end
    if strcmp(name, 'DsS2')
        more = uibutton(fig, 'push', 'Text', 'More', 'Position', [10 10 60 22]);
        comps(end+1, :) = {more, 'MoreButton'};
    end
    if ~strcmp(name, 'DsS4')
        for c = 1:size(comps, 1)
            addprop(comps{c, 1}, 'DesignTimeProperties');
            comps{c, 1}.DesignTimeProperties = struct('CodeName', comps{c, 2}, 'GroupId', '', 'ComponentCode', {{}});
        end
    end
    fn = fullfile(folder, [name '.mlapp']);
    if exist(fn, 'file'), delete(fn); end
    s = appdesigner.internal.serialization.MLAPPSerializer(fn, fig);
    s.OverwriteTargetFile = true;
    s.MatlabCodeText = text;
    s.Callbacks = struct('Name', {'GoButtonPushed', 'ClearButtonPushed'}, 'Code', {{}, {}});
    s.StartupCallback = struct('Name', 'startupFcn', 'Code', {{}});
    s.InputParameters = 'amp';
    try
        s.save();
    catch e
        fprintf('SAVE ERROR %s | %s\n', e.identifier, e.message); delete(fig); continue
    end
    delete(fig);

    try
        des = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fn);
        a = des.getAppData();
        f = a.components.UIFigure;
        dtp = findall(f, '-property', 'DesignTimeProperties');
        names = arrayfun(@(c) c.DesignTimeProperties.CodeName, dtp, 'UniformOutput', false);
        fprintf('canvas: %d components in the tree, %d with DesignTimeProperties: %s\n', numel(findall(f)), numel(dtp), strjoin(names', ','));
        b = findall(f, 'Type', 'uibutton');
        fprintf('canvas buttons: %s\n', strjoin(arrayfun(@(x) x.Text, b, 'UniformOutput', false)', ','));
        for c = 1:numel(a.code.Callbacks)
            cd = a.code.Callbacks(c).ComponentData;
            if isempty(cd), w = '(none)'; else, w = strjoin(arrayfun(@(x) sprintf('%s.%s', x.CodeName, x.CallbackPropertyName), cd, 'UniformOutput', false), ','); end
            fprintf('callback %s bound to: %s\n', a.code.Callbacks(c).Name, w);
        end
        if ~isempty(des.Warnings), fprintf('warnings: %s\n', strjoin(arrayfun(@(x) char(string(x.Id)), des.Warnings, 'UniformOutput', false), ',')); end
        delete(f);
    catch e
        fprintf('load ERROR %s | %s\n', e.identifier, strrep(e.message, newline, ' '));
    end
    try
        app = feval(name, 5);
        props = {'GridLayout', 'AmpField', 'GoButton', 'ClearButton'};
        has = props(cellfun(@(p) isprop(app, p) && ~isempty(app.(p)) && isvalid(app.(p)), props));
        fprintf('run: makes %s; Go text %s\n', strjoin(has, ','), app.GoButton.Text);
        delete(app);
    catch e
        fprintf('run ERROR %s | %s\n', e.identifier, strrep(e.message, newline, ' '));
    end
end
