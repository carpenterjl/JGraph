function u2w_resize
% U2 window probe: what a resize does with a window on screen - AutoResizeChildren and
% SizeChangedFcn, in a uifigure and in a classic figure. It OPENS WINDOWS (two, for a few seconds
% each) and drives nothing: every resize is a script's own Position write. Run only with the
% user's leave: run-probe.ps1 -Name u2w_resize -WithWindows.

%% a uifigure, AutoResizeChildren on (its default)
uf = uifigure('Position', [100 100 400 300], 'Name', 'u2w uifigure');
b = uicontrol(uf, 'Position', [101 51 100 50]);
ub = uibutton(uf, 'Position', [250 200 100 22]);
n = uicontrol(uf, 'Units', 'normalized', 'Position', [0.5 0.5 0.25 0.25]);
p = uipanel(uf, 'Position', [201 151 100 100]);
k = uicontrol(p, 'Position', [11 11 40 20]);
kb = uibutton(p, 'Position', [50 50 40 22]);
ax = axes(uf, 'Units', 'pixels', 'Position', [20 200 100 80]);
settle();
report('uifigure 400x300 start', uf, b, ub, n, p, k, kb, ax);
uf.Position = [100 100 800 600]; settle();
report('uifigure -> 800x600', uf, b, ub, n, p, k, kb, ax);
uf.Position = [100 100 200 150]; settle();
report('uifigure -> 200x150', uf, b, ub, n, p, k, kb, ax);
uf.Position = [100 100 400 300]; settle();
report('uifigure -> back to 400x300', uf, b, ub, n, p, k, kb, ax);
uf.Position = [100 100 600 300]; settle();
report('uifigure -> 600x300 (width only)', uf, b, ub, n, p, k, kb, ax);
uf.Position = [100 100 400 300]; settle();

%% the same uifigure with AutoResizeChildren off and SizeChangedFcns
uf.AutoResizeChildren = 'off';
p.AutoResizeChildren = 'off';
uf.SizeChangedFcn = @(s, e) fprintf('  uifigure SizeChangedFcn size=%s class=%s names=%s\n', mat2str(s.Position(3:4)), class(e), strjoin(fieldnames(e)', ','));
p.SizeChangedFcn = @(s, e) fprintf('  uifigure panel SizeChangedFcn size=%s class=%s\n', mat2str(s.Position(3:4)), class(e));
fprintf('uifigure, AutoResizeChildren off: Position -> 500x400\n');
uf.Position = [100 100 500 400]; settle();
report('uifigure ARC off -> 500x400', uf, b, ub, n, p, k, kb, ax);
fprintf('uifigure: same size again\n');
uf.Position = [100 100 500 400]; settle();
fprintf('uifigure: moved only\n');
uf.Position = [150 150 500 400]; settle();
fprintf('uifigure: panel resized by its own Position\n');
p.Position = [201 151 150 120]; settle();
delete(uf);

%% a classic figure
f = figure('Position', [100 100 400 300], 'Name', 'u2w classic', 'MenuBar', 'none', 'ToolBar', 'none');
fprintf('classic: created\n');
f.SizeChangedFcn = @(s, e) fprintf('  classic SizeChangedFcn size=%s class=%s EventName=%s\n', mat2str(s.Position(3:4)), class(e), e.EventName);
pn = uipanel(f, 'Units', 'normalized', 'Position', [0.1 0.1 0.5 0.5], 'SizeChangedFcn', @(s, e) fprintf('  classic normalized panel SizeChangedFcn pixels=%s class=%s\n', mat2str(getpixelposition(s)), class(e)));
pp = uipanel(f, 'Units', 'pixels', 'Position', [300 200 80 80], 'SizeChangedFcn', @(s, e) fprintf('  classic pixel panel SizeChangedFcn\n'));
c = uicontrol(f, 'Position', [20 250 100 30]);
cn = uicontrol(pn, 'Units', 'normalized', 'Position', [0.1 0.1 0.5 0.5]);
fprintf('classic: before the first drawnow\n');
settle();
fprintf('classic: after the first drawnow\n');
fprintf('classic: Position -> 800x600\n');
f.Position = [100 100 800 600]; settle();
fprintf('  control=%s normalized-panel pixels=%s pixel-panel=%s child pixels=%s\n', mat2str(c.Position), mat2str(getpixelposition(pn)), mat2str(pp.Position), mat2str(getpixelposition(cn)));
fprintf('classic: same size again\n');
f.Position = [100 100 800 600]; settle();
fprintf('classic: moved only\n');
f.Position = [150 150 800 600]; settle();
fprintf('classic: pixel panel resized by its own Position\n');
pp.Position = [300 200 120 100]; settle();
fprintf('classic: normalized panel resized by its own Position\n');
pn.Position = [0.1 0.1 0.25 0.25]; settle();
fprintf('classic: AutoResizeChildren on, then Position -> 400x300\n');
warning('off', 'MATLAB:ui:containers:SizeChangedFcnDisabledWhenAutoResizeOn');
f.AutoResizeChildren = 'on';
f.Position = [150 150 400 300]; settle();
fprintf('  control=%s pixel-panel=%s normalized-panel pixels=%s\n', mat2str(c.Position, 6), mat2str(pp.Position, 6), mat2str(getpixelposition(pn), 6));
fprintf('  OuterPosition - Position with a window: %s\n', mat2str(f.OuterPosition - f.Position));
delete(f);
end

function settle()
drawnow; pause(0.7); drawnow;
end

function report(label, uf, b, ub, n, p, k, kb, ax)
fprintf('%s: figure=%s\n', label, mat2str(uf.Position(3:4)));
fprintf('  uicontrol=%s uibutton=%s normalized uicontrol=%s (pixels %s)\n', mat2str(b.Position, 6), mat2str(ub.Position, 6), mat2str(n.Position, 6), mat2str(getpixelposition(n), 6));
fprintf('  panel=%s child uicontrol=%s child uibutton=%s pixel axes=%s\n', mat2str(p.Position, 6), mat2str(k.Position, 6), mat2str(kb.Position, 6), mat2str(ax.Position, 6));
end
