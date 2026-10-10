% graphics_missing_defaults.m -- R2025b's value of each name get(h) lists that this build's model
% lacked (open item 88; the names are recorded/graphics_missing_names.txt). Run from this folder as
% matlab -noFigureWindows -batch graphics_missing_defaults > recorded/graphics_missing_defaults.txt;
% gen_graphics_defaults.py writes JgsGraphicsProperties.R2025bDefaults.cs from the output.
f = figure('Visible', 'off');
ax = axes(f);
hold(ax, 'on');
k = {};
h = {};
k{end+1} = 'figure'; h{end+1} = f;
k{end+1} = 'axes'; h{end+1} = ax;
k{end+1} = 'line'; h{end+1} = plot(ax, 1:3);
k{end+1} = 'text'; h{end+1} = text(ax, 1, 1, 'a');
k{end+1} = 'patch'; h{end+1} = patch(ax, [0 1 1], [0 0 1], 'r');
k{end+1} = 'surface'; h{end+1} = surface(ax, magic(3));
k{end+1} = 'image'; h{end+1} = image(ax, magic(3));
k{end+1} = 'scatter'; h{end+1} = scatter(ax, 1:3, 1:3);
k{end+1} = 'bar'; h{end+1} = bar(ax, 1:3);
k{end+1} = 'hggroup'; h{end+1} = hggroup('Parent', ax);
k{end+1} = 'hgtransform'; h{end+1} = hgtransform('Parent', ax);
k{end+1} = 'light'; h{end+1} = light(ax);
k{end+1} = 'stem'; h{end+1} = stem(ax, 1:3);
k{end+1} = 'stairs'; h{end+1} = stairs(ax, 1:3);
k{end+1} = 'area'; h{end+1} = area(ax, 1:3);
k{end+1} = 'errorbar'; h{end+1} = errorbar(ax, 1:3, [1 1 1]);
k{end+1} = 'quiver'; h{end+1} = quiver(ax, 1:3, 1:3, 1:3, 1:3);
[~, c] = contour(ax, magic(4)); k{end+1} = 'contour'; h{end+1} = c;
k{end+1} = 'rectangle'; h{end+1} = rectangle(ax);
k{end+1} = 'xline'; h{end+1} = xline(ax, 1);
k{end+1} = 'animatedline'; h{end+1} = animatedline(ax);
k{end+1} = 'legend'; h{end+1} = legend(ax);
k{end+1} = 'colorbar'; h{end+1} = colorbar(ax);
k{end+1} = 'ruler'; h{end+1} = ax.XAxis;
k{end+1} = 'title'; h{end+1} = ax.Title;
k{end+1} = 'histogram'; h{end+1} = histogram(axes(figure('Visible', 'off')), [1 2 2 3]);
k{end+1} = 'histogram2'; h{end+1} = histogram2(axes(figure('Visible', 'off')), [1 2 3], [1 2 3]);
k{end+1} = 'bubblechart'; h{end+1} = bubblechart(axes(figure('Visible', 'off')), 1:3, 1:3, 1:3);
k{end+1} = 'boxchart'; h{end+1} = boxchart(axes(figure('Visible', 'off')), [1; 2; 3; 4; 5]);
k{end+1} = 'heatmap'; h{end+1} = heatmap(figure('Visible', 'off'), magic(3));
k{end+1} = 'polaraxes'; h{end+1} = polaraxes(figure('Visible', 'off'));
af = figure('Visible', 'off');
k{end+1} = 'textbox'; h{end+1} = annotation(af, 'textbox');
k{end+1} = 'arrow'; h{end+1} = annotation(af, 'arrow');
k{end+1} = 'annline'; h{end+1} = annotation(af, 'line');
k{end+1} = 'ellipse'; h{end+1} = annotation(af, 'ellipse');
k{end+1} = 'doublearrow'; h{end+1} = annotation(af, 'doublearrow');
k{end+1} = 'textarrow'; h{end+1} = annotation(af, 'textarrow');
uf = uifigure('Visible', 'off');
k{end+1} = 'uiaxes'; h{end+1} = uiaxes(uf);
pairs = strsplit(strtrim(fileread(fullfile('recorded', 'graphics_missing_names.txt'))), newline);
for p = 1:numel(pairs)
    parts = strsplit(strtrim(pairs{p}), '|');
    j = find(strcmp(k, parts{1}), 1);
    name = parts{2};
    try
        v = get(h{j}, name);
        fprintf('%s|%s|%s|%s|%s\n', parts{1}, name, class(v), mat2str(size(v)), render(v));
    catch e
        fprintf('%s|%s|ERR|%s\n', parts{1}, name, e.message);
    end
end
close all force
delete(uf);

function s = render(v)
if ischar(v)
    s = v;
elseif isa(v, 'matlab.lang.OnOffSwitchState')
    s = char(v);
elseif isnumeric(v) || islogical(v)
    s = mat2str(v, 17);
elseif iscell(v) && isempty(v)
    s = '{}';
elseif isstring(v)
    s = strjoin(v, '\t');
elseif isa(v, 'function_handle')
    s = func2str(v);
else
    s = '?';
end
end
