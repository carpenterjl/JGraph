% record: -noFigureWindows
% Open items 47, 60, 65 and 87 (ADR 0230): a new axes' default plot box and the limit-method words
% (87); arrays of uistyle objects and StyleConfigurations as a table of them (65); a uitable's
% display order, DisplayData and DisplaySelection, and a datetime variable edited in place (60);
% scroll and ScrollableViewportLocation on a grid, a panel, a tab and a uifigure (47). Probes
% probe_87, probe_65, probe_60, probe_60b and probe_47 (open-items scratch). The limits were padded
% by decision when this was recorded and are tick-aligned since open item 99 (ADR 0231).
close all force

% --- item 87: the plot box
f = figure('Visible', 'off', 'Position', [100 100 1120 840]);
ax = axes(f);
u9b_chk('ax_position', @() round(ax.Position, 4));
u9b_chk('ax_inner', @() round(ax.InnerPosition, 4));
u9b_chk('ax_outer', @() round(ax.OuterPosition, 4));
u9b_chk('ax_constraint', @() ax.PositionConstraint);
plot(ax, 1:3); title(ax, 'T'); xlabel(ax, 'x'); ylabel(ax, 'y'); drawnow;
u9b_chk('ax_position_labelled', @() round(ax.Position, 4));
u9b_chk('ax_xlim_plot13', @() ax.XLim);
u9b_chk('ax_limit_method_default', @() ax.XLimitMethod);
ax.XLimitMethod = 'tick';
u9b_chk('ax_limit_method_prefix', @() ax.XLimitMethod);
ax.XLimitMethod = "Padded";
u9b_chk('ax_limit_method_string', @() ax.XLimitMethod);
ax.XLimitMethod = 'tight';
u9b_chk('ax_limit_method_tight', @() ax.XLim);
u9b_chk('ax_limit_method_bogus', @() set(ax, 'XLimitMethod', 'bogus'));
u9b_chk('ax_limit_method_t', @() set(ax, 'XLimitMethod', 't'));
ax.OuterPosition = [0 0 0.5 1];
u9b_chk('ax_outer_written', @() round(ax.Position, 4));
u9b_chk('ax_outer_constraint', @() ax.PositionConstraint);
ax.Position = [0.2 0.3 0.4 0.5];
u9b_chk('ax_inner_written', @() round(ax.Position, 4));
u9b_chk('ax_inner_constraint', @() ax.PositionConstraint);
s1 = subplot(2, 1, 1, 'Parent', f);
u9b_chk('subplot_position', @() round(s1.Position, 4));
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);
ua = uiaxes(uf);
u9b_chk('uiaxes_position', @() ua.Position);
u9b_chk('uiaxes_limit_method', @() ua.XLimitMethod);

% --- item 65: arrays of styles and StyleConfigurations
s = uistyle('FontWeight', 'bold');
s2 = uistyle('BackgroundColor', [0 0 1], 'FontColor', 'g', 'FontWeight', 'bold', 'FontAngle', 'italic', 'FontName', 'Arial', ...
    'HorizontalAlignment', 'center', 'HorizontalClipping', 'left', 'Interpreter', 'html', 'IconAlignment', 'right', 'Icon', 'error');
a = [s s2];
u9b_chk('arr_size', @() size(a));
u9b_chk('arr_class', @() class(a));
u9b_chk('arr_numel', @() numel(a));
u9b_chk('arr_isempty', @() isempty(a));
u9b_chk('arr_isobject', @() isobject(a));
u9b_chk('arr_isa', @() isa(a, 'matlab.ui.style.Style'));
u9b_chk('arr_read', @() a(2).FontName);
u9b_chk('arr_end', @() a(end).FontAngle);
a(1).FontWeight = 'normal';
u9b_chk('arr_write', @() {a(1).FontWeight, s.FontWeight});
u9b_chk('arr_column', @() size([s; s2]));
u9b_chk('arr_four', @() size([a a]));
u9b_chk('arr_stack', @() size([a; a]));
u9b_chk('arr_pick', @() size(a([2 1 2])));
u9b_chk('arr_pick_class', @() class(a(2)));
u9b_chk('arr_none', @() size(a([])));
u9b_chk('arr_mixed', @() [s 1]);
u9b_chk('scalar_two', @() s(2));
u9b_chk('scalar_one', @() class(s(1)));
u9b_chk('scalar_numel', @() [numel(s) isempty(s)]);
a(3) = s2;
u9b_chk('arr_grown', @() size(a));
u9b_chk('arr_grown_read', @() a(3).FontName);
a(2) = [];
u9b_chk('arr_shrunk', @() size(a));
u9b_chk('arr_disp', @() oi_disp(a));
u9b_chk('style_disp', @() oi_disp(s));
u9b_chk('style_disp_empty', @() oi_disp(uistyle()));
u9b_chk('style_disp_full', @() oi_disp(s2));
u9b_chk('style_disp_cube', @() oi_disp(uistyle('Icon', zeros(4, 4, 3))));
n = 0;
for q = a
    n = n + strcmp(class(q), 'matlab.ui.style.Style');
end
u9b_chk('arr_loop', @() n);
t = uitable(uf, 'Data', magic(4));
addStyle(t, s, 'row', 1); addStyle(t, uistyle('BackgroundColor', 'red'), 'cell', [2 2]); addStyle(t, uistyle());
T = t.StyleConfigurations;
u9b_chk('sc_size', @() size(T));
u9b_chk('sc_style_class', @() class(T.Style));
u9b_chk('sc_style_size', @() size(T.Style));
u9b_chk('sc_style_read', @() T.Style(1).FontWeight);
u9b_chk('sc_style_colour', @() T.Style(2).BackgroundColor);
u9b_chk('sc_style_brace', @() T.Style{1});
u9b_chk('sc_target_class', @() class(T.Target)); % a categorical since open item 115 (ADR 0232)
u9b_chk('sc_target_eq', @() T.Target == 'row');
u9b_chk('sc_target_eq_string', @() T.Target == "cell");
u9b_chk('sc_target_string', @() string(T.Target)');
u9b_chk('sc_target_char', @() char(T.Target(1)));
u9b_chk('sc_target_cellstr', @() cellstr(T.Target)');
u9b_chk('sc_target_rows', @() size(T(T.Target == 'row', :)));
u9b_chk('sc_target_ismember', @() ismember('row', T.Target));
u9b_chk('sc_target_strcmp', @() strcmp(T.Target, 'row'));
u9b_chk('sc_target_brace', @() oi_try(@() T.Target{1}));
u9b_chk('sc_index', @() T.TargetIndex');
w = T.Style;
u9b_chk('sc_whole', @() {class(w), size(w)});
z = T.Style(1); z.FontWeight = 'normal';
u9b_chk('sc_copy', @() {z.FontWeight, t.StyleConfigurations.Style(1).FontWeight});
u9b_chk('sc_add_array', @() oi_ran(@() addStyle(t, a, 'row', 2)));
t2 = uitable(uf, 'Data', magic(3));
E = t2.StyleConfigurations;
u9b_chk('sc_empty', @() {size(E), class(E.Style), size(E.Style), class(E.TargetIndex)});

% --- item 60: a table's display order
t3 = uitable(uf, 'Data', magic(5), 'ColumnSortable', true);
u9b_chk('dd_isprop', @() [isprop(t3, 'DisplayRowOrder') isprop(t3, 'DisplayColumnOrder') isfield(get(t3), 'DisplayRowOrder')]);
u9b_chk('dd_order_default', @() {t3.DisplayRowOrder, t3.DisplayColumnOrder});
u9b_chk('dd_data_default', @() isequal(t3.DisplayData, t3.Data));
t3.Selection = [2 3]; drawnow; pause(0.5);
u9b_chk('dd_selection_default', @() t3.DisplaySelection);
t3.DisplayRowOrder = [5 4 3 2 1]; drawnow; pause(0.5);
u9b_chk('dd_rows_reversed', @() t3.DisplayData);
u9b_chk('dd_selection_rows', @() {t3.Selection, t3.DisplaySelection});
t3.DisplayColumnOrder = [5 4 3 2 1]; drawnow; pause(0.5);
u9b_chk('dd_both_reversed', @() t3.DisplayData);
u9b_chk('dd_selection_both', @() {t3.Selection, t3.DisplaySelection});
u9b_chk('dd_orders', @() {t3.DisplayRowOrder, t3.DisplayColumnOrder});
t3.DisplayRowOrder = [];
u9b_chk('dd_rows_back', @() t3.DisplayData);
t3.Data = magic(4);
u9b_chk('dd_new_data', @() {t3.DisplayRowOrder, t3.DisplayColumnOrder, isequal(t3.DisplayData, t3.Data)});
u9b_chk('dd_set_data', @() set(t3, 'DisplayData', magic(3)));
u9b_chk('dd_set_selection', @() set(t3, 'DisplaySelection', [1 1]));
u9b_chk('dd_set_extent', @() set(t3, 'Extent', [0 0 1 1]));
u9b_chk('dd_fcn_default', @() t3.DisplayDataChangedFcn);
TT = table([3; 1; 2], {'c'; 'a'; 'b'}, 'VariableNames', {'n', 's'});
t4 = uitable(uf, 'Data', TT);
t4.DisplayRowOrder = [2 3 1];
u9b_chk('dd_table_rows', @() {class(t4.DisplayData), t4.DisplayData.n', t4.DisplayData.s'});
t4.DisplayColumnOrder = [2 1];
u9b_chk('dd_table_columns', @() t4.DisplayData.Properties.VariableNames);
t5 = uitable(uf, 'Data', magic(3), 'Position', [20 20 400 150]);
u9b_chkdiv('table_extent', @() t5.Extent, 'ADR0230');
W = table(datetime(2024, 1, 1:3)', [1; 2; 3], 'VariableNames', {'when', 'n'});
t6 = uitable(uf, 'Data', W);
t6.Data.when(2) = datetime(2025, 5, 5);
u9b_chk('table_datetime_edit', @() {char(t6.Data.when(2)), t6.Data.when.Format});
u9b_chk('table_scroll_words', @() {oi_ran(@() scroll(t3, 'bottom')), oi_ran(@() scroll(t3, 'row', 2)), oi_ran(@() scroll(t3, 'cell', [2 2]))});
u9b_chk('table_scroll_bad_row', @() oi_ran(@() scroll(t3, 'row', 99)));

% --- item 47: scrolling
uf1 = uifigure('Visible', 'off', 'Position', [100 100 300 200]);
g = uigridlayout(uf1, [12 1]); g.RowHeight = repmat({40}, 1, 12); g.ColumnWidth = {'1x'};
for k = 1:12, uilabel(g, 'Text', sprintf('row %d', k)); end
drawnow; pause(0.5);
u9b_chk('g_off', @() g.ScrollableViewportLocation);
lastwarn('');
scroll(g, 'bottom');
[~, wid] = lastwarn;
u9b_chk('g_off_warning', @() wid);
g.Scrollable = 'on'; drawnow; pause(0.5);
u9b_chk('g_on', @() oi_loc(g));
u9b_chk('g_bottom', @() oi_scrolled(g, 'bottom'));
u9b_chk('g_top', @() oi_scrolled(g, 'top'));
u9b_chk('g_xy', @() oi_scrolled(g, 1, 100));
u9b_chk('g_pair', @() oi_scrolled(g, [1 200]));
u9b_chk('g_zero', @() oi_scrolled(g, 1, 0));
u9b_chk('g_negative', @() oi_scrolled(g, -5, -5));
u9b_chk('g_fraction', @() oi_scrolled(g, 1.5, 2.5));
u9b_chk('g_right', @() oi_scrolled(g, 'right'));
g.ScrollableViewportLocation = [1 50];
u9b_chk('g_written', @() oi_loc(g));
g.ScrollableViewportLocation = [1 99999];
u9b_chk('g_written_huge', @() oi_loc(g));
g.ScrollableViewportLocation = [0; 0];
u9b_chk('g_written_column', @() oi_loc(g));
g.ScrollableViewportLocation = int8([1 5]);
u9b_chk('g_written_int', @() {class(g.ScrollableViewportLocation), oi_loc(g)});
u9b_chk('g_written_text', @() set(g, 'ScrollableViewportLocation', 'a'));
u9b_chk('g_written_three', @() set(g, 'ScrollableViewportLocation', [1 2 3]));
u9b_chk('g_written_nan', @() set(g, 'ScrollableViewportLocation', [NaN 1]));
u9b_chk('g_middle', @() oi_ran(@() scroll(g, 'middle')));
u9b_chk('g_bottom_one', @() oi_ran(@() scroll(g, 'bottom', 1)));
u9b_chk('g_one', @() oi_ran(@() scroll(g, 1)));
u9b_chk('g_three', @() oi_ran(@() scroll(g, [1 2 3])));
u9b_chk('g_upper', @() oi_ran(@() scroll(g, 'BOTTOM')));
u9b_chk('g_string', @() oi_scrolled(g, "bottom"));
g.Scrollable = 'off'; drawnow; pause(0.5);
u9b_chk('g_off_again', @() oi_loc(g));
u9b_chk('g_names', @() [isprop(g, 'ScrollableViewportLocation') isfield(get(g), 'ScrollableViewportLocation') isfield(set(g), 'ScrollableViewportLocation')]);
uf2 = uifigure('Visible', 'off', 'Position', [100 100 400 300]);
p = uipanel(uf2, 'Position', [10 10 200 100], 'Scrollable', 'on', 'Title', 'P');
l1 = uilabel(p, 'Position', [10 -50 100 20], 'Text', 'below');
l2 = uilabel(p, 'Position', [300 10 100 20], 'Text', 'right');
l3 = uilabel(p, 'Position', [10 150 100 20], 'Text', 'above');
drawnow; pause(0.5);
u9b_chk('p_start_x', @() oi_locx(p));
u9b_chkdiv('p_start_y', @() oi_locy(p), 'ADR0230');
u9b_chk('p_right_x', @() oi_scrolledx(p, 'right'));
u9b_chk('p_left_x', @() oi_scrolledx(p, 'left'));
u9b_chk('p_bottom_x', @() oi_scrolledx(p, 'bottom'));
u9b_chkdiv('p_bottom_y', @() oi_locy(p), 'ADR0230');
u9b_chk('p_pair_x', @() oi_scrolledx(p, 50, 50));
u9b_chkdiv('p_pair_y', @() oi_locy(p), 'ADR0230');
delete(l1); delete(l3); l2.Position = [50 10 100 20]; drawnow; pause(0.5);
u9b_chk('p_fits', @() oi_loc(p));
p.Scrollable = 'off'; drawnow; pause(0.5);
u9b_chk('p_off', @() oi_loc(p));
tg = uitabgroup(uf2, 'Position', [120 120 150 60]); tb = uitab(tg, 'Scrollable', 'on'); uilabel(tb, 'Position', [200 10 80 20]); drawnow; pause(0.5);
u9b_chk('tab_right_x', @() oi_scrolledx(tb, 'right'));
u9b_chkdiv('tab_right_y', @() oi_locy(tb), 'ADR0230');
g2 = uigridlayout(uipanel(uf2, 'Position', [220 10 100 100]), [1 6], 'Scrollable', 'on'); g2.ColumnWidth = repmat({50}, 1, 6); g2.RowHeight = {30};
for k = 1:6, uilabel(g2); end
drawnow; pause(0.5);
u9b_chk('g2_right', @() oi_scrolled(g2, 'right'));
u9b_chk('g2_bottom', @() oi_scrolled(g2, 'bottom'));
g4 = uigridlayout(uipanel(uf2, 'Position', [290 220 100 70]), [4 4], 'Scrollable', 'on'); g4.RowHeight = repmat({40}, 1, 4); g4.ColumnWidth = repmat({60}, 1, 4);
for k = 1:16, uilabel(g4); end
drawnow; pause(0.5);
u9b_chk('g4_bottom', @() oi_scrolled(g4, 'bottom'));
u9b_chk('g4_bottom_right', @() oi_scrolled(g4, 'right'));
u9b_chk('g4_top_right', @() oi_scrolled(g4, 'top'));
u9b_chk('g4_pair', @() oi_scrolled(g4, 30, 30));
% A scrollable uifigure's location is not recorded: headless R2025b answers it differently from
% one run to the next (probe_47 read [1 1] at the start, one recording [1 137], the next [1 1]
% after scroll(uf, 'top') and [1 137] after scroll(uf, 10, 20)); OpenItemsBatch17Tests holds it.
uf3 = uifigure('Visible', 'off', 'Position', [100 100 300 200], 'Scrollable', 'on');
lf = uilabel(uf3, 'Position', [-50 -30 100 20], 'Text', 'far'); drawnow; pause(0.5);
u9b_chk('fig_negative', @() {oi_scrolled(uf3, 'bottom'), oi_scrolled(uf3, 'top')});
fc = figure('Visible', 'off');
u9b_chk('classic_scrollable', @() fc.Scrollable);
lastwarn('');
scroll(fc, 'top');
[~, wid] = lastwarn;
u9b_chk('classic_scroll_warning', @() wid);

function r = oi_ran(fn)
fn();
r = 'ran';
end

function r = oi_try(fn)
% What fn answers, or the identifier of the error it raises: one line either way.
try
    r = fn();
catch err
    r = err.identifier;
end
end

function s = oi_disp(v)
% What disp prints, without the links R2025b's console adds and with the multiplication sign as x.
s = regexprep(evalc('disp(v)'), '<a [^>]*>|</a>', '');
s = strrep(s, char(215), 'x');
% The offer of the rest is a link whose text differs with where evalc runs ("Show all properties"
% or "Use properties to show all properties"): one word for either.
s = regexprep(s, ' *(Use properties to show all properties|Show all properties)', '  FOOTER');
end

function v = oi_loc(c)
drawnow; pause(0.3);
v = c.ScrollableViewportLocation;
end

function x = oi_locx(c)
v = oi_loc(c);
x = v(1);
end

function y = oi_locy(c)
v = oi_loc(c);
y = v(2);
end

function v = oi_scrolled(c, varargin)
scroll(c, varargin{:});
v = oi_loc(c);
end

function x = oi_scrolledx(c, varargin)
v = oi_scrolled(c, varargin{:});
x = v(1);
end
