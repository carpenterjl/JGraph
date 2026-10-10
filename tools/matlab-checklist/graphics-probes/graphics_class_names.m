% graphics_class_names.m -- every public-readable property of each graphics class, hidden ones
% included, as class|Type|name|hidden lines (open item 82). gen_graphics_class_names.ps1 runs it in
% R2025b and writes src/JGraph.Scripting/Jgs/JgsGraphicsProperties.R2025bNames.cs from the output.
f = figure('Visible', 'off');
ax = axes(f);
hold(ax, 'on');
h = {};
h{end+1} = f;
h{end+1} = ax;
h{end+1} = plot(ax, 1:3);
h{end+1} = text(ax, 1, 1, 'a');
h{end+1} = patch(ax, [0 1 1], [0 0 1], 'r');
h{end+1} = surface(ax, magic(3));
h{end+1} = image(ax, magic(3));
h{end+1} = scatter(ax, 1:3, 1:3);
h{end+1} = bar(ax, 1:3);
h{end+1} = hggroup('Parent', ax);
h{end+1} = hgtransform('Parent', ax);
h{end+1} = light(ax);
h{end+1} = stem(ax, 1:3);
h{end+1} = stairs(ax, 1:3);
h{end+1} = area(ax, 1:3);
h{end+1} = errorbar(ax, 1:3, [1 1 1]);
h{end+1} = quiver(ax, 1:3, 1:3, 1:3, 1:3);
[~, h{end+1}] = contour(ax, magic(4));
h{end+1} = rectangle(ax);
h{end+1} = xline(ax, 1);
h{end+1} = animatedline(ax);
h{end+1} = fplot(ax, @sin);
h{end+1} = fimplicit(ax, @(x, y) x.^2 + y.^2 - 1);
h{end+1} = legend(ax);
h{end+1} = colorbar(ax);
h{end+1} = ax.XAxis;
h{end+1} = ax.Title;
f2 = figure('Visible', 'off');
h{end+1} = histogram(axes(f2), randn(1, 10));
h{end+1} = histogram2(axes(figure('Visible', 'off')), randn(1, 10), randn(1, 10));
h{end+1} = bubblechart(axes(figure('Visible', 'off')), 1:3, 1:3, 1:3);
h{end+1} = swarmchart(axes(figure('Visible', 'off')), [1 1 2], 1:3);
h{end+1} = boxchart(axes(figure('Visible', 'off')), randn(5, 1));
h{end+1} = fsurf(axes(figure('Visible', 'off')), @(x, y) x + y);
h{end+1} = fmesh(axes(figure('Visible', 'off')), @(x, y) x + y);
h{end+1} = fcontour(axes(figure('Visible', 'off')), @(x, y) x + y);
h{end+1} = fplot3(axes(figure('Visible', 'off')), @sin, @cos, @(t) t);
h{end+1} = quiver3(axes(figure('Visible', 'off')), 1, 1, 1, 1, 1, 1);
h{end+1} = scatter3(axes(figure('Visible', 'off')), 1:3, 1:3, 1:3);
h{end+1} = heatmap(figure('Visible', 'off'), magic(3));
pf = figure('Visible', 'off');
pax = polaraxes(pf);
h{end+1} = pax;
h{end+1} = pax.RAxis;
h{end+1} = pax.ThetaAxis;
h{end+1} = polarhistogram(polaraxes(figure('Visible', 'off')), rand(1, 10));
af = figure('Visible', 'off');
h{end+1} = annotation(af, 'textbox');
h{end+1} = annotation(af, 'arrow');
h{end+1} = annotation(af, 'line');
h{end+1} = annotation(af, 'rectangle');
h{end+1} = annotation(af, 'ellipse');
h{end+1} = annotation(af, 'doublearrow');
h{end+1} = annotation(af, 'textarrow');
h{end+1} = stackedplot(figure('Visible', 'off'), rand(4, 2));
h{end+1} = pie(axes(figure('Visible', 'off')), [1 2]);
uf = uifigure('Visible', 'off');
h{end+1} = uiaxes(uf);
seen = {};
for k = 1:numel(h)
    for e = 1:numel(h{k})
        obj = h{k}(e);
        mc = metaclass(obj);
        if any(strcmp(seen, mc.Name)), continue, end
        seen{end+1} = mc.Name; %#ok<AGROW>
        if isprop(obj, 'Type'), ty = get(obj, 'Type'); else, ty = '-'; end
        for p = mc.PropertyList'
            if ischar(p.GetAccess) && strcmp(p.GetAccess, 'public')
                fprintf('%s|%s|%s|%d\n', mc.Name, ty, p.Name, p.Hidden);
            end
        end
    end
end
% The words set(h) answers for each property that takes some (open item 41), as SET rows of the
% class, the property and the words joined by tabs; no words is a colour's 0-by-1 cell. An on/off
% property's words are its two states.
seen = {};
for k = 1:numel(h)
    for e = 1:numel(h{k})
        obj = h{k}(e);
        name = class(obj);
        if any(strcmp(seen, name)), continue, end
        seen{end+1} = name; %#ok<AGROW>
        s = set(obj);
        for p = fieldnames(s)'
            v = s.(p{1});
            % A colour's words are a 0-by-1 cell where a property without any has a 0-by-0 one.
            if iscell(v) && isequal(size(v), [0 1])
                fprintf('SET|%s|%s|\n', name, p{1});
                continue
            end
            if isempty(v) || ~iscell(v), continue, end
            words = cellfun(@char, v(:)', 'UniformOutput', false);
            fprintf('SET|%s|%s|%s\n', name, p{1}, strjoin(words, char(9)));
        end
    end
end

% A line or scatter in polar axes gains its polar names as dynamic properties, which metaclass does
% not see: what get lists for each is recorded under the class name and @polaraxes.
polar = {polarplot(polaraxes(figure('Visible', 'off')), 1:3, 1:3), ...
    polarscatter(polaraxes(figure('Visible', 'off')), 1:3, 1:3)};
for k = 1:numel(polar)
    names = fieldnames(get(polar{k}));
    for j = 1:numel(names)
        fprintf('%s@polaraxes|%s|%s|0\n', class(polar{k}), get(polar{k}, 'Type'), names{j});
    end
end
close all force
