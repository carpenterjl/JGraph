% oi141_font - open item 141, second pass: where a legend title's automatic FontSize and
% FontWeight come from. Headless. oi141_title read FontSize 9 and bold on a fresh legend, 10 after
% the legend's FontSize was set, 10 and normal under display() and 9 and bold under disp() of the
% same object. This reads both before and after each step and each drawnow, against the axes',
% the legend's and the root's text defaults, and deletes the title with no output asked for.
set(groot, 'DefaultFigureVisible', 'off');
fprintf('%s\n', version);

[f, lg] = fresh();
t = lg.Title;
r('F1 fresh', t, lg);
r('F2 again, no drawnow', t, lg);
drawnow;
r('F3 after drawnow', t, lg);
t.String = 'Title';
r('F4 after String', t, lg);
drawnow;
r('F5 after drawnow', t, lg);
lg.FontSize = 14;
r('F6 lg.FontSize 14', t, lg);
drawnow;
r('F7 after drawnow', t, lg);
lg.FontSize = 6;
drawnow;
r('F8 lg.FontSize 6 + drawnow', t, lg);
s1 = evalc('display(t)');
r('F9 after display(t)', t, lg);
s2 = evalc('disp(t)');
r('F10 after disp(t)', t, lg);
fprintf('F9 text: %s\n', strrep(s1, newline, ' / '));
fprintf('F10 text: %s\n', strrep(s2, newline, ' / '));
delete(f);

[f, lg] = fresh();
t = lg.Title;
s1 = evalc('display(t)');
r('F11 display first, fresh', t, lg);
fprintf('F11 text: %s\n', strrep(s1, newline, ' / '));
delete(f);

[f, lg] = fresh();
ax = gca;
ax.FontSize = 20;
drawnow;
r('F12 axes FontSize 20 + drawnow', lg.Title, lg);
lg.Title.String = 'T';
drawnow;
r('F13 then a title + drawnow', lg.Title, lg);
delete(f);

set(groot, 'DefaultTextFontSize', 15);
[f, lg] = fresh();
drawnow;
r('F14 DefaultTextFontSize 15', lg.Title, lg);
delete(f);
set(groot, 'DefaultTextFontSize', 'remove');

set(groot, 'DefaultLegendFontSize', 13);
[f, lg] = fresh();
drawnow;
r('F15 DefaultLegendFontSize 13', lg.Title, lg);
delete(f);
set(groot, 'DefaultLegendFontSize', 'remove');

set(groot, 'DefaultAxesFontSize', 16);
[f, lg] = fresh();
drawnow;
r('F16 DefaultAxesFontSize 16', lg.Title, lg);
delete(f);
set(groot, 'DefaultAxesFontSize', 'remove');

[f, lg] = fresh();
lg.FontSize = 14;
lg.Title.String = 'T';
drawnow;
r('F17 lg.FontSize 14 first, then title + drawnow', lg.Title, lg);
lg.FontWeight = 'bold';
drawnow;
r('F18 lg.FontWeight bold + drawnow', lg.Title, lg);
lg.FontAngle = 'italic';
drawnow;
r('F19 lg.FontAngle italic + drawnow', lg.Title, lg);
delete(f);

[f, lg] = fresh();
lg.Title.String = 'Kept';
try
    delete(lg.Title);
    fprintf('F20 delete(lg.Title): ok; isvalid=%d String=%s\n', isvalid(lg.Title), lg.Title.String);
catch e
    fprintf('F20 delete(lg.Title): error %s | %s\n', e.identifier, e.message);
end
delete(f);

[f, lg] = fresh();
lg.Title.String = 'Weights';
lg.Title.FontWeight = 'normal';
lg.Title.FontWeight = 'bold';
r('F21 FontWeight normal then bold', lg.Title, lg);
lg.Title.FontWeightMode = 'auto';
drawnow;
r('F22 FontWeightMode auto + drawnow', lg.Title, lg);
lg.Title.FontSizeMode = 'manual';
r('F23 FontSizeMode manual', lg.Title, lg);
delete(f);

[f, lg] = fresh();
lg.Title.String = 'Box';
lg.Units = 'pixels';
drawnow;
p1 = lg.Position;
lg.Title.FontSize = 20;
drawnow;
p2 = lg.Position;
lg.Title.Visible = 'off';
drawnow;
p3 = lg.Position;
fprintf('F24 positions title 10/20/hidden: %s\n', mat2str([p1; p2; p3], 6));
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
ax = lg.Axes;
fprintf('%s: title FontSize=%g(%s) FontWeight=%s(%s) FontAngle=%s FontName=%s | legend FontSize=%g(%s) FontWeight=%s | axes FontSize=%g\n', ...
    label, t.FontSize, t.FontSizeMode, t.FontWeight, t.FontWeightMode, t.FontAngle, t.FontName, ...
    lg.FontSize, lg.FontSizeMode, lg.FontWeight, ax.FontSize);
end
