function u2_units
% U2 probe: Units on figures, axes, panels and uicontrols; getpixelposition/setpixelposition;
% root screen values; movegui. Headless: run-probe.ps1 -Name u2_units.
r = groot;
ss = r.ScreenSize;
fprintf('ScreenSize=%s MonitorPositions=%s PPI=%g\n', mat2str(ss), mat2str(r.MonitorPositions), r.ScreenPixelsPerInch);
tryp('set root Units bogus', @() set(r, 'Units', 'bogus'));
tryp('set root ScreenSize', @() set(r, 'ScreenSize', [1 1 2 2]));
tryp('set root MonitorPositions', @() set(r, 'MonitorPositions', [1 1 2 2]));
set(r, 'Units', 'normalized'); fprintf('root normalized: ScreenSize=%s MonitorPositions=%s\n', mat2str(r.ScreenSize), mat2str(r.MonitorPositions));
set(r, 'Units', 'points'); fprintf('root points: MonitorPositions=%s PointerLocation numel=%d\n', mat2str(r.MonitorPositions), numel(r.PointerLocation));
set(r, 'Units', 'pixels');

f = figure('Visible', 'off', 'Units', 'pixels', 'Position', [100 100 560 420]);
units = {'pixels', 'points', 'inches', 'centimeters', 'normalized', 'characters'};

%% figure: Position, Inner, Outer in each unit; setting in a unit
for u = units
    f.Units = u{1};
    fprintf('figure %-11s Position=%s Inner=%s Outer=%s\n', u{1}, mat2str(f.Position, 8), mat2str(f.InnerPosition, 8), mat2str(f.OuterPosition, 8));
end
f.Units = 'points'; f.Position = [75 75 300 225]; f.Units = 'pixels';
fprintf('figure set [75 75 300 225] points -> pixels %s\n', mat2str(f.Position, 8));
f.Units = 'normalized'; f.Position = [0.25 0.25 0.5 0.5]; f.Units = 'pixels';
fprintf('figure set [.25 .25 .5 .5] normalized -> pixels %s (screen %s)\n', mat2str(f.Position, 8), mat2str(ss));
f.Units = 'characters'; f.Position = [10 10 80 20]; f.Units = 'pixels';
fprintf('figure set [10 10 80 20] characters -> pixels %s\n', mat2str(f.Position, 8));
f.Position = [100 100 560 420];
tryp('figure Units bogus', @() set(f, 'Units', 'bogus'));
tryp('figure Units PIX', @() setget(f, 'Units', 'PIX'));
tryp('figure Units norm', @() setget(f, 'Units', 'norm'));
f.Units = 'pixels';
g = figure('Visible', 'off', 'Units', 'normalized', 'Position', [0.1 0.1 0.5 0.5]);
fprintf('figure(Units normalized, Position) -> Units=%s Position=%s\n', g.Units, mat2str(g.Position, 8));
delete(g);
g = figure('Visible', 'off', 'Position', [0.1 0.1 0.5 0.5], 'Units', 'normalized');
fprintf('figure(Position, Units normalized) -> Units=%s Position=%s\n', g.Units, mat2str(g.Position, 8));
delete(g);
g = figure('Visible', 'off');
fprintf('default figure Position size=%s Units=%s\n', mat2str(g.Position(3:4)), g.Units);
delete(g);

%% axes
a = axes(f);
fprintf('axes default Units=%s Position=%s Outer=%s\n', a.Units, mat2str(a.Position, 8), mat2str(a.OuterPosition, 8));
for u = units
    a.Units = u{1};
    fprintf('axes %-11s Position=%s Inner=%s Outer=%s\n', u{1}, mat2str(a.Position, 8), mat2str(a.InnerPosition, 8), mat2str(a.OuterPosition, 8));
end
a.Units = 'pixels'; a.Position = [50 60 300 200];
fprintf('axes pixels set [50 60 300 200]: Position=%s constraint=%s\n', mat2str(a.Position, 8), a.PositionConstraint);
f.Position = [100 100 700 500];
fprintf('  after figure resize to 700x500: Position=%s (pixels held)\n', mat2str(a.Position, 8));
a.Units = 'normalized';
fprintf('  back to normalized: Position=%s\n', mat2str(a.Position, 8));
f.Position = [100 100 560 420];
fprintf('  figure back to 560x420, axes normalized held: Position=%s ; pixels=%s\n', mat2str(a.Position, 8), mat2str(getpixelposition(a), 8));
tryp('axes Units bogus', @() set(a, 'Units', 'bogus'));
tryp('axes Position zero width', @() set(a, 'Position', [0 0 0 1]));
a2 = axes(f, 'Units', 'pixels', 'Position', [10 10 100 80]);
fprintf('axes(Units pixels, Position) -> %s %s\n', a2.Units, mat2str(a2.Position, 8));
a3 = axes(f, 'Position', [10 10 100 80], 'Units', 'pixels');
fprintf('axes(Position, Units pixels) -> %s %s\n', a3.Units, mat2str(a3.Position, 8));
a4 = axes(f, 'OuterPosition', [0 0 0.5 0.5]);
fprintf('axes OuterPosition [0 0 .5 .5] pixels gpp=%s\n', mat2str(getpixelposition(a4), 8));
delete([a a2 a3 a4]);

%% uicontrol and panel in each unit
c = uicontrol(f, 'Position', [20 30 100 40]);
p = uipanel(f, 'Units', 'pixels', 'Position', [50 60 200 150]);
for u = units
    c.Units = u{1}; p.Units = u{1};
    fprintf('%-11s uicontrol=%s panel=%s panelInner=%s\n', u{1}, mat2str(c.Position, 8), mat2str(p.Position, 8), mat2str(p.InnerPosition, 8));
end
c.Units = 'normalized'; c.Position = [0.5 0.5 0.25 0.25]; c.Units = 'pixels';
fprintf('uicontrol normalized [.5 .5 .25 .25] in 560x420 -> %s\n', mat2str(c.Position, 8));
c.Units = 'normalized';
f.Position = [100 100 700 500];
fprintf('  after figure 700x500: normalized=%s pixels=%s\n', mat2str(c.Position, 8), mat2str(getpixelposition(c), 8));
f.Position = [100 100 560 420];
k = uicontrol(p, 'Units', 'normalized', 'Position', [0.5 0.5 0.25 0.25]);
fprintf('child normalized in panel 200x150: gpp=%s gpp(true)=%s\n', mat2str(getpixelposition(k), 8), mat2str(getpixelposition(k, true), 8));
k.Units = 'characters'; fprintf('  child characters=%s\n', mat2str(k.Position, 8));
k.Units = 'points'; fprintf('  child points=%s\n', mat2str(k.Position, 8));
u1 = uicontrol(f, 'Units', 'normalized');
fprintf('uicontrol(Units normalized) default Position=%s\n', mat2str(u1.Position, 8));
u2 = uicontrol(f, 'Units', 'normalized', 'Position', [0.1 0.1 0.2 0.2]);
u2.Units = 'pixels';
fprintf('uicontrol(Units normalized, Position [.1 .1 .2 .2]) pixels=%s\n', mat2str(u2.Position, 8));
u3 = uicontrol(f, 'Position', [0.1 0.1 0.2 0.2], 'Units', 'normalized');
fprintf('uicontrol(Position [.1 .1 .2 .2], Units normalized) Position=%s\n', mat2str(u3.Position, 8));
u4 = uicontrol(f, 'FontUnits', 'normalized', 'FontSize', 0.5, 'Position', [10 10 100 40]);
u4.FontUnits = 'pixels'; fprintf('FontUnits normalized 0.5 of height 40 -> pixels %g\n', u4.FontSize);
u4.FontUnits = 'points'; fprintf('   -> points %g\n', u4.FontSize);

%% getpixelposition / setpixelposition forms and refusals
fprintf('gpp(f)=%s gpp(f,true)=%s\n', mat2str(getpixelposition(f)), mat2str(getpixelposition(f, true)));
tryp('gpp()', @() getpixelposition());
tryp('gpp(5.5)', @() getpixelposition(5.5));
tryp('gpp(0)', @() getpixelposition(0));
tryp('gpp(c, 1)', @() getpixelposition(c, 1));
tryp('gpp(c, ''x'')', @() getpixelposition(c, 'x'));
tryp('gpp([c p])', @() getpixelposition([c p]));
c.Units = 'normalized';
setpixelposition(c, [5 6 70 80]);
fprintf('setpixelposition on normalized: Units=%s Position=%s gpp=%s\n', c.Units, mat2str(c.Position, 8), mat2str(getpixelposition(c), 8));
setpixelposition(k, [40 50 30 20], true);
fprintf('setpixelposition(k,..,true) in panel at [50 60]: gpp=%s gpp(true)=%s Units=%s\n', mat2str(getpixelposition(k), 8), mat2str(getpixelposition(k, true), 8), k.Units);
tryp('spp(c)', @() setpixelposition(c));
tryp('spp(c,[1 2 3])', @() setpixelposition(c, [1 2 3]));
tryp('spp(5.5,[1 2 3 4])', @() setpixelposition(5.5, [1 2 3 4]));
tryp('spp(f,[10 20 300 200])', @() spp(f, [10 20 300 200]));
a = axes(f);
tryp('spp(axes)', @() spp(a, [30 40 200 100]));
fprintf('  axes Units after spp=%s\n', a.Units);
tryp('nargout spp', @() nargout('setpixelposition'));
tryp('nargout gpp', @() nargout('getpixelposition'));
tryp('nargin gpp', @() nargin('getpixelposition'));

%% movegui: where each word puts a 300x200 figure, as offsets from the screen's edges
f.Position = [200 200 300 200];
for w = {'north', 'south', 'east', 'west', 'northeast', 'northwest', 'southeast', 'southwest', 'center', 'onscreen'}
    f.Position = [200 200 300 200];
    movegui(f, w{1});
    pos = f.Position; op = f.OuterPosition;
    fprintf('movegui %-9s Position=%s left=%g bottom=%g rightgap=%g topgap=%g outer=%s\n', w{1}, mat2str(pos), pos(1) - ss(1), pos(2) - ss(2), ss(3) - (pos(1) + pos(3) - 1), ss(4) - (pos(2) + pos(4) - 1), mat2str(op));
end
f.Position = [200 200 300 200]; movegui(f, [10 20]); fprintf('movegui [10 20] -> %s\n', mat2str(f.Position));
f.Position = [200 200 300 200]; movegui(f, [-10 -20]); pos = f.Position; fprintf('movegui [-10 -20] -> rightgap=%g topgap=%g\n', ss(3) - (pos(1) + pos(3) - 1), ss(4) - (pos(2) + pos(4) - 1));
f.Position = [-500 -500 300 200]; movegui(f); fprintf('movegui(f) from [-500 -500] -> %s\n', mat2str(f.Position));
f.Position = [-500 -500 300 200]; movegui(f, 'onscreen'); fprintf('movegui onscreen from [-500 -500] -> %s\n', mat2str(f.Position));
f.Position = [ss(3) + 100, ss(4) + 100, 300, 200]; movegui(f, 'onscreen'); pos = f.Position; fprintf('movegui onscreen from beyond top right -> rightgap=%g topgap=%g\n', ss(3) - (pos(1) + pos(3) - 1), ss(4) - (pos(2) + pos(4) - 1));
tryp('movegui bogus', @() movegui(f, 'bogus'));
tryp('movegui(5.5)', @() movegui(5.5));
tryp('movegui(c)', @() movegui(c, 'center'));
tryp('movegui(''center'') string first', @() mg('center'));
tryp('nargout movegui', @() nargout('movegui'));
f.Units = 'normalized'; movegui(f, 'center'); fprintf('movegui on normalized figure: Units=%s\n', f.Units);
delete(f);
end

function v = spp(h, pos)
setpixelposition(h, pos);
v = getpixelposition(h);
end

function v = mg(w)
movegui(w);
v = get(gcf, 'Position');
v = v(3:4);
end

function v = setget(h, name, value)
set(h, name, value);
v = get(h, name);
end
