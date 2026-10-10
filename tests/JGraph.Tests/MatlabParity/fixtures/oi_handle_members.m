% record: -noFigureWindows
% Open items 68, 88 and 56 (ADR 0222): a graphics handle answers as R2025b's object does. Through the
% dot a method of its class is called with the handle first and an unknown name is refused in the
% dot's words; methods(h), properties(h) and events(h) are R2025b's lists; get(h) lists the names
% R2025b's does, a name the model lacked answering R2025b's value; a rectangle and an animated line
% are their own classes; addlistener takes any event the class defines and a PostSet on any
% observable property. Probes probe_68, probe_68b, probe_88, probe_88b and probe_56 (open-items
% scratch); the lists are tools/matlab-checklist/graphics-probes/graphics_class_members.m.

f = figure('Visible', 'off');
ax = axes(f);
hold(ax, 'on');
ln = plot(ax, 1:3);
u = uifigure('Visible', 'off');
t = uitree(u);
n = uitreenode(t, 'Text', 'a');
uitreenode(n, 'Text', 'b');
h = uihtml(u);
b = uibutton(u);

% --- item 68: methods through the dot, the dot's refusals, the lists
u9b_chk('dot_tree_expand', @() as_statement(@() t.expand()));
u9b_chk('dot_node_collapse', @() as_statement(@() n.collapse));
u9b_chk('dot_html_send', @() as_statement(@() h.sendEventToHTMLSource('x', 1)));
u9b_chk('dot_get', @() ln.get('Marker'));
u9b_chk('dot_set', @() as_statement(@() ln.set('MarkerSize', 9)));
u9b_chk('dot_set_read', @() ln.MarkerSize);
u9b_chk('dot_isvalid', @() ln.isvalid);
u9b_chk('dot_isprop', @() ln.isprop('Color'));
u9b_chk('dot_unknown_read_html', @() as_statement(@() h.Bogus));
u9b_chk('dot_unknown_write_html', @() as_statement(@() set_dot(h)));
u9b_chk('dot_unknown_read_line', @() as_statement(@() ln.Bogus));
u9b_chk('dot_unknown_write_line', @() as_statement(@() set_dot(ln)));
u9b_chk('methods_line', @() strjoin(methods(ln)', ' '));
u9b_chk('methods_tree', @() strjoin(methods(t)', ' '));
u9b_chk('methods_figure', @() strjoin(methods(u)', ' '));
u9b_chk('properties_line', @() strjoin(properties(ln)', ' '));
u9b_chk('properties_html', @() strjoin(properties(h)', ' '));
u9b_chk('properties_button', @() strjoin(properties(b)', ' '));
u9b_chk('ismethod_line', @() [ismethod(ln, 'get'), ismethod(ln, 'bogus'), ismethod(t, 'expand')]);
u9b_chkdiv('methods_listing_line', @() printed('methods', ln), '0222');
u9b_chk('properties_listing_line', @() printed('properties', ln));

% --- item 88: the names the model lacked, rectangle and animatedline, the bubble limits
r = rectangle(ax, 'Position', [1 2 3 4], 'Curvature', 0.5);
u9b_chk('rect_type', @() r.Type);
u9b_chk('rect_position', @() r.Position);
u9b_chk('rect_curvature', @() r.Curvature);
r.Position = [0 0 2 2];
u9b_chk('rect_position_set', @() r.Position);
a = animatedline(ax);
u9b_chk('anim_type', @() a.Type);
u9b_chk('anim_cap', @() a.MaximumNumPoints);
a.MaximumNumPoints = 3;
addpoints(a, 1:5, 1:5);
[x, ~] = getpoints(a);
u9b_chk('anim_kept', @() x);
u9b_chk('grid_width', @() {ax.GridLineWidth, ax.GridLineWidthMode});
ax.GridLineWidth = 2;
u9b_chk('grid_width_set', @() {ax.GridLineWidth, ax.GridLineWidthMode});
bb = bar(ax, 1:3);
u9b_chk('bar_labels', @() {class(bb.Labels), size(bb.Labels), bb.GroupWidth, bb.LabelLocation});
u9b_chk('line_affects_limits', @() char(ln.AffectAutoLimits));
u9b_chk('legend_direction', @() get(legend(ax), 'Direction'));
u9b_chk('bubble_limits_get', @() as_statement(@() get(ax, 'BubbleSizeLimits')));
u9b_chk('bubble_range_set', @() as_statement(@() set(ax, 'BubbleSizeRange', [1 2])));
u9b_chk('bubble_limits_dot', @() as_statement(@() ax.BubbleSizeLimits));
u9b_chk('histogram_in_axes', @() get(histogram(axes(figure('Visible', 'off')), [1 2 2 3]), 'NumBins'));
u9b_chk('heatmap_in_figure', @() class(get(heatmap(figure('Visible', 'off'), magic(3)), 'ColorData')));
u9b_chkdiv('names_line', @() numel(fieldnames(get(ln))), '0222');
u9b_chk('names_text', @() numel(fieldnames(get(text(ax, 1, 1, 'z')))));
u9b_chk('names_rectangle', @() strjoin(sort(fieldnames(get(r)))', ' '));
u9b_chk('names_animatedline', @() strjoin(sort(fieldnames(get(a)))', ' '));

% --- item 56: events, listeners on them, PostSet
u9b_chk('events_axes', @() strjoin(events(ax)', ' '));
u9b_chk('events_line', @() strjoin(events(ln)', ' '));
u9b_chk('events_button', @() strjoin(events(b)', ' '));
setappdata(f, 'hits', {});
l1 = addlistener(ax, 'XLim', 'PostSet', @(s, e) setappdata(f, 'hits', [getappdata(f, 'hits'), {[e.EventName ' ' s.Name]}]));
xlim(ax, [0 5]);
ax.XLim = [0 6];
u9b_chk('postset_hits', @() strjoin(getappdata(f, 'hits'), '; '));
u9b_chk('postset_class', @() class(l1));
u9b_chk('hit_listener', @() class(addlistener(ax, 'Hit', @(s, e) 1)));
u9b_chk('pushed_listener', @() class(addlistener(b, 'ButtonPushed', @(s, e) 1)));
u9b_chk('bad_event', @() as_statement(@() addlistener(ln, 'Bogus', @(s, e) 1)));
u9b_chk('bad_property', @() as_statement(@() addlistener(ln, 'Bogus', 'PostSet', @(s, e) 1)));
u9b_chk('not_observable', @() as_statement(@() addlistener(ln, 'Type', 'PostSet', @(s, e) 1)));
delete(l1);
close all force
delete(u);

function set_dot(h)
h.Bogus = 1;
end

function r = as_statement(fn)
% 'ran', or the refusal's identifier and sentence.
try
    fn();
    r = 'ran';
catch err
    r = [err.identifier ' | ' err.message];
end
end

function s = printed(verb, h) %#ok<INUSD>
% What a listing verb prints for h, its spacing folded.
s = squash(evalc([verb '(h)']));
end

function s = squash(s)
% The text with its layout's spacing folded, so a line says what was printed.
s = strtrim(regexprep(s, '\s+', ' '));
end
