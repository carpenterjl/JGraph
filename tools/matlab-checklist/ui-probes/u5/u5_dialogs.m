function u5_dialogs
% U5 probe: uialert, uiconfirm, uiprogressdlg, focus and uiaxes without a display. Headless.
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);
f = figure('Visible', 'off');
b = uibutton(uf);

%% on an invisible figure
tryp('uialert invisible', @() uialert(uf, 'm', 't'));
tryp('uialert no args', @() uialert());
tryp('uialert one arg', @() uialert(uf));
tryp('uialert two args', @() uialert(uf, 'm'));
tryp('uialert number', @() uialert(5.5, 'm', 't'));
tryp('uialert classic figure', @() uialert(f, 'm', 't'));
tryp('uialert component', @() uialert(b, 'm', 't'));
tryp('uialert bad option on invisible', @() uialert(uf, 'm', 't', 'Bogus', 1));
tryp('uialert bad icon on invisible', @() uialert(uf, 'm', 't', 'Icon', 'bogus'));
tryp('uialert numeric message on invisible', @() uialert(uf, 5, 't'));
tryp('uiconfirm invisible', @() uiconfirm(uf, 'm', 't'));
tryp('uiconfirm no args', @() uiconfirm());
tryp('uiconfirm classic figure', @() uiconfirm(f, 'm', 't'));
tryp('uiconfirm number', @() uiconfirm(5.5, 'm', 't'));
tryp('uiprogressdlg invisible', @() uiprogressdlg(uf));
tryp('uiprogressdlg no args', @() uiprogressdlg());
tryp('uiprogressdlg classic figure', @() uiprogressdlg(f));
tryp('uiprogressdlg number', @() uiprogressdlg(5.5));
tryp('uiprogressdlg bad option on invisible', @() uiprogressdlg(uf, 'Bogus', 1));
lastwarn('');
tryp('focus invisible figure', @() focus(uf));
[m, id] = lastwarn; fprintf('   lastwarn %s | %s\n', id, oneline(m));
lastwarn('');
tryp('focus component of invisible figure', @() focus(b));
[m, id] = lastwarn; fprintf('   lastwarn %s | %s\n', id, oneline(m));
tryp('focus classic figure', @() focus(f));
tryp('focus number', @() focus(5.5));
tryp('focus no args', @() focus());
tryp('focus label', @() focus(uilabel(uf)));
[m, id] = lastwarn; fprintf('   lastwarn %s | %s\n', id, oneline(m));

%% on a figure that is "visible" with no display
vf = uifigure('Position', [100 100 400 300]);
fprintf('visible uifigure: Visible %s\n', char(vf.Visible));
tryp('uialert visible', @() uialert(vf, 'm', 't'));
tryp('uialert message cell', @() uialert(vf, {'a', 'b'}, 't'));
tryp('uialert message string array', @() uialert(vf, ["a" "b"], "t"));
tryp('uialert message number', @() uialert(vf, 5, 't'));
tryp('uialert title number', @() uialert(vf, 'm', 5));
tryp('uialert bad option', @() uialert(vf, 'm', 't', 'Bogus', 1));
tryp('uialert odd options', @() uialert(vf, 'm', 't', 'Icon'));
icons = {'error', 'warning', 'info', 'success', 'question', 'none', '', 'bogus', 5, 'ERROR', 'err'};
for k = 1:numel(icons), tryp(sprintf('uialert Icon %s', v2s(icons{k})), @() uialert(vf, 'm', 't', 'Icon', icons{k})); end
tryp('uialert Modal false', @() uialert(vf, 'm', 't', 'Modal', false));
tryp('uialert Modal bogus', @() uialert(vf, 'm', 't', 'Modal', 'bogus'));
tryp('uialert Interpreter html', @() uialert(vf, 'm', 't', 'Interpreter', 'html'));
tryp('uialert Interpreter bogus', @() uialert(vf, 'm', 't', 'Interpreter', 'bogus'));
tryp('uialert CloseFcn handle', @() uialert(vf, 'm', 't', 'CloseFcn', @(s, e) disp(1)));
tryp('uialert CloseFcn number', @() uialert(vf, 'm', 't', 'CloseFcn', 5));
tryp('uialert nargout', @() nargout('uialert'));
tryp('uialert with output', @() outof(@() uialert(vf, 'm', 't')));
tryp('uiconfirm visible', @() uiconfirm(vf, 'm', 't'));
tryp('uiconfirm bad option', @() uiconfirm(vf, 'm', 't', 'Bogus', 1));
tryp('uiconfirm Options number', @() uiconfirm(vf, 'm', 't', 'Options', 5));
tryp('uiconfirm five options', @() uiconfirm(vf, 'm', 't', 'Options', {'a', 'b', 'c', 'd', 'e'}));
tryp('uiconfirm DefaultOption 9', @() uiconfirm(vf, 'm', 't', 'DefaultOption', 9));
tryp('uiconfirm DefaultOption text not in options', @() uiconfirm(vf, 'm', 't', 'DefaultOption', 'zz'));
tryp('uiconfirm CancelOption 9', @() uiconfirm(vf, 'm', 't', 'CancelOption', 9));
tryp('uiconfirm Icon bogus', @() uiconfirm(vf, 'm', 't', 'Icon', 'bogus'));
d = [];
tryp('uiprogressdlg visible', @() class(uiprogressdlg(vf)));
try
    d = uiprogressdlg(vf);
    names = fieldnames(get(d));
    fprintf('progress names: %s\n', strjoin(names', ' '));
    for k = 1:numel(names), fprintf('progress default %s : %s\n', names{k}, v2s(get(d, names{k}))); end
    vals = {0.5, 1, 0, -0.1, 1.5, [0.1 0.2], 'a', NaN, int8(1), true, []};
    for k = 1:numel(vals), tryp(sprintf('progress Value <- %s', v2s(vals{k})), @() setget(d, 'Value', vals{k})); end
    texts = {'abc', "str", {'a', 'b'}, ["a"; "b"], 5, '', []};
    for k = 1:numel(texts), tryp(sprintf('progress Message <- %s', v2s(texts{k})), @() setget(d, 'Message', texts{k})); end
    for k = 1:numel(texts), tryp(sprintf('progress Title <- %s', v2s(texts{k})), @() setget(d, 'Title', texts{k})); end
    for k = 1:numel(texts), tryp(sprintf('progress CancelText <- %s', v2s(texts{k})), @() setget(d, 'CancelText', texts{k})); end
    onoff = {'on', 'off', true, 0, 'bogus', [1 0]};
    for name = {'Indeterminate', 'Cancelable', 'ShowPercentage'}
        for k = 1:numel(onoff), tryp(sprintf('progress %s <- %s', name{1}, v2s(onoff{k})), @() setget(d, name{1}, onoff{k})); end
    end
    for k = 1:numel(icons), tryp(sprintf('progress Icon <- %s', v2s(icons{k})), @() setget(d, 'Icon', icons{k})); end
    tryp('progress Interpreter html', @() setget(d, 'Interpreter', 'html'));
    tryp('progress Interpreter bogus', @() setget(d, 'Interpreter', 'bogus'));
    tryp('progress CancelRequested get', @() get(d, 'CancelRequested'));
    tryp('progress CancelRequested set', @() setget(d, 'CancelRequested', true));
    tryp('progress Bogus set', @() setget(d, 'Bogus', 1));
    tryp('progress dot', @() d.Value);
    fprintf('progress isvalid %d class %s\n', isvalid(d), class(d));
    close(d);
    fprintf('after close: isvalid %d\n', isvalid(d));
    tryp('progress Value after close', @() setget(d, 'Value', 0.3));
    tryp('progress close twice', @() close(d));
    d = uiprogressdlg(vf, 'Title', 'T', 'Message', 'M', 'Value', 0.25, 'Cancelable', 'on', 'Indeterminate', 'on');
    fprintf('progress ctor pairs: Title %s Message %s Value %s Cancelable %s Indeterminate %s\n', d.Title, d.Message, v2s(d.Value), char(d.Cancelable), char(d.Indeterminate));
    delete(d);
    fprintf('after delete: isvalid %d\n', isvalid(d));
    d = uiprogressdlg(vf); d2 = uiprogressdlg(vf);
    fprintf('two dialogs: valid %d %d\n', isvalid(d), isvalid(d2));
    delete(vf);
    fprintf('after the figure is deleted: valid %d %d\n', isvalid(d), isvalid(d2));
catch e
    fprintf('progress section ERR %s | %s\n', e.identifier, oneline(e.message));
end
tryp('uiprogressdlg odd', @() uiprogressdlg(uifigure, 'Title'));
tryp('uiprogressdlg bad option', @() uiprogressdlg(uifigure, 'Bogus', 1));
tryp('uiprogressdlg bad value', @() uiprogressdlg(uifigure, 'Value', 5));
delete(findall(groot, 'Type', 'figure', 'Visible', 'on'));

%% uiaxes
before = findall(groot, 'Type', 'figure');
tryp('uiaxes ()', @() axinfo(uiaxes()));
delete(setdiff(findall(groot, 'Type', 'figure'), before));
tryp('uiaxes (uifigure)', @() axinfo(uiaxes(uf)));
tryp('uiaxes (figure)', @() axinfo(uiaxes(f)));
tryp('uiaxes (panel)', @() axinfo(uiaxes(uipanel(uf))));
tryp('uiaxes (classic panel)', @() axinfo(uiaxes(uipanel(f))));
tryp('uiaxes (grid)', @() axinfo(uiaxes(uigridlayout(uf))));
tryp('uiaxes (buttongroup)', @() axinfo(uiaxes(uibuttongroup(uf))));
tryp('uiaxes (axes)', @() axinfo(uiaxes(axes(f))));
tryp('uiaxes (button)', @() axinfo(uiaxes(b)));
tryp('uiaxes (5.5)', @() axinfo(uiaxes(5.5)));
tryp('uiaxes (Parent, uf)', @() axinfo(uiaxes('Parent', uf)));
tryp('uiaxes (uf, Position)', @() axinfo(uiaxes(uf, 'Position', [20 30 200 150])));
tryp('uiaxes (uf, Units normalized, Position)', @() axinfo(uiaxes(uf, 'Units', 'normalized', 'Position', [0.1 0.1 0.5 0.5])));
tryp('uiaxes (uf, Bogus)', @() axinfo(uiaxes(uf, 'Bogus', 1)));
tryp('uiaxes (uf, odd)', @() axinfo(uiaxes(uf, 'XLim')));
tryp('uiaxes (uf, XLim, Title? )', @() get(uiaxes(uf, 'XLim', [0 5]), 'XLim'));
tryp('uiaxes (struct)', @() axinfo(uiaxes(uf, struct('Tag', 's'))));
tryp('axes (uifigure)', @() axinfo(axes(uf)));
tryp('axes (uipanel in uifigure)', @() axinfo(axes(uipanel(uf))));
delete(allchild(uf)); delete(allchild(f));
ax = uiaxes(uf);
fprintf('uiaxes: isa UIAxes %d isa Axes %d Type %s NextPlot %s Box %s FontSize %g FontUnits %s FontName %s Toolbar %s\n', isa(ax, 'matlab.ui.control.UIAxes'), ...
    isa(ax, 'matlab.graphics.axis.Axes'), ax.Type, ax.NextPlot, char(ax.Box), ax.FontSize, ax.FontUnits, ax.FontName, class(ax.Toolbar));
fprintf('uiaxes: InnerPosition %s OuterPosition %s PositionConstraint %s Units %s\n', mat2str(ax.InnerPosition, 6), mat2str(ax.OuterPosition, 6), ax.PositionConstraint, ax.Units);
tryp('uiaxes BackgroundColor', @() get(ax, 'BackgroundColor'));
tryp('uiaxes Layout', @() class(get(ax, 'Layout')));
tryp('uiaxes Units <- normalized', @() setget(ax, 'Units', 'normalized'));
fprintf('   Position now %s\n', mat2str(ax.Position, 6));
ax.Units = 'pixels';
tryp('uiaxes Position <- [50 60 300 200]', @() setget(ax, 'Position', [50 60 300 200]));
fprintf('   Inner %s Outer %s Constraint %s\n', mat2str(ax.InnerPosition, 6), mat2str(ax.OuterPosition, 6), ax.PositionConstraint);
tryp('uiaxes InnerPosition <- [80 90 200 100]', @() setget(ax, 'InnerPosition', [80 90 200 100]));
fprintf('   Position %s Outer %s Constraint %s\n', mat2str(ax.Position, 6), mat2str(ax.OuterPosition, 6), ax.PositionConstraint);
tryp('uiaxes OuterPosition <- [10 10 400 300]', @() setget(ax, 'OuterPosition', [10 10 400 300]));
fprintf('   Position %s Inner %s Constraint %s\n', mat2str(ax.Position, 6), mat2str(ax.InnerPosition, 6), ax.PositionConstraint);
title(ax, 'T'); xlabel(ax, 'X'); ylabel(ax, 'Y');
h = plot(ax, 1:3, [2 4 3]);
fprintf('after plot(ax): children %d title kept [%s] xlabel [%s] XLim %s\n', numel(ax.Children), ax.Title.String, ax.XLabel.String, mat2str(ax.XLim));
plot(ax, 1:5);
fprintf('after second plot(ax): children %d title kept [%s] XLim %s\n', numel(ax.Children), ax.Title.String, mat2str(ax.XLim));
hold(ax, 'on'); plot(ax, 5:-1:1); fprintf('hold on: children %d NextPlot %s\n', numel(ax.Children), ax.NextPlot);
figsBefore = numel(findall(groot, 'Type', 'figure'));
tryp('gca with only a uiaxes', @() class(get(gca, 'Parent')));
fprintf('   figures before %d after %d\n', figsBefore, numel(findall(groot, 'Type', 'figure')));
delete(setdiff(findall(groot, 'Type', 'figure'), [uf; f]));
fprintf('figure children order: %s\n', strjoin(arrayfun(@(k) k.Type, uf.Children', 'UniformOutput', false), ' '));
b2 = uibutton(uf); ax2 = uiaxes(uf);
fprintf('button, then uiaxes: %s\n', strjoin(arrayfun(@(k) k.Type, uf.Children', 'UniformOutput', false), ' '));
tryp('cla(ax)', @() numel(get(cla2(ax), 'Children')));
tryp('grid(ax, on)', @() grid2(ax));
delete(uf); delete(f);
end

function v = setget(h, name, value)
set(h, name, value);
v = get(h, name);
end

function s = outof(fn)
x = fn(); s = class(x);
end

function s = axinfo(ax)
s = sprintf('%s type %s parent %s Units %s Position %s', class(ax), ax.Type, class(ax.Parent), ax.Units, mat2str(ax.Position, 6));
end

function ax = cla2(ax)
cla(ax);
end

function s = grid2(ax)
grid(ax, 'on'); s = sprintf('XGrid %s YGrid %s', char(ax.XGrid), char(ax.YGrid));
end
