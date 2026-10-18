% oi141_follow - open item 141, third pass: which of the legend's text settings its title's
% automatic values follow once the legend has updated (FontName, Interpreter, TextColor, FontAngle,
% FontSize), read after a drawnow and an earlier touch so no read is the stale first one
% (oi141_font: the first read of a fresh title answers the plain text defaults). Also: assigning an
% axes' Title as R2025b takes it, the title's Color 'none', and the text a multi-line title reads.
set(groot, 'DefaultFigureVisible', 'off');
fprintf('%s\n', version);

[f, lg] = fresh();
t = lg.Title;
t.String = 'Follow';
drawnow;
r('G1 settled', t, lg);
lg.FontName = 'Arial';
drawnow;
r('G2 lg.FontName Arial', t, lg);
lg.Interpreter = 'latex';
drawnow;
r('G3 lg.Interpreter latex', t, lg);
lg.TextColor = [1 0 0];
drawnow;
r('G4 lg.TextColor red', t, lg);
lg.FontAngle = 'italic';
drawnow;
r('G5 lg.FontAngle italic', t, lg);
t.FontName = 'Courier New';
t.Interpreter = 'none';
lg.FontName = 'Times New Roman';
lg.Interpreter = 'tex';
drawnow;
r('G6 title FontName and Interpreter manual, legend changed again', t, lg);
delete(f);

[f, lg] = fresh();
t = lg.Title;
t.String = 'C';
drawnow;
t.Color = 'none';
fprintf('G7 Color none: %s mode=%s\n', mat2str(t.Color), t.ColorMode);
t.Color = 'b';
fprintf('G8 Color b: %s mode=%s\n', mat2str(t.Color), t.ColorMode);
t.ColorMode = 'auto';
drawnow;
fprintf('G9 ColorMode auto: %s mode=%s\n', mat2str(t.Color), t.ColorMode);
delete(f);

f = figure('Visible', 'off');
ax = axes(f);
try
    ax.Title = 'assigned';
    fprintf('G10 ax.Title = ''assigned'': ok, class=%s String=%s\n', class(ax.Title), ax.Title.String);
catch e
    fprintf('G10 ax.Title = ''assigned'': error %s | %s\n', e.identifier, e.message);
end
try
    set(ax, 'Title', 'set');
    fprintf('G11 set(ax, ''Title'', ''set''): ok, String=%s\n', ax.Title.String);
catch e
    fprintf('G11 set(ax, ''Title'', ''set''): error %s | %s\n', e.identifier, e.message);
end
delete(f);

[f, lg] = fresh();
title(lg, {'one', 'two'});
t = lg.Title;
fprintf('G12 multi-line: class=%s size=%s\n', class(t.String), mat2str(size(t.String)));
fprintf('G13 display:\n%s', evalc('t'));
lg.Title.String = '';
fprintf('G14 empty display:\n%s', evalc('t'));
fprintf('G15 HitTest=%s PickableParts=%s HandleVisibility=%s Selected=%s SelectionHighlight=%s Visible=%s\n', ...
    char(t.HitTest), t.PickableParts, t.HandleVisibility, char(t.Selected), char(t.SelectionHighlight), char(t.Visible));
fprintf('G16 class(t.Visible)=%s class(t.Children)=%s size=%s\n', class(t.Visible), class(t.Children), mat2str(size(t.Children)));
try
    t.Parent = gca;
    fprintf('G17 t.Parent = gca: ok\n');
catch e
    fprintf('G17 t.Parent = gca: error %s | %s\n', e.identifier, e.message);
end
try
    s = get(t, 'string');
    fprintf('G18 get(t, ''string''): %s\n', s);
catch e
    fprintf('G18 get lower: error %s | %s\n', e.identifier, e.message);
end
try
    t.fonts = 12;
    fprintf('G19 t.fonts = 12: ok %g\n', t.FontSize);
catch e
    fprintf('G19 t.fonts = 12: error %s | %s\n', e.identifier, e.message);
end
try
    set(t, 'FontS', 12);
    fprintf('G20 set(t, ''FontS'', 12): ok %g\n', t.FontSize);
catch e
    fprintf('G20 set FontS: error %s | %s\n', e.identifier, e.message);
end
fprintf('G21 isequal(lg.Title, lg.Title)=%d  lg.Title == lg.Title: %d\n', isequal(lg.Title, lg.Title), lg.Title == lg.Title);
delete(f);
fprintf('done\n');

function [f, lg] = fresh()
f = figure('Visible', 'off');
plot(1:3);
hold on;
plot(3:-1:1);
lg = legend('up', 'down');
end

function r(label, t, lg)
fprintf('%s: title FontName=%s(%s) Interpreter=%s(%s) Color=%s(%s) FontAngle=%s(%s) FontSize=%g | legend FontName=%s Interpreter=%s TextColor=%s\n', ...
    label, t.FontName, t.FontNameMode, t.Interpreter, t.InterpreterMode, mat2str(t.Color, 4), t.ColorMode, ...
    t.FontAngle, t.FontAngleMode, t.FontSize, lg.FontName, lg.Interpreter, mat2str(lg.TextColor, 4));
end
