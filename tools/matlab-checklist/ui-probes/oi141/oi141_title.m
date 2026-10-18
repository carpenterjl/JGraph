% oi141_title - open item 141: what R2025b's legend Title is. Headless.
% lg.Title is a text object of the legend's own (matlab.graphics.illustration.legend.Text, measured
% once in batch 28). This records its class, its property list and defaults, what title(lg, ...)
% answers, which legend properties it follows (FontSize, FontName, TextColor, Interpreter), the
% refusals (assigning the property, a number), its parent and children, delete, the legend's box
% growing under it, its display, and a savefig round trip.
set(groot, 'DefaultFigureVisible', 'off');
fprintf('%s\n', version);

[f, lg] = fresh();
t = lg.Title;
say('T1 class', class(t));
say('T2 isgraphics/ishghandle/ishandle/isvalid', [isgraphics(t), ishghandle(t), ishandle(t), isvalid(t)]);
tryit('T3 Type', @() get(t, 'Type'));
p = properties(t);
say('T4 properties', strjoin(p', ' '));
g = get(t);
fn = fieldnames(g);
fprintf('--- T5 get(lg.Title)\n');
for k = 1:numel(fn)
    fprintf('  %s = %s\n', fn{k}, v2s(g.(fn{k})));
end
say('T6 lg.FontSize/FontName/TextColor/Interpreter', {lg.FontSize, lg.FontName, lg.TextColor, lg.Interpreter});
tryit('T7 t.Parent class', @() class(t.Parent));
tryit('T8 isequal(t.Parent, lg)', @() isequal(t.Parent, lg));
tryit('T9 t.Children', @() numel(t.Children));
tryit('T10 lg.Children', @() arrayfun(@class, lg.Children, 'UniformOutput', false));
tryit('T11 allchild(lg)', @() arrayfun(@class, allchild(lg), 'UniformOutput', false));
tryit('T12 get(lg, ''Title'') class', @() class(get(lg, 'Title')));
delete(f);

% title(lg, ...) and what it answers
[f, lg] = fresh();
tryit('T13 out = title(lg, ''Series'') class', @() class(title(lg, 'Series')));
tryit('T14 isequal(title(lg, ''S2''), lg.Title)', @() isequal(title(lg, 'S2'), lg.Title));
say('T15 String after', lg.Title.String);
tryit('T16 title(lg, ''S'', ''FontWeight'', ''normal'')', @() then_read(@() title(lg, 'S', 'FontWeight', 'normal'), @() lg.Title.FontWeight));
tryit('T17 title(lg, ''S'', ''Color'', ''r'')', @() then_read(@() title(lg, 'S', 'Color', 'r'), @() lg.Title.Color));
tryit('T18 title(lg, {''a'', ''b''})', @() then_read(@() title(lg, {'a', 'b'}), @() lg.Title.String));
tryit('T19 title(lg, ["a" "b"])', @() then_read(@() title(lg, ["a" "b"]), @() lg.Title.String));
tryit('T20 title(lg, 5)', @() then_read(@() title(lg, 5), @() lg.Title.String));
tryit('T21 title(lg)', @() title(lg));
tryit('T22 title(lg, '''')', @() then_read(@() title(lg, ''), @() lg.Title.String));
delete(f);

% writing the property and its pieces
[f, lg] = fresh();
tryit('T23 lg.Title = ''x''', @() setfield_obj(lg, 'Title', 'x'));
tryit('T24 set(lg, ''Title'', ''x'')', @() set(lg, 'Title', 'x'));
tryit('T25 lg.Title.String = 7', @() then_read(@() set(lg.Title, 'String', 7), @() lg.Title.String));
tryit('T26 lg.Title.String = "s"', @() then_read(@() set(lg.Title, 'String', "s"), @() lg.Title.String));
tryit('T27 lg.Title.String = {''p''; ''q''}', @() then_read(@() set(lg.Title, 'String', {'p'; 'q'}), @() lg.Title.String));
tryit('T28 lg.Title.FontSize = 0', @() set(lg.Title, 'FontSize', 0));
tryit('T29 lg.Title.FontWeight = ''heavy''', @() set(lg.Title, 'FontWeight', 'heavy'));
tryit('T30 lg.Title.Color = ''none''', @() set(lg.Title, 'Color', 'none'));
tryit('T31 lg.Title.Bogus = 1', @() set(lg.Title, 'Bogus', 1));
tryit('T32 lg.Title.Position', @() get(lg.Title, 'Position'));
tryit('T33 lg.Title.Tag', @() get(lg.Title, 'Tag'));
tryit('T34 lg.Title.Units', @() get(lg.Title, 'Units'));
tryit('T35 lg.Title.HorizontalAlignment', @() get(lg.Title, 'HorizontalAlignment'));
tryit('T36 lg.Title.Visible = off', @() then_read(@() set(lg.Title, 'Visible', 'off'), @() lg.Title.Visible));
delete(f);

% what the title follows
[f, lg] = fresh();
lg.Title.String = 'T';
lg.FontSize = 14;
say('T37 lg.FontSize 14 -> title FontSize', lg.Title.FontSize);
lg.FontName = 'Arial';
say('T38 lg.FontName Arial -> title FontName', lg.Title.FontName);
lg.TextColor = [1 0 0];
say('T39 lg.TextColor red -> title Color', lg.Title.Color);
lg.Interpreter = 'latex';
say('T40 lg.Interpreter latex -> title Interpreter', lg.Title.Interpreter);
lg.Title.FontSize = 20;
lg.FontSize = 8;
say('T41 title FontSize 20 then lg.FontSize 8', lg.Title.FontSize);
lg.Title.Color = [0 0 1];
lg.TextColor = [0 1 0];
say('T42 title Color blue then TextColor green', lg.Title.Color);
tryit('T43 FontSizeMode', @() get(lg.Title, 'FontSizeMode'));
ax = gca;
ax.FontSize = 20;
say('T44 axes FontSize 20 -> lg.FontSize / title FontSize', [lg.FontSize, lg.Title.FontSize]);
delete(f);

[f, lg] = fresh();
ax = gca;
ax.FontSize = 20;
say('T45 axes FontSize 20 before any write -> lg.FontSize / title FontSize', [lg.FontSize, lg.Title.FontSize]);
lg.Title.String = 'T';
say('T46 then a title -> title FontSize', lg.Title.FontSize);
delete(f);

% the box grows under a title
[f, lg] = fresh();
lg.Units = 'pixels';
p0 = lg.Position;
lg.Title.String = 'Title';
drawnow;
p1 = lg.Position;
lg.Title.String = {'Two', 'lines'};
drawnow;
p2 = lg.Position;
lg.Title.String = 'A much longer legend title than any entry';
drawnow;
p3 = lg.Position;
say('T47 positions none/one/two/long', [p0; p1; p2; p3]);
delete(f);

% delete, a remade legend, display
[f, lg] = fresh();
lg.Title.String = 'Kept';
tryit('T48 delete(lg.Title)', @() delete(lg.Title));
tryit('T49 after delete: isvalid / String', @() {isvalid(lg.Title), lg.Title.String});
delete(f);

[f, lg] = fresh();
lg.Title.String = 'Kept';
lg2 = legend('again');
say('T50 legend(''again'') keeps the title', {isequal(lg, lg2), lg2.Title.String});
legend('off');
lg3 = legend('third');
say('T51 legend off then legend: title', lg3.Title.String);
delete(f);

[f, lg] = fresh();
lg.Title.String = 'Shown';
fprintf('--- T52 display of lg.Title\n%s', evalc('display(lg.Title)'));
fprintf('--- T53 disp of lg.Title\n%s', evalc('disp(lg.Title)'));
delete(f);

[f, lg] = fresh();
tryit('T54 legend(''a'', ''Title'', ''x'')', @() class(legend('a', 'Title', 'x')));
delete(f);

% savefig round trip
[f, lg] = fresh();
lg.Title.String = 'Saved';
lg.Title.FontWeight = 'normal';
tmp = [tempname '.fig'];
savefig(f, tmp);
delete(f);
h = openfig(tmp, 'invisible');
lgr = findobj(h, 'Type', 'legend');
say('T55 savefig/openfig: String / FontWeight', {lgr.Title.String, lgr.Title.FontWeight});
delete(h);
delete(tmp);
fprintf('done\n');

function [f, lg] = fresh()
f = figure('Visible', 'off');
plot(1:3);
hold on;
plot(3:-1:1);
lg = legend('up', 'down');
end

function say(label, value)
fprintf('--- %s\n  %s\n', label, v2s(value));
end

function tryit(label, fn)
fprintf('--- %s\n', label);
try
    out = fn();
    fprintf('  ok: %s\n', v2s(out));
catch e
    fprintf('  error: %s | %s\n', e.identifier, e.message);
end
end

function out = then_read(do, read)
do();
out = read();
end

function out = setfield_obj(obj, name, value)
obj.(name) = value;
out = 'assigned';
end

function s = v2s(x)
if ischar(x)
    s = ['''' x ''' char' mat2str(size(x))];
elseif isstring(x)
    s = ['string ' mat2str(size(x)) ' ' char(strjoin(x, '|'))];
elseif iscell(x)
    parts = cellfun(@v2s, x, 'UniformOutput', false);
    s = ['{' strjoin(parts, ', ') '} cell' mat2str(size(x))];
elseif isnumeric(x) || islogical(x)
    s = [mat2str(x, 6) ' ' class(x)];
elseif isa(x, 'matlab.graphics.Graphics') || isobject(x)
    s = ['<' class(x) ' ' mat2str(size(x)) '>'];
elseif isstruct(x)
    s = ['struct: ' strjoin(fieldnames(x)', ' ')];
elseif isa(x, 'function_handle')
    s = func2str(x);
else
    s = ['<' class(x) '>'];
end
end
