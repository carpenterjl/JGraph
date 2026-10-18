% oi139_shapes - open item 139: what hgsave writes for a colour bar's label, a legend's title, an
% axes' subtitle and a polar axes' title, and what R2025b's loader rebuilds from the tree alone.
% Headless. Part A: four figures written by hgsave, stripped to hgS_070000, dumped (u11dump) and
% reopened. Part B: trees built by hand in the shapes Part A shows, the risky ones last (an access
% violation ends the run; OI139_FROM resumes from a case).
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, '..', 'u11'));
rt = fullfile(here, 'rt');
if ~isfolder(rt), mkdir(rt); end
warning('off', 'MATLAB:hgsave:HgsaveToBeRemoved');
set(groot, 'DefaultFigureVisible', 'off');
fprintf('%s\n', version);
from = str2double(getenv('OI139_FROM'));
if isnan(from), from = 1; end

%% --- Part A -----------------------------------------------------------------------------------------
figs = {};

f = figure('Name', 'cbl', 'Tag', 'cbl');
imagesc(magic(4));
cb = colorbar;
cb.Label.String = 'Heat';
cb.Label.FontSize = 14;
cb.Label.Color = [1 0 0];
cb.Label.FontWeight = 'bold';
set(cb, 'Tag', 'cb');
figs{end + 1} = {'cbl', f};

f = figure('Name', 'cbh', 'Tag', 'cbh');
imagesc(magic(4));
cb = colorbar('southoutside');
ylabel(cb, 'Below');
set(cb, 'Tag', 'cb');
figs{end + 1} = {'cbh', f};

f = figure('Name', 'lgt', 'Tag', 'lgt');
plot(1:5, [1 4 2 5 3], 'Tag', 'first');
hold on;
plot(1:5, [2 2 3 3 4], 'Tag', 'second');
lg = legend('a', 'b', 'Location', 'northwest');
title(lg, 'Series');
lg.Title.FontSize = 12;
lg.Title.Color = [0 0 1];
set(lg, 'Tag', 'lg');
figs{end + 1} = {'lgt', f};

f = figure('Name', 'sub', 'Tag', 'sub');
plot(1:5, [1 4 2 5 3]);
title('Main');
subtitle('Below the main');
xlabel('X');
set(gca, 'Tag', 'ax');
figs{end + 1} = {'sub', f};

f = figure('Name', 'sub2', 'Tag', 'sub2');
plot(1:5, [1 4 2 5 3]);
subtitle('Only a subtitle');
set(gca, 'Tag', 'ax');
figs{end + 1} = {'sub2', f};

f = figure('Name', 'pol', 'Tag', 'pol');
pax = polaraxes;
polarplot(pax, linspace(0, 2 * pi, 20), 1:20, 'Tag', 'pl');
title(pax, 'Polar');
subtitle(pax, 'Polar sub');
set(pax, 'Tag', 'pax');
figs{end + 1} = {'pol', f};

for i = 1:numel(figs)
    name = figs{i}{1};
    f = figs{i}{2};
    full = fullfile(rt, ['s_' name '.fig']);
    only = fullfile(rt, ['s_' name '_only.fig']);
    hgsave(f, full);
    delete(f);
    s = load(full, '-mat', 'hgS_070000');
    hgS_070000 = s.hgS_070000; %#ok<NASGU>
    save(only, 'hgS_070000', '-mat');
    fprintf('\n=== %s (hgsave tree)\n', name);
    u11dump(only);
    fprintf('--- %s reopened from the tree alone\n', name);
    reopen(only);
end

%% --- Part B -----------------------------------------------------------------------------------------
fprintf('\n=== Part B: hand-built trees\n');
leaf = @(type, handle, props) struct('type', type, 'handle', handle, 'properties', props, 'children', [], 'special', []);
node = @(type, handle, props, kids, special) struct('type', type, 'handle', handle, 'properties', props, 'children', kids, 'special', special);
fr = 0.0001220703125;

line1 = leaf('graph2d.lineseries', 3 + fr, struct('XData', 1:3, 'YData', [2 4 3], 'Color', [1 0 0], 'Tag', 'L'));
line2 = leaf('graph2d.lineseries', 4 + fr, struct('XData', 1:3, 'YData', [1 1 2], 'Color', [0 0 1], 'Tag', 'L2'));
axes1 = node('axes', 2 + fr, struct('Tag', 'A', 'Position', [0.2 0.2 0.6 0.6]), [line1; line2], zeros(4, 1));
fig1 = node('figure', 1, struct('Name', 'hand', 'Tag', 'F'), axes1, []);

im = leaf('image', 5 + fr, struct('CData', magic(4), 'CDataMapping', 'scaled', 'Tag', 'I'));
axesI = node('axes', 2 + fr, struct('Tag', 'A', 'Position', [0.13 0.11 0.62 0.815], 'CLim', [1 16], 'YDir', 'reverse', 'XLim', [0.5 4.5], 'YLim', [0.5 4.5]), im, zeros(4, 1));
cbProps = struct('Location', 'eastoutside', 'Limits', [1 16], 'XLim', [-1 2], 'YLim', [1 16], 'Tag', 'CB', ...
    'ApplicationData', struct('NonDataObject', [], 'PostDeserializeFcn', @colorbarpostdeserialize), 'Axes', 2 + fr);

cases = {};
add = @(varargin) varargin;

% C1: a colour bar with a text child saying the label (the guess at hgsave's form; Part A shows it)
lbl = leaf('text', 6 + fr, struct('String', 'Heat', 'Color', [1 0 0], 'FontSize', 14, 'Tag', 'cblabel'));
cb1 = node('scribe.colorbar', 7 + fr, cbProps, lbl, []);
t = fig1; t.children = [axesI; cb1];
cases{end + 1} = add('C1 colorbar with a text child', t, @(h) {get(findobj(h, 'Type', 'colorbar'), 'Tag'), findobj(h, 'Type', 'colorbar').Label.String, findobj(h, 'Type', 'colorbar').Label.Color, numel(findall(h, 'Type', 'text'))});

% C2: a colour bar with Label written as a char (a handle property given text)
cb2 = node('scribe.colorbar', 7 + fr, setfield(cbProps, 'Label', 'Heat'), [], []); %#ok<SFLD>
t = fig1; t.children = [axesI; cb2];
cases{end + 1} = add('C2 colorbar with Label char', t, @(h) {findobj(h, 'Type', 'colorbar').Label.String});

% C3: a colour bar with a text child whose Description names it
lbl3 = lbl; lbl3.properties.Description = 'Colorbar Label';
cb3 = node('scribe.colorbar', 7 + fr, cbProps, lbl3, []);
t = fig1; t.children = [axesI; cb3];
cases{end + 1} = add('C3 colorbar text child with Description', t, @(h) {findobj(h, 'Type', 'colorbar').Label.String, numel(findall(h, 'Type', 'text'))});

% C4: a legend with Title written as a char
lgProps = struct('String', {{'first', 'second'}}, 'Location', 'northwest', 'Tag', 'LG', 'Axes', 2 + fr, 'PlotChildren', [3 + fr; 4 + fr], ...
    'ApplicationData', struct('NonDataObject', [], 'PostDeserializeFcn', @legendpostdeserialize, 'PlotChildren', [3 + fr; 4 + fr], 'PeerAxes', 2 + fr));
lg4 = node('scribe.legend', 8 + fr, setfield(lgProps, 'Title', 'Series'), [], []); %#ok<SFLD>
t = fig1; t.children = [t.children; lg4];
cases{end + 1} = add('C4 legend with Title char', t, @(h) {findobj(h, 'Type', 'legend').Title.String});

% C5: a legend with a text child as its title
ttl = leaf('text', 9 + fr, struct('String', 'Series', 'Color', [0 0 1], 'Tag', 'lgtitle'));
lg5 = node('scribe.legend', 8 + fr, lgProps, ttl, []);
t = fig1; t.children = [t.children; lg5];
cases{end + 1} = add('C5 legend with a text child', t, @(h) {findobj(h, 'Type', 'legend').Title.String, numel(findall(h, 'Type', 'text'))});

% C6: an axes with Subtitle written as a char
t = fig1; t.children.properties.Subtitle = 'Sub as char';
cases{end + 1} = add('C6 axes Subtitle char', t, @(h) {h.Children.Subtitle.String});

% C7: a polar axes with its title as a text child named by special
pl = leaf('graph2d.lineseries', 3 + fr, struct('ThetaData', linspace(0, 6, 10), 'RData', 1:10, 'Tag', 'PL'));
pt = leaf('text', 6 + fr, struct('String', 'Polar title', 'Tag', 'ptitle'));
pax7 = node('matlab.graphics.axis.PolarAxes', 2 + fr, struct('Tag', 'PA', 'Position', [0.2 0.2 0.6 0.6]), [pl; pt], [2; 0; 0; 0]);
t = fig1; t.children = pax7;
cases{end + 1} = add('C7 polar title by special', t, @(h) {class(h.Children), h.Children.Title.String, numel(h.Children.Children)});

% C8: a polar axes with Title written as a char
pax8 = node('matlab.graphics.axis.PolarAxes', 2 + fr, struct('Tag', 'PA', 'Position', [0.2 0.2 0.6 0.6], 'Title', 'Polar char'), pl, zeros(4, 1));
t = fig1; t.children = pax8;
cases{end + 1} = add('C8 polar Title char', t, @(h) {h.Children.Title.String});

% C9 (risky): an axes with a fifth special place for the subtitle
st = leaf('text', 6 + fr, struct('String', 'Sub by special', 'Tag', 'sub'));
t = fig1; t.children.children = [line1; line2; st]; t.children.special = [0; 0; 0; 0; 3];
cases{end + 1} = add('C9 axes subtitle by a fifth special', t, @(h) {h.Children.Subtitle.String, numel(h.Children.Children)});

for i = from:numel(cases)
    c = cases{i};
    fprintf('[%d] ', i);
    try1(rt, c{1}, c{2}, c{3});
end
fprintf('done\n');

%% ---------------------------------------------------------------------------------------------------
function try1(rt, label, tree, look)
file = fullfile(rt, [lower(regexprep(label, '[^A-Za-z0-9]+', '_')) '.fig']);
file = regexprep(file, '_\.fig$', '.fig');
hgS_070000 = tree; %#ok<NASGU>
save(file, 'hgS_070000', '-mat');
fprintf('--- %s\n', label);
lastwarn('');
try
    [txt, h] = evalc('openfig(file, ''invisible'')');
    if ~isempty(strtrim(txt)), fprintf('  printed: %s\n', strtrim(txt)); end
    [msg, id] = lastwarn;
    if ~isempty(msg), fprintf('  warning: %s | %s\n', id, msg); end
    try
        v = look(h);
        fprintf('  %s\n', shortv(v));
    catch e
        fprintf('  look failed: %s\n', e.message);
    end
    delete(h);
catch e
    fprintf('  error: %s | %s\n', e.identifier, e.message);
end
end

function reopen(file)
lastwarn('');
try
    [txt, h] = evalc('openfig(file, ''invisible'')');
    if ~isempty(strtrim(txt)), fprintf('  printed: %s\n', strtrim(txt)); end
    [msg, id] = lastwarn;
    if ~isempty(msg), fprintf('  warning: %s | %s\n', id, msg); end
    all = findall(h);
    fprintf('  findall: %s\n', strjoin(cellfun(@(x) char(x), get(all, 'Type'), 'UniformOutput', false)', ','));
    for a = findall(h, '-isa', 'matlab.graphics.axis.Axes')'
        fprintf('  axes %s: title=%s subtitle=%s(%s) xlabel=%s children=%s\n', a.Tag, a.Title.String, a.Subtitle.String, mat2str(a.Subtitle.FontSize), a.XLabel.String, strjoin(cellfun(@(x) char(x), cellstr(get(a.Children, 'Type')), 'UniformOutput', false)', ','));
    end
    for a = findall(h, 'Type', 'polaraxes')'
        fprintf('  polaraxes %s: title=%s subtitle=%s children=%s\n', a.Tag, a.Title.String, a.Subtitle.String, strjoin(cellfun(@(x) char(x), cellstr(get(a.Children, 'Type')), 'UniformOutput', false)', ','));
    end
    for lg = findobj(h, 'Type', 'legend')'
        fprintf('  legend: String=%s title=%s(%s %s)\n', strjoin(lg.String, '|'), lg.Title.String, mat2str(lg.Title.FontSize), mat2str(lg.Title.Color));
    end
    for cb = findobj(h, 'Type', 'colorbar')'
        fprintf('  colorbar: Location=%s label=%s(%s %s %s rot=%s)\n', cb.Location, cb.Label.String, mat2str(cb.Label.FontSize), mat2str(cb.Label.Color), cb.Label.FontWeight, mat2str(cb.Label.Rotation));
    end
    delete(h);
catch e
    fprintf('  error: %s | %s\n', e.identifier, e.message);
end
end

function t = shortv(v)
if iscell(v)
    t = ['{' strjoin(cellfun(@shortv, v, 'UniformOutput', false), ' ; ') '}'];
elseif ischar(v)
    t = ['''' v ''''];
elseif isstring(v)
    t = ['"' strjoin(cellstr(v), '","') '"'];
elseif isnumeric(v) || islogical(v)
    t = mat2str(v, 5);
elseif isa(v, 'matlab.lang.OnOffSwitchState')
    t = char(v);
else
    t = ['<' class(v) '>'];
end
end
