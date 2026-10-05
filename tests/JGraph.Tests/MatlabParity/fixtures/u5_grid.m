% record: -noFigureWindows
% U5 of the app-building plan (ADR 0202): uigridlayout in a uifigure that is never shown - the
% forms of the call, what its tracks take, where it puts each child as it arrives, the Layout
% options, and the rectangles it gives its children once its layout has settled. Sizes that come
% from measured text are held to a pixel; the grid's own arithmetic to a hundredth.
uf = uifigure('Visible', 'off', 'Position', [100 100 600 400]);
f = figure('Visible', 'off');
warm = uilabel(uf); drawnow; pause(0.5); delete(warm);

% --- the forms of the call ----------------------------------------------------------------------
u2_chk('form_parent', @() get(uigridlayout(uf), 'Type'));
u2_chk('form_size', @() u5_tracks(uigridlayout(uf, [3 4])));
u2_chk('form_size_1x1', @() u5_tracks(uigridlayout(uf, [1 1])));
u2_chk('form_size_scalar', @() u5_tracks(uigridlayout(uf, 3)));
u2_chk('form_size_three', @() u5_tracks(uigridlayout(uf, [1 2 3])));
u2_chk('form_size_zero', @() u5_tracks(uigridlayout(uf, [0 2])));
u2_chk('form_size_fraction', @() u5_tracks(uigridlayout(uf, [1.5 2])));
u2_chk('form_size_text', @() u5_tracks(uigridlayout(uf, 'a')));
u2_chk('form_size_column', @() u5_tracks(uigridlayout(uf, [2; 3])));
u2_chk('form_size_then_pairs', @() u5_tracks(uigridlayout(uf, [2 3], 'RowHeight', {10, 20})));
u2_chk('form_size_then_longer', @() u5_tracks(uigridlayout(uf, [2 3], 'RowHeight', {10, 20, 30})));
u2_chk('form_pairs', @() u5_tracks(uigridlayout(uf, 'ColumnWidth', {'fit', 5})));
u2_chk('form_named_parent', @() u5_tracks(uigridlayout('Parent', uf)));
u2_chk('form_odd', @() uigridlayout(uf, 'RowHeight'));
u2_chk('form_unknown', @() uigridlayout(uf, 'Bogus', 1));
u2_chk('form_not_a_handle', @() uigridlayout(5.5));
u2_chk('form_empty', @() uigridlayout([]));
u2_chk('form_in_label', @() uigridlayout(uilabel(uf)));
u2_chk('form_struct', @() uigridlayout(uf, struct('RowSpacing', 3)));
u2_chk('form_position', @() uigridlayout(uf, 'Position', [1 2 30 40]));
u2_chk('form_prefix', @() get(uigridlayout(uf, 'Ta', 't'), 'Tag'));
delete(allchild(uf));
u2_chk('form_in_panel', @() get(get(uigridlayout(uipanel(uf)), 'Parent'), 'Type'));
u2_chk('form_in_grid', @() get(get(uigridlayout(uigridlayout(uf)), 'Parent'), 'Type'));
u2_chk('form_in_group', @() get(get(uigridlayout(uibuttongroup(uf)), 'Parent'), 'Type'));
u2_chk('form_in_classic_figure', @() get(get(uigridlayout(f), 'Parent'), 'Type'));
u2_chk('form_in_classic_panel', @() get(get(uigridlayout(uipanel(f)), 'Parent'), 'Type'));
u2_chk('form_uicontrol_in_grid', @() uicontrol(uigridlayout(uf)));
u2_chk('form_axes_in_grid', @() get(axes(uigridlayout(uf)), 'Type'));
delete(allchild(uf)); delete(allchild(f));

% --- what the tracks take -----------------------------------------------------------------------
tracks = {{'1x', 'fit', 40, '2x'}, [30 40], ["fit" "1x"], {'1x'}, '1x', 'fit', 50, {}, [], {'0x'}, {'1.5x'}, {'x'}, {' 2x'}, {'2X'}, ...
    {'FIT'}, {0}, {-5}, {Inf}, {NaN}, {'bogus'}, {1, 'fit'; 2, 3}, {'1x'; '2x'}, {int8(5)}, {true}, {"2x"}, "3x", {'10'}, {'10px'}, ...
    {[1 2]}, {single(2.5)}, {'1e1x'}, {'-1x'}, {'.5x'}};
for k = 1:numel(tracks)
    u5_try(sprintf('rowheight_%02d', k), uigridlayout(uf), 'RowHeight', tracks{k});
    u5_try(sprintf('columnwidth_%02d', k), uigridlayout(uf), 'ColumnWidth', tracks{k});
    delete(allchild(uf));
end
paddings = {5, [1 2], [1 2 3 4], [1 2 3 4]', -1, [1 2 3 -4], 'a', {1, 2, 3, 4}, [], NaN(1, 4), [Inf 1 1 1], int8([1 2 3 4]), true, [1.5 2.5 3.5 4.5]};
for k = 1:numel(paddings)
    u5_try(sprintf('padding_%02d', k), uigridlayout(uf), 'Padding', paddings{k});
end
spacings = {5, 0, -1, 2.5, [1 2], 'a', [], NaN, Inf, int8(4), true};
for k = 1:numel(spacings)
    u5_try(sprintf('rowspacing_%02d', k), uigridlayout(uf), 'RowSpacing', spacings{k});
    u5_try(sprintf('columnspacing_%02d', k), uigridlayout(uf), 'ColumnSpacing', spacings{k});
end
delete(allchild(uf));

% --- where a child goes as it arrives -----------------------------------------------------------
g = uigridlayout(uf, [2 2]);
c = cell(1, 6);
for k = 1:6
    c{k} = uibutton(g, 'Text', sprintf('b%d', k));
    fprintf('CHK|auto_child_%d|%s %s|exact\n', k, u5_cell(c{k}), u5_tracks(g));
end
delete(c{2});
n = uibutton(g);
fprintf('CHK|auto_after_delete|%s|exact\n', u5_cell(n));
c{1}.Layout.Row = 2; c{1}.Layout.Column = 2;
n2 = uibutton(g);
fprintf('CHK|auto_after_move|%s %s|exact\n', u5_cell(n2), u5_tracks(g));
delete(g);
g = uigridlayout(uf, [2 2]);
a = uibutton(g); a.Layout.Row = [1 2]; a.Layout.Column = [1 2];
b = uibutton(g);
fprintf('CHK|auto_after_span|%s %s|exact\n', u5_cell(b), u5_tracks(g));
delete(g);
g = uigridlayout(uf, [1 3]);
a = uibutton(g); a.Layout.Column = 3;
b = uibutton(g);
fprintf('CHK|auto_after_last_column|%s %s|exact\n', u5_cell(b), u5_tracks(g));
delete(g);
g = uigridlayout(uf, [2 2]);
p = uipanel(g); ax = uiaxes(g); gg = uigridlayout(g); bg = uibuttongroup(g);
fprintf('CHK|auto_containers|%s / %s / %s / %s|exact\n', u5_cell(p), u5_cell(ax), u5_cell(gg), u5_cell(bg));
delete(g);

% --- the Layout options -------------------------------------------------------------------------
g = uigridlayout(uf, [3 3]);
b = uibutton(g);
fprintf('CHK|layout_fields|%s|exact\n', strjoin(sort(fieldnames(b.Layout))', ' '));
spans = {2, [1 2], [2 3], [2 1], [1 3], 0, -1, 1.5, 5, [1 5], [1 2 3], 'a', [], {1}, true, int8(2), NaN, Inf, [2 2], single(3), "2", [0 1]};
for k = 1:numel(spans)
    u2_chk(sprintf('layout_row_%02d', k), @() u5_setrow(b, spans{k}));
    fprintf('CHK|layout_row_%02d_tracks|%s|exact\n', k, u5_tracks(g));
    g.RowHeight = {'1x', '1x', '1x'};
    u2_chk(sprintf('layout_column_%02d', k), @() u5_setcolumn(b, spans{k}));
    g.ColumnWidth = {'1x', '1x', '1x'};
end
u2_err('layout_struct', @() set(b, 'Layout', struct('Row', 1, 'Column', 1)));
u2_err('layout_empty', @() set(b, 'Layout', []));
u2_err('layout_number', @() set(b, 'Layout', 5));
lone = uibutton(uf);
fprintf('CHK|layout_outside_a_grid|%d|exact\n', isempty(lone.Layout));
lone.Parent = g;
fprintf('CHK|layout_moved_in|%s|exact\n', u5_cell(lone));
lone.Parent = uf;
fprintf('CHK|layout_moved_out|%d %s|exact\n', isempty(lone.Layout), mat2str(lone.Position));
b.Layout.Row = 3; b.Layout.Column = 3;
g2 = uigridlayout(uipanel(uf), [1 1]);
b.Parent = g2;
fprintf('CHK|layout_moved_to_another_grid|%s %s|exact\n', u5_cell(b), u5_tracks(g2));
delete(allchild(uf));
g = uigridlayout(uf, [3 3]);
b = uibutton(g); b.Layout.Row = 3; b.Layout.Column = 3;
g.RowHeight = {'1x'}; g.ColumnWidth = {'1x', '1x'};
fprintf('CHK|tracks_cut_under_a_child|%s %s|exact\n', u5_cell(b), u5_tracks(g));
delete(g);

% --- where everything lands, once the layout has settled ----------------------------------------
g = uigridlayout(uf);
u5_settle(uilabel(g), [100 100 31 22]);
u5_rect('grid_position', g.Position, 0.01);
u5_rect('grid_inner', g.InnerPosition, 0.01);
u5_rect('grid_outer', g.OuterPosition, 0.01);
u2_err('grid_position_set', @() set(g, 'Position', [10 10 50 50]));
delete(g);

g = uigridlayout(uf, [3 3], 'RowHeight', {'1x', '2x', 50}, 'ColumnWidth', {100, '1x', 'fit'});
h = cell(1, 9);
for k = 1:9, h{k} = uibutton(g, 'Text', sprintf('button %d', k)); end
u5_settle(h{9}, [100 100 100 22]);
for k = 1:9, u5_rect(sprintf('mixed_%d', k), h{k}.Position, 1); end
h{1}.Layout.Column = [1 3]; delete(h{2}); delete(h{3});
was = h{4}.Position; h{4}.Layout.Row = [2 3]; delete(h{7}); u5_settle(h{4}, was);
u5_rect('span_columns', h{1}.Position, 1);
u5_rect('span_rows', h{4}.Position, 1);
h{9}.Visible = 'off'; drawnow; pause(0.5); drawnow;
u5_rect('fit_column_child_hidden', h{6}.Position, 1);
was = h{5}.Position; delete(h{9}); delete(h{6}); u5_settle(h{5}, was);
u5_rect('fit_column_empty', h{5}.Position, 0.01);
delete(g);

g = uigridlayout(uf, [2 2], 'RowHeight', {300, 300}, 'ColumnWidth', {400, '1x'});
h = cell(1, 4); for k = 1:4, h{k} = uibutton(g); end
u5_settle(h{4}, [100 100 100 22]);
for k = 1:4, u5_rect(sprintf('overflow_%d', k), h{k}.Position, 0.01); end
delete(g);

g = uigridlayout(uf, [2 1], 'RowHeight', {'fit', '1x'});
a = uibutton(g); b = uilabel(g, 'Text', {'one', 'two', 'three'}); b.Layout.Row = 1;
c2 = uibutton(g); c2.Layout.Row = 2;
u5_settle(c2, [100 100 100 22]);
u5_rect('two_in_a_fit_cell_button', a.Position, 0.1);
u5_rect('two_in_a_fit_cell_label', b.Position, 0.1);
u5_rect('two_in_a_fit_cell_below', c2.Position, 0.1);
delete(g);

g = uigridlayout(uf, [3 1], 'RowHeight', {'fit', 'fit', '1x'});
a = uilistbox(g); a.Layout.Row = [1 2];
c2 = uibutton(g); c2.Layout.Row = 3;
u5_settle(c2, [100 100 100 22]);
u5_rect('span_two_fit_rows_list', a.Position, 0.1);
u5_rect('span_two_fit_rows_below', c2.Position, 0.1);
delete(g);

g = uigridlayout(uf, [2 1], 'RowHeight', {'fit', '1x'}, 'ColumnWidth', {'fit'});
inner = uigridlayout(g, [1 2], 'ColumnWidth', {'fit', 'fit'}, 'RowHeight', {'fit'});
x = uilabel(inner, 'Text', 'Name'); y = uieditfield(inner);
z = uibutton(g);
u5_settle(z, [100 100 100 22]);
u5_rect('nested_inner', inner.Position, 0.1);
u5_rect('nested_label', x.Position, 0.1);
u5_rect('nested_field', y.Position, 0.1);
u5_rect('nested_below', z.Position, 0.1);
delete(g);

g = uigridlayout(uf, [1 2], 'ColumnWidth', {'fit', '1x'});
p = uipanel(g, 'Title', 'Design');
pg = uigridlayout(p, [2 1], 'RowHeight', {'fit', 'fit'});
x = uilabel(pg, 'Text', 'Type'); y = uidropdown(pg);
z = uibutton(g);
u5_settle(z, [100 100 100 22]);
u5_rect('panel_in_fit_column', p.Position, 1);
u5_rect('panel_grid', pg.Position, 1);
u5_rect('panel_label', x.Position, 1);
u5_rect('panel_dropdown', y.Position, 1);
u5_rect('panel_right', z.Position, 1);
delete(g);

g = uigridlayout(uf, [1 2]);
ax = uiaxes(g); z = uibutton(g);
u5_settle(z, [100 100 100 22]); pause(1); drawnow;
u5_rect('uiaxes_in_grid', ax.Position, 0.01);
fprintf('CHK|uiaxes_in_grid_units|%s|exact\n', ax.Units);
delete(g);

p = uipanel(uf, 'Position', [20 20 300 200], 'Title', 'T');
g = uigridlayout(p, [1 1]); b = uibutton(g); u5_settle(b, [100 100 100 22]);
u5_rect('grid_in_titled_panel', g.Position, 0.1);
u5_rect('grid_in_titled_panel_button', b.Position, 0.1);
delete(p);

g = uigridlayout(uf, [1 1]); b = uibutton(g); u5_settle(b, [100 100 100 22]);
u5_try('child_position_set', b, 'Position', [5 5 50 50]);
u5_try('child_innerposition_set', b, 'InnerPosition', [5 5 50 50]);
delete(g);

% --- the sizes things ask for in a 'fit' track --------------------------------------------------
g = uigridlayout(uf, [1 1], 'RowHeight', {'fit'}, 'ColumnWidth', {'fit'});
texts = {'a', 'W', 'Label', 'iiii', 'MMMM', 'The quick brown fox', '0123456789', ' ', ''};
for k = 1:numel(texts)
    for fs = [12 20]
        for bold = 0:1
            l = uilabel(g, 'Text', texts{k}, 'FontSize', fs);
            if bold, l.FontWeight = 'bold'; end
            r = u5_settle(l, [100 100 31 22]);
            fprintf('CHK|fit_label_%d_%d_%d_w|%.6f|abs=0.05\n', k, fs, bold, r(3));
            fprintf('CHK|fit_label_%d_%d_%d_h|%.6f|abs=0.05\n', k, fs, bold, r(4));
            delete(l);
        end
    end
end
fits = {'Label', 'Button', 'StateButton', 'EditField', 'NumericEditField', 'TextArea', 'DropDown', 'ListBox', 'CheckBox', 'Slider', ...
    'RangeSlider', 'Spinner', 'Image', 'Hyperlink', 'GridLayout'};
for k = 1:numel(fits)
    for fs = [12 20]
        c2 = u5_make(fits{k}, g);
        was = c2.Position;
        if isprop(c2, 'FontSize'), c2.FontSize = fs; end
        r = u5_settle(c2, was);
        fprintf('CHK|fit_%s_%d_w|%.6f|abs=0.6\n', fits{k}, fs, r(3));
        fprintf('CHK|fit_%s_%d_h|%.6f|abs=0.6\n', fits{k}, fs, r(4));
        delete(c2);
    end
end
long = {
    'long_button',       @() uibutton(g, 'Text', 'A much longer button text')
    'long_checkbox',     @() uicheckbox(g, 'Text', 'A much longer check box')
    'long_dropdown',     @() uidropdown(g, 'Items', {'Short', 'A much longer item text'})
    'long_listbox',      @() uilistbox(g, 'Items', {'A much longer item text', 'b'})
    'two_line_label',    @() uilabel(g, 'Text', {'two', 'lines'})
    'two_line_button',   @() uibutton(g, 'Text', {'two', 'lines'})
    'long_hyperlink',    @() uihyperlink(g, 'Text', 'A much longer link text')
    'long_state_button', @() uibutton(g, 'state', 'Text', 'Longer state button')
    };
for k = 1:size(long, 1)
    c2 = long{k, 2}();
    r = u5_settle(c2, c2.Position);
    fprintf('CHK|fit_%s_w|%.6f|abs=0.6\n', long{k, 1}, r(3));
    fprintf('CHK|fit_%s_h|%.6f|abs=0.6\n', long{k, 1}, r(4));
    delete(c2);
end
delete(uf); delete(f);
