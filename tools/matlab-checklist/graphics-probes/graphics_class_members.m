% graphics_class_members.m -- what methods(h) and properties(h) answer for each graphics and app
% component class, in R2025b's order, as METHODS|class|names and PROPS|class|names lines (open item
% 68). gen_graphics_class_names.ps1 runs it in R2025b and writes
% src/JGraph.Scripting/Jgs/JgsGraphicsProperties.R2025bMembers.cs from the output.
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
h{end+1} = legend(ax);
h{end+1} = colorbar(ax);
h{end+1} = ax.XAxis;
h{end+1} = histogram(axes(figure('Visible', 'off')), randn(1, 10));
h{end+1} = histogram2(axes(figure('Visible', 'off')), randn(1, 10), randn(1, 10));
h{end+1} = bubblechart(axes(figure('Visible', 'off')), 1:3, 1:3, 1:3);
h{end+1} = boxchart(axes(figure('Visible', 'off')), randn(5, 1));
h{end+1} = heatmap(figure('Visible', 'off'), magic(3));
h{end+1} = polaraxes(figure('Visible', 'off'));
af = figure('Visible', 'off');
h{end+1} = annotation(af, 'textbox');
h{end+1} = annotation(af, 'arrow');
h{end+1} = annotation(af, 'line');
h{end+1} = annotation(af, 'ellipse');
h{end+1} = annotation(af, 'doublearrow');
h{end+1} = annotation(af, 'textarrow');
cf = figure('Visible', 'off');
h{end+1} = uicontrol(cf);
h{end+1} = uipanel(cf);
h{end+1} = uibuttongroup(cf);
h{end+1} = uimenu(cf);
h{end+1} = uicontextmenu(cf);
tb = uitoolbar(cf);
h{end+1} = tb;
h{end+1} = uipushtool(tb);
h{end+1} = uitoggletool(tb);
u = uifigure('Visible', 'off');
h{end+1} = u;
h{end+1} = uiaxes(u);
makers = {@uibutton, @uicheckbox, @uidatepicker, @uidropdown, @uieditfield, @uigauge, @uihtml, ...
    @uiimage, @uiknob, @uilabel, @uilamp, @uilistbox, @uiradiobutton, @uislider, @uispinner, ...
    @uiswitch, @uitextarea, @uitogglebutton, @uitree, @uitable, @uipanel, @uigridlayout, ...
    @uitabgroup, @uibuttongroup, @uihyperlink, @uicolorpicker, @uimenu, @uicontextmenu, @uitoolbar};
for k = 1:numel(makers)
    try
        h{end+1} = makers{k}(u); %#ok<AGROW>
    catch
    end
end
h{end+1} = uibutton(u, 'state');
h{end+1} = uieditfield(u, 'numeric');
h{end+1} = uigauge(u, 'linear');
h{end+1} = uigauge(u, 'ninetydegree');
h{end+1} = uigauge(u, 'semicircular');
h{end+1} = uiknob(u, 'discrete');
h{end+1} = uiswitch(u, 'rocker');
h{end+1} = uiswitch(u, 'toggle');
h{end+1} = uislider(u, 'range');
h{end+1} = uitree(u, 'checkbox');
t = uitree(u);
h{end+1} = uitreenode(t);
tg = uitabgroup(u);
h{end+1} = uitab(tg);
ut = uitoolbar(u);
h{end+1} = uipushtool(ut);
h{end+1} = uitoggletool(ut);
seen = {};
for k = 1:numel(h)
    for e = 1:numel(h{k})
        obj = h{k}(e);
        name = class(obj);
        if any(strcmp(seen, name)), continue, end
        seen{end+1} = name; %#ok<AGROW>
        fprintf('METHODS|%s|%s\n', name, strjoin(methods(obj)', ' '));
        fprintf('PROPS|%s|%s\n', name, strjoin(properties(obj)', ' '));
        % The printed listing leaves out what handle gives and nothing overrides, and puts the
        % static methods apart: LISTED rows hold its instance names, STATIC rows the others.
        listing = splitlines(evalc('methods(obj)'));
        part = '';
        shown = struct('LISTED', {{}}, 'STATIC', {{}});
        for j = 1:numel(listing)
            row = strtrim(listing{j});
            if startsWith(row, 'Methods for class'), part = 'LISTED'; continue, end
            if strcmp(row, 'Static methods:'), part = 'STATIC'; continue, end
            if isempty(row) || isempty(part) || contains(row, '<a href'), continue, end
            shown.(part) = [shown.(part), strsplit(row)];
        end
        fprintf('LISTED|%s|%s\n', name, strjoin(shown.LISTED, ' '));
        fprintf('STATIC|%s|%s\n', name, strjoin(shown.STATIC, ' '));
        % events(h), and every event addlistener takes (the hidden ones too, an axes' Hit among
        % them), and the properties a PreSet or PostSet listener may watch (open item 56).
        mc = metaclass(obj);
        fprintf('EVENTS|%s|%s\n', name, strjoin(events(obj)', ' '));
        fprintf('LISTENS|%s|%s\n', name, strjoin(sort({mc.EventList.Name}), ' '));
        props = mc.PropertyList;
        keep = arrayfun(@(p) p.SetObservable && ischar(p.GetAccess) && strcmp(p.GetAccess, 'public') ...
            && ~endsWith(p.Name, '_I'), props);
        fprintf('OBSERVABLE|%s|%s\n', name, strjoin(sort({props(keep).Name}), ' '));
    end
end
close all force
delete(findall(groot, 'Type', 'figure'));
