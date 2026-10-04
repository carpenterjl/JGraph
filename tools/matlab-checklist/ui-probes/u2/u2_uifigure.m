function u2_uifigure
% U2 probe: uifigure - its defaults, its handle, how gcf and the figure list see it,
% AutoResizeChildren and SizeChangedFcn. Headless: run-probe.ps1 -Name u2_uifigure.

%% the handle and the list
uf = uifigure('Visible', 'off');
fprintf('class=%s Type=%s Number=%s IntegerHandle=%s HandleVisibility=%s\n', class(uf), uf.Type, v2s(uf.Number), char(uf.IntegerHandle), uf.HandleVisibility);
d = double(uf); fprintf('double(uf) integer? %d ; >0 %d ; ishandle(double)=%d ; isequal(handle(d),uf)=%d\n', d == floor(d), d > 0, ishandle(d), isequal(handle(d), uf));
fprintf('get(0,Children) count=%d ; findobj(0,Type,figure)=%d ; findall(0,Type,figure)=%d ; isempty(get(0,CurrentFigure))=%d\n', numel(get(0, 'Children')), numel(findobj(0, 'Type', 'figure')), numel(findall(0, 'Type', 'figure')), isempty(get(0, 'CurrentFigure')));
f1 = figure('Visible', 'off'); fprintf('figure after uifigure: Number=%d\n', f1.Number);
fprintf('gcf==f1 %d\n', gcf == f1);
figure(uf); fprintf('figure(uf): gcf==uf %d ; get(0,CurrentFigure)==uf %d\n', gcf == uf, isequal(get(0, 'CurrentFigure'), uf));
delete(f1);
fprintf('after delete(f1): isempty(get(0,CurrentFigure))=%d\n', isempty(get(0, 'CurrentFigure')));
close all; fprintf('close all: isvalid(uf)=%d\n', isvalid(uf));
close all force; fprintf('close all force: isvalid(uf)=%d\n', isvalid(uf));
uf = uifigure('Visible', 'off');
close(uf); fprintf('close(uf): isvalid(uf)=%d\n', isvalid(uf));
uf = uifigure('Visible', 'off');

%% defaults
fprintf('uifigure defaults:\n');
s = get(uf); names = fieldnames(s);
for k = 1:numel(names)
    if any(strcmp(names{k}, {'Position', 'OuterPosition', 'InnerPosition', 'PaperPosition'})), fprintf('  %s size=%s\n', names{k}, mat2str(s.(names{k})(3:4), 6)); continue; end
    fprintf('  %s = %s\n', names{k}, v2s(s.(names{k})));
end
f = figure('Visible', 'off');
sf = get(f); fnames = fieldnames(sf);
fprintf('names only in figure: %s\n', strjoin(setdiff(fnames, names)', ','));
fprintf('names only in uifigure: %s\n', strjoin(setdiff(names, fnames)', ','));
fprintf('classic figure: AutoResizeChildren=%s Scrollable=%s HandleVisibility=%s IntegerHandle=%s MenuBar=%s ToolBar=%s NumberTitle=%s Color=%s\n', v2s(f.AutoResizeChildren), v2s(f.Scrollable), f.HandleVisibility, v2s(f.IntegerHandle), f.MenuBar, f.ToolBar, v2s(f.NumberTitle), mat2str(f.Color, 6));
delete(f);

%% argument forms
tryp('uifigure(''Name'',''N'')', @() dget(uifigure('Visible', 'off', 'Name', 'N'), 'Name'));
tryp('uifigure(5)', @() uifigure(5));
tryp('uifigure(''Name'')', @() uifigure('Name'));
tryp('uifigure(''Bogus'',1)', @() uifigure('Bogus', 1));
tryp('uifigure(struct)', @() dget(uifigure(struct('Visible', 'off', 'Name', 'S')), 'Name'));
tryp('uifigure IntegerHandle on', @() dget(uifigure('Visible', 'off', 'IntegerHandle', 'on'), 'Number'));
tryp('uifigure HandleVisibility on -> in list', @() hv());
tryp('figure IntegerHandle off', @() ih());
tryp('nargout uifigure', @() nargout('uifigure'));
tryp('set Number', @() set(uf, 'Number', 3));
tryp('set uf MenuBar figure', @() setget(uf, 'MenuBar', 'figure'));
tryp('set uf ToolBar figure', @() setget(uf, 'ToolBar', 'figure'));
tryp('set uf Units normalized', @() setget(uf, 'Units', 'normalized'));
uf.Units = 'pixels';
tryp('set uf Scrollable on', @() setget(uf, 'Scrollable', 'on'));
tryp('set uf WindowStyle modal', @() setget(uf, 'WindowStyle', 'modal'));
tryp('set uf WindowStyle alwaysontop', @() setget(uf, 'WindowStyle', 'alwaysontop'));
tryp('set uf WindowStyle docked', @() setget(uf, 'WindowStyle', 'docked'));
tryp('set uf WindowStyle bogus', @() setget(uf, 'WindowStyle', 'bogus'));
uf.WindowStyle = 'normal';
tryp('set uf Icon', @() setget(uf, 'Icon', ''));
tryp('set uf Resize off', @() setget(uf, 'Resize', 'off'));
tryp('isa matlab.ui.Figure', @() isa(uf, 'matlab.ui.Figure'));
tryp('matlab.ui.internal.isUIFigure', @() matlab.ui.internal.isUIFigure(uf));
tryp('isgraphics(uf,''figure'')', @() isgraphics(uf, 'figure'));
tryp('ishghandle(uf)', @() ishghandle(uf));

%% children in a uifigure: what the classic verbs do
tryp('axes(uf)', @() class(axes(uf)));
tryp('uiaxes(uf) class', @() class(uiaxes(uf)));
tryp('uicontrol(uf)', @() class(uicontrol(uf)));
tryp('uipanel(uf).Units', @() get(uipanel(uf), 'Units'));
fprintf('uf.Children: %s\n', strjoin(arrayfun(@(x) x.Type, uf.Children, 'UniformOutput', false)', ','));
delete(uf.Children);

%% AutoResizeChildren
uf.Position = [100 100 400 300];
b = uicontrol(uf, 'Position', [100 100 100 50]);
p = uipanel(uf, 'Position', [200 150 100 100]);
k = uicontrol(p, 'Position', [10 10 40 20]);
drawnow;
uf.Position = [100 100 800 600]; drawnow;
fprintf('ARC on, 400x300 -> 800x600: control=%s panel=%s child=%s\n', mat2str(b.Position, 6), mat2str(p.Position, 6), mat2str(k.Position, 6));
uf.Position = [100 100 200 150]; drawnow;
fprintf('ARC on, -> 200x150: control=%s panel=%s child=%s\n', mat2str(b.Position, 6), mat2str(p.Position, 6), mat2str(k.Position, 6));
uf.Position = [100 100 400 300]; drawnow;
fprintf('ARC on, back to 400x300: control=%s panel=%s child=%s\n', mat2str(b.Position, 6), mat2str(p.Position, 6), mat2str(k.Position, 6));
uf.AutoResizeChildren = 'off';
uf.Position = [100 100 800 600]; drawnow;
fprintf('ARC off, -> 800x600: control=%s panel=%s\n', mat2str(b.Position, 6), mat2str(p.Position, 6));
uf.Position = [100 100 400 300]; drawnow;

%% SizeChangedFcn
uf.AutoResizeChildren = 'on';
tryp('SizeChangedFcn with ARC on', @() setget2(uf, 'SizeChangedFcn', @(s, e) fprintf('  uf SizeChangedFcn %s evt=%s\n', mat2str(s.Position(3:4)), class(e))));
fprintf('  ARC after setting SizeChangedFcn: %s\n', char(uf.AutoResizeChildren));
uf.Position = [100 100 500 300]; drawnow; fprintf('  (resized with ARC %s)\n', char(uf.AutoResizeChildren));
uf.AutoResizeChildren = 'off';
uf.Position = [100 100 600 300]; fprintf('  set Position, before drawnow\n'); drawnow; fprintf('  after drawnow\n');
uf.Position = [100 100 600 300]; drawnow; fprintf('  same size again: no call expected\n');
uf.Position = [150 150 600 300]; drawnow; fprintf('  moved only\n');
tryp('ARC on while SizeChangedFcn set', @() setget(uf, 'AutoResizeChildren', 'on'));
fprintf('  SizeChangedFcn now: %s\n', v2s(uf.SizeChangedFcn));
delete(uf);

f = figure('Visible', 'off', 'Position', [100 100 400 300]);
f.SizeChangedFcn = @(s, e) fprintf('  f SizeChangedFcn %s evt=%s EventName=%s\n', mat2str(s.Position(3:4)), class(e), e.EventName);
p = uipanel(f, 'Units', 'normalized', 'Position', [0.1 0.1 0.5 0.5]);
p.SizeChangedFcn = @(s, e) fprintf('  panel SizeChangedFcn %s evt=%s\n', mat2str(getpixelposition(s)), class(e));
q = uipanel(p, 'Units', 'normalized', 'Position', [0 0 0.5 0.5], 'SizeChangedFcn', @(s, e) fprintf('  inner panel SizeChangedFcn %s\n', mat2str(getpixelposition(s))));
fprintf('classic: after creating (before drawnow)\n'); drawnow; fprintf('classic: after first drawnow\n');
f.Position = [100 100 800 600]; fprintf('classic: set figure 800x600, before drawnow\n'); drawnow; fprintf('classic: after drawnow\n');
p.Position = [0.1 0.1 0.25 0.25]; fprintf('classic: set panel Position, before drawnow\n'); drawnow; fprintf('classic: after drawnow\n');
p.Position = [0.2 0.2 0.25 0.25]; drawnow; fprintf('classic: panel moved only\n');
tryp('classic ARC on', @() setget(f, 'AutoResizeChildren', 'on'));
fprintf('  f.SizeChangedFcn after ARC on: %s\n', v2s(f.SizeChangedFcn));
tryp('f.ResizeFcn', @() get(f, 'ResizeFcn'));
delete(f);
end

function v = dget(h, name)
v = get(h, name);
delete(h);
end

function v = hv()
u = uifigure('Visible', 'off', 'HandleVisibility', 'on');
v = [any(get(0, 'Children') == u), isequal(get(0, 'CurrentFigure'), u)];
delete(u);
end

function v = ih()
f = figure('Visible', 'off', 'IntegerHandle', 'off');
d = double(f);
v = [d == floor(d), isempty(f.Number), any(get(0, 'Children') == f)];
f.IntegerHandle = 'on';
v = [v, double(f) == floor(double(f)), f.Number];
delete(f);
end

function v = setget(h, name, value)
set(h, name, value);
v = get(h, name);
end

function v = setget2(h, name, value)
set(h, name, value);
v = class(get(h, name));
end
