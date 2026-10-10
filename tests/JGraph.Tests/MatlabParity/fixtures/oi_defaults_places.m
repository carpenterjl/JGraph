% record: -noFigureWindows
% Open items 44, 46, 48 and 55 (ADR 0223): the root's, a figure's and an axes' Default* values, which
% new objects take, with 'remove', 'factory' and 'default'; a text's 'normalized' unit as a fraction
% of the plot box, with Position moved into a unit it changes to; a component made with
% 'Parent', [] and placed later, and matlab.ui.layout.GridLayoutOptions as a value; a uifigure whose
% handle is visible as the current figure. Probes probe_44, probe_44b, probe_46, probe_48 and probe_55
% (open-items scratch). The factory values are this build's own (a line's width is decision D1's).
close all force

% --- item 44: defaults
u9b_chk('factory_uicontrol_font', @() {get(0, 'FactoryUicontrolFontSize'), get(0, 'FactoryUicontrolFontName')});
u9b_chkdiv('factory_line_width', @() get(groot, 'FactoryLineLineWidth'), '0223');
u9b_chkdiv('factory_axes_font', @() get(groot, 'FactoryAxesFontSize'), '0223');
u9b_chk('bad_class', @() as_statement(@() get(0, 'DefaultBogusThing')));
u9b_chk('bad_factory_property', @() as_statement(@() get(0, 'FactoryAxesBogus')));
u9b_chk('bad_default_property', @() as_statement(@() set(0, 'DefaultLineBogus', 1)));
u9b_chk('root_default_none', @() isempty(get(groot, 'DefaultLineMarkerSize')));
set(0, 'DefaultAxesFontSize', 14);
u9b_chk('default_set', @() get(0, 'DefaultAxesFontSize'));
ax = axes(figure('Visible', 'off'));
u9b_chk('default_taken', @() ax.FontSize);
set(0, 'DefaultAxesFontSize', 'remove');
ax2 = axes(figure('Visible', 'off'));
u9b_chk('default_removed', @() ax2.FontSize == get(0, 'FactoryAxesFontSize'));
set(0, 'DefaultLineLineWidth', 3);
p = plot(axes(figure('Visible', 'off')), 1:3);
u9b_chk('line_default_width', @() p.LineWidth);
set(p, 'LineWidth', 'factory');
u9b_chk('line_factory_word', @() p.LineWidth == get(0, 'FactoryLineLineWidth'));
set(p, 'LineWidth', 1);
set(p, 'LineWidth', 'default');
u9b_chk('line_default_word', @() p.LineWidth);
f = figure('Visible', 'off');
u9b_chk('figure_inherits_root', @() get(f, 'DefaultLineLineWidth'));
u9b_chk('figure_holds_none', @() class(get(f, 'Default')));
set(0, 'DefaultLineLineWidth', 'remove');
fa = axes(f);
set(fa, 'DefaultLineLineWidth', 6);
l1 = line(fa, 1:2, 1:2);
l2 = line(axes(f), 1:2, 1:2);
u9b_chk('axes_default_reaches_its_own', @() [l1.LineWidth, l2.LineWidth == get(0, 'FactoryLineLineWidth')]);
u9b_chk('axes_default_struct', @() fieldnames(get(fa, 'Default'))');
set(f, 'DefaultLineColor', [1 0 0]);
p2 = plot(axes(f), 1:3);
u9b_chk('plot_colour_beats_default', @() isequal(p2.Color, [1 0 0]));
u9b_chk('figure_default_read', @() get(f, 'DefaultLineColor'));
set(0, 'DefaultFigureColor', [0.25 0.5 0.75]);
g = figure('Visible', 'off');
u9b_chkdiv('figure_colour_default', @() g.Color, '0223');
set(0, 'DefaultFigureColor', 'remove');
close all force

% --- item 46: a text's normalized unit
f = figure('Visible', 'off', 'Position', [100 100 560 420]);
ax = axes(f, 'Units', 'pixels', 'Position', [60 50 400 300]);
xlim(ax, [0 10]);
ylim(ax, [0 100]);
t = text(ax, 0.5, 0.5, 'mid', 'Units', 'normalized');
u9b_chk('normalized_position', @() t.Position);
t.Units = 'data';
u9b_chk('to_data', @() round(t.Position(1:2), 9));
t.Units = 'pixels';
u9b_chk('to_pixels', @() t.Position);
t.Units = 'normalized';
u9b_chk('back_to_normalized', @() t.Position);
d = text(ax, 2, 25, 'd');
d.Units = 'normalized';
u9b_chk('data_to_normalized', @() d.Position);
xlim(ax, [0 20]);
u9b_chk('normalized_keeps_fraction', @() d.Position);
d.Units = 'data';
u9b_chk('normalized_to_new_data', @() round(d.Position(1:2), 9));
close all force

% --- item 48: no parent yet, and GridLayoutOptions
b = uibutton('Parent', []);
u9b_chk('detached_parent_empty', @() isempty(b.Parent));
u9b_chk('detached_text', @() b.Text);
u = uifigure('Visible', 'off');
b.Parent = u;
u9b_chk('placed', @() [isequal(b.Parent, u), numel(u.Children)]);
pn = uipanel('Parent', []);
u9b_chk('panel_detached', @() isempty(pn.Parent));
ua = uiaxes('Parent', []);
u9b_chk('uiaxes_detached', @() isempty(ua.Parent));
ua.Parent = u;
u9b_chk('uiaxes_placed', @() isequal(ua.Parent, u));
g = uigridlayout(u, [2 2]);
o = matlab.ui.layout.GridLayoutOptions('Row', 2, 'Column', [1 2]);
u9b_chk('options', @() {class(o), o.Row, o.Column});
c = uibutton(g);
c.Layout = o;
u9b_chk('options_placed', @() {c.Layout.Row, c.Layout.Column});
u9b_chk('options_default', @() [numel(matlab.ui.layout.GridLayoutOptions().Row) numel(matlab.ui.layout.GridLayoutOptions().Column)]);
close all force
delete(u);

% --- item 55: a uifigure whose handle is visible is the current figure
u = uifigure('Visible', 'off');
u9b_chk('hidden_is_not_current', @() isequal(gcf, u));
close all force
delete(u);
u = uifigure('Visible', 'off', 'HandleVisibility', 'on');
u9b_chk('visible_is_current', @() [isequal(gcf, u), isequal(get(groot, 'CurrentFigure'), u)]);
u9b_chk('gca_in_it', @() isequal(ancestor(gca, 'figure'), u));
pl = plot(1:3);
u9b_chk('plot_in_it', @() [isequal(ancestor(pl, 'figure'), u), numel(findall(groot, 'Type', 'figure'))]);
close all force
delete(u);
f = figure('Visible', 'off');
u = uifigure('Visible', 'off', 'HandleVisibility', 'on');
u9b_chk('made_last_wins', @() isequal(gcf, u));
figure(f);
u9b_chk('figure_raises', @() isequal(gcf, f));
close all force
delete(u);

function r = as_statement(fn)
% 'ran', or the refusal's identifier and sentence.
try
    fn();
    r = 'ran';
catch err
    r = [err.identifier ' | ' err.message];
end
end
