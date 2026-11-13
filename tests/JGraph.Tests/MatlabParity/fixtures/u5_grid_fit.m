% record: -noFigureWindows
% Open item 153 (ADR 0347): a uigridlayout's arithmetic as R2025b's own grid code does it, in a
% uifigure that is never shown - a 0-pixel track that is not weighted gives up its spacing, a child
% spanning 'fit' tracks fills the ones no child sits in alone, largest child first, a grid's own fit
% size takes a weighted track's content times its weight over the smallest, and a scrollable grid
% scrolls only to its last occupied row and column. Fixed tracks are held to a hundredth; rectangles
% that come from measured text to a pixel or two.
uf = uifigure('Visible', 'off', 'Position', [100 100 600 400]);
warm = uilabel(uf); drawnow; pause(0.5); delete(warm);
z4 = [0 0 0 0];
start = [100 100 100 22];

% --- a 0-pixel track that is not weighted -------------------------------------------------------
g = uigridlayout(uf, [1 4], 'ColumnWidth', {10, 0, 0, 10}, 'Padding', z4);
a = uibutton(g); a.Layout.Column = 1; b = uibutton(g); b.Layout.Column = 4;
u5_settle(b, start); u5_settle(a, start);
u5_rect('zero_middle_columns', b.Position, 0.01);
delete(g);
g = uigridlayout(uf, [4 1], 'RowHeight', {10, 0, 0, 10}, 'Padding', z4);
a = uibutton(g); a.Layout.Row = 1; b = uibutton(g); b.Layout.Row = 4;
u5_settle(b, start); u5_settle(a, start);
u5_rect('zero_middle_rows', b.Position, 0.01);
delete(g);
g = uigridlayout(uf, [1 3], 'ColumnWidth', {10, '0x', '1x'}, 'Padding', z4);
a = uibutton(g); a.Layout.Column = 1; b = uibutton(g); b.Layout.Column = 3;
u5_settle(b, start);
u5_rect('zero_weight', b.Position, 0.01);
delete(g);
g = uigridlayout(uf, [1 3], 'ColumnWidth', {10, 0, '1x'}, 'Padding', z4);
a = uibutton(g); a.Layout.Column = 2; b = uibutton(g); b.Layout.Column = 3;
u5_settle(b, start); u5_settle(a, start);
u5_rect('zero_fixed_in', a.Position, 0.01);
u5_rect('zero_fixed_after', b.Position, 0.01);
delete(g);
g = uigridlayout(uf, [1 3], 'ColumnWidth', {'1x', 10, 0}, 'Padding', z4);
a = uibutton(g); a.Layout.Column = 1; b = uibutton(g); b.Layout.Column = 2; c = uibutton(g); c.Layout.Column = 3;
u5_settle(c, start); u5_settle(a, start); u5_settle(b, start);
u5_rect('zero_last_first', a.Position, 0.01);
u5_rect('zero_last_second', b.Position, 0.01);
u5_rect('zero_last_in', c.Position, 0.01);
delete(g);
g = uigridlayout(uf, [1 3], 'ColumnWidth', {'1x', 10, 0}, 'Padding', z4);
a = uibutton(g); a.Layout.Column = [1 3];
u5_settle(a, start);
u5_rect('zero_last_spanned', a.Position, 0.01);
delete(g);
g = uigridlayout(uf, [3 1], 'RowHeight', {20, 'fit', 20});
a = uibutton(g); a.Layout.Row = 1; b = uibutton(g); b.Layout.Row = 3;
u5_settle(b, start);
u5_rect('empty_fit_between', b.Position, 0.01);
delete(g);

% --- a child spanning 'fit' tracks --------------------------------------------------------------
g = uigridlayout(uf, [4 1], 'RowHeight', {'fit', 'fit', 'fit', '1x'});
a = uilabel(g, 'Text', 'a'); a.Layout.Row = 1;
l = uilistbox(g); l.Layout.Row = [1 3];
c = uibutton(g); c.Layout.Row = 4;
u5_settle(c, start); u5_settle(a, start); u5_settle(l, start);
u5_rect('span_alone_label', a.Position, 1);
u5_rect('span_alone_list', l.Position, 1);
u5_rect('span_alone_below', c.Position, 1);
delete(g);
g = uigridlayout(uf, [4 1], 'RowHeight', {'fit', 'fit', 'fit', '1x'});
t = uitextarea(g); t.Layout.Row = [2 3];
l = uilistbox(g); l.Layout.Row = [1 2];
c = uibutton(g); c.Layout.Row = 4;
u5_settle(c, start); u5_settle(t, start); u5_settle(l, start);
u5_rect('span_two_list', l.Position, 1);
u5_rect('span_two_text', t.Position, 1);
u5_rect('span_two_below', c.Position, 1);
delete(g);
g = uigridlayout(uf, [3 1], 'RowHeight', {'fit', 30, 'fit'});
l = uilistbox(g); l.Layout.Row = [1 3];
u5_settle(l, start);
u5_rect('span_over_fixed', l.Position, 1);
delete(g);

% --- a grid's own fit size ----------------------------------------------------------------------
g = uigridlayout(uf, [2 2], 'RowHeight', {'fit', '1x'}, 'ColumnWidth', {'fit', '1x'});
in = uigridlayout(g, [2 2], 'RowHeight', {'1x', '2x'}, 'ColumnWidth', {'0.5x', '1x'});
in.Layout.Row = 1; in.Layout.Column = 1;
x = uibutton(in); x.Layout.Row = 1; x.Layout.Column = 1;
y = uibutton(in); y.Layout.Row = 2; y.Layout.Column = 2;
u5_settle(y, start); u5_settle(x, start);
u5_rect('weighted_grid', in.Position, 2);
u5_rect('weighted_first', x.Position, 2);
u5_rect('weighted_second', y.Position, 2);
delete(g);
g = uigridlayout(uf, [2 2], 'RowHeight', {'fit', '1x'}, 'ColumnWidth', {'fit', '1x'});
in = uigridlayout(g, [2 1], 'RowHeight', {'0x', '1x'}, 'ColumnWidth', {'1x'});
in.Layout.Row = 1; in.Layout.Column = 1;
y = uibutton(in); y.Layout.Row = 2;
u5_settle(y, start);
u5_rect('zero_weight_grid', in.Position, 1);
delete(g);

% --- a scrollable grid --------------------------------------------------------------------------
p = uipanel(uf, 'Position', [10 10 200 150], 'BorderType', 'none');
g = uigridlayout(p, [5 1], 'RowHeight', {40, 40, 40, 40, 40}, 'Scrollable', 'on');
b = uibutton(g); b.Layout.Row = 2;
u5_settle(b, start);
u5_rect('scroll_second_row', b.Position, 0.01);
scroll(g, 'bottom'); drawnow; pause(0.5);
fprintf('CHK|scroll_second_row_bottom|%s|exact\n', mat2str(g.ScrollableViewportLocation));
was = b.Position; b.Layout.Row = 5; u5_settle(b, was);
u5_rect('scroll_fifth_row', b.Position, 0.01);
scroll(g, 'bottom'); drawnow; pause(0.5);
fprintf('CHK|scroll_fifth_row_bottom|%s|exact\n', mat2str(g.ScrollableViewportLocation));
delete(p);
p = uipanel(uf, 'Position', [10 10 200 150], 'BorderType', 'none');
g = uigridlayout(p, [1 5], 'ColumnWidth', {60, 60, 60, 60, 60}, 'RowHeight', {'1x'}, 'Scrollable', 'on');
b = uibutton(g); b.Layout.Column = 3;
u5_settle(b, start);
u5_rect('scroll_third_column', b.Position, 0.01);
scroll(g, 'right'); drawnow; pause(0.5);
fprintf('CHK|scroll_third_column_right|%s|exact\n', mat2str(g.ScrollableViewportLocation));
delete(p);
delete(uf);
